using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RusK.API;

namespace RusK.Core.UI;

internal enum EntryKind
{
    /// <summary>クリック / Enter で ON/OFF (モジュール)</summary>
    Toggle,
    /// <summary>クリック / Enter で設定を開く (トリガー、設定だけのモジュールなど)</summary>
    Expand,
    /// <summary>クリック / Enter で 1 回実行 (+ Add Trigger、Load など)</summary>
    Command,
}

/// <summary>メニューの 1 項目。TabGUI と ClickGUI はこのモデルを描くだけで、中身の種類を知らない</summary>
internal interface IMenuEntry
{
    string Label { get; }
    string Suffix { get; }
    string Description { get; }
    bool Active { get; }
    bool Dim { get; }
    EntryKind Kind { get; }
    IReadOnlyList<Row> Rows { get; }
    void Activate();
}

/// <summary>
/// メニューの中身。カテゴリ = モジュールのカテゴリ + Triggers + Mods。
/// Row や項目のオブジェクトはキャッシュして使い回す (スライダーのドラッグやキー割り当て中の状態を保つため)。
/// </summary>
internal sealed class MenuModel
{
    public const string TriggersCategory = "Triggers";
    public const string ModsCategory = "Mods";

    private readonly Dictionary<object, IMenuEntry> _cache = new();

    public List<string> Categories =>
        Rusk.Modules.Categories.Concat(new[] { TriggersCategory, ModsCategory }).ToList();

    public List<IMenuEntry> Entries(string category)
    {
        if (_cache.Count > 512) _cache.Clear();
        var list = new List<IMenuEntry>();

        switch (category)
        {
            case TriggersCategory:
                foreach (var t in Rusk.Triggers.All)
                    list.Add(Get(t, () => new TriggerEntry(t)));
                list.Add(Get("add-trigger", () => new CommandEntry("+ Add Trigger",
                    "新しいアクショントリガーを追加", () => Rusk.Triggers.Add())));
                break;

            case ModsCategory:
                foreach (var mod in Rusk.Mods.Loaded)
                    list.Add(Get(mod, () => new LoadedModEntry(mod)));
                foreach (var file in Rusk.Mods.UnloadedFiles)
                {
                    var path = file;
                    list.Add(Get("file:" + path, () => new CommandEntry(Path.GetFileName(path),
                        "未読み込みの Mod。実行すると読み込む", () => Rusk.Defer(() => Rusk.Mods.Load(path)), dim: true)));
                }
                list.Add(Get("check", () => new CheckEntry()));
                list.Add(Get("profile", () => new ProfileEntry()));
                list.Add(Get("rescan", () => new CommandEntry("Rescan Mods",
                    "mods フォルダの新しい DLL を読み込む", () => Rusk.Defer(Rusk.Mods.LoadAll))));
                break;

            default:
                foreach (var m in Rusk.Modules.InCategory(category))
                    list.Add(Get(m, () => new ModuleEntry(m)));
                break;
        }

        return list;
    }

    private IMenuEntry Get(object key, Func<IMenuEntry> create)
    {
        if (!_cache.TryGetValue(key, out var entry))
            _cache[key] = entry = create();
        return entry;
    }
}

internal sealed class ModuleEntry : IMenuEntry
{
    private readonly Module _m;
    private List<Row> _rows;

    public ModuleEntry(Module m) => _m = m;

    public Module Module => _m;
    private string ModId => _m.Context?.Info?.Id ?? RuskLang.CoreId;
    public string Label => RuskLang.T(ModId, _m.Name);
    public string Suffix => _m.Toggleable && !_m.Keybind.IsNone ? _m.Keybind.Display : null;
    public string Description => RuskLang.T(ModId, _m.Description);
    public bool Active => _m.Enabled;
    public bool Dim => false;
    public EntryKind Kind => _m.Toggleable ? EntryKind.Toggle : EntryKind.Expand;

    /// <summary>トグルできるモジュールは先頭に「Bind」(トグルキー) 行と、ArrayList に出すかの行が付く</summary>
    public IReadOnlyList<Row> Rows =>
        _rows ??= (_m.Toggleable ? Row.For(_m.BindSetting, RuskLang.CoreId) : Enumerable.Empty<Row>())
            .Concat(_m.Toggleable && _m.VisibleInArrayList ? Row.For(_m.ListSetting, RuskLang.CoreId) : Enumerable.Empty<Row>())
            .Concat(Row.For(_m.Settings, ModId)).ToList();

    /// <summary>Bind 行 (Shift+クリックでのキー割り当てに使う)</summary>
    public Row BindRow => _m.Toggleable ? Rows[0] : null;

    public void Activate()
    {
        if (_m.Toggleable) _m.Toggle();
    }
}

internal sealed class TriggerEntry : IMenuEntry
{
    private readonly Trigger _t;
    private List<Row> _rows;

    public TriggerEntry(Trigger t) => _t = t;

