using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace RusK.Core.UI;

/// <summary>
/// 新 Input System (UnityEngine.InputSystem) のラッパー。
/// このゲームは Active Input Handling が "Input System (New)" なので、
/// レガシーの UnityEngine.Input.GetKey は例外を投げる。こちらを使う。
///
/// 公開API側は馴染みのある KeyCode のままにして、内部で Key へ変換する。
/// </summary>
internal static class NewInput
{
    private static readonly Dictionary<KeyCode, Key> Map = BuildMap();

    public static bool IsPressed(KeyCode code)
    {
        var ctrl = Control(code);
        return ctrl != null && ctrl.isPressed;
    }

    public static bool WasPressedThisFrame(KeyCode code)
    {
        var ctrl = Control(code);
        return ctrl != null && ctrl.wasPressedThisFrame;
    }

    public static bool Ctrl => IsPressed(KeyCode.LeftControl) || IsPressed(KeyCode.RightControl);
    public static bool Shift => IsPressed(KeyCode.LeftShift) || IsPressed(KeyCode.RightShift);
    public static bool Alt => IsPressed(KeyCode.LeftAlt) || IsPressed(KeyCode.RightAlt);

    private static KeyControl Control(KeyCode code)
    {
        var kb = Keyboard.current;
        if (kb == null || !Map.TryGetValue(code, out var key) || key == Key.None) return null;
        try { return kb[key]; }
        catch { return null; } // このキーを持たないレイアウトなど
    }

    private static Dictionary<KeyCode, Key> BuildMap()
    {
        var map = new Dictionary<KeyCode, Key>();

        // A-Z, F1-F12, 多くの記号・特殊キーは KeyCode と Key で名前が一致する
        for (var k = KeyCode.A; k <= KeyCode.Z; k++) TryName(map, k);
        for (var k = KeyCode.F1; k <= KeyCode.F12; k++) TryName(map, k);
        foreach (var k in new[]
                 {
                     KeyCode.Insert, KeyCode.Home, KeyCode.End, KeyCode.PageUp, KeyCode.PageDown,
                     KeyCode.Delete, KeyCode.Backspace, KeyCode.Escape, KeyCode.Space, KeyCode.Tab,
                     KeyCode.LeftShift, KeyCode.RightShift, KeyCode.LeftAlt, KeyCode.RightAlt,
                     KeyCode.UpArrow, KeyCode.DownArrow, KeyCode.LeftArrow, KeyCode.RightArrow,
                     KeyCode.Comma, KeyCode.Period, KeyCode.Slash, KeyCode.Backslash, KeyCode.Semicolon,
                     KeyCode.Quote, KeyCode.LeftBracket, KeyCode.RightBracket, KeyCode.Minus,
                     KeyCode.BackQuote,
                 })
            TryName(map, k);

        // 名前が違うもの
        Alias(map, KeyCode.Return, "Enter");
        Alias(map, KeyCode.KeypadEnter, "NumpadEnter");
        Alias(map, KeyCode.LeftControl, "LeftCtrl");
        Alias(map, KeyCode.RightControl, "RightCtrl");
        Alias(map, KeyCode.BackQuote, "Backquote");
        Alias(map, KeyCode.KeypadMinus, "NumpadMinus");
        Alias(map, KeyCode.KeypadPeriod, "NumpadPeriod");
        for (int n = 0; n <= 9; n++)
        {
            Alias(map, KeyCode.Alpha0 + n, "Digit" + n);
            Alias(map, KeyCode.Keypad0 + n, "Numpad" + n);
        }

        return map;
    }

    private static void TryName(Dictionary<KeyCode, Key> map, KeyCode code)
    {
        if (Enum.TryParse<Key>(code.ToString(), out var key))
            map[code] = key;
    }

    private static void Alias(Dictionary<KeyCode, Key> map, KeyCode code, string keyName)
    {
        if (Enum.TryParse<Key>(keyName, out var key))
            map[code] = key;
    }
}
