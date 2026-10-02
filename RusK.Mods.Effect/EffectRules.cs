using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using UnityEngine;

namespace RusK.Mods.Effect;

/// <summary>1 つの対象 (全体 / キャラ / 敵 / エフェクト) にかける見た目の変更</summary>
internal sealed class EffectRule
{
    public bool Enabled { get; set; } = true;
    /// <summary>色付きの部分の色相を Hue に変える</summary>
    public bool UseHue { get; set; }
    /// <summary>色相 (0〜360)</summary>
    public float Hue { get; set; } = 210f;
    /// <summary>白っぽい部分にも Hue の色を重ねる量 (0〜1。UseHue のときだけ)</summary>
    public float TintWhite { get; set; }
    public float Saturation { get; set; } = 1f;
    public float Brightness { get; set; } = 1f;
    public float Opacity { get; set; } = 1f;
    public float Size { get; set; } = 1f;
    public bool Hide { get; set; }
    /// <summary>差し替え先のゲームのエフェクトの名前 (無ければ null)。元のエフェクトは隠す</summary>
    public string Replace { get; set; }

    /// <summary>元のエフェクトを隠すか (表示しない・差し替え)</summary>
    public bool HidesOriginal => Hide || !string.IsNullOrEmpty(Replace);

    /// <summary>見た目を何も変えないか</summary>
    public bool IsIdentity =>
        !UseHue && !HidesOriginal && Mathf.Approximately(Saturation, 1f) && Mathf.Approximately(Brightness, 1f) &&
        Mathf.Approximately(Opacity, 1f) && Mathf.Approximately(Size, 1f);

    /// <summary>同じ見た目かを比べる用の文字列 (かけ直すかの判断に使う)</summary>
    public string Signature =>
        $"{UseHue}|{Hue:0.#}|{TintWhite:0.##}|{Saturation:0.##}|{Brightness:0.##}|{Opacity:0.##}|{Size:0.##}|{HidesOriginal}";

    /// <summary>パーティクルの色 1 つを変える</summary>
    public Color Apply(Color c)
    {
        Color.RGBToHSV(c, out float h, out float s, out float v);
        if (UseHue)
        {
            if (s >= 0.12f) h = Hue / 360f;
            else if (TintWhite > 0f)
            {
                h = Hue / 360f;
                s = Mathf.Lerp(s, 0.8f, TintWhite);
            }
        }
        s = Mathf.Clamp01(s * Saturation);
        v *= Brightness;
        var o = Color.HSVToRGB(h, s, v, true);
        o.a = Mathf.Clamp01(c.a * Opacity);
        return o;
    }

    public EffectRule Clone() => (EffectRule)MemberwiseClone();
}

/// <summary>
/// 対象ごとの設定。キーは "*" (全体)・"char:1002" (キャラ)・"enemy" (敵)・"fx:名前" (エフェクト)。
/// 細かい方が優先 (エフェクト → キャラ / 敵 → 全体)。RusK\data\effect\rules.json に保存する
/// </summary>
internal sealed class EffectRules
{
    public const string All = "*";
    public const string Enemy = "enemy";
    public static string Char(long id) => "char:" + id;
    public static string Fx(string name) => "fx:" + name;

    private readonly string _file;
    private Dictionary<string, EffectRule> _rules = new();
    private bool _dirty;
    private float _saveAt;

    /// <summary>設定が変わるたびに増える (かけ直しの判断に使う)</summary>
    public int Version { get; private set; }

    public EffectRules(string folder)
    {
        _file = Path.Combine(folder, "rules.json");
        Load();
    }

    public IReadOnlyDictionary<string, EffectRule> Rules => _rules;

    public EffectRule Get(string key) => _rules.TryGetValue(key, out var r) ? r : null;

    public EffectRule GetOrCreate(string key)
    {
        if (!_rules.TryGetValue(key, out var r)) _rules[key] = r = new EffectRule();
        return r;
    }

    public void Remove(string key)
    {
        if (_rules.Remove(key)) Changed();
    }

    /// <summary>エフェクトにかける設定 (エフェクト → 持ち主 → 全体の順に、有効なもの)</summary>
    public EffectRule Resolve(string owner, string fx)
    {
        if (fx != null && _rules.TryGetValue(Fx(fx), out var r) && r.Enabled) return r;
        if (owner != null && _rules.TryGetValue(owner, out r) && r.Enabled) return r;
        if (_rules.TryGetValue(All, out r) && r.Enabled) return r;
        return null;
    }

    /// <summary>設定を変えたら呼ぶ (少し待ってから保存する)</summary>
    public void Changed()
    {
        Version++;
        _dirty = true;
        _saveAt = Time.unscaledTime + 1f;
    }

    public void Tick()
    {
        if (_dirty && Time.unscaledTime >= _saveAt) Save();
    }

    public void Save()
    {
        _dirty = false;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(_rules, new JsonSerializerOptions { WriteIndented = true }),
                new UTF8Encoding(false));
        }
        catch (Exception e)
        {
            EffectMod.Ctx?.Log.Warning($"Effect: 設定を保存できません: {e.Message}");
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_file)) return;
            _rules = JsonSerializer.Deserialize<Dictionary<string, EffectRule>>(File.ReadAllText(_file)) ?? new();
        }
        catch (Exception e)
        {
            EffectMod.Ctx?.Log.Warning($"Effect: 設定を読めません: {e.Message}");
            _rules = new();
        }
        Version++;
    }
}
