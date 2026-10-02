using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RusK.Mods.Voice;

/// <summary>
/// 新しいキャラ (Custom Character) 専用の声。新しいキャラは土台のキャラの声の名前 (例 LightAttackVoice_1006_1_JP) で鳴るので、
/// RusK\voices\〈新しいキャラの番号〉\ に置いた声は、そのキャラが場にいて、土台のキャラ本人がいないときだけ使う
/// (土台のキャラの声は変えない)。キャラの定義 (番号と土台) は RusK\characters\*\character.json から読む
/// </summary>
internal static class CharacterVoices
{
    /// <summary>土台のキャラの番号 → 新しいキャラの番号</summary>
    private static Dictionary<long, List<long>> _byBase;
    private static readonly HashSet<long> Present = new();
    private static float _nextScan = -1f;

    public static void Load(string voicesFolder)
    {
        _byBase = new Dictionary<long, List<long>>();
        try
        {
            var dir = Path.GetFullPath(Path.Combine(voicesFolder, "..", "characters"));
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.GetFiles(dir, "character.json", SearchOption.AllDirectories))
            {
                try
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(file));
                    var root = doc.RootElement;
                    long id = root.GetProperty("id").GetInt64(), bs = root.GetProperty("base").GetInt64();
                    if (!_byBase.TryGetValue(bs, out var list)) _byBase[bs] = list = new List<long>();
                    list.Add(id);
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>この声を、どの新しいキャラ専用の声で置き換えるか (無ければ -1)</summary>
    public static long For(string clipName)
    {
        if (_byBase == null || _byBase.Count == 0 || string.IsNullOrEmpty(clipName)) return -1;
        Scan();
        foreach (var kv in _byBase)
        {
            var tag = "_" + kv.Key;
            int i = clipName.IndexOf(tag, StringComparison.Ordinal);
            if (i < 0) continue;
            int end = i + tag.Length;
            if (end < clipName.Length && clipName[end] != '_') continue; // _1006 の後ろが数字なら別の番号
            if (Present.Contains(kv.Key)) continue;                      // 土台のキャラ本人がいる
            foreach (var id in kv.Value)
                if (Present.Contains(id)) return id;
        }
        return -1;
    }

    /// <summary>場にいるキャラ (操作キャラ・パーティ) の番号。1 秒ごとに数え直す</summary>
    private static void Scan()
    {
        if (Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + 1f;
        Present.Clear();
        try
        {
            foreach (var p in Object.FindObjectsOfType<PlayerController>())
                if (p != null && p.gameObject.activeInHierarchy)
                    Present.Add((long)Math.Round(p.GetPlayerId()));
        }
        catch { }
    }
}
