using System;
using System.Collections.Generic;
using System.Linq;
using RusK.API;
using UnityEngine;
using Module = RusK.API.Module;

namespace RusK.Mods.Party;

/// <summary>
/// アクティブ3人 (ZZZ 風のキャラ切り替え)。
/// 選んだ仲間 2 人を戦闘ステージで控えに用意し、「次へ」「前へ」のキーで切り替える。
/// </summary>
[RuskMod("party", "Party", "1.4.0",
    Author = "you",
    GameVersion = "0.0.1876",
    Description = "アクティブ3人。仲間を選んで、戦闘中にキーでキャラを切り替える")]
public sealed class PartyMod : RuskMod
{
    protected override void OnLoad()
    {
        PartyManager.Log = Context.Log;
        PartyManager.Ctx = Context;
        PartyManager.LoadCompanions();

        var select = new PartySelectWindow();
        Context.RegisterWindow(select);
        Context.Harmony.PatchAll(typeof(JustWarningPatch));
        Context.Harmony.PatchAll(typeof(JustParryPatch));
        Context.Harmony.PatchAll(typeof(JustDefencePatch));
        Context.Harmony.PatchAll(typeof(PartyHpChangePatch));
        Context.Harmony.PatchAll(typeof(PartyDiePatch));
        var party = new PartyModule(select);
        Context.RegisterModule(party);

        Context.RegisterAction("PartyNext", () => PartyManager.Cycle(+1), "パーティの次のキャラに切り替える");
        Context.RegisterAction("PartyPrev", () => PartyManager.Cycle(-1), "パーティの前のキャラに切り替える");
        Context.RegisterAction("PartySelect", () => select.Toggle(), "仲間の選択画面を開く / 閉じる");

        // 開発者向けの Party Lab (調査用ウィンドウ・試し撃ちのアクション) は、設定 DevTools がオンのときだけ登録する
        // (Mod からモジュールの登録を外す手段がないので、切り替えはゲームの再起動で反映)
        if (party.DevTools)
        {
            Context.RegisterModule(new PartyLabModule(Context));
            Context.RegisterAction("PartyLabQte", () => QteProbe.Fire(PartyManager.Current, "キー割り当て"), "(調査用) 今のキャラで追加攻撃を撃つ");
            Context.RegisterAction("PartyLabQteSwitch", QteProbe.SwitchAndFire, "(調査用) 次のキャラに切り替えて追加攻撃を撃つ");
        }
    }

    protected override void OnUnload() => PartyManager.Clear();
}

/// <summary>パーティの本体。キー操作・控えの自動作成・パーティ HUD</summary>
public sealed class PartyModule : Module
{
    private readonly HotkeySetting _nextKey;
    private readonly HotkeySetting _prevKey;
    private readonly FloatSetting _cooldown;
    private readonly BoolSetting _safeSwitch;
    private readonly BoolSetting _autoSpawn;
    private readonly BoolSetting _just;
    private readonly FloatSetting _justWindow;
    private readonly FloatSetting _guardTime;
    private readonly BoolSetting _justIgnoreCooldown;
    private readonly BoolSetting _shareBuffs;
    private readonly BoolSetting _devTools;
    private readonly ModeSetting _style;
    private readonly BoolSetting _showHud;
    private readonly FloatSetting _hudX;
    private readonly FloatSetting _hudY;
    private readonly FloatSetting _hudScale;

    private float _alpha;
    private float _visibleFor;

