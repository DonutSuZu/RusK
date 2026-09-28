using System.Collections.Generic;
using UnityEngine;

namespace RusK.API;

/// <summary>
/// IMGUI の描画ヘルパー。OnGUI の中でだけ呼ぶこと。
/// RusK 本体の GUI と Mod の HUD で共通に使う。
/// </summary>
public static class Render
{
    private static readonly Dictionary<(int size, TextAnchor anchor, bool bold), GUIStyle> Styles = new();
    private static readonly Dictionary<(string text, int size, bool bold), float> WidthCache = new();

    public static bool IsRepaint => Event.current != null && Event.current.type == EventType.Repaint;

    public static void Rect(float x, float y, float w, float h, Color color, float radius = 0f)
    {
        if (!IsRepaint) return;
        GUI.DrawTexture(new Rect(x, y, w, h), Texture2D.whiteTexture, ScaleMode.StretchToFill, true, 0f,
            color, 0f, radius);
    }

    public static void Outline(float x, float y, float w, float h, Color color, float thickness = 1f)
    {
        Rect(x, y, w, thickness, color);
        Rect(x, y + h - thickness, w, thickness, color);
        Rect(x, y, thickness, h, color);
        Rect(x + w - thickness, y, thickness, h, color);
    }

    public static void Text(float x, float y, float w, float h, string text, Color color, int size = 13,
        TextAnchor anchor = TextAnchor.MiddleLeft, bool bold = false, bool shadow = false)
    {
        if (!IsRepaint || string.IsNullOrEmpty(text)) return;
        var style = GetStyle(size, anchor, bold);
        if (shadow)
        {
            style.normal.textColor = new Color(0f, 0f, 0f, color.a * 0.8f);
            GUI.Label(new Rect(x + 1f, y + 1f, w, h), text, style);
        }
        style.normal.textColor = color;
        GUI.Label(new Rect(x, y, w, h), text, style);
    }

    public static float TextWidth(string text, int size = 13, bool bold = false)
    {
        if (string.IsNullOrEmpty(text)) return 0f;
        var key = (text, size, bold);
        if (WidthCache.TryGetValue(key, out var w)) return w;
        w = GetStyle(size, TextAnchor.MiddleLeft, bold).CalcSize(new GUIContent(text)).x;
        if (WidthCache.Count > 4096) WidthCache.Clear();
        WidthCache[key] = w;
        return w;
    }

    public static bool Contains(float x, float y, float w, float h, Vector2 p) =>
        p.x >= x && p.x < x + w && p.y >= y && p.y < y + h;

    public static Color WithAlpha(Color c, float a) => new(c.r, c.g, c.b, a);

    /// <summary>HSV (各 0～1) から色を作る。レインボー表示用</summary>
    public static Color Hsv(float h, float s, float v, float a = 1f)
    {
        h = (h % 1f + 1f) % 1f * 6f;
        int i = (int)h;
        float f = h - i, p = v * (1f - s), q = v * (1f - s * f), t = v * (1f - s * (1f - f));
        return i switch
        {
            0 => new Color(v, t, p, a),
            1 => new Color(q, v, p, a),
            2 => new Color(p, v, t, a),
            3 => new Color(p, q, v, a),
            4 => new Color(t, p, v, a),
            _ => new Color(v, p, q, a),
        };
    }

    public static void RgbToHsv(Color c, out float h, out float s, out float v)
    {
        float max = Mathf.Max(c.r, Mathf.Max(c.g, c.b));
        float min = Mathf.Min(c.r, Mathf.Min(c.g, c.b));
        float d = max - min;
        v = max;
        s = max <= 0f ? 0f : d / max;
        if (d <= 0f) { h = 0f; return; }
        if (max == c.r) h = (c.g - c.b) / d % 6f;
        else if (max == c.g) h = (c.b - c.r) / d + 2f;
        else h = (c.r - c.g) / d + 4f;
        h /= 6f;
        if (h < 0f) h += 1f;
    }

    public static string ToHex(Color c) =>
        "#" + Byte(c.r).ToString("X2") + Byte(c.g).ToString("X2") + Byte(c.b).ToString("X2");

    public static bool TryParseHex(string hex, out Color color)
    {
        color = new Color(1f, 1f, 1f, 1f);
        if (string.IsNullOrEmpty(hex)) return false;
        hex = hex.Trim().TrimStart('#');
        if (hex.Length != 6) return false;
        if (!int.TryParse(hex, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out int rgb))
            return false;
        color = new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
        return true;
    }

    private static int Byte(float f) => Mathf.Clamp(Mathf.RoundToInt(f * 255f), 0, 255);

    /// <summary>テクスチャを色付き (乗算) で描く。アイコン用</summary>
    public static void Image(float x, float y, float w, float h, Texture texture, Color tint)
    {
        if (!IsRepaint || texture == null) return;
        GUI.DrawTexture(new Rect(x, y, w, h), texture, ScaleMode.StretchToFill, true, 0f, tint, 0f, 0f);
    }

    // IMGUI の標準フォントには日本語の文字がないので、OS の日本語フォントを使う
    private static readonly string[] JapaneseFonts = { "Yu Gothic UI", "Meiryo UI", "Meiryo", "MS Gothic" };
    private static Font _font;
    private static bool _fontTried;

    /// <summary>RusK が使う日本語対応フォント (取れなければ null = 標準フォント)</summary>
    public static Font Font => TextFont;

    private static Font TextFont
    {
        get
        {
            if (_fontTried) return _font;
            _fontTried = true;
            try
            {
                _font = Font.CreateDynamicFontFromOSFont(
                    new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStringArray(JapaneseFonts), 16);
                if (_font != null) _font.hideFlags = HideFlags.HideAndDontSave;
            }
            catch
            {
                _font = null; // 失敗したら標準フォントのまま
            }
            return _font;
        }
    }

    private static GUIStyle GetStyle(int size, TextAnchor anchor, bool bold)
    {
        var key = (size, anchor, bold);
        if (Styles.TryGetValue(key, out var style) && style != null) return style;
        style = new GUIStyle(GUI.skin.label)
        {
            font = TextFont,
            fontSize = size,
            alignment = anchor,
            fontStyle = bold ? FontStyle.Bold : FontStyle.Normal,
            wordWrap = false,
            clipping = TextClipping.Overflow,
            richText = false,
        };
        style.padding = new RectOffset(0, 0, 0, 0);
        style.margin = new RectOffset(0, 0, 0, 0);
        Styles[key] = style;
        return style;
    }
}
