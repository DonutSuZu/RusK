using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace RusK.Installer;

/// <summary>インストールできる部品。payload.zip の中のどのファイルが属するかを持つ</summary>
internal sealed class Component
{
    public string Id;
    public string Name;
    public string Description;
    public bool Required;
    public bool DefaultOn = true;
    public Func<string, bool> Owns; // payload 内の相対パス → この部品のファイルか
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

/// <summary>インストール・アンインストールの本体。進捗は log / progress のコールバックで知らせる</summary>
internal static class InstallEngine
{
    public static readonly Component[] Components =
    {
        new Component
        {
            Id = "core", Name = "RusK 本体 (必須)", Required = true,
            Description = "Mod ローダー・メニュー (TabGUI / ClickGUI)・HUD・Config・Check (Mod の点検)",
            Owns = p => p.StartsWith("BepInEx/plugins/RusK/", StringComparison.OrdinalIgnoreCase) ||
                        p.Equals("RusK/README.txt", StringComparison.OrdinalIgnoreCase),
        },
        new Component
        {
            Id = "ui", Name = "RusK UI",
            Description = "ゼンゼロ風ボタン HUD (液体ゲージ・追加攻撃が光る)・攻撃予兆・キー追加",
            Owns = p => p.StartsWith("RusK/mods/RuskUi.", StringComparison.OrdinalIgnoreCase),
        },
        new Component
        {
            Id = "extreme", Name = "EXTREME Difficulty",
            Description = "難易度 EXTREME を解放。敵の HP・攻撃力・攻撃頻度・シールドを強化",
            Owns = p => p.StartsWith("RusK/mods/RuskExtreme.", StringComparison.OrdinalIgnoreCase),
        },
        new Component
        {
            Id = "music", Name = "Music Manager",
            Description = "戦闘中の BGM を RusK\\music の曲 (mp3 / ogg / wav) に置き換える",
            Owns = p => p.StartsWith("RusK/mods/RuskMusic.", StringComparison.OrdinalIgnoreCase),
        },
        new Component
        {
            Id = "camera", Name = "Camera View",
            Description = "視点の切り替え (近い肩越し / 真後ろ / 一人称 / カスタム)",
            Owns = p => p.StartsWith("RusK/mods/RuskCamera.", StringComparison.OrdinalIgnoreCase),
        },
        new Component
        {
            Id = "party", Name = "Party (アクティブ3人)",
            Description = "仲間 2 人と戦闘中にキーで交代。切り替えパリィ・戦闘不能時の自動交代",
            Owns = p => p.StartsWith("RusK/mods/RuskParty.", StringComparison.OrdinalIgnoreCase),
        },
        new Component
        {
            Id = "model", Name = "Custom Model (VRM)",
            Description = "キャラの見た目を VRM にする (RusK\\models に .vrm を置く。揺れ物・まばたき対応)",
            Owns = p => p.StartsWith("RusK/mods/RuskModel.", StringComparison.OrdinalIgnoreCase),
        },
        new Component
        {
            Id = "itemmodel", Name = "Custom Item Model (glb)",
            Description = "武器・装飾品の見た目を glb にする (RusK\\props に .glb を置く。発光も設定できる)",
            Owns = p => p.StartsWith("RusK/mods/RuskItemModel.", StringComparison.OrdinalIgnoreCase),
        },
    };

    /// <summary>このセットアップが対応しているゲームのバージョン</summary>
    public const string SupportedGameVersion = "0.0.1872 (9e092a0)";

    /// <summary>このセットアップに入っている RusK のバージョン</summary>
    public static string PayloadVersion =>
        typeof(InstallEngine).Assembly.GetName().Version.ToString(3);

