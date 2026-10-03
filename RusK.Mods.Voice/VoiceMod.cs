using System;
using System.Diagnostics;
using System.IO;
using RusK.API;

namespace RusK.Mods.Voice;

/// <summary>
/// Voice Replacer: キャラのボイス (と効果音) を、RusK/voices に置いた音声ファイルに置き換える。
/// ファイル名をゲームの音声の名前にする。名前は LogPlayed をオンにすると RusK/voices/_played.txt に書き出される
/// </summary>
[RuskMod("voice", "Voice Replacer", "1.2.0",
    Author = "you",
    GameVersion = "0.0.1878",
    Description = "キャラのボイス (と効果音) を RusK/voices の音声ファイル (ogg / wav / mp3) に置き換える")]
public sealed class VoiceMod : RuskMod
{
    internal static IModContext Ctx;
    private VoiceModule _module;

    protected override void OnLoad()
    {
        RusK.Mods.Shared.SceneChars.Patch(Context.Harmony);
        Ctx = Context;
        _module = new VoiceModule(Context);
        Context.RegisterModule(_module);
        Context.RegisterAction("VoiceRescan", () => _module.Rescan(), "ボイスを読み込み直す");
        Context.Harmony.PatchAll(typeof(CreateSfxOnTransformPatch));
        Context.Harmony.PatchAll(typeof(CreateSfxAtPositionPatch));
        Context.Harmony.PatchAll(typeof(PersistentVoicePatch));
        Context.Harmony.PatchAll(typeof(UiCharacterVoicePatch));
        Context.Harmony.PatchAll(typeof(ResolveVoicePatch));
    }

    protected override void OnUnload()
    {
        VoiceSwap.Enabled = false;
        VoiceSwap.Bank?.Clear();
        VoiceSwap.Bank = null;
    }
}

/// <summary>メニューの Music > VoiceReplacer</summary>
public sealed class VoiceModule : Module
{
    private readonly FloatSetting _volume;
    private readonly BoolSetting _log;
    private bool _scanned;

    public VoiceModule(IModContext context)
        : base("VoiceReplacer", "Music", "キャラのボイス (と効果音) を RusK/voices の音声ファイルに置き換える")
    {
        _volume = AddSetting(new FloatSetting("Volume", 1f, 0f, 2f, 0.05f, "0.00", "置き換えた音声の音量 (ゲームの音量にさらに掛かる)"));
        _log = AddSetting(new BoolSetting("LogPlayed", false,
            "鳴った音声の名前を RusK/voices/_played.txt に書き出す (置き換えるファイルの名前を調べる用)"));
        AddSetting(new ButtonSetting("OpenFolder", OpenFolder, "voices フォルダを開く"));
        AddSetting(new ButtonSetting("Rescan", Rescan, "ボイスを読み込み直す"));

        var folder = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(context.DataDirectory)!, "..", "voices"));
        VoiceSwap.Bank = new VoiceBank(context, folder);
        Enabled = true;
    }

    public override string Suffix => VoiceSwap.Bank == null ? null :
        VoiceSwap.Bank.Loading ? "…" : VoiceSwap.Bank.Count.ToString();

    public override void OnEnable() => VoiceSwap.Enabled = true;

    public override void OnDisable() => VoiceSwap.Enabled = false;

    public override void OnUpdate()
    {
        // 最初から有効のときは OnEnable が呼ばれないので、ここで有効にして読み込みを始める
        VoiceSwap.Enabled = Enabled;
        if (!_scanned) Rescan();
        VoiceSwap.VolumeScale = _volume.Value;
        if (_log.Value && !VoiceSwap.LogPlayed) VoiceSwap.ResetLog();
        VoiceSwap.LogPlayed = _log.Value;
        VoiceSwap.Bank?.Tick();
    }

    internal void Rescan()
    {
        _scanned = true;
        VoiceSwap.Bank?.Scan();
    }

    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(VoiceSwap.Bank.Folder);
            Process.Start("explorer.exe", $"\"{VoiceSwap.Bank.Folder}\"");
        }
        catch (Exception e) { VoiceMod.Ctx?.Log.Warning($"Open folder failed: {e.Message}"); }
    }
}
