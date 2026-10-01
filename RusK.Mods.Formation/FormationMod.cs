using System.Collections.Generic;
using System.Linq;
using RusK.API;
using UnityEngine;
using Module = RusK.API.Module;

namespace RusK.Mods.Formation;

/// <summary>
/// Party Formation: Party の編成を、ゼンゼロの編成画面のようなカードで変える GUI だけの Mod (Party Mod が必要)。
/// Party の中身は PartyBridge (版 3) をリフレクションで使う (PartyLink)
/// </summary>
[RuskMod("formation", "Party Formation", "1.0.0",
    Author = "you",
    GameVersion = "0.0.1876",
    Description = "Party の編成画面 (ゼンゼロ風のカード)。仲間の選択とバトルスタイルの切り替え (Party Mod が必要)")]
public sealed class FormationMod : RuskMod
{
    protected override void OnLoad()
    {
        var window = new FormationWindow();
        Context.RegisterWindow(window);
        Context.RegisterModule(new FormationModule(window));
        Context.RegisterAction("FormationOpen", () => window.Toggle(), "パーティ編成の画面を開く / 閉じる");
    }
}

/// <summary>ON で編成画面を開く (メニューの「Formation」)</summary>
public sealed class FormationModule : Module
{
    private readonly FormationWindow _window;

    public FormationModule(FormationWindow window)
        : base("PartyFormation", "Formation", "パーティ編成の画面を開く (仲間の選択・バトルスタイル)")
    {
        _window = window;
        _window.VisibleChanged += w => Enabled = w.Visible;
    }

    public override bool VisibleInArrayList => false;
    public override void OnEnable() => _window.Visible = true;
    public override void OnDisable() => _window.Visible = false;
}

/// <summary>
/// 編成画面。ゼンゼロの編成画面のように、斜めのカードを 3 枚並べる (1 枚目はリーダー、2・3 枚目は仲間)。
/// 仲間のカードをクリックすると、下にキャラの一覧が出て選べる。下の帯でバトルスタイルを切り替える
/// </summary>
public sealed class FormationWindow : RuskWindow
{
    private const float Slant = 0.12f; // カードの傾き (上に行くほど右へずれる量 / 高さ)

    private static readonly Color CardBg = new(0.07f, 0.08f, 0.1f, 0.95f);
    private static readonly Color CardBgHover = new(0.11f, 0.12f, 0.15f, 0.95f);
    private static readonly Color TextCol = new(0.97f, 0.97f, 0.99f, 1f);
    private static readonly Color DimCol = new(0.62f, 0.65f, 0.72f, 1f);
    private static readonly Color Accent = new(1f, 0.82f, 0.25f, 1f);

    private int _picking = -1; // 選んでいる仲間の枠 (-1: なし)

    public FormationWindow() : base("formation", "パーティ編成", 860f, 620f)
    {
        MinWidth = 560f;
        MinHeight = 420f;
    }

    protected override void OnClose() => _picking = -1;

    public override void Draw(WindowGui gui)
    {
        if (!PartyLink.Available)
        {
            gui.Label(PartyLink.Problem ?? L.T("Party Mod とつないでいます…"), RuskStyle.TextDim);
            return;
        }

        bool fight = PartyLink.InFight;
        if (fight) _picking = -1;
        gui.Label(fight ? L.T("戦闘中は編成を変更できません。拠点に戻ると変更できます。")
                        : L.T("仲間のカードをクリックすると、キャラを選べます。リーダーは拠点のキャラ選択で変わります。"),
            RuskStyle.TextDim, small: true);

        float s = gui.Scale;
        var area = gui.Next(330f * s);
        DrawCards(area, fight, s);

        if (_picking >= 0) DrawPicker(gui, s);

        gui.Space(6f);
        DrawStyle(gui, s);

        gui.Space(4f);
        if (gui.Button(L.T("閉じる"), accent: true)) Visible = false;
    }

    // ------------------------------------------------------------------ カード

