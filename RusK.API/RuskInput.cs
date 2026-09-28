using System;
using UnityEngine;

namespace RusK.API;

/// <summary>
/// Mod からキー入力を調べる。ゲームの入力方式 (新 Input System など) の違いは RusK 本体が吸収する。
/// メニュー (ClickGUI) を開いている間・入力欄の編集中・キー割り当て中は Blocked になり、どのキーも押されていない扱いになる。
/// </summary>
public static class RuskInput
{
    // RusK 本体が設定する
    internal static Func<KeyCode, bool> PressedFunc;
    internal static Func<KeyCode, bool> HeldFunc;
    internal static Func<(bool ctrl, bool shift, bool alt)> ModifiersFunc;

    /// <summary>RusK のメニューや入力欄を操作中で、ゲーム用のキー入力を無視すべきか</summary>
    public static bool Blocked { get; internal set; }

    /// <summary>このフレームにキーが押されたか</summary>
    public static bool WasPressed(KeyCode key) => !Blocked && key != KeyCode.None && (PressedFunc?.Invoke(key) ?? false);

    /// <summary>キーが押されているか</summary>
    public static bool IsHeld(KeyCode key) => !Blocked && key != KeyCode.None && (HeldFunc?.Invoke(key) ?? false);

    /// <summary>このフレームにホットキー (修飾キー込み) が押されたか</summary>
    public static bool WasPressed(Hotkey hotkey)
    {
        if (hotkey.IsNone || !WasPressed(hotkey.Key)) return false;
        var (ctrl, shift, alt) = ModifiersFunc?.Invoke() ?? (false, false, false);
        return hotkey.ModifiersHeld(ctrl, shift, alt);
    }

    /// <summary>ホットキー (修飾キー込み) が押されているか</summary>
    public static bool IsHeld(Hotkey hotkey)
    {
        if (hotkey.IsNone || !IsHeld(hotkey.Key)) return false;
        var (ctrl, shift, alt) = ModifiersFunc?.Invoke() ?? (false, false, false);
        return hotkey.ModifiersHeld(ctrl, shift, alt);
    }

    /// <summary>RusK 本体から入力の取り方を設定する (Mod からは呼ばない)</summary>
    public static void Configure(Func<KeyCode, bool> pressed, Func<KeyCode, bool> held,
        Func<(bool ctrl, bool shift, bool alt)> modifiers)
    {
        PressedFunc = pressed;
        HeldFunc = held;
        ModifiersFunc = modifiers;
    }

    /// <summary>RusK 本体から、ゲーム用のキー入力を止めるかを設定する (Mod からは呼ばない)</summary>
    public static void SetBlocked(bool blocked) => Blocked = blocked;
}
