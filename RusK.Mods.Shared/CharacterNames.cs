using System;
using System.Collections.Generic;

namespace RusK.Mods.Shared;

/// <summary>
/// キャラの表示名。ゲームのキャラ選択画面と同じ名前 (翻訳キー ActorName_&lt;ID&gt;、ゲームの表示言語に合わせる) を返す。
/// PlayerShow2D.nameEng は内部の仮の名前で、別キャラに同じ名前が入っていることがあるので使わない。
/// 各 Mod のプロジェクトにソースごと取り込んで使う (RusK.Mods.Shared)。
/// </summary>
internal static class CharacterNames
{
    private static readonly Dictionary<long, string> Cache = new();
    private static string _language;
    private static int _checkedFrame = -1;

    public static string Get(double id, string fallback = null)
    {
        CheckLanguage();
        long key = (long)Math.Round(id);
        if (Cache.TryGetValue(key, out var name)) return name;

        name = null;
        try
        {
            var text = GameUtil.GetLocale($"ActorName_{key}");
            if (!string.IsNullOrWhiteSpace(text) && !text.StartsWith("ActorName_")) name = text.Trim();
        }
        catch { }
        name ??= string.IsNullOrWhiteSpace(fallback) ? $"#{key}" : fallback;

        Cache[key] = name;
        return name;
    }

    public static string Get(MotionManager m) => m == null ? "?" : Get(m.id, m.name);

    /// <summary>ゲームの表示言語が変わったら覚えた名前を捨てる</summary>
    private static void CheckLanguage()
    {
        if (_checkedFrame == UnityEngine.Time.frameCount) return;
        _checkedFrame = UnityEngine.Time.frameCount;
        string lang = null;
        try { lang = GameUtil.GetLocale("ActorName_1001"); } catch { }
        if (lang == _language) return;
        _language = lang;
        Cache.Clear();
    }
}
