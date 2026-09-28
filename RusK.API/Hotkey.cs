using System;
using UnityEngine;

namespace RusK.API;

/// <summary>
/// 修飾キー (Ctrl / Shift / Alt) 付きのキー。モジュールのトグルキーやアクショントリガーに使う。
/// 文字列表現は "Ctrl+Shift+F1" のような形 (Config にもこの形で保存される)。
/// </summary>
public readonly struct Hotkey : IEquatable<Hotkey>
{
    public Hotkey(KeyCode key, bool ctrl = false, bool shift = false, bool alt = false)
    {
        Key = key;
        Ctrl = ctrl;
        Shift = shift;
        Alt = alt;
    }

    public KeyCode Key { get; }
    public bool Ctrl { get; }
    public bool Shift { get; }
    public bool Alt { get; }

    public static Hotkey None => default;
    public bool IsNone => Key == KeyCode.None;
    public int ModifierCount => (Ctrl ? 1 : 0) + (Shift ? 1 : 0) + (Alt ? 1 : 0);

    /// <summary>このホットキーが要求する修飾キーがすべて押されているか (余分な修飾キーは許す)</summary>
    public bool ModifiersHeld(bool ctrl, bool shift, bool alt) =>
        (!Ctrl || ctrl) && (!Shift || shift) && (!Alt || alt);

    /// <summary>GUI 表示用の短い名前 (Alpha1 → 1, Keypad1 → Num1)</summary>
    public string Display => IsNone ? "None" : Prefix() + KeyName(Key);

    /// <summary>保存用の文字列 (TryParse で元に戻せる)</summary>
    public override string ToString() => IsNone ? "None" : Prefix() + Key;

    public static bool TryParse(string text, out Hotkey hotkey)
    {
        hotkey = None;
        if (string.IsNullOrWhiteSpace(text)) return false;

        bool ctrl = false, shift = false, alt = false;
        var parts = text.Split('+');
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].Trim().ToLowerInvariant())
            {
                case "ctrl": case "control": ctrl = true; break;
                case "shift": shift = true; break;
                case "alt": alt = true; break;
                default: return false;
            }
        }

        var keyText = parts[^1].Trim();
        if (keyText.Equals("None", StringComparison.OrdinalIgnoreCase)) return true;

        if (!Enum.TryParse(keyText, true, out KeyCode key))
        {
            // 表示用の名前 ("1", "Num1") も受け付ける
            if (keyText.Length == 1 && char.IsDigit(keyText[0]))
                key = KeyCode.Alpha0 + (keyText[0] - '0');
            else if (keyText.StartsWith("Num", StringComparison.OrdinalIgnoreCase) &&
                     int.TryParse(keyText.Substring(3), out int n) && n is >= 0 and <= 9)
                key = KeyCode.Keypad0 + n;
            else
                return false;
        }

        hotkey = new Hotkey(key, ctrl, shift, alt);
        return true;
    }

    public static implicit operator Hotkey(KeyCode key) => new(key);

    public bool Equals(Hotkey other) =>
        Key == other.Key && Ctrl == other.Ctrl && Shift == other.Shift && Alt == other.Alt;

    public override bool Equals(object obj) => obj is Hotkey other && Equals(other);
    public override int GetHashCode() => HashCode.Combine((int)Key, Ctrl, Shift, Alt);
    public static bool operator ==(Hotkey a, Hotkey b) => a.Equals(b);
    public static bool operator !=(Hotkey a, Hotkey b) => !a.Equals(b);

    private string Prefix() => (Ctrl ? "Ctrl+" : "") + (Shift ? "Shift+" : "") + (Alt ? "Alt+" : "");

    private static string KeyName(KeyCode key)
    {
        if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9) return ((int)(key - KeyCode.Alpha0)).ToString();
        if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9) return "Num" + (int)(key - KeyCode.Keypad0);
        return key.ToString();
    }
}
