using System.IO;
using System.Linq;
using RusK.API;
using RusK.Mods.Model.Vrm;
using RusK.Mods.Shared;
using UnityEngine;
using Module = RusK.API.Module;

namespace RusK.Mods.Model;

/// <summary>ON で Custom Model の画面を開く</summary>
public sealed class CustomModelModule : Module
{
    private readonly CustomModelWindow _window;

    public CustomModelModule(CustomModelWindow window)
        : base("CustomVRMLoader", Categories.Visual, "キャラの見た目を VRM にする画面を開く")
    {
        MovedFrom("model:CustomModel"); // 前の名前 (Custom Model) の設定を引き継ぐ
        _window = window;
        _window.VisibleChanged += w => Enabled = w.Visible;
    }

    public override bool VisibleInArrayList => false;
    public override void OnEnable() => _window.Visible = true;
    public override void OnDisable() => _window.Visible = false;
}

/// <summary>
/// キャラごとに、見た目にする VRM を選ぶ画面。
/// 選んだ VRM は、そのキャラを操作しているときに自動で付く (Party の控えのキャラも、切り替えたときに付く)
/// </summary>
public sealed class CustomModelWindow : RuskWindow
{
    private double _selected = -1;

    public CustomModelWindow() : base("main", "Custom VRM Loader", 460f, 560f)
    {
        MinWidth = 360f;
    }

    public override void Draw(WindowGui gui)
    {
        var p = PlayerRef.Current;
        var characters = ModelSwap.Characters().Where(Unlocked).ToList();
        if (_selected < 0 && p != null) _selected = p.GetPlayerId();

        gui.Label(L.T("キャラを選んでから、VRM を選んでください。そのキャラを操作しているときに見た目が VRM になります。"),
            RuskStyle.TextDim, small: true);

        gui.Header(L.T("キャラ"));
        if (characters.Count == 0) gui.Label(L.T("キャラ一覧を取得できません (ゲームに入ってから開いてください)"), RuskStyle.TextDim);
        foreach (var c in characters)
        {
            var file = VrmSwap.AssignedById((long)System.Math.Round(c.id));
            bool isCur = p != null && System.Math.Abs(p.GetPlayerId() - c.id) < 0.5;
            string right = (file != null ? Path.GetFileNameWithoutExtension(file) : L.T("元の見た目")) + (isCur ? "  " + L.T("(操作中)") : "");
            if (gui.Selectable(CharacterNames.Get(c), System.Math.Abs(_selected - c.id) < 0.5, right))
                _selected = c.id;
        }

        gui.Space(6f);
        var selected = characters.FirstOrDefault(c => System.Math.Abs(c.id - _selected) < 0.5);
        long id = (long)System.Math.Round(_selected);
        var assigned = selected != null ? VrmSwap.AssignedById(id) : null;
        gui.Header(selected != null ? L.T("{0} の見た目", CharacterNames.Get(selected)) : L.T("見た目"), null);

        gui.BeginRow(1f, 1f);
        if (gui.Button(L.T("元の見た目にする"), enabled: assigned != null)) VrmSwap.AssignById(id, null);
        if (gui.Button(L.T("フォルダを開く"))) VrmSwap.OpenModelsFolder();

        var files = VrmSwap.Files().ToList();
        if (files.Count == 0)
            gui.Label(L.T("RusK\\models に .vrm ファイルを置いてください。"), RuskStyle.TextDim, small: true);
        foreach (var f in files)
        {
            bool mine = assigned != null && Path.GetFileName(assigned) == Path.GetFileName(f);
            if (gui.Selectable(Path.GetFileNameWithoutExtension(f), mine, $"{new FileInfo(f).Length / 1024 / 1024} MB")
                && selected != null && !mine)
                VrmSwap.AssignById(id, f);
        }

        SkirtUi.Draw(gui);
    }

    private static bool Unlocked(MotionManager m)
    {
        try { return GameUtil.Instance == null || GameUtil.Instance.IsCharacterUnlock(m.id); }
        catch { return true; }
    }
}
