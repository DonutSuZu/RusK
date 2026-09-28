using System;
using System.Collections.Generic;

namespace RusK.API;

/// <summary>
/// Mod 同士で値をやり取りする共有の置き場。名前 (例 "camera.hideBody") を付けて、どの Mod からも書いたり読んだりできる。
/// 相手の Mod が入っていなくても動くように、読む側は既定値を渡す。名前は「Mod の ID.内容」の形にしておくと衝突しにくい。
///
/// 使われている名前:
///   camera.hideBody (bool) … Camera View が一人称で自分の体を隠している (Custom Model が VRM も隠す)
/// </summary>
public static class RuskShared
{
    private static readonly Dictionary<string, object> Values = new();

    /// <summary>値が変わったときに呼ばれる (名前, 新しい値)</summary>
    public static event Action<string, object> Changed;

    public static void Set(string key, object value)
    {
        if (Values.TryGetValue(key, out var old) && Equals(old, value)) return;
        Values[key] = value;
        try { Changed?.Invoke(key, value); }
        catch { }
    }

    public static T Get<T>(string key, T defaultValue = default) =>
        Values.TryGetValue(key, out var v) && v is T t ? t : defaultValue;

    public static bool Has(string key) => Values.ContainsKey(key);

    public static void Remove(string key)
    {
        if (Values.Remove(key))
        {
            try { Changed?.Invoke(key, null); }
            catch { }
        }
    }
}
