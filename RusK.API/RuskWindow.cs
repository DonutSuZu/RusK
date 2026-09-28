using System;
using UnityEngine;

namespace RusK.API;

/// <summary>
/// RusK の Flex Window。タイトルバーでドラッグ、右下でリサイズ、✕ で閉じられるウィンドウ。
/// 位置と大きさは Config プロファイルに保存される。
/// Mod はこれを継承して Draw に中身を書き、IModContext.RegisterWindow で登録する。
/// </summary>
public abstract class RuskWindow
{
    private bool _visible;

    /// <param name="name">Mod 内で一意な名前 (保存のキーに使う)</param>
    /// <param name="title">タイトルバーに出す文字</param>
    protected RuskWindow(string name, string title, float defaultWidth = 440f, float defaultHeight = 480f)
    {
        Name = name;
        Title = title;
        DefaultWidth = defaultWidth;
        DefaultHeight = defaultHeight;
    }

    public string Name { get; }
    public string Title { get; set; }
    public float DefaultWidth { get; }
    public float DefaultHeight { get; }
    public float MinWidth { get; set; } = 260f;
    public float MinHeight { get; set; } = 180f;

    /// <summary>"modid:name" 形式の ID。登録時に RusK が設定する</summary>
    public string Id { get; internal set; }
    public IModContext Context { get; internal set; }

    /// <summary>表示 / 非表示が切り替わったとき (✕ で閉じたときも) に呼ばれる</summary>
    public event Action<RuskWindow> VisibleChanged;

    public bool Visible
    {
        get => _visible;
        set
        {
            if (_visible == value) return;
            _visible = value;
            try
            {
                if (value) OnOpen();
                else OnClose();
            }
            catch (Exception e)
            {
                Context?.Log.Error($"[{Id}] {(value ? "OnOpen" : "OnClose")} failed: {e}");
            }
            VisibleChanged?.Invoke(this);
        }
    }

    public void Toggle() => Visible = !Visible;

    protected virtual void OnOpen() { }
    protected virtual void OnClose() { }

    /// <summary>ウィンドウの中身を描く。表示中は IMGUI の描画タイミングごとに呼ばれる</summary>
    public abstract void Draw(WindowGui gui);
}

/// <summary>
/// RusK の現在の配色。RusK 本体が毎フレーム更新するので、Mod の HUD やウィンドウで
/// メニューと同じ色 (ライティング設定込み) を使える。
/// </summary>
public static class RuskStyle
{
    public static float Scale { get; internal set; } = 1f;
    public static Color Accent { get; internal set; } = new(0.36f, 0.62f, 1f, 1f);
    public static Color Background { get; internal set; } = new(0.09f, 0.10f, 0.13f, 0.9f);
    public static Color Header { get; internal set; } = new(0.13f, 0.14f, 0.18f, 0.95f);
    public static Color Row { get; internal set; } = new(0.11f, 0.12f, 0.15f, 0.9f);
    public static Color RowHover { get; internal set; } = new(0.16f, 0.17f, 0.21f, 0.9f);
    public static Color Field { get; internal set; } = new(0.06f, 0.07f, 0.09f, 0.95f);
    public static Color Text { get; internal set; } = new(0.90f, 0.92f, 0.96f, 1f);
    public static Color TextDim { get; internal set; } = new(0.55f, 0.58f, 0.65f, 1f);
    public static Color Track { get; internal set; } = new(0.20f, 0.21f, 0.26f, 1f);

    /// <summary>RusK 本体から配色を設定する (Mod からは呼ばない)</summary>
    public static void Set(float scale, Color accent, Color background, Color header, Color row, Color rowHover,
        Color text, Color textDim, Color track)
    {
        Scale = scale;
        Accent = accent;
        Background = background;
        Header = header;
        Row = row;
        RowHover = rowHover;
        Text = text;
        TextDim = textDim;
        Track = track;
        Field = new Color(background.r * 0.6f, background.g * 0.6f, background.b * 0.6f, Mathf.Max(background.a, 0.9f));
    }
}
