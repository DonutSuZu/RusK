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
/// きっかけ: 1500P たまっている (または前回キャンセルしたストックがある)、もしくは敵のシールドが割れている状態で、
///           操作キャラの追加攻撃がその敵に当たる。
/// 流れ: 時間をほぼ止めて 5 秒のゲージ → C / Z で次のキャラを選ぶ → 敵の前に出して追加攻撃 → 当たったら次の選択へ。
///       編成の人数だけ繋がる (最初の追加攻撃を含む)。敵が倒れる・追加攻撃が外れる・時間切れで終わる。
///       最初の選択を時間切れにしたら、1 回分をストックする。連携中は操作キャラが攻撃を受けない。
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

    public static int Points;
    public static int Stock;

    private enum State { Idle, Choosing, Attacking }

    private static State _state;
    public static bool Active => _state != State.Idle;

    private static EnemyController _target;
    private static readonly HashSet<IntPtr> Used = new();
    private static readonly Dictionary<IntPtr, EnemyController> ShieldUsed = new(); // シールド割れで一度始めた敵 (シールドが戻るまで)
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
        ShieldUsed.Clear();
    }

    // ------------------------------------------------------------------ 敵に攻撃が当たったとき

    public static void OnEnemyHit(EnemyController enemy, AttackBoxType boxType)
    {
        if (!Enabled || enemy == null || boxType != AttackBoxType.Player) return;
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

        bool qte;
        try { qte = cur.IsCurMotionQTE() && enemy.IsAlive(); }
        catch { return; }
        if (!qte || !PartyHud.OnField()) return;

        bool byPoints = Points >= PointsNeeded || Stock > 0;
        bool byShield = ShieldBroken(enemy) && !ShieldUsed.ContainsKey(enemy.Pointer);
        if (!byPoints && !byShield) return;
        if (Remaining(cur).Count == 0) return;

        if (byShield) ShieldUsed[enemy.Pointer] = enemy;
        Points = 0;
        Stock = 0;
        _target = enemy;
        Used.Clear();
        Used.Add(cur.Pointer);
        _anyFollowUp = false;
        PartyManager.Log?.Info($"Party: 連携攻撃 開始 ({(byShield ? "シールド割れ" : "ポイント")}) {PartyManager.Name(cur)} → 選択へ");
        BeginChoose();
    }

    private static bool ShieldBroken(EnemyController e)
    {
        try { return e.GetMaxShieldPoint() > 0 && e.GetShieldPoint() <= 0; }
        catch { return false; }
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
            ShieldUsed.Clear();
            return;
        }

        // シールドが戻った敵は、また割れたら連携できる (0.5 秒に 1 回確かめる)
        if (ShieldUsed.Count > 0 && Time.unscaledTime >= _nextShieldScan)
        {
            _nextShieldScan = Time.unscaledTime + 0.5f;
            foreach (var key in ShieldUsed.Where(kv => kv.Value == null || !ShieldBroken(kv.Value)).Select(kv => kv.Key).ToList())
                ShieldUsed.Remove(key);
        }

        if (!Enabled)
        {
            if (Active) End("無効にした");
            return;
        }

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

    private static float _nextShieldScan;

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
        else if (Time.unscaledTime - _chooseStart >= ChooseTime)
        {
            if (!_anyFollowUp)
            {
                Stock = 1;
                PartyManager.Log?.Info("Party: 連携攻撃 キャンセル (時間切れ) → 1 回分ストック");
                Ctx?.Notify(L.T("連携攻撃をストックしました"), NotifyLevel.Info);
            }
            End("時間切れ");
        }
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

    /// <summary>選択中のゲージ (画面中央の下)</summary>
    public static void DrawChoose()
    {
        if (_state != State.Choosing || !Render.IsRepaint) return;
        float s = Mathf.Max(0.6f, Screen.height / 1080f);
        float w = 620f * s, h = 96f * s, face = 80f * s;
        float x = (Screen.width - w) * 0.5f, y = Screen.height * 0.78f;

        Render.Rect(x, y, w, h, PanelCol);
        Render.Text(x, y - 30f * s, w, 28f * s, "CHAIN ATTACK", GaugeCol, Mathf.RoundToInt(22f * s),
            TextAnchor.MiddleCenter, true, true);

        // 左右の顔 (左 = 次へのキー、右 = 前へのキー)
        DrawPick(LeftPick, x + 8f * s, y + 8f * s, face, NextKey.Display, s);
        DrawPick(RightPick, x + w - 8f * s - face, y + 8f * s, face, PrevKey.Display, s);

        // 真ん中: 両端から中央に向かって進むゲージ
        float gx = x + face + 24f * s, gw = w - 2f * (face + 24f * s), gh = 14f * s;
        float gy = y + (h - gh) * 0.5f;
        Render.Rect(gx, gy, gw, gh, new Color(0f, 0f, 0f, 0.7f));
        float half = gw * 0.5f * ChooseProgress;
        Render.Rect(gx, gy, half, gh, GaugeCol);
        Render.Rect(gx + gw - half, gy, half, gh, GaugeCol);
        float left = Mathf.Max(0f, ChooseTime - (Time.unscaledTime - _chooseStart));
        Render.Text(gx, gy + gh + 4f * s, gw, 20f * s, left.ToString("0.0"), TextCol, Mathf.RoundToInt(14f * s),
            TextAnchor.MiddleCenter, true, true);
    }

    private static void DrawPick(PlayerController p, float x, float y, float size, string key, float s)
    {
        if (p == null) return;
        var mm = PartyManager.FindCharacter(PartyManager.Id(p));
        PartyHud.DrawPortrait(mm, x, y, size, 1f);
        float kw = 26f * s, kh = 22f * s;
        Render.Rect(x - 4f * s, y + size - kh + 4f * s, kw, kh, new Color(0.02f, 0.02f, 0.03f, 0.95f));
        Render.Text(x - 4f * s, y + size - kh + 4f * s, kw, kh, key, TextCol, Mathf.RoundToInt(14f * s),
            TextAnchor.MiddleCenter, true);
    }
}

// bool EnemyController.GetHit(Transform atker, AttackBox atkBox, int damage, string hitEffOverride, AttackBoxType boxType, AttackBoxController atkBoxCon)
// (呼び出し元は PlayerController.MakeDamageCallBack だけ。戻り値 true = 当たった)
[HarmonyPatch(typeof(EnemyController), nameof(EnemyController.GetHit))]
internal static class ChainEnemyHitPatch
{
    private static void Postfix(EnemyController __instance, bool __result, AttackBoxType boxType)
    {
        if (!__result) return;
        try { ChainAttack.OnEnemyHit(__instance, boxType); }
        catch (Exception e) { PartyManager.Log?.Warning($"Party: 連携攻撃のヒット処理でエラー: {e.Message}"); }
    }
}