    public PartyModule(PartySelectWindow select)
        : base("Party", "Party", "アクティブ3人。戦闘中に「次へ」「前へ」のキーでキャラを切り替える")
    {
        _nextKey = AddSetting(new HotkeySetting("NextKey", new Hotkey(KeyCode.C), "次のキャラに切り替えるキー"));
        _prevKey = AddSetting(new HotkeySetting("PrevKey", new Hotkey(KeyCode.Z), "前のキャラに切り替えるキー"));
        _cooldown = AddSetting(new FloatSetting("Cooldown", 3f, 0f, 10f, 0.5f, "0.0s", "切り替えのクールタイム"));
        _safeSwitch = AddSetting(new BoolSetting("SafeSwitch", true, "回避中・スロー演出中などは切り替えない"));
        _autoSpawn = AddSetting(new BoolSetting("AutoSpawn", true, "戦闘ステージで、選んだ仲間を自動で控えに用意する"));
        _just = AddSetting(new BoolSetting("JustSwitch", true,
            "切り替えを「ガードを押した」扱いにする。攻撃の直前に切り替えると、出てきたキャラがゲーム本来のガードでパリィする"));
        _guardTime = AddSetting(new FloatSetting("SwitchGuardTime", 1.0f, 0.1f, 3f, 0.1f, "0.0s",
            "切り替えの後、この秒数の間に受けた攻撃をガードで受ける (パリィになるかはゲームのガードと同じ判定)"));
        _justWindow = AddSetting(new FloatSetting("JustWindow", 0.6f, 0.1f, 2f, 0.05f, "0.00s",
            "攻撃の予兆 (キラーン) からこの秒数以内なら、クールタイム中でも切り替えられる"));
        _justIgnoreCooldown = AddSetting(new BoolSetting("JustIgnoresCooldown", true,
            "予兆の直後はクールタイム中でも切り替えられる"));
        _shareBuffs = AddSetting(new BoolSetting("ShareBuffs", true,
            "戦闘中に獲得したパッシブバフをパーティ全員で共有する (操作中のキャラが得たバフを控えにも付ける)"));
        _showHud = AddSetting(new BoolSetting("ShowHud", true, "パーティ HUD (顔・HP・必殺技ゲージ) を出す"));
        _hudX = AddSetting(new FloatSetting("HudX", 0.015f, 0f, 1f, 0.005f, "0.000", "パーティ HUD の横位置 (画面比)"));
        _hudY = AddSetting(new FloatSetting("HudY", 0.34f, 0f, 1f, 0.005f, "0.000", "パーティ HUD の縦位置 (画面比)"));
        _hudScale = AddSetting(new FloatSetting("HudScale", 1f, 0.5f, 2f, 0.05f, "0.00", "パーティ HUD の大きさ"));
        AddSetting(new ButtonSetting("SelectMembers", () => select.Visible = true, "仲間の選択画面を開く"));
        Instance = this;
        _style = AddSetting(new ModeSetting("BattleStyle", new[] { "ゼンゼロ", "エンドフィールド" }, 0,
            "バトルスタイル。ゼンゼロ: 控えは隠れて交代する。エンドフィールド: 全員がフィールドで戦い、操作していないキャラはオート (Party Op.2 が必要)"));
        _devTools = AddSetting(new BoolSetting("DevTools", false,
            "開発者向け: Party Lab (調査用のウィンドウ) をメニューに出す。ゲームの再起動で反映"));
        Enabled = true;
    }

    /// <summary>開発者向けの Party Lab を出すか (読み込み時の値で決まる)</summary>
    public bool DevTools => _devTools.Value;

    /// <summary>ほかの Mod 用 (PartyBridge): バトルスタイル (0: ゼンゼロ、1: エンドフィールド)</summary>
    internal static int StyleValue;
    internal static PartyModule Instance;

    /// <summary>バトルスタイルを変える (Party Formation から)。設定にも保存される</summary>
    internal void SetStyle(int style)
    {
        _style.Value = Mathf.Clamp(style, 0, 1);
        StyleValue = _style.Value;
    }

    /// <summary>ほかの Mod 用 (PartyBridge): 今の「次へ」「前へ」のキー</summary>
    internal static Hotkey NextKeyValue = new(KeyCode.C);
    internal static Hotkey PrevKeyValue = new(KeyCode.Z);

    public override string Suffix => L.T("{0}人", PartyManager.Members.Count);

    public override void OnUpdate()
    {
        PartyManager.Cooldown = _cooldown.Value;
        PartyManager.SafeSwitch = _safeSwitch.Value;
        JustSwitch.Enabled = _just.Value;
        JustSwitch.Window = _justWindow.Value;
        JustSwitch.GuardTime = _guardTime.Value;
        JustSwitch.IgnoreCooldown = _justIgnoreCooldown.Value;
        BuffShare.Enabled = _shareBuffs.Value;
        NextKeyValue = _nextKey.Value;
        PrevKeyValue = _prevKey.Value;
        StyleValue = _style.Value;
        EndfieldLink.Tick();
        PartyManager.Tick();
        PartyManager.SyncLeader();
        BuffShare.Tick();

        // 開発者向けの試作 (Party Lab で置いたキャラ)。ウィンドウを閉じても動き続けるように、ここで回す
        if (PartyBridge.EndfieldActive)
        {
            try { PartyBridge.EndfieldUpdate?.Invoke(); }
            catch (Exception e) { EndfieldLink.Fault("毎フレームの処理", e); }
        }

        if (_autoSpawn.Value) PartyManager.AutoSpawn();

        bool field = PartyHud.OnField();
        if (field && !PartyBridge.SuppressSwitchKeys) // ほかの Mod (連携攻撃の選択など) が切り替えキーを使っている間は切り替えない
        {
            if (RuskInput.WasPressed(_nextKey.Value)) PartyManager.Cycle(+1);
            else if (RuskInput.WasPressed(_prevKey.Value)) PartyManager.Cycle(-1);
        }

        // HUD のフェード (ロード明けのチラつき防止に、少し待ってから出す)
        bool visible = _showHud.Value && field && PartyManager.Members.Count >= 2 && !PartyBridge.EndfieldActive; // エンドフィールドスタイルでは Op.2 の HUD を出す
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        _visibleFor = visible ? _visibleFor + dt : 0f;
        bool show = visible && _visibleFor >= 0.4f;
        _alpha = Mathf.MoveTowards(_alpha, show ? 1f : 0f, dt / (show ? 0.3f : 0.12f));
    }

