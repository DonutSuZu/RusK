using System;
using System.Collections.Generic;
using System.Linq;
using RusK.API;
using UnityEngine;

namespace RusK.Mods.Op2;

/// <summary>
/// 調査用 (Party Lab、エンドフィールド風の戦闘の試作): 特殊攻撃と必殺技 (追加攻撃) のキー。
///   - 共有 EP: パーティで 1 本 (最大 300)。パーティの誰かの攻撃が 1 ヒットするたびに +1 (仮。自然回復はしない)
///   - 1〜3 の短押し: その番号のキャラが、操作を移さずにその場で特殊攻撃 (EP を 100 使う)
///   - 1〜3 の長押し (0.4 秒): そのキャラの必殺技ゲージ (キャラごと、1 ヒット 1P、最大 50) が満タンなら追加攻撃
/// 番号はパーティの並び (Party の HUD と同じ順)。操作していないキャラは「今のプレイヤー」を差し替えて撃たせる
/// </summary>
internal static class FieldSkills
{
    public const float MaxEp = 300f;
    public const float EpCost = 100f;
    public const float EpPerHit = 1f;
    public const int SlotCount = 3; // 3 人編成 (1〜3)
    public const int UltMax = 50;
    public const float LongPress = 0.4f;

    public static bool Enabled => true;

    public static float Ep = 100f;
    private static readonly Dictionary<IntPtr, int> Ult = new();

    private static readonly float[] PressedAt = { -1f, -1f, -1f };
    private static readonly bool[] LongFired = new bool[SlotCount];
    private static readonly KeyCode[] Keys = { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3 };
    private static readonly KeyCode[] SwitchKeys = { KeyCode.F1, KeyCode.F2, KeyCode.F3 };

    public static int UltOf(PlayerController p) => p != null && Ult.TryGetValue(p.Pointer, out var v) ? v : 0;

    /// <summary>キャラの攻撃が敵に当たった (必殺技ゲージ +1)</summary>
    public static void OnHit(PlayerController p)
    {
        if (!Enabled || p == null) return;
        Ult[p.Pointer] = Math.Min(UltMax, UltOf(p) + 1);
        Ep = Mathf.Min(MaxEp, Ep + EpPerHit);
    }

    /// <summary>パーティの並び (操作中のキャラと置いたキャラ。番号 1〜4)</summary>
    private static List<PlayerController> Slots() => P.Members.Where(m => m != null).Take(SlotCount).ToList();

    public static void Tick()
    {
        if (!Enabled) return;

        var slots = Slots();

        // F1〜F3: その番号のキャラに、その場で操作を移す (C / Z に加えて)
        for (int i = 0; i < SlotCount && i < slots.Count; i++)
        {
            if (!RuskInput.WasPressed(new Hotkey(SwitchKeys[i]))) continue;
            var target = slots[i];
            if (target == null || target.Pointer == P.Current?.Pointer || !Field.IsFielded(target)) break;
            P.SwitchTo(target); // Party の切り替え (ジャスト切り替え・クールタイムつき。エンドフィールドスタイルではその場で)
            break;
        }

        for (int i = 0; i < SlotCount; i++)
        {
            var hotkey = new Hotkey(Keys[i]);
            if (RuskInput.WasPressed(hotkey))
            {
                PressedAt[i] = Time.unscaledTime;
                LongFired[i] = false;
            }
            if (PressedAt[i] < 0f) continue;

            bool held = RuskInput.IsHeld(Keys[i]);
            var p = i < slots.Count ? slots[i] : null;
            if (held)
            {
                if (!LongFired[i] && Time.unscaledTime - PressedAt[i] >= LongPress)
                {
                    LongFired[i] = true;
                    Ultimate(p, i + 1);
                }
                continue;
            }
            // 離した: 長押しで撃っていなければ短押し
            if (!LongFired[i]) Special(p, i + 1);
            PressedAt[i] = -1f;
        }
    }

    private static bool OnField(PlayerController p) =>
        p != null && p.gameObject.activeInHierarchy &&
        (p.Pointer == P.Current?.Pointer || Field.IsFielded(p));

