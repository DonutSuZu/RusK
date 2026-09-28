using System.Collections.Generic;
using RusK.API;
using UnityEngine;

namespace RusK.Core.UI;

/// <summary>
/// Horion 風の TabGUI。画面左上に出るキーボード操作のメニュー。
///   ↑↓: 選択 / →・Enter: 開く・ON/OFF / ←・Backspace: 戻る
///   設定の列では ←→ で値を変更、Enter でキー割り当て・ボタン実行
/// 列は「カテゴリ → 項目 → 設定」の 3 段。
/// </summary>
internal sealed class TabGui
{
    private readonly Theme _t;
    private readonly MenuModel _model;
    private readonly Dictionary<KeyCode, float> _repeatAt = new();

    private int _level;   // 0: カテゴリ, 1: 項目, 2: 設定
    private int _cat, _entry, _row;

    public TabGui(Theme theme, MenuModel model)
    {
        _t = theme;
        _model = model;
    }

    /// <summary>Update から呼ぶ。キー入力でカーソルを動かす</summary>
    public void HandleInput()
    {
        var cats = _model.Categories;
        if (cats.Count == 0) return;
        _cat = Wrap(_cat, cats.Count);
        var entries = _model.Entries(cats[_cat]);

        bool up = Repeat(KeyCode.UpArrow), down = Repeat(KeyCode.DownArrow);
        bool left = Repeat(KeyCode.LeftArrow), right = Repeat(KeyCode.RightArrow);
        bool enter = NewInput.WasPressedThisFrame(KeyCode.Return) || NewInput.WasPressedThisFrame(KeyCode.KeypadEnter);
        bool back = NewInput.WasPressedThisFrame(KeyCode.Backspace);

        switch (_level)
        {
            case 0:
                if (up) _cat = Wrap(_cat - 1, cats.Count);
                if (down) _cat = Wrap(_cat + 1, cats.Count);
                if ((right || enter) && _model.Entries(cats[_cat]).Count > 0)
                {
                    _level = 1;
                    _entry = 0;
                }
                break;

            case 1:
            {
                if (entries.Count == 0) { _level = 0; break; }
                _entry = Wrap(_entry, entries.Count);
                if (up) _entry = Wrap(_entry - 1, entries.Count);
                if (down) _entry = Wrap(_entry + 1, entries.Count);
                if (left || back) { _level = 0; break; }

                var e = entries[_entry];
                if (enter)
                {
                    if (e.Kind == EntryKind.Expand) OpenRows(e);
                    else e.Activate();
                }
                else if (right)
                {
                    if (e.Rows.Count > 0) OpenRows(e);
                    else if (e.Kind == EntryKind.Command) e.Activate();
                }
                break;
            }

            case 2:
            {
                if (entries.Count == 0) { _level = 0; break; }
                _entry = Wrap(_entry, entries.Count);
                var rows = entries[_entry].Rows;
                if (rows.Count == 0) { _level = 1; break; }
                _row = Wrap(_row, rows.Count);

                if (up) _row = Wrap(_row - 1, rows.Count);
                if (down) _row = Wrap(_row + 1, rows.Count);
                if (back) { _level = 1; break; }
                if (left) rows[_row].Adjust(-1);
                if (right) rows[_row].Adjust(+1);
                if (enter) rows[_row].Activate();
                break;
            }
        }
    }

    private void OpenRows(IMenuEntry e)
    {
        if (e.Rows.Count == 0) return;
        _level = 2;
        _row = 0;
    }

