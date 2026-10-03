using System;
using System.Linq;
using System.Windows.Forms;
using RusK.Manager;

namespace RusK.PackCreator;

internal static class Program
{
    public static string Version => typeof(Program).Assembly.GetName().Version.ToString(3);

    /// <summary>
    /// 引数: Pack のフォルダ (exe にドラッグ＆ドロップしたとき) → 開く。
    /// --zip 〈Pack のフォルダ〉 〈zip〉 → 画面を出さずに zip を書き出す
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Length >= 3 && args[0] == "--zip")
        {
            var skipped = Pack.Zip(System.IO.Path.GetFullPath(args[1]), System.IO.Path.GetFullPath(args[2]));
            Console.WriteLine("zip: " + args[2] + " skipped: " + string.Join(",", skipped));
            return;
        }
        Run(args.FirstOrDefault(a => System.IO.File.Exists(System.IO.Path.Combine(a, "character.json"))));
    }

    private static void Run(string openFolder)
    {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        // 表示の言語とゲームのフォルダは、Mod Manager と同じ設定 (%APPDATA%\RusK\manager.ini) を使う
        if (Settings.Language is { } lang && Strings.Codes.Contains(lang)) Strings.Current = lang;
        while (true)
        {
            var form = new PackForm(openFolder);
            Application.Run(form);
            if (!form.RestartForLanguage) break;
        }
    }
}
