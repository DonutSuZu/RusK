using System;
using System.Collections.Generic;
using System.Linq;
using RusK.API;
using UnityEngine;

namespace RusK.Core.UI;

/// <summary>
/// 設定 1 行分の操作をまとめたもの。TabGUI (キーボード) と ClickGUI (マウス) が同じ Row を使う。
/// Adjust = ←/→ や右クリック、Activate = Enter や左クリック。
/// </summary>
internal abstract class Row
{
    public abstract string Label { get; }
    public abstract string Value { get; }
    public virtual string Description => "";

    /// <summary>スライダーで表せる行は 0～1 の値を返す (それ以外は null)</summary>
    public virtual float? Normalized { get => null; set { } }

    /// <summary>色見本を出す行はその色を返す</summary>
    public virtual Color? Swatch => null;

    public virtual bool Capturing => false;

    public virtual void Adjust(int dir) { }
    public virtual void Activate() => Adjust(+1);

    /// <summary>Setting から GUI 用の Row を作る。色の設定は H/S/V の 3 行になる</summary>
    public static IEnumerable<Row> For(Setting setting) => setting switch
    {
        BoolSetting b => new Row[] { new BoolRow(b) },
        FloatSetting f => new Row[] { new FloatRow(f) },
        IntSetting i => new Row[] { new IntRow(i) },
        HotkeySetting h => new Row[] { new HotkeyRow(h) },
        ButtonSetting btn => new Row[] { new ActionRow(btn.Name, btn.Click, btn.Description) },
        ColorSetting c => new Row[] { new ColorRow(c, 0), new ColorRow(c, 1), new ColorRow(c, 2) },
        ICycleSetting cyc => new Row[] { new CycleRow(setting, cyc) },
        _ => new Row[] { new InfoRow(setting) },
    };

    public static List<Row> For(IEnumerable<Setting> settings) => settings.SelectMany(For).ToList();
}

internal sealed class BoolRow : Row
{
    private readonly BoolSetting _s;
    public BoolRow(BoolSetting s) => _s = s;
    public override string Label => _s.Name;
    public override string Value => _s.DisplayValue;
    public override string Description => _s.Description;
    public override void Adjust(int dir) => _s.Value = !_s.Value;
}

internal sealed class FloatRow : Row
{
    private readonly FloatSetting _s;
    public FloatRow(FloatSetting s) => _s = s;
    public override string Label => _s.Name;
    public override string Value => _s.DisplayValue;
    public override string Description => _s.Description;
    public override float? Normalized { get => _s.Normalized; set { if (value.HasValue) _s.Normalized = value.Value; } }

    public override void Adjust(int dir)
    {
        float step = _s.Step > 0f ? _s.Step : (_s.Max - _s.Min) / 50f;
        _s.Value += step * dir;
    }

    public override void Activate() { } // Enter では何もしない (←/→ で調整)
}

internal sealed class IntRow : Row
{
    private readonly IntSetting _s;
    public IntRow(IntSetting s) => _s = s;
    public override string Label => _s.Name;
    public override string Value => _s.DisplayValue;
    public override string Description => _s.Description;
    public override float? Normalized { get => _s.Normalized; set { if (value.HasValue) _s.Normalized = value.Value; } }
    public override void Adjust(int dir) => _s.Value += dir;
    public override void Activate() { }
}

internal sealed class CycleRow : Row
{
    private readonly Setting _s;
    private readonly ICycleSetting _cycle;

    public CycleRow(Setting s, ICycleSetting cycle)
    {
        _s = s;
        _cycle = cycle;
    }

    public override string Label => _s.Name;
    public override string Value => _s.DisplayValue;
    public override string Description => _s.Description;

    public override void Adjust(int dir)
    {
        if (dir >= 0) _cycle.Next();
        else _cycle.Previous();
    }
}

/// <summary>Enter / クリックで「次に押したキー」を割り当てる行</summary>
internal sealed class HotkeyRow : Row
{
    private readonly HotkeySetting _s;
    public HotkeyRow(HotkeySetting s) => _s = s;
    public override string Label => _s.Name;
    public override string Value => Capturing ? "[ press a key ]" : _s.DisplayValue;
    public override string Description => _s.Description + " (Esc: キャンセル / Del: 解除)";
    public override bool Capturing => HotkeyCapture.IsCapturing(this);
    public override void Adjust(int dir) { }
    public override void Activate() => HotkeyCapture.Begin(this, hk => _s.Value = hk);
}

/// <summary>押すと処理を実行する行 (ボタン)</summary>
internal sealed class ActionRow : Row
{
    private readonly string _label;
    private readonly Action _action;
    private readonly string _description;

    public ActionRow(string label, Action action, string description = "")
    {
        _label = label;
        _action = action;
        _description = description;
    }

    public override string Label => _label;
    public override string Value => "▶";
    public override string Description => _description;
    public override void Adjust(int dir) { }
    public override void Activate() => _action();
}

/// <summary>色の 1 チャンネル (0: 色相, 1: 彩度, 2: 明度) をスライダーで編集する行</summary>
internal sealed class ColorRow : Row
{
    private static readonly string[] Names = { "Hue", "Sat", "Bright" };
    private readonly ColorSetting _s;
    private readonly int _channel;

    public ColorRow(ColorSetting s, int channel)
    {
        _s = s;
        _channel = channel;
    }

    public override string Label => $"{_s.Name} {Names[_channel]}";
    public override string Value => _channel == 0 ? _s.DisplayValue : $"{Mathf.RoundToInt(Get() * 100f)}%";
    public override string Description => _s.Description;
    public override Color? Swatch => _s.Value;

    public override float? Normalized { get => Get(); set { if (value.HasValue) Set(value.Value); } }

    public override void Adjust(int dir) => Set(Get() + 0.02f * dir);
    public override void Activate() { }

    private float Get() => _channel switch { 0 => _s.H, 1 => _s.S, _ => _s.V };

    private void Set(float v)
    {
        switch (_channel)
        {
            case 0: _s.H = Mathf.Repeat(v, 1f); break;
            case 1: _s.S = v; break;
            default: _s.V = v; break;
        }
    }
}

/// <summary>値を表示するだけの行</summary>
internal sealed class InfoRow : Row
{
    private readonly Setting _s;
    public InfoRow(Setting s) => _s = s;
    public override string Label => _s.Name;
    public override string Value => _s.DisplayValue;
    public override string Description => _s.Description;
}

/// <summary>任意の値を ←/→ で切り替える行 (プロファイル選択など)</summary>
internal sealed class FuncRow : Row
{
    private readonly string _label;
    private readonly Func<string> _value;
    private readonly Action<int> _adjust;
    private readonly string _description;

    public FuncRow(string label, Func<string> value, Action<int> adjust, string description = "")
    {
        _label = label;
        _value = value;
        _adjust = adjust;
        _description = description;
    }

    public override string Label => _label;
    public override string Value => _value();
    public override string Description => _description;
    public override void Adjust(int dir) => _adjust(dir);
}
