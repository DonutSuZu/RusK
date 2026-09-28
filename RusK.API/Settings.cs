using System;
using System.Globalization;
using UnityEngine;

namespace RusK.API;

/// <summary>モジュールの設定項目。ClickGUI / TabGUI で編集でき、Config に保存される</summary>
public abstract class Setting
{
    protected Setting(string name, string description)
    {
        Name = name;
        Description = description ?? "";
    }

    public string Name { get; }
    public string Description { get; }

    /// <summary>false の設定 (ボタンなど) は Config に保存しない</summary>
    public virtual bool Persistent => true;

    public event Action Changed;
    protected void RaiseChanged() => Changed?.Invoke();

    /// <summary>GUI に表示する値の文字列</summary>
    public abstract string DisplayValue { get; }

    public abstract string Serialize();
    public abstract bool Deserialize(string text);
    public abstract void ResetToDefault();
}

/// <summary>左右キー / クリックで選択肢を前後に切り替えられる設定</summary>
public interface ICycleSetting
{
    void Next();
    void Previous();
}

public abstract class Setting<T> : Setting
{
    private T _value;

    protected Setting(string name, string description) : base(name, description) { }

    public T Default { get; protected set; }

    public T Value
    {
        get => _value;
        set
        {
            var coerced = Coerce(value);
            if (Equals(_value, coerced)) return;
            _value = coerced;
            RaiseChanged();
        }
    }

    protected virtual T Coerce(T value) => value;

    public override string DisplayValue => Convert.ToString(Value, CultureInfo.InvariantCulture);
    public override void ResetToDefault() => Value = Default;

    public static implicit operator T(Setting<T> setting) => setting.Value;
}

public sealed class BoolSetting : Setting<bool>
{
    public BoolSetting(string name, bool defaultValue, string description = "") : base(name, description)
    {
        Default = defaultValue;
        Value = defaultValue;
    }

    public override string DisplayValue => Value ? "ON" : "OFF";
    public override string Serialize() => Value ? "true" : "false";

    public override bool Deserialize(string text)
    {
        if (!bool.TryParse(text, out var v)) return false;
        Value = v;
        return true;
    }
}

public sealed class FloatSetting : Setting<float>
{
    public FloatSetting(string name, float defaultValue, float min, float max, float step = 0.1f,
        string format = "0.##", string description = "") : base(name, description)
    {
        Min = min;
        Max = max;
        Step = step;
        Format = format;
        Default = Coerce(defaultValue);
        Value = Default;
    }

    public float Min { get; }
    public float Max { get; }
    public float Step { get; }
    public string Format { get; }

    /// <summary>0～1 の割合 (スライダー描画用)</summary>
    public float Normalized
    {
        get => Max > Min ? (Value - Min) / (Max - Min) : 0f;
        set => Value = Min + Mathf.Clamp01(value) * (Max - Min);
    }

    protected override float Coerce(float value)
    {
        if (Step > 0f) value = Min + (float)Math.Round((value - Min) / Step) * Step;
        return Mathf.Clamp(value, Min, Max);
    }

    public override string DisplayValue => Value.ToString(Format, CultureInfo.InvariantCulture);
    public override string Serialize() => Value.ToString("R", CultureInfo.InvariantCulture);

    public override bool Deserialize(string text)
    {
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) return false;
        Value = v;
        return true;
    }
}

public sealed class IntSetting : Setting<int>
{
    public IntSetting(string name, int defaultValue, int min, int max, string description = "")
        : base(name, description)
    {
        Min = min;
        Max = max;
        Default = Coerce(defaultValue);
        Value = Default;
    }

    public int Min { get; }
    public int Max { get; }

    public float Normalized
    {
        get => Max > Min ? (Value - Min) / (float)(Max - Min) : 0f;
        set => Value = Min + (int)Math.Round(Mathf.Clamp01(value) * (Max - Min));
    }

    protected override int Coerce(int value) => Math.Clamp(value, Min, Max);
    public override string Serialize() => Value.ToString(CultureInfo.InvariantCulture);

    public override bool Deserialize(string text)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)) return false;
        Value = v;
        return true;
    }
}

/// <summary>選択肢から 1 つ選ぶ設定。クリック / → で次、右クリック / ← で前の選択肢</summary>
public sealed class ModeSetting : Setting<int>, ICycleSetting
{
    public ModeSetting(string name, string[] options, int defaultIndex = 0, string description = "")
        : base(name, description)
    {
        if (options == null || options.Length == 0) throw new ArgumentException("options must not be empty");
        Options = options;
        Default = Coerce(defaultIndex);
        Value = Default;
    }

    public string[] Options { get; }
    public string Selected => Options[Value];

    public void Next() => Value = (Value + 1) % Options.Length;
    public void Previous() => Value = (Value - 1 + Options.Length) % Options.Length;

    protected override int Coerce(int value) => Math.Clamp(value, 0, Options.Length - 1);
    public override string DisplayValue => Selected;
    public override string Serialize() => Selected;

    public override bool Deserialize(string text)
    {
        int i = Array.IndexOf(Options, text);
        if (i < 0) return false;
        Value = i;
        return true;
    }
}

/// <summary>ホットキー (修飾キー付き)。GUI でクリック / Enter すると次に押したキーを割り当てる</summary>
public sealed class HotkeySetting : Setting<Hotkey>
{
    public HotkeySetting(string name, Hotkey defaultValue = default, string description = "")
        : base(name, description)
    {
        Default = defaultValue;
        Value = defaultValue;
    }

    public override string DisplayValue => Value.Display;
    public override string Serialize() => Value.ToString();

    public override bool Deserialize(string text)
    {
        if (!Hotkey.TryParse(text, out var v)) return false;
        Value = v;
        return true;
    }
}

/// <summary>
/// 色の設定。内部では HSV で持つので、GUI では色相・彩度・明度のスライダーとして編集できる。
/// Config には "#RRGGBB" で保存される。
/// </summary>
public sealed class ColorSetting : Setting
{
    private float _h, _s, _v;

    public ColorSetting(string name, string defaultHex, string description = "") : base(name, description)
    {
        DefaultHex = defaultHex;
        Deserialize(defaultHex);
    }

    public string DefaultHex { get; }

    public float H { get => _h; set => Set(ref _h, value); }
    public float S { get => _s; set => Set(ref _s, value); }
    public float V { get => _v; set => Set(ref _v, value); }

    public Color Value => Render.Hsv(_h, _s, _v);

    public override string DisplayValue => Render.ToHex(Value);
    public override string Serialize() => Render.ToHex(Value);

    public override bool Deserialize(string text)
    {
        if (!Render.TryParseHex(text, out var color)) return false;
        Render.RgbToHsv(color, out _h, out _s, out _v);
        RaiseChanged();
        return true;
    }

    public override void ResetToDefault() => Deserialize(DefaultHex);

    private void Set(ref float field, float value)
    {
        value = Mathf.Clamp01(value);
        if (field == value) return;
        field = value;
        RaiseChanged();
    }
}

/// <summary>押すと処理を実行するボタン。値は持たず、Config にも保存されない</summary>
public sealed class ButtonSetting : Setting
{
    private readonly Action _onClick;

    public ButtonSetting(string name, Action onClick, string description = "") : base(name, description)
    {
        _onClick = onClick ?? throw new ArgumentNullException(nameof(onClick));
    }

    public override bool Persistent => false;
    public override string DisplayValue => "▶";

    public void Click() => _onClick();

    public override string Serialize() => "";
    public override bool Deserialize(string text) => false;
    public override void ResetToDefault() { }
}
