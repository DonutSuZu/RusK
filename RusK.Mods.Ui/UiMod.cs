using RusK.API;

namespace RusK.Mods.Ui;

/// <summary>
/// ゲームバランスに影響しない、見た目と操作の補助。
/// ZZZ 風ボタン HUD (追加攻撃が撃てるときに光る)・攻撃予兆 (キラーン)・キー追加。
/// </summary>
[RuskMod("ui", "RusK UI", "1.1.0",
    Author = "you",
    GameVersion = "0.0.1872",
    Description = "ボタン HUD・攻撃予兆・キー追加 (ゲームバランスに影響しない補助)")]
public sealed class UiMod : RuskMod
{
    public static IRuskLogger Log;

    protected override void OnLoad()
    {
        Log = Context.Log;
        Context.RegisterModule(new AttackGlintModule());
        Context.RegisterModule(new ButtonHudModule());
        Context.RegisterModule(new ExtraKeyModule());
        // ゲームがバトル UI を出す瞬間を捕まえる (ボタン HUD とゲージの表示タイミング用)
        Context.Harmony.PatchAll(typeof(BattleUiPatch));
    }

    protected override void OnUnload()
    {
        SkillGauge.Reset();
        AttackGlintModule.Instance?.Cleanup();
        AttackGlintModule.Instance = null;
        Icons.Dispose();
        ExtraKeyModule.Instance = null;
        Log = null;
    }
}
