using System.Collections.Generic;
using RusK.API;
using UnityEngine;

namespace RusK.Core;

/// <summary>RusK Check の結果を見るウィンドウ。Mods > Check か、アクション OpenCheck で開く</summary>
internal sealed class CheckWindow : RuskWindow
{
    private readonly HashSet<string> _expanded = new();

    public CheckWindow() : base("check", "RusK Check", 520f, 480f)
    {
        MinWidth = 360f;
        MinHeight = 240f;
    }

    public override void Draw(WindowGui gui)
    {
        var doctor = Rusk.Doctor;

        gui.Header(L.T("ゲーム"), string.IsNullOrEmpty(doctor.GameVersion) ? L.T("バージョン不明") : $"v{doctor.GameVersion}");
        if (doctor.PreviousGameVersion != null)
            gui.Label("▲ " + L.T("ゲームが更新されました (v{0} → v{1})", doctor.PreviousGameVersion, doctor.GameVersion), Warn, bold: true);
        gui.Label(L.T("結果: {0}", doctor.Summary), Color(doctor.ErrorCount > 0 ? Severity.Error
            : doctor.WarningCount > 0 ? Severity.Warning : Severity.Ok), bold: true);

        gui.BeginRow(1f, 1f);
        if (gui.Button(L.T("再チェック"), accent: true)) doctor.Recheck();
        if (gui.Button(L.T("レポートを書き出す")))
        {
            doctor.WriteReport();
            Rusk.Notifications.Push(L.T("RusK\\check_report.txt に書き出しました"), NotifyLevel.Success);
        }

        gui.Space(6f);
        int count = 0;
        foreach (var _ in doctor.Reports) count++;
        gui.Header("Mod", L.T("{0} 個", count));
        if (count == 0)
        {
            gui.Label(L.T("読み込んだ Mod はありません"), RuskStyle.TextDim, small: true);
            return;
        }

        foreach (var r in doctor.Reports)
        {
            bool open = _expanded.Contains(r.Path);
            string summary = r.Issues.Count == 0 ? L.T("正常") : L.T("{0} 件", r.Issues.Count);
            if (gui.Selectable($"{Doctor.Mark(r.Worst)}  {r.Name}  v{r.Version}", open, summary, Color(r.Worst)))
            {
                if (!_expanded.Add(r.Path)) _expanded.Remove(r.Path);
            }

            if (!open) continue;

            gui.Label($"   {System.IO.Path.GetFileName(r.Path)}   " + L.T("メソッド {0} 個 / パッチ {1} 個を検査", r.CheckedMethods, r.CheckedPatches) +
                      "   " + (r.TestedGameVersion != "" ? L.T("確認済みゲーム v{0}", r.TestedGameVersion) : L.T("(確認済みゲームの版の記載なし)")),
                RuskStyle.TextDim, small: true);

            if (r.Issues.Count == 0)
            {
                gui.Label("   " + L.T("問題は見つかりませんでした"), RuskStyle.TextDim, small: true);
                continue;
            }

            foreach (var issue in r.Issues)
            {
                gui.Label($"   {Doctor.Mark(issue.Severity)} {L.T(issue.Title)}{(issue.Count > 1 ? $"  ×{issue.Count}" : "")}",
                    Color(issue.Severity), small: true);
                gui.Label($"      {Trim(issue.Detail, 110)}", RuskStyle.TextDim, small: true);
            }
        }
    }

    private static readonly Color Warn = new(0.95f, 0.75f, 0.3f);

    private static Color Color(Severity s) => s switch
    {
        Severity.Error => new Color(0.95f, 0.4f, 0.4f),
        Severity.Warning => Warn,
        _ => new Color(0.4f, 0.85f, 0.5f),
    };

    private static string Trim(string s, int max) =>
        string.IsNullOrEmpty(s) || s.Length <= max ? s : s.Substring(0, max) + "…";
}