    private static void Special(PlayerController p, int slot)
    {
        if (!OnField(p)) { Log($"{slot}: キャラがフィールドにいません"); return; }
        if (Ep < EpCost) { Log($"{slot}: {P.Name(p)} の特殊攻撃 → EP が足りない ({Ep:0}/{EpCost:0})"); return; }
        string before = Motion(p);
        Do(p, () =>
        {
            FaceTarget(p);
            p.SpecialAttack();
        });
        string after = Motion(p);
        if (after == before || after.IndexOf("SpecialAttack", StringComparison.OrdinalIgnoreCase) < 0)
        {
            // 必殺技ゲージ (ゲームのエネルギー) が足りないなどで受け付けられなければ、動作を直接
            var name = Field.FindMotion(p, "SpecialAttack", "QTE");
            if (name != null) Do(p, () => p.ChangeMotion(name, true, 0.05f, default));
            after = Motion(p);
        }
        if (after.IndexOf("SpecialAttack", StringComparison.OrdinalIgnoreCase) < 0)
        {
            Log($"{slot}: {P.Name(p)} の特殊攻撃を出せません (動作 '{before}' → '{after}')");
            return;
        }
        Ep -= EpCost;
        Log($"{slot}: {P.Name(p)} の特殊攻撃 '{after}' (EP 残り {Ep:0})");
    }

    private static void Ultimate(PlayerController p, int slot)
    {
        if (!OnField(p)) { Log($"{slot}: キャラがフィールドにいません"); return; }
        int ult = UltOf(p);
        if (ult < UltMax) { Log($"{slot}: {P.Name(p)} の必殺技 → ゲージが足りない ({ult}/{UltMax})"); return; }
        var name = Field.FindMotion(p, "NormalAttack_QTE");
        if (name == null) { Log($"{slot}: {P.Name(p)} に追加攻撃の動作がありません"); return; }
        Do(p, () =>
        {
            FaceTarget(p);
            try { p.SetQTEListen(false); } catch { }
            p.ChangeMotion(name, true, 0.05f, default);
        });
        Ult[p.Pointer] = 0;
        Log($"{slot}: {P.Name(p)} の必殺技 (追加攻撃) '{Motion(p)}'");
    }

    /// <summary>操作中のキャラはそのまま、置いたキャラは「今のプレイヤー」を差し替えて</summary>
    private static void Do(PlayerController p, Action action)
    {
        try
        {
            if (p.Pointer == P.Current?.Pointer) action();
            else FieldSwap.Run(p, action);
        }
        catch (Exception e) { Log($"{P.Name(p)} でエラー: {e.Message}"); }
    }

