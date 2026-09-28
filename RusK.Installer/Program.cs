using System;
using System.Windows.Forms;

namespace RusK.Installer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        // 表示の言語を変えたら、その言語でウィンドウを作り直す (入力したゲームのフォルダは引き継ぐ)
        string path = null;
        while (true)
        {
            var form = new SetupForm(path);
            Application.Run(form);
            if (!form.RestartForLanguage) break;
            path = form.GamePath;
        }
    }
}
