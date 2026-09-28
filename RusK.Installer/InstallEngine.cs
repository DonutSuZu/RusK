using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace RusK.Installer;

/// <summary>
/// インストールできる部品。本体 (core) はセットアップに同梱、Mod は GitHub のリリースから最新の DLL をダウンロードする
/// </summary>
internal sealed class Component
{
    public string Id;
    public string Name;
    public string Description;   // 日本語 (表示するときに Strings.T で訳す)
    public bool Required;
    public bool DefaultOn = true;
    public string Asset;         // リリースに添付する DLL の名前 (本体は null)
}

internal sealed class InstallOptions
{
    public string GameDir;
    public string BepInExZip;          // BepInEx が無いときに展開する zip (無ければ null)
    public HashSet<string> Components = new();
    public bool CreateMusicFolder = true;
}

internal sealed class UninstallOptions
{
    public string GameDir;
    public bool RemoveData;   // RusK/configs と RusK/data
    public bool RemoveMusic;  // RusK/music
}

/// <summary>インストール・アンインストールの本体。進捗は log / progress / status のコールバックで知らせる</summary>
internal static class InstallEngine
{
    public static readonly Component[] Components =
    {
        new Component
        {
            Id = "core", Name = "RusK 本体 (必須)", Required = true,
            Description = "Mod ローダー・メニュー (TabGUI / ClickGUI)・HUD・Config・Check (Mod の点検)・言語",
        },
        new Component
        {
            Id = "ui", Name = "RusK UI", Asset = "RuskUi.dll",
            Description = "ゼンゼロ風ボタン HUD (液体ゲージ・追加攻撃が光る)・攻撃予兆・キー追加",
        },
        new Component
        {
            Id = "extreme", Name = "EXTREME Difficulty", Asset = "RuskExtreme.dll",
            Description = "難易度 EXTREME を解放。敵の HP・攻撃力・攻撃頻度・シールドを強化",
        },
        new Component
        {
            Id = "music", Name = "Music Manager", Asset = "RuskMusic.dll",
            Description = "戦闘中の BGM を RusK\\music の曲 (mp3 / ogg / wav) に置き換える",
        },
        new Component
        {
            Id = "camera", Name = "Camera View", Asset = "RuskCamera.dll",
            Description = "視点の切り替え (近い肩越し / 真後ろ / 一人称 / カスタム)",
        },
        new Component
        {
            Id = "party", Name = "Party", Asset = "RuskParty.dll",
            Description = "アクティブ3人。仲間 2 人と戦闘中にキーで交代。切り替えパリィ・戦闘不能時の自動交代",
        },
        new Component
        {
            Id = "model", Name = "Custom VRM Loader", Asset = "RuskModel.dll",
            Description = "キャラの見た目を VRM にする (RusK\\models に .vrm を置く)。口パク・表情・揺れ物に対応",
        },
        new Component
        {
            Id = "itemmodel", Name = "Custom Item Model", Asset = "RuskItemModel.dll",
            Description = "武器・装飾品の見た目を glb にする (RusK\\props に .glb を置く)。発光も設定できる",
        },
    };

    /// <summary>このセットアップが対応しているゲームのバージョン</summary>
    public const string SupportedGameVersion = "0.0.1872 (9e092a0)";

    /// <summary>このセットアップに入っている RusK のバージョン</summary>
    public static string PayloadVersion =>
        typeof(InstallEngine).Assembly.GetName().Version.ToString(3);

