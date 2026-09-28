using System.Collections.Generic;
using System.Linq;
using RusK.API;
using UnityEngine;

namespace RusK.Core.UI;

/// <summary>常時表示のオーバーレイ: ArrayList (右上), ウォーターマークと通知 (右下), モジュールの HUD</summary>
internal sealed class Hud
{
    private readonly Theme _t;

    // ArrayList の各行のアニメーション状態
    private sealed class ListItem
    {
        public string Label;
        public float Width;
        public float Y, TargetY;
        public float T;       // 0: 隠れている → 1: 表示
        public bool Alive;
    }

    private readonly Dictionary<string, ListItem> _items = new();
    private int _animatedFrame = -1;

    public Hud(Theme theme) => _t = theme;

    public void Draw()
    {
        // 各モジュールの HUD 描画 (必殺技ゲージなど)
        Rusk.Modules.OnGUI();

        if (_t.ShowArrayList) DrawArrayList();
        float watermarkHeight = _t.ShowWatermark ? DrawWatermark() : 0f;
        if (_t.ShowNotifications) DrawNotifications(watermarkHeight);
    }

    /// <summary>右下に「RusK v1.1.0」。描いた高さ (余白込み) を返す</summary>
    private float DrawWatermark()
    {
        string name = Rusk.Name;
        string ver = $" v{Rusk.Version}";
        int fs = _t.FontSize;
        float s = _t.Scale;
        float pad = 8f * s;
        float nameW = Render.TextWidth(name, fs, bold: true);
        float w = nameW + Render.TextWidth(ver, fs) + pad * 2;
        float h = _t.ModuleHeight + 2f * s;
        float margin = 8f * s;
        float x = Screen.width - margin - w;
        float y = Screen.height - margin - h;

        Render.Rect(x, y, w, h, _t.Background);
        Render.Rect(x, y + h - 2f, w, 2f, _t.Accent());
        Render.Text(x + pad, y, nameW + 2f, h, name, _t.Text, fs, TextAnchor.MiddleLeft, bold: true);
        Render.Text(x + pad + nameW, y, w, h, ver, _t.Accent(), fs);
        return h + margin;
    }

    /// <summary>右上に有効モジュールを名前の長い順で並べる。出入りはゆっくりスライド＋フェード</summary>
    private void DrawArrayList()
    {
        float s = _t.Scale;
        float h = _t.ModuleHeight;
        float top = 8f * s;
        float right = Screen.width - 8f * s;
        float pad = 6f * s;

        // アニメーションの更新は 1 フレームに 1 回 (OnGUI は 1 フレームに何度も呼ばれる)
        if (_animatedFrame != Time.frameCount)
        {
            _animatedFrame = Time.frameCount;
            Animate(top, h, pad);
        }

        int i = 0;
        foreach (var item in _items.Values.OrderBy(v => v.Y))
        {
            float e = EaseOutCubic(item.T);
            if (e <= 0.001f) continue;

            float w = item.Width + pad * 2;
            float x = right - w + (1f - e) * (w + 16f * s); // 右からスライドイン
            var accent = Render.WithAlpha(_t.Accent(i * 0.06f), e);

            Render.Rect(x, item.Y, w, h, Render.WithAlpha(_t.Background, _t.Opacity * e));
            Render.Rect(x + w - 2f, item.Y, 2f, h, accent);
            Render.Text(x + pad, item.Y, item.Width + 2f, h, item.Label, Render.WithAlpha(_t.Text, e), _t.FontSize,
                TextAnchor.MiddleLeft, bold: true, shadow: true);
            i++;
        }
    }

    private void Animate(float top, float h, float pad)
    {
        float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
        float speed = _t.ListAnimSpeed;

        var active = Rusk.Modules.All
            .Where(m => m.Enabled && m.ShownInArrayList)
            .Select(m => (m.Id, label: m.Suffix == null ? RuskLang.T(m.Context?.Info?.Id, m.Name) : $"{RuskLang.T(m.Context?.Info?.Id, m.Name)} {m.Suffix}"))
            .Select(a => (a.Id, a.label, width: Render.TextWidth(a.label, _t.FontSize, bold: true)))
            .OrderByDescending(a => a.width)
            .ToList();

        foreach (var item in _items.Values) item.Alive = false;

        float y = top;
        foreach (var (id, label, width) in active)
        {
            if (!_items.TryGetValue(id, out var item))
                _items[id] = item = new ListItem { Y = y, T = 0f };
            item.Alive = true;
            item.Label = label;
            item.Width = width;
            item.TargetY = y;
            y += h + 1f;
        }

        foreach (var key in _items.Keys.ToList())
        {
            var item = _items[key];
            item.T = Mathf.MoveTowards(item.T, item.Alive ? 1f : 0f, dt * speed);
            item.Y = Mathf.Lerp(item.Y, item.TargetY, 1f - Mathf.Exp(-dt * speed * 3f)); // 並び替えもゆっくり
            if (!item.Alive && item.T <= 0f) _items.Remove(key);
        }
    }

    /// <summary>右下、ウォーターマークの上に通知を積む</summary>
    private void DrawNotifications(float bottomOffset)
    {
        var items = Rusk.Notifications.Items;
        if (items.Count == 0) return;

        float s = _t.Scale;
        float h = 24f * s;
        float margin = 8f * s;
        float y = Screen.height - Mathf.Max(bottomOffset, margin) - h;
        double now = Rusk.Notifications.Now;

        for (int i = items.Count - 1; i >= 0; i--)
        {
            var n = items[i];
            float life = (float)((now - n.CreatedAt) / NotificationManager.Lifetime);
            float appear = Mathf.Clamp01(life * 12f);                                  // 最初にスッと出る
            float alpha = life < 0.85f ? appear : Mathf.InverseLerp(1f, 0.85f, life); // 最後にフェードアウト

            float pad = 10f * s;
            float tw = Render.TextWidth(n.Text, _t.FontSize);
            float w = tw + pad * 2 + 4f;
            float x = Screen.width - margin - w + (1f - EaseOutCubic(appear)) * 30f * s;
            var color = _t.LevelColor(n.Level);

            Render.Rect(x, y, w, h, Render.WithAlpha(_t.Background, _t.Opacity * alpha));
            Render.Rect(x, y, 3f, h, Render.WithAlpha(color, alpha));
            Render.Text(x + pad, y, tw, h, n.Text, Render.WithAlpha(_t.Text, alpha), _t.FontSize);
            Render.Rect(x, y + h - 2f, w * Mathf.Clamp01(1f - life), 2f, Render.WithAlpha(color, alpha));

            y -= h + 4f * s;
        }
    }

    private static float EaseOutCubic(float t)
    {
        t = Mathf.Clamp01(t);
        float u = 1f - t;
        return 1f - u * u * u;
    }
}
