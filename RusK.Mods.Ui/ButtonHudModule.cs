using System;
using System.Collections.Generic;
using System.Linq;
using RusK.API;
using UnityEngine;
using UnityEngine.InputSystem;
using Module = RusK.API.Module;

using RusK.Mods.Shared;

namespace RusK.Mods.Ui;

/// <summary>
/// ゼンレスゾーンゼロ風のボタンステータス HUD。画面右下に 5 つの丸ボタン
/// (回避 ×2 / 攻撃 / 追加攻撃 / スキル) を並べ、割り当てキー・押下・ゲージを表示する。
///
/// 各ボタンがどの入力アクションか (Action)、何番目のキー割り当てを出すか (KeyIndex)、
/// 周りのリングに何を出すか (Gauge)、表示するか (Show) は設定で変えられる。
/// キー名はゲーム自身のキー設定 (InputController.OnGetBindNameOfAction) から取るので、
/// ゲーム内でキーを変えても追従する。
/// </summary>
public sealed class ButtonHudModule : Module
{
    /// <summary>
    /// ZZZ: ゼンゼロと同じ並び (下の段に 攻撃・回避・スキル・ガード、ガードの上に追加攻撃)
    /// Arc: 攻撃ボタンのまわりに円弧で並べる (v1.2.1 までの ZZZ)
    /// </summary>
    private static readonly string[] Layouts = { "ZZZ", "Arc", "Row", "Column" };
    private static readonly string[] Gauges = { "None", "Ultimate", "Skill" };

    /// <summary>このゲームの入力アクション名の候補 (InputController の OnXxx から推測)</summary>
    internal static readonly string[] CandidateActions =
        { "Dash", "Attack", "SpecialAttack", "QTEAttack", "Defence", "Interection", "ChangePerson", "LockEnemy" };

    private sealed class Slot
    {
        public string Label;
        public Func<Texture2D> Icon;
        public ActionChoiceSetting Action;
        public IntSetting KeyIndex;
        public ModeSetting Gauge;
        public BoolSetting Show;
        public float BaseRadius;
        public Vector2 ZzzOffset; // Arc レイアウトでの位置 (攻撃ボタン中心からの相対, 1080p 基準)
        public Vector2 RowOffset; // ZZZ レイアウトでの位置 (右下のボタン中心からの相対, 1080p 基準)
        public readonly LiquidFill Liquid = new();
        public int LineIndex;     // Row / Column での攻撃ボタンからの距離 (攻撃 = 0)

        // 実行時の状態
        public InputAction InputAction;
        public string KeyText = "--";
        public float Press;       // 押し込みアニメ 0..1
        public float FlashAt = -1f;
        public bool WasPressed;
        public float ReadyUntil = -1f; // 追加攻撃の受付 (一瞬なので、終わってからも少し光を残す)
    }

    private readonly ModeSetting _layout;
    private readonly FloatSetting _posX;
    private readonly FloatSetting _posY;
    private readonly FloatSetting _scale;
    private readonly FloatSetting _spacing;
    private readonly FloatSetting _opacity;
    private readonly ColorSetting _color;
    private readonly BoolSetting _labels;
    private readonly BoolSetting _icons;
    private readonly ModeSetting _show;
    private readonly HudFader _fader = new();
    private readonly List<Slot> _slots = new();

    private float _nextResolve;
    private bool _qteWasReady;
    private float _qteSince;
    private const float QteMaxWindow = 1.5f;

