using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RusK.API;
using RusK.Mods.Model.Vrm;
using UnityEngine;
using Module = RusK.API.Module;

namespace RusK.Mods.ItemModel;

/// <summary>
/// Custom Item Model: 武器・装備品の見た目を glb に置き換える。RusK\props に置いた .glb を、装備ごとに選べる。
/// glb の読み込みは Custom Model (RusK.Mods.Model/Vrm) の部品をソースごと取り込んで使う。
/// </summary>
[RuskMod("itemmodel", "Custom Item Model", "1.1.0",
    Author = "you",
    GameVersion = "0.0.1872",
    Description = "武器・装備品の見た目を glb にする (RusK\\props に .glb を置く)")]
public sealed class ItemModelMod : RuskMod
{
    protected override void OnLoad()
    {
        VrmEnv.Ctx = Context;
        // 明るさ・影の濃さの調整は VRM (Custom Model) 用。武器・装備品はテクスチャの色そのまま、影はゲームと同じ
        MaskModes.Brightness = 1f;
        MaskModes.ShadowStrength = 1f;
        ItemModels.Load();
        var window = new ItemModelWindow();
        Context.RegisterWindow(window);
        Context.RegisterModule(new ItemModelModule(window));
        Context.RegisterModule(new ItemModelRuntimeModule());
    }

    protected override void OnUnload() => ItemModels.RestoreAll();
}

/// <summary>ON で Custom Item Model の画面を開く</summary>
public sealed class ItemModelModule : Module
{
    private readonly ItemModelWindow _window;

    public ItemModelModule(ItemModelWindow window)
        : base("CustomItemModel", Categories.Visual, "武器・装備品の見た目を glb にする画面を開く")
    {
        _window = window;
        _window.VisibleChanged += w => Enabled = w.Visible;
    }

    public override bool VisibleInArrayList => false;
    public override void OnEnable() => _window.Visible = true;
    public override void OnDisable() => _window.Visible = false;
}

/// <summary>置き換えの更新 (常に ON)</summary>
public sealed class ItemModelRuntimeModule : Module
{
    public ItemModelRuntimeModule() : base("ItemModelRuntime", Categories.Visual, "武器・装備品の置き換えの更新 (常に ON にしておいてください)")
    {
        Enabled = true;
    }

    public override bool VisibleInArrayList => false;
    public override void OnUpdate() => ItemModels.Update();
}

/// <summary>装備を選び、glb を選んで、位置・回転・大きさを調整する画面</summary>
public sealed class ItemModelWindow : RuskWindow
{
    private long _selected = -1;
    private bool _all;
    private List<(long id, string name, string type)> _catalog;
    private float _catalogAt = -999f;

    public ItemModelWindow() : base("main", "Custom Item Model", 480f, 620f)
    {
        MinWidth = 380f;
    }

