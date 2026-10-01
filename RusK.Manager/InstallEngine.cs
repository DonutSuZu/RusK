using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace RusK.Manager;

internal sealed class UninstallOptions
{
    public string GameDir;
    public bool RemoveData;   // RusK/configs と RusK/data (曲・VRM などの素材は消さない)
}

/// <summary>BepInEx・RusK 本体の導入とアンインストール。進捗は log / progress のコールバックで知らせる</summary>
internal static class InstallEngine
{
    /// <summary>動作確認した BepInEx (公式の 6.0.0-be.788) の zip と、その SHA256</summary>
    public const string BepInExVersion = "6.0.0-be.788";
    public const string BepInExUrl =
        "https://builds.bepinex.dev/projects/bepinex_be/788/BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.788%2B5b766a3.zip";
    public const string BepInExSha256 = "f4cc496bd098a0df4164b81e3737297707f13a47c2478dba2f60eefab784817a";

    /// <summary>BepInEx が無ければ、動作確認済みの公式版をダウンロードして入れる</summary>
    public static void EnsureBepInEx(string gameDir, Action<string> log, Action<int> progress)
    {
        if (GameLocator.HasBepInEx(gameDir)) return;
        EnsureGameClosed(gameDir);
        log(Strings.T("BepInEx {0} をダウンロードしています...", BepInExVersion));
        var zip = Path.Combine(Path.GetTempPath(), $"RusK-BepInEx-{BepInExVersion}.zip");
        try
        {
            ReleaseClient.DownloadFile(BepInExUrl, zip, BepInExSha256, (got, total) =>
            {
                if (total > 0) progress((int)(100 * got / total));
            });
        }
        catch (Exception e) when (e is not InvalidOperationException)
        {
            throw new InvalidOperationException(Strings.T("{0} をダウンロードできませんでした: {1}", "BepInEx", e.Message), e);
        }
        log(Strings.T("BepInEx を展開しています..."));
        ExtractBepInEx(zip, gameDir, log);
        try { File.Delete(zip); } catch { }
        if (!GameLocator.HasBepInEx(gameDir))
            throw new InvalidOperationException(Strings.T("BepInEx を展開しましたが、IL2CPP 版の BepInEx が見つかりません。"));
        log(Strings.T("BepInEx を導入しました"));
    }

    /// <summary>RusK 本体 (リリースの RusK-Core.zip) をダウンロードしてゲームフォルダに展開する</summary>
    public static void InstallCore(string gameDir, ReleaseClient client, ReleaseAsset asset, Action<string> log, Action<int> progress)
    {
        EnsureGameClosed(gameDir);
        EnsureBepInEx(gameDir, log, progress);
        log(Strings.T("{0} をダウンロードしています ({1})...", "RusK", "v" + asset.Version));
        var zip = Path.Combine(Path.GetTempPath(), "RusK-Core-" + asset.Version + ".zip");
        client.Download(asset, zip, (got, total) => { if (total > 0) progress((int)(100 * got / total)); });
        using (var archive = ZipFile.OpenRead(zip))
        {
            foreach (var entry in archive.Entries.Where(e => !string.IsNullOrEmpty(e.Name)))
            {
                var rel = entry.FullName.Replace('\\', '/');
                // zip の中は BepInEx/ と RusK/ の下だけ (ほかの場所には書かない)
                if (rel.Contains("..") || !(rel.StartsWith("BepInEx/plugins/RusK/") || rel.StartsWith("RusK/"))) continue;
                var dest = Path.Combine(gameDir, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                entry.ExtractToFile(dest, overwrite: true);
            }
        }
        try { File.Delete(zip); } catch { }
        Directory.CreateDirectory(ModLibrary.ModsDir(gameDir));
        Directory.CreateDirectory(Path.Combine(gameDir, "RusK", "configs"));
        log(Strings.T("RusK v{0} を入れました", asset.Version));
    }

    public static void Uninstall(UninstallOptions o, Action<string> log)
    {
        EnsureGameClosed(o.GameDir);
        // RusK 本体と Mod (RusK が無いと動かない) を消す。BepInEx・ほかの BepInEx のプラグイン・VRM などの素材は残す
        DeleteDir(Path.Combine(o.GameDir, "BepInEx", "plugins", "RusK"), log);
        DeleteDir(ModLibrary.ModsDir(o.GameDir), log);
        var readme = Path.Combine(o.GameDir, "RusK", "README.txt");
        if (File.Exists(readme)) { File.Delete(readme); log("  - RusK/README.txt"); }
        if (o.RemoveData)
        {
            DeleteDir(Path.Combine(o.GameDir, "RusK", "configs"), log);
            DeleteDir(Path.Combine(o.GameDir, "RusK", "data"), log);
        }
        DeleteIfEmpty(Path.Combine(o.GameDir, "RusK"), log);
        log(Strings.T("アンインストールが完了しました (BepInEx は残しています)"));
    }

    /// <summary>
    /// BepInEx の zip を展開する。zip の中身が 1 つのフォルダに入っている場合は、その中身をゲームフォルダに置く
    /// </summary>
    private static void ExtractBepInEx(string zipPath, string gameDir, Action<string> log)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var files = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();

        // winhttp.dll がある場所をルートとみなす
        var loader = files.FirstOrDefault(e => e.Name.Equals("winhttp.dll", StringComparison.OrdinalIgnoreCase))
                     ?? throw new InvalidOperationException(Strings.T("zip に winhttp.dll がありません。BepInEx の zip ではないようです。"));
        string prefix = loader.FullName.Substring(0, loader.FullName.Length - loader.Name.Length);

        foreach (var e in files)
        {
            if (!e.FullName.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var rel = e.FullName.Substring(prefix.Length);
            var dest = Path.Combine(gameDir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            e.ExtractToFile(dest, overwrite: true);
        }
        log(Strings.T("  {0} ファイルを展開しました", files.Count));
    }

    public static void EnsureGameClosed(string gameDir)
    {
        if (GameLocator.GameRunning(gameDir))
            throw new InvalidOperationException(Strings.T("ゲームが起動中です。ゲームを終了してからやり直してください。"));
    }

    private static void DeleteDir(string dir, Action<string> log)
    {
        if (!Directory.Exists(dir)) return;
        Directory.Delete(dir, recursive: true);
        log($"  - {dir}");
    }

    private static void DeleteIfEmpty(string dir, Action<string> log)
    {
        if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
        {
            Directory.Delete(dir);
            log($"  - {dir}");
        }
    }
}
