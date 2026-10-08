using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RusK.API;
using UnityEngine;

namespace RusK.Mods.Stamina;

/// <summary>
/// スタミナ (縛り)。回避にスタミナを使い、無くなると回避できない。パーティの 3 人で 1 本のゲージを共有する。
/// Party Mod があれば、切り替えのクールタイムを固定し、ジャスト切り替えにもクールタイムを効かせる (無限に避けられないように)。
/// ゲームを難しくする側の Mod (ランキングを有利にしない)。
/// </summary>
[RuskMod("stamina", "Stamina", "1.0.0",
    Author = "you",
    GameVersion = "0.0.1881",
    Description = "回避にスタミナを使う縛り。3 人で共有の 1 本のゲージ。Party の切り替えのクールタイムも固定する")]
public sealed class StaminaMod : RuskMod
{
    internal static IModContext Ctx;

    protected override void OnLoad()
    {
        Ctx = Context;
        StaminaModule.Instance = new StaminaModule();
        Context.RegisterModule(StaminaModule.Instance);
        Context.Harmony.PatchAll(typeof(StaminaMod).Assembly);
    }

    protected override void OnUnload()
    {
        PartyLink.Release();
        StaminaModule.Instance = null;
    }
}

public sealed class StaminaModule : RusK.API.Module
{
    internal static StaminaModule Instance;

    public StaminaModule() : base("Stamina", Categories.Combat,
        "回避にスタミナを使う縛り (パーティの 3 人で 1 本のゲージ)。無くなると回避できない。Party の切り替えのクールタイムも固定する")
    {
        Cost = AddSetting(new FloatSetting("DodgeCost", 34f, 5f, 100f, 1f, "0", "回避 1 回で使うスタミナ (ゲージは 100。少しでも残っていれば回避できる)"));
        Delay = AddSetting(new FloatSetting("RegenDelay", 1f, 0f, 5f, 0.1f, "0.0s", "回避してから回復が始まるまでの時間"));
        Regen = AddSetting(new FloatSetting("RegenPerSecond", 25f, 5f, 100f, 1f, "0", "1 秒に回復するスタミナ"));
        PerfectRefund = AddSetting(new FloatSetting("PerfectRefund", 20f, 0f, 100f, 1f, "0", "ジャスト回避が決まったときに返すスタミナ"));
        SwitchCooldown = AddSetting(new FloatSetting("SwitchCooldown", 3f, 0f, 10f, 0.5f, "0.0s",
            "Party の切り替えのクールタイム (固定。ジャスト切り替えにも効く)"));
    }

    public FloatSetting Cost { get; }
    public FloatSetting Delay { get; }
    public FloatSetting Regen { get; }
    public FloatSetting PerfectRefund { get; }
    public FloatSetting SwitchCooldown { get; }

    public override string Suffix => Enabled ? $"{Stamina.Value:0}" : null;

    public override void OnEnable() => Stamina.Reset();

    public override void OnDisable()
    {
        PartyLink.Release();
        Stamina.Reset();
    }

    public override void OnUpdate()
    {
        PartyLink.Apply(SwitchCooldown.Value);
        Stamina.Tick();
        StaminaHud.UpdateFade();
    }

    public override void OnGUI() => StaminaHud.Draw();
}

/// <summary>スタミナの値 (パーティで 1 本)</summary>
internal static class Stamina
{
    public const float Max = 100f;
    public static float Value = Max;
    private static float _lastUse = -99f;        // ゲームの時間 (ポーズ中・スロー中は進まない)
    public static float LastChange = -99f;       // 表示用 (実時間)
    public static float LastFail = -99f;         // 足りなくて回避できなかった (実時間)
    private static float _lastRefund = -99f;

    public static bool Active => StaminaModule.Instance is { Enabled: true };

    public static void Reset()
    {
        Value = Max;
        _lastUse = -99f;
    }

    public static void Tick()
    {
        if (!Active || Value >= Max) return;
        var m = StaminaModule.Instance;
        if (Time.time - _lastUse < m.Delay.Value) return;
        Value = Mathf.Min(Max, Value + m.Regen.Value * Time.deltaTime);
    }

    /// <summary>少しでも残っていれば回避できる (34 なら 100 → 66 → 32 → 0 の 3 回)</summary>
    public static bool CanDodge => Value >= 1f;

    public static void Use()
    {
        Value = Mathf.Max(0f, Value - StaminaModule.Instance.Cost.Value);
        _lastUse = Time.time;
        LastChange = Time.unscaledTime;
    }

    public static void Fail()
    {
        LastFail = Time.unscaledTime;
        LastChange = Time.unscaledTime;
    }

