using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace RusK.Mods.Effect;

/// <summary>
/// エフェクトを、ゲームの別のエフェクトに差し替える。
/// 元のエフェクトは表示だけ消して (EffectApplier の Hide)、差し替え先を元のエフェクトの子として同じ場所に出す
/// (元と一緒に動き、元がプールに戻されて非表示になると一緒に消える)。
/// 元は使い回されるので、差し替え先も元 1 つにつき 1 つ作って使い回し、出るたびに頭から再生する。
/// 差し替え先は、ゲームのエフェクト (Resources/VFX) と、RusK\effects の AssetBundle (*.bundle、EffectKit で作る) の中のプレハブ。
/// 自作のものは名前の前に "custom/" を付けて区別する
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
    private static readonly List<AssetBundle> Bundles = new();

    public const string CustomPrefix = "custom/";

    /// <summary>自作エフェクトのバンドルを置くフォルダ (RusK\effects)</summary>
    public static string Folder;

    /// <summary>読み込んだ自作エフェクトの数</summary>
    public static int CustomCount { get; private set; }

    public static bool IsCustom(string name) => name != null && name.StartsWith(CustomPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>一覧に出す名前 (自作は ★ を付ける)</summary>
    public static string Display(string name) => IsCustom(name) ? "★ " + name.Substring(CustomPrefix.Length) : name;

    /// <summary>差し替え先が今あるか (自作のバンドルを消したときなど、無ければ元のエフェクトを隠さない)</summary>
    public static bool Has(string name)
    {
        LoadPrefabs();
        return name != null && _prefabs.ContainsKey(name);
    }

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
        LoadCustom();
        // 自作を先に
        _names = _prefabs.Keys.OrderBy(n => IsCustom(n) ? 0 : 1).ThenBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>RusK\effects の *.bundle を読み込む。ファイルを掴みっぱなしにしないよう、中身をメモリに読んでから開く (Unity で書き出し直せるように)</summary>
    private static void LoadCustom()
    {
        CustomCount = 0;
        if (string.IsNullOrEmpty(Folder)) return;
        try
        {
            Directory.CreateDirectory(Folder);
            foreach (var path in Directory.EnumerateFiles(Folder, "*.bundle", SearchOption.AllDirectories)
                         .OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    var bundle = AssetBundle.LoadFromMemory(File.ReadAllBytes(path));
                    if (bundle == null)
                    {
                        EffectMod.Ctx?.Log.Warning($"Effect: {Path.GetFileName(path)} を開けません (Unity 2022.3 で書き出したものか、同じものを二重に入れていないか確認してください)");
                        continue;
                    }
                    Bundles.Add(bundle);
                    foreach (var o in bundle.LoadAllAssets(Il2CppType.Of<GameObject>()))
                    {
                        var go = o.TryCast<GameObject>();
                        if (go == null) continue;
                        var key = CustomPrefix + go.name;
                        if (_prefabs.ContainsKey(key))
                        {
                            EffectMod.Ctx?.Log.Warning($"Effect: 同じ名前の自作エフェクトがあります: {go.name} ({Path.GetFileName(path)} の方は使いません)");
                            continue;
                        }
                        _prefabs[key] = go;
                        CustomCount++;
                    }
                }
                catch (Exception e)
                {
                    EffectMod.Ctx?.Log.Warning($"Effect: {Path.GetFileName(path)}: {e.Message}");
                }
            }
        }
        catch (Exception e)
        {
            EffectMod.Ctx?.Log.Warning($"Effect: effects フォルダを読めません: {e.Message}");
        }
        if (CustomCount > 0) EffectMod.Ctx?.Log.Info($"Effect: 自作エフェクト {CustomCount} 個 ({Bundles.Count} バンドル)");
    }

    /// <summary>自作エフェクトを読み込み直す (出していた差し替え先は消して、次に出たときに作り直す)</summary>
    public static void ReloadCustom()
    {
        foreach (var id in Slots.Keys.ToList())
        {
            var slot = Slots[id];
            if (!IsCustom(slot.Name)) continue;
            if (slot.Clone != null) UnityEngine.Object.Destroy(slot.Clone);
            Slots.Remove(id);
        }
        foreach (var b in Bundles)
            try { if (b != null) b.Unload(true); } catch { }
        Bundles.Clear();
        _prefabs = null;
        LoadPrefabs();
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
            if (!_prefabs.TryGetValue(name, out var prefab) || prefab == null)
            {
                // 差し替え先が消えている (場面の切り替えでゲームの元のプレハブが消えたなど)。次に読み直す
                if (prefab == null) _prefabs.Remove(name);
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
