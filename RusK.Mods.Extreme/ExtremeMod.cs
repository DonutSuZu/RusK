using System;
using RusK.API;

namespace RusK.Mods.Extreme;

/// <summary>
/// EXTREME 難易度。
/// ゲームには難易度 Extreme がすでに作られているが、選択肢には出てこない (HARD クリア後に解放される仕組みらしい)。
/// この Mod はそれを設定画面の「難易度」と初回の難易度選択画面に出し、選ばれている間は敵をさらに強化する。
/// 表示名・説明文がゲームに無い場合だけ RusK が補う。
/// </summary>
[RuskMod("extreme", "EXTREME Difficulty", "1.1.0",
    Author = "you",
    GameVersion = "0.0.1873",
    Description = "難易度 EXTREME を解放し、敵の HP・攻撃力・攻撃頻度・シールドを強化")]
public sealed class ExtremeMod : RuskMod
{
    protected override void OnLoad()
    {
        ExtremeState.Init(Context);
        ExtremeModule.Instance = new ExtremeModule();
        Context.RegisterModule(ExtremeModule.Instance);

        // 選択肢の解放・表示と、敵の強化のパッチ。効き目はモジュールの ON/OFF と難易度で切り替える
        Context.Harmony.PatchAll(typeof(ExtremeMod).Assembly);
        Context.Log.Info($"EXTREME: ゲームの難易度 {ExtremeState.GameDifficulty}");
    }

    protected override void OnUnload()
    {
        DifficultyUi.RestoreViews();
        ExtremeModule.Instance = null;
    }
}

internal static class ExtremeState
{
    private static IModContext _ctx;

    public static IRuskLogger Log => _ctx?.Log;

    /// <summary>初回の難易度選択画面で、今 EXTREME ボタンを表示しているか (決定前)</summary>
    public static bool Pending;

    public static void Init(IModContext ctx) => _ctx = ctx;

    /// <summary>EXTREME を選択肢に出すか (モジュールが ON)</summary>
    public static bool Unlocked => ExtremeModule.Instance is { Enabled: true };

    public static GameDifficultyType GameDifficulty
    {
        get
        {
            try { return GameUtil.GetDifficultyType(); }
            catch { return GameDifficultyType.Normal; }
        }
    }

    /// <summary>今、敵を強化するか (モジュールが ON で、ゲームの難易度が EXTREME)</summary>
    public static bool Active => Unlocked && GameDifficulty == GameDifficultyType.Extreme;

    /// <summary>メニューから直接難易度を変える</summary>
    public static void Choose(GameDifficultyType type)
    {
        try
        {
            GameUtil.SetDifficultyType(type, true);
            _ctx?.Notify(L.T("難易度: {0}", type == GameDifficultyType.Extreme ? "EXTREME" : type.ToString()),
                type == GameDifficultyType.Extreme ? NotifyLevel.Warning : NotifyLevel.Info);
        }
        catch (Exception e)
        {
            Log?.Error($"EXTREME choose failed: {e}");
        }
    }

    /// <summary>説明文の下に足す、RusK の強化内容</summary>
    public static string BoostSummary
    {
        get
        {
            var m = ExtremeModule.Instance;
            if (m == null) return "";
            return L.T("RusK: HP ×{0:0.0} / 攻撃力 ×{1:0.0} / 攻撃頻度 ×{2:0.0} / シールド ×{3:0.0}",
                m.Hp.Value, m.Damage.Value, m.AttackRate.Value, m.Shield.Value);
        }
    }
}

/// <summary>EXTREME の強化の設定。ON の間は EXTREME を選択肢に出し、EXTREME のときに敵を強化する</summary>
public sealed class ExtremeModule : RusK.API.Module
{
    internal static ExtremeModule Instance;

    public ExtremeModule() : base("EXTREME", Categories.Combat, "難易度 EXTREME を解放し、選んでいる間は敵を強化する")
    {
        Hp = AddSetting(new FloatSetting("HpMultiplier", 2f, 1f, 10f, 0.1f, "0.0x", "敵の HP の倍率"));
        Damage = AddSetting(new FloatSetting("DamageMultiplier", 1.5f, 1f, 10f, 0.1f, "0.0x", "敵の攻撃力 (プレイヤーが受けるダメージ) の倍率"));
        AttackRate = AddSetting(new FloatSetting("AttackFrequency", 1.5f, 1f, 5f, 0.1f, "0.0x", "敵の攻撃の頻度 (攻撃間隔を短くする)"));
        Shield = AddSetting(new FloatSetting("ShieldMultiplier", 2f, 1f, 5f, 0.1f, "0.0x", "敵のシールドポイントの倍率"));
        AddSetting(new ButtonSetting("SelectNow", () => ExtremeState.Choose(GameDifficultyType.Extreme),
            "今すぐ難易度を EXTREME にする (設定画面を使わない場合)"));
        AddSetting(new ButtonSetting("BackToHard", () => ExtremeState.Choose(GameDifficultyType.Hard),
            "難易度を HARD に戻す"));

        Enabled = true; // 既定で ON (EXTREME を選ばない限り敵は強化されない)
    }

    public FloatSetting Hp { get; }
    public FloatSetting Damage { get; }
    public FloatSetting AttackRate { get; }
    public FloatSetting Shield { get; }

    public override bool VisibleInArrayList => ExtremeState.Active;
    public override string Suffix => ExtremeState.Active ? "ACTIVE" : null;
}
