using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace RusK.Manager;

/// <summary>
/// RusK Mod Manager 自身の更新。動いている exe は消せないが名前は変えられるので、
/// 新しい exe を .new に落とす → 今の exe を .old に → .new を元の名前に → 新しい方を起動して終わる。
/// 次に起動したときに .old を消す
/// </summary>
internal static class SelfUpdater
{
    private static string ExePath => Process.GetCurrentProcess().MainModule.FileName;

    /// <summary>リリースの最新版がこの exe より新しければ、その添付ファイル</summary>
    public static ReleaseAsset Check(ReleaseClient client, Catalog catalog)
    {
        var asset = client.Latest(catalog.ManagerAsset);
        return asset != null && ReleaseClient.CompareVersions(asset.Version, Program.Version) > 0 ? asset : null;
    }

    public static void Apply(ReleaseClient client, ReleaseAsset asset, Action<int> progress)
    {
        var exe = ExePath;
        var fresh = exe + ".new";
        var old = exe + ".old";
        client.Download(asset, fresh, (got, total) => { if (total > 0) progress((int)(100 * got / total)); });
        if (File.Exists(old)) File.Delete(old);
        File.Move(exe, old);
        try
        {
            File.Move(fresh, exe);
        }
        catch
        {
            File.Move(old, exe); // 元に戻す
            throw;
        }
        Process.Start(new ProcessStartInfo(exe, "--updated") { UseShellExecute = false });
    }

    /// <summary>前の版の exe (.old) を消す。前の版がまだ終わりきっていないことがあるので、少し待ちながら何度か試す</summary>
    public static void CleanUp()
    {
        var old = ExePath + ".old";
        for (int i = 0; i < 20 && File.Exists(old); i++)
        {
            try { File.Delete(old); }
            catch { Thread.Sleep(150); }
        }
    }
}
