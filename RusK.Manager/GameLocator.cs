using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace RusK.Manager;

/// <summary>ゲームフォルダの自動検出と、フォルダの状態 (ゲーム・BepInEx・RusK) の判定</summary>
internal static class GameLocator
{
    public const string SteamAppId = "3255500";
    public const string GameExe = "ved.exe";

    /// <summary>Steam のライブラリから VED:Recure を探す。見つからなければ null</summary>
    public static string FindGame()
    {
        var libraries = SteamLibraries().ToList();

        // 1. appmanifest_3255500.acf に書かれたインストール先
        foreach (var lib in libraries)
        {
            var acf = Path.Combine(lib, "steamapps", $"appmanifest_{SteamAppId}.acf");
            if (!File.Exists(acf)) continue;
            var m = Regex.Match(File.ReadAllText(acf), "\"installdir\"\\s+\"([^\"]+)\"");
            if (!m.Success) continue;
            var dir = Path.Combine(lib, "steamapps", "common", m.Groups[1].Value);
            if (IsGameFolder(dir)) return dir;
        }

        // 2. common の下で ved.exe があるフォルダ
        foreach (var lib in libraries)
        {
            var common = Path.Combine(lib, "steamapps", "common");
            if (!Directory.Exists(common)) continue;
            foreach (var dir in Directory.GetDirectories(common))
                if (IsGameFolder(dir)) return dir;
        }
        return null;
    }

    private static IEnumerable<string> SteamLibraries()
    {
        var roots = new List<string>();
        void AddRoot(object value)
        {
            if (value is string s && !string.IsNullOrWhiteSpace(s))
                roots.Add(s.Replace('/', '\\'));
        }

        try
        {
            AddRoot(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null));
            AddRoot(Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null));
        }
        catch { }

        var result = new List<string>();
        foreach (var root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            result.Add(root);
            var vdf = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf)) continue;
            try
            {
                foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                    result.Add(m.Groups[1].Value.Replace("\\\\", "\\"));
            }
            catch { }
        }
        return result.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase);
    }

    public static bool IsGameFolder(string dir) =>
        !string.IsNullOrWhiteSpace(dir) &&
        File.Exists(Path.Combine(dir, GameExe)) &&
        File.Exists(Path.Combine(dir, "GameAssembly.dll"));

    public static bool HasBepInEx(string dir) =>
        File.Exists(Path.Combine(dir, "BepInEx", "core", "BepInEx.Unity.IL2CPP.dll")) &&
        File.Exists(Path.Combine(dir, "winhttp.dll"));

    public static string BepInExVersion(string dir) =>
        FileVersion(Path.Combine(dir, "BepInEx", "core", "BepInEx.Core.dll"));

    /// <summary>ゲームの版 (build_info.txt の buildVersion、例: 0.0.1876)。読めなければ null</summary>
    public static string GameBuildVersion(string dir)
    {
        try
        {
            var path = Path.Combine(dir, "build_info.txt");
            if (!File.Exists(path)) return null;
            foreach (var line in File.ReadAllLines(path))
            {
                var parts = line.Split('=');
                if (parts.Length == 2 && parts[0].Trim() == "buildVersion") return parts[1].Trim();
            }
        }
        catch { }
        return null;
    }

    public static string RuskVersion(string dir) =>
        FileVersion(Path.Combine(dir, "BepInEx", "plugins", "RusK", "RusK.Core.dll"));

    /// <summary>
    /// ゲームが起動中か。gameDir を渡すと、そのフォルダの ved.exe が動いているときだけ true
    /// (実行ファイルの場所が取れないときは、安全のため起動中とみなす)
    /// </summary>
    public static bool GameRunning(string gameDir = null)
    {
        foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(GameExe)))
        {
            if (string.IsNullOrEmpty(gameDir)) return true;
            try
            {
                var exe = Path.GetFullPath(p.MainModule.FileName);
                var target = Path.GetFullPath(Path.Combine(gameDir, GameExe));
                if (string.Equals(exe, target, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch
            {
                return true;
            }
            finally
            {
                p.Dispose();
            }
        }
        return false;
    }

    private static string FileVersion(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var v = info.ProductVersion ?? info.FileVersion;
            if (string.IsNullOrWhiteSpace(v)) return "?";
            int plus = v.IndexOf('+');
            return plus > 0 ? v.Substring(0, plus) : v;
        }
        catch
        {
            return "?";
        }
    }
}
