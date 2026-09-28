using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace RusK.API;

/// <summary>
/// Flex Window の中身を描くための部品集 (イミディエイトモード)。
/// 上から順に呼ぶと縦に積まれる。BeginRow / EndRow の間は横に並ぶ。
/// <code>
/// gui.Header("装備");
/// if (gui.Button("回復")) Heal();
/// hp = gui.Number("hp", "HP", hp, 10f);
/// gui.BeginRow(2, 1);  // 2:1 の幅で横並び
/// gui.Label("名前"); gui.Button("変更");
/// gui.EndRow();
/// </code>
/// </summary>
public sealed class WindowGui
{
    // 数値入力欄の編集状態 (同時に編集できるのは 1 つだけ)。
    // IMGUI の GUI.TextField はこのゲームではストリップされていて使えないので自前で実装している。
    private static string _focusId;
    private static string _buffer = "";
    private static float _original;
    private static bool _fresh; // フォーカス直後の最初の入力で中身を置き換える

    /// <summary>
    /// 入力欄にフォーカスがある間に打たれた文字。RusK 本体が新 Input System から毎フレーム詰める。
    /// '\b' = Backspace, '\n' = Enter, '\x1b' = Esc
    /// </summary>
    internal static string PendingInput = "";

    /// <summary>数値入力欄を編集中か (RusK 本体はこの間ホットキーを止める)</summary>
    public static bool IsEditing => _focusId != null;

    public static void ClearFocus()
    {
        _focusId = null;
        PendingInput = "";
    }

    // RusK 本体が毎フレーム設定する
    internal float Width;
    internal bool InputEnabled;
    internal bool MouseHeld;
    internal float StartY;
    internal float EndY;
    internal string ActiveSlider;

    private float _y;
    private float[] _rowWeights;
    private int _rowIndex;
    private float _rowY, _rowHeight;

    internal void Begin(float width, float scrollY, bool inputEnabled, bool mouseHeld)
    {
        Width = width;
        InputEnabled = inputEnabled;
        MouseHeld = mouseHeld;
        StartY = Padding - scrollY;
        _y = StartY;
        _rowWeights = null;
    }

    internal void End()
    {
        if (_rowWeights != null) EndRow();
        EndY = _y;
        if (!MouseHeld) ActiveSlider = null;
    }

    public float Scale => RuskStyle.Scale;
    public float LineHeight => 24f * Scale;
    public float Gap => 4f * Scale;
    public float Padding => 8f * Scale;
    public int FontSize => Mathf.RoundToInt(13f * Scale);
    public int SmallFontSize => Mathf.RoundToInt(11f * Scale);

    /// <summary>内容の幅 (余白を除く)</summary>
    public float ContentWidth => Width - Padding * 2;

    // ---- レイアウト ----

    /// <summary>横並びを開始する。weights は各列の幅の比 (省略時は均等 2 列)</summary>
    public void BeginRow(params float[] weights)
    {
        if (_rowWeights != null) EndRow();
        _rowWeights = weights == null || weights.Length == 0 ? new[] { 1f, 1f } : weights;
        _rowIndex = 0;
        _rowY = _y;
        _rowHeight = 0f;
    }

    public void EndRow()
    {
        if (_rowWeights == null) return;
        _y = _rowY + _rowHeight + Gap;
        _rowWeights = null;
    }

    public void Space(float height = 6f)
    {
        if (_rowWeights != null) { Next(height); return; }
        _y += height * Scale;
    }

    public void Separator()
    {
        var r = Next(6f * Scale);
        Render.Rect(r.x, r.y + r.height * 0.5f, r.width, 1f, RuskStyle.Track);
    }

    /// <summary>次の部品の場所を確保する (独自の部品を描くとき用)</summary>
    public Rect Next(float height)
    {
        if (_rowWeights == null)
        {
            var r = new Rect(Padding, _y, ContentWidth, height);
            _y += height + Gap;
            return r;
        }

        float total = 0f;
        foreach (var w in _rowWeights) total += w;
        float x = Padding;
        for (int i = 0; i < _rowIndex && i < _rowWeights.Length; i++)
            x += ContentWidth * _rowWeights[i] / total;
        int col = Math.Min(_rowIndex, _rowWeights.Length - 1);
        float width = ContentWidth * _rowWeights[col] / total - (col < _rowWeights.Length - 1 ? Gap : 0f);

        var rect = new Rect(x, _rowY, width, height);
        _rowHeight = Mathf.Max(_rowHeight, height);
        _rowIndex++;
        if (_rowIndex >= _rowWeights.Length) EndRow();
        return rect;
    }

