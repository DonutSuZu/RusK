using System;
using RusK.API;

namespace RusK.Mods.Op2;

/// <summary>
/// Party Op.2 (エンドフィールド風のバトルスタイル。はじめの名前は Party Op.2)。Party Mod が必要。
///
/// モジュールは登録しない (メニューに出さない)。スタイルの切り替えは Party の設定 BattleStyle でする。
/// 毎フレームの処理と画面の描画は、Party が PartyBridge.EndfieldUpdate / EndfieldGui から呼ぶ
/// (Party が Op2Entry.Attach を探して呼び、ここで登録する。Mod の読み込み順に左右されないように)
/// </summary>
[RuskMod("op2", "Party Op.2", "1.0.0",
    Author = "you",
    GameVersion = "0.0.1876",
    Description = "エンドフィールド風のバトルスタイル。全員がフィールドで戦い、操作していないキャラはオート。Party の BattleStyle で切り替える (Party Mod が必要)")]
public sealed class Op2Mod : RuskMod
{
    internal static IModContext Ctx;

    protected override void OnLoad()
    {
        Ctx = Context;
        P.Log = Context.Log;
        Context.Harmony.PatchAll(typeof(FieldSwapPatch));
        Context.Harmony.PatchAll(typeof(FieldSwapSkillUiPatch));
        Context.Harmony.PatchAll(typeof(FieldKeyPatch));
        Context.Harmony.PatchAll(typeof(FieldEnemyHitPatch));
        Context.Harmony.PatchAll(typeof(FieldSpecialKeyPatch));
    }

    protected override void OnUnload()
    {
        Field.Stop();
        Op2Entry.Detach();
    }
}

/// <summary>Party から呼ばれる入口</summary>
public static class Op2Entry
{
    /// <summary>Party の入口に、エンドフィールドスタイルの処理を登録する (Party の EndfieldLink から呼ばれる)</summary>
    public static void Attach()
    {
        if (!P.Bind()) return;
        P.Set("EndfieldUpdate", (Action)Field.Update);
        P.Set("EndfieldGui", (Action)FieldSkills.DrawHud);
        P.Set("EndfieldStop", (Action)Field.Stop);
        P.Set("ControlSwitched", (Action<PlayerController, PlayerController>)Field.OnControlSwitched);
        P.Set("Shielded", (Func<PlayerController, bool>)Field.IsShielded);
        P.Log?.Info("Op.2: Party に登録しました (エンドフィールドスタイル)");
    }

    internal static void Detach()
    {
        if (!P.Bound) return;
        try
        {
            P.Set("EndfieldUpdate", null);
            P.Set("EndfieldGui", null);
            P.Set("EndfieldStop", null);
            P.Set("ControlSwitched", null);
            P.Set("Shielded", null);
        }
        catch { }
    }
}