    public string Label => $"{(_t.Key.Value.IsNone ? "(no key)" : _t.Key.Value.Display)} → {_t.Target.DisplayValue}";
    public string Suffix => _t.IsHold ? "Hold" : null;
    public string Description => "キーを押すと Mod アクションを実行する";
    public bool Active => _t.Holding;
    public bool Dim => _t.Key.Value.IsNone;
    public EntryKind Kind => EntryKind.Expand;
    public IReadOnlyList<Row> Rows => _rows ??= Row.For(_t.Settings);
    public void Activate() { }
}

internal sealed class LoadedModEntry : IMenuEntry
{
    private readonly LoadedMod _mod;
    private readonly List<Row> _rows;

    public LoadedModEntry(LoadedMod mod)
    {
        _mod = mod;
        _rows = new List<Row>
        {
            new ActionRow("Reload", () => Rusk.Defer(() => Rusk.Mods.Reload(_mod)), "DLL を読み直す (ビルドし直した後に)"),
            new ActionRow("Unload", () => Rusk.Defer(() => Rusk.Mods.Unload(_mod)), "この Mod を外す"),
        };
    }

    public string Label => _mod.DisplayName;
    public string Suffix => null;
    public string Description => string.Join(" / ", _mod.Contexts.Select(c => RuskLang.T(c.Info.Id, c.Info.Description)).Where(d => d != ""));
    public bool Active => true;
    public bool Dim => false;
    public EntryKind Kind => EntryKind.Expand;
    public IReadOnlyList<Row> Rows => _rows;
    public void Activate() { }
}

internal sealed class ProfileEntry : IMenuEntry
{
    private readonly List<Row> _rows;

    public ProfileEntry()
    {
        _rows = new List<Row>
        {
            new FuncRow("Profile", () => Rusk.Config.CurrentProfile, dir => Rusk.Defer(() => Switch(dir)),
                "←/→ でプロファイルを切り替え (今の状態は保存してから切り替える)"),
            new ActionRow("Save", () =>
            {
                Rusk.Config.Save();
                Rusk.Notifications.Push($"Config '{Rusk.Config.CurrentProfile}' saved", NotifyLevel.Success);
            }, "今の状態を保存"),
            new ActionRow("Reload", () => Rusk.Defer(() =>
            {
                Rusk.Config.Load(Rusk.Config.CurrentProfile);
                Rusk.Notifications.Push($"Config '{Rusk.Config.CurrentProfile}' loaded");
            }), "保存済みの状態に戻す"),
            new ActionRow("New Profile", () => Rusk.Defer(() =>
            {
                var name = Rusk.Config.CreateProfile();
                Rusk.Notifications.Push($"Profile '{name}' created", NotifyLevel.Success);
            }), "今の状態を新しいプロファイルとして保存"),
        };
    }

    public string Label => $"Config: {Rusk.Config.CurrentProfile}";
    public string Suffix => null;
    public string Description => "設定プロファイルの保存 / 読み込み";
    public bool Active => false;
    public bool Dim => false;
    public EntryKind Kind => EntryKind.Expand;
    public IReadOnlyList<Row> Rows => _rows;
    public void Activate() { }

    private static void Switch(int dir)
    {
        var profiles = Rusk.Config.Profiles.ToList();
        if (profiles.Count < 2) return;
        int i = profiles.FindIndex(p => string.Equals(p, Rusk.Config.CurrentProfile, StringComparison.OrdinalIgnoreCase));
        var next = profiles[(i + dir + profiles.Count) % profiles.Count];
        Rusk.Config.Save();
        Rusk.Config.Load(next);
        Rusk.Notifications.Push($"Profile → {next}");
    }
}

/// <summary>Mods カテゴリの「Check」。状態を表示し、実行で診断ウィンドウを開く</summary>
internal sealed class CheckEntry : IMenuEntry
{
    public string Label => $"Check: {Rusk.Doctor.Summary}";
    public string Suffix => null;
    public string Description => "Mod の診断 (ゲームの更新で壊れた Mod や、エラーが出ている Mod を調べる)";
    public bool Active => Rusk.Doctor.ErrorCount > 0 || Rusk.Doctor.WarningCount > 0;
    public bool Dim => false;
    public EntryKind Kind => EntryKind.Command;
    public IReadOnlyList<Row> Rows => Array.Empty<Row>();
    public void Activate() => Rusk.CheckWindow.Toggle();
}

internal sealed class CommandEntry : IMenuEntry
{
    private readonly Action _action;

    public CommandEntry(string label, string description, Action action, bool dim = false)
    {
        Label = label;
        Description = description;
        _action = action;
        Dim = dim;
    }

    public string Label { get; }
    public string Suffix => null;
    public string Description { get; }
    public bool Active => false;
    public bool Dim { get; }
    public EntryKind Kind => EntryKind.Command;
    public IReadOnlyList<Row> Rows => Array.Empty<Row>();
    public void Activate() => _action();
}
