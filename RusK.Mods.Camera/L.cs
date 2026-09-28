using RusK.API;

namespace RusK.Mods.Camera;

/// <summary>表示する言葉を今の言語に訳す (この Mod の言語ファイル lang/*.json と RusK\lang$id\*.json)</summary>
internal static class L
{
    public const string Id = "camera";
    public static string T(string text) => RuskLang.T(Id, text);
    public static string T(string text, params object[] args) => RuskLang.T(Id, text, args);
}