    // ---- 表示 ----

    public void Label(string text, Color? color = null, bool bold = false, TextAnchor anchor = TextAnchor.MiddleLeft,
        bool small = false)
    {
        var r = Next(small ? LineHeight * 0.8f : LineHeight);
        Render.Text(r.x + 2f, r.y, r.width - 4f, r.height, text, color ?? RuskStyle.Text,
            small ? SmallFontSize : FontSize, anchor, bold);
    }

    /// <summary>見出し (左にアクセントの帯)</summary>
    public void Header(string text, string right = null)
    {
        var r = Next(LineHeight);
        Render.Rect(r.x, r.y + 3f, 3f, r.height - 6f, RuskStyle.Accent);
        Render.Text(r.x + 10f, r.y, r.width - 12f, r.height, text, RuskStyle.Text, FontSize, TextAnchor.MiddleLeft, true);
        if (right != null)
            Render.Text(r.x, r.y, r.width - 4f, r.height, right, RuskStyle.TextDim, SmallFontSize, TextAnchor.MiddleRight);
    }

    /// <summary>色付きの帯 (カードの背景などに)。高さ分の場所を確保せず、指定の矩形に描く</summary>
    public void Box(Rect r, Color color, float radius = 4f) => Render.Rect(r.x, r.y, r.width, r.height, color, radius * Scale);

    /// <summary>今の縦位置 (Box でカードの背景を描くとき用)</summary>
    public float CursorY => _rowWeights != null ? _rowY : _y;

    // ---- 入力 ----

    public bool Button(string text, bool enabled = true, bool accent = false)
    {
        var r = Next(LineHeight);
        bool hover = enabled && Hover(r);
        var bg = !enabled ? RuskStyle.Row : accent ? Render.WithAlpha(RuskStyle.Accent, hover ? 0.55f : 0.35f)
            : hover ? RuskStyle.RowHover : RuskStyle.Track;
        Render.Rect(r.x, r.y, r.width, r.height, bg, 3f * Scale);
        Render.Text(r.x, r.y, r.width, r.height, text, enabled ? (hover ? RuskStyle.Accent : RuskStyle.Text) : RuskStyle.TextDim,
            FontSize, TextAnchor.MiddleCenter, accent);
        return enabled && Clicked(r);
    }

    /// <summary>タブ。押されたタブの番号を返す</summary>
    public int Tabs(string[] tabs, int selected)
    {
        var r = Next(LineHeight + 2f * Scale);
        float w = r.width / tabs.Length;
        for (int i = 0; i < tabs.Length; i++)
        {
            var tr = new Rect(r.x + w * i, r.y, w - 2f, r.height);
            bool sel = i == selected;
            bool hover = Hover(tr);
            Render.Rect(tr.x, tr.y, tr.width, tr.height, sel ? RuskStyle.Header : hover ? RuskStyle.RowHover : RuskStyle.Row, 3f * Scale);
            if (sel) Render.Rect(tr.x, tr.y + tr.height - 2f, tr.width, 2f, RuskStyle.Accent);
            Render.Text(tr.x, tr.y, tr.width, tr.height, tabs[i], sel ? RuskStyle.Text : RuskStyle.TextDim, FontSize,
                TextAnchor.MiddleCenter, sel);
            if (Clicked(tr)) selected = i;
        }
        return selected;
    }

    public bool Toggle(string label, bool value)
    {
        var r = Next(LineHeight);
        bool hover = Hover(r);
        if (hover) Render.Rect(r.x, r.y, r.width, r.height, RuskStyle.RowHover, 3f * Scale);
        float box = r.height - 10f * Scale;
        Render.Rect(r.x + 4f, r.y + 5f * Scale, box, box, value ? RuskStyle.Accent : RuskStyle.Track, 3f * Scale);
        Render.Text(r.x + box + 12f, r.y, r.width - box - 14f, r.height, label, RuskStyle.Text, FontSize);
        return Clicked(r) ? !value : value;
    }

