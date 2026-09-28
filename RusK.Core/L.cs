using RusK.API;

namespace RusK.Core;

/// <summary>RusK 本体の表示する言葉を今の言語に訳す (RusK.Core/lang/*.json と RusK\lang\rusk\*.json)</summary>
internal static class L
{
    public static string T(string text) => RuskLang.T(RuskLang.CoreId, text);
    public static string T(string text, params object[] args) => RuskLang.T(RuskLang.CoreId, text, args);
}