    public static void Install(InstallOptions o, Action<string> log, Action<int> progress, Action<string> status)
    {
        EnsureGameClosed(o.GameDir);
        progress(5);

        // 1. BepInEx (無い場合だけ、選ばれた zip を展開)
        if (!GameLocator.HasBepInEx(o.GameDir))
        {
            if (string.IsNullOrEmpty(o.BepInExZip))
                throw new InvalidOperationException(Strings.T("BepInEx が入っていません。BepInEx の zip を選んでください。"));
            log(Strings.T("BepInEx を展開しています..."));
            ExtractBepInEx(o.BepInExZip, o.GameDir, log);
            if (!GameLocator.HasBepInEx(o.GameDir))
                throw new InvalidOperationException(Strings.T("BepInEx を展開しましたが、IL2CPP 版の BepInEx が見つかりません。\n「BepInEx-Unity.IL2CPP-win-x64」の zip か確認してください。"));
            log(Strings.T("  BepInEx を導入しました"));
        }
        else
        {
            log(Strings.T("BepInEx: 導入済み") + $" ({GameLocator.BepInExVersion(o.GameDir)})");
        }
        progress(25);

        // 2. RusK 本体 (セットアップに同梱)
        log(Strings.T("RusK 本体をインストールしています..."));
        using (var zip = OpenPayload())
        {
            foreach (var entry in zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)))
            {
                var rel = entry.FullName.Replace('\\', '/');
                var dest = Path.Combine(o.GameDir, rel.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(dest));
                entry.ExtractToFile(dest, overwrite: true);
                log($"  + {rel}");
            }
        }
        progress(30);

        // 3. Mod (選ばれたものは GitHub のリリースから最新の DLL をダウンロード、選ばれていないものは削除)
        var modsDir = Path.Combine(o.GameDir, "RusK", "mods");
        Directory.CreateDirectory(modsDir);
        var wanted = Components.Where(c => c.Asset != null && o.Components.Contains(c.Id)).ToList();
        foreach (var c in Components.Where(c => c.Asset != null && !o.Components.Contains(c.Id)))
        {
            foreach (var f in new[] { c.Asset, Path.ChangeExtension(c.Asset, ".pdb") })
            {
                var path = Path.Combine(modsDir, f);
                if (!File.Exists(path)) continue;
                File.Delete(path);
                log(Strings.T("  - {0} (選択されていないので削除)", "RusK/mods/" + f));
            }
        }
        if (wanted.Count > 0)
        {
            log(Strings.T("リリースの一覧を取得しています..."));
            var client = new ReleaseClient();
            client.Fetch();
            for (int i = 0; i < wanted.Count; i++)
            {
                var c = wanted[i];
                var asset = client.Latest(c.Asset)
                            ?? throw new InvalidOperationException(Strings.T("{0} がリリースに見つかりません", c.Asset));
                log(Strings.T("{0} をダウンロードしています ({1})...", c.Name, "v" + asset.Version));
                int from = 30 + 65 * i / wanted.Count, span = 65 / wanted.Count;
                try
                {
                    client.Download(asset, Path.Combine(modsDir, c.Asset), (got, total) =>
                    {
                        status(Strings.T("{0}: {1} / {2}", c.Asset, ReleaseClient.FormatSize(got), ReleaseClient.FormatSize(total)));
                        if (total > 0) progress(from + (int)(span * got / total));
                    });
                }
                catch (Exception e) when (e is not InvalidOperationException)
                {
                    throw new InvalidOperationException(Strings.T("{0} をダウンロードできませんでした: {1}", c.Asset, e.Message), e);
                }
                // 前のバージョンの pdb は DLL と合わないので消す
                var pdb = Path.Combine(modsDir, Path.ChangeExtension(c.Asset, ".pdb"));
                if (File.Exists(pdb)) File.Delete(pdb);
                log(Strings.T("  {0} ({1}) を配置しました", c.Asset, "v" + asset.Version));
            }
            status("");
        }
        progress(95);

        // 3. フォルダ
        Directory.CreateDirectory(Path.Combine(o.GameDir, "RusK", "mods"));
        Directory.CreateDirectory(Path.Combine(o.GameDir, "RusK", "configs"));
        if (o.Components.Contains("model"))
        {
            Directory.CreateDirectory(Path.Combine(o.GameDir, "RusK", "models"));
            log(Strings.T("  models フォルダを作成しました (ここに .vrm を置きます)"));
        }
        if (o.Components.Contains("itemmodel"))
        {
            Directory.CreateDirectory(Path.Combine(o.GameDir, "RusK", "props"));
            log(Strings.T("  props フォルダを作成しました (ここに .glb を置きます)"));
        }
        if (o.CreateMusicFolder && o.Components.Contains("music"))
        {
            Directory.CreateDirectory(Path.Combine(o.GameDir, "RusK", "music"));
            Directory.CreateDirectory(Path.Combine(o.GameDir, "RusK", "music", "boss"));
            log(Strings.T("  music フォルダを作成しました"));
        }
        progress(100);
        log(Strings.T("インストールが完了しました"));
    }

    public static void Uninstall(UninstallOptions o, Action<string> log, Action<int> progress)
    {
        EnsureGameClosed(o.GameDir);
        progress(10);

        // RusK が入れたファイルだけを消す (ほかの Mod や BepInEx には触らない)
        var ours = new List<string>();
        using (var zip = OpenPayload())
            ours.AddRange(zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).Select(e => e.FullName.Replace('\\', '/')));
        // ダウンロードした Mod
        foreach (var c in Components.Where(c => c.Asset != null))
        {
            ours.Add("RusK/mods/" + c.Asset);
            ours.Add("RusK/mods/" + Path.ChangeExtension(c.Asset, ".pdb"));
        }

        foreach (var rel in ours)
        {
            var path = Path.Combine(o.GameDir, rel.Replace('/', Path.DirectorySeparatorChar));
            if (!File.Exists(path)) continue;
            File.Delete(path);
            log($"  - {rel}");
        }
        DeleteIfEmpty(Path.Combine(o.GameDir, "BepInEx", "plugins", "RusK"), log);
        progress(50);

        if (o.RemoveData)
        {
            DeleteDir(Path.Combine(o.GameDir, "RusK", "configs"), log);
            DeleteDir(Path.Combine(o.GameDir, "RusK", "data"), log);
        }
        if (o.RemoveMusic)
            DeleteDir(Path.Combine(o.GameDir, "RusK", "music"), log);

        DeleteIfEmpty(Path.Combine(o.GameDir, "RusK", "mods"), log);
        DeleteIfEmpty(Path.Combine(o.GameDir, "RusK"), log);
        progress(100);
        log(Strings.T("アンインストールが完了しました (BepInEx は残しています)"));
    }

    private static ZipArchive OpenPayload()
    {
        var stream = typeof(InstallEngine).Assembly.GetManifestResourceStream("payload.zip")
                     ?? throw new InvalidOperationException(Strings.T("セットアップに payload.zip が入っていません (ビルドの問題)"));
        return new ZipArchive(stream, ZipArchiveMode.Read);
    }

    /// <summary>
    /// BepInEx の zip を展開する。zip の中身が 1 つのフォルダに入っている場合は、その中身をゲームフォルダに置く
    /// </summary>
    private static void ExtractBepInEx(string zipPath, string gameDir, Action<string> log)
    {
        using var zip = ZipFile.OpenRead(zipPath);
        var files = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();

        // winhttp.dll がある場所をルートとみなす
        var loader = files.FirstOrDefault(e => e.Name.Equals("winhttp.dll", StringComparison.OrdinalIgnoreCase));
        if (loader == null)
            throw new InvalidOperationException(Strings.T("選んだ zip に winhttp.dll がありません。BepInEx の zip ではないようです。"));
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

    private static void EnsureGameClosed(string gameDir)
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
