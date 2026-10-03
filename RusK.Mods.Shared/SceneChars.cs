using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace RusK.Mods.Shared;

/// <summary>
/// 場にいるキャラの名簿 (操作キャラ・仲間 = PlayerController、画面に見せるモデル = CharacterShowController)。
/// シーン全体から探す (FindObjectsOfType) と、このゲームは部品が多く 1 回 40 ms 以上かかって引っかかるので、
/// ゲームがキャラを用意したとき (PlayerController.SetData / CharacterShowController.InitialSetting) に登録してもらう。
/// 使う Mod は OnLoad で SceneChars.Patch(Context.Harmony) を呼ぶ
/// </summary>
internal static class SceneChars
{
    private static readonly Dictionary<IntPtr, PlayerController> PlayersMap = new();
    private static readonly Dictionary<IntPtr, CharacterShowController> ShowsMap = new();
    private static bool _patched;
    private static bool _seeded;

    public static void Patch(Harmony harmony)
    {
        if (_patched) return;
        _patched = true;
        harmony.PatchAll(typeof(SceneCharsPlayerPatch));
        harmony.PatchAll(typeof(SceneCharsShowPatch));
    }

    public static void Add(PlayerController p) { if (p != null) PlayersMap[p.Pointer] = p; }
    public static void Add(CharacterShowController s) { if (s != null) ShowsMap[s.Pointer] = s; }

    /// <summary>今いる操作キャラ・仲間 (消えたものは外す)</summary>
    public static List<PlayerController> Players()
    {
        Seed();
        var cur = PlayerRef.Current;
        if (cur != null) PlayersMap[cur.Pointer] = cur;
        var list = new List<PlayerController>();
        List<IntPtr> dead = null;
        foreach (var kv in PlayersMap)
        {
            if (kv.Value == null) (dead ??= new List<IntPtr>()).Add(kv.Key);
            else list.Add(kv.Value);
        }
        if (dead != null) foreach (var k in dead) PlayersMap.Remove(k);
        return list;
    }

    /// <summary>今ある画面に見せるモデル (消えたものは外す)</summary>
    public static List<CharacterShowController> Shows()
    {
        Seed();
        var list = new List<CharacterShowController>();
        List<IntPtr> dead = null;
        foreach (var kv in ShowsMap)
        {
            if (kv.Value == null) (dead ??= new List<IntPtr>()).Add(kv.Key);
            else list.Add(kv.Value);
        }
        if (dead != null) foreach (var k in dead) ShowsMap.Remove(k);
        return list;
    }

    /// <summary>Mod を読み込む前からいたキャラ (起動直後の 1 回だけ、シーン全体から探す)</summary>
    private static IntPtr _lastCurrent;

    private static void Seed()
    {
        // 操作キャラが入れ替わった (場面の読み込み・キャラの切り替え) ときも 1 回探し直す (登録の取りこぼしを拾う)
        var cur = PlayerRef.Current;
        var ptr = cur != null ? cur.Pointer : IntPtr.Zero;
        if (ptr != _lastCurrent) { _lastCurrent = ptr; _seeded = false; }
        if (_seeded) return;
        _seeded = true;
        try
        {
            foreach (var p in UnityEngine.Object.FindObjectsOfType<PlayerController>()) Add(p);
            foreach (var s in Resources.FindObjectsOfTypeAll<CharacterShowController>())
                if (s != null && s.gameObject.scene.name != null) Add(s);
        }
        catch { }
    }
}

[HarmonyPatch(typeof(PlayerController), nameof(PlayerController.SetData))]
internal static class SceneCharsPlayerPatch
{
    private static void Postfix(PlayerController __instance) => SceneChars.Add(__instance);
}

[HarmonyPatch(typeof(CharacterShowController), nameof(CharacterShowController.InitialSetting))]
internal static class SceneCharsShowPatch
{
    private static void Postfix(CharacterShowController __instance) => SceneChars.Add(__instance);
}
