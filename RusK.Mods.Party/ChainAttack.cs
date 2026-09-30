using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RusK.API;
using UnityEngine;

namespace RusK.Mods.Party;

/// <summary>
/// 連携攻撃 (ゼンゼロのチェーン攻撃風)。
///
/// ポイント: 敵への 1 ヒット (プレイヤーの攻撃判定) で 1P。ステージ移動・連携の発動・30 秒攻撃なしでリセット。
/// きっかけ: 1500P たまっている (またはストックがある) 状態で、操作キャラの追加攻撃が敵に当たる
///           (敵のシールド割れでも発動させていたが、雑魚のシールドが柔らかすぎるのでやめた)。
/// 流れ: 時間をほぼ止めて 5 秒のゲージ → C / Z で次のキャラを選ぶ → 敵の前に出して追加攻撃 → 当たったら次の選択へ。
///       編成の人数だけ繋がる (最初の追加攻撃を含む)。敵が倒れる・追加攻撃が外れる・時間切れで終わる。
///       最初の選択で連携回避のキーを押すか時間切れにしたら、1 回分をストックする (最大 1)。連携中は攻撃を受けない。
///
/// 追加攻撃は、キャラの動作の一覧から "NormalAttack_QTE_*" を名前で探して直接その動作にする
/// (PlayerController.QTEAttack は今のコンボの続きの追加攻撃しか出せず、待機中や切り替え直後は何も出ない)
/// </summary>
internal static class ChainAttack
{
    public const int PointsNeeded = 1500;
    public const int PointsShown = 3000;
    private const float ResetAfter = 30f;

    public static bool Enabled = true;
    public static float ChooseTime = 5f;
    public static float SlowScale = 0.03f;
    public static float Distance = 1.6f;
    public static Hotkey NextKey = new(KeyCode.C);
    public static Hotkey PrevKey = new(KeyCode.Z);
    public static Hotkey SkipKey = new(KeyCode.X);

    public static int Points;
    public static int Stock;

    private enum State { Idle, Choosing, Attacking }

    private static State _state;
    public static bool Active => _state != State.Idle;

    private static EnemyController _target;
    private static readonly HashSet<IntPtr> Used = new();
    private static bool _anyFollowUp;
    private static float _lastHit = -999f;
    private static float? _savedScale;

    private static float _chooseStart;
    public static PlayerController LeftPick, RightPick; // 左 = 次へ (C)、右 = 前へ (Z)

    private static PlayerController _attacker;
    private static int _fireFrame;
    private static bool _fired;
    private static float _attackStart;
    private static bool _hit;

    public static float ChooseProgress => Mathf.Clamp01((Time.unscaledTime - _chooseStart) / ChooseTime);
    public static bool Choosing => _state == State.Choosing;

    public static void Reset()
    {
        if (Active) End("リセット");
        Points = 0;
    }

    // ------------------------------------------------------------------ 敵に攻撃が当たったとき

    private static bool _loggedFirstHit;
    private static readonly HashSet<AttackBoxType> LoggedTypes = new();
    private static float _lastQteLog = -999f;

    public static void OnEnemyHit(EnemyController enemy, AttackBoxType boxType)
    {
        if (!_loggedFirstHit)
        {
            _loggedFirstHit = true;
            PartyManager.Log?.Info("Party: 連携攻撃 敵へのヒットの検知 OK");
        }
        if (LoggedTypes.Add(boxType)) PartyManager.Log?.Info($"Party: 連携攻撃 ヒットの種類 {boxType} を検知");
        if (!Enabled || enemy == null) return;
        if (boxType != AttackBoxType.Player && boxType != AttackBoxType.PlayerCopy) return;
        var cur = PartyManager.Current;
        if (cur == null) return;
        _lastHit = Time.unscaledTime;

        if (_state == State.Attacking)
        {
            if (_attacker != null && cur.Pointer == _attacker.Pointer) _hit = true;
            return;
        }
        if (_state != State.Idle) return;

        Points++;

        bool alive = false;
        try { alive = enemy.IsAlive(); } catch { }
        // 追加攻撃の動作中か、その動作が終わった直後 (弾などが遅れて当たる) のヒットを追加攻撃のヒットとみなす
        bool qte = InQte(cur) || Time.unscaledTime <= _qteUntil;
        string motion = _qteMotion;
        if (!qte) return;

        bool byPoints = Points >= PointsNeeded;
        bool byStock = !byPoints && Stock > 0;
        int remaining = Remaining(cur).Count;
        bool field = PartyHud.OnField();
        string why = !alive ? "敵が倒れている" : !field ? "戦闘画面ではない" : remaining == 0 ? "交代できる仲間がいない"
            : !byPoints && !byStock ? "ポイントが足りない" : null;
        if (why != null || Time.unscaledTime - _lastQteLog > 1f)
        {
            _lastQteLog = Time.unscaledTime;
            PartyManager.Log?.Info($"Party: 連携攻撃 追加攻撃が命中 '{motion}' ポイント {Points} ストック {Stock} " +
                                   $"仲間 {remaining} 人 → {(why ?? "発動")}");
        }
        if (why != null) return;

        // ポイントで発動したらポイントを 0 に、ストックで発動したらストックを使う
        if (byPoints) Points = 0;
        else Stock = 0;
        _target = enemy;
        Used.Clear();
        Used.Add(cur.Pointer);
        _anyFollowUp = false;
        PartyManager.Log?.Info($"Party: 連携攻撃 開始 ({(byPoints ? "ポイント" : "ストック")}) {PartyManager.Name(cur)} → 選択へ");
        BeginChoose();
    }

