using Cinemachine;
using HarmonyLib;

namespace RusK.Mods.Model.Vrm;

// void CinemachineBrain.LateUpdate() (private) … タイトル画面・キャラクター画面の見せるためのモデルの動きを VRM に写す。
// タイトル画面では CameraController が動かないので、見せるためのモデルを映すカメラ (Cinemachine) の後で更新する
[HarmonyPatch(typeof(CinemachineBrain), "LateUpdate")]
internal static class VrmBrainPatch
{
    private static void Postfix() => VrmSwap.LateTick(shows: true);
}
