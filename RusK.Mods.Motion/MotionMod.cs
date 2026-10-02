using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Cinemachine;
using HarmonyLib;
using RusK.API;
using RusK.Mods.Shared;
using UnityEngine;

namespace RusK.Mods.Motion;

/// <summary>
/// Custom Motion: RusK\motions の glb / vrma (Blender などで作ったアニメーション、VRM のアニメーション) を、ゲームのキャラの動きとして再生する。
/// 骨は名前で対応させる (Model Lab の「骨格を glb で書き出す」で書き出した骨格で作った動きなら、そのまま使える)
/// </summary>
[RuskMod("motion", "Custom Motion", "1.0.0",
    Author = "you",
    GameVersion = "0.0.1878",
    Description = "Blender などで作ったアニメーション (glb / vrma) を、キャラの動きとして再生する。待機・攻撃などの動作に割り当てられる")]
public sealed class MotionMod : RuskMod
{
    internal static IModContext Ctx;

    protected override void OnLoad()
    {
        Ctx = Context;
        var window = new MotionWindow();
        Context.RegisterWindow(window);
        Context.RegisterModule(new MotionModule(window));
        Context.Harmony.PatchAll(typeof(MotionCameraPatch));
        Context.Harmony.PatchAll(typeof(MotionBrainPatch));
    }

    protected override void OnUnload() => MotionPlayers.StopAll(immediately: true);

    /// <summary>動きを置くフォルダ (RusK\motions)</summary>
    internal static string Folder =>
        Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Ctx.DataDirectory)!, "..", "motions"));

    /// <summary>フォルダの glb を全部読む (ファイルごとにアニメーションの一覧)</summary>
    internal static List<GltfMotion> LoadAll()
    {
        var list = new List<GltfMotion>();
        try
        {
            Directory.CreateDirectory(Folder);
            // glb (Blender などで書き出したもの) と vrma (VRM のアニメーション。中身は glb)
            foreach (var f in Directory.GetFiles(Folder, "*.*", SearchOption.AllDirectories)
                         .Where(f => f.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".vrma", StringComparison.OrdinalIgnoreCase))
                         .OrderBy(f => f))
            {
                try { list.AddRange(GltfMotion.Load(f)); }
                catch (Exception e) { Ctx.Log.Warning($"Motion: {Path.GetFileName(f)} を読めません: {e.Message}"); }
            }
        }
        catch (Exception e) { Ctx.Log.Warning($"Motion: motions フォルダを読めません: {e.Message}"); }
        return list;
    }

    internal static void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            Process.Start("explorer.exe", $"\"{Folder}\"");
        }
        catch (Exception e) { Ctx.Log.Warning($"Motion: フォルダを開けません: {e.Message}"); }
    }

    /// <summary>見せるためのモデル (タイトル画面・キャラクター画面) の根元</summary>
    internal static IEnumerable<Transform> ShowModels()
    {
        foreach (var s in Resources.FindObjectsOfTypeAll<CharacterShowController>())
            if (s != null && s.gameObject.scene.name != null && s.gameObject.activeInHierarchy) yield return s.transform;
    }
}

internal sealed class MotionModule : Module
{
    public MotionModule(MotionWindow window) : base("CustomMotion", "Visual", "Blender などで作ったアニメーション (glb / vrma) を、キャラの動きとして再生する。待機・攻撃などの動作に割り当てられる")
    {
        AddSetting(new ButtonSetting("OpenWindow", () => window.Visible = true, "画面を開く"));
        AddSetting(new ButtonSetting("OpenFolder", MotionMod.OpenFolder, "motions フォルダを開く"));
        Enabled = true;
    }

    public override void OnDisable() => MotionPlayers.StopAll();
}

/// <summary>
/// 動きの画面:
/// 1. 動きを押すと、操作キャラと画面に見せるキャラでその場で再生 (試し用)
/// 2. 「このキャラの動作」から動作を選んでから動きを押すと、その動作に割り当てる (その動作のときに自動で再生)
/// </summary>
internal sealed class MotionWindow : RuskWindow
{
    private string _selected; // 割り当てる先のゲームの動作の名前

    public MotionWindow() : base("motion", "Custom Motion", 400f, 560f)
    {
        MinWidth = 320f;
        MinHeight = 260f;
    }

