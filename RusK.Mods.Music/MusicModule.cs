using System;
using System.Diagnostics;
using System.IO;
using RusK.API;
using UnityEngine;

namespace RusK.Mods.Music;

/// <summary>
/// ON の間、戦闘中の BGM を RusK/music の曲に置き換える。
/// 設定はメニュー (Music カテゴリ) とプレイヤー画面の両方から変えられる。
/// </summary>
public sealed class MusicModule : RusK.API.Module
{
    private static readonly string[] Modes = { "FightOnly", "Always" };

    private readonly ModeSetting _mode;
    private readonly ModeSetting _order;
    private readonly FloatSetting _volume;
    private readonly FloatSetting _fade;
    private readonly BoolSetting _bossFolder;
    private readonly BoolSetting _notify;

    public MusicModule(IModContext context) : base("MusicManager", "Music", "戦闘中の BGM を RusK/music の曲に置き換える")
    {
        _mode = AddSetting(new ModeSetting("When", Modes, 0, "FightOnly: 戦闘中だけ / Always: いつでも"));
        _order = AddSetting(new ModeSetting("Order", new[] { "Shuffle", "Sequential" }, 0, "曲順"));
        _volume = AddSetting(new FloatSetting("Volume", 0.8f, 0f, 1f, 0.05f, "0.00",
            "音量 (ゲームの音楽ボリューム設定にさらに掛かる)"));
        _fade = AddSetting(new FloatSetting("Fade", 1.5f, 0f, 5f, 0.1f, "0.0s", "切り替えのフェード時間"));
        _bossFolder = AddSetting(new BoolSetting("BossFolder", true, "boss フォルダの曲をボス戦で流す"));
        _notify = AddSetting(new BoolSetting("NowPlaying", true, "曲が変わったら通知する"));
        AddSetting(new ButtonSetting("OpenPlayer", () => Window.Toggle(), "プレイヤー画面を開く"));
        AddSetting(new ButtonSetting("OpenFolder", () => OpenFolder(), "music フォルダを開く"));
        AddSetting(new ButtonSetting("Rescan", () => Player.Scan(), "曲を探し直す"));

        var folder = Path.Combine(Path.GetDirectoryName(context.DataDirectory)!, "..", "music");
        Player = new MusicPlayer(context, Path.GetFullPath(folder));
        Window = new MusicWindow(this);
        context.RegisterWindow(Window);
    }

    internal MusicPlayer Player { get; }
    internal MusicWindow Window { get; }
    internal FloatSetting Volume => _volume;
    internal ModeSetting Order => _order;

    public override string Suffix => Player.Current != null ? "♪" : null;

    public override void OnDisable() => Player.StopImmediate();

    public override void OnUpdate()
    {
        Player.Shuffle = _order.Value == 0;
        Player.Volume = _volume.Value;
        Player.FadeSeconds = _fade.Value;
        Player.UseBossFolder = _bossFolder.Value;
        Player.NotifyTrack = _notify.Value;

        bool fight = InFight(out bool boss);
        bool want = _mode.Value == 1 || fight;
        Player.Tick(want, boss);
    }

    /// <summary>今が戦闘中か (ゲームの GameUtil.InFightScene / IsInBossFight)</summary>
    internal static bool InFight(out bool boss)
    {
        boss = false;
        try
        {
            var util = GameUtil.Instance;
            if (util == null) return false;
            boss = util.IsInBossFight();
            return util.InFightScene() || boss;
        }
        catch
        {
            return false;
        }
    }

    internal void OpenFolder()
    {
        try { Process.Start("explorer.exe", $"\"{Player.Folder}\""); }
        catch (Exception e) { Context.Log.Warning($"Open folder failed: {e.Message}"); }
    }
}
