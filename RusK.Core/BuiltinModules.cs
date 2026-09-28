using RusK.API;
using UnityEngine;

namespace RusK.Core;

/// <summary>
/// Visual カテゴリの「Menu」。RusK 本体の見た目と操作の設定をまとめた、ON/OFF を持たないモジュール。
/// 設定は他のモジュールと同じく Config プロファイルに保存される。
/// </summary>
internal sealed class MenuSettingsModule : Module
{
    public static readonly string[] GuiModes = { "Tab", "Click" };
    public static readonly string[] LightingModes = { "Static", "Rainbow", "Breathing", "Wave" };

    public MenuSettingsModule() : base("Menu", Categories.Visual, "RusK のメニューと HUD の見た目")
    {
        GuiMode = AddSetting(new ModeSetting("GUI", GuiModes, 0, "メニューの種類 (Tab: キーボード操作 / Click: マウス操作)"));
        MenuKey = AddSetting(new HotkeySetting("MenuKey", KeyCode.Insert, "メニューを開閉するキー"));
        Opacity = AddSetting(new FloatSetting("Opacity", 0.85f, 0.2f, 1f, 0.05f, "0.00", "背景の不透明度"));
        Accent = AddSetting(new ColorSetting("Color", "#5C9EFF", "アクセントカラー"));
        Lighting = AddSetting(new ModeSetting("Lighting", LightingModes, 0,
            "Static: 固定色 / Rainbow: 虹色 / Breathing: 明滅 / Wave: 色の波"));
        LightSpeed = AddSetting(new FloatSetting("LightSpeed", 1f, 0.1f, 5f, 0.1f, "0.0", "ライティングの速さ"));
        ArrayList = AddSetting(new BoolSetting("ArrayList", true, "有効なモジュール一覧を右上に表示"));
        ListAnim = AddSetting(new FloatSetting("ListAnim", 1.5f, 0.3f, 8f, 0.1f, "0.0",
            "ArrayList のアニメーション速度 (小さいほどゆっくり)"));
        Watermark = AddSetting(new BoolSetting("Watermark", true, "右下に RusK のバージョンを表示"));
        Notifications = AddSetting(new BoolSetting("Notifications", true, "通知トーストを表示"));
    }

    public override bool Toggleable => false;

    public ModeSetting GuiMode { get; }
    public HotkeySetting MenuKey { get; }
    public FloatSetting Opacity { get; }
    public ColorSetting Accent { get; }
    public ModeSetting Lighting { get; }
    public FloatSetting LightSpeed { get; }
    public BoolSetting ArrayList { get; }
    public FloatSetting ListAnim { get; }
    public BoolSetting Watermark { get; }
    public BoolSetting Notifications { get; }

    public bool IsClickGui => GuiMode.Value == 1;

    /// <summary>メニューキー。None にされてもメニューを開けなくならないよう Insert に戻す</summary>
    public Hotkey EffectiveMenuKey => MenuKey.Value.IsNone ? KeyCode.Insert : MenuKey.Value;
}
