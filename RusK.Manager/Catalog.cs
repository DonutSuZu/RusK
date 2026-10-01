using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace RusK.Manager;

/// <summary>カタログに載っている公開中の Mod</summary>
internal sealed class CatalogMod
{
    public string Id, Name, Asset;
    public string[] Requires = Array.Empty<string>();
    public string[] Folders = Array.Empty<string>();  // 入れたときに RusK の下に作るフォルダ (例: models)
    public Dictionary<string, string> Description = new();

    public string LocalDescription =>
        Description.TryGetValue(Strings.Current, out var s) && !string.IsNullOrEmpty(s) ? s
        : Description.TryGetValue("ja", out var ja) ? ja : "";
}

internal sealed class CatalogNews
{
    public string Date;
    public Dictionary<string, string> Text = new();

    public string LocalText =>
        Text.TryGetValue(Strings.Current, out var s) && !string.IsNullOrEmpty(s) ? s
        : Text.TryGetValue("ja", out var ja) ? ja : "";
}

/// <summary>
/// 公開中の Mod の一覧とお知らせ (リポジトリの catalog.json)。起動のたびに main ブランチから取得する。
/// 取得できないとき (オフラインなど) は、この exe に埋め込んだビルド時の catalog.json を使う。
/// 版は catalog.json には書かず、GitHub のリリース (そのファイルが添付された最新のリリースのタグ) から決める
/// </summary>
internal sealed class Catalog
{
    public const string Url = "https://raw.githubusercontent.com/" + ReleaseClient.Repo + "/main/catalog.json";

    public string GameVersion = "";
    public string CoreAsset = "RusK-Core.zip";
    public string ManagerAsset = "RusK-Mod-Manager.exe";
    public readonly List<CatalogMod> Mods = new();
    public readonly List<CatalogNews> News = new();
    /// <summary>ネットから取れたか (false なら埋め込みの古い一覧)</summary>
    public bool Online;

    public CatalogMod Find(string id) =>
        Mods.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    public CatalogMod FindByAsset(string fileName) =>
        Mods.FirstOrDefault(m => string.Equals(m.Asset, fileName, StringComparison.OrdinalIgnoreCase));

    public static Catalog Builtin()
    {
        using var s = typeof(Catalog).Assembly.GetManifestResourceStream("catalog.json");
        using var r = new StreamReader(s);
        return Parse(r.ReadToEnd());
    }

    public static Catalog Parse(string json)
    {
        var root = new JavaScriptSerializer { MaxJsonLength = int.MaxValue }.DeserializeObject(json) as Dictionary<string, object>
                   ?? throw new InvalidDataException("catalog.json");
        var c = new Catalog { GameVersion = Str(root, "gameVersion") };
        if (root.TryGetValue("core", out var core) && core is Dictionary<string, object> cd) c.CoreAsset = Str(cd, "asset") ?? c.CoreAsset;
        if (root.TryGetValue("manager", out var man) && man is Dictionary<string, object> md) c.ManagerAsset = Str(md, "asset") ?? c.ManagerAsset;
        foreach (var o in List(root, "mods"))
        {
            if (o is not Dictionary<string, object> d || Str(d, "id") == null || Str(d, "asset") == null) continue;
            c.Mods.Add(new CatalogMod
            {
                Id = Str(d, "id"),
                Name = Str(d, "name") ?? Str(d, "id"),
                Asset = Str(d, "asset"),
                Requires = List(d, "requires").OfType<string>().ToArray(),
                Folders = List(d, "folders").OfType<string>().ToArray(),
                Description = Texts(d),
            });
        }
        foreach (var o in List(root, "news"))
            if (o is Dictionary<string, object> d)
                c.News.Add(new CatalogNews { Date = Str(d, "date") ?? "", Text = Texts(d) });
        return c;
    }

    private static Dictionary<string, string> Texts(Dictionary<string, object> d)
    {
        var t = new Dictionary<string, string>();
        foreach (var code in Strings.Codes)
            if (Str(d, code) is { } s) t[code] = s;
        return t;
    }

    private static string Str(Dictionary<string, object> d, string key) =>
        d.TryGetValue(key, out var v) ? v as string : null;

    private static IEnumerable<object> List(Dictionary<string, object> d, string key) =>
        d.TryGetValue(key, out var v) && v is object[] a ? a : Array.Empty<object>();
}
