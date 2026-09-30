using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using BepInEx;
using HarmonyLib;
using RusK.API;

namespace RusK.Core;

internal enum Severity
{
    Ok,
    Warning,
    Error,
}

internal sealed class Issue
{
    public Severity Severity;
    public string Title;
    public string Detail;
    public int Count = 1;
    public bool Runtime; // 実行中に起きたもの (再チェックで消さない)
}

/// <summary>Mod 1 つ (DLL 1 つ) の診断結果</summary>
internal sealed class ModReport
{
    public string Path;
    public string Name;
    public string Version = "";
    public string TestedGameVersion = "";
    public readonly List<Issue> Issues = new();
    public int CheckedMethods;
    public int CheckedPatches;

    /// <summary>動作確認した版と今のゲームの版が違う (それだけでは注意にしない。検査で問題が出たときの手がかり)</summary>
    public bool Untested;

    public Severity Worst => Issues.Count == 0 ? Severity.Ok : Issues.Max(i => i.Severity);
}

/// <summary>
/// RusK Check (診断)。ゲームの更新で壊れた Mod や、正しく動いていない Mod を見つける。
///   読み込み時: Mod の全メソッドを JIT コンパイルさせて、消えた/形が変わったゲームの関数・型を検出する。
///               Harmony のパッチ先が実在するかも確かめる
///   実行中:     モジュールのエラーや初期化の失敗を Mod ごとに記録する
///   バージョン: build_info.txt のゲームの版と、Mod が動作確認した版を比べる
/// </summary>
internal sealed class Doctor
{
    private readonly Dictionary<string, ModReport> _reports = new(StringComparer.OrdinalIgnoreCase);

    public Doctor()
    {
        GameVersion = ReadGameVersion();
    }

    public string GameVersion { get; }

    /// <summary>前回起動時のゲームの版 (更新されていなければ null)</summary>
    public string PreviousGameVersion { get; private set; }

    public IEnumerable<ModReport> Reports => _reports.Values.OrderByDescending(r => r.Worst).ThenBy(r => r.Name);
    public int ErrorCount => _reports.Values.Count(r => r.Worst == Severity.Error);
    public int WarningCount => _reports.Values.Count(r => r.Worst == Severity.Warning);

    /// <summary>メニューに出す短い状態表示</summary>
    public string Summary =>
        ErrorCount > 0 ? L.T("✗ {0} エラー", ErrorCount) + (WarningCount > 0 ? $" / ▲ {WarningCount}" : "")
        : WarningCount > 0 ? L.T("▲ {0} 注意", WarningCount)
        : L.T("✓ 問題なし");

    /// <summary>ゲームの版が前回から変わったかを記録する (起動時に 1 回)</summary>
    public void CheckGameUpdate(BepInEx.Configuration.ConfigEntry<string> lastVersion)
    {
        if (string.IsNullOrEmpty(GameVersion)) return;
        if (!string.IsNullOrEmpty(lastVersion.Value) && lastVersion.Value != GameVersion)
            PreviousGameVersion = lastVersion.Value;
        lastVersion.Value = GameVersion;
    }

    // ------------------------------------------------------------------ 読み込み時

    public ModReport Begin(string path)
    {
        var report = new ModReport { Path = path, Name = System.IO.Path.GetFileNameWithoutExtension(path) };
        _reports[path] = report;
        return report;
    }

    public void Remove(string path) => _reports.Remove(path);

    public ModReport Find(string path) => path != null && _reports.TryGetValue(path, out var r) ? r : null;

    /// <summary>
    /// Mod の名前・版を記録し、動作確認したゲームの版と比べる。
    /// 版が違うだけでは注意にしない (ゲームの更新のたびに全 Mod を出し直さなくて済むように)。
    /// 本当に壊れているか (消えた関数・型、無くなったパッチ先) は CheckAssembly が調べる
    /// </summary>
    public void SetInfo(ModReport report, RuskModAttribute info)
    {
        report.Name = info.Name;
        report.Version = info.Version;
        report.TestedGameVersion = info.GameVersion ?? "";
        report.Untested = !string.IsNullOrEmpty(report.TestedGameVersion) && !string.IsNullOrEmpty(GameVersion) &&
                          report.TestedGameVersion != GameVersion;
    }

