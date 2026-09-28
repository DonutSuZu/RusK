using RusK.API;

namespace RusK.Mods.Music;

/// <summary>
/// 音楽マネージャー。ゲーム/RusK/music 以下の曲を、戦闘中にゲームの BGM と置き換えて流す。
/// </summary>
[RuskMod("music", "Music Manager", "1.1.0",
    Author = "you",
    GameVersion = "0.0.1872",
    Description = "戦闘中の BGM を RusK/music の曲に置き換える")]
public sealed class MusicMod : RuskMod
{
    private MusicModule _module;

    protected override void OnLoad()
    {
        _module = new MusicModule(Context);
        Context.RegisterModule(_module);

        Context.RegisterAction("MusicNext", () => _module.Player.Next(), "次の曲へ");
        Context.RegisterAction("MusicPrev", () => _module.Player.Previous(), "前の曲へ");
        Context.RegisterAction("MusicPause", () => _module.Player.TogglePause(), "一時停止 / 再開");
        Context.RegisterAction("MusicWindow", () => _module.Window.Toggle(), "プレイヤー画面を開く / 閉じる");
    }

    protected override void OnUnload()
    {
        _module?.Player.Dispose();
        _module = null;
    }
}
