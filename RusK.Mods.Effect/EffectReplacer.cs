using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace RusK.Mods.Effect;

/// <summary>
/// エフェクトを、ゲームの別のエフェクトに差し替える。
/// 元のエフェクトは表示だけ消して (EffectApplier の Hide)、差し替え先を元のエフェクトの子として同じ場所に出す
/// (元と一緒に動き、元がプールに戻されて非表示になると一緒に消える)。
/// 元は使い回されるので、差し替え先も元 1 つにつき 1 つ作って使い回し、出るたびに頭から再生する
/// </summary>
internal static class EffectReplacer
{
    private sealed class Slot
    {
        public string Name;
        public GameObject Clone;
    }

    // 元のエフェクトのインスタンス ID → 差し替え先
    private static readonly Dictionary<int, Slot> Slots = new();
    private static Dictionary<string, GameObject> _prefabs;

    /// <summary>ゲームのエフェクトの名前の一覧 (Resources/VFX のプレハブ。差し替え先を選ぶ用)</summary>
    public static IReadOnlyList<string> Names
    {
        get
        {
            LoadPrefabs();
            return _names;
        }
    }

    private static List<string> _names = new();

    private static void LoadPrefabs()
    {
        if (_prefabs != null) return;
        _prefabs = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var o in Resources.LoadAll("VFX", Il2CppType.Of<GameObject>()))
            {
                var go = o.TryCast<GameObject>();
                // パーティクルの無いもの (メッシュだけの部品) は差し替え先にしない
                if (go == null || _prefabs.ContainsKey(go.name) || go.GetComponentsInChildren<ParticleSystem>(true).Length == 0) continue;
                _prefabs[go.name] = go;
            }
        }
        catch (Exception e)
        {
            EffectMod.Ctx?.Log.Warning($"Effect: エフェクトの一覧を読めません: {e.Message}");
        }
        _names = new List<string>(_prefabs.Keys);
        _names.Sort(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>エフェクトが出たときに呼ぶ。rule に差し替え先があれば出し、無ければ前に出したものを消す</summary>
    public static void Handle(GameObject root, EffectRule rule)
    {
        int id = root.GetInstanceID();
        var name = rule?.Replace;
        Slots.TryGetValue(id, out var slot);

        if (string.IsNullOrEmpty(name))
        {
            if (slot?.Clone != null) slot.Clone.SetActive(false);
            return;
        }

        if (slot == null || slot.Name != name || slot.Clone == null)
        {
            if (slot?.Clone != null) UnityEngine.Object.Destroy(slot.Clone);
            LoadPrefabs();
            if (!_prefabs.TryGetValue(name, out var prefab))
            {
                Slots.Remove(id);
                return;
            }
            var clone = UnityEngine.Object.Instantiate(prefab, root.transform);
            clone.name = "RusK:" + name;
            clone.transform.localPosition = Vector3.zero;
            clone.transform.localRotation = Quaternion.identity;
            clone.transform.localScale = Vector3.one;
            // ゲームのスクリプト (時間で隠す・プールに戻すなど) は止める。見た目 (パーティクル) だけ使う
            foreach (var mb in clone.GetComponentsInChildren<MonoBehaviour>(true)) mb.enabled = false;
            Slots[id] = slot = new Slot { Name = name, Clone = clone };
        }

        // 差し替え先にも色・大きさをかける (隠す・差し替えは除く)
        var look = rule.Clone();
        look.Hide = false;
        look.Replace = null;
        EffectApplier.Apply(slot.Clone, look);

        slot.Clone.SetActive(true);
        foreach (var ps in slot.Clone.GetComponentsInChildren<ParticleSystem>(true))
        {
            ps.Clear(false);
            ps.Play(false);
        }
    }
}
