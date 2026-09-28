using System;
using System.IO;
using System.Linq;

namespace RusK.Mods.Model.Vrm;

/// <summary>
/// VRM を付けたキャラの装飾品 (差し込み口 WeaponHolder_1～4) を部位ごとに表示するか。
/// VRM の体格や髪型によっては、頭の装飾品が髪に埋まったり頭にめり込んだりするので、隠せるようにする
/// (RusK/data/model/accessories.txt に保存)
/// </summary>
internal static class AccessoryVisibility
{
    /// <summary>差し込み口の番号 (1～4) と部位の名前。0 は武器なので対象外</summary>
    public static readonly (int slot, string name)[] Slots = { (1, "頭"), (2, "肩"), (3, "腰"), (4, "背中") };

    private static readonly bool[] Shown = { true, true, true, true, true };

    private static string FilePath => Path.Combine(VrmEnv.Ctx.DataDirectory, "accessories.txt");

    public static bool IsShown(int slot) => slot < 0 || slot >= Shown.Length || Shown[slot];

    public static void Set(int slot, bool shown)
    {
        if (slot < 1 || slot >= Shown.Length) return;
        Shown[slot] = shown;
        Save();
    }

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                var kv = line.Split('=');
                if (kv.Length == 2 && int.TryParse(kv[0].Trim(), out var slot) && slot >= 1 && slot < Shown.Length)
                    Shown[slot] = kv[1].Trim() != "0";
            }
        }
        catch (Exception e) { VrmEnv.Ctx?.Log.Warning($"Model: accessories.txt を読めません: {e.Message}"); }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(VrmEnv.Ctx.DataDirectory);
            File.WriteAllLines(FilePath, Slots.Select(s => $"{s.slot}={(Shown[s.slot] ? 1 : 0)}"));
        }
        catch (Exception e) { VrmEnv.Ctx?.Log.Warning($"Model: accessories.txt を保存できません: {e.Message}"); }
    }
}