    public ButtonHudModule() : base("ButtonHUD", Categories.Visual, "ZZZ風のボタン表示 (回避・ガード・攻撃・追加攻撃・スキル。追加攻撃は撃てるときに光る)")
    {
        _layout = AddSetting(new ModeSetting("Layout", Layouts, 0,
            "ZZZ: ゼンゼロと同じ並び / Arc: 攻撃ボタンのまわりに円弧 / Row: 横一列 / Column: 縦一列"));
        _posX = AddSetting(new FloatSetting("PosX", 0.92f, 0f, 1f, 0.01f, "0.00", "横位置 (ZZZ: 右下のボタン / それ以外: 攻撃ボタンの中心)"));
        _posY = AddSetting(new FloatSetting("PosY", 0.86f, 0f, 1f, 0.01f, "0.00", "縦位置 (ZZZ: 右下のボタン / それ以外: 攻撃ボタンの中心)"));
        _scale = AddSetting(new FloatSetting("Scale", 1f, 0.4f, 2.5f, 0.05f, "0.00", "全体の大きさ"));
        _spacing = AddSetting(new FloatSetting("Spacing", 1f, 0.6f, 2f, 0.05f, "0.00", "ボタン同士の間隔"));
        _opacity = AddSetting(new FloatSetting("Opacity", 0.85f, 0.2f, 1f, 0.05f, "0.00", "ボタンの不透明度"));
        _color = AddSetting(new ColorSetting("Color", "#5C9EFF", "押したときの色"));
        _labels = AddSetting(new BoolSetting("Labels", true, "ボタン名 (回避・攻撃…) を表示"));
        _icons = AddSetting(new BoolSetting("Icons", true, "アイコンを表示 (OFF にするとキー名を大きく表示)"));
        _show = AddSetting(new ModeSetting("Show", FieldState.ShowModes, 0, FieldState.ShowModeDescription));
        // 既定の割り当て。
        //   ZZZ: 下の段に 攻撃・回避・スキル・ガード (右下が基準)、ガードの上に追加攻撃
        //   Arc: 攻撃ボタンを中心にした半径 118 の円弧 (スキル 90°・追加攻撃 128°・回避 166°・ガード 204°)
        // 回避ボタンには、ExtraKey で追加したキーも「RMB/Shift」のように並べて出す
        const float d = 118f; // ZZZ の下の段のボタンの間隔
        AddSlot("回避", "Dodge", "Dash", 0, 0, 36f, new Vector2(-114f, -29f), new Vector2(-2 * d, 0f), 1, () => Icons.Dodge);
        AddSlot("ガード", "Guard", "Defence", 0, 0, 36f, new Vector2(-108f, 48f), new Vector2(0f, 0f), 2, () => Icons.Guard);
        AddSlot("攻撃", "Attack", "Attack", 0, 0, 48f, new Vector2(0f, 0f), new Vector2(-3 * d, 0f), 0, () => Icons.Attack);
        // ゲームのキー設定の「追加攻撃」は QTEAttack (ガード・回避反撃が成功した直後だけ撃てる)。
        AddSlot("追加攻撃", "FollowUp", "QTEAttack", 0, 0, 36f, new Vector2(-73f, -93f), new Vector2(0f, -1.3f * d), 3, () => Icons.Extra);
        AddSlot("スキル", "Special", "SpecialAttack", 0, 1, 36f, new Vector2(0f, -118f), new Vector2(-d, 0f), 4, () => Icons.Skill);
    }

    public override bool VisibleInArrayList => false;

    private void AddSlot(string label, string key, string action, int keyIndex, int gauge, float radius, Vector2 zzz,
        Vector2 row, int lineIndex, Func<Texture2D> icon)
    {
        _slots.Add(new Slot
        {
            Label = label,
            Icon = icon,
            Show = AddSetting(new BoolSetting($"{key}Show", true, $"「{key}」ボタンを表示する")),
            Action = AddSetting(new ActionChoiceSetting($"{key}Action", action, $"「{key}」に表示する入力アクション")),
            KeyIndex = AddSetting(new IntSetting($"{key}KeyIndex", keyIndex, 0, 3,
                $"「{key}」に表示するキー割り当ての番号 (同じアクションに複数のキーがあるとき)")),
            Gauge = AddSetting(new ModeSetting($"{key}Gauge", Gauges, gauge,
                $"「{key}」の周りに出すゲージ (None / Ultimate: エネルギー (スキルで使う) / Skill: キャラのスキルゲージ)")),
            BaseRadius = radius,
            ZzzOffset = zzz,
            RowOffset = row,
            LineIndex = lineIndex,
        });
    }

    public override void OnEnable() => _nextResolve = 0f;