    public void Draw()
    {
        var cats = _model.Categories;
        if (cats.Count == 0) return;
        _cat = Wrap(_cat, cats.Count);

        float s = _t.Scale;
        float x = 8f * s, y = 8f * s;
        float rowH = _t.ModuleHeight;
        float gap = 4f * s;
        float catW = 110f * s, entryW = 170f * s, rowW = 220f * s;

        // タイトル
        Render.Rect(x, y, catW, _t.HeaderHeight, _t.Header);
        Render.Rect(x, y + _t.HeaderHeight - 2f, catW, 2f, _t.Accent());
        Render.Text(x + 8f * s, y, catW, _t.HeaderHeight, Rusk.Name, _t.Text, _t.FontSize, TextAnchor.MiddleLeft, bold: true);
        float top = y + _t.HeaderHeight;
        float bottom = top;

        // 列 0: カテゴリ
        for (int i = 0; i < cats.Count; i++)
        {
            bool sel = i == _cat;
            float ry = top + i * rowH;
            DrawRowBg(x, ry, catW, rowH, sel, _level == 0);
            Render.Text(x + 10f * s, ry, catW - 20f * s, rowH, RuskLang.T(RuskLang.CoreId, cats[i]), sel ? _t.Text : _t.TextDim, _t.FontSize);
            if (sel && _level >= 1)
                Render.Text(x, ry, catW - 6f * s, rowH, "›", _t.Accent(), _t.FontSize, TextAnchor.MiddleRight);
        }
        bottom = Mathf.Max(bottom, top + cats.Count * rowH);

        string description = null;

        // 列 1: 項目
        if (_level >= 1)
        {
            var entries = _model.Entries(cats[_cat]);
            _entry = entries.Count == 0 ? 0 : Wrap(_entry, entries.Count);
            float ex = x + catW + gap;

            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                bool sel = i == _entry;
                float ry = top + i * rowH;
                DrawRowBg(ex, ry, entryW, rowH, sel, _level == 1);
                if (e.Active) Render.Rect(ex, ry, 2.5f, rowH, _t.Accent(i * 0.05f));

                var color = e.Active ? _t.Accent(i * 0.05f) : e.Dim ? _t.TextDim : _t.Text;
                Render.Text(ex + 10f * s, ry, entryW - 20f * s, rowH, L.T(e.Label), color, _t.FontSize);

                string right = e.Suffix ?? (e.Rows.Count > 0 ? "›" : null);
                if (right != null)
                    Render.Text(ex, ry, entryW - 8f * s, rowH, right, _t.TextDim, _t.SmallFontSize, TextAnchor.MiddleRight);

                if (sel) description = L.T(e.Description);
            }
            bottom = Mathf.Max(bottom, top + entries.Count * rowH);

            // 列 2: 設定
            if (_level >= 2 && entries.Count > 0)
            {
                var rows = entries[_entry].Rows;
                _row = rows.Count == 0 ? 0 : Wrap(_row, rows.Count);
                float rx = ex + entryW + gap;

                for (int i = 0; i < rows.Count; i++)
                {
                    var r = rows[i];
                    bool sel = i == _row;
                    float ry = top + i * rowH;
                    DrawRowBg(rx, ry, rowW, rowH, sel, true);
                    DrawSettingRow(r, rx, ry, rowW, rowH, sel);
                    if (sel) description = r.TDescription;
                }
                bottom = Mathf.Max(bottom, top + rows.Count * rowH);
            }
        }

        // 選択中の項目の説明
        if (!string.IsNullOrEmpty(description))
            Render.Text(x + 2f, bottom + 4f * s, 900f * s, rowH, description, _t.Text, _t.SmallFontSize,
                TextAnchor.MiddleLeft, shadow: true);
    }

    private void DrawSettingRow(Row r, float x, float y, float w, float h, bool sel)
    {
        float s = _t.Scale;
        float pad = 10f * s;
        Render.Text(x + pad, y, w - pad * 2, h, r.TLabel, sel ? _t.Text : _t.TextDim, _t.SmallFontSize);

        float valueRight = w - pad;
        if (r.Swatch.HasValue)
        {
            float sw = h - 8f * s;
            Render.Rect(x + w - pad - sw, y + 4f * s, sw, sw, r.Swatch.Value, 2f * s);
            valueRight -= sw + 6f * s;
        }

        var valueColor = r.Capturing ? _t.Accent() : sel ? _t.Accent() : _t.Text;
        string value = sel && r.Normalized == null && !r.Capturing && !(r is ActionRow) ? $"‹ {r.TValue} ›" : r.TValue;
        Render.Text(x, y, valueRight, h, value, valueColor, _t.SmallFontSize, TextAnchor.MiddleRight);

        if (r.Normalized is float n)
        {
            float bx = x + pad, bw = w - pad * 2;
            Render.Rect(bx, y + h - 3f, bw, 2f, _t.SliderTrack);
            Render.Rect(bx, y + h - 3f, bw * Mathf.Clamp01(n), 2f, _t.Accent());
        }
    }

    private void DrawRowBg(float x, float y, float w, float h, bool selected, bool focused)
    {
        Render.Rect(x, y, w, h, _t.Background);
        if (!selected) return;
        Render.Rect(x, y, w, h, _t.Selected(focused ? 0.28f : 0.12f));
        Render.Rect(x, y, 2f, h, _t.Accent());
    }

    /// <summary>押しっぱなしでリピートするキー入力 (最初 0.35 秒待ってから 0.06 秒ごと)</summary>
    private bool Repeat(KeyCode key)
    {
        float now = Time.unscaledTime;
        if (NewInput.WasPressedThisFrame(key))
        {
            _repeatAt[key] = now + 0.35f;
            return true;
        }
        if (NewInput.IsPressed(key) && _repeatAt.TryGetValue(key, out var at) && now >= at)
        {
            _repeatAt[key] = now + 0.06f;
            return true;
        }
        return false;
    }

    private static int Wrap(int i, int count) => count == 0 ? 0 : ((i % count) + count) % count;
}
