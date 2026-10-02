using System;
using System.Collections.Generic;
using HarmonyLib;
using RusK.Mods.Shared;
using UnityEngine;

namespace RusK.Mods.Effect;

/// <summary>
/// エフェクトが出た瞬間に設定をかける。エフェクトはどれも GameUtil.LoadEffect (3 つ) を通る。
/// 誰のエフェクトかは、それを呼んだ関数で決める (プレイヤーの技 = そのキャラ、敵に当たったヒット = 攻撃したキャラ、敵の技 = 敵)
/// </summary>
internal static class EffectHook
{
    public static bool Enabled;
    public static EffectRules Rules;

    /// <summary>今出ようとしているエフェクトの持ち主 ("char:1002" / "enemy" / null)</summary>
    public static string Owner;

    /// <summary>最近出たエフェクト (持ち主ごとに新しい順、設定画面で選ぶ用)</summary>
    public static readonly Dictionary<string, List<string>> Seen = new();
    /// <summary>持ち主の表示名</summary>
    public static readonly Dictionary<string, string> OwnerNames = new();
    public static int SeenVersion;

    private const int SeenLimit = 80;

    public static string OwnerOf(PlayerController p)
    {
        if (p == null) return null;
        try
        {
            double id = p.GetPlayerId();
            var key = EffectRules.Char((long)id);
            if (!OwnerNames.ContainsKey(key)) OwnerNames[key] = CharacterNames.Get(id, p.name);
            return key;
        }
        catch { return null; }
    }

    /// <summary>LoadEffect の後: 記録して、設定をかける</summary>
    public static void OnLoaded(GameObject go, string name)
    {
        if (go == null) return;
        try
        {
            if (string.IsNullOrEmpty(name)) name = go.name.Replace("(Clone)", "").Trim();
            Remember(Owner ?? "", name);
            var rule = Enabled && Rules != null ? Rules.Resolve(Owner, name) : null;
            if (rule != null && !string.IsNullOrEmpty(rule.Replace) && !EffectReplacer.Has(rule.Replace))
            {
                // 差し替え先が無い (自作のバンドルを外したなど) ときは、元のエフェクトを隠さない
                rule = rule.Clone();
                rule.Replace = null;
            }
            EffectApplier.Apply(go, rule);
            EffectReplacer.Handle(go, rule);
        }
        catch (Exception e)
        {
            EffectMod.Ctx?.Log.Warning($"Effect: {name}: {e.Message}");
        }
    }

    private static void Remember(string owner, string name)
    {
        if (!Seen.TryGetValue(owner, out var list)) Seen[owner] = list = new List<string>();
        if (list.Count > 0 && list[0] == name) return;
        list.Remove(name);
        list.Insert(0, name);
        if (list.Count > SeenLimit) list.RemoveAt(list.Count - 1);
        SeenVersion++;
    }
}

[HarmonyPatch(typeof(GameUtil), nameof(GameUtil.LoadEffect),
    new[] { typeof(EffectSetting), typeof(Transform), typeof(bool), typeof(string), typeof(bool) })]
internal static class LoadEffectOnTransformPatch
{
    private static void Postfix(EffectSetting eft, bool nameOverride, string effName, GameObject __result) =>
        EffectHook.OnLoaded(__result, nameOverride && !string.IsNullOrEmpty(effName) ? effName : eft?.effectName);
}

[HarmonyPatch(typeof(GameUtil), nameof(GameUtil.LoadEffect),
    new[] { typeof(EffectSetting), typeof(Vector3), typeof(bool) })]
internal static class LoadEffectAtPositionPatch
{
    private static void Postfix(EffectSetting eft, GameObject __result) => EffectHook.OnLoaded(__result, eft?.effectName);
}

[HarmonyPatch(typeof(GameUtil), nameof(GameUtil.LoadEffect),
    new[] { typeof(EffectSetting), typeof(Transform), typeof(string) })]
internal static class LoadEffectByPathPatch
{
    private static void Postfix(EffectSetting eft, GameObject __result) => EffectHook.OnLoaded(__result, eft?.effectName);
}

// ---- 持ち主を決める (呼んだ関数の間だけ Owner を入れる) ----

[HarmonyPatch(typeof(PlayerController), nameof(PlayerController.CreateEffectOnTrans))]
internal static class PlayerEffectScope
{
    private static void Prefix(PlayerController __instance, out string __state)
    {
        __state = EffectHook.Owner;
        EffectHook.Owner = EffectHook.OwnerOf(__instance);
    }

    private static Exception Finalizer(Exception __exception, string __state)
    {
        EffectHook.Owner = __state;
        return __exception;
    }
}

[HarmonyPatch(typeof(PlayerController), nameof(PlayerController.SetPerfectDefence))]
internal static class PlayerDefenceScope
{
    private static void Prefix(PlayerController __instance, out string __state)
    {
        __state = EffectHook.Owner;
        EffectHook.Owner = EffectHook.OwnerOf(__instance);
    }

    private static Exception Finalizer(Exception __exception, string __state)
    {
        EffectHook.Owner = __state;
        return __exception;
    }
}

[HarmonyPatch(typeof(PlayerController), nameof(PlayerController.CreateGroundTrail))]
internal static class PlayerTrailScope
{
    private static void Prefix(PlayerController __instance, out string __state)
    {
        __state = EffectHook.Owner;
        EffectHook.Owner = EffectHook.OwnerOf(__instance);
    }

    private static Exception Finalizer(Exception __exception, string __state)
    {
        EffectHook.Owner = __state;
        return __exception;
    }
}

/// <summary>敵に攻撃が当たったときのヒットのエフェクトは、攻撃したキャラのものにする</summary>
[HarmonyPatch(typeof(EnemyController), nameof(EnemyController.GetHitCallback))]
internal static class EnemyHitScope
{
    private static void Prefix(Transform atker, out string __state)
    {
        __state = EffectHook.Owner;
        PlayerController p = null;
        try { if (atker != null) p = atker.GetComponentInParent<PlayerController>(); } catch { }
        EffectHook.Owner = EffectHook.OwnerOf(p ?? PlayerRef.Current);
    }

    private static Exception Finalizer(Exception __exception, string __state)
    {
        EffectHook.Owner = __state;
        return __exception;
    }
}

/// <summary>敵の技のエフェクト</summary>
[HarmonyPatch]
internal static class EnemyEffectScope
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(EnemyController), nameof(EnemyController.CreateEffect));
        yield return AccessTools.Method(typeof(EnemyController), nameof(EnemyController.CreateEffectOnRoot));
        yield return AccessTools.Method(typeof(EnemyController), nameof(EnemyController.CreateEffectOnPlayer));
        yield return AccessTools.Method(typeof(EnemyController), nameof(EnemyController.CreateGroundTrail));
    }

    private static void Prefix(out string __state)
    {
        __state = EffectHook.Owner;
        EffectHook.Owner = EffectRules.Enemy;
        EffectHook.OwnerNames[EffectRules.Enemy] = L.T("敵");
    }

    private static Exception Finalizer(Exception __exception, string __state)
    {
        EffectHook.Owner = __state;
        return __exception;
    }
}
