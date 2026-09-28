using System;
using HarmonyLib;

namespace RusK.Mods.Party;

// void PlayerController.HpChange(float change, EnemyController enmCon, bool force, HpHealType healType)
// 操作中のキャラが倒れるダメージを受けたら、交代できる仲間がいる限り HP 1 で止めて戦闘不能にする
[HarmonyPatch(typeof(PlayerController), nameof(PlayerController.HpChange))]
internal static class PartyHpChangePatch
{
    private static bool Prefix(PlayerController __instance, float change)
    {
        try
        {
            if (change >= 0f) return true;
            if (PartyManager.IsDown(__instance)) return false; // 交代待ちの間はダメージを受けない
            if (__instance.GetCurHp() + change > 0f) return true;
            if (!PartyManager.TryRescue(__instance)) return true; // 仲間がいない → ゲーム本来の処理 (ゲームオーバー)

            __instance.SetCurrentHp(1f);
            return false;
        }
        catch (Exception e)
        {
            PartyManager.Log?.Error($"Party: 戦闘不能の判定でエラー: {e}");
            return true;
        }
    }
}

// void PlayerController.OnPlayerDie(int idx)
// HpChange 以外の経路で倒れたとき (落下など) の保険
[HarmonyPatch(typeof(PlayerController), nameof(PlayerController.OnPlayerDie))]
internal static class PartyDiePatch
{
    private static bool Prefix(PlayerController __instance)
    {
        try
        {
            if (!PartyManager.TryRescue(__instance)) return true;
            if (__instance.GetCurHp() <= 0f) __instance.SetCurrentHp(1f);
            return false;
        }
        catch (Exception e)
        {
            PartyManager.Log?.Error($"Party: 戦闘不能の判定でエラー: {e}");
            return true;
        }
    }
}
