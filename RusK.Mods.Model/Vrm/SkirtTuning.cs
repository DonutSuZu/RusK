using System;
using System.Globalization;
using System.IO;
using System.Linq;

namespace RusK.Mods.Model.Vrm;

/// <summary>
/// スカートと脚のバランスの調整値。VRoid のスカートは数本の骨の列で布の形を近似しているだけなので、
/// ゲームの走りのように脚を大きく振ると、脚がどうしても裾から出てしまう。完全には直せないので、
/// Model Lab からゲーム中に値を変えて、見た目のバランスを探せるようにする (RusK/data/model/skirt.txt に保存)
/// </summary>
internal static class SkirtTuning
{
    /// <summary>スカートの前と後ろが、脚の振りに付いていく量 (0～1)</summary>
    public static float FrontBack = 0.8f;
    /// <summary>スカートの横が、脚の振りに付いていく量 (0～1)</summary>
    public static float Side = 0.4f;
    /// <summary>VRM の太ももの振りの大きさ (1 でゲームのキャラと同じ。下げると振りを抑える)</summary>
    public static float LegSwing = 0.8f;
    /// <summary>揺れ物の当たり判定の太さの倍率</summary>
    public static float Collider = 1.15f;

    private static string FilePath => Path.Combine(VrmEnv.Ctx.DataDirectory, "skirt.txt");

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                var kv = line.Split('=');
                if (kv.Length != 2 || !float.TryParse(kv[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                    continue;
                switch (kv[0].Trim())
                {
                    case "FrontBack": FrontBack = v; break;
                    case "Side": Side = v; break;
                    case "LegSwing": LegSwing = v; break;
                    case "Collider": Collider = v; break;
                }
            }
        }
        catch (Exception e) { VrmEnv.Ctx?.Log.Warning($"Model: skirt.txt を読めません: {e.Message}"); }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(VrmEnv.Ctx.DataDirectory);
            string F(float v) => v.ToString("0.00", CultureInfo.InvariantCulture);
            File.WriteAllLines(FilePath, new[]
            {
                $"FrontBack={F(FrontBack)}", $"Side={F(Side)}", $"LegSwing={F(LegSwing)}", $"Collider={F(Collider)}",
            });
        }
        catch (Exception e) { VrmEnv.Ctx?.Log.Warning($"Model: skirt.txt を保存できません: {e.Message}"); }
    }

    public static void Reset()
    {
        // ゲーム内で試して、脚がスカートから出るのがほぼ分からなくなった組み合わせ
        FrontBack = 0.8f;
        Side = 0.4f;
        LegSwing = 0.8f;
        Collider = 1.15f;
        Save();
    }
}
