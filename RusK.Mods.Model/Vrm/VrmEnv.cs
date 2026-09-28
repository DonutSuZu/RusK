using RusK.API;

namespace RusK.Mods.Model.Vrm;

/// <summary>
/// glb / VRM の読み込み部品が使う Mod のコンテキスト (ログ・データフォルダ)。
/// この Vrm フォルダの一部は Custom Item Model にもソースごと取り込むので、特定の Mod に依存しないようにここを通す
/// </summary>
internal static class VrmEnv
{
    public static IModContext Ctx;
}
