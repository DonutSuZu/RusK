using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RusK.Mods.Party;

/// <summary>
/// 調査用 (Party Lab、エンドフィールド風の戦闘の試作): フィールドに置いたキャラの部品が動く間だけ、
/// ゲームの「今のプレイヤー」(GameUtil.m_curPlayer) をそのキャラに差し替え、終わったら戻す。
///
/// キャラの部品 (ActionAnimController のアニメ、MovementController の移動・地面、PlayerController の攻撃など) は
/// 今のプレイヤーかどうかを見て、そうでなければ何もしないらしい
/// (置いたキャラは動作が SpecialAttack に変わってもアニメが待機のまま進まず、地面に埋まった)
/// </summary>
internal static class FieldSwap
{
    public static bool Enabled = true;

    private static readonly Stack<PlayerController> Saved = new();
    private static readonly Dictionary<IntPtr, PlayerController> Owners = new();

    /// <summary>差し替え中か (このときはキー入力の処理を止める)</summary>
    public static bool InSwap => Saved.Count > 0;

    private static PlayerController Owner(Component c)
    {
        if (c == null) return null;
        if (Owners.TryGetValue(c.Pointer, out var p) && p != null) return p;
        try { p = c.TryCast<PlayerController>() ?? c.GetComponentInParent<PlayerController>(true); }
        catch { p = null; }
        Owners[c.Pointer] = p;
        return p;
    }

    /// <summary>c の持ち主が置いたキャラなら、今のプレイヤーを差し替えて true</summary>
    public static bool Begin(Component c)
    {
        if (!Enabled || FieldProbe.Fielded.Count == 0) return false;
        var owner = Owner(c);
        if (owner == null || !FieldProbe.IsFielded(owner)) return false;
        var util = GameUtil.Instance;
        if (util == null) return false;
        var cur = util.m_curPlayer;
        if (cur != null && cur.Pointer == owner.Pointer) return false;
        Saved.Push(cur);
        util.m_curPlayer = owner;
        return true;
    }

    public static void End(bool swapped)
    {
        if (!swapped || Saved.Count == 0) return;
        var prev = Saved.Pop();
        try { GameUtil.Instance.m_curPlayer = prev; } catch { }
    }

    /// <summary>置いたキャラに何かをさせる間だけ差し替える</summary>
    public static void Run(PlayerController p, Action action)
    {
        bool swapped = Begin(p);
        try { action(); }
        finally { End(swapped); }
    }
}

[HarmonyPatch]
internal static class FieldSwapPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var list = new (Type type, string name)[]
        {
            (typeof(PlayerController), "Update"),
            (typeof(PlayerController), "FixedUpdate"),
            (typeof(PlayerController), "LateUpdate"),
            (typeof(ActionAnimController), "Update"),
            (typeof(ActionAnimController), "OnAnimatorMove"),
            (typeof(MotionController), "Update"),
            (typeof(MovementController), "Update"),
            (typeof(MovementController), "FixedUpdate"),
            (typeof(MovementController), "LateUpdate"),
        };
        foreach (var (type, name) in list)
        {
            var m = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (m != null) yield return m;
        }
    }

    private static void Prefix(Component __instance, out bool __state) => __state = FieldSwap.Begin(__instance);

    private static void Finalizer(bool __state) => FieldSwap.End(__state);
}
