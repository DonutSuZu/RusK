using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RusK.Manager;

internal enum ModState { NotInstalled, Enabled, Disabled }

/// <summary>一覧の 1 行。カタログ (公開中) とゲームフォルダの DLL を合わせたもの</summary>
internal sealed class ModEntry
{
    public string Id;
    public string FileName;          // RusK\mods に置く名前 (例: RuskParty.dll)
    public CatalogMod Catalog;       // 公開中の Mod なら
    public ModDllInfo Info;          // 入っている DLL の [RuskMod] (RusK の Mod でなければ null)
    public string LocalPath;         // 入っている DLL の場所
    public ReleaseAsset Latest;      // リリースの最新版 (取得できなければ null)
    public ModState State;

    public bool Installed => State != ModState.NotInstalled;
    public bool IsRuskMod => Info != null;
    public string Name => Catalog?.Name ?? Info?.Name ?? Path.GetFileNameWithoutExtension(FileName);
    public string InstalledVersion => Info?.Version ?? (Installed ? "?" : null);
    public bool UpdateAvailable =>
        Installed && Latest != null && ReleaseClient.CompareVersions(Latest.Version, InstalledVersion) > 0;

    /// <summary>説明 (公開中の Mod はカタログの訳、手で入れた Mod は DLL の言語ファイルの訳)</summary>
    public string Description
    {
        get
        {
            var c = Catalog?.LocalDescription;
            if (!string.IsNullOrEmpty(c)) return c;
            return Info == null ? "" : Info.Translate(Info.Description);
        }
    }

    public string[] Requires => Catalog?.Requires ?? Array.Empty<string>();
}

/// <summary>
/// ゲームフォルダの Mod の管理。有効な Mod は RusK\mods\*.dll (RusK はこのフォルダの直下だけを読み込む)、
/// 無効にした Mod は RusK\mods\disabled\ に移しておく
/// </summary>
internal static class ModLibrary
{
    public static string ModsDir(string gameDir) => Path.Combine(gameDir, "RusK", "mods");
    public static string DisabledDir(string gameDir) => Path.Combine(ModsDir(gameDir), "disabled");

    /// <summary>入っている Mod と、公開中でまだ入れていない Mod の一覧</summary>
    public static List<ModEntry> Scan(string gameDir, Catalog catalog, Func<string, ReleaseAsset> latest)
    {
        var list = new List<ModEntry>();
        if (!string.IsNullOrEmpty(gameDir))
        {
            void Add(string dir, ModState state)
            {
                if (!Directory.Exists(dir)) return;
                foreach (var path in Directory.GetFiles(dir, "*.dll"))
                {
                    var file = Path.GetFileName(path);
                    var info = ModInfoReader.Read(path);
                    var cat = catalog.FindByAsset(file) ?? (info != null ? catalog.Find(info.Id) : null);
                    // 同じ Mod が mods と disabled の両方にあるときは、有効な方だけを出す
                    if (list.Any(e => string.Equals(e.FileName, file, StringComparison.OrdinalIgnoreCase))) continue;
                    list.Add(new ModEntry
                    {
                        Id = info?.Id ?? cat?.Id ?? Path.GetFileNameWithoutExtension(file),
                        FileName = file,
                        Catalog = cat,
                        Info = info,
                        LocalPath = path,
                        State = state,
                        Latest = cat != null ? latest(cat.Asset) : null,
                    });
                }
            }
            Add(ModsDir(gameDir), ModState.Enabled);
            Add(DisabledDir(gameDir), ModState.Disabled);
        }
        foreach (var cat in catalog.Mods)
        {
            if (list.Any(e => e.Catalog == cat)) continue;
            var asset = latest(cat.Asset);
            // オンラインなのにリリースが無いもの (準備中) は出さない
            if (asset == null && catalog.Online) continue;
            list.Add(new ModEntry { Id = cat.Id, FileName = cat.Asset, Catalog = cat, Latest = asset, State = ModState.NotInstalled });
        }
        return list;
    }