    public static void Install(InstallOptions o, Action<string> log, Action<int> progress)
    {
        EnsureGameClosed(o.GameDir);
        progress(5);

        // 1. BepInEx (無い場合だけ、選ばれた zip を展開)
        if (!GameLocator.HasBepInEx(o.GameDir))
        {
            if (string.IsNullOrEmpty(o.BepInExZip))
                throw new InvalidOperationException("BepInEx が入っていません。BepInEx の zip を選んでください。");
            log("BepInEx を展開しています...");
            ExtractBepInEx(o.BepInExZip, o.GameDir, log);
            if (!GameLocator.HasBepInEx(o.GameDir))
                throw new InvalidOperationException("BepInEx を展開しましたが、IL2CPP 版の BepInEx が見つかりません。\n" +
                                                    "「BepInEx-Unity.IL2CPP-win-x64」の zip か確認してください。");
            log("  BepInEx を導入しました");
        }
        else
        {
            log($"BepInEx: 導入済み ({GameLocator.BepInExVersion(o.GameDir)})");
        }
        progress(25);

        // 2. RusK のファイルを展開 (選ばれた部品だけ。選ばれていない部品は削除)
        using (var zip = OpenPayload())
        {
            var entries = zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).ToList();
            int done = 0;
            foreach (var entry in entries)
            {
                var rel = entry.FullName.Replace('\\', '/');
                var component = Components.FirstOrDefault(c => c.Owns(rel));
                var dest = Path.Combine(o.GameDir, rel.Replace('/', Path.DirectorySeparatorChar));

                if (component == null || o.Components.Contains(component.Id))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(dest));
                    entry.ExtractToFile(dest, overwrite: true);
                    log($"  + {rel}");
                }
                else if (File.Exists(dest))
                {
                    File.Delete(dest);
                    log($"  - {rel} (選択されていないので削除)");
                }

                done++;
                progress(25 + 60 * done / Math.Max(1, entries.Count));
            }
        }

        // 3. フォルダ
        Directory.CreateDirectory(Path.Combine(o.GameDir, "RusK", "mods"));
        Directory.CreateDirectory(Path.Combine(o.GameDir, "RusK", "configs"));
        if (o.Components.Contains("model"))
        {
            Directory.CreateDirectory(Path.Combine(o.GameDir, "RusK", "models"));
        }
        if (o.Components.Contains("itemmodel"))
        {
            Directory.CreateDirectory(Path.Combine(o.GameDir, "RusK", "props"));
            log("  props フォルダを作成しました (ここに .glb を置きます)");
            log("  models フォルダを作成しました (ここに .vrm を置きます)");
        }
        if (o.CreateMusicFolder && o.Components.Contains("music"))
        {
            Directory.CreateDirectory(Path.Combine(o.GameDir, "RusK", "music"));
            Directory.CreateDirectory(Path.Combine(o.GameDir, "RusK", "music", "boss"));
            log("  music フォルダを作成しました");
        }
        progress(100);
        log("インストールが完了しました");
    }

    public static void Uninstall(UninstallOptions o, Action<string> log, Action<int> progress)
    {
        EnsureGameClosed(o.GameDir);
        progress(10);

        // RusK が入れたファイルだけを消す (ほかの Mod や BepInEx には触らない)
        var ours = new List<string>();
        using (var zip = OpenPayload())
            ours.AddRange(zip.Entries.Where(e => !string.IsNullOrEmpty(e.Name)).Select(e => e.FullName.Replace('\\', '/')));

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
        log("アンインストールが完了しました (BepInEx は残しています)");
    }

    private static ZipArchive OpenPayload()
    {
        var stream = typeof(InstallEngine).Assembly.GetManifestResourceStream("payload.zip")
                     ?? throw new InvalidOperationException("セットアップに payload.zip が入っていません (ビルドの問題)");
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
            throw new InvalidOperationException("選んだ zip に winhttp.dll がありません。BepInEx の zip ではないようです。");
        string prefix = loader.FullName.Substring(0, loader.FullName.Length - loader.Name.Length);

        foreach (var e in files)
        {
            if (!e.FullName.StartsWith(prefix, StringComparison.Ordinal)) continue;
            var rel = e.FullName.Substring(prefix.Length);
            var dest = Path.Combine(gameDir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(dest));
            e.ExtractToFile(dest, overwrite: true);
        }
        log($"  {files.Count} ファイルを展開しました");
    }

    private static void EnsureGameClosed(string gameDir)
    {
        if (GameLocator.GameRunning(gameDir))
            throw new InvalidOperationException("ゲームが起動中です。ゲームを終了してからやり直してください。");
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
