using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using RusK.API;
using RusK.Core.UI;

namespace RusK.Core;

/// <summary>RusK 全体の状態をまとめるエントリ</summary>
internal static class Rusk
{
    public const string Guid = "rusk.core";
    public const string Name = "RusK";
    public const string Version = "1.1.4";

    private static readonly Queue<Action> Deferred = new();

    public static ManualLogSource Log { get; private set; }

    public static string RootDir { get; private set; }
    public static string ModsDir { get; private set; }
    public static string ConfigsDir { get; private set; }
    public static string DataDir { get; private set; }

    public static ModuleManager Modules { get; private set; }
    public static ActionRegistry Actions { get; private set; }
    public static TriggerManager Triggers { get; private set; }
    public static ModManager Mods { get; private set; }
    public static ConfigManager Config { get; private set; }
    public static NotificationManager Notifications { get; private set; }
    public static RuskUi Ui { get; private set; }

    /// <summary>RusK Check (Mod の診断)</summary>
    public static Doctor Doctor { get; private set; }
    public static CheckWindow CheckWindow { get; private set; }

    /// <summary>Visual > Menu の設定 (GUI の種類・色・ライティングなど)</summary>
    public static MenuSettingsModule MenuSettings { get; private set; }

    /// <summary>Visual > Language (表示の言語)</summary>
    public static LanguageModule Language { get; private set; }

    public static ConfigEntry<bool> CollectibleMods { get; private set; }
    public static ConfigEntry<string> LastProfile { get; private set; }
    public static ConfigEntry<string> LastGameVersion { get; private set; }

    public static void Initialize(BasePlugin plugin)
    {
        Log = plugin.Log;

        RootDir = Path.Combine(Paths.GameRootPath, "RusK");
        ModsDir = Path.Combine(RootDir, "mods");
        ConfigsDir = Path.Combine(RootDir, "configs");
        DataDir = Path.Combine(RootDir, "data");
        foreach (var dir in new[] { RootDir, ModsDir, ConfigsDir, DataDir })
            Directory.CreateDirectory(dir);

        // 言語: 本体の言語ファイル (DLL に埋め込み) と、上書き・追加用のフォルダ RusK\lang
        RuskLang.OverrideDir = Path.Combine(RootDir, "lang");
        RuskLang.Register(RuskLang.CoreId, typeof(Rusk).Assembly, w => plugin.Log.LogWarning(w));

        CollectibleMods = plugin.Config.Bind("Loader", "CollectibleMods", false,
            "true: アンロード時に Mod のアセンブリをメモリから解放する (実験的)。\n" +
            "false: パッチとモジュールは解除するが、アセンブリはメモリに残る (安定)");
        LastProfile = plugin.Config.Bind("Loader", "LastProfile", "default", "起動時に読み込む Config プロファイル");
        LastGameVersion = plugin.Config.Bind("Check", "LastGameVersion", "",
            "前回起動したときのゲームのバージョン (更新の検出用。書き換え不要)");

        Doctor = new Doctor();
        Doctor.CheckGameUpdate(LastGameVersion);
        Notifications = new NotificationManager();
        Modules = new ModuleManager();
        Actions = new ActionRegistry();
        Triggers = new TriggerManager();
        Config = new ConfigManager(ConfigsDir);
        Mods = new ModManager();
        Ui = new RuskUi();

        Config.Load(LastProfile.Value);
        RegisterBuiltins();
        Mods.LoadAll();

        Log.LogMessage($"{Name} {Version} ready ({Mods.Loaded.Count} mod(s), {Modules.All.Count} module(s))");
        Notifications.Push(L.T("{0} {1} loaded — {2} でメニュー", Name, Version, MenuSettings.EffectiveMenuKey.Display),
            NotifyLevel.Success);
        Doctor.NotifyStartup();
    }

    /// <summary>
    /// RusK 本体の機能を、ID "rusk" の組み込み Mod として登録する
    /// (Visual > Menu の設定、Check ウィンドウ)
    /// </summary>
    private static void RegisterBuiltins()
    {
        var context = new ModContext(new RuskModAttribute("rusk", Name, Version), sourcePath: null);
        MenuSettings = new MenuSettingsModule();
        context.RegisterModule(MenuSettings);
        Language = new LanguageModule();
        context.RegisterModule(Language);
        Language.Apply();

        CheckWindow = new CheckWindow();
        context.RegisterWindow(CheckWindow);
        context.RegisterAction("OpenCheck", () => CheckWindow.Toggle(), "RusK Check (Mod の診断) を開く");
    }

    /// <summary>
    /// 次のフレームの最初に実行する。GUI の描画中に Mod のアンロードなど
    /// 「一覧を書き換える処理」を安全に行うために使う。
    /// </summary>
    public static void Defer(Action action) => Deferred.Enqueue(action);

    public static void RunDeferred()
    {
        while (Deferred.Count > 0)
        {
            var action = Deferred.Dequeue();
            try { action(); }
            catch (Exception e) { Log.LogError($"Deferred action failed: {e}"); }
        }
    }

    public static void Shutdown()
    {
        try
        {
            Config?.Save();
            Mods?.UnloadAll();
        }
        catch (Exception e)
        {
            Log.LogError(e);
        }
    }
}
