using System.Collections.Generic;
using System.Linq;
using RusK.API;
using UnityEngine;

namespace RusK.Mods.Effect;

/// <summary>
/// Effect Tuner の設定画面。対象 (全体 / キャラ / 敵 / エフェクト) を選んで、色相・彩度・明るさ・不透明度・大きさ・非表示を変える。
/// 変えた設定は、次にそのエフェクトが出たときから効く
/// </summary>
internal sealed class EffectWindow : RuskWindow
{
    private string _owner = EffectRules.All; // 選んでいる持ち主 (エフェクト一覧の絞り込み)
    private string _target = EffectRules.All; // 編集している設定のキー

    // 差し替え先を選ぶ画面
    private bool _picking;
    private int _pickTab;   // 0: 最近出たもの、1: すべて
    private int _pickGroup; // すべて: 名前の頭 (HCFX_ など) で分けたグループ
    private int _pickPage;
    private const int PageSize = 25;

    public EffectWindow() : base("tuner", "Effect Tuner", 420f, 600f)
    {
        MinWidth = 340f;
        MinHeight = 300f;
    }

    private static EffectRules Rules => EffectHook.Rules;

    public override void Draw(WindowGui gui)
    {
        if (Rules == null) return;

        if (!EffectHook.Enabled)
            gui.Label(L.T("OFF です (Visual > EffectTuner を ON にすると効きます)"), RuskStyle.TextDim, small: true);

        // 1. 編集している対象
        gui.Header(L.T("設定"), TargetName(_target));
        DrawEditor(gui, _target);

        // 2. 対象を選ぶ
        gui.Space(6f);
        gui.Header(L.T("対象"));
        foreach (var owner in Owners())
        {
            bool has = Rules.Get(owner)?.Enabled ?? false;
            if (gui.Selectable(OwnerName(owner), _target == owner, has ? "●" : null, has ? RuskStyle.Accent : null))
            {
                _owner = owner;
                _target = owner;
            }
        }

        // 3. その持ち主で最近出たエフェクト
        gui.Space(6f);
        var seenKey = _owner == EffectRules.All ? null : _owner;
        var list = Effects(seenKey);
        gui.Header(L.T("最近出たエフェクト"), seenKey == null ? L.T("すべて") : OwnerName(seenKey));
        if (list.Count == 0)
        {
            gui.Label(L.T("まだありません。戦闘で技を出すと、ここに出ます"), RuskStyle.TextDim, small: true);
            return;
        }
        foreach (var fx in list)
        {
            var key = EffectRules.Fx(fx);
            bool has = Rules.Get(key)?.Enabled ?? false;
            if (gui.Selectable(fx, _target == key, has ? "●" : null, has ? RuskStyle.Accent : null))
                _target = key;
        }
    }

    private void DrawEditor(WindowGui gui, string key)
    {
        var rule = Rules.Get(key);
        if (rule == null)
        {
            gui.Label(key.StartsWith("fx:") ? L.T("このエフェクトだけの設定はありません (キャラや全体の設定がかかります)")
                : L.T("この対象の設定はありません"), RuskStyle.TextDim, small: true);
            if (gui.Button(L.T("設定を作る"), accent: true))
            {
                Rules.GetOrCreate(key);
                Rules.Changed();
            }
            return;
        }

        bool changed = false;
        bool enabled = gui.Toggle(L.T("この設定を使う"), rule.Enabled);
        if (enabled != rule.Enabled) { rule.Enabled = enabled; changed = true; }

        gui.BeginRow(1f, 1f);
        bool useHue = gui.Toggle(L.T("色を変える"), rule.UseHue);
        if (useHue != rule.UseHue) { rule.UseHue = useHue; changed = true; }
        // 色の見本
        var swatch = gui.Next(gui.LineHeight);
        gui.Box(swatch, rule.UseHue ? Color.HSVToRGB(rule.Hue / 360f, 0.8f, 1f) : RuskStyle.Track, 4f);

        if (rule.UseHue)
        {
            changed |= Slider(gui, "hue", L.T("色相"), v => rule.Hue = v, rule.Hue, 0f, 360f, "0");
            changed |= Slider(gui, "tint", L.T("白い部分にも色をつける"), v => rule.TintWhite = v, rule.TintWhite, 0f, 1f, "0%");
        }
        changed |= Slider(gui, "sat", L.T("彩度"), v => rule.Saturation = v, rule.Saturation, 0f, 2f, "0%");
        changed |= Slider(gui, "bri", L.T("明るさ"), v => rule.Brightness = v, rule.Brightness, 0f, 3f, "0%");
        changed |= Slider(gui, "opa", L.T("不透明度"), v => rule.Opacity = v, rule.Opacity, 0f, 1f, "0%");
        changed |= Slider(gui, "size", L.T("大きさ"), v => rule.Size = v, rule.Size, 0.2f, 3f, "0.00x");
        bool hide = gui.Toggle(L.T("表示しない"), rule.Hide);
        if (hide != rule.Hide) { rule.Hide = hide; changed = true; }

        // 差し替え (ゲームの別のエフェクトに)
        gui.BeginRow(2.2f, 1f, 1f);
        gui.Label(L.T("差し替え: {0}", string.IsNullOrEmpty(rule.Replace) ? L.T("なし") : rule.Replace),
            string.IsNullOrEmpty(rule.Replace) ? RuskStyle.TextDim : RuskStyle.Text);
        if (gui.Button(_picking ? L.T("閉じる") : L.T("選ぶ"))) _picking = !_picking;
        if (gui.Button(L.T("外す"), enabled: !string.IsNullOrEmpty(rule.Replace))) { rule.Replace = null; changed = true; }
        if (_picking && DrawPicker(gui, out var picked))
        {
            rule.Replace = picked;
            _picking = false;
            changed = true;
        }

        if (gui.Button(L.T("この設定を消す")))
        {
            Rules.Remove(key);
            return;
        }
        gui.Label(L.T("次にエフェクトが出たときから変わります"), RuskStyle.TextDim, small: true);
        if (changed) Rules.Changed();
    }

