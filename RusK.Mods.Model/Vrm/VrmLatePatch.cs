using HarmonyLib;

namespace RusK.Mods.Model.Vrm;

// void CameraController.LateUpdate()  … アニメーションの後、毎フレーム VRM に動きを写す
// (Application.onBeforeRender はこのゲームでは削られていて使えない)
[HarmonyPatch(typeof(CameraController), nameof(CameraController.LateUpdate))]
internal static class VrmLatePatch
{
    private static void Postfix() => VrmSwap.LateTick();
}