    public override void Draw(WindowGui gui)
    {
        var motions = MotionLibrary.All;
        var p = PlayerRef.Current;
        gui.BeginRow(1f, 1f, 1f);
        if (gui.Button(L.T("止める"), enabled: MotionPlayers.Active.Count > 0)) MotionPlayers.StopAll();
        if (gui.Button(L.T("読み込み直す"))) MotionLibrary.Reload();
        if (gui.Button(L.T("フォルダを開く"))) MotionMod.OpenFolder();

        // glb の動き
        gui.Header(L.T("動き"), _selected != null ? L.T("押すと「{0}」に割り当て", _selected) : L.T("押すとその場で再生"));
        if (motions.Count == 0) gui.Label(L.T("RusK/motions にアニメーション入りの glb がありません"), RuskStyle.TextDim, small: true);
        foreach (var m in motions)
        {
            if (!gui.Selectable(m.Name, false, $"{Path.GetFileName(m.File)}  {m.Length:0.0}s")) continue;
            if (_selected != null && p != null)
            {
                MotionBinder.Set(CharId(p), _selected, m);
                _selected = null;
                continue;
            }
            if (p != null) MotionPlayers.Play(p.transform, m).Manual = true;
            foreach (var show in MotionMod.ShowModels()) MotionPlayers.Play(show, m).Manual = true;
        }

        // 操作キャラの動作と、割り当て
        gui.Space(6f);
        gui.Header(L.T("このキャラの動作"), p != null ? CharacterNames.Get(p.GetPlayerId()) : null);
        if (p == null || p.m_motionMgr?.motions == null)
        {
            gui.Label(L.T("ゲームに入ってから開いてください"), RuskStyle.TextDim, small: true);
            return;
        }
        gui.Label(L.T("動作を選んでから、上の動きを押すと割り当てます。その動作のときに自動で再生します"), RuskStyle.TextDim, small: true);
        if (_selected != null)
        {
            gui.BeginRow(1f, 1f);
            if (gui.Button(L.T("割り当てを外す"), enabled: MotionBinder.Get(CharId(p), _selected) != null))
            {
                MotionBinder.Set(CharId(p), _selected, null);
                _selected = null;
            }
            if (gui.Button(L.T("選ぶのをやめる"))) _selected = null;
            if (_selected != null) DrawHit(gui, p);
        }
        var current = p.m_animController?.m_animMotion?.name;
        var seen = new HashSet<string>();
        foreach (var m in p.m_motionMgr.motions)
        {
            if (m == null || string.IsNullOrEmpty(m.name) || !seen.Add(m.name)) continue;
            var bound = MotionBinder.Get(CharId(p), m.name);
            string right = (bound != null ? "★ " + bound.Substring(bound.IndexOf('#') + 1) : "") + (m.name == current ? "  " + L.T("(今)") : "");
            if (gui.Selectable(m.name, _selected == m.name, right, bound != null ? RuskStyle.Accent : null))
                _selected = _selected == m.name ? null : m.name;
        }
    }

    /// <summary>選んだ動作の割り当ての、当たる瞬間の調整 (攻撃の動作だけ)</summary>
    private void DrawHit(WindowGui gui, PlayerController p)
    {
        long id = CharId(p);
        var bound = MotionBinder.Get(id, _selected);
        if (bound == null) return;
        var (key, hit) = MotionBinder.Parse(bound);
        var glb = MotionLibrary.Find(key);
        MotionState state = null;
        foreach (var m in p.m_motionMgr.motions)
            if (m != null && m.name == _selected) { state = m; break; }
        var gameHit = MotionBinder.GameHit(state);
        if (gameHit == null)
        {
            gui.Label(L.T("この動作には攻撃判定がありません (動き全体を動作の長さに合わせます)"), RuskStyle.TextDim, small: true);
            return;
        }
        string progress = gameHit.Value.ToString("0.00"), seconds = (gameHit.Value * MotionBinder.ClipSeconds(state)).ToString("0.00");
        gui.Label(L.T("ゲームの攻撃判定: 進み具合 {0} ({1} 秒目)", progress, seconds), RuskStyle.TextDim, small: true);
        if (glb == null) return;
        float cur = hit ?? glb.AutoHit;
        float v = gui.Slider("hit", hit == null ? L.T("当たる瞬間 (自動)") : L.T("当たる瞬間"), cur, 0f, glb.Length, "0.00s");
        if (Mathf.Abs(v - cur) > 0.005f)
        {
            MotionBinder.SetHit(id, _selected, v);
            Preview(p, glb, v);
        }
        gui.BeginRow(1f, 1f, 1f);
        if (gui.Button(L.T("止めて見る"))) Preview(p, glb, cur);
        if (gui.Button(L.T("自動に戻す"), enabled: hit != null)) MotionBinder.SetHit(id, _selected, null);
        if (gui.Button(L.T("見るのをやめる"))) MotionPlayers.StopAll();
    }

    /// <summary>動きを time 秒で止めて、操作キャラと画面に見せるキャラで見せる (当たる瞬間の確認)</summary>
    private static void Preview(PlayerController p, GltfMotion m, float time)
    {
        foreach (var root in MotionMod.ShowModels().Prepend(p.transform))
        {
            if (MotionPlayers.Active.TryGetValue(root.Pointer, out var list))
            {
                var ex = list.LastOrDefault(x => x.Manual && x.Motion == m && x.Target > 0f);
                if (ex != null) { ex.Hold = time; continue; }
            }
            var player = MotionPlayers.Play(root, m);
            player.Manual = true;
            player.Hold = time;
        }
    }

    private static long CharId(PlayerController p) => (long)System.Math.Round(p.GetPlayerId());
}

// アニメーションの後、VRM に動きを写す前 (Custom Model のパッチより先) に上書きする
[HarmonyPatch(typeof(CameraController), nameof(CameraController.LateUpdate))]
internal static class MotionCameraPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Postfix() => MotionPlayers.Tick();
}

// タイトル画面などでは CameraController が動かないので、Cinemachine の後でも
[HarmonyPatch(typeof(CinemachineBrain), "LateUpdate")]
internal static class MotionBrainPatch
{
    [HarmonyPriority(Priority.First)]
    private static void Postfix() => MotionPlayers.Tick();
}