    /// <summary>リリースから DLL を落として入れる / 更新する (無効にしてある Mod は無効のまま更新する)</summary>
    public static void Download(string gameDir, ModEntry e, ReleaseClient client, Action<int> progress)
    {
        InstallEngine.EnsureGameClosed(gameDir);
        if (e.Latest == null) throw new InvalidOperationException(Strings.T("{0} がリリースに見つかりません", e.FileName));
        var dir = e.State == ModState.Disabled ? DisabledDir(gameDir) : ModsDir(gameDir);
        var dest = Path.Combine(dir, e.Catalog?.Asset ?? e.FileName);
        client.Download(e.Latest, dest, (got, total) => { if (total > 0) progress((int)(100 * got / total)); });
        // 前の版の pdb は DLL と合わないので消す
        DeleteFile(Path.ChangeExtension(dest, ".pdb"));
        foreach (var folder in e.Catalog?.Folders ?? Array.Empty<string>())
            Directory.CreateDirectory(Path.Combine(gameDir, "RusK", folder.Replace('/', Path.DirectorySeparatorChar)));
    }

    public static void SetEnabled(string gameDir, ModEntry e, bool enabled)
    {
        InstallEngine.EnsureGameClosed(gameDir);
        if (!e.Installed || (e.State == ModState.Enabled) == enabled) return;
        var to = enabled ? ModsDir(gameDir) : DisabledDir(gameDir);
        Directory.CreateDirectory(to);
        Move(e.LocalPath, Path.Combine(to, e.FileName));
        var pdb = Path.ChangeExtension(e.LocalPath, ".pdb");
        if (File.Exists(pdb)) Move(pdb, Path.Combine(to, Path.GetFileName(pdb)));
    }

    public static void Remove(string gameDir, ModEntry e)
    {
        InstallEngine.EnsureGameClosed(gameDir);
        foreach (var dir in new[] { ModsDir(gameDir), DisabledDir(gameDir) })
        {
            DeleteFile(Path.Combine(dir, e.FileName));
            DeleteFile(Path.Combine(dir, Path.ChangeExtension(e.FileName, ".pdb")));
        }
    }

    /// <summary>
    /// 手元の DLL を RusK\mods にコピーする (ドラッグ＆ドロップ)。同じ ID の Mod が別の名前で入っていれば、それを置き換える。
    /// 返り値は入れたファイルの名前
    /// </summary>
    public static string Import(string gameDir, string source, ModDllInfo info, IEnumerable<ModEntry> current)
    {
        InstallEngine.EnsureGameClosed(gameDir);
        var file = Path.GetFileName(source);
        if (info != null)
        {
            foreach (var old in current.Where(c => c.Installed && c.Info != null && c.Info.Id == info.Id &&
                                                   !string.Equals(c.FileName, file, StringComparison.OrdinalIgnoreCase)))
                Remove(gameDir, old);
        }
        // 無効にしてあった同じ名前のファイルは消して、有効な方に入れる
        DeleteFile(Path.Combine(DisabledDir(gameDir), file));
        DeleteFile(Path.Combine(DisabledDir(gameDir), Path.ChangeExtension(file, ".pdb")));
        Directory.CreateDirectory(ModsDir(gameDir));
        var dest = Path.Combine(ModsDir(gameDir), file);
        if (!string.Equals(Path.GetFullPath(source), Path.GetFullPath(dest), StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(source, dest, overwrite: true);
            DeleteFile(Path.ChangeExtension(dest, ".pdb"));
            var pdb = Path.ChangeExtension(source, ".pdb");
            if (File.Exists(pdb)) File.Copy(pdb, Path.ChangeExtension(dest, ".pdb"), overwrite: true);
        }
        return file;
    }

    private static void Move(string from, string to)
    {
        DeleteFile(to);
        File.Move(from, to);
    }

    private static void DeleteFile(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }
}