    public override void OnGUI()
    {
        if (PartyBridge.EndfieldActive)
        {
            try { PartyBridge.EndfieldGui?.Invoke(); }
            catch (Exception e) { EndfieldLink.Fault("画面の描画", e); }
        }
        if (_alpha <= 0.001f || !Render.IsRepaint) return;
        var prev = GUI.color;
        GUI.color = new Color(prev.r, prev.g, prev.b, prev.a * _alpha);
        try
        {
            PartyHud.Draw(Screen.width * _hudX.Value, Screen.height * _hudY.Value, _hudScale.Value,
                _prevKey.Value, _nextKey.Value);
        }
        finally
        {
            GUI.color = prev;
        }
    }
}

/// <summary>画面左のパーティ HUD</summary>
internal static class PartyHud
{
    private static int _frame = -1;
    private static bool _onField;

    /// <summary>戦闘ステージにいて、ロード中でもウィンドウ表示中でもポーズ中でもない (1 フレームに 1 回だけ調べる)</summary>
    public static bool OnField()
    {
        if (_frame == Time.frameCount) return _onField;
        _frame = Time.frameCount;
        _onField = Check();
        return _onField;
    }

    private static bool Check()
    {
        if (PartyManager.Current == null || !PartyManager.InFight) return false;
        try
        {
            var util = GameUtil.Instance;
            if (util != null && (util.GetInLoading() || util.GetInPause())) return false;
            var loader = SceneLoader.Instance;
            if (loader != null && loader.InSceneLoading()) return false;
            var ui = UIController.Instance;
            if (ui != null)
            {
                if (ui.AnyWindowOpen()) return false;
                var loading = ui.m_loadingObject;
                if (loading != null && loading.activeInHierarchy) return false;
                var hp = ui.m_healthCon;
                if (hp != null && !hp.gameObject.activeInHierarchy) return false;
            }
        }
        catch { }
        return true;
    }

    // ---- ゼンゼロ風の見た目: 斜めのパネルとゲージ。操作中のキャラは大きく、控えは小さく下に並べる

    /// <summary>斜めの傾き (上に行くほど右へずれる量 / 高さ)</summary>
    private const float Slant = 0.28f;

    private static readonly Color TextCol = new(0.97f, 0.97f, 0.99f, 1f);
    private static readonly Color DimCol = new(0.66f, 0.68f, 0.74f, 1f);
    private static readonly Color PanelCol = new(0.04f, 0.045f, 0.06f, 0.82f);
    private static readonly Color HpCol = new(0.62f, 0.93f, 0.25f, 1f);
    private static readonly Color HpLowCol = new(0.98f, 0.30f, 0.26f, 1f);

    /// <summary>ダメージを受けたときに遅れて減る白いゲージ用 (キャラごとの表示中の HP 割合)</summary>
    private static readonly Dictionary<IntPtr, float> TrailHp = new();

    public static void Draw(float x, float y, float scale, Hotkey prevKey, Hotkey nextKey)
    {
        var cur = PartyManager.Current;
        if (cur == null) return;
        var members = PartyManager.Members.Where(m => m != null).ToList();
        int ci = members.FindIndex(m => m.Pointer == cur.Pointer);
        if (ci < 0) return;

        float s = scale * Mathf.Max(0.6f, Screen.height / 1080f);
        float cd = PartyManager.CooldownRemaining;

        // 操作中 → 次 → … → 前 の順 (切り替えキーで回る順番)
        DrawActive(cur, x, y, s);
        float yy = y + 84f * s;
        int n = members.Count;
        for (int k = 1; k < n; k++)
        {
            var m = members[(ci + k) % n];
            string key = k == 1 ? nextKey.Display : k == n - 1 ? prevKey.Display : null;
            DrawReserve(m, x + (10f + 8f * (k - 1)) * s, yy, s, key, cd);
            yy += 56f * s;
        }
        try { PartyBridge.HudExtras?.Invoke(x + (10f + 8f * (n - 1)) * s, yy + 2f * s, s); }
        catch (Exception e) { PartyManager.Log?.Warning($"Party: HUD の描き足し (ほかの Mod) でエラー: {e.Message}"); PartyBridge.HudExtras = null; }
    }