    // ---- 追加攻撃の動作の見張り (毎フレーム)

    private static bool _inQte;
    private static float _qteUntil = -999f;
    private static string _qteMotion = "";

    private static bool InQte(PlayerController p)
    {
        try
        {
            if (p.IsCurMotionQTE()) return true;
            var n = p.GetCurMotion()?.name;
            return n != null && n.IndexOf("QTE", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        catch { return false; }
    }

    private static void WatchQte()
    {
        var cur = PartyManager.Current;
        bool now = cur != null && InQte(cur);
        if (now)
        {
            if (!_inQte)
            {
                try { _qteMotion = cur.GetCurMotion()?.name ?? ""; } catch { _qteMotion = ""; }
                PartyManager.Log?.Info($"Party: 連携攻撃 {PartyManager.Name(cur)} が追加攻撃の動作に入った '{_qteMotion}' (ポイント {Points} ストック {Stock})");
            }
            _qteUntil = Time.unscaledTime + 0.4f;
        }
        _inQte = now;
    }

    // ------------------------------------------------------------------ 毎フレーム

    public static void Tick()
    {
        // ステージ移動 (読み込み) でポイントをリセット
        bool loading = false;
        try
        {
            loading = (GameUtil.Instance?.GetInLoading() ?? false) || (SceneLoader.Instance?.InSceneLoading() ?? false);
        }
        catch { }
        if (loading || !PartyManager.InFight)
        {
            if (Active) End("ステージ移動");
            Points = 0;
            return;
        }

        if (!Enabled)
        {
            if (Active) End("無効にした");
            return;
        }
        WatchQte();

        switch (_state)
        {
            case State.Idle:
                if (Points > 0 && Time.unscaledTime - _lastHit > ResetAfter) Points = 0;
                break;
            case State.Choosing:
                TickChoose();
                break;
            case State.Attacking:
                TickAttack();
                break;
        }
    }

    // ---- 選択 (時間をほぼ止めてゲージを出す)

    private static List<PlayerController> Remaining(PlayerController cur)
    {
        PartyManager.Refresh();
        return PartyManager.Members.Where(m => m != null && m.Pointer != cur.Pointer && !Used.Contains(m.Pointer) &&
                                               !PartyManager.IsDown(m)).ToList();
    }

    private static void BeginChoose()
    {
        var cur = PartyManager.Current;
        if (cur == null || !Alive(_target))
        {
            End("敵が倒れた");
            return;
        }
        var left = Remaining(cur);
        if (left.Count == 0)
        {
            End("全員が攻撃した");
            return;
        }

        // 切り替えキーで回る順番で、左 (次へ) と右 (前へ) の候補を決める。1 人しかいなければ両方その人
        LeftPick = Walk(cur, +1, left);
        RightPick = Walk(cur, -1, left);
        _savedScale ??= Time.timeScale >= 0.5f ? Time.timeScale : 1f;
        Time.timeScale = SlowScale;
        _chooseStart = Time.unscaledTime;
        _state = State.Choosing;
    }

    private static PlayerController Walk(PlayerController from, int dir, List<PlayerController> candidates)
    {
        var members = PartyManager.Members.Where(m => m != null).ToList();
        int i = members.FindIndex(m => m.Pointer == from.Pointer);
        int n = members.Count;
        for (int k = 1; k < n; k++)
        {
            var m = members[((i + dir * k) % n + n) % n];
            if (candidates.Any(c => c.Pointer == m.Pointer)) return m;
        }
        return candidates[0];
    }

    private static void TickChoose()
    {
        if (!Alive(_target))
        {
            End("敵が倒れた");
            return;
        }
        if (Time.timeScale != SlowScale) Time.timeScale = SlowScale; // ゲームの演出で戻されても止めておく

        if (RuskInput.WasPressed(NextKey)) Choose(LeftPick);
        else if (RuskInput.WasPressed(PrevKey)) Choose(RightPick);
        else if (RuskInput.WasPressed(SkipKey)) Cancel("連携回避");
        else if (Time.unscaledTime - _chooseStart >= ChooseTime) Cancel("時間切れ");
    }

    /// <summary>選ばずに終える。まだ誰も繋いでいなければ 1 回分をストックする (最大 1)</summary>
    private static void Cancel(string reason)
    {
        if (!_anyFollowUp)
        {
            Stock = 1;
            PartyManager.Log?.Info($"Party: 連携攻撃 キャンセル ({reason}) → 1 回分ストック");
            Ctx?.Notify(L.T("連携攻撃をストックしました"), NotifyLevel.Info);
        }
        End(reason);
    }

    private static IModContext Ctx => PartyManager.Ctx;

    private static void Choose(PlayerController next)
    {
        if (next == null) return;
        var prev = PartyManager.Current;
        RestoreTime();
        if (!PartyManager.Switch(next, ignoreCooldown: true, force: true))
        {
            End("切り替えに失敗");
            return;
        }
        MoveToTarget(next, prev);
        Used.Add(next.Pointer);
        _attacker = next;
        _fired = false;
        _hit = false;
        _fireFrame = Time.frameCount + 2; // 切り替えの後処理 (1 フレーム後に待機へ戻す) の後に撃つ
        _anyFollowUp = true;
        _state = State.Attacking;
    }

    /// <summary>出てくるキャラを敵の目の前 (前のキャラがいた側) に、敵の方を向けて置く</summary>
    private static void MoveToTarget(PlayerController p, PlayerController from)
    {
        try
        {
            var enemyPos = _target.transform.position;
            var fromPos = from != null ? from.transform.position : p.transform.position;
            var dir = fromPos - enemyPos;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f)
            {
                dir = _target.transform.forward;
                dir.y = 0f;
            }
            dir.Normalize();
            var pos = enemyPos + dir * Distance;
            pos.y = fromPos.y;
            var look = enemyPos - pos;
            look.y = 0f;
            var rot = look.sqrMagnitude > 0.001f ? Quaternion.LookRotation(look) : p.transform.rotation;

            var cc = p.GetComponent<CharacterController>();
            bool had = cc != null && cc.enabled;
            if (had) cc.enabled = false;
            p.transform.SetPositionAndRotation(pos, rot);
            if (had) cc.enabled = true;
        }
        catch (Exception e)
        {
            PartyManager.Log?.Warning($"Party: 連携攻撃 敵の前に移動できません: {e.Message}");
        }
    }

    // ---- 追加攻撃

    private static void TickAttack()
    {
        var p = _attacker;
        if (p == null || PartyManager.Current?.Pointer != p.Pointer)
        {
            End("攻撃するキャラがいない");
            return;
        }

        if (!_fired)
        {
            if (Time.frameCount < _fireFrame) return;
            _fired = true;
            _attackStart = Time.unscaledTime;
            var name = QteProbe.FindQteMotion(p);
            if (name == null)
            {
                End("追加攻撃の動作がない");
                return;
            }
            try
            {
                try { p.SetQTEListen(false); } catch { }
                p.ChangeMotion(name, true, 0.05f, default);
                PartyManager.Log?.Info($"Party: 連携攻撃 {PartyManager.Name(p)} の追加攻撃 '{name}'");
            }
            catch (Exception e)
            {
                PartyManager.Log?.Warning($"Party: 連携攻撃 追加攻撃を出せません: {e.Message}");
                End("追加攻撃を出せない");
            }
            return;
        }

        if (!Alive(_target))
        {
            End("敵が倒れた");
            return;
        }
        if (_hit)
        {
            BeginChoose(); // 当たったら次の選択へ (残りがいなければ終わり)
            return;
        }

        float t = Time.unscaledTime - _attackStart;
        bool inQte = false;
        try { inQte = p.IsCurMotionQTE(); } catch { }
        if ((t > 0.3f && !inQte) || t > 3f) End("追加攻撃が当たらなかった");
    }

    // ---- 終わり

    private static void End(string reason)
    {
        if (_state != State.Idle) PartyManager.Log?.Info($"Party: 連携攻撃 終了 ({reason})");
        RestoreTime();
        _state = State.Idle;
        _target = null;
        _attacker = null;
        LeftPick = RightPick = null;
        Used.Clear();
    }

    private static void RestoreTime()
    {
        if (_savedScale == null) return;
        Time.timeScale = _savedScale.Value;
        _savedScale = null;
    }

    private static bool Alive(EnemyController e)
    {
        try { return e != null && e.gameObject.activeInHierarchy && e.IsAlive(); }
        catch { return false; }
    }

    // ------------------------------------------------------------------ HUD

    private static readonly Color PanelCol = new(0.04f, 0.045f, 0.06f, 0.85f);
    private static readonly Color TextCol = new(0.97f, 0.97f, 0.99f, 1f);
    private static readonly Color GaugeCol = new(1f, 0.78f, 0.25f, 1f);

    /// <summary>ポイントのゲージ (パーティ HUD の下)</summary>
    public static void DrawPoints(float x, float y, float s, float alpha)
    {
        if (!Enabled || alpha <= 0.001f) return;
        int shown = Mathf.Min(Points, PointsShown);
        float w = 200f * s, h = 6f * s;
        var prev = GUI.color;
        GUI.color = new Color(prev.r, prev.g, prev.b, prev.a * alpha);
        try
        {
            bool ready = Points >= PointsNeeded || Stock > 0;
            string label = $"CHAIN  {shown}" + (Stock > 0 ? "  +1" : "");
            Render.Text(x, y, w, 18f * s, label, ready ? GaugeCol : TextCol, Mathf.RoundToInt(12f * s),
                TextAnchor.MiddleLeft, true, true);
            float by = y + 19f * s;
            Render.Rect(x, by, w, h, new Color(0f, 0f, 0f, 0.7f));
            // 0〜1500 を 1 本目、1500〜3000 を上に重ねて明るく
            float a = Mathf.Clamp01(shown / (float)PointsNeeded);
            float b = Mathf.Clamp01((shown - PointsNeeded) / (float)PointsNeeded);
            Render.Rect(x, by, w * a, h, ready ? GaugeCol : new Color(0.75f, 0.78f, 0.85f, 1f));
            if (b > 0f) Render.Rect(x, by, w * b, h, Color.Lerp(GaugeCol, Color.white, 0.5f));
            Render.Rect(x + w * 0.5f, by, 1f, h, new Color(0f, 0f, 0f, 0.6f));
        }
        finally { GUI.color = prev; }
    }

    /// <summary>
    /// 選択中のゲージ (画面中央の下、ゼンゼロの連携の見た目)。
    /// 左右に丸い顔 (オレンジの縁、下にキー)、真ん中に赤〜オレンジのバー。バーは時間とともに両端から中央へ削れていく。
    /// バーの上に "C H A I N  A T T A C K"、下に残り時間
    /// </summary>
    public static void DrawChoose()
    {
        if (_state != State.Choosing || !Render.IsRepaint) return;
        float s = Mathf.Max(0.6f, Screen.height / 1080f);
        float face = 64f * s, ring = 4f * s;
        float w = 620f * s;
        float x = (Screen.width - w) * 0.5f, cy = Screen.height * 0.80f;

        // 左右の顔
        DrawPick(LeftPick, x + face * 0.5f, cy, face, ring, NextKey.Display, s);
        DrawPick(RightPick, x + w - face * 0.5f, cy, face, ring, PrevKey.Display, s);

        // バー
        float bx = x + face + 10f * s, bw = w - 2f * (face + 10f * s), bh = 16f * s;
        float by = cy - bh * 0.5f;
        Render.Rect(bx - 2f * s, by - 2f * s, bw + 4f * s, bh + 4f * s, new Color(0.02f, 0.02f, 0.03f, 0.9f), (bh + 4f * s) * 0.5f);
        float remain = 1f - ChooseProgress;
        float fw = bw * remain, fx = bx + (bw - fw) * 0.5f;
        if (fw > 1f)
        {
            // 赤 → オレンジ → 赤 のグラデーション (細い帯を並べる)
            const int seg = 40;
            float sw = bw / seg;
            for (int k = 0; k < seg; k++)
            {
                float sx = bx + k * sw, ex = sx + sw;
                float a = Mathf.Max(sx, fx), b = Mathf.Min(ex, fx + fw);
                if (b <= a) continue;
                float t = Mathf.Abs((k + 0.5f) / seg - 0.5f) * 2f; // 中央 0 → 端 1
                var col = Color.Lerp(new Color(1f, 0.42f, 0.18f, 1f), new Color(0.93f, 0.12f, 0.22f, 1f), t);
                Render.Rect(a, by, b - a + 0.5f, bh, col);
            }
            Render.Rect(fx, by, fw, bh * 0.35f, new Color(1f, 1f, 1f, 0.18f)); // 上側の光沢
        }
        Render.Text(bx, by - 1f * s, bw, bh + 2f * s, "C H A I N    A T T A C K", new Color(1f, 1f, 1f, 0.92f),
            Mathf.RoundToInt(11f * s), TextAnchor.MiddleCenter, true, true);

        // 残り時間 (00:SS:CC)
        float left = Mathf.Max(0f, ChooseTime - (Time.unscaledTime - _chooseStart));
        int sec = Mathf.FloorToInt(left), cs = Mathf.FloorToInt((left - sec) * 100f);
        Render.Text(bx, cy + bh * 0.5f + 4f * s, bw, 40f * s, $"00:{sec:00}:{cs:00}", new Color(1f, 1f, 1f, 0.85f),
            Mathf.RoundToInt(30f * s), TextAnchor.MiddleCenter, true, true);
        if (!SkipKey.IsNone)
        {
            string hint = _anyFollowUp ? L.T("{0}: 連携をやめる", SkipKey.Display) : L.T("{0}: 連携回避 (ストックする)", SkipKey.Display);
            Render.Text(bx, cy + bh * 0.5f + 44f * s, bw, 18f * s, hint, new Color(1f, 1f, 1f, 0.7f),
                Mathf.RoundToInt(12f * s), TextAnchor.MiddleCenter, true, true);
        }
    }

    private static void DrawPick(PlayerController p, float cx, float cy, float size, float ring, string key, float s)
    {
        if (p == null) return;
        var mm = PartyManager.FindCharacter(PartyManager.Id(p));
        float r = size * 0.5f;
        // オレンジの縁 → 暗い下地 → 丸く切り抜いた顔
        Render.Rect(cx - r - ring, cy - r - ring, size + ring * 2f, size + ring * 2f, new Color(1f, 0.55f, 0.12f, 1f), r + ring);
        Render.Rect(cx - r, cy - r, size, size, new Color(0.08f, 0.08f, 0.1f, 1f), r);
        var tex = PartyHud.Portrait(mm);
        if (tex != null)
            GUI.DrawTexture(new Rect(cx - r, cy - r, size, size), tex, ScaleMode.StretchToFill, true, 0f, Color.white, 0f, r);

        // 下のキー (小さな札)
        int fs = Mathf.RoundToInt(11f * s);
        float kw = Mathf.Max(22f * s, Render.TextWidth(key, fs, true) + 10f * s), kh = 16f * s;
        float kx = cx - kw * 0.5f, ky = cy + r + ring + 5f * s;
        Render.Rect(kx, ky, kw, kh, new Color(0.02f, 0.02f, 0.03f, 0.9f), kh * 0.5f);
        Render.Text(kx, ky, kw, kh, key, Color.white, fs, TextAnchor.MiddleCenter, true);
    }
}

// bool EnemyController.GetHit(Transform atker, AttackBox atkBox, int damage, string hitEffOverride, AttackBoxType boxType, AttackBoxController atkBoxCon)
// (呼び出し元は PlayerController.MakeDamageCallBack だけ。戻り値 true = 当たった)
[HarmonyPatch(typeof(EnemyController), nameof(EnemyController.GetHit))]
internal static class ChainEnemyHitPatch
{
    private static void Postfix(EnemyController __instance, bool __result, AttackBoxType boxType)
    {
        // 戻り値は「当たったか」ではないらしい (追加攻撃のヒットが数えられなかった) ので見ない
        try { ChainAttack.OnEnemyHit(__instance, boxType); }
        catch (Exception e) { PartyManager.Log?.Warning($"Party: 連携攻撃のヒット処理でエラー: {e.Message}"); }
    }
}