    /// <summary>リストの 1 行。クリックされたら true。selected なら強調する</summary>
    public bool Selectable(string text, bool selected, string right = null, Color? color = null)
    {
        var r = Next(LineHeight);
        bool hover = Hover(r);
        Render.Rect(r.x, r.y, r.width, r.height,
            selected ? Render.WithAlpha(RuskStyle.Accent, 0.25f) : hover ? RuskStyle.RowHover : RuskStyle.Row, 3f * Scale);
        if (selected) Render.Rect(r.x, r.y, 3f, r.height, RuskStyle.Accent);
        Render.Text(r.x + 8f, r.y, r.width - 16f, r.height, text, color ?? RuskStyle.Text, FontSize);
        if (right != null)
            Render.Text(r.x, r.y, r.width - 8f, r.height, right, RuskStyle.TextDim, SmallFontSize, TextAnchor.MiddleRight);
        return Clicked(r);
    }

    /// <summary>「◀ 値 ▶」。◀ で -1、▶ で +1、それ以外は 0 を返す。label が null なら値だけ</summary>
    public int Stepper(string label, string value, Color? valueColor = null)
    {
        var r = Next(LineHeight);
        float x = r.x, w = r.width;
        if (!string.IsNullOrEmpty(label))
        {
            float lw = w * 0.38f;
            Render.Text(x + 2f, r.y, lw - 4f, r.height, label, RuskStyle.TextDim, FontSize);
            x += lw;
            w -= lw;
        }

        float bw = r.height;
        var left = new Rect(x, r.y, bw, r.height);
        var right = new Rect(x + w - bw, r.y, bw, r.height);
        Render.Rect(x, r.y, w, r.height, RuskStyle.Field, 3f * Scale);
        ArrowButton(left, "◀");
        ArrowButton(right, "▶");
        Render.Text(x + bw, r.y, w - bw * 2, r.height, value, valueColor ?? RuskStyle.Text, FontSize, TextAnchor.MiddleCenter);

        if (Clicked(left)) return -1;
        if (Clicked(right)) return +1;
        return 0;
    }

