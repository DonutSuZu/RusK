using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using RusK.API;

namespace RusK.Core;

internal sealed class LoadedMod
{
    public string Path { get; init; }
    public ModLoadContext LoadContext { get; init; }
    public Assembly Assembly { get; init; }
    public List<ModContext> Contexts { get; } = new();

    public string DisplayName => Contexts.Count == 1
        ? $"{Contexts[0].Info.Name} v{Contexts[0].Info.Version}"
        : System.IO.Path.GetFileNameWithoutExtension(Path);
}

/// <summary>RusK/mods の DLL を読み込み・アンロード・リロードする</summary>
internal sealed class ModManager
{
    private readonly List<LoadedMod> _loaded = new();

    public IReadOnlyList<LoadedMod> Loaded => _loaded;

    /// <summary>mods フォルダにあるが、まだ読み込まれていない DLL</summary>
    public IEnumerable<string> UnloadedFiles =>
        Directory.GetFiles(Rusk.ModsDir, "*.dll")
            .Where(f => !_loaded.Any(m => SamePath(m.Path, f)))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);

    public void LoadAll()
    {
        foreach (var file in UnloadedFiles.ToArray())
            Load(file);
    }

    public bool Load(string path)
    {
        path = System.IO.Path.GetFullPath(path);
        if (_loaded.Any(m => SamePath(m.Path, path))) return false;

        var fileName = System.IO.Path.GetFileName(path);
        var alc = new ModLoadContext(System.IO.Path.GetFileNameWithoutExtension(path), Rusk.CollectibleMods.Value);
        var report = Rusk.Doctor.Begin(path);

        Assembly assembly;
        try
        {
            // ファイルをロックしないようにメモリから読む (ゲーム起動中でも DLL を上書きできる)
            using var dll = new MemoryStream(File.ReadAllBytes(path));
            var pdbPath = System.IO.Path.ChangeExtension(path, ".pdb");
            using var pdb = File.Exists(pdbPath) ? new MemoryStream(File.ReadAllBytes(pdbPath)) : null;
            assembly = alc.LoadFromStream(dll, pdb);
        }
        catch (Exception e)
        {
            Rusk.Log.LogError($"Failed to load {fileName}: {e}");
            Rusk.Notifications.Push($"{fileName} の読み込みに失敗", NotifyLevel.Error);
            Rusk.Doctor.RecordLoadError(report, "DLL を読み込めません", e);
            if (alc.IsCollectible) alc.Unload();
            return false;
        }

        // Check: OnLoad より先に検査する (壊れた参照があると OnLoad の途中で落ちるため)
        try { Rusk.Doctor.CheckAssembly(report, assembly); }
        catch (Exception e) { Rusk.Log.LogWarning($"[Check] {fileName}: {e.Message}"); }

        var mod = new LoadedMod { Path = path, LoadContext = alc, Assembly = assembly };

        foreach (var type in GetLoadableTypes(assembly))
        {
            if (type.IsAbstract || !typeof(IRuskMod).IsAssignableFrom(type)) continue;
            var info = type.GetCustomAttribute<RuskModAttribute>();
            if (info == null)
            {
                Rusk.Log.LogWarning($"{type.FullName} は [RuskMod] 属性がないのでスキップ");
                continue;
            }

            if (FindContext(info.Id) != null)
            {
                Rusk.Log.LogWarning($"Mod ID '{info.Id}' はすでに読み込まれているのでスキップ ({fileName})");
                continue;
            }

            var context = new ModContext(info, path);
            Rusk.Doctor.SetInfo(report, info);
            try
            {
                context.Instance = (IRuskMod)Activator.CreateInstance(type);
                context.Instance.OnLoad(context);
                mod.Contexts.Add(context);
                Rusk.Log.LogInfo($"Loaded mod {info.Name} {info.Version} ({info.Id}) from {fileName}");
            }
            catch (Exception e)
            {
                Rusk.Log.LogError($"{info.Name}.OnLoad failed: {e}");
                Rusk.Notifications.Push($"{info.Name} の初期化に失敗", NotifyLevel.Error);
                Rusk.Doctor.RecordLoadError(report, "初期化 (OnLoad) に失敗", e);
                Teardown(context, callOnUnload: false);
            }
        }

        if (mod.Contexts.Count == 0)
        {
            Rusk.Log.LogWarning($"{fileName} に読み込める Mod がありません");
            if (report.Issues.Count == 0)
                Rusk.Doctor.Add(report, Severity.Error, "読み込める Mod がありません",
                    "IRuskMod を実装し [RuskMod] が付いたクラスがない");
            if (alc.IsCollectible) alc.Unload();
            return false;
        }

        _loaded.Add(mod);
        Rusk.Notifications.Push($"Loaded {mod.DisplayName}", NotifyLevel.Success);
        return true;
    }

    public void Unload(LoadedMod mod)
    {
        if (!_loaded.Remove(mod)) return;

        foreach (var context in mod.Contexts)
            Teardown(context, callOnUnload: true);

        if (mod.LoadContext.IsCollectible)
            mod.LoadContext.Unload();

        Rusk.Doctor.Remove(mod.Path);
        Rusk.Log.LogInfo($"Unloaded {mod.DisplayName}");
        Rusk.Notifications.Push($"Unloaded {mod.DisplayName}");
    }

    public void Reload(LoadedMod mod)
    {
        var path = mod.Path;
        Unload(mod);
        Load(path);
    }

    public void UnloadAll()
    {
        foreach (var mod in _loaded.ToArray())
            Unload(mod);
    }

    private static void Teardown(ModContext context, bool callOnUnload)
    {
        // 0. Hold 中のトリガーを離す (この Mod のモジュールを ON にしたままにしない)
        Rusk.Triggers.ReleaseHolds();

        // 1. モジュールを OFF にして登録解除 (状態は Config に控える)、アクションも解除
        try
        {
            Rusk.Modules.UnregisterOwner(context);
            Rusk.Actions.UnregisterOwner(context);
            Rusk.Ui.Windows.UnregisterOwner(context);
        }
        catch (Exception e) { Rusk.Log.LogError($"[{context.Info.Id}] unregister failed: {e}"); }

        // 2. Mod 独自の後片付け
        if (callOnUnload && context.Instance != null)
        {
            try { context.Instance.OnUnload(); }
            catch (Exception e) { Rusk.Log.LogError($"[{context.Info.Id}] OnUnload failed: {e}"); }
        }

        // 3. この Mod が当てた Harmony パッチを全部外す
        try { context.Harmony.UnpatchSelf(); }
        catch (Exception e) { Rusk.Log.LogError($"[{context.Info.Id}] unpatch failed: {e}"); }

        context.Dispose();
    }

    private ModContext FindContext(string id) =>
        _loaded.SelectMany(m => m.Contexts).FirstOrDefault(c => c.Info.Id == id);

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try { return assembly.GetTypes(); }
        catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null); }
    }

    private static bool SamePath(string a, string b) =>
        string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
}

/// <summary>Mod ごとの AssemblyLoadContext。同じ DLL を何度でも読み直せる</summary>
internal sealed class ModLoadContext : AssemblyLoadContext
{
    public ModLoadContext(string name, bool collectible) : base("RusK:" + name, collectible) { }

    protected override Assembly Load(AssemblyName name)
    {
        // RusK.API / BepInEx / Harmony / interop などの共有アセンブリは、既にロード済みのものを使う
        foreach (var assembly in Default.Assemblies)
        {
            if (string.Equals(assembly.GetName().Name, name.Name, StringComparison.OrdinalIgnoreCase))
                return assembly;
        }

        // Mod が同梱する依存 DLL は RusK/mods/libs に置く
        var lib = System.IO.Path.Combine(Rusk.ModsDir, "libs", name.Name + ".dll");
        if (File.Exists(lib))
        {
            using var stream = new MemoryStream(File.ReadAllBytes(lib));
            return LoadFromStream(stream);
        }

        // 見つからなければ既定の解決 (BepInEx の interop 解決) に任せる
        return null;
    }
}
