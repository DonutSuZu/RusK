using System;
using System.Collections.Generic;
using System.Linq;
using RusK.API;
using UnityEngine;

namespace RusK.Core.UI;

/// <summary>
/// Flex Window の管理。タイトルバーでドラッグ、右下のつまみでリサイズ、✕ で閉じる。
/// 最後にクリックしたウィンドウが最前面。位置と大きさは Config の Windows に保存される。
/// </summary>
internal sealed class WindowManager
{
    private sealed class Entry
    {
        public RuskWindow Window;
        public ModContext Owner;
        public readonly WindowGui Gui = new();
        public float ScrollY;
    }

    private readonly Theme _t;
    private readonly List<Entry> _entries = new(); // 後ろほど前面

    private Entry _drag, _resize;
    private Vector2 _grabOffset;
    private bool _mouseHeld;

    public WindowManager(Theme theme) => _t = theme;

    public bool AnyVisible => _entries.Any(e => e.Window.Visible);

    public void Register(RuskWindow window, ModContext owner)
    {
        if (window == null) throw new ArgumentNullException(nameof(window));
        var id = $"{owner.Info.Id}:{window.Name}";
        if (_entries.Any(e => e.Window.Id == id))
            throw new InvalidOperationException($"Window '{id}' is already registered");

        window.Id = id;
        window.Context = owner;
        window.VisibleChanged += OnVisibleChanged;
        _entries.Add(new Entry { Window = window, Owner = owner });
    }

    public void UnregisterOwner(ModContext owner)
    {
        foreach (var e in _entries.Where(e => e.Owner == owner).ToList())
        {
            e.Window.Visible = false;
            e.Window.VisibleChanged -= OnVisibleChanged;
            _entries.Remove(e);
        }
    }

    /// <summary>マウスが表示中のウィンドウの上にあるか (下にあるメニューのクリックを止めるため)</summary>
    public bool IsPointerOver(Vector2 p) => TopAt(p) != null;

    private void OnVisibleChanged(RuskWindow window)
    {
        if (!window.Visible) return;
        // 開いたウィンドウを最前面へ
        var entry = _entries.FirstOrDefault(e => e.Window == window);
        if (entry == null) return;
        _entries.Remove(entry);
        _entries.Add(entry);
    }

    private WindowState State(Entry e)
    {
        var s = Rusk.Config.Window(e.Window.Id, e.Window.DefaultWidth * _t.Scale, e.Window.DefaultHeight * _t.Scale);
        s.W = Mathf.Clamp(s.W, e.Window.MinWidth * _t.Scale, Screen.width);
        s.H = Mathf.Clamp(s.H, e.Window.MinHeight * _t.Scale, Screen.height);
        s.X = Mathf.Clamp(s.X, 0f, Mathf.Max(0f, Screen.width - s.W));
        s.Y = Mathf.Clamp(s.Y, 0f, Mathf.Max(0f, Screen.height - _t.HeaderHeight));
        return s;
    }

    private Entry TopAt(Vector2 p)
    {
        for (int i = _entries.Count - 1; i >= 0; i--)
        {
            var e = _entries[i];
            if (!e.Window.Visible) continue;
            var s = State(e);
            if (p.x >= s.X && p.x < s.X + s.W && p.y >= s.Y && p.y < s.Y + s.H) return e;
        }
        return null;
    }

    public void Draw()
    {
        var ev = Event.current;
        if (ev == null || !AnyVisible) return;
        var mouse = ev.mousePosition;

        if (ev.type == EventType.MouseDown && ev.button == 0) _mouseHeld = true;
        if (ev.type == EventType.MouseUp && ev.button == 0) _mouseHeld = false;

        var hover = TopAt(mouse);

        // クリックしたウィンドウを最前面へ。ウィンドウの外をクリックしたら入力欄の編集を終える
        if (ev.type == EventType.MouseDown)
        {
            if (hover != null)
            {
                _entries.Remove(hover);
                _entries.Add(hover);
            }
            else
            {
                WindowGui.ClearFocus();
            }
        }

        UpdateDragResize(mouse);

        foreach (var e in _entries.ToList())
        {
            if (!e.Window.Visible) continue;
            DrawWindow(e, e == hover || e == _drag || e == _resize);
        }
    }

