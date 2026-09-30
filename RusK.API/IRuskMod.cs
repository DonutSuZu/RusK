using System;
using HarmonyLib;

namespace RusK.API;

/// <summary>
/// RusK の Mod のエントリポイント。RusK/mods/*.dll の中から、
/// これを実装していて [RuskMod] が付いたクラスが読み込まれる。
/// </summary>
public interface IRuskMod
{
    /// <summary>ロード時に呼ばれる。モジュール・アクションの登録はここで行う</summary>
    void OnLoad(IModContext context);

    /// <summary>
    /// アンロード時に呼ばれる。モジュール・アクションの登録解除と Harmony パッチの解除は
    /// RusK が自動で行うので、ここでは static 変数の後片付けなど Mod 独自の処理だけを書けばよい
    /// </summary>
    void OnUnload();
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class RuskModAttribute : Attribute
{
    public RuskModAttribute(string id, string name, string version)
    {
        Id = id;
        Name = name;
        Version = version;
    }

    /// <summary>一意な ID。Config のキーや Harmony の ID に使われる (例: "camera")</summary>
    public string Id { get; }
    public string Name { get; }
    public string Version { get; }
    public string Author { get; set; } = "";
    public string Description { get; set; } = "";

    /// <summary>
    /// 動作確認したゲームのバージョン (build_info.txt の buildVersion、例: "0.0.1873")。
    /// 書いておくと RusK の Check の詳細に表示される (版が違うだけでは注意にしない。関数が消えた・パッチ先が無いなど、本当に壊れているときだけ ✗ になる)
    /// </summary>
    public string GameVersion { get; set; } = "";
}

/// <summary>RusK から Mod に渡される機能の窓口</summary>
public interface IModContext
{
    RuskModAttribute Info { get; }
    IRuskLogger Log { get; }

    /// <summary>この Mod 専用の Harmony インスタンス。アンロード時に自動で UnpatchSelf される</summary>
    Harmony Harmony { get; }

    /// <summary>この Mod が自由にファイルを置けるフォルダ (RusK/data/&lt;id&gt;)</summary>
    string DataDirectory { get; }

    void RegisterModule(Module module);

    /// <summary>
    /// 1 回実行するタイプの処理 (アクション) を登録する。
    /// 登録したアクションは、メニューの「Triggers」で好きなキーに割り当てられる。
    /// </summary>
    ModAction RegisterAction(string name, Action callback, string description = "");

    /// <summary>
    /// Flex Window を登録する。登録しただけでは表示されないので、window.Visible = true で開く。
    /// アンロード時は RusK が自動で閉じて登録解除する。
    /// </summary>
    void RegisterWindow(RuskWindow window);

    void Notify(string message, NotifyLevel level = NotifyLevel.Info);
}

/// <summary>アクショントリガーから呼び出せる処理</summary>
public sealed class ModAction
{
    private readonly Action _callback;

    public ModAction(string name, Action callback, string description = "")
    {
        Name = name;
        Description = description ?? "";
        _callback = callback ?? throw new ArgumentNullException(nameof(callback));
    }

    public string Name { get; }
    public string Description { get; }

    /// <summary>"modid:Name" 形式の ID。登録時に RusK が設定する</summary>
    public string Id { get; internal set; }

    public IModContext Context { get; internal set; }

    public void Invoke() => _callback();
}

public interface IRuskLogger
{
    void Debug(object message);
    void Info(object message);
    void Warning(object message);
    void Error(object message);
}

public enum NotifyLevel
{
    Info,
    Success,
    Warning,
    Error,
}
