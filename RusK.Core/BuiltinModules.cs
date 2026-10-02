using System.Collections.Generic;
using System.IO;
using System.Linq;
using RusK.API;
using UnityEngine;

namespace RusK.Core;

/// <summary>
/// Visual カテゴリの「Menu」。RusK 本体の見た目と操作の設定をまとめた、ON/OFF を持たないモジュール。
/// 設定は他のモジュールと同じく Config プロファイルに保存される。
/// </summary>
/// <summary>
/// Visual カテゴリの「Language」。メニューと各 Mod の表示の言語。Auto はパソコンの言語に合わせる。
/// 言語ファイルは各 Mod の DLL に同梱 (lang/ja.json など)、RusK\lang\&lt;Mod の ID&gt;\&lt;言語&gt;.json で上書き・追加できる
/// </summary>
internal sealed class LanguageModule : Module
{
    private readonly List<string> _codes;

    public LanguageModule() : base("Language", Categories.Visual, "メニューと Mod の表示の言語")
    {
        // 選べる言語: 標準の 3 つ + 上書き用フォルダに言語ファイルがある言語
        _codes = new List<string>(RuskLang.Codes);
        try
        {
            if (!string.IsNullOrEmpty(RuskLang.OverrideDir) && Directory.Exists(RuskLang.OverrideDir))
                foreach (var f in Directory.GetFiles(RuskLang.OverrideDir, "*.json", SearchOption.AllDirectories))
                {
                    var code = Path.GetFileNameWithoutExtension(f).ToLowerInvariant();
                    if (!_codes.Contains(code)) _codes.Add(code);
                }
        }
        catch { }
        var options = new[] { "Auto" }.Concat(_codes.Select(RuskLang.NameOf)).ToArray();
        Language = AddSetting(new ModeSetting("Language", options, 0, "表示の言語 (Auto: パソコンの言語に合わせる)"));
        Language.Changed += Apply;
    }

    public override bool Toggleable => false;
    public override bool VisibleInArrayList => false;

    public ModeSetting Language { get; }

    /// <summary>設定の言語を当てる (起動時と、設定を変えたとき)</summary>
    public void Apply()
    {
        string code = Language.Value == 0 ? SystemCode() : _codes[Language.Value - 1];
        RuskLang.SetLanguage(code);
    }

    private static string SystemCode() => Application.systemLanguage switch
    {
        SystemLanguage.Japanese => "ja",
        SystemLanguage.Chinese or SystemLanguage.ChineseSimplified or SystemLanguage.ChineseTraditional => "zh",
        _ => "en",
    };
}

internal sealed class MenuSettingsModule : Module
{
    public static readonly string[] GuiModes = { "Tab", "Click" };
    public static readonly string[] PadMenuModes = { "LS+RS", "View+Menu", "Off" };
    public static readonly string[] LightingModes = { "Static", "Rainbow", "Breathing", "Wave" };

    public MenuSettingsModule() : base("Menu", Categories.Visual, "RusK のメニューと HUD の見た目")
    {
        GuiMode = AddSetting(new ModeSetting("GUI", GuiModes, 0, "メニューの種類 (Tab: キーボード操作 / Click: マウス操作)"));
        MenuKey = AddSetting(new HotkeySetting("MenuKey", KeyCode.Insert, "メニューを開閉するキー"));
        PadMenu = AddSetting(new ModeSetting("PadMenu", PadMenuModes, 0,
            "ゲームパッドでメニューを開閉するボタン (2 つ同時押し)。開いている間はゲームの操作を止め、十字キーで選択・A で決定・B で戻る"));
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
    public ModeSetting PadMenu { get; }
    public FloatSetting Opacity { get; }
    public ColorSetting Accent { get; }
    public ModeSetting Lighting { get; }
    public FloatSetting LightSpeed { get; }
    public BoolSetting ArrayList { get; }
    public FloatSetting ListAnim { get; }
    public BoolSetting Watermark { get; }
    public BoolSetting Notifications { get; }

    public bool IsClickGui => GuiMode.Value == 1;

    /// <summary>ゲームパッドでメニューを開閉する 2 つのボタン (Off なら None)</summary>
    public (KeyCode a, KeyCode b) PadMenuButtons => PadMenu.Value switch
    {
        0 => (PadButton.LS, PadButton.RS),
        1 => (PadButton.View, PadButton.Menu),
        _ => (KeyCode.None, KeyCode.None),
    };

    /// <summary>メニューキー。None にされてもメニューを開けなくならないよう Insert に戻す</summary>
    public Hotkey EffectiveMenuKey => MenuKey.Value.IsNone ? KeyCode.Insert : MenuKey.Value;
}