    public override void OnUpdate()
    {
        // Party のエンドフィールドスタイルでは隠す (Op.2 のスキルボタンが代わりに出る)
        _fader.Update(PlayerRef.Current != null && FieldState.ShouldShow(_show.Value) && !PartyStyle.HideButtonHud);

        if (Time.unscaledTime >= _nextResolve)
        {
            _nextResolve = Time.unscaledTime + 2f;
            ResolveActions();
        }

        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        var player = PlayerRef.Current;
        // 本物の受付は一瞬なので、1.5 秒以上 ○ のままの値は、前の戦闘などから残っているだけとして無視する
        bool raw = QteReady(player);
        if (raw && !_qteWasReady) _qteSince = Time.unscaledTime;
        _qteWasReady = raw;
        bool qte = raw && Time.unscaledTime - _qteSince < QteMaxWindow;
        foreach (var slot in _slots)
        {
            // 追加攻撃 (QTEAttack) の受付中は光らせる。受付は一瞬なので 0.4 秒は光を残す
            if (qte && slot.Action.Value == "QTEAttack") slot.ReadyUntil = Time.unscaledTime + 0.4f;

            bool pressed = false;
            try { pressed = slot.InputAction != null && slot.InputAction.IsPressed(); }
            catch { slot.InputAction = null; }

            if (pressed && !slot.WasPressed) slot.FlashAt = Time.unscaledTime;
            slot.WasPressed = pressed;
            slot.Press = Mathf.MoveTowards(slot.Press, pressed ? 1f : 0f, dt * 14f);
        }
    }

    /// <summary>入力アクションとキー名を取り直す (シーン切り替えで PlayerInput が作り直されるため定期的に)</summary>
    private void ResolveActions()
    {
        InputActionAsset asset = null;
        try { asset = InputController.Instance?.GetPlayerInput()?.actions; }
        catch { }

        ActionChoiceSetting.Available.Clear();
        if (asset != null)
        {
            foreach (var name in CandidateActions)
            {
                try
                {
                    if (asset.FindAction(name, false) != null)
                        ActionChoiceSetting.Available.Add(name);
                }
                catch { }
            }
        }

        foreach (var slot in _slots)
        {
            slot.InputAction = null;
            slot.KeyText = "--";
            if (asset == null || string.IsNullOrEmpty(slot.Action.Value)) continue;
            try
            {
                slot.InputAction = asset.FindAction(slot.Action.Value, false);
                if (slot.InputAction != null)
                    slot.KeyText = Shorten(KeyName(slot.Action.Value, slot.InputAction, slot.KeyIndex.Value));

                // ExtraKey でこの操作にキーを足していれば並べて表示 (例: RMB/Shift)
                var extra = ExtraKeyModule.Instance;
                if (extra != null && extra.Enabled && extra.ActionName == slot.Action.Value && slot.KeyIndex.Value == 0)
                    slot.KeyText += "/" + extra.KeyLabel;
            }
            catch { }
        }
    }

    private static string KeyName(string actionName, InputAction action, int index)
    {
        try
        {
            // ゲーム自身のキー設定画面と同じ名前
            var name = InputController.Instance?.OnGetBindNameOfAction(actionName, index);
            if (!string.IsNullOrEmpty(name)) return name;
        }
        catch { }

        try { return InputActionRebindingExtensions.GetBindingDisplayString(action, index, default); }
        catch { return "?"; }
    }

    /// <summary>"Q [Keyboard]" → "Q"、"Left Button [Mouse]" → "LMB" のように短くする</summary>
    private static string Shorten(string name)
    {
        if (string.IsNullOrEmpty(name)) return "--";
        int bracket = name.IndexOf('[');
        if (bracket > 0) name = name.Substring(0, bracket);
        name = name.Trim();

        switch (name)
        {
            case "Left Button": case "Left Mouse Button": case "Left Click": return "LMB";
            case "Right Button": case "Right Mouse Button": case "Right Click": return "RMB";
            case "Middle Button": case "Middle Mouse Button": return "MMB";
            case "Left Shift": case "Right Shift": return "Shift";
            case "Left Control": case "Left Ctrl": case "Right Control": case "Right Ctrl": return "Ctrl";
            case "Left Alt": case "Right Alt": return "Alt";
            case "Space Bar": return "Space";
        }
        return name.Length > 7 ? name.Substring(0, 7) : name;
    }

