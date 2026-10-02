using System;
using System.Collections.Generic;
using RusK.API;
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
/// ゲームパッドのボタンは KeyCode.JoystickButton0〜15 (PadButton) で表し、今つながっているパッド (Gamepad.current) から読む。
/// </summary>
internal static class NewInput
{
    private static readonly Dictionary<KeyCode, Key> Map = BuildMap();

    public static bool IsPressed(KeyCode code)
    {
        if (Hotkey.IsPad(code)) return AnyPad(code, c => c.isPressed);
        var ctrl = Control(code);
        return ctrl != null && ctrl.isPressed;
    }

    public static bool WasPressedThisFrame(KeyCode code)
    {
        if (Hotkey.IsPad(code)) return AnyPad(code, c => c.wasPressedThisFrame);
        var ctrl = Control(code);
        return ctrl != null && ctrl.wasPressedThisFrame;
    }

    /// <summary>
    /// つながっているゲームパッドのどれかで、そのボタンが条件を満たすか。
    /// Gamepad.current は最後に動いたパッドなので、ほかのソフトの仮想パッドなどがあると、そちらに移ってしまうことがある
    /// </summary>
    private static bool AnyPad(KeyCode code, Func<ButtonControl, bool> test)
    {
        try
        {
            var pads = Gamepad.s_Gamepads;
            int count = Gamepad.s_GamepadCount;
            for (int i = 0; i < count && pads != null && i < pads.Length; i++)
            {
                var c = PadControl(pads[i], code);
                if (c != null && test(c)) return true;
            }
        }
        catch { }
        return false;
    }

    public static bool Ctrl => IsPressed(KeyCode.LeftControl) || IsPressed(KeyCode.RightControl);
    public static bool Shift => IsPressed(KeyCode.LeftShift) || IsPressed(KeyCode.RightShift);
    public static bool Alt => IsPressed(KeyCode.LeftAlt) || IsPressed(KeyCode.RightAlt);

    private static ButtonControl Control(KeyCode code)
    {
        var kb = Keyboard.current;
        if (kb == null || !Map.TryGetValue(code, out var key) || key == Key.None) return null;
        try { return kb[key]; }
        catch { return null; } // このキーを持たないレイアウトなど
    }

    private static ButtonControl PadControl(Gamepad pad, KeyCode code)
    {
        try
        {
            if (pad == null) return null;
            return code switch
            {
                PadButton.A => pad.buttonSouth,
                PadButton.B => pad.buttonEast,
                PadButton.X => pad.buttonWest,
                PadButton.Y => pad.buttonNorth,
                PadButton.LB => pad.leftShoulder,
                PadButton.RB => pad.rightShoulder,
                PadButton.View => pad.selectButton,
                PadButton.Menu => pad.startButton,
                PadButton.LS => pad.leftStickButton,
                PadButton.RS => pad.rightStickButton,
                PadButton.LT => pad.leftTrigger,
                PadButton.RT => pad.rightTrigger,
                PadButton.Up => pad.dpad.up,
                PadButton.Down => pad.dpad.down,
                PadButton.Left => pad.dpad.left,
                PadButton.Right => pad.dpad.right,
                _ => null,
            };
        }
        catch { return null; }
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
