using System.Collections.Generic;
using RusK.API;
using UnityEngine;

namespace RusK.Core.UI;

/// <summary>
/// Horion 風の ClickGUI (マウス操作)。Visual > Menu の GUI を "Click" にすると使われる。
/// カテゴリごとにパネルを並べ、左クリックで ON/OFF・実行、右クリックで設定の開閉。
/// パネルはドラッグで移動でき、位置は Config に保存される。
/// </summary>
internal sealed class ClickGui
{
    private readonly Theme _t;
    private readonly MenuModel _model;
    private readonly Mouse _mouse = new();
    private readonly HashSet<IMenuEntry> _expanded = new();

    private string _dragPanel;
    private Vector2 _dragOffset;

    // スライダーのドラッグ中の行と、その位置
    private Row _dragRow;
    private float _dragX, _dragW;

    public ClickGui(Theme theme, MenuModel model)
    {
        _t = theme;
        _model = model;
    }

    public void OnClose()
    {
        _dragPanel = null;
        _dragRow = null;
        HotkeyCapture.Cancel();
    }

    public void Draw(WindowManager windows)
    {
        _mouse.Sample();
        // ウィンドウの上でのクリックは、下にあるパネルに通さない
        if (windows.IsPointerOver(_mouse.Position)) _mouse.Suppress();

        // 背景を少し暗くしてメニューを見やすく
        Render.Rect(0, 0, Screen.width, Screen.height, new Color(0f, 0f, 0f, 0.25f * _t.Opacity));

        float x0 = 12f * _t.Scale, y0 = 12f * _t.Scale;
        float x = x0, y = y0;
        int col = 0;
        string description = null;

        foreach (var category in _model.Categories)
        {
            var panel = Rusk.Config.Panel(category, x, y);
            DrawPanel(category, panel, ref description);

            // まだ配置されていないパネルの既定位置 (横に並べ、6 枚で折り返す)
            col++;
            x += _t.PanelWidth + _t.PanelGap;
            if (col % 6 == 0) { x = x0; y += 260f * _t.Scale; }
        }

        UpdateDrags();
        DrawFooter(description);
    }

    private void DrawPanel(string category, PanelState panel, ref string description)
    {
        float x = panel.X, y = panel.Y, w = _t.PanelWidth, hh = _t.HeaderHeight;

        // ヘッダー
        Render.Rect(x + 2, y + 2, w, hh, _t.Shadow);
        Render.Rect(x, y, w, hh, _t.Header);
        Render.Rect(x, y + hh - 2f, w, 2f, _t.Accent());
        Render.Text(x + 10, y, w - 20, hh, RuskLang.T(RuskLang.CoreId, category), _t.Text, _t.FontSize, TextAnchor.MiddleLeft, bold: true);
        Render.Text(x + 10, y, w - 20, hh, panel.Collapsed ? "+" : "–", _t.TextDim, _t.FontSize, TextAnchor.MiddleRight);

        if (Render.Contains(x, y, w, hh, _mouse.Position))
        {
            if (_mouse.LeftClick)
            {
                _dragPanel = category;
                _dragOffset = new Vector2(_mouse.Position.x - x, _mouse.Position.y - y);
                _mouse.Consume();
            }
            else if (_mouse.RightClick)
            {
                panel.Collapsed = !panel.Collapsed;
                _mouse.Consume();
            }
        }

        if (panel.Collapsed) return;

        float cy = y + hh;
        var entries = _model.Entries(category);
        for (int i = 0; i < entries.Count; i++)
            cy = DrawEntry(entries[i], i, x, cy, w, ref description);
    }

