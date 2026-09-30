using System;
using HarmonyLib;
using RusK.API;
using UnityEngine;
using Module = RusK.API.Module;

namespace RusK.Mods.Chain;

/// <summary>連携攻撃 (ゼンゼロのチェーン攻撃風)。Party Mod が必要</summary>
[RuskMod("chain", "Chain Attack", "1.0.0",
    Author = "you",
    GameVersion = "0.0.1876",
    Description = "連携攻撃。ヒットをためて追加攻撃を当てると時間が止まり、仲間の追加攻撃を繋げる (Party Mod が必要)")]
public sealed class ChainMod : RuskMod
{
    internal static IRuskLogger Log;
    internal static IModContext Ctx;

    protected override void OnLoad()
    {
        Log = Context.Log;
        Ctx = Context;
        Context.Harmony.PatchAll(typeof(ChainEnemyHitPatch));
        var module = new ChainModule();
        Context.RegisterModule(module);
        if (module.DevTools)
            Context.RegisterAction("ChainDevFillPoints", () => ChainAttack.Points = ChainAttack.PointsNeeded,
                "(開発者向け) 連携のポイントを必要な数までためる");
    }

    protected override void OnUnload() => ChainAttack.Shutdown();
}

/// <summary>連携攻撃の設定と、毎フレームの処理・選択画面の描画</summary>
public sealed class ChainModule : Module
{
    private readonly HotkeySetting _skipKey;
    private readonly IntSetting _pointsNeeded;
    private readonly BoolSetting _devTools;
    private bool _warnedNoParty;

    public ChainModule()
        : base("ChainAttack", "Party",
            "連携攻撃: ヒットをためて追加攻撃を当てると、時間が止まり、次のキャラを選んで追加攻撃を繋げる (Party Mod が必要)")
    {
        _pointsNeeded = AddSetting(new IntSetting("PointsNeeded", 300, 50, 3000,
            "連携に必要なポイント (敵への 1 ヒットで 1 ポイント)"));
        _skipKey = AddSetting(new HotkeySetting("SkipKey", new Hotkey(KeyCode.X),
            "連携回避のキー: 最初の選択中に押すと、連携せずに 1 回分をストックする"));
        _devTools = AddSetting(new BoolSetting("DevTools", false,
            "開発者向け: 連携のポイントを必要な数までためるアクション (ChainDevFillPoints) を登録する。ゲームの再起動で反映"));
        Enabled = true;
    }

    public bool DevTools => _devTools.Value;

    public override string Suffix => ChainAttack.Stock > 0 ? "+1" : null;

    public override void OnDisable() => ChainAttack.Shutdown();

    public override void OnUpdate()
    {
        if (!PartyLink.Available)
        {
            if (!_warnedNoParty && Time.unscaledTime > 10f)
            {
                _warnedNoParty = true;
                ChainMod.Ctx?.Notify(L.T("Chain Attack には Party Mod が必要です"), NotifyLevel.Warning);
            }
            return;
        }
        ChainAttack.Enabled = true; // OFF → ON に戻したとき
        ChainAttack.SkipKey = _skipKey.Value;
        ChainAttack.PointsNeeded = _pointsNeeded.Value;
        ChainAttack.Tick();
    }

    public override void OnGUI() => ChainAttack.DrawChoose();
}

// bool EnemyController.GetHit(Transform atker, AttackBox atkBox, int damage, string hitEffOverride, AttackBoxType boxType, AttackBoxController atkBoxCon)
// (呼び出し元は PlayerController.MakeDamageCallBack だけ)
[HarmonyPatch(typeof(EnemyController), nameof(EnemyController.GetHit))]
internal static class ChainEnemyHitPatch
{
    private static void Postfix(EnemyController __instance, AttackBoxType boxType)
    {
        // 戻り値は「当たったか」ではないらしい (追加攻撃のヒットが数えられなかった) ので見ない
        try { ChainAttack.OnEnemyHit(__instance, boxType); }
        catch (Exception e) { ChainMod.Log?.Warning($"Chain: ヒット処理でエラー: {e.Message}"); }
    }
}
