using System;
using System.Collections.Generic;

namespace RusK.API;

/// <summary>ClickGUI / TabGUI のカテゴリ名。文字列なので Mod が独自のカテゴリを作ってもよい</summary>
public static class Categories
{
    public const string Combat = "Combat";
    public const string Visual = "Visual";
    public const string Movement = "Movement";
    public const string Player = "Player";
    public const string World = "World";
    public const string Misc = "Misc";
}

/// <summary>
/// メニューに並ぶ 1 つの機能。ON/OFF・トグルキー・設定値を持つ。
/// Mod はこれを継承して IModContext.RegisterModule で登録する。
/// </summary>
public abstract class Module
{
    private readonly List<Setting> _settings = new();
    private readonly HotkeySetting _bind = new("Bind", Hotkey.None, "ON/OFF を切り替えるキー");
    private readonly BoolSetting _listed = new("ShowInList", true, "ON の間、ArrayList (右上の一覧) に表示する");
    private bool _enabled;

    protected Module(string name, string category, string description = "")
    {
        Name = name;
        Category = category;
        Description = description;
    }

    public string Name { get; }
    public string Category { get; }
    public string Description { get; }

    /// <summary>"modid:Name" 形式の ID。登録時に RusK が設定する</summary>
    public string Id { get; internal set; }

    /// <summary>登録元 Mod のコンテキスト。登録時に RusK が設定する</summary>
    public IModContext Context { get; internal set; }

    /// <summary>ON/OFF を切り替えるホットキー。メニューの「Bind」行からも変更できる</summary>
    public Hotkey Keybind
    {
        get => _bind.Value;
        set => _bind.Value = value;
    }

    /// <summary>Keybind を GUI で編集するための設定 (Settings には含まれない)</summary>
    public HotkeySetting BindSetting => _bind;

    /// <summary>ArrayList に出すかを GUI で編集するための設定 (Settings には含まれない)</summary>
    public BoolSetting ListSetting => _listed;

    /// <summary>ArrayList に表示するか (モジュールの VisibleInArrayList と、ユーザーの ShowInList の両方が true)</summary>
    public bool ShownInArrayList => VisibleInArrayList && _listed.Value;

    public IReadOnlyList<Setting> Settings => _settings;

    /// <summary>
    /// false にすると ON/OFF を持たない「設定だけのモジュール」になる。
    /// Enabled にならず、OnUpdate / OnGUI も呼ばれない。
    /// </summary>
    public virtual bool Toggleable => true;

    /// <summary>false にすると ArrayList (有効モジュール一覧) に表示しない</summary>
    public virtual bool VisibleInArrayList => true;

    /// <summary>ArrayList で名前の横に薄く表示する補足 (例: "x5")</summary>
    public virtual string Suffix => null;

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (value && !Toggleable) return;
            if (_enabled == value) return;
            _enabled = value;
            EnabledChanged?.Invoke(this);
        }
    }

    public void Toggle() => Enabled = !Enabled;

    internal event Action<Module> EnabledChanged;

    // OnEnable が例外を投げたときなど、イベントを起こさずに状態だけ戻す
    internal void SetEnabledSilently(bool value) => _enabled = value;

    private string[] _formerIds = Array.Empty<string>();

    /// <summary>前に使っていた ID ("modid:Name")。今の ID の設定が無いとき、ここから設定を引き継ぐ</summary>
    public IReadOnlyList<string> FormerIds => _formerIds;

    /// <summary>
    /// モジュールを別の Mod に移したときや名前を変えたときに、前の ID を登録しておく
    /// (例: MovedFrom("oldmod:ButtonHUD"))。Config に今の ID の設定が無ければ、前の ID の設定を使う
    /// </summary>
    protected void MovedFrom(params string[] ids) => _formerIds = ids ?? Array.Empty<string>();

    protected T AddSetting<T>(T setting) where T : Setting
    {
        _settings.Add(setting);
        return setting;
    }

    public virtual void OnEnable() { }
    public virtual void OnDisable() { }

    /// <summary>有効な間、毎フレーム呼ばれる</summary>
    public virtual void OnUpdate() { }

    /// <summary>有効な間、IMGUI の描画タイミングで呼ばれる。HUD の描画はここで行う</summary>
    public virtual void OnGUI() { }
}