    /// <summary>差し替え先を選ぶ。選んだら true</summary>
    private bool DrawPicker(WindowGui gui, out string picked)
    {
        picked = null;
        _pickTab = gui.Tabs(new[] { L.T("最近出たもの"), L.T("すべて") }, _pickTab);
        List<string> names;
        if (_pickTab == 0)
        {
            names = Effects(null);
            if (names.Count == 0) gui.Label(L.T("まだありません。戦闘で技を出すと、ここに出ます"), RuskStyle.TextDim, small: true);
        }
        else
        {
            var all = EffectReplacer.Names;
            var groups = all.Select(Group).Distinct().ToList();
            if (groups.Count == 0) { gui.Label(L.T("エフェクトの一覧を読めませんでした"), RuskStyle.TextDim, small: true); return false; }
            _pickGroup = Mathf.Clamp(_pickGroup, 0, groups.Count - 1);
            int g = gui.Stepper(L.T("グループ"), $"{groups[_pickGroup]} ({_pickGroup + 1}/{groups.Count})");
            if (g != 0) { _pickGroup = (_pickGroup + g + groups.Count) % groups.Count; _pickPage = 0; }
            names = all.Where(n => Group(n) == groups[_pickGroup]).ToList();
            int pages = Mathf.Max(1, (names.Count + PageSize - 1) / PageSize);
            _pickPage = Mathf.Clamp(_pickPage, 0, pages - 1);
            if (pages > 1)
            {
                int p = gui.Stepper(L.T("ページ"), $"{_pickPage + 1}/{pages}");
                _pickPage = Mathf.Clamp(_pickPage + p, 0, pages - 1);
            }
            names = names.Skip(_pickPage * PageSize).Take(PageSize).ToList();
        }
        foreach (var n in names)
        {
            if (gui.Selectable("  " + n, false))
            {
                picked = n;
                return true;
            }
        }
        return false;
    }

    /// <summary>名前の頭 (最初の _ まで)。HCFX_Beam_01 → HCFX</summary>
    private static string Group(string name)
    {
        int i = name.IndexOf('_');
        return i > 0 ? name.Substring(0, i) : name;
    }

    private static bool Slider(WindowGui gui, string id, string label, System.Action<float> set, float value, float min, float max, string format)
    {
        float v = gui.Slider(id, label, value, min, max, format);
        if (Mathf.Approximately(v, value)) return false;
        set(v);
        return true;
    }

    /// <summary>対象の一覧: 全体、出てきたキャラ・敵、設定のあるキャラ</summary>
    private static List<string> Owners()
    {
        var list = new List<string> { EffectRules.All };
        foreach (var k in EffectHook.Seen.Keys.Concat(Rules.Rules.Keys))
            if (k.StartsWith("char:") || k == EffectRules.Enemy)
                if (!list.Contains(k)) list.Add(k);
        return list;
    }

    /// <summary>エフェクトの一覧 (owner が null なら、すべての持ち主の分と、設定のあるもの)</summary>
    private static List<string> Effects(string owner)
    {
        if (owner != null)
            return EffectHook.Seen.TryGetValue(owner, out var l) ? l.ToList() : new List<string>();
        var all = new List<string>();
        foreach (var l in EffectHook.Seen.Values)
        foreach (var fx in l)
            if (!all.Contains(fx)) all.Add(fx);
        foreach (var k in Rules.Rules.Keys)
            if (k.StartsWith("fx:") && !all.Contains(k.Substring(3))) all.Add(k.Substring(3));
        return all;
    }

    private static string OwnerName(string key)
    {
        if (key == EffectRules.All) return L.T("全体");
        if (key == EffectRules.Enemy) return L.T("敵");
        return EffectHook.OwnerNames.TryGetValue(key, out var n) ? n : key;
    }

    private static string TargetName(string key) =>
        key.StartsWith("fx:") ? key.Substring(3) : OwnerName(key);
}
