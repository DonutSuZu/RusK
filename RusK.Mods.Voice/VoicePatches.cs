using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace RusK.Mods.Voice;

/// <summary>
/// ゲームが音を鳴らす直前に、RusK\voices に同じ名前のファイルがあれば、その音声に差し替える。
/// ボイスはどれも AudioPlayer を通る (戦闘: PlayVoiceFunc → CreateSFX / PlayPersistentVoice、
/// 吹き出し: PlayBubbleVoice → CreateSFX、会話: ResolveVoiceClip → CreateSFX、キャラ画面: PlayUICharacterVoice)。
/// 効果音も CreateSFX を通るので、同じ名前で置けば差し替わる
/// </summary>
internal static class VoiceSwap
{
    public static VoiceBank Bank;
    public static bool Enabled;
    public static float VolumeScale = 1f;
    public static bool LogPlayed;

    private static readonly HashSet<string> Logged = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>名前が一致すれば clip を差し替える (差し替えたら true)</summary>
    public static bool Swap(ref AudioClip clip, ref float volume, string where)
    {
        try
        {
            if (clip == null) return false;
            var name = clip.name;
            if (LogPlayed) Log(name, where);
            if (!Enabled || Bank == null) return false;
            var replacement = Bank.Find(name);
            if (replacement == null) return false;
            clip = replacement;
            volume *= VolumeScale;
            return true;
        }
        catch (Exception e)
        {
            VoiceMod.Ctx?.Log.Warning($"Voice: {e.Message}");
            return false;
        }
    }

    public static bool Swap(ref AudioClip clip, string where)
    {
        float dummy = 1f;
        return Swap(ref clip, ref dummy, where);
    }

    /// <summary>鳴った音の名前を RusK\voices\_played.txt に書く (同じ名前は 1 回だけ)。置き換えるファイルの名前を探す用</summary>
    private static void Log(string name, string where)
    {
        if (string.IsNullOrEmpty(name) || name.StartsWith("RusK:") || !Logged.Add(name)) return;
        try
        {
            File.AppendAllText(Path.Combine(Bank.Folder, "_played.txt"), $"{name}\t{where}\n");
        }
        catch { }
    }

    public static void ResetLog() => Logged.Clear();
}

[HarmonyPatch(typeof(AudioPlayer), nameof(AudioPlayer.CreateSFX),
    new[] { typeof(AudioClip), typeof(float), typeof(Transform), typeof(float), typeof(AudioGroupType) })]
internal static class CreateSfxOnTransformPatch
{
    private static void Prefix(ref AudioClip clip, ref float volume, AudioGroupType group) =>
        VoiceSwap.Swap(ref clip, ref volume, group.ToString());
}

[HarmonyPatch(typeof(AudioPlayer), nameof(AudioPlayer.CreateSFX),
    new[] { typeof(AudioClip), typeof(float), typeof(Vector3), typeof(float), typeof(AudioGroupType) })]
internal static class CreateSfxAtPositionPatch
{
    private static void Prefix(ref AudioClip clip, ref float volume, AudioGroupType group) =>
        VoiceSwap.Swap(ref clip, ref volume, group.ToString());
}

[HarmonyPatch(typeof(AudioPlayer), nameof(AudioPlayer.PlayPersistentVoice))]
internal static class PersistentVoicePatch
{
    // 差し替えたら、ゲームの言語の選び直し (中国語 → 日本語の音声) はしない
    private static void Prefix(ref AudioClip clip, ref float volume, ref bool resolveLanguage)
    {
        if (VoiceSwap.Swap(ref clip, ref volume, "PersistentVoice")) resolveLanguage = false;
    }
}

[HarmonyPatch(typeof(AudioPlayer), nameof(AudioPlayer.PlayUICharacterVoice))]
internal static class UiCharacterVoicePatch
{
    private static void Prefix(ref AudioClip clip, ref float volume) =>
        VoiceSwap.Swap(ref clip, ref volume, "UICharacterVoice");
}

[HarmonyPatch(typeof(AudioPlayer), nameof(AudioPlayer.ResolveVoiceClip))]
internal static class ResolveVoicePatch
{
    // 言語を選び直した後の音声 (日本語の音声など) の名前でも差し替える
    private static void Postfix(ref AudioClip __result) => VoiceSwap.Swap(ref __result, "Voice");
}