    public override void OnGUI()
    {
        if (!Render.IsRepaint) return;
        var player = PlayerRef.Current;
        if (player == null) return;
        _fader.Draw(() => DrawButtons(player, onlyFollowUp: false));

        // スキルの演出中などはゲームが HUD を消すのでボタン HUD も隠れるが、
        // 追加攻撃の受付中は、追加攻撃のボタンだけは必ず出して光らせる
        if (_fader.Alpha < 0.99f && !PartyStyle.HideButtonHud && _slots.Any(sl => Time.unscaledTime < sl.ReadyUntil) && CanForceShow())
            DrawButtons(player, onlyFollowUp: true);
    }

    private void DrawButtons(PlayerController player, bool onlyFollowUp)
    {
        float s = Screen.height / 1080f * _scale.Value;
        float anchorX = _posX.Value * Screen.width;
        float anchorY = _posY.Value * Screen.height;

        // Row / Column では、非表示のボタンを詰めて並べる
        int line = 0;
        foreach (var slot in _slots.OrderBy(sl => sl.LineIndex))
        {
            if (!slot.Show.Value) continue;
            var offset = LayoutOffset(slot, line++) * _spacing.Value * s;
            float r = _layout.Value switch { 0 => 44f, 1 => slot.BaseRadius, _ => 40f } * s;
            bool followUp = Time.unscaledTime < slot.ReadyUntil;
            if (onlyFollowUp && !followUp) continue;
            float? gauge = GaugeValue(slot.Gauge.Value, player);
            DrawButton(slot, anchorX + offset.x, anchorY + offset.y, r, gauge, s, followUp);
        }
    }

    /// <summary>
    /// 攻撃ボタンの中心からの相対位置 (1080p 基準)。
    /// Row / Column は攻撃が基準点 (右端・下端) で、回避 → 回避2 → 追加攻撃 → スキル の順に左 / 上へ並べる
    /// </summary>
    private Vector2 LayoutOffset(Slot slot, int line) => _layout.Value switch
    {
        0 => slot.RowOffset,
        1 => slot.ZzzOffset,
        2 => new Vector2(-line * 96f, 0f),
        _ => new Vector2(0f, -line * 96f),
    };

    /// <summary>
    /// HUD が隠れていても追加攻撃のボタンだけ出してよいか。ムービーや会話の間も「撃てる」の値が残ることがあるので、
    /// 操作できない (入力が止められている)・ムービーや会話の最中・ロード中やウィンドウ表示中は出さない
    /// </summary>
    private static bool CanForceShow()
    {
        try
        {
            if (!FieldState.OnField) return false;
            // ゲームが戦闘用の UI を出していないとき (戦闘の前後の演出など) は出さない
            if (BattleUi.Observed && !BattleUi.Active) return false;
            var input = InputController.Instance;
            if (input != null && input.GetLockInput()) return false;
            var cam = CameraController.Instance;
            if (cam != null && (cam.IsTimelinePlaying() || cam.IsDialogueWindowOpen())) return false;
            var util = GameUtil.Instance;
            if (util != null && util.IsInDialogue()) return false;
        }
        catch { return false; }
        return true;
    }

    /// <summary>今、追加攻撃 (QTEAttack) を撃てるか (ゲーム自身の判定)</summary>
    private static bool QteReady(PlayerController player)
    {
        if (player == null) return false;
        try { return player.QTEAttackAble(); }
        catch { return false; }
    }

    private static float? GaugeValue(int gauge, PlayerController player) => gauge switch
    {
        1 => player == null ? null : Mathf.Clamp01(player.GetCurEnergyPercent()),
        2 => SkillGauge.Read(),
        _ => null,
    };