    private float DrawEntry(IMenuEntry e, int index, float x, float y, float w, ref string description)
    {
        float h = _t.ModuleHeight;
        bool hover = Render.Contains(x, y, w, h, _mouse.Position);
        if (hover) description = L.T(e.Description);

        Render.Rect(x, y, w, h, hover ? _t.ModuleHover : _t.ModuleBg);
        var accent = _t.Accent(index * 0.05f);
        if (e.Active) Render.Rect(x, y, 2.5f, h, accent);

        var textColor = e.Active ? accent : e.Dim ? _t.TextDim : _t.Text;
        Render.Text(x + 10, y, w - 20, h, L.T(e.Label), textColor, _t.FontSize);

        bool expanded = _expanded.Contains(e);
        var bindRow = (e as ModuleEntry)?.BindRow;
        string right = bindRow is { Capturing: true } ? "[...]"
            : e.Suffix ?? (e.Rows.Count > 0 ? (expanded ? "▾" : "▸") : null);
        if (right != null)
            Render.Text(x + 10, y, w - 16, h, right, bindRow is { Capturing: true } ? accent : _t.TextDim,
                _t.SmallFontSize, TextAnchor.MiddleRight);

        if (hover && _mouse.LeftClick)
        {
            if (bindRow != null && NewInput.Shift)
                bindRow.Activate(); // Shift+クリック: トグルキーの割り当て
            else if (e.Kind == EntryKind.Expand)
                ToggleExpanded(e);
            else
                e.Activate();
            _mouse.Consume();
        }
        else if (hover && _mouse.RightClick)
        {
            ToggleExpanded(e);
            _mouse.Consume();
        }

        float cy = y + h;
        if (_expanded.Contains(e))
        {
            foreach (var row in e.Rows)
                cy = DrawRow(row, x, cy, w, ref description);
        }
        return cy;
    }

    private float DrawRow(Row row, float x, float y, float w, ref string description)
    {
        float h = _t.SettingHeight;
        float pad = 12f;
        bool hover = Render.Contains(x, y, w, h, _mouse.Position);
        if (hover) description = row.TDescription;

        Render.Rect(x, y, w, h, hover ? _t.ModuleHover : _t.SettingBg);
        Render.Text(x + pad, y, w - pad * 2, h, row.TLabel, _t.TextDim, _t.SmallFontSize);

        float valueRight = w - pad;
        if (row.Swatch.HasValue)
        {
            float sw = h - 6f;
            Render.Rect(x + w - pad - sw, y + 3f, sw, sw, row.Swatch.Value, 2f);
            valueRight -= sw + 4f;
        }
        Render.Text(x, y, valueRight, h, row.TValue, row.Capturing ? _t.Accent() : _t.Text, _t.SmallFontSize,
            TextAnchor.MiddleRight);

        if (row.Normalized is float n)
        {
            float bx = x + pad, bw = w - pad * 2;
            Render.Rect(bx, y + h - 3f, bw, 2f, _t.SliderTrack);
            Render.Rect(bx, y + h - 3f, bw * Mathf.Clamp01(n), 2f, _t.Accent());

            if (hover && _mouse.LeftClick)
            {
                _dragRow = row;
                _dragX = bx;
                _dragW = bw;
                row.Normalized = (_mouse.Position.x - bx) / bw;
                _mouse.Consume();
            }
            else if (hover && _mouse.RightClick)
            {
                row.Adjust(-1);
                _mouse.Consume();
            }
        }
        else if (hover && _mouse.LeftClick)
        {
            row.Activate();
            _mouse.Consume();
        }
        else if (hover && _mouse.RightClick)
        {
            row.Adjust(-1);
            _mouse.Consume();
        }

        return y + h;
    }

    private void UpdateDrags()
    {
        if (_dragPanel != null)
        {
            var panel = Rusk.Config.Panel(_dragPanel, 0, 0);
            panel.X = Mathf.Clamp(_mouse.Position.x - _dragOffset.x, 0, Screen.width - _t.PanelWidth);
            panel.Y = Mathf.Clamp(_mouse.Position.y - _dragOffset.y, 0, Screen.height - _t.HeaderHeight);
            if (!_mouse.LeftHeld) _dragPanel = null;
        }

        if (_dragRow != null)
        {
            _dragRow.Normalized = (_mouse.Position.x - _dragX) / _dragW;
            if (!_mouse.LeftHeld) _dragRow = null;
        }
    }

    /// <summary>画面下部: 操作ヒントと、マウスを乗せている項目の説明</summary>
    private void DrawFooter(string description)
    {
        float h = _t.ModuleHeight;
        float x = 12f * _t.Scale;
        float y = Screen.height - h * 2 - 8f * _t.Scale;
        Render.Text(x, y, Screen.width, h,
            "Left: toggle / run   Right: settings   Shift+Left: bind key   Drag header: move",
            _t.TextDim, _t.SmallFontSize, TextAnchor.MiddleLeft, shadow: true);
        if (!string.IsNullOrEmpty(description))
            Render.Text(x, y + h, Screen.width, h, description, _t.Text, _t.SmallFontSize, TextAnchor.MiddleLeft,
                shadow: true);
    }

    private void ToggleExpanded(IMenuEntry e)
    {
        if (e.Rows.Count == 0) return;
        if (!_expanded.Add(e)) _expanded.Remove(e);
    }
}
