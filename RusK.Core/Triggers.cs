using System;
using System.Collections.Generic;
using System.Linq;
using RusK.API;
using RusK.Core.UI;

namespace RusK.Core;

/// <summary>Config に保存するトリガー 1 件分</summary>
internal sealed class TriggerData
{
    public string Target { get; set; }
    public string Key { get; set; } = "None";
    public string Mode { get; set; } = "Press";
}

/// <summary>トリガーの発動先。Mod のアクション、またはモジュールの ON/OFF</summary>
internal sealed class TriggerTarget
{
    public string Id { get; init; }
    public string Name { get; init; }
    public Module Module { get; init; }
    public ModAction Action { get; init; }
}

/// <summary>
/// アクショントリガー 1 件。「このキーを押したら、この Mod アクションを実行する」。
/// 各項目は Setting なので、TabGUI / ClickGUI の設定行としてそのまま編集できる。
/// </summary>
internal sealed class Trigger
{
    public Trigger(TriggerManager manager)
    {
        Target = new TargetSetting("Action", manager);
        Key = new HotkeySetting("Key", Hotkey.None, "発動するキー (修飾キーも可)");
        Mode = new ModeSetting("Mode", new[] { "Press", "Hold" }, 0,
            "Press: 押すたびに実行 / Hold: 押している間だけ (モジュールは押している間 ON)");
        Delete = new ButtonSetting("Delete", () => Rusk.Defer(() => manager.Remove(this)), "このトリガーを削除");
        Settings = new Setting[] { Target, Key, Mode, Delete };
    }

    public TargetSetting Target { get; }
    public HotkeySetting Key { get; }
    public ModeSetting Mode { get; }
    public ButtonSetting Delete { get; }
    public IReadOnlyList<Setting> Settings { get; }

    public bool IsHold => Mode.Value == 1;

    internal bool Holding;
    internal bool PreviousEnabled;
}

/// <summary>トリガーの発動先を選ぶ設定。選択肢は、今読み込まれているアクションとモジュールから毎回作る</summary>
internal sealed class TargetSetting : Setting<string>, ICycleSetting
{
    private readonly TriggerManager _manager;

    public TargetSetting(string name, TriggerManager manager) : base(name, "実行する Mod アクション / モジュール")
    {
        _manager = manager;
    }

    public override string DisplayValue =>
        string.IsNullOrEmpty(Value) ? "(none)" : _manager.Resolve(Value)?.Name ?? $"(missing) {Value}";

    public void Next() => Step(+1);
    public void Previous() => Step(-1);

    private void Step(int dir)
    {
        var targets = _manager.Targets;
        if (targets.Count == 0) return;
        int i = targets.FindIndex(t => t.Id == Value);
        i = i < 0 ? 0 : (i + dir + targets.Count) % targets.Count;
        Value = targets[i].Id;
    }

    public override string Serialize() => Value ?? "";

    public override bool Deserialize(string text)
    {
        Value = text;
        return true;
    }
}

internal sealed class TriggerManager
{
    private readonly List<Trigger> _triggers = new();

    public IReadOnlyList<Trigger> All => _triggers;

    /// <summary>今選べる発動先。アクション → モジュールの ON/OFF の順</summary>
    public List<TriggerTarget> Targets =>
        Rusk.Actions.All
            .Select(a => new TriggerTarget { Id = "action:" + a.Id, Name = a.Name, Action = a })
            .Concat(Rusk.Modules.All.Where(m => m.Toggleable)
                .Select(m => new TriggerTarget { Id = "module:" + m.Id, Name = "Toggle " + m.Name, Module = m }))
            .ToList();

    public TriggerTarget Resolve(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (id.StartsWith("action:", StringComparison.Ordinal))
        {
            var action = Rusk.Actions.All.FirstOrDefault(a => a.Id == id.Substring(7));
            return action == null ? null : new TriggerTarget { Id = id, Name = action.Name, Action = action };
        }
        if (id.StartsWith("module:", StringComparison.Ordinal))
        {
            var module = Rusk.Modules.All.FirstOrDefault(m => m.Id == id.Substring(7));
            return module == null ? null : new TriggerTarget { Id = id, Name = "Toggle " + module.Name, Module = module };
        }
        return null;
    }

    public Trigger Add()
    {
        var trigger = new Trigger(this);
        trigger.Target.Value = Targets.FirstOrDefault()?.Id;
        _triggers.Add(trigger);
        return trigger;
    }

    public void Remove(Trigger trigger)
    {
        Release(trigger);
        _triggers.Remove(trigger);
    }

    /// <summary>Press モードのトリガーを実行する</summary>
    public void Fire(Trigger trigger)
    {
        var target = Resolve(trigger.Target.Value);
        if (target == null) return;
        try
        {
            if (target.Module != null) target.Module.Toggle();
            else target.Action.Invoke();
        }
        catch (Exception e)
        {
            Rusk.Log.LogError($"Trigger '{target.Id}' failed: {e}");
            Rusk.Notifications.Push(L.T("{0} でエラー", target.Name), NotifyLevel.Error);
        }
    }

    /// <summary>Hold モードのトリガーを、キーの押下状態に合わせて更新する (毎フレーム)</summary>
    public void UpdateHolds(bool ctrl, bool shift, bool alt)
    {
        foreach (var trigger in _triggers.ToArray())
        {
            if (!trigger.IsHold) { Release(trigger); continue; }

            var hk = trigger.Key.Value;
            bool down = !hk.IsNone && NewInput.IsPressed(hk.Key) && hk.ModifiersHeld(ctrl, shift, alt);

            if (down && !trigger.Holding)
            {
                var target = Resolve(trigger.Target.Value);
                if (target == null) continue;
                trigger.Holding = true;
                if (target.Module != null)
                {
                    trigger.PreviousEnabled = target.Module.Enabled;
                    target.Module.Enabled = true;
                }
            }

            if (down && trigger.Holding)
            {
                // アクションは押している間、毎フレーム実行
                var target = Resolve(trigger.Target.Value);
                if (target?.Action != null)
                {
                    try { target.Action.Invoke(); }
                    catch (Exception e)
                    {
                        Rusk.Log.LogError($"Trigger '{target.Id}' failed: {e}");
                        Release(trigger);
                        trigger.Mode.Value = 0; // 毎フレームのエラーを止める
                    }
                }
            }
            else if (!down && trigger.Holding)
            {
                Release(trigger);
            }
        }
    }

    public void ReleaseHolds()
    {
        foreach (var trigger in _triggers)
            Release(trigger);
    }

    private void Release(Trigger trigger)
    {
        if (!trigger.Holding) return;
        trigger.Holding = false;
        var target = Resolve(trigger.Target.Value);
        if (target?.Module != null)
            target.Module.Enabled = trigger.PreviousEnabled;
    }

    public void Load(IEnumerable<TriggerData> data)
    {
        ReleaseHolds();
        _triggers.Clear();
        foreach (var d in data ?? Enumerable.Empty<TriggerData>())
        {
            var trigger = new Trigger(this);
            trigger.Target.Deserialize(d.Target);
            trigger.Key.Deserialize(d.Key);
            trigger.Mode.Deserialize(d.Mode);
            _triggers.Add(trigger);
        }
    }

    public List<TriggerData> Export() =>
        _triggers.Select(t => new TriggerData
        {
            Target = t.Target.Serialize(),
            Key = t.Key.Serialize(),
            Mode = t.Mode.Serialize(),
        }).ToList();
}
