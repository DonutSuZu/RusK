using System;
using System.Collections.Generic;
using System.Linq;
using RusK.API;
using UnityEngine;

namespace RusK.Mods.Party;

/// <summary>
/// 調査用 (Party Lab、エンドフィールド風の戦闘の試作): 特殊攻撃と必殺技 (追加攻撃) のキー。
///   - 共有 EP: パーティで 1 本 (最大 300)。仮に 1 秒に 10 ずつ自然に貯まる
///   - 1〜4 の短押し: その番号のキャラが、操作を移さずにその場で特殊攻撃 (EP を 100 使う)
///   - 1〜4 の長押し (0.4 秒): そのキャラの必殺技ゲージ (キャラごと、1 ヒット 1P、最大 50) が満タンなら追加攻撃
/// 番号はパーティの並び (Party の HUD と同じ順)。操作していないキャラは「今のプレイヤー」を差し替えて撃たせる
/// </summary>
internal static class FieldSkillProbe
{
    public const float MaxEp = 300f;
    public const float EpCost = 100f;
    public const float EpRegen = 10f;
    public const int UltMax = 50;
    public const float LongPress = 0.4f;

    public static bool Enabled => FieldAi.Enabled;

    public static float Ep = 100f;
    private static readonly Dictionary<IntPtr, int> Ult = new();

    private static readonly float[] PressedAt = { -1f, -1f, -1f, -1f };
    private static readonly bool[] LongFired = new bool[4];
    private static readonly KeyCode[] Keys = { KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4 };

    public static int UltOf(PlayerController p) => p != null && Ult.TryGetValue(p.Pointer, out var v) ? v : 0;

    /// <summary>キャラの攻撃が敵に当たった (必殺技ゲージ +1)</summary>
    public static void OnHit(PlayerController p)
    {
        if (!Enabled || p == null) return;
        Ult[p.Pointer] = Math.Min(UltMax, UltOf(p) + 1);
    }

    /// <summary>パーティの並び (操作中のキャラと置いたキャラ。番号 1〜4)</summary>
    private static List<PlayerController> Slots() => PartyManager.Members.Where(m => m != null).Take(4).ToList();

    public static void Tick()
    {
        if (!Enabled) return;
        Ep = Mathf.Min(MaxEp, Ep + EpRegen * Time.deltaTime);

        var slots = Slots();
        for (int i = 0; i < 4; i++)
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
        (p.Pointer == PartyManager.Current?.Pointer || FieldProbe.IsFielded(p));

    private static void Special(PlayerController p, int slot)
    {
        if (!OnField(p)) { Log($"{slot}: キャラがフィールドにいません"); return; }
        if (Ep < EpCost) { Log($"{slot}: {PartyManager.Name(p)} の特殊攻撃 → EP が足りない ({Ep:0}/{EpCost:0})"); return; }
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
            var name = FieldProbe.FindMotion(p, "SpecialAttack", "QTE");
            if (name != null) Do(p, () => p.ChangeMotion(name, true, 0.05f, default));
            after = Motion(p);
        }
        if (after.IndexOf("SpecialAttack", StringComparison.OrdinalIgnoreCase) < 0)
        {
            Log($"{slot}: {PartyManager.Name(p)} の特殊攻撃を出せません (動作 '{before}' → '{after}')");
            return;
        }
        Ep -= EpCost;
        Log($"{slot}: {PartyManager.Name(p)} の特殊攻撃 '{after}' (EP 残り {Ep:0})");
    }

    private static void Ultimate(PlayerController p, int slot)
    {
        if (!OnField(p)) { Log($"{slot}: キャラがフィールドにいません"); return; }
        int ult = UltOf(p);
        if (ult < UltMax) { Log($"{slot}: {PartyManager.Name(p)} の必殺技 → ゲージが足りない ({ult}/{UltMax})"); return; }
        var name = FieldProbe.FindMotion(p, "NormalAttack_QTE");
        if (name == null) { Log($"{slot}: {PartyManager.Name(p)} に追加攻撃の動作がありません"); return; }
        Do(p, () =>
        {
            FaceTarget(p);
            try { p.SetQTEListen(false); } catch { }
            p.ChangeMotion(name, true, 0.05f, default);
        });
        Ult[p.Pointer] = 0;
        Log($"{slot}: {PartyManager.Name(p)} の必殺技 (追加攻撃) '{Motion(p)}'");
    }

    /// <summary>操作中のキャラはそのまま、置いたキャラは「今のプレイヤー」を差し替えて</summary>
    private static void Do(PlayerController p, Action action)
    {
        try
        {
            if (p.Pointer == PartyManager.Current?.Pointer) action();
            else FieldSwap.Run(p, action);
        }
        catch (Exception e) { Log($"{PartyManager.Name(p)} でエラー: {e.Message}"); }
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

    private static void Log(string s) => PartyManager.Log?.Info("Party Lab スキル: " + s);

    /// <summary>試作の表示: 画面下に EP と各キャラの必殺技ゲージ</summary>
    public static void DrawHud()
    {
        if (!Enabled || !Render.IsRepaint || FieldProbe.Fielded.Count == 0) return;
        float s = Mathf.Max(0.6f, Screen.height / 1080f);
        float w = 520f * s, h = 22f * s;
        float x = (Screen.width - w) * 0.5f, y = Screen.height - 150f * s;
        Render.Rect(x, y, w, h, new Color(0f, 0f, 0f, 0.6f), 4f * s);
        Render.Rect(x, y, w * (Ep / MaxEp), h, new Color(1f, 0.82f, 0.3f, 0.9f), 4f * s);
        for (int k = 1; k < 3; k++) Render.Rect(x + w * k / 3f, y, 2f * s, h, new Color(0f, 0f, 0f, 0.7f));
        Render.Text(x, y, w, h, $"EP {Ep:0} / {MaxEp:0}", Color.white, Mathf.RoundToInt(13f * s), TextAnchor.MiddleCenter, true, true);

        var slots = Slots();
        float cw = w / 4f;
        for (int i = 0; i < slots.Count; i++)
        {
            var p = slots[i];
            int ult = UltOf(p);
            bool ready = ult >= UltMax;
            string text = $"{i + 1} {PartyManager.Name(p)}  必殺 {ult}/{UltMax}";
            Render.Text(x + cw * i, y + h + 2f * s, cw, 18f * s, text, ready ? new Color(1f, 0.85f, 0.3f) : Color.white,
                Mathf.RoundToInt(12f * s), TextAnchor.MiddleCenter, true, true);
        }
    }
}
