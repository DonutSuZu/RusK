using System;
using HarmonyLib;
using UnityEngine;

namespace RusK.Mods.Extreme;

// 敵の強化。どれも ExtremeState.Active (EXTREME を選んでいて、Mod が ON で、ゲームの難易度が HARD) のときだけ効く。
// Il2CppInterop のパッチはゲームの関数そのものを差し替えるので、ゲーム内部からの呼び出しにも効く。

// HP: void EnemyController.InitialHp()  … HP を初期化した直後に、最大 HP を掛け算して満タンにする
[HarmonyPatch(typeof(EnemyController), nameof(EnemyController.InitialHp))]
internal static class ExtremeHpPatch
{
    private static void Postfix(EnemyController __instance)
    {
        if (!ExtremeState.Active) return;
        try
        {
            float mul = ExtremeModule.Instance.Hp.Value;
            if (mul == 1f) return;
            float max = __instance.m_maxHp * mul;
            __instance.m_maxHp = max;
            __instance.m_curHp = max;
        }
        catch (Exception e) { ExtremeState.Log?.Warning($"EXTREME hp: {e.Message}"); }
    }
}

// 攻撃の頻度: float EnemyController.GetAttackInterval()  … 攻撃間隔を倍率で割る (短くなる = よく攻撃する)
[HarmonyPatch(typeof(EnemyController), nameof(EnemyController.GetAttackInterval))]
internal static class ExtremeAttackRatePatch
{
    private static void Postfix(ref float __result)
    {
        if (!ExtremeState.Active) return;
        float mul = ExtremeModule.Instance.AttackRate.Value;
        if (mul > 1f) __result /= mul;
    }
}

// 攻撃力: int GameUtil.CaculateGetHitDamage(...)  … プレイヤーが受けるダメージを掛け算する
[HarmonyPatch(typeof(GameUtil), nameof(GameUtil.CaculateGetHitDamage))]
internal static class ExtremeDamagePatch
{
    private static void Postfix(ref int __result)
    {
        if (!ExtremeState.Active || __result <= 0) return;
        float mul = ExtremeModule.Instance.Damage.Value;
        if (mul != 1f) __result = Mathf.RoundToInt(__result * mul);
    }
}

// シールド: int EnemyController.GetMaxShieldPoint()  … シールドポイントの上限を掛け算する
[HarmonyPatch(typeof(EnemyController), nameof(EnemyController.GetMaxShieldPoint))]
internal static class ExtremeShieldPatch
{
    private static void Postfix(ref int __result)
    {
        if (!ExtremeState.Active || __result <= 0) return;
        float mul = ExtremeModule.Instance.Shield.Value;
        if (mul != 1f) __result = Mathf.CeilToInt(__result * mul);
    }
}