    private void DrawButton(Slot slot, float cx, float cy, float radius, float? gauge, float s, bool followUp = false)
    {
        float op = _opacity.Value;
        float now = Time.unscaledTime;
        var accent = _color.Value;
        bool ready = gauge is >= 0.999f || followUp;
        float pulse = Mathf.Sin(now * 5f) * 0.5f + 0.5f;

        float r = radius * (1f - 0.08f * slot.Press); // 押すと少し縮む

        // 満タン時の外側の発光
        if (ready)
        {
            float g = r + (6f + pulse * 5f) * s;
            Render.Rect(cx - g, cy - g, g * 2, g * 2, new Color(1f, 0.8f, 0.3f, (0.10f + 0.15f * pulse) * op), g);
        }

        // 追加攻撃の受付中は、もう一回り大きく強く光らせて READY! を出す
        if (followUp)
        {
            float g2 = r + (12f + pulse * 6f) * s;
            Render.Rect(cx - g2, cy - g2, g2 * 2, g2 * 2, new Color(1f, 0.85f, 0.35f, (0.18f + 0.2f * pulse) * op), g2);
            int rfs = Mathf.RoundToInt(Mathf.Max(10f, r * 0.34f));
            Render.Text(cx - r * 2f, cy - r - rfs - 10f * s, r * 4f, rfs + 4f * s, "READY!", new Color(1f, 0.86f, 0.4f, op),
                rfs, TextAnchor.MiddleCenter, bold: true, shadow: true);
        }

        // 本体
        Render.Rect(cx - r - 2f * s, cy - r - 2f * s, (r + 2f * s) * 2, (r + 2f * s) * 2,
            new Color(0f, 0f, 0f, 0.45f * op), r + 2f * s);
        Render.Rect(cx - r, cy - r, r * 2, r * 2, new Color(0.07f, 0.08f, 0.10f, 0.82f * op), r);
        if (slot.Press > 0f)
            Render.Rect(cx - r, cy - r, r * 2, r * 2, Render.WithAlpha(accent, 0.45f * slot.Press * op), r);
        Ring(cx, cy, r, Mathf.Max(1.5f, 2f * s), new Color(1f, 1f, 1f, (0.22f + 0.3f * slot.Press) * op));

        // ゲージ: ボタンの中に液体がたまっていく (液面は波で揺れる)
        if (gauge.HasValue && Render.IsRepaint)
        {
            Color deep, top;
            if (ready) { deep = new Color(1f, 0.55f, 0.12f); top = new Color(1f, 0.9f, 0.45f); }
            else if (slot.Gauge.Value == 1) { deep = new Color(0.12f, 0.3f, 0.95f); top = new Color(0.35f, 0.8f, 1f); }
            else { deep = new Color(0.05f, 0.55f, 0.65f); top = new Color(0.4f, 1f, 0.9f); }
            var tex = slot.Liquid.Draw(gauge.Value, now, deep, top);
            var prev = GUI.color;
            GUI.color = new Color(prev.r, prev.g, prev.b, prev.a * op);
            GUI.DrawTexture(new Rect(cx - r, cy - r, r * 2, r * 2), tex, ScaleMode.StretchToFill, true);
            GUI.color = prev;
            Ring(cx, cy, r, Mathf.Max(2f, 3f * s), Render.WithAlpha(top, 0.55f * op));
        }

        // 押した瞬間に広がるリング
        float flash = now - slot.FlashAt;
        if (slot.FlashAt >= 0f && flash < 0.3f)
        {
            float k = flash / 0.3f;
            Ring(cx, cy, r + k * 14f * s, Mathf.Max(1.5f, 2.5f * s), Render.WithAlpha(accent, (1f - k) * 0.8f * op));
        }

        var white = new Color(1f, 1f, 1f, op);
        Texture2D icon = null;
        if (_icons.Value)
        {
            try { icon = slot.Icon(); }
            catch { icon = null; }
        }

        if (icon != null)
        {
            // アイコン (満タン時は金色、押している間はアクセント色寄り)
            float iconSize = r * 1.1f;
            var tint = ready ? new Color(1f, 0.86f, 0.45f, op) : Color.Lerp(white, Render.WithAlpha(accent, op), slot.Press * 0.6f);
            Render.Image(cx - iconSize * 0.5f, cy - iconSize * 0.5f - r * 0.05f, iconSize, iconSize, icon, tint);

            // キー名は小さなタグに。ZZZ ではボタンの下に離して、それ以外はボタン下端に重ねて
            bool zzz = _layout.Value == 0;
            int fs = Mathf.RoundToInt(Mathf.Max(9f, r * (zzz ? 0.3f : 0.26f)));
            float tw = Mathf.Max(fs + 14f * s, Render.TextWidth(slot.KeyText, fs, bold: true) + 16f * s);
            float th = fs + 10f * s;
            float tx = cx - tw * 0.5f, ty = zzz ? cy + r + 8f * s : cy + r - th * 0.6f;
            Render.Rect(tx, ty, tw, th, new Color(0.04f, 0.05f, 0.07f, 0.92f * op), th * 0.5f);
            Render.Rect(tx, ty, tw, th, Render.WithAlpha(accent, 0.25f * op), th * 0.5f);
            Render.Text(tx, ty, tw, th, slot.KeyText, white, fs, TextAnchor.MiddleCenter, bold: true);
        }
        else
        {
            // アイコンなし: キー名を大きく中央に
            int keySize = Mathf.RoundToInt(r * (slot.KeyText.Length <= 2 ? 0.55f : slot.KeyText.Length <= 4 ? 0.40f : 0.30f));
            Render.Text(cx - r, cy - r, r * 2, r * 2, slot.KeyText, white, keySize, TextAnchor.MiddleCenter,
                bold: true, shadow: true);
        }

        // ボタン名はボタンの下に
        if (_labels.Value)
        {
            int fs = Mathf.RoundToInt(Mathf.Max(9f, r * 0.26f));
            float ly = cy + r + (icon != null ? (_layout.Value == 0 ? 8f + fs + 16f : 10f) : 4f) * s;
            Render.Text(cx - r * 1.5f, ly, r * 3f, fs + 6f * s, L.T(slot.Label), new Color(0.85f, 0.9f, 0.95f, 0.95f * op),
                fs, TextAnchor.MiddleCenter, bold: true, shadow: true);
        }

        // ゲージが満タンでないときは % を右上に小さく
        if (gauge.HasValue && !ready && _labels.Value)
            Render.Text(cx + r * 0.55f, cy - r * 1.3f, r * 1.4f, r * 0.5f, $"{Mathf.RoundToInt(gauge.Value * 100f)}%",
                new Color(1f, 1f, 1f, 0.8f * op), Mathf.RoundToInt(Mathf.Max(9f, r * 0.24f)), TextAnchor.MiddleLeft,
                shadow: true);
    }