    private static void DrawActive(PlayerController p, float x, float y, float s)
    {
        var mm = PartyManager.FindCharacter(PartyManager.Id(p));
        var theme = ThemeColor(mm);
        float h = 76f * s, w = 270f * s, face = 68f * s;

        // パネル: 暗い斜めの帯 + テーマカラーの差し色
        Skew(x + 16f * s, y, w, h, PanelCol);
        Skew(x + 16f * s + w - 5f * s, y, 5f * s, h, theme);
        Skew(x + 6f * s, y + 4f * s, face + 10f * s, h - 8f * s, theme);

        // 顔
        float fx = x + 14f * s, fy = y + 4f * s;
        DrawPortrait(mm, fx, fy, face, 1f);
        Render.Rect(fx, fy + face - 3f * s, face, 3f * s, theme);

        // 名前
        float tx = fx + face + 16f * s, tw = w - face - 30f * s;
        Render.Text(tx, y + 6f * s, tw, 22f * s, PartyManager.DisplayName(mm), TextCol,
            Mathf.RoundToInt(17f * s), TextAnchor.MiddleLeft, true, true);

        Stats(p, out float hp, out float cur, out float max, out float energy);

        // HP (遅れて減る白いゲージ付き)
        float by = y + 32f * s;
        HpBar(p, tx, by, tw, 12f * s, hp);
        Render.Text(tx, by + 13f * s, tw, 16f * s, $"{cur:0} / {max:0}", TextCol,
            Mathf.RoundToInt(12f * s), TextAnchor.MiddleLeft, true, true);

        // 必殺技ゲージ (溜まったら明滅)
        EnergyBar(tx + 70f * s, by + 18f * s, tw - 74f * s, 6f * s, energy, theme);

        // ジャスト切り替え / パリィ / 回避の演出 (いちばん新しいもの)
        float t = Time.unscaledTime;
        string label = null;
        float at = -999f;
        Color labelCol = theme;
        if (JustSwitch.LastJust > at) { at = JustSwitch.LastJust; label = "JUST!"; labelCol = theme; }
        if (JustSwitch.LastParry >= at) { at = JustSwitch.LastParry; label = "PARRY!"; labelCol = Color.white; }
        if (JustSwitch.LastEvade >= at) { at = JustSwitch.LastEvade; label = "EVADE!"; labelCol = new Color(0.55f, 0.9f, 1f, 1f); }
        if (t - at < 1f)
        {
            float since = t - at;
            float a = 1f - Mathf.Clamp01((since - 0.6f) / 0.4f);
            float pop = 1f + 0.35f * Mathf.Clamp01(1f - since / 0.15f); // 出た瞬間だけ大きく
            int size = Mathf.RoundToInt(26f * s * pop);
            float lx = x + 16f * s + w + 14f * s;
            Skew(lx - 6f * s, y + 18f * s, 150f * s, 40f * s, Render.WithAlpha(PanelCol, 0.8f * a));
            Skew(lx - 6f * s, y + 18f * s, 4f * s, 40f * s, Render.WithAlpha(theme, a));
            Render.Text(lx + 6f * s, y + 18f * s, 150f * s, 40f * s, label,
                Render.WithAlpha(labelCol, a), size, TextAnchor.MiddleLeft, true, true);
        }
        else if (t - PartyManager.LastBlockTime < 1.2f && !string.IsNullOrEmpty(PartyManager.LastBlockReason))
        {
            // 切り替えられなかった理由
            float a = 1f - Mathf.Clamp01((t - PartyManager.LastBlockTime - 0.8f) / 0.4f);
            float lx = x + 16f * s + w + 14f * s;
            Render.Text(lx, y + h - 26f * s, 260f * s, 20f * s, PartyManager.LastBlockReason,
                Render.WithAlpha(new Color(1f, 0.75f, 0.45f, 1f), a), Mathf.RoundToInt(13f * s),
                TextAnchor.MiddleLeft, true, true);
        }
    }

