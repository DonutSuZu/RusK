using HarmonyLib;

namespace RusK.Mods.Model.Vrm;

// void CharacterShowController.InitialSetting(double id, bool showDecor, bool autoShowPose, bool ditherShow)
// … タイトル画面・キャラクター画面・装備画面の見せるためのモデルに、どのキャラかが渡される。VRM を付けるキャラの判定に使う
[HarmonyPatch(typeof(CharacterShowController), nameof(CharacterShowController.InitialSetting))]
internal static class VrmShowPatch
{
    private static void Postfix(CharacterShowController __instance, double id) => VrmSwap.OnShowInitialized(__instance, id);
}