    private void DrawCards(Rect area, bool fight, float s)
    {
        int max = PartyLink.MaxCompanions;
        int count = 1 + max;
        float gap = 18f * s;
        float w = (area.width - gap * (count - 1) - area.height * Slant) / count;
        var companions = PartyLink.Companions;
        var leader = PartyLink.Leader;
        var members = fight ? PartyLink.Members : new List<PlayerController>();
        var cur = PartyLink.Current;

        for (int i = 0; i < count; i++)
        {
            var r = new Rect(area.x + i * (w + gap), area.y, w, area.height);
            MotionManager mm;
            string role;
            if (i == 0)
            {
                mm = leader != null ? PartyLink.FindCharacter(PartyLink.Id(leader)) : null;
                role = L.T("リーダー");
            }
            else
            {
                mm = i - 1 < companions.Count ? PartyLink.FindCharacter(companions[i - 1]) : null;
                role = L.T("仲間 {0}", i);
            }

            // 戦闘中は、そのキャラの今の様子
            string state = null;
            if (fight && mm != null)
            {
                var m = members.FirstOrDefault(p => PartyLink.Same(PartyLink.Id(p), mm.id));
                state = m == null ? L.T("不参加")
                    : PartyLink.IsDown(m) ? L.T("戦闘不能")
                    : cur != null && m.Pointer == cur.Pointer ? L.T("操作中") : L.T("控え");
            }

            bool clickable = !fight && i > 0;
            bool hover = clickable && Hover(r);
            DrawCard(r, mm, role, state, hover, _picking == i - 1 && i > 0, s);

            if (clickable)
            {
                // 外すボタン (仲間がいる枠だけ)
                if (mm != null)
                {
                    var rm = new Rect(r.x + r.width - 64f * s, r.y + r.height - 34f * s, 56f * s, 24f * s);
                    bool h = Hover(rm);
                    Render.Rect(rm.x, rm.y, rm.width, rm.height, new Color(0f, 0f, 0f, h ? 0.85f : 0.6f), 4f * s);
                    Render.Text(rm.x, rm.y, rm.width, rm.height, L.T("外す"), h ? Accent : TextCol, Mathf.RoundToInt(12f * s), TextAnchor.MiddleCenter, true);
                    if (Clicked(rm))
                    {
                        PartyLink.RemoveCompanion(mm.id);
                        _picking = -1;
                        continue;
                    }
                }
                if (Clicked(r)) _picking = _picking == i - 1 ? -1 : i - 1;
            }
        }
    }

    private static void DrawCard(Rect r, MotionManager mm, string role, string state, bool hover, bool picking, float s)
    {
        var theme = mm != null ? PartyLink.ThemeOf(mm) : new Color(0.3f, 0.32f, 0.38f, 1f);

        // 斜めのカード: 下地 → テーマカラーの帯
        Skew(r.x, r.y, r.width, r.height, hover ? CardBgHover : CardBg);
        Skew(r.x, r.y, r.width, 6f * s, theme);
        Skew(r.x, r.y + r.height - 4f * s, r.width, 4f * s, Render.WithAlpha(theme, 0.8f));
        if (picking) SkewOutline(r, Accent, 3f * s);

        // 役割 (左上)
        int fs = Mathf.RoundToInt(12f * s);
        float rw = Render.TextWidth(role, fs, true) + 16f * s;
        Skew(r.x + 10f * s, r.y + 16f * s, rw, 22f * s, Render.WithAlpha(theme, 0.9f));
        Render.Text(r.x + 10f * s, r.y + 16f * s, rw, 22f * s, role, TextCol, fs, TextAnchor.MiddleCenter, true);

        if (mm == null)
        {
            // 空の枠
            Render.Text(r.x, r.y, r.width, r.height - 40f * s, "+", DimCol, Mathf.RoundToInt(64f * s), TextAnchor.MiddleCenter, true);
            Render.Text(r.x, r.y + r.height * 0.62f, r.width, 24f * s, L.T("仲間を選ぶ"), DimCol, Mathf.RoundToInt(14f * s), TextAnchor.MiddleCenter, true);
            return;
        }

        // 顔 (大きく)
        float face = Mathf.Min(r.width * 0.78f, r.height * 0.58f);
        float fx = r.x + (r.width - face) * 0.5f + r.height * Slant * 0.5f, fy = r.y + 48f * s;
        Render.Rect(fx - 3f * s, fy - 3f * s, face + 6f * s, face + 6f * s, Render.WithAlpha(theme, 0.85f), 10f * s);
        var tex = PartyLink.PortraitOf(mm);
        if (tex != null) GUI.DrawTexture(new Rect(fx, fy, face, face), tex, ScaleMode.StretchToFill, true, 0f, GUI.color, 0f, 8f * s);

        // 名前・様子
        float ny = fy + face + 12f * s;
        Render.Text(r.x + 12f * s, ny, r.width - 24f * s, 30f * s, PartyLink.DisplayName(mm), TextCol,
            Mathf.RoundToInt(22f * s), TextAnchor.MiddleCenter, true, true);
        if (state != null)
            Render.Text(r.x + 12f * s, ny + 30f * s, r.width - 24f * s, 20f * s, state, DimCol,
                Mathf.RoundToInt(13f * s), TextAnchor.MiddleCenter, true, true);
    }

    // ------------------------------------------------------------------ キャラの一覧 (仲間の選択)