    /// <summary>輪郭だけの円</summary>
    private static void Ring(float cx, float cy, float r, float width, Color color)
    {
        if (!Render.IsRepaint) return;
        GUI.DrawTexture(new Rect(cx - r, cy - r, r * 2, r * 2), Texture2D.whiteTexture, ScaleMode.StretchToFill,
            true, 0f, color, width, r);
    }

    /// <summary>点を円周に並べたゲージ。真上から時計回りに value の割合だけ塗る</summary>
    private static void DotRing(float cx, float cy, float r, float dot, float value, Color fill, Color track)
    {
        const int count = 40;
        for (int i = 0; i < count; i++)
        {
            float t = i / (float)count;
            float ang = (90f - 360f * t) * Mathf.Deg2Rad;
            float px = cx + Mathf.Cos(ang) * r - dot * 0.5f;
            float py = cy - Mathf.Sin(ang) * r - dot * 0.5f;
            Render.Rect(px, py, dot, dot, t < value ? fill : track, dot * 0.5f);
        }
    }
}

/// <summary>
/// 入力アクション名を選ぶ設定。←/→ で、ゲームに実際にあるアクションを順に切り替える
/// (ゲーム外でまだ取得できていないときは候補の一覧から選ぶ)。
/// </summary>
internal sealed class ActionChoiceSetting : Setting<string>, ICycleSetting
{
    internal static readonly List<string> Available = new();

    public ActionChoiceSetting(string name, string defaultValue, string description) : base(name, description)
    {
        Default = defaultValue;
        Value = defaultValue;
    }

    private static IReadOnlyList<string> Options =>
        Available.Count > 0 ? Available : ButtonHudModule.CandidateActions;

    public void Next() => Step(+1);
    public void Previous() => Step(-1);

    private void Step(int dir)
    {
        var options = Options;
        int i = options.ToList().IndexOf(Value);
        Value = options[i < 0 ? 0 : (i + dir + options.Count) % options.Count];
    }

    public override string Serialize() => Value ?? "";

    public override bool Deserialize(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        Value = text;
        return true;
    }
}