    public void Add(ModReport report, Severity severity, string title, string detail, bool runtime = false)
    {
        if (report == null) return;
        var same = report.Issues.FirstOrDefault(i => i.Title == title && i.Detail == detail);
        if (same != null)
        {
            same.Count++;
            return;
        }
        report.Issues.Add(new Issue { Severity = severity, Title = title, Detail = detail, Runtime = runtime });
        if (severity == Severity.Error)
            Rusk.Log.LogWarning($"[Check] {report.Name}: {title} - {detail}");
    }

    /// <summary>
    /// アセンブリの静的な検査。全メソッドを JIT コンパイルさせると、参照しているゲームの関数・型が
    /// 無くなっていれば MissingMethodException などが起きるので、それを集める
    /// </summary>
    public void CheckAssembly(ModReport report, Assembly assembly)
    {
        report.Issues.RemoveAll(i => !i.Runtime);
        report.CheckedMethods = 0;
        report.CheckedPatches = 0;

        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            foreach (var le in e.LoaderExceptions.Where(x => x != null).Distinct())
                Add(report, Severity.Error, "型を読み込めません", Short(le));
            types = e.Types.Where(t => t != null).ToArray();
        }

        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                                 BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var type in types)
        {
            if (type.ContainsGenericParameters) continue;

            IEnumerable<MethodBase> methods;
            try { methods = type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)); }
            catch (Exception e)
            {
                Add(report, Severity.Error, $"{type.Name} を調べられません", Short(e));
                continue;
            }

            foreach (var m in methods)
            {
                if (m.IsAbstract || m.ContainsGenericParameters) continue;
                if ((m.MethodImplementationFlags & MethodImplAttributes.InternalCall) != 0) continue;
                try
                {
                    RuntimeHelpers.PrepareMethod(m.MethodHandle);
                    report.CheckedMethods++;
                }
                catch (Exception e) when (IsMissingMember(e))
                {
                    Add(report, Severity.Error, "ゲームの関数・型が見つかりません",
                        $"{type.Name}.{m.Name}: {Short(e)}");
                }
                catch
                {
                    // JIT できない特殊なメソッドなどは対象外
                }
            }

            CheckHarmonyPatch(report, type);
        }
    }

    /// <summary>[HarmonyPatch] のパッチ先が今のゲームに実在するか</summary>
    private void CheckHarmonyPatch(ModReport report, Type type)
    {
        bool isPatch;
        try { isPatch = type.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0; }
        catch { return; }
        if (!isPatch) return;

        report.CheckedPatches++;
        try
        {
            // TargetMethods / TargetMethod で対象を動的に決めるパッチ
            // (AccessTools.Method は見つからないと毎回ログに警告を出すので、普通のリフレクションで探す)
            const BindingFlags flags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            var dynamicTargets = type.GetMethod("TargetMethods", flags, null, Type.EmptyTypes, null)
                                 ?? type.GetMethod("TargetMethod", flags, null, Type.EmptyTypes, null);
            if (dynamicTargets != null)
            {
                var result = dynamicTargets.Invoke(null, null);
                bool any = result is MethodBase || (result is System.Collections.IEnumerable list && list.Cast<object>().Any());
                if (!any)
                    Add(report, Severity.Error, "パッチ先が見つかりません", $"{type.Name}: {dynamicTargets.Name} が空");
                return;
            }

            var info = HarmonyMethodExtensions.GetMergedFromType(type);
            if (info?.declaringType == null) return;

            MethodBase target = info.methodType switch
            {
                MethodType.Getter => AccessTools.PropertyGetter(info.declaringType, info.methodName),
                MethodType.Setter => AccessTools.PropertySetter(info.declaringType, info.methodName),
                MethodType.Constructor => AccessTools.Constructor(info.declaringType, info.argumentTypes),
                MethodType.StaticConstructor => AccessTools.Constructor(info.declaringType, info.argumentTypes, true),
                _ => info.methodName == null ? null : AccessTools.Method(info.declaringType, info.methodName, info.argumentTypes),
            };
            if (target == null && info.methodName != null)
                Add(report, Severity.Error, "パッチ先が見つかりません",
                    $"{type.Name} → {info.declaringType.Name}.{info.methodName}");
        }
        catch (Exception e)
        {
            Add(report, Severity.Error, "パッチ先を調べられません", $"{type.Name}: {Short(e)}");
        }
    }

    // ------------------------------------------------------------------ 実行中

    public void RecordRuntimeError(IModContext context, string where, Exception e)
    {
        var report = Find((context as ModContext)?.SourcePath);
        Add(report, Severity.Warning, "実行中にエラー", $"{where}: {Short(e)}", runtime: true);
    }

    public void RecordLoadError(ModReport report, string what, Exception e) =>
        Add(report, Severity.Error, what, Short(e), runtime: true);

    /// <summary>読み込み済みの Mod をもう一度検査する (実行中のエラー記録は残す)</summary>
    public void Recheck()
    {
        foreach (var mod in Rusk.Mods.Loaded)
        {
            var report = Find(mod.Path);
            if (report == null) continue;
            CheckAssembly(report, mod.Assembly);
            foreach (var ctx in mod.Contexts) SetInfo(report, ctx.Info);
        }
        Rusk.Notifications.Push($"Check: {Summary}", ErrorCount > 0 ? NotifyLevel.Error
            : WarningCount > 0 ? NotifyLevel.Warning : NotifyLevel.Success);
    }

    /// <summary>診断結果をテキストにしてログと RusK/check_report.txt に書き出す</summary>
    public string WriteReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"RusK Check  {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"ゲーム: v{GameVersion}" + (PreviousGameVersion != null ? $" (前回 v{PreviousGameVersion} から更新)" : ""));
        sb.AppendLine($"RusK: v{Rusk.Version}");
        sb.AppendLine();
        foreach (var r in Reports)
        {
            sb.AppendLine($"[{Mark(r.Worst)}] {r.Name} v{r.Version}  ({System.IO.Path.GetFileName(r.Path)})" +
                          $"  メソッド {r.CheckedMethods} / パッチ {r.CheckedPatches}" +
                          (r.TestedGameVersion != "" ? $"  確認済みゲーム v{r.TestedGameVersion}" : "") +
                          (r.Untested ? " (今のゲームとは違う版。検査の結果は上のとおり)" : ""));
            foreach (var i in r.Issues)
                sb.AppendLine($"    {Mark(i.Severity)} {i.Title}{(i.Count > 1 ? $" ×{i.Count}" : "")}: {i.Detail}");
        }

        var text = sb.ToString();
        try
        {
            var path = System.IO.Path.Combine(Rusk.RootDir, "check_report.txt");
            File.WriteAllText(path, text, new UTF8Encoding(false));
            Rusk.Log.LogInfo($"[Check] report written: {path}");
        }
        catch (Exception e)
        {
            Rusk.Log.LogWarning($"[Check] report write failed: {e.Message}");
        }
        Rusk.Log.LogInfo("\n" + text);
        return text;
    }

    /// <summary>起動時の結果を通知する</summary>
    public void NotifyStartup()
    {
        if (PreviousGameVersion != null)
        {
            bool broken = ErrorCount > 0;
            Rusk.Notifications.Push(L.T("ゲームが更新されました (v{0} → v{1})", PreviousGameVersion, GameVersion) + " — " +
                                    (broken ? L.T("動かない Mod があります") : L.T("Mod の検査で問題は見つかりませんでした")),
                broken ? NotifyLevel.Warning : NotifyLevel.Info);
        }
        if (ErrorCount > 0 || WarningCount > 0)
            Rusk.Notifications.Push(L.T("Check: {0} (Mods > Check で詳細)", Summary),
                ErrorCount > 0 ? NotifyLevel.Error : NotifyLevel.Warning);
        Rusk.Log.LogInfo($"[Check] {Summary} ({_reports.Count} mod(s), game v{GameVersion})");
    }

    public static string Mark(Severity s) => s switch
    {
        Severity.Error => "✗",
        Severity.Warning => "▲",
        _ => "✓",
    };

    // ------------------------------------------------------------------

    private static bool IsMissingMember(Exception e)
    {
        for (var x = e; x != null; x = x.InnerException)
        {
            if (x is MissingMemberException or TypeLoadException or FileNotFoundException or FileLoadException
                or BadImageFormatException)
                return true;
        }
        return false;
    }

    private static string Short(Exception e)
    {
        while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
        var msg = $"{e.GetType().Name}: {e.Message}";
        return msg.Length > 240 ? msg.Substring(0, 240) + "…" : msg;
    }

    private static string ReadGameVersion()
    {
        try
        {
            var path = System.IO.Path.Combine(Paths.GameRootPath, "build_info.txt");
            if (!File.Exists(path)) return "";
            foreach (var line in File.ReadAllLines(path))
            {
                var parts = line.Split('=');
                if (parts.Length == 2 && parts[0].Trim() == "buildVersion") return parts[1].Trim();
            }
        }
        catch { }
        return "";
    }
}
