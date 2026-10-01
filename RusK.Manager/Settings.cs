using System;
using System.Collections.Generic;
using System.IO;

namespace RusK.Manager;

/// <summary>Mod Manager の設定 (%AppData%\RusK\manager.ini: ゲームのフォルダ・表示の言語)</summary>
internal static class Settings
{
    private static readonly string FilePath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RusK", "manager.ini");

    private static readonly Dictionary<string, string> Values = Load();

    public static string GameDir
    {
        get => Get("GameDir");
        set => Set("GameDir", value);
    }

    public static string Language
    {
        get => Get("Language");
        set => Set("Language", value);
    }

    private static string Get(string key) => Values.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

    private static void Set(string key, string value)
    {
        Values[key] = value ?? "";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
            var lines = new List<string>();
            foreach (var kv in Values) lines.Add(kv.Key + "=" + kv.Value);
            File.WriteAllLines(FilePath, lines);
        }
        catch { }
    }

    private static Dictionary<string, string> Load()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(FilePath))
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) d[line.Substring(0, i).Trim()] = line.Substring(i + 1).Trim();
                }
        }
        catch { }
        return d;
    }
}