    private static void FaceTarget(PlayerController p)
    {
        var t = FieldAi.PlayerTarget;
        if (t == null) return;
        try
        {
            var look = t.transform.position - p.transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f) p.transform.rotation = Quaternion.LookRotation(look);
        }
        catch { }
    }

    private static string Motion(PlayerController p)
    {
        try { return p.GetCurMotion()?.name ?? ""; }
        catch { return ""; }
    }

    private static void Log(string s) => P.Log?.Info("Op.2 スキル: " + s);

    // ------------------------------------------------------------------ HUD
    // エンドフィールドのレイアウトを参考に、図形とこのゲームの顔アイコンで描く (あちらの画像は使わない)
    //   左下: パーティの顔 (操作中は大きく縁が光る、上に印)。下に HP、右上に必殺技ゲージの丸。上に「切り替え」の案内
    //   右下: スキルボタン 1〜3 (丸い顔、下にキー)。EP が足りれば下側が青緑に満ちる。上に必殺技ゲージの輪 (満タンで光る)
    //   右下の上: 共有 EP (100 ごとの 3 区切り)

    private static readonly Color Teal = new(0.18f, 0.78f, 0.82f, 1f);
    private static readonly Color TealDim = new(0.18f, 0.78f, 0.82f, 0.25f);
    private static readonly Color UltCol = new(0.75f, 0.95f, 0.25f, 1f);
    private static readonly Color Panel = new(0.05f, 0.06f, 0.08f, 0.72f);
    private static readonly Color Ring = new(1f, 1f, 1f, 0.85f);

    public static bool Active => Enabled && Field.Fielded.Count > 0;

    public static void DrawHud()
    {
        if (!Active || !Render.IsRepaint) return;
        float s = Mathf.Max(0.6f, Screen.height / 1080f);
        var slots = Slots();
        var cur = P.Current;
        DrawParty(slots, cur, s);
        DrawSkills(slots, s);
    }

    private static void DrawParty(List<PlayerController> slots, PlayerController cur, float s)
    {
        float x = 40f * s, baseY = Screen.height - 340f * s; // ゲームの HP 表示 (左下) より上
        float small = 64f * s, big = 78f * s, gap = 18f * s;

        // 切り替えの案内
        string keys = $"{P.NextKey.Display} / {P.PrevKey.Display}  F1〜F{SlotCount}";
        Render.Rect(x, baseY - 40f * s, 22f * s, 22f * s, Panel, 4f * s);
        Render.Text(x, baseY - 40f * s, 22f * s, 22f * s, "⇄", Color.white, Mathf.RoundToInt(13f * s), TextAnchor.MiddleCenter, true);
        Render.Text(x + 28f * s, baseY - 40f * s, 260f * s, 22f * s, L.T("切り替え ({0})", keys), Color.white,
            Mathf.RoundToInt(14f * s), TextAnchor.MiddleLeft, true, true);

        for (int i = 0; i < slots.Count; i++)
        {
            var p = slots[i];
            bool on = cur != null && p.Pointer == cur.Pointer;
            float size = on ? big : small;
            float cx = x + size * 0.5f, cy = baseY + big * 0.5f + (on ? 0f : (big - small) * 0.5f);
            float r = size * 0.5f;

            // 縁 → 顔 (丸)
            if (on)
            {
                RingArc(cx, cy, r + 6f * s, 2.5f * s, 1f, Ring);
                Render.Text(cx - 10f * s, cy - r - 26f * s, 20f * s, 16f * s, "▼", Ring, Mathf.RoundToInt(12f * s), TextAnchor.MiddleCenter, true);
            }
            Render.Rect(cx - r, cy - r, size, size, new Color(0.1f, 0.1f, 0.12f, 0.9f), r);
            var tex = P.Portrait(p);
            float dim = on ? 1f : 0.8f;
            if (tex != null)
                GUI.DrawTexture(new Rect(cx - r, cy - r, size, size), tex, ScaleMode.StretchToFill, true, 0f,
                    new Color(dim, dim, dim, 1f), 0f, r);

            // 番号
            Render.Text(cx - r, cy + r - 18f * s, 26f * s, 16f * s, $"F{i + 1}", Color.white, Mathf.RoundToInt(11f * s), TextAnchor.MiddleLeft, true, true);

            // 必殺技ゲージの丸 (右上)
            float ux = cx + r - 2f * s, uy = cy - r + 8f * s, ur = 9f * s;
            float ult = UltOf(p) / (float)UltMax;
            Render.Rect(ux - ur, uy - ur, ur * 2f, ur * 2f, Panel, ur);
            RingArc(ux, uy, ur, 2.5f * s, ult, ult >= 1f ? UltCol : Teal);
            if (ult >= 1f) Render.Rect(ux - 4f * s, uy - 4f * s, 8f * s, 8f * s, UltCol, 4f * s);

            // HP
            float hp = 0f;
            try { hp = Mathf.Clamp01(p.GetCurHp() / Mathf.Max(1f, p.GetMaxHp())); } catch { }
            float bw = size, by = cy + r + 8f * s;
            Render.Rect(cx - r, by, bw, 5f * s, new Color(0f, 0f, 0f, 0.6f), 2f * s);
            Render.Rect(cx - r, by, bw * hp, 5f * s, hp > 0.3f ? Teal : new Color(0.95f, 0.3f, 0.3f), 2f * s);

            x += size + gap;
        }
    }

    private static void DrawSkills(List<PlayerController> slots, float s)
    {
        float btn = 72f * s, gap = 26f * s;
        float total = SlotCount * btn + (SlotCount - 1) * gap;
        float x = Screen.width - total - 60f * s, cy = Screen.height - 110f * s; // 右下 (エンドフィールドスタイルでは RusK UI のボタン HUD は隠れる)

        // 共有 EP (3 区切り)
        float ew = total, eh = 8f * s, ey = cy - btn * 0.5f - 86f * s;
        for (int k = 0; k < 3; k++)
        {
            float sx = x + k * (ew + 6f * s) / 3f, sw = ew / 3f - 4f * s;
            float fill = Mathf.Clamp01((Ep - k * EpCost) / EpCost);
            Render.Rect(sx, ey, sw, eh, new Color(0f, 0f, 0f, 0.6f), 3f * s);
            if (fill > 0f) Render.Rect(sx, ey, sw * fill, eh, fill >= 1f ? Teal : TealDim, 3f * s);
        }
        Render.Text(x, ey - 20f * s, ew, 18f * s, $"EP {Ep:0} / {MaxEp:0}", Color.white, Mathf.RoundToInt(12f * s), TextAnchor.MiddleRight, true, true);

        bool epReady = Ep >= EpCost;
        for (int i = 0; i < SlotCount; i++)
        {
            float cx = x + btn * 0.5f + i * (btn + gap);
            float r = btn * 0.5f;
            var p = i < slots.Count ? slots[i] : null;

            // 必殺技ゲージの輪 (ボタンの上)
            float ur = 13f * s, uy = cy - r - 30f * s;
            float ult = p == null ? 0f : UltOf(p) / (float)UltMax;
            Render.Rect(cx - ur, uy - ur, ur * 2f, ur * 2f, Panel, ur);
            RingArc(cx, uy, ur, 3f * s, ult, ult >= 1f ? UltCol : Teal);
            if (ult >= 1f)
            {
                Render.Rect(cx - ur + 4f * s, uy - ur + 4f * s, ur * 2f - 8f * s, ur * 2f - 8f * s, UltCol, ur);
                Render.Text(cx - 40f * s, uy - ur - 18f * s, 80f * s, 16f * s, L.T("長押し"), UltCol, Mathf.RoundToInt(11f * s), TextAnchor.MiddleCenter, true, true);
            }

            // ボタン: 暗い丸 → 顔 → EP が足りれば下側が青緑
            Render.Rect(cx - r - 3f * s, cy - r - 3f * s, btn + 6f * s, btn + 6f * s, new Color(1f, 1f, 1f, 0.25f), r + 3f * s);
            Render.Rect(cx - r, cy - r, btn, btn, new Color(0.08f, 0.09f, 0.11f, 0.92f), r);
            var tex = p == null ? null : P.Portrait(p);
            if (tex != null)
            {
                float dim = epReady ? 1f : 0.45f;
                GUI.DrawTexture(new Rect(cx - r, cy - r, btn, btn), tex, ScaleMode.StretchToFill, true, 0f, new Color(dim, dim, dim, 1f), 0f, r);
            }
            if (epReady)
            {
                // 下 3 割を青緑に (丸の下側を細い横帯で塗る)
                int bands = 10;
                for (int k = 0; k < bands; k++)
                {
                    float yy = cy + r * 0.35f + k * (r * 0.65f / bands);
                    float dy = yy + r * 0.65f / bands * 0.5f - cy;
                    float half = Mathf.Sqrt(Mathf.Max(0f, r * r - dy * dy));
                    Render.Rect(cx - half, yy, half * 2f, r * 0.65f / bands + 0.5f, new Color(Teal.r, Teal.g, Teal.b, 0.85f));
                }
            }

            // キー
            float kw = 22f * s, kh = 20f * s;
            Render.Rect(cx - kw * 0.5f, cy + r + 10f * s, kw, kh, Panel, 3f * s);
            Render.Text(cx - kw * 0.5f, cy + r + 10f * s, kw, kh, (i + 1).ToString(), Color.white, Mathf.RoundToInt(13f * s), TextAnchor.MiddleCenter, true);
        }
    }

    /// <summary>円弧 (上から時計回りに fraction の割合)。小さな丸を並べて描く</summary>
    private static void RingArc(float cx, float cy, float radius, float thickness, float fraction, Color color)
    {
        fraction = Mathf.Clamp01(fraction);
        if (fraction <= 0f) return;
        int n = Mathf.Max(12, Mathf.RoundToInt(radius * 1.6f));
        int count = Mathf.CeilToInt(n * fraction);
        for (int k = 0; k < count; k++)
        {
            float a = (k / (float)n) * Mathf.PI * 2f - Mathf.PI * 0.5f;
            float px = cx + Mathf.Cos(a) * radius, py = cy + Mathf.Sin(a) * radius;
            Render.Rect(px - thickness * 0.5f, py - thickness * 0.5f, thickness, thickness, color, thickness * 0.5f);
        }
    }
}
