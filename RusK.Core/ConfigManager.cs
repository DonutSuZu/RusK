using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RusK.API;

namespace RusK.Core;

/// <summary>RusK/configs/&lt;profile&gt;.json の中身</summary>
internal sealed class ProfileData
{
    public int Version { get; set; } = 2;
    public Dictionary<string, ModuleState> Modules { get; set; } = new();
    public List<TriggerData> Triggers { get; set; } = new();
    public Dictionary<string, PanelState> Panels { get; set; } = new();
    public Dictionary<string, WindowState> Windows { get; set; } = new();
}

internal sealed class WindowState
{
    public float X { get; set; }
    public float Y { get; set; }
    public float W { get; set; }
    public float H { get; set; }
}

internal sealed class ModuleState
{
    public bool Enabled { get; set; }
    public string Key { get; set; } = "None";
    public bool Listed { get; set; } = true;
    public Dictionary<string, string> Settings { get; set; } = new();
}

internal sealed class PanelState
{
    public float X { get; set; }
    public float Y { get; set; }
    public bool Collapsed { get; set; }
}

/// <summary>
/// Config プロファイルの読み書き。
/// アンロード中の Mod の設定も消さずに保持するので、再ロードすると状態が戻る。
/// </summary>
internal sealed class ConfigManager
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _dir;
    private ProfileData _data = new();

    public ConfigManager(string dir) => _dir = dir;

    public string CurrentProfile { get; private set; } = "default";

    public IEnumerable<string> Profiles =>
        Directory.GetFiles(_dir, "*.json").Select(Path.GetFileNameWithoutExtension)
            .Append(CurrentProfile).Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase);

    public PanelState Panel(string name, float defaultX, float defaultY)
    {
        if (!_data.Panels.TryGetValue(name, out var panel))
            _data.Panels[name] = panel = new PanelState { X = defaultX, Y = defaultY };
        return panel;
    }

    /// <summary>ウィンドウの位置と大きさ。なければ画面中央に既定サイズで作る</summary>
    public WindowState Window(string id, float defaultW, float defaultH)
    {
        if (!_data.Windows.TryGetValue(id, out var state))
        {
            _data.Windows[id] = state = new WindowState
            {
                W = defaultW,
                H = defaultH,
                X = (UnityEngine.Screen.width - defaultW) * 0.5f,
                Y = (UnityEngine.Screen.height - defaultH) * 0.5f,
            };
        }
        return state;
    }

    public void Load(string profile)
    {
        var path = PathOf(profile);
        ProfileData data = null;
        if (File.Exists(path))
        {
            try
            {
                data = JsonSerializer.Deserialize<ProfileData>(File.ReadAllText(path), JsonOptions);
            }
            catch (Exception e)
            {
                Rusk.Log.LogError($"Config '{profile}' の読み込みに失敗: {e.Message}");
                Rusk.Notifications?.Push($"Config '{profile}' が壊れています", NotifyLevel.Error);
                return;
            }
        }

        _data = data ?? new ProfileData();
        _data.Modules ??= new();
        _data.Triggers ??= new();
        _data.Panels ??= new();
        _data.Windows ??= new();
        CurrentProfile = profile;
        Rusk.LastProfile.Value = profile;

        Rusk.Triggers.Load(_data.Triggers);

        // すでに登録済みのモジュールにも反映する
        foreach (var module in Rusk.Modules.All.ToArray())
            Rusk.Modules.Quietly(() => Apply(module, resetMissing: true));

        Rusk.Log.LogInfo($"Config '{profile}' loaded");
    }

    public void Save(string profile = null)
    {
        profile ??= CurrentProfile;
        foreach (var module in Rusk.Modules.All)
            Capture(module);
        _data.Triggers = Rusk.Triggers.Export();

        try
        {
            File.WriteAllText(PathOf(profile), JsonSerializer.Serialize(_data, JsonOptions));
            CurrentProfile = profile;
            Rusk.LastProfile.Value = profile;
        }
        catch (Exception e)
        {
            Rusk.Log.LogError($"Config '{profile}' の保存に失敗: {e}");
            Rusk.Notifications.Push("Config の保存に失敗", NotifyLevel.Error);
        }
    }

    /// <summary>使われていない名前で新しいプロファイルを作り、今の状態を保存する</summary>
    public string CreateProfile()
    {
        var existing = new HashSet<string>(Profiles, StringComparer.OrdinalIgnoreCase);
        int n = 1;
        while (existing.Contains($"profile{n}")) n++;
        var name = $"profile{n}";
        Save(name);
        return name;
    }

    /// <summary>保存されている状態をモジュールに反映する</summary>
    public void Apply(Module module, bool resetMissing = false)
    {
        if (!_data.Modules.TryGetValue(module.Id, out var state) && !TryFormer(module, out state))
        {
            if (resetMissing)
            {
                module.Enabled = false;
                module.BindSetting.ResetToDefault();
                module.ListSetting.ResetToDefault();
                foreach (var setting in module.Settings) setting.ResetToDefault();
            }
            return;
        }

        if (Hotkey.TryParse(state.Key, out var key))
            module.Keybind = key;
        module.ListSetting.Value = state.Listed;

        foreach (var setting in module.Settings)
        {
            if (!setting.Persistent) continue;
            if (state.Settings != null && state.Settings.TryGetValue(setting.Name, out var text))
            {
                if (!setting.Deserialize(text))
                    Rusk.Log.LogWarning($"[{module.Id}] {setting.Name} = '{text}' を読めませんでした");
            }
            else if (resetMissing)
            {
                setting.ResetToDefault();
            }
        }

        // 設定値を入れてから ON にする (OnEnable で設定値を使うモジュールのため)
        module.Enabled = state.Enabled;
    }

    /// <summary>前の ID (別の Mod から移したモジュールなど) の設定を探す</summary>
    private bool TryFormer(Module module, out ModuleState state)
    {
        foreach (var id in module.FormerIds)
        {
            if (!_data.Modules.TryGetValue(id, out state)) continue;
            Rusk.Log.LogInfo($"[{module.Id}] 前の ID '{id}' の設定を引き継ぎました");
            return true;
        }
        state = null;
        return false;
    }

    /// <summary>モジュールの現在の状態をメモリ上の Config に控える</summary>
    public void Capture(Module module)
    {
        _data.Modules[module.Id] = new ModuleState
        {
            Enabled = module.Enabled,
            Key = module.Keybind.ToString(),
            Listed = module.ListSetting.Value,
            Settings = module.Settings.Where(s => s.Persistent).ToDictionary(s => s.Name, s => s.Serialize()),
        };
    }

    private string PathOf(string profile)
    {
        foreach (var c in Path.GetInvalidFileNameChars())
            profile = profile.Replace(c, '_');
        return Path.Combine(_dir, profile + ".json");
    }
}
