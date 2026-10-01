using System;
using System.Linq;
using System.Windows.Forms;

namespace RusK.Manager;

internal static class Program
{
    /// <summary>この Mod Manager の版 (csproj の Version)</summary>
    public static string Version => typeof(Program).Assembly.GetName().Version.ToString(3);

    [STAThread]
    private static void Main(string[] args)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // 自分を更新した後なら、前の版の exe (.old) を消す
        try { SelfUpdater.CleanUp(); } catch { }

        if (Settings.Language is { } lang && Strings.Codes.Contains(lang)) Strings.Current = lang;

        // 表示の言語を変えたら、その言語でウィンドウを作り直す
        while (true)
        {
            var form = new ManagerForm();
            Application.Run(form);
            if (!form.RestartForLanguage) break;
        }
    }
}