    public static void Refund()
    {
        if (Time.unscaledTime - _lastRefund < 0.5f) return; // 1 回のジャスト回避で何度も呼ばれても 1 回だけ
        _lastRefund = Time.unscaledTime;
        Value = Mathf.Min(Max, Value + StaminaModule.Instance.PerfectRefund.Value);
        LastChange = Time.unscaledTime;
    }

    /// <summary>今プレイヤーが操作しているキャラか (エンドフィールドスタイルで自動で戦う仲間は数えない)</summary>
    public static bool IsControlled(PlayerController p)
    {
        if (p == null) return false;
        var cur = PartyLink.Current() ?? GameUtil.Instance?.GetPlayer();
        return cur != null && cur.Pointer == p.Pointer;
    }

    /// <summary>縛るのは戦闘ステージだけ (Party が無いときは常に)</summary>
    public static bool InFight => PartyLink.InFight() ?? true;
}

/// <summary>回避 (PlayerController.Dash) に割り込む</summary>
[HarmonyPatch(typeof(PlayerController), nameof(PlayerController.Dash))]
internal static class DashPatch
{
    internal struct State
    {
        public bool Check;
        public IntPtr Motion;
        public string Name;
        public float Time;
    }

    private static bool Prefix(PlayerController __instance, bool force, out State __state)
    {
        __state = default;
        try
        {
            if (!Stamina.Active || force || !Stamina.InFight || !Stamina.IsControlled(__instance)) return true;
            if (!Stamina.CanDodge)
            {
                Stamina.Fail();
                return false; // スタミナが無いので回避しない
            }
            var m = __instance.GetCurMotion();
            __state = new State { Check = true, Motion = m?.Pointer ?? IntPtr.Zero, Name = m?.name, Time = AnimTime(__instance) };
        }
        catch (Exception e) { StaminaMod.Ctx?.Log.Warning($"Stamina: 回避の確認に失敗: {e.Message}"); }
        return true;
    }

    private static void Postfix(PlayerController __instance, State __state)
    {
        if (!__state.Check) return;
        try
        {
            var m = __instance.GetCurMotion();
            var name = m?.name;
            if (!IsDodge(name)) return;
            // 回避の動作に変わったか、回避の動作をやり直した (続けて回避) ときだけ使う
            bool started = m.Pointer != __state.Motion || !IsDodge(__state.Name) || AnimTime(__instance) < __state.Time - 0.05f;
            if (started) Stamina.Use();
        }
        catch (Exception e) { StaminaMod.Ctx?.Log.Warning($"Stamina: 回避の確認に失敗: {e.Message}"); }
    }

    private static bool IsDodge(string name) =>
        name != null && name.Contains("Dash") && !name.Contains("Attack") && !name.Contains("QTE");

    private static float AnimTime(PlayerController p)
    {
        try { return p.GetAnimController()?.GetCurAnimNormalizedTime() ?? 0f; }
        catch { return 0f; }
    }
}

/// <summary>ジャスト回避が決まったら、スタミナを少し返す</summary>
[HarmonyPatch(typeof(PlayerController), nameof(PlayerController.SetPerfectDash))]
internal static class PerfectDashPatch
{
    private static void Postfix(PlayerController __instance, bool flag)
    {
        try
        {
            if (flag && Stamina.Active && Stamina.IsControlled(__instance)) Stamina.Refund();
        }
        catch { }
    }
}

/// <summary>
/// Party Mod の入口 (RusK.Mods.Party.PartyBridge) をリフレクションで使う。
/// Mod は別々に読み込まれ、互いの DLL を直接参照できないため。Party が無ければ何もしない
/// </summary>
internal static class PartyLink
{
    private static bool _tried;
    private static Type _bridge;
    private static FieldInfo _forcedCooldown, _forceJust;
    private static Func<PlayerController> _current;
    private static Func<bool> _inFight, _onField;
    private static bool _applied;

