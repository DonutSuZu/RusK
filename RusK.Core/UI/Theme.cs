using RusK.API;
using UnityEngine;

namespace RusK.Core.UI;

/// <summary>
/// 見た目の一元管理。値は Visual > Menu の設定 (MenuSettingsModule) から読む。
/// ライティング (Rainbow など) もここで計算するので、新しい光り方を足すときは Accent() に追加する。
/// </summary>
internal sealed class Theme
{
    private static MenuSettingsModule S => Rusk.MenuSettings;

    // レイアウト
    public float Scale => Mathf.Clamp(Screen.height / 1080f, 0.75f, 2f);
    public float PanelWidth => 160f * Scale;
    public float HeaderHeight => 26f * Scale;
    public float ModuleHeight => 20f * Scale;
    public float SettingHeight => 18f * Scale;
    public float PanelGap => 8f * Scale;
    public int FontSize => Mathf.RoundToInt(13f * Scale);
    public int SmallFontSize => Mathf.RoundToInt(11f * Scale);

    // 設定
    public float Opacity => S?.Opacity.Value ?? 0.85f;
    public bool ShowArrayList => S?.ArrayList.Value ?? true;
    public bool ShowWatermark => S?.Watermark.Value ?? true;
    public bool ShowNotifications => S?.Notifications.Value ?? true;
    public float ListAnimSpeed => S?.ListAnim.Value ?? 1.5f;

    // 色
    public Color Background => new(0.09f, 0.10f, 0.13f, Opacity);
    public Color Header => new(0.13f, 0.14f, 0.18f, Mathf.Clamp01(Opacity + 0.1f));
    public Color ModuleBg => new(0.11f, 0.12f, 0.15f, Opacity);
    public Color ModuleHover => new(0.16f, 0.17f, 0.21f, Opacity);
    public Color SettingBg => new(0.08f, 0.09f, 0.11f, Opacity);
    public Color Text => new(0.90f, 0.92f, 0.96f, 1f);
    public Color TextDim => new(0.55f, 0.58f, 0.65f, 1f);
    public Color SliderTrack => new(0.20f, 0.21f, 0.26f, 1f);
    public Color Shadow => new(0f, 0f, 0f, 0.35f * Opacity);

    public Color Selected(float alpha = 0.22f) => Render.WithAlpha(Accent(), alpha);

    /// <summary>
    /// アクセントカラー。ライティング設定に応じて時間で変化する。
    /// phase をずらすと、ArrayList の行ごとに色をずらした「流れる」表現になる。
    /// </summary>
    public Color Accent(float phase = 0f)
    {
        if (S == null) return new Color(0.36f, 0.62f, 1f, 1f);

        var accent = S.Accent;
        float time = Time.realtimeSinceStartup * S.LightSpeed.Value;

        switch (S.Lighting.Value)
        {
            case 1: // Rainbow
                return Render.Hsv(time * 0.08f + phase, Mathf.Max(accent.S, 0.45f), Mathf.Max(accent.V, 0.85f));

            case 2: // Breathing
            {
                float k = 0.55f + 0.45f * (Mathf.Sin(time * 2f - phase * 6f) * 0.5f + 0.5f);
                return Render.Hsv(accent.H, accent.S, accent.V * k);
            }

            case 3: // Wave
                return Render.Hsv(accent.H + Mathf.Sin(time * 1.5f - phase * 10f) * 0.07f, accent.S, accent.V);

            default: // Static
                return accent.Value;
        }
    }

    public Color LevelColor(NotifyLevel level) => level switch
    {
        NotifyLevel.Success => new Color(0.4f, 0.85f, 0.5f),
        NotifyLevel.Warning => new Color(0.95f, 0.75f, 0.3f),
        NotifyLevel.Error => new Color(0.95f, 0.4f, 0.4f),
        _ => Accent(),
    };
}
