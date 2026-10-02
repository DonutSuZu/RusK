using System;
using System.Diagnostics;
using System.IO;
using RusK.API;

namespace RusK.Mods.Effect;

/// <summary>
/// Effect Tuner: 技やヒットのエフェクトの色・明るさ・大きさを、全体・キャラ・敵・エフェクトごとに変える。
/// ゲームのエフェクトはほぼ全部 ParticleSystem で、色はパーティクルの startColor / colorOverLifetime で付いているので、そこを変える
/// </summary>
[RuskMod("effect", "Effect Tuner", "1.0.0",
    Author = "you",
    GameVersion = "0.0.1878",
    Description = "技やヒットのエフェクトの色・明るさ・大きさを変える。ゲームの別のエフェクトや自作エフェクト (RusK/effects) に差し替えもできる")]
public sealed class EffectMod : RuskMod
{
    internal static IModContext Ctx;

    protected override void OnLoad()
    {
        Ctx = Context;
        EffectHook.Rules = new EffectRules(Context.DataDirectory);
        // RusK\data\effect → RusK\effects
        EffectReplacer.Folder = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Context.DataDirectory)!, "..", "effects"));
        var window = new EffectWindow();
        Context.RegisterWindow(window);
        Context.RegisterModule(new EffectModule(window));
        Context.RegisterAction("EffectTunerWindow", () => window.Toggle(), "Effect Tuner の画面を開く / 閉じる");

        Context.Harmony.PatchAll(typeof(LoadEffectOnTransformPatch));
        Context.Harmony.PatchAll(typeof(LoadEffectAtPositionPatch));
        Context.Harmony.PatchAll(typeof(LoadEffectByPathPatch));
        Context.Harmony.PatchAll(typeof(PlayerEffectScope));
        Context.Harmony.PatchAll(typeof(PlayerDefenceScope));
        Context.Harmony.PatchAll(typeof(PlayerTrailScope));
        Context.Harmony.PatchAll(typeof(EnemyHitScope));
        Context.Harmony.PatchAll(typeof(EnemyEffectScope));
    }

    protected override void OnUnload()
    {
        EffectHook.Enabled = false;
        EffectHook.Rules?.Save();
    }
}

/// <summary>メニューの Visual > EffectTuner (ON の間、設定がかかる)</summary>
internal sealed class EffectModule : Module
{
    private readonly EffectWindow _window;

    public EffectModule(EffectWindow window)
        : base("EffectTuner", "Visual", "技やヒットのエフェクトの色・明るさ・大きさを変える")
    {
        _window = window;
        AddSetting(new ButtonSetting("OpenWindow", () => _window.Visible = true, "設定画面を開く"));
        AddSetting(new ButtonSetting("ReloadCustom", EffectReplacer.ReloadCustom, "自作エフェクト (RusK/effects の .bundle) を読み込み直す"));
        AddSetting(new ButtonSetting("OpenFolder", OpenFolder, "effects フォルダを開く"));
        Enabled = true;
    }

    public override void OnUpdate()
    {
        // 最初から有効のときは OnEnable が呼ばれないので、毎フレーム合わせる
        EffectHook.Enabled = Enabled;
        EffectHook.Rules?.Tick();
    }

    public override void OnDisable() => EffectHook.Enabled = false;

    public static void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(EffectReplacer.Folder);
            Process.Start("explorer.exe", $"\"{EffectReplacer.Folder}\"");
        }
        catch (Exception e)
        {
            EffectMod.Ctx?.Log.Warning($"Effect: フォルダを開けません: {e.Message}");
        }
    }
}
