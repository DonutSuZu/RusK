using System;
using System.Collections.Generic;
using System.Linq;
using RusK.API;
using UnityEngine;

namespace RusK.Core;

/// <summary>登録されたモジュールの一覧と、ON/OFF・毎フレーム処理の配送</summary>
internal sealed class ModuleManager
{
    private static readonly string[] CategoryOrder =
    {
        API.Categories.Combat, API.Categories.Visual, API.Categories.Movement,
        API.Categories.Player, API.Categories.World, API.Categories.Misc,
    };

    private readonly List<Module> _modules = new();
    private int _quiet;

    public IReadOnlyList<Module> All => _modules;

    /// <summary>標準カテゴリを先に、Mod 独自のカテゴリをその後ろに並べる</summary>
    public IEnumerable<string> Categories =>
        _modules.Select(m => m.Category).Distinct()
            .OrderBy(c => Array.IndexOf(CategoryOrder, c) is var i && i >= 0 ? i : int.MaxValue)
            .ThenBy(c => c, StringComparer.Ordinal);

    public IEnumerable<Module> InCategory(string category) => _modules.Where(m => m.Category == category);

    public void Register(Module module, ModContext owner)
    {
        if (module == null) throw new ArgumentNullException(nameof(module));
        var id = $"{owner.Info.Id}:{module.Name}";
        if (_modules.Any(m => m.Id == id))
            throw new InvalidOperationException($"Module '{id}' is already registered");

        module.Id = id;
        module.Context = owner;
        module.EnabledChanged += OnEnabledChanged;
        _modules.Add(module);

        // 前回の状態 (ON/OFF, キー, 設定値) を Config から戻す
        Quietly(() => Rusk.Config.Apply(module));
    }

    public void UnregisterOwner(ModContext owner)
    {
        foreach (var module in _modules.Where(m => m.Context == owner).ToArray())
        {
            Rusk.Config.Capture(module);
            if (module.Enabled) Quietly(() => module.Enabled = false);
            module.EnabledChanged -= OnEnabledChanged;
            _modules.Remove(module);
        }
    }

    public void Update()
    {
        foreach (var module in _modules.ToArray())
        {
            if (!module.Enabled) continue;
            try { module.OnUpdate(); }
            catch (Exception e) { Fault(module, "OnUpdate", e); }
        }
    }

    public void OnGUI()
    {
        foreach (var module in _modules.ToArray())
        {
            if (!module.Enabled) continue;
            try { module.OnGUI(); }
            catch (Exception e) { Fault(module, "OnGUI", e); }
        }
    }

    /// <summary>通知を出さずに処理する (Config 読み込み時など)</summary>
    public void Quietly(Action action)
    {
        _quiet++;
        try { action(); }
        finally { _quiet--; }
    }

    private void OnEnabledChanged(Module module)
    {
        try
        {
            if (module.Enabled) module.OnEnable();
            else module.OnDisable();
        }
        catch (Exception e)
        {
            Rusk.Log.LogError($"[{module.Id}] {(module.Enabled ? "OnEnable" : "OnDisable")} failed: {e}");
            Rusk.Doctor.RecordRuntimeError(module.Context, $"{module.Name}.{(module.Enabled ? "OnEnable" : "OnDisable")}", e);
            module.SetEnabledSilently(false);
            Rusk.Notifications.Push($"{module.Name} でエラー (ログを確認)", NotifyLevel.Error);
            return;
        }

        if (_quiet == 0)
        {
            Rusk.Notifications.Push($"{module.Name} {(module.Enabled ? "enabled" : "disabled")}",
                module.Enabled ? NotifyLevel.Success : NotifyLevel.Info);
        }
    }

    // 例外を出し続けるモジュールは止める (毎フレームのエラーログ連打を防ぐ)
    private void Fault(Module module, string where, Exception e)
    {
        Rusk.Log.LogError($"[{module.Id}] {where} threw, disabling: {e}");
        Rusk.Doctor.RecordRuntimeError(module.Context, $"{module.Name}.{where} (停止しました)", e);
        Quietly(() => module.Enabled = false);
        Rusk.Notifications.Push($"{module.Name} を停止しました (エラー)", NotifyLevel.Error);
    }
}