    private static void DrawReserve(PlayerController p, float x, float y, float s, string key, float cd)
    {
        var mm = PartyManager.FindCharacter(PartyManager.Id(p));
        var theme = ThemeColor(mm);
        float h = 48f * s, w = 200f * s, face = 42f * s;

        Skew(x + 12f * s, y, w, h, Render.WithAlpha(PanelCol, 0.7f));
        Skew(x + 4f * s, y + 3f * s, face + 8f * s, h - 6f * s, Render.WithAlpha(theme, 0.75f));

        float fx = x + 10f * s, fy = y + 3f * s;
        DrawPortrait(mm, fx, fy, face, 0.85f);

        Stats(p, out float hp, out _, out _, out float energy);
        bool down = hp <= 0f;

        float tx = fx + face + 14f * s, tw = w - face - 24f * s;
        Render.Text(tx, y + 3f * s, tw, 18f * s, PartyManager.DisplayName(mm), down ? DimCol : TextCol,
            Mathf.RoundToInt(13f * s), TextAnchor.MiddleLeft, true, true);
        HpBar(p, tx, y + 23f * s, tw, 7f * s, hp);
        EnergyBar(tx, y + 34f * s, tw, 4f * s, energy, theme);

        // クールタイム / 戦闘不能
        if (down || cd > 0f)
        {
            Render.Rect(fx, fy, face, face, new Color(0f, 0f, 0f, 0.6f));
            Render.Text(fx, fy, face, face, down ? "×" : cd.ToString("0.0"), TextCol,
                Mathf.RoundToInt((down ? 22f : 15f) * s), TextAnchor.MiddleCenter, true, true);
        }

        // 切り替えキーの札 (顔の左下)
        if (!string.IsNullOrEmpty(key))
        {
            int size = Mathf.RoundToInt(11f * s);
            float kw = Mathf.Max(18f * s, Render.TextWidth(key, size, true) + 10f * s), kh = 16f * s;
            float kx = fx - 6f * s, ky = fy + face - kh + 2f * s;
            Skew(kx, ky, kw, kh, new Color(0.02f, 0.02f, 0.03f, 0.95f));
            Skew(kx, ky + kh - 2f * s, kw, 2f * s, theme);
            Render.Text(kx, ky, kw, kh, key, TextCol, size, TextAnchor.MiddleCenter, true);
        }
    }

    private static void Stats(PlayerController p, out float ratio, out float cur, out float max, out float energy)
    {
        ratio = cur = max = energy = 0f;
        try
        {
            max = p.GetMaxHp();
            cur = PartyManager.IsDown(p) ? 0f : Mathf.Max(0f, p.GetCurHp());
            ratio = max > 0f ? Mathf.Clamp01(cur / max) : 0f;
            energy = Mathf.Clamp01(p.GetCurEnergyPercent());
        }
        catch { }
    }

    private static void HpBar(PlayerController p, float x, float y, float w, float h, float value)
    {
        // 遅れて減る白いゲージ (回復したときはすぐ追いつく)
        TrailHp.TryGetValue(p.Pointer, out float trail);
        trail = value >= trail ? value : Mathf.MoveTowards(trail, value, Time.unscaledDeltaTime * 0.6f);
        TrailHp[p.Pointer] = trail;

        var col = value > 0.3f ? HpCol : HpLowCol;
        Skew(x, y, w, h, new Color(0f, 0f, 0f, 0.7f));
        if (trail > value) Skew(x, y, w * trail, h, new Color(1f, 1f, 1f, 0.85f));
        if (value > 0f)
        {
            Skew(x, y, w * value, h, col);
            Skew(x, y, w * value, h * 0.35f, Color.Lerp(col, Color.white, 0.45f)); // 上側の光沢
        }
    }