    private void DrawPicker(WindowGui gui, float s)
    {
        gui.Header(L.T("仲間 {0} を選ぶ", _picking + 1), L.T("クリックで決定"));
        var leader = PartyLink.Leader;
        double leaderId = leader != null ? PartyLink.Id(leader) : -1;
        var companions = PartyLink.Companions;
        var list = PartyLink.Characters.Where(c => c != null && !PartyLink.Same(c.id, leaderId)).ToList();
        if (list.Count == 0)
        {
            gui.Label(L.T("キャラ一覧を取得できません (ゲームに入ってから開いてください)"), RuskStyle.TextDim);
            return;
        }

        float cell = 92f * s, gap = 10f * s;
        float width = gui.ContentWidth;
        int perRow = Mathf.Max(1, Mathf.FloorToInt((width + gap) / (cell + gap)));
        int rows = Mathf.CeilToInt(list.Count / (float)perRow);
        var area = gui.Next(rows * (cell + 22f * s + gap));

        for (int k = 0; k < list.Count; k++)
        {
            var c = list[k];
            float x = area.x + (k % perRow) * (cell + gap), y = area.y + (k / perRow) * (cell + 22f * s + gap);
            var r = new Rect(x, y, cell, cell + 22f * s);
            int inSlot = companions.FindIndex(id => PartyLink.Same(id, c.id));
            bool hover = Hover(r);
            var theme = PartyLink.ThemeOf(c);
            Render.Rect(r.x, r.y, r.width, r.height, hover ? CardBgHover : CardBg, 6f * s);
            if (inSlot >= 0) Render.Rect(r.x, r.y, r.width, 3f * s, theme, 2f * s);
            var tex = PartyLink.PortraitOf(c);
            float face = cell - 12f * s;
            if (tex != null)
                GUI.DrawTexture(new Rect(r.x + 6f * s, r.y + 6f * s, face, face), tex, ScaleMode.StretchToFill, true, 0f,
                    inSlot >= 0 && inSlot != _picking ? new Color(0.6f, 0.6f, 0.6f, GUI.color.a) : GUI.color, 0f, 6f * s);
            Render.Text(r.x, r.y + cell - 2f * s, r.width, 22f * s, PartyLink.DisplayName(c), hover ? Accent : TextCol,
                Mathf.RoundToInt(12f * s), TextAnchor.MiddleCenter, true);
            if (inSlot >= 0)
                Render.Text(r.x + 8f * s, r.y + 8f * s, r.width, 18f * s, L.T("仲間 {0}", inSlot + 1), TextCol,
                    Mathf.RoundToInt(11f * s), TextAnchor.UpperLeft, true, true);

            if (Clicked(r))
            {
                PartyLink.SetCompanion(_picking, c.id);
                _picking = -1;
                return;
            }
        }
    }

    // ------------------------------------------------------------------ バトルスタイル

    private static void DrawStyle(WindowGui gui, float s)
    {
        bool endfield = PartyLink.EndfieldInstalled;
        gui.Header(L.T("バトルスタイル"), endfield ? null : L.T("エンドフィールドには Party Op.2 が必要です"));
        var row = gui.Next(40f * s);
        string[] names = { L.T("ゼンゼロ"), L.T("エンドフィールド") };
        string[] notes = { L.T("控えは隠れて交代する"), L.T("全員がフィールドで戦い、操作していないキャラはオート") };
        int style = PartyLink.BattleStyle;
        float w = (row.width - 8f * s) / 2f;
        for (int i = 0; i < 2; i++)
        {
            var r = new Rect(row.x + i * (w + 8f * s), row.y, w, row.height);
            bool enabled = i == 0 || endfield;
            bool on = style == i;
            bool hover = enabled && Hover(r);
            Render.Rect(r.x, r.y, r.width, r.height,
                on ? Render.WithAlpha(RuskStyle.Accent, 0.45f) : hover ? RuskStyle.RowHover : RuskStyle.Row, 4f * s);
            if (on) Render.Rect(r.x, r.y + r.height - 3f * s, r.width, 3f * s, RuskStyle.Accent);
            Render.Text(r.x + 10f * s, r.y + 2f * s, r.width - 20f * s, 20f * s, names[i], enabled ? TextCol : DimCol,
                Mathf.RoundToInt(14f * s), TextAnchor.MiddleLeft, true);
            Render.Text(r.x + 10f * s, r.y + 20f * s, r.width - 20f * s, 18f * s, notes[i], DimCol,
                Mathf.RoundToInt(11f * s), TextAnchor.MiddleLeft);
            if (enabled && !on && Clicked(r)) PartyLink.SetBattleStyle(i);
        }
    }

    // ------------------------------------------------------------------ 描画・入力の部品

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

    private static void SkewOutline(Rect r, Color color, float t)
    {
        var prev = GUI.matrix;
        var m = Matrix4x4.identity;
        float cy = r.y + r.height * 0.5f;
        m.m01 = -Slant;
        m.m03 = Slant * cy;
        GUI.matrix = prev * m;
        Render.Outline(r.x, r.y, r.width, r.height, color, t);
        GUI.matrix = prev;
    }

    private static bool Hover(Rect r)
    {
        var e = Event.current;
        return e != null && r.Contains(e.mousePosition);
    }

    private static bool Clicked(Rect r)
    {
        var e = Event.current;
        if (e == null || e.type != EventType.MouseDown || e.button != 0 || !r.Contains(e.mousePosition)) return false;
        e.Use();
        return true;
    }
}
