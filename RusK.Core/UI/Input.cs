using System;
using System.Collections.Generic;
using System.Linq;
using RusK.API;
using UnityEngine;

namespace RusK.Core.UI;

/// <summary>ホットキーに使えるキーの一覧と、押されたキーの検出 (新 Input System)</summary>
internal static class KeyBindWatcher
{
    // 矢印キーと Enter / Backspace は TabGUI の操作に使うので、割り当て候補から外している
    public static readonly KeyCode[] Watched = BuildWatchList();

    public static IEnumerable<KeyCode> Pressed()
    {
        foreach (var key in Watched)
            if (NewInput.WasPressedThisFrame(key))
                yield return key;
    }

    private static KeyCode[] BuildWatchList()
    {
        var list = new List<KeyCode>();
        for (var k = KeyCode.F1; k <= KeyCode.F12; k++) list.Add(k);
        for (var k = KeyCode.A; k <= KeyCode.Z; k++) list.Add(k);
        for (var k = KeyCode.Alpha0; k <= KeyCode.Alpha9; k++) list.Add(k);
        for (var k = KeyCode.Keypad0; k <= KeyCode.Keypad9; k++) list.Add(k);
        list.AddRange(new[]
        {
            KeyCode.Insert, KeyCode.Home, KeyCode.End, KeyCode.PageUp, KeyCode.PageDown,
            KeyCode.LeftBracket, KeyCode.RightBracket, KeyCode.Semicolon, KeyCode.Quote,
            KeyCode.Comma, KeyCode.Period, KeyCode.Slash, KeyCode.Backslash, KeyCode.Minus,
            KeyCode.BackQuote, KeyCode.Tab, KeyCode.Space,
        });
        return list.ToArray();
    }
}

/// <summary>
/// 「次に押したキーを割り当てる」状態の管理。GUI の Hotkey 行から Begin される。
/// Esc でキャンセル、Delete / Backspace で割り当て解除 (None)。
/// 押したときに Ctrl / Shift / Alt を押していれば、それも含めて記録する。
/// </summary>
internal static class HotkeyCapture
{
    private static object _owner;
    private static Action<Hotkey> _onCaptured;
    private static int _startFrame;

    public static bool Active => _onCaptured != null;
    public static bool IsCapturing(object owner) => Active && ReferenceEquals(_owner, owner);

    public static void Begin(object owner, Action<Hotkey> onCaptured)
    {
        _owner = owner;
        _onCaptured = onCaptured;
        _startFrame = Time.frameCount;
    }

    public static void Cancel()
    {
        _owner = null;
        _onCaptured = null;
    }

    public static void Update()
    {
        // 開始したフレームのキー入力 (Enter など) は拾わない
        if (!Active || Time.frameCount == _startFrame) return;

        if (NewInput.WasPressedThisFrame(KeyCode.Escape))
        {
            Cancel();
            return;
        }

        if (NewInput.WasPressedThisFrame(KeyCode.Delete) || NewInput.WasPressedThisFrame(KeyCode.Backspace))
        {
            Finish(Hotkey.None);
            return;
        }

        foreach (var key in KeyBindWatcher.Pressed())
        {
            Finish(new Hotkey(key, NewInput.Ctrl, NewInput.Shift, NewInput.Alt));
            return;
        }
    }

    private static void Finish(Hotkey hotkey)
    {
        var callback = _onCaptured;
        Cancel();
        callback?.Invoke(hotkey);
    }
}

/// <summary>
/// 押されたキーを、モジュールのトグルキーと Press トリガーに配る。
/// 同じキーに複数の割り当てがあるときは、修飾キーがより多く一致するものを優先する
/// (Ctrl+F1 を押したとき、F1 単体の割り当ては発動しない)。
/// </summary>
internal static class HotkeyDispatcher
{
    public static void Dispatch()
    {
        var pressed = KeyBindWatcher.Pressed().ToList();
        if (pressed.Count == 0) return;

        bool ctrl = NewInput.Ctrl, shift = NewInput.Shift, alt = NewInput.Alt;

        var bindings = new List<(Hotkey hotkey, Action fire)>();
        foreach (var module in Rusk.Modules.All)
        {
            if (module.Toggleable && !module.Keybind.IsNone)
                bindings.Add((module.Keybind, module.Toggle));
        }
        foreach (var trigger in Rusk.Triggers.All)
        {
            if (!trigger.IsHold && !trigger.Key.Value.IsNone)
            {
                var t = trigger;
                bindings.Add((t.Key.Value, () => Rusk.Triggers.Fire(t)));
            }
        }

        foreach (var key in pressed)
        {
            var matches = bindings.Where(b => b.hotkey.Key == key && b.hotkey.ModifiersHeld(ctrl, shift, alt)).ToList();
            if (matches.Count == 0) continue;
            int best = matches.Max(b => b.hotkey.ModifierCount);
            foreach (var b in matches.Where(b => b.hotkey.ModifierCount == best))
                b.fire();
        }
    }
}

/// <summary>マウスの座標とクリックのエッジ検出 (IMGUI の Event から取る。これはレガシー Input ではない)</summary>
internal sealed class Mouse
{
    public Vector2 Position { get; private set; }
    public bool LeftClick { get; private set; }
    public bool RightClick { get; private set; }
    public bool LeftHeld { get; private set; }

    public void Sample()
    {
        var e = Event.current;
        if (e == null) return;

        Position = e.mousePosition;
        LeftClick = false;
        RightClick = false;

        switch (e.type)
        {
            case EventType.MouseDown when e.button == 0:
                LeftClick = true;
                LeftHeld = true;
                break;
            case EventType.MouseDown when e.button == 1:
                RightClick = true;
                break;
            case EventType.MouseUp when e.button == 0:
                LeftHeld = false;
                break;
        }
    }

    /// <summary>クリックを無かったことにする (イベントは消費しない)</summary>
    public void Suppress()
    {
        LeftClick = false;
        RightClick = false;
    }

    public void Consume()
    {
        LeftClick = false;
        RightClick = false;
        Event.current?.Use();
    }
}