    private void UpdateDragResize(Vector2 mouse)
    {
        if (!_mouseHeld)
        {
            _drag = null;
            _resize = null;
            return;
        }

        if (_drag != null)
        {
            var s = State(_drag);
            s.X = mouse.x - _grabOffset.x;
            s.Y = mouse.y - _grabOffset.y;
        }
        if (_resize != null)
        {
            var s = State(_resize);
            s.W = Mathf.Max(_resize.Window.MinWidth * _t.Scale, mouse.x - s.X + _grabOffset.x);
            s.H = Mathf.Max(_resize.Window.MinHeight * _t.Scale, mouse.y - s.Y + _grabOffset.y);
        }
    }

    private void DrawWindow(Entry e, bool input)
    {
        var ev = Event.current;
        var s = State(e);
        float hh = _t.HeaderHeight;
        float grip = 14f * _t.Scale;

        // 影・本体・タイトルバー
        Render.Rect(s.X + 4f, s.Y + 4f, s.W, s.H, _t.Shadow, 6f);
        Render.Rect(s.X, s.Y, s.W, s.H, _t.Background, 6f);
        Render.Rect(s.X, s.Y, s.W, hh, _t.Header, 6f);
        Render.Rect(s.X, s.Y + hh - 2f, s.W, 2f, _t.Accent());
        Render.Text(s.X + 10f, s.Y, s.W - hh - 14f, hh, e.Window.Title, _t.Text, _t.FontSize, TextAnchor.MiddleLeft, bold: true);

        // ✕ ボタン
        float cx = s.X + s.W - hh;
        bool closeHover = input && Render.Contains(cx, s.Y, hh, hh, ev.mousePosition);
        Render.Text(cx, s.Y, hh, hh, "✕", closeHover ? _t.Accent() : _t.TextDim, _t.FontSize, TextAnchor.MiddleCenter);

        if (input && ev.type == EventType.MouseDown && ev.button == 0)
        {
            if (closeHover)
            {
                ev.Use();
                e.Window.Visible = false;
                return;
            }
            if (Render.Contains(s.X, s.Y, s.W - hh, hh, ev.mousePosition))
            {
                _drag = e;
                _grabOffset = ev.mousePosition - new Vector2(s.X, s.Y);
                ev.Use();
            }
            else if (Render.Contains(s.X + s.W - grip, s.Y + s.H - grip, grip, grip, ev.mousePosition))
            {
                _resize = e;
                _grabOffset = new Vector2(s.X + s.W - ev.mousePosition.x, s.Y + s.H - ev.mousePosition.y);
                ev.Use();
            }
        }

        // 中身 (クリップしてスクロール)
        var content = new Rect(s.X, s.Y + hh, s.W, s.H - hh - grip * 0.5f);
        if (input && ev.type == EventType.ScrollWheel && Render.Contains(content.x, content.y, content.width, content.height, ev.mousePosition))
        {
            e.ScrollY += ev.delta.y * 24f * _t.Scale;
            ev.Use();
        }

        GUI.BeginGroup(content);
        try
        {
            e.Gui.Begin(content.width, e.ScrollY, input && _drag == null && _resize == null, _mouseHeld);
            e.Window.Draw(e.Gui);
            e.Gui.End();
        }
        catch (Exception ex)
        {
            Rusk.Log.LogError($"[{e.Window.Id}] Draw failed: {ex}");
            e.Window.Visible = false;
            Rusk.Notifications.Push($"{e.Window.Title} でエラー (ログを確認)", NotifyLevel.Error);
        }
        finally
        {
            GUI.EndGroup();
        }

        // スクロール量を中身の高さに合わせて制限し、スクロールバーを描く
        float contentHeight = e.Gui.EndY - e.Gui.StartY + e.Gui.Padding * 2;
        float maxScroll = Mathf.Max(0f, contentHeight - content.height);
        e.ScrollY = Mathf.Clamp(e.ScrollY, 0f, maxScroll);
        if (maxScroll > 0f)
        {
            float barH = Mathf.Max(20f, content.height * content.height / contentHeight);
            float barY = content.y + (content.height - barH) * (e.ScrollY / maxScroll);
            Render.Rect(s.X + s.W - 5f, barY, 3f, barH, Render.WithAlpha(_t.Accent(), 0.6f), 1.5f);
        }

        // リサイズのつまみ
        for (int i = 1; i <= 3; i++)
            Render.Rect(s.X + s.W - 4f - i * 4f, s.Y + s.H - 4f - (4 - i) * 4f + 2f, 2f, 2f, _t.TextDim);
    }
}