    /// <summary>
    /// 数値入力。[−] [入力欄] [+]。入力欄をクリックすると直接タイプできる
    /// (0-9 . - / Backspace / Enter で確定 / Esc で元に戻す)。打つたびに反映される。
    /// id はウィンドウ内で一意な文字列 (どの欄を編集中か覚えておくため)。
    /// </summary>
    public float Number(string id, string label, float value, float step = 1f, string format = "0.##")
    {
        var r = Next(LineHeight);
        float x = r.x, w = r.width;
        if (!string.IsNullOrEmpty(label))
        {
            float lw = w * 0.38f;
            Render.Text(x + 2f, r.y, lw - 4f, r.height, label, RuskStyle.TextDim, FontSize);
            x += lw;
            w -= lw;
        }

        float bw = r.height;
        var minus = new Rect(x, r.y, bw, r.height);
        var plus = new Rect(x + w - bw, r.y, bw, r.height);
        var field = new Rect(x + bw + 2f, r.y, w - bw * 2 - 4f, r.height);

        ArrowButton(minus, "−");
        ArrowButton(plus, "+");
        if (Clicked(minus)) return value - step;
        if (Clicked(plus)) return value + step;

        // 入力欄をクリックでフォーカス
        if (Clicked(field))
        {
            _focusId = id;
            _original = value;
            _buffer = value.ToString(format, CultureInfo.InvariantCulture);
            _fresh = true;
            PendingInput = "";
        }

        bool focused = _focusId == id;
        Render.Rect(field.x, field.y, field.width, field.height, RuskStyle.Field, 3f * Scale);
        if (!focused)
        {
            bool hover = Hover(field);
            if (hover) Render.Outline(field.x, field.y, field.width, field.height, Render.WithAlpha(RuskStyle.Accent, 0.5f));
            Render.Text(field.x, field.y, field.width, field.height, value.ToString(format, CultureInfo.InvariantCulture),
                RuskStyle.Text, FontSize, TextAnchor.MiddleCenter);
            return value;
        }

        // 編集中: 打たれた文字を反映
        float result = value;
        if (PendingInput.Length > 0)
        {
            foreach (char c in PendingInput)
            {
                switch (c)
                {
                    case '\x1b': // Esc: 元の値に戻して終了
                        _focusId = null;
                        PendingInput = "";
                        return _original;
                    case '\n': // Enter: 確定して終了
                        _focusId = null;
                        break;
                    case '\b':
                        _buffer = _fresh || _buffer.Length == 0 ? "" : _buffer.Substring(0, _buffer.Length - 1);
                        _fresh = false;
                        break;
                    default:
                        if (_fresh) { _buffer = ""; _fresh = false; }
                        if (c == '-' && _buffer.Length > 0) break;          // マイナスは先頭だけ
                        if (c == '.' && _buffer.Contains('.')) break;        // 小数点は 1 つだけ
                        if (_buffer.Length < 16) _buffer += c;
                        break;
                }
                if (_focusId == null) break;
            }
            PendingInput = "";

            if (float.TryParse(_buffer, NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                result = parsed;
        }

        // 枠と点滅するカーソル
        Render.Outline(field.x, field.y, field.width, field.height, RuskStyle.Accent);
        bool caret = (int)(Time.unscaledTime * 2f) % 2 == 0;
        string shown = _fresh ? _buffer : _buffer + (caret ? "|" : " ");
        Render.Text(field.x, field.y, field.width, field.height, shown, _fresh ? RuskStyle.Accent : RuskStyle.Text,
            FontSize, TextAnchor.MiddleCenter);
        return result;
    }

    public float Slider(string id, string label, float value, float min, float max, string format = "0.##")
    {
        var r = Next(LineHeight);
        float x = r.x, w = r.width;
        float lw = string.IsNullOrEmpty(label) ? 0f : w * 0.38f;
        if (lw > 0f) Render.Text(x + 2f, r.y, lw - 4f, r.height, label, RuskStyle.TextDim, FontSize);
        x += lw;
        w -= lw;

        float norm = max > min ? Mathf.Clamp01((value - min) / (max - min)) : 0f;
        float ty = r.y + r.height * 0.5f - 2f;
        Render.Rect(x, ty, w, 4f, RuskStyle.Track, 2f);
        Render.Rect(x, ty, w * norm, 4f, RuskStyle.Accent, 2f);
        Render.Rect(x + w * norm - 5f, r.y + r.height * 0.5f - 7f, 10f, 14f, RuskStyle.Text, 3f);
        Render.Text(x, r.y - r.height * 0.3f, w, r.height, value.ToString(format, CultureInfo.InvariantCulture),
            RuskStyle.TextDim, SmallFontSize, TextAnchor.UpperRight);

        var track = new Rect(x, r.y, w, r.height);
        if (Clicked(track)) ActiveSlider = id;
        if (ActiveSlider == id && MouseHeld)
        {
            float mx = Event.current.mousePosition.x;
            return min + Mathf.Clamp01((mx - x) / w) * (max - min);
        }
        return value;
    }

    // ---- 内部 ----

    private void ArrowButton(Rect r, string text)
    {
        bool hover = Hover(r);
        Render.Rect(r.x, r.y, r.width, r.height, hover ? RuskStyle.RowHover : RuskStyle.Track, 3f * Scale);
        Render.Text(r.x, r.y, r.width, r.height, text, hover ? RuskStyle.Accent : RuskStyle.Text, FontSize,
            TextAnchor.MiddleCenter);
    }

    private bool Hover(Rect r)
    {
        if (!InputEnabled) return false;
        var e = Event.current;
        if (e == null) return false;
        var p = e.mousePosition;
        return p.x >= r.x && p.x < r.x + r.width && p.y >= r.y && p.y < r.y + r.height;
    }

    private bool Clicked(Rect r)
    {
        var e = Event.current;
        if (e == null || e.type != EventType.MouseDown || e.button != 0 || !Hover(r)) return false;
        e.Use();
        _focusId = null; // どこかをクリックしたら編集を終える (入力欄なら直後に付け直す)
        return true;
    }
}