    public override void Draw(WindowGui gui)
    {
        gui.Label(L.T("装備を選んでから、glb を選んでください。その装備を付けているとき (武器は持っているとき) に見た目が変わります。"),
            RuskStyle.TextDim, small: true);

        var inScene = ItemModels.InScene();
        var catalog = Catalog();
        gui.Header(L.T("装備"), L.T(_all ? "すべて" : "今シーンにある装備"));
        _all = gui.Toggle(L.T("すべての装備を表示する"), _all);
        var shown = catalog.Where(c => _all || inScene.Contains(c.id) || ItemModels.Get(c.id) != null).ToList();
        if (shown.Count == 0)
            gui.Label(L.T(_all ? "装備の一覧を取得できません" : "(戦闘フィールドで開くと、付けている装備が並びます)"), RuskStyle.TextDim, small: true);
        foreach (var c in shown)
        {
            var a = ItemModels.Get(c.id);
            string right = (a != null ? Path.GetFileNameWithoutExtension(a.File) : L.T("元の見た目")) + "  " + L.T(c.type);
            if (gui.Selectable(c.name, _selected == c.id, right)) _selected = c.id;
        }

        var sel = catalog.FirstOrDefault(c => c.id == _selected);
        if (sel.name == null) return;
        var assigned = ItemModels.Get(_selected);

        gui.Space(6f);
        gui.Header(L.T("{0} の見た目", sel.name));
        gui.BeginRow(1f, 1f);
        if (gui.Button(L.T("元の見た目にする"), enabled: assigned != null)) ItemModels.Assign(_selected, null);
        if (gui.Button(L.T("フォルダを開く"))) ItemModels.OpenFolder();
        var files = ItemModels.Files().ToList();
        if (files.Count == 0) gui.Label(L.T("RusK\\props に .glb ファイルを置いてください。"), RuskStyle.TextDim, small: true);
        foreach (var f in files)
        {
            bool mine = assigned != null && Path.GetFileName(assigned.File) == Path.GetFileName(f);
            if (gui.Selectable(Path.GetFileNameWithoutExtension(f), mine, $"{new FileInfo(f).Length / 1024} KB") && !mine)
                ItemModels.Assign(_selected, f);
        }

        if (assigned == null) return;
        gui.Space(6f);
        gui.Header(L.T("位置・回転・大きさ"), L.T("元の武器・装備品からのずれ"));
        bool changed = false;
        changed |= Tune(gui, "位置 X", ref assigned.Position.x, 0.01f);
        changed |= Tune(gui, "位置 Y", ref assigned.Position.y, 0.01f);
        changed |= Tune(gui, "位置 Z", ref assigned.Position.z, 0.01f);
        changed |= Tune(gui, "回転 X", ref assigned.Rotation.x, 5f);
        changed |= Tune(gui, "回転 Y", ref assigned.Rotation.y, 5f);
        changed |= Tune(gui, "回転 Z", ref assigned.Rotation.z, 5f);
        changed |= Tune(gui, "大きさ", ref assigned.Scale, 0.05f, 0.01f);
        if (gui.Button(L.T("ずれを戻す")))
        {
            assigned.Position = Vector3.zero;
            assigned.Rotation = Vector3.zero;
            assigned.Scale = 1f;
            changed = true;
        }

        // 発光 (ON なら glb の発光より優先。OFF で glb 自身の発光に戻る)
        gui.Space(6f);
        gui.Header(L.T("発光"), L.T("glb に発光が入っていれば OFF でもそれが光る"));
        bool glow = gui.Toggle(L.T("光らせる"), assigned.Glow);
        if (glow != assigned.Glow) { assigned.Glow = glow; changed = true; }
        if (assigned.Glow)
        {
            var colors = Assignment.GlowColors;
            int c = Mathf.Clamp(assigned.GlowColor, 0, colors.Length - 1);
            int step = gui.Stepper(L.T("色"), L.T(colors[c].name), colors[c].color);
            if (step != 0)
            {
                assigned.GlowColor = (c + step + colors.Length) % colors.Length;
                changed = true;
            }
            changed |= Tune(gui, "強さ", ref assigned.GlowStrength, 0.25f, 0f);
        }
        if (changed)
        {
            assigned.Revision++;
            ItemModels.Save();
        }
    }

    private static bool Tune(WindowGui gui, string label, ref float value, float step, float min = float.MinValue)
    {
        int s = gui.Stepper(L.T(label), value.ToString(step >= 1f ? "0" : "0.00"));
        if (s == 0) return false;
        value = Mathf.Max(min, Mathf.Round((value + s * step) * 1000f) / 1000f);
        return true;
    }

    /// <summary>ゲームの装備の一覧 (ID・表示名・種類)。10 秒に 1 回取り直す</summary>
    private List<(long id, string name, string type)> Catalog()
    {
        if (_catalog != null && Time.unscaledTime - _catalogAt < 10f) return _catalog;
        _catalogAt = Time.unscaledTime;
        var result = new List<(long, string, string)>();
        try
        {
            var util = GameUtil.Instance;
            var list = util?.GetEquipList();
            for (int i = 0; list != null && i < list.Count; i++)
            {
                var s = list[i];
                if (s == null) continue;
                string name = s.name;
                try
                {
                    var n = util.GetEquipName(s);
                    if (!string.IsNullOrWhiteSpace(n)) name = n;
                }
                catch { }
                string type = s.equipType.ToString() == "Weapon" ? "武器" : "装飾";
                result.Add(((long)Math.Round(s.equipId), name, type));
            }
        }
        catch { }
        _catalog = result.OrderBy(c => c.Item3 == "武器" ? 0 : 1).ThenBy(c => c.Item1).ToList();
        return _catalog;
    }
}
