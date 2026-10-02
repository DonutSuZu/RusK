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
/// Custom Motion (試作): RusK\motions の glb (Blender などで作ったアニメーション) を、ゲームのキャラの動きとして再生する。
/// 骨は名前で対応させる (Model Lab の「骨格を glb で書き出す」で書き出した骨格で作った動きなら、そのまま使える)
/// </summary>
[RuskMod("motion", "Custom Motion", "0.1.0",
    Author = "you",
    GameVersion = "0.0.1878",
    Description = "Blender などで作ったアニメーション (glb) を、キャラの動きとして再生する (試作)")]
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

    protected override void OnUnload() => MotionPlayers.StopAll();

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
            foreach (var f in Directory.GetFiles(Folder, "*.glb", SearchOption.AllDirectories).OrderBy(f => f))
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
    public MotionModule(MotionWindow window) : base("CustomMotion", "Visual", "Blender などで作ったアニメーション (glb) を、キャラの動きとして再生する (試作)")
    {
        AddSetting(new ButtonSetting("OpenWindow", () => window.Visible = true, "画面を開く"));
        AddSetting(new ButtonSetting("OpenFolder", MotionMod.OpenFolder, "motions フォルダを開く"));
        Enabled = true;
    }

    public override void OnDisable() => MotionPlayers.StopAll();
}

/// <summary>動きを選んで再生する画面 (試作)</summary>
internal sealed class MotionWindow : RuskWindow
{
    private List<GltfMotion> _motions;

    public MotionWindow() : base("motion", "Custom Motion", 380f, 460f)
    {
        MinWidth = 300f;
        MinHeight = 240f;
    }

    public override void Draw(WindowGui gui)
    {
        _motions ??= MotionMod.LoadAll();
        gui.Label(L.T("RusK/motions の glb の動きを再生します (試作)。押すと、操作キャラと画面に見せるキャラで再生します"), RuskStyle.TextDim, small: true);
        gui.BeginRow(1f, 1f, 1f);
        if (gui.Button(L.T("止める"), enabled: MotionPlayers.Active.Count > 0)) MotionPlayers.StopAll();
        if (gui.Button(L.T("読み込み直す"))) _motions = MotionMod.LoadAll();
        if (gui.Button(L.T("フォルダを開く"))) MotionMod.OpenFolder();

        gui.Header(L.T("動き"));
        if (_motions.Count == 0) gui.Label(L.T("RusK/motions にアニメーション入りの glb がありません"), RuskStyle.TextDim, small: true);
        foreach (var m in _motions)
        {
            if (!gui.Selectable($"{m.Name}", false, $"{Path.GetFileName(m.File)}  {m.Length:0.0}s")) continue;
            var p = PlayerRef.Current;
            if (p != null) MotionPlayers.Play(p.transform, m);
            foreach (var show in MotionMod.ShowModels()) MotionPlayers.Play(show, m);
        }
    }
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
