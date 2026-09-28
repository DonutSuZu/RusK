using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace RusK.API;

/// <summary>
/// 表示する言葉の翻訳。各 Mod は DLL に lang/ja.json・en.json・zh.json を埋め込んで同梱し、
/// RusK\lang\&lt;mod の ID&gt;\&lt;言語&gt;.json を置くと、そちらが優先される (自分で言葉を変えられる)。
///
/// 言語ファイルは「元の文 → 訳」の表 ({"スカートの調整": "Skirt tuning"})。コードに書いた文 (日本語など) がそのまま
/// キーになるので、Mod は表示する文字を T("...") で包むだけでよい。表に無ければ元の文をそのまま出す。
/// "{0}" などを含む文は T("{0} の見た目", name) のように値を渡す。
///
/// 本体はモジュール名・説明・設定名を、そのモジュールの Mod の表で自動的に訳す (Mod のコードの変更は要らない)。
/// 新しい言語 (ko.json など) は、ファイルを足すだけで選べるようになる。言語の名前は表の "_name" に書く。
/// </summary>
public static class RuskLang
{
    /// <summary>標準で用意する言語</summary>
    public static readonly string[] BuiltinCodes = { "ja", "en", "zh" };

    private static readonly Dictionary<string, string> BuiltinNames = new()
    {
        ["ja"] = "日本語",
        ["en"] = "English",
        ["zh"] = "中文",
    };

    /// <summary>Mod の ID → 言語 → (元の文 → 訳)</summary>
    private static readonly Dictionary<string, Dictionary<string, Dictionary<string, string>>> Tables = new();

    /// <summary>RusK 本体の表の ID</summary>
    public const string CoreId = "rusk";

    /// <summary>今の言語 (ja / en / zh ...)</summary>
    public static string Current { get; private set; } = "ja";

    /// <summary>言語が変わったとき</summary>
    public static event Action Changed;

    /// <summary>上書き用の言語ファイルを置くフォルダ (RusK\lang)。本体が設定する</summary>
    public static string OverrideDir { get; set; }

    /// <summary>選べる言語 (標準の 3 つ + 言語ファイルがある言語)</summary>
    public static IReadOnlyList<string> Codes
    {
        get
        {
            var codes = new List<string>(BuiltinCodes);
            foreach (var mod in Tables.Values)
                foreach (var code in mod.Keys)
                    if (!codes.Contains(code)) codes.Add(code);
            return codes;
        }
    }

    /// <summary>言語の表示名 (表の "_name"、無ければ標準の名前、それも無ければコード)</summary>
    public static string NameOf(string code)
    {
        foreach (var mod in Tables.Values)
            if (mod.TryGetValue(code, out var t) && t.TryGetValue("_name", out var n) && !string.IsNullOrEmpty(n))
                return n;
        return BuiltinNames.TryGetValue(code, out var b) ? b : code;
    }

    public static void SetLanguage(string code)
    {
        if (string.IsNullOrEmpty(code) || code == Current) return;
        Current = code;
        Changed?.Invoke();
    }

    /// <summary>元の文を今の言語に訳す (modId の表 → 本体の表 → 元の文)</summary>
    public static string T(string modId, string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        if (Lookup(modId, Current, text, out var r)) return r;
        if (modId != CoreId && Lookup(CoreId, Current, text, out r)) return r;
        return text;
    }

    /// <summary>"{0}" などを含む文を訳して値を入れる</summary>
    public static string T(string modId, string text, params object[] args)
    {
        var format = T(modId, text);
        try { return string.Format(format, args); }
        catch (FormatException) { return string.Format(text, args); }
    }

    private static bool Lookup(string modId, string code, string text, out string result)
    {
        result = null;
        if (modId == null || !Tables.TryGetValue(modId, out var langs) || !langs.TryGetValue(code, out var table)) return false;
        if (!table.TryGetValue(text, out result) || string.IsNullOrEmpty(result)) return false;
        return true;
    }

    /// <summary>
    /// Mod の言語ファイルを読み込む。DLL に埋め込んだ lang.&lt;言語&gt;.json と、上書き用フォルダの &lt;言語&gt;.json。
    /// 本体が Mod を読み込むときに呼ぶ (Mod のコードから呼ぶ必要はない)
    /// </summary>
    public static void Register(string modId, Assembly assembly, Action<string> warn = null)
    {
        var langs = new Dictionary<string, Dictionary<string, string>>();
        if (assembly != null)
        {
            foreach (var res in assembly.GetManifestResourceNames())
            {
                // 例: "lang.ja.json" (csproj の LogicalName) / "RusK.Mods.Ui.lang.ja.json"
                int at = res.IndexOf("lang.", StringComparison.Ordinal);
                if (at < 0 || !res.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                string code = res.Substring(at + 5, res.Length - at - 5 - 5);
                if (code.Contains('.')) continue;
                try
                {
                    using var s = assembly.GetManifestResourceStream(res);
                    if (s != null) Merge(langs, code, Parse(s));
                }
                catch (Exception e) { warn?.Invoke($"言語ファイル {res} を読めません: {e.Message}"); }
            }
        }
        if (!string.IsNullOrEmpty(OverrideDir))
        {
            var dir = Path.Combine(OverrideDir, modId);
            try
            {
                if (Directory.Exists(dir))
                    foreach (var file in Directory.GetFiles(dir, "*.json"))
                    {
                        try
                        {
                            using var s = File.OpenRead(file);
                            Merge(langs, Path.GetFileNameWithoutExtension(file).ToLowerInvariant(), Parse(s));
                        }
                        catch (Exception e) { warn?.Invoke($"言語ファイル {file} を読めません: {e.Message}"); }
                    }
            }
            catch { }
        }
        Tables[modId] = langs;
    }

    public static void Unregister(string modId) => Tables.Remove(modId);

    /// <summary>その Mod の表を読み直す (上書き用フォルダの言語ファイルを変えたとき)</summary>
    public static bool Has(string modId) => Tables.ContainsKey(modId);

    private static void Merge(Dictionary<string, Dictionary<string, string>> langs, string code, Dictionary<string, string> table)
    {
        if (!langs.TryGetValue(code, out var cur)) langs[code] = cur = new Dictionary<string, string>();
        foreach (var kv in table) cur[kv.Key] = kv.Value;
    }

    private static Dictionary<string, string> Parse(Stream s)
    {
        var result = new Dictionary<string, string>();
        using var doc = JsonDocument.Parse(s, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        foreach (var p in doc.RootElement.EnumerateObject())
            if (p.Value.ValueKind == JsonValueKind.String) result[p.Name] = p.Value.GetString();
        return result;
    }
}
