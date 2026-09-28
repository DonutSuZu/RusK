using RusK.API;
using UnityEngine;

namespace RusK.Mods.Music;

/// <summary>音楽プレイヤー画面 (Flex Window)。再生中の曲・操作ボタン・音量・曲一覧</summary>
internal sealed class MusicWindow : RuskWindow
{
    private readonly MusicModule _module;

    public MusicWindow(MusicModule module) : base("player", "Music Player", 420f, 520f)
    {
        _module = module;
        MinWidth = 320f;
        MinHeight = 260f;
    }

    public override void Draw(WindowGui gui)
    {
        var player = _module.Player;
        bool fight = MusicModule.InFight(out bool boss);

        string state = !_module.Enabled ? "OFF (MusicManager を ON にすると置き換えます)"
            : player.Loading ? "読み込み中..."
            : player.Current != null ? (player.Paused ? "一時停止中" : boss ? "ボス戦" : "再生中")
            : fight ? "曲がありません" : "戦闘になったら再生します";
        gui.Header("Now Playing", state);
        gui.Label(player.Current?.Name ?? "(再生していません)",
            player.Current != null ? RuskStyle.Text : RuskStyle.TextDim, bold: true);

        // 進行バー
        float len = player.Length, t = player.Time;
        var bar = gui.Next(6f * gui.Scale);
        gui.Box(bar, RuskStyle.Track, 3f);
        if (len > 0f)
            gui.Box(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(t / len), bar.height), RuskStyle.Accent, 3f);
        gui.Label($"{Format(t)} / {Format(len)}", RuskStyle.TextDim, anchor: TextAnchor.MiddleRight, small: true);

        gui.BeginRow(1f, 1.2f, 1f);
        if (gui.Button("◀◀ 前へ", enabled: player.Current != null)) player.Previous();
        if (gui.Button(player.Paused ? "▶ 再開" : "一時停止", enabled: player.Current != null, accent: true))
            player.TogglePause();
        if (gui.Button("次へ ▶▶", enabled: player.Current != null)) player.Next();

        _module.Volume.Value = gui.Slider("volume", "音量", _module.Volume.Value, 0f, 1f, "0%");

        gui.BeginRow(1f, 1f);
        _module.Enabled = gui.Toggle("BGM を置き換える", _module.Enabled);
        bool shuffle = gui.Toggle("シャッフル", _module.Order.Value == 0);
        _module.Order.Value = shuffle ? 0 : 1;

        gui.BeginRow(1f, 1f);
        if (gui.Button("フォルダを開く")) _module.OpenFolder();
        if (gui.Button("再スキャン")) player.Scan();

        gui.Space(6f);
        gui.Header("曲一覧", $"{player.Tracks.Count} 曲");
        if (player.Tracks.Count == 0)
        {
            gui.Label("music フォルダに mp3 / ogg / wav を入れて「再スキャン」", RuskStyle.TextDim, small: true);
            gui.Label(player.Folder, RuskStyle.TextDim, small: true);
            return;
        }

        foreach (var track in player.Tracks)
        {
            if (gui.Selectable(track.Name, track == player.Current, track.IsBoss ? "BOSS" : null))
                player.Play(track);
        }
    }

    private static string Format(float seconds)
    {
        if (seconds <= 0f || float.IsNaN(seconds)) return "0:00";
        int s = Mathf.FloorToInt(seconds);
        return $"{s / 60}:{s % 60:00}";
    }
}