    private static bool Bind()
    {
        if (_bridge != null) return true;
        if (_tried && Time.frameCount % 300 != 0) return false; // 見つからなければ、ときどき探し直す
        _tried = true;
        var asm = AppDomain.CurrentDomain.GetAssemblies().LastOrDefault(a => a.GetName().Name == "RuskParty");
        var t = asm?.GetType("RusK.Mods.Party.PartyBridge");
        if (t == null) return false;
        const BindingFlags st = BindingFlags.Public | BindingFlags.Static;
        try
        {
            _current = (Func<PlayerController>)Delegate.CreateDelegate(typeof(Func<PlayerController>), t.GetMethod("Current", st, null, Type.EmptyTypes, null));
            _inFight = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), t.GetMethod("InFight", st, null, Type.EmptyTypes, null));
            _onField = (Func<bool>)Delegate.CreateDelegate(typeof(Func<bool>), t.GetMethod("OnField", st, null, Type.EmptyTypes, null));
        }
        catch (Exception e)
        {
            StaminaMod.Ctx?.Log.Warning($"Stamina: Party の入口を使えません: {e.Message}");
            return false;
        }
        _forcedCooldown = t.GetField("ForcedCooldown", st);
        _forceJust = t.GetField("ForceJustCooldown", st);
        if (_forcedCooldown == null)
            StaminaMod.Ctx?.Log.Warning("Stamina: Party が古いので、切り替えのクールタイムを固定できません (Party 1.6.0 以上にしてください)");
        _bridge = t;
        return true;
    }

    public static PlayerController Current() => Bind() ? _current() : null;
    public static bool? InFight() => Bind() ? _inFight() : null;
    public static bool? OnField() => Bind() ? _onField() : null;

    /// <summary>切り替えのクールタイムを固定し、ジャスト切り替えにも効かせる (毎フレーム)</summary>
    public static void Apply(float cooldown)
    {
        if (!Bind() || _forcedCooldown == null) return;
        _forcedCooldown.SetValue(null, cooldown);
        _forceJust?.SetValue(null, true);
        _applied = true;
    }

    /// <summary>固定をやめて、Party の設定に戻す</summary>
    public static void Release()
    {
        if (!_applied || _bridge == null) return;
        _forcedCooldown?.SetValue(null, -1f);
        _forceJust?.SetValue(null, false);
        _applied = false;
    }
}

/// <summary>スタミナのゲージ (ゲームの HP 表示の上)</summary>
internal static class StaminaHud
{
    private static float _alpha, _visibleFor;
    private static float _shown = Stamina.Max; // 表示の値 (なめらかに追う)

    public static void UpdateFade()
    {
        bool onField = Stamina.Active && Stamina.InFight && (PartyLink.OnField() ?? GameUtil.Instance?.GetPlayer() != null);
        // 満タンでしばらく使っていなければ隠す
        bool busy = Stamina.Value < Stamina.Max - 0.5f || Time.unscaledTime - Stamina.LastChange < 1.5f;
        bool visible = onField && busy;
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        _visibleFor = visible ? _visibleFor + dt : 0f;
        _alpha = Mathf.MoveTowards(_alpha, visible ? 1f : 0f, dt / (visible ? 0.15f : 0.4f));
        _shown = Mathf.MoveTowards(_shown, Stamina.Value, Stamina.Max * dt * 3f);
    }

    public static void Draw()
    {
        if (_alpha <= 0.001f || !Render.IsRepaint) return;
        var prev = GUI.color;
        GUI.color = new Color(prev.r, prev.g, prev.b, prev.a * _alpha);
        try
        {
            float s = Mathf.Max(0.6f, Screen.height / 1080f);
            float x = 82f * s, y = Screen.height - 200f * s, w = 270f * s, h = 9f * s;
            float t = Time.unscaledTime;
            bool fail = t - Stamina.LastFail < 0.6f;
            bool low = Stamina.Value < StaminaModule.Instance.Cost.Value;

            Render.Text(x, y - 20f * s, w, 18f * s, "STAMINA", new Color(1f, 1f, 1f, 0.85f), Mathf.RoundToInt(12f * s), TextAnchor.MiddleLeft, true, true);
            Render.Rect(x - 2f * s, y - 2f * s, w + 4f * s, h + 4f * s, new Color(0f, 0f, 0f, 0.55f), 3f * s);
            Render.Rect(x, y, w, h, new Color(0.18f, 0.2f, 0.24f, 0.9f), 2f * s);

            var fill = low ? new Color(1f, 0.62f, 0.25f, 1f) : new Color(0.42f, 0.86f, 1f, 1f);
            if (fail && Mathf.Repeat(t * 10f, 1f) < 0.5f) fill = new Color(1f, 0.25f, 0.25f, 1f); // 足りないときは赤く点滅
            float ratio = Mathf.Clamp01(_shown / Stamina.Max);
            if (ratio > 0.001f) Render.Rect(x, y, w * ratio, h, fill, 2f * s);

            // 回避 1 回分の目盛り
            var cost = StaminaModule.Instance.Cost.Value;
            for (float v = cost; v < Stamina.Max - 0.5f; v += cost)
                Render.Rect(x + w * (v / Stamina.Max) - 1f * s, y, 2f * s, h, new Color(0f, 0f, 0f, 0.6f));
        }
        finally { GUI.color = prev; }
    }
}
