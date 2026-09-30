using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RusK.Mods.Party;

/// <summary>
/// ジャスト切り替え (ZZZ のパリィ支援)。
///
/// 切り替えた瞬間を「ガードを押した瞬間」として扱う。切り替えの後 GuardTime 秒の間に攻撃を受けたら、
/// 出てきたキャラをゲーム本来のガードに入れ、ガード開始からの時間を「切り替えからの時間」にする。
/// パリィになるか普通のガードになるかは、ゲーム自身のガード判定に任せる
/// (受付時間・弾や爆弾を弾けるか・エフェクトや音も、本物のガードと同じになる)。
///
/// 攻撃の予兆 (キラーン) の直後の切り替えは、クールタイム中でもできる。
/// </summary>
internal static class JustSwitch
{
    public static bool Enabled = true;
    /// <summary>予兆からこの秒数以内なら、クールタイム中でも切り替えられる</summary>
    public static float Window = 0.6f;
    /// <summary>切り替えの後、この秒数の間に受けた攻撃をガードで受ける</summary>
    public static float GuardTime = 1.0f;
    /// <summary>予兆の直後はクールタイムを無視する</summary>
    public static bool IgnoreCooldown = true;

    private static float _lastWarning = -999f;

    /// <summary>最後にジャスト切り替え / パリィ / 回避が起きた時刻 (HUD の演出用)</summary>
    public static float LastJust = -999f;
    public static float LastParry = -999f;
    public static float LastEvade = -999f;

    /// <summary>今切り替えれば予兆の直後になるか (クールタイムを無視できる)</summary>
    public static bool IsJustTiming => Enabled && Time.unscaledTime - _lastWarning <= Window;

    public static void OnWarning(EnemyController enemy) => _lastWarning = Time.unscaledTime;

    public static void OnSwitched(PlayerController next, bool just)
    {
        if (just) _lastWarning = -999f; // 同じ予兆で何度もクールタイムを無視しないように
    }

    public static void Reset() => _lastWarning = -999f;

    // ---- 攻撃を受けたときの処理 (GetHit の前後)

    private enum HitKind { None, SwitchGuard, RealGuard }

    private static HitKind _kind;
    private static float _elapsed;
    private static float _counter;
    private static float _counterStart;

    // 本物のガード (プレイヤーがガードを押した) の時刻。ゲームのガードの受付時間を調べるための記録用
    private static bool _ourDefenceCall;
    private static float _realGuardAt = -999f;

    public static void OnDefence()
    {
        if (!_ourDefenceCall) _realGuardAt = Time.time;
    }

    /// <summary>GetHit の前。false を返したら攻撃を無効にする (元の処理を止める)</summary>
    public static bool BeforeHit(PlayerController p, EnemyController enemy, AttackBox box)
    {
        _kind = HitKind.None;

        // 戦闘不能で交代を待っているキャラは、これ以上攻撃を受けない
        if (PartyManager.IsDown(p)) return false;

        // 連携攻撃の間は攻撃を受けない (切り替えガードも働かせない)
        if (ChainAttack.Active) return false;

        float elapsed = Time.time - PartyManager.LastSwitchGameTime;
        if (Enabled && p.Pointer == PartyManager.LastSwitchedIn && elapsed >= 0f && elapsed <= GuardTime)
        {
            StartGuard(p, elapsed);
            return true;
        }

        // 本物のガードの記録 (ガード中に攻撃を受けた)
        try
        {
            if (p.GetInDefence())
            {
                _kind = HitKind.RealGuard;
                _elapsed = Time.time - _realGuardAt;
                _counter = p.m_defenceTimeCnt;
            }
        }
        catch { }
        return true;
    }

    /// <summary>「切り替えた瞬間にガードを押した」状態にする</summary>
    private static void StartGuard(PlayerController p, float elapsed)
    {
        _kind = HitKind.SwitchGuard;
        _elapsed = elapsed;
        _ourDefenceCall = true;
        try { p.Defence(true); }
        catch (Exception e) { PartyManager.Log?.Warning($"Party: ガードに入れませんでした: {e.Message}"); }
        finally { _ourDefenceCall = false; }

        // キャラによっては (Kiki など) ガードの動作に入っても「ガード中」の印がすぐ立たないので立てる。
        // ガードの残り時間 (m_defenceTimeCnt、ガードを押すと約 1.8 秒から減っていく) は、
        // 切り替えた瞬間にガードを押したことにして、切り替えからの時間を引く (パリィになるかはゲームが判定する)
        try
        {
            if (!p.GetInDefence()) p.SetInDefence(true);
            float start = p.m_defenceTimeCnt;
            _counterStart = start;
            if (start > elapsed) p.m_defenceTimeCnt = start - elapsed;
            _counter = p.m_defenceTimeCnt;
        }
        catch (Exception e) { PartyManager.Log?.Warning($"Party: ガード状態を設定できませんでした: {e.Message}"); }
    }

    public static void AfterHit(PlayerController p)
    {
        var kind = _kind;
        _kind = HitKind.None;
        if (kind == HitKind.None) return;

        bool parried = false;
        try { parried = p.IsInPerfectDefence() || p.GetPerfectDefenceTimeSlow() || p.m_inPerfectDefencePending; } catch { }

        if (kind == HitKind.SwitchGuard)
        {
            if (parried) LastParry = Time.unscaledTime;
            string motion = "?";
            try { motion = p.GetCurMotion()?.name; } catch { }
            PartyManager.Log?.Info($"Party: 切り替えガード {PartyManager.Name(p)} 切り替えから {_elapsed:0.00}s " +
                                   $"(残り時間 {_counterStart:0.000} → {_counter:0.000}) → {(parried ? "パリィ" : "ガード")} 動作='{motion}'");
        }
        else
        {
            string motion = "?";
            try { motion = p.GetCurMotion()?.name; } catch { }
            PartyManager.Log?.Info($"Party: 本物のガード {PartyManager.Name(p)} ガード入力から {_elapsed:0.00}s " +
                                   $"(残り時間 {_counter:0.000}) → {(parried ? "パリィ" : "ガード")} 動作='{motion}'");
        }
    }
}

// void EnemyController.CreateWarning(...) の 3 つのオーバーロードすべて (攻撃の予兆 = キラーン)
[HarmonyPatch]
internal static class JustWarningPatch
{
    private static IEnumerable<MethodBase> TargetMethods() =>
        typeof(EnemyController).GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.Name == nameof(EnemyController.CreateWarning));

    private static void Postfix(EnemyController __instance) => JustSwitch.OnWarning(__instance);
}

// void PlayerController.Defence(bool force)  … プレイヤーがガードを押した時刻の記録
[HarmonyPatch(typeof(PlayerController), nameof(PlayerController.Defence))]
internal static class JustDefencePatch
{
    private static void Prefix() => JustSwitch.OnDefence();
}

// void PlayerController.GetHit(EnemyController enmCon, AttackBox box, AttackBoxController boxCon, Collider hitCollider)
[HarmonyPatch(typeof(PlayerController), nameof(PlayerController.GetHit))]
internal static class JustParryPatch
{
    private static bool Prefix(PlayerController __instance, EnemyController enmCon, AttackBox box)
    {
        try { return JustSwitch.BeforeHit(__instance, enmCon, box); }
        catch (Exception e)
        {
            PartyManager.Log?.Error($"Party: パリィ判定でエラー: {e}");
            return true;
        }
    }

    private static void Postfix(PlayerController __instance)
    {
        try { JustSwitch.AfterHit(__instance); }
        catch (Exception e) { PartyManager.Log?.Error($"Party: パリィ判定でエラー: {e}"); }
    }
}