    private static void EnergyBar(float x, float y, float w, float h, float value, Color theme)
    {
        Skew(x, y, w, h, new Color(0f, 0f, 0f, 0.7f));
        if (value <= 0f) return;
        var col = theme;
        if (value >= 0.999f)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
            col = Color.Lerp(theme, Color.white, 0.25f + 0.35f * pulse);
        }
        Skew(x, y, w * value, h, col);
    }

    /// <summary>斜めの四角 (平行四辺形)。GUI.matrix にせん断を掛けて、矩形の縦の中心を軸に傾ける</summary>
    private static void Skew(float x, float y, float w, float h, Color color)
    {
        if (w <= 0f || h <= 0f) return;
        var prev = GUI.matrix;
        var m = Matrix4x4.identity;
        float cy = y + h * 0.5f;
        m.m01 = -Slant;
        m.m03 = Slant * cy;
        GUI.matrix = prev * m;
        Render.Rect(x, y, w, h, color);
        GUI.matrix = prev;
    }

    private static readonly Dictionary<double, Color> Themes = new();

    internal static Color ThemeColor(MotionManager mm)
    {
        var fallback = new Color(0.36f, 0.62f, 1f, 1f);
        if (mm == null) return fallback;
        if (Themes.TryGetValue(mm.id, out var c)) return c;
        c = fallback;
        try
        {
            var show = mm.playerShow;
            if (show != null)
            {
                var t = show.themeCol;
                c = new Color(t.r, t.g, t.b, 1f);
            }
        }
        catch { }
        Themes[mm.id] = c;
        return c;
    }

    // ---- 顔アイコン
    // Sprite は大きな画像の一部なので、顔の部分 (正方形) を RenderTexture に一度だけ切り出して描く
    // (GUI.DrawTextureWithTexCoords はこのゲームでは使えないため)

    private static readonly Dictionary<long, RenderTexture> Portraits = new();
    private static readonly Dictionary<long, string> PortraitSource = new();
    private static readonly HashSet<long> PortraitFailed = new();

    public static void DrawPortrait(MotionManager mm, float x, float y, float size, float brightness)
    {
        var tex = Portrait(mm);
        if (tex == null) return;
        var prev = GUI.color;
        GUI.color = new Color(prev.r * brightness, prev.g * brightness, prev.b * brightness, prev.a);
        try { GUI.DrawTexture(new Rect(x, y, size, size), tex, ScaleMode.StretchToFill, true); }
        finally { GUI.color = prev; }
    }

    private static readonly Dictionary<string, Sprite> Sprites = new();
    private static float _spritesAt = -999f;

    /// <summary>読み込まれている画像を名前で探す (一覧は 10 秒に 1 回まで作り直す)</summary>
    private static Sprite FindSprite(string name)
    {
        if (Sprites.TryGetValue(name, out var sp) && sp != null) return sp;
        if (Time.unscaledTime - _spritesAt < 10f) return null;
        _spritesAt = Time.unscaledTime;
        Sprites.Clear();
        try
        {
            foreach (var s in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                var n = s?.name;
                if (n != null && n.StartsWith("avatar_")) Sprites[n] = s;
            }
        }
        catch { }
        return Sprites.TryGetValue(name, out sp) ? sp : null;
    }

    internal static Texture Portrait(MotionManager mm)
    {
        if (mm == null) return null;
        long id = (long)Math.Round(mm.id);
        // ID 付きの顔アイコン (avatar_<ID>) を優先する。PlayerShow2D.portrait は別キャラの画像が入っていることがある
        var sprite = FindSprite($"avatar_{id}");
        Portraits.TryGetValue(id, out var rt);
        if (rt != null && rt.IsCreated() && (sprite == null || PortraitSource.GetValueOrDefault(id) == sprite.name)) return rt;
        if (sprite == null && PortraitFailed.Contains(id)) return null;

        try
        {
            if (sprite == null) sprite = mm.playerShow?.portrait;
            var tex = sprite?.texture;
            if (tex == null)
            {
                PortraitFailed.Add(id);
                PartyManager.Log?.Warning($"Party: {PartyManager.DisplayName(mm)} の顔アイコンがありません");
                return null;
            }

            // 長い辺を切って正方形に (縦長なら上寄せにして顔を残す)
            var r = sprite.textureRect;
            float side = Mathf.Min(r.width, r.height);
            float ox = r.x + (r.width - side) * 0.5f;
            float oy = r.y + (r.height - side); // テクスチャ座標は下が 0 なので、上端に合わせる

            if (rt == null)
            {
                rt = new RenderTexture(160, 160, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                rt.Create();
                Portraits[id] = rt;
            }
            else if (!rt.IsCreated()) rt.Create();

            Graphics.Blit(tex, rt, new Vector2(side / tex.width, side / tex.height),
                new Vector2(ox / tex.width, oy / tex.height));
            PortraitSource[id] = sprite.name;
            return rt;
        }
        catch (Exception e)
        {
            PortraitFailed.Add(id);
            PartyManager.Log?.Warning($"Party: 顔アイコンを作れませんでした ({PartyManager.DisplayName(mm)}): {e.Message}");
            return null;
        }
    }
}

