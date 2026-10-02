using System;
using RusK.API;
using UnityEngine;

namespace RusK.Core.UI;

/// <summary>UI 全体の入口。キー入力の振り分けと、各描画コンポーネントの呼び出し</summary>
internal sealed class RuskUi
{
    private readonly Theme _theme = new();
    private readonly MenuModel _model = new();
    private readonly TabGui _tabGui;
    private readonly ClickGui _clickGui;
    private readonly Hud _hud;

    private bool _menuOpen;
    private bool _padMenu; // ゲームパッドで開いた (開いている間はゲームの操作を止める)

    // ClickGUI やウィンドウの表示中にカーソルを出すため、元の状態を覚えておく
    private bool _cursorForced;
    private bool _prevCursorVisible;
    private CursorLockMode _prevCursorLock;

    public RuskUi()
    {
        _tabGui = new TabGui(_theme, _model);
        _clickGui = new ClickGui(_theme, _model);
        _hud = new Hud(_theme);
        Windows = new WindowManager(_theme);

        // Mod がキー入力を調べられるようにする (RuskInput)
        RuskInput.Configure(NewInput.WasPressedThisFrame, NewInput.IsPressed,
            () => (NewInput.Ctrl, NewInput.Shift, NewInput.Alt));
    }

    public WindowManager Windows { get; }

    /// <summary>ClickGUI (マウス操作) で出すか。ゲームパッドで開いたときは、パッドで操作できる TabGUI で出す</summary>
    private bool ClickMode => (Rusk.MenuSettings?.IsClickGui ?? false) && !_padMenu;

    /// <summary>ウィンドウの数値入力欄を編集している最中か</summary>
    private static bool Typing => WindowGui.IsEditing;

    private static readonly (KeyCode key, char ch)[] TypingKeys = BuildTypingKeys();

    private static (KeyCode, char)[] BuildTypingKeys()
    {
        var list = new System.Collections.Generic.List<(KeyCode, char)>();
        for (int n = 0; n <= 9; n++)
        {
            list.Add((KeyCode.Alpha0 + n, (char)('0' + n)));
            list.Add((KeyCode.Keypad0 + n, (char)('0' + n)));
        }
        list.Add((KeyCode.Minus, '-'));
        list.Add((KeyCode.KeypadMinus, '-'));
        list.Add((KeyCode.Period, '.'));
        list.Add((KeyCode.KeypadPeriod, '.'));
        list.Add((KeyCode.Backspace, '\b'));
        list.Add((KeyCode.Return, '\n'));
        list.Add((KeyCode.KeypadEnter, '\n'));
        list.Add((KeyCode.Escape, '\x1b'));
        return list.ToArray();
    }

    /// <summary>編集中の入力欄へ、このフレームに押されたキーを文字にして渡す</summary>
    private static void FeedTyping()
    {
        foreach (var (key, ch) in TypingKeys)
            if (NewInput.WasPressedThisFrame(key))
                WindowGui.PendingInput += ch;
    }

    public void Update()
    {
        Rusk.Notifications.Prune();

        // キー割り当て中・入力欄の編集中・ClickGUI を開いている間は、Mod 用のキー入力も止める
        RuskInput.SetBlocked(HotkeyCapture.Active || Typing || (_menuOpen && (ClickMode || _padMenu)));

        // カーソル: ClickGUI かウィンドウが出ている間は表示する
        if ((_menuOpen && ClickMode) || Windows.AnyVisible) ForceCursor();
        else RestoreCursor();

        // キー割り当て中は、そのキーで他の操作が起きないようにする
        if (HotkeyCapture.Active)
        {
            HotkeyCapture.Update();
            Rusk.Triggers.ReleaseHolds();
            return;
        }

        // ウィンドウが無いのに編集状態が残っていたら外す
        if (Typing && !Windows.AnyVisible)
            WindowGui.ClearFocus();

        // 入力欄の編集中は、キーを入力欄に渡してホットキーを止める
        if (Typing)
        {
            FeedTyping();
            Rusk.Triggers.ReleaseHolds();
            return;
        }

        // メニュー開閉
        var menuKey = Rusk.MenuSettings.EffectiveMenuKey;
        if (NewInput.WasPressedThisFrame(menuKey.Key) &&
            menuKey.ModifiersHeld(NewInput.Ctrl, NewInput.Shift, NewInput.Alt))
        {
            ToggleMenu();
            return;
        }
        // ゲームパッド: 2 つのボタンの同時押し (片方を押している間にもう片方を押す)
        var (padA, padB) = Rusk.MenuSettings.PadMenuButtons;
        if (padA != KeyCode.None &&
            ((NewInput.WasPressedThisFrame(padA) && NewInput.IsPressed(padB)) ||
             (NewInput.WasPressedThisFrame(padB) && NewInput.IsPressed(padA))))
        {
            ToggleMenu(viaPad: true);
            return;
        }

        if (_menuOpen && ClickMode)
        {
            // ClickGUI 中はゲーム操作とぶつからないよう、ホットキーを止める
            Rusk.Triggers.ReleaseHolds();
            return;
        }

        if (_menuOpen && _tabGui.HandleInput())
        {
            ToggleMenu(); // パッドの B でいちばん上から戻った
            return;
        }

        // パッドでメニューを操作している間は、パッドのボタンをホットキーに配らない
        HotkeyDispatcher.Dispatch(skipPad: _menuOpen && _padMenu);
        Rusk.Triggers.UpdateHolds(NewInput.Ctrl, NewInput.Shift, NewInput.Alt);
    }

    public void ToggleMenu(bool viaPad = false)
    {
        _menuOpen = !_menuOpen;
        if (_menuOpen && viaPad)
        {
            _padMenu = true;
            GameInputLock.Lock();
        }
        if (!_menuOpen)
        {
            _padMenu = false;
            GameInputLock.Unlock();
            _clickGui.OnClose();
            HotkeyCapture.Cancel();
            Rusk.Config.Save();
        }
    }

    public void ReleaseKeys()
    {
        // フォーカスを失ったら Hold 中のトリガーを離す
        Rusk.Triggers.ReleaseHolds();
    }

    public void OnGUI()
    {
        try
        {
            // Mod の HUD やウィンドウが RusK と同じ配色を使えるように共有する
            RuskStyle.Set(_theme.Scale, _theme.Accent(), _theme.Background, _theme.Header, _theme.ModuleBg,
                _theme.ModuleHover, _theme.Text, _theme.TextDim, _theme.SliderTrack);

            _hud.Draw();
            if (_menuOpen)
            {
                if (ClickMode) _clickGui.Draw(Windows);
                else _tabGui.Draw();
            }
            Windows.Draw(); // ウィンドウは最前面
        }
        catch (Exception e)
        {
            Rusk.Log.LogError($"UI draw error: {e}");
        }
    }

    private void ForceCursor()
    {
        if (!_cursorForced)
        {
            _prevCursorVisible = Cursor.visible;
            _prevCursorLock = Cursor.lockState;
            _cursorForced = true;
        }
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
    }

    private void RestoreCursor()
    {
        if (!_cursorForced) return;
        _cursorForced = false;
        Cursor.visible = _prevCursorVisible;
        Cursor.lockState = _prevCursorLock;
    }
}
