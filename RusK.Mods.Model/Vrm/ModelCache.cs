using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RusK.Mods.Model.Vrm;

/// <summary>
/// モデルの読み込みの使い回しと先読み。キャラが出るたびにモデルを読み直すと、ファイルの読み込み・テクスチャの展開に時間がかかり、
/// 初めての場面ではモデルが付くまで元のキャラが見え、動きもかくつく。
/// - PMX の中身 (PmxFile) と、展開したテクスチャ (PMX の画像・VRM の画像・透明度のテクスチャ) を取っておき、2 回目からは使い回す
///   (使い回すテクスチャはモデルを外しても消さない)
/// - ゲームが起動したら、割り当てたモデルを 1 コマに 1 つずつ先に読んでおく (Preload)
/// </summary>
internal static class ModelCache
{
    private static readonly Dictionary<string, (DateTime stamp, Pmx.PmxFile pmx)> Pmxs = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, (Texture2D tex, bool alpha)> Images = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<IntPtr, (Texture2D tex, (byte[] a, int w, int h)? bytes)> Alphas = new();
    private static readonly HashSet<IntPtr> Shared = new();
    private static readonly Queue<Action> Pending = new();
    private static bool _preloadQueued;

    /// <summary>VRM の画像の使い回しの名前に使う、読み込み中のファイル</summary>
    [ThreadStatic] internal static string CurrentFile;

    public static bool IsShared(Texture tex) => tex != null && Shared.Contains(tex.Pointer);

    public static Pmx.PmxFile Pmx(string path)
    {
        var stamp = File.GetLastWriteTimeUtc(path);
        if (Pmxs.TryGetValue(path, out var c) && c.stamp == stamp) return c.pmx;
        var pmx = global::RusK.Mods.Model.Vrm.Pmx.PmxFile.Load(path);
        Pmxs[path] = (stamp, pmx);
        return pmx;
    }

    /// <summary>画像ファイル (PMX のテクスチャ) を展開したもの</summary>
    public static (Texture2D tex, bool alpha) Image(string file)
    {
        if (Images.TryGetValue(file, out var c) && c.tex != null) return c;
        var tex = global::RusK.Mods.Model.Vrm.Pmx.ImageDecoder.Load(file, out bool alpha);
        if (tex != null) Keep(tex);
        Images[file] = (tex, alpha);
        return (tex, alpha);
    }

    /// <summary>VRM (glb) の中の画像。make は初めてのときだけ呼ぶ</summary>
    public static Texture2D GlbImage(int index, Func<Texture2D> make)
    {
        if (CurrentFile == null) return make();
        var key = CurrentFile + "#" + index;
        if (Images.TryGetValue(key, out var c) && c.tex != null) return c.tex;
        var tex = make();
        if (tex != null) Keep(tex);
        Images[key] = (tex, false);
        return tex;
    }

    public static bool TryAlpha(Texture2D src, out Texture2D tex, out (byte[] a, int w, int h)? bytes)
    {
        if (src != null && Alphas.TryGetValue(src.Pointer, out var c) && c.tex != null) { tex = c.tex; bytes = c.bytes; return true; }
        tex = null; bytes = null;
        return false;
    }

    public static void PutAlpha(Texture2D src, Texture2D tex, (byte[] a, int w, int h)? bytes)
    {
        if (src == null || tex == null) return;
        Keep(tex);
        Alphas[src.Pointer] = (tex, bytes);
    }

    private static void Keep(Texture2D tex)
    {
        tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
        Shared.Add(tex.Pointer);
    }

    // ------------------------------------------------------------------ 先読み

    /// <summary>割り当てたモデルを先に読む (1 回だけ)。PMX は中身とテクスチャ、VRM は画像</summary>
    public static void Preload(IEnumerable<string> files)
    {
        if (_preloadQueued) return;
        _preloadQueued = true;
        foreach (var f in files)
        {
            var file = f;
            if (!File.Exists(file)) continue;
            if (file.EndsWith(".pmx", StringComparison.OrdinalIgnoreCase))
            {
                Pending.Enqueue(() =>
                {
                    var pmx = Pmx(file);
                    var dir = Path.GetDirectoryName(file)!;
                    foreach (var t in pmx.Textures)
                    {
                        var rel = t.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar).Trim();
                        var img = Path.Combine(dir, rel);
                        if (!File.Exists(img)) img = Path.Combine(dir, Path.GetFileName(rel));
                        if (File.Exists(img)) Pending.Enqueue(() => { try { Image(img); } catch { } });
                    }
                });
            }
        }
        if (Pending.Count > 0) VrmEnv.Ctx?.Log.Info($"Model: モデル {Pending.Count} 個を先に読み込みます");
    }

    /// <summary>毎フレーム: 先読みを 1 つ進める</summary>
    public static void Tick()
    {
        if (Pending.Count == 0) return;
        var step = Pending.Dequeue();
        try { step(); }
        catch (Exception e) { VrmEnv.Ctx?.Log.Warning($"Model: 先読みに失敗: {e.Message}"); }
        if (Pending.Count == 0) VrmEnv.Ctx?.Log.Info($"Model: 先読みが終わりました (PMX {Pmxs.Count}、テクスチャ {Images.Count})");
    }
}
