using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RusK.API;
using RusK.Mods.Shared;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace RusK.Mods.Model;

/// <summary>
/// D-1: ゲームのキャラ同士で見た目を入れ替える (置き換えの仕組みの確認)。
///
/// 動き (骨格・アニメーション・当たり判定) は今のキャラのまま、体のメッシュだけを別のキャラのものにする。
/// 別のキャラのプレハブを非表示で読み込み、その体の SkinnedMeshRenderer を今のキャラの下に作り直して、
/// 骨を名前で今のキャラの骨格に付け替える。全キャラ 3ds Max の Biped (Bip001 ...) なので名前で対応が取れる。
/// 今のキャラに無い骨 (髪やスカートの揺れ用など) は、元のモデルと同じ位置関係で作り足す (その部分は揺れない)。
/// </summary>
internal static class ModelSwap
{
    private const string HolderName = "RusK_Model";

    /// <summary>キャラごとに、隠した元の体の Renderer</summary>
    private static readonly Dictionary<IntPtr, List<Renderer>> Hidden = new();

    /// <summary>キャラごとに、足りない骨として作った Transform (元に戻すときに消す)</summary>
    private static readonly Dictionary<IntPtr, List<GameObject>> Created = new();

    /// <summary>材質の値を元の体から毎フレーム写す組 (新しい Renderer ← 元の Renderer)</summary>
    private static readonly List<(SkinnedMeshRenderer mine, SkinnedMeshRenderer orig)> Sync = new();

    public static bool IsSwapped(PlayerController p) =>
        p != null && p.transform.Find(HolderName) != null;

    public static void Apply(PlayerController target, MotionManager source)
    {
        var log = ModelLab.Ctx?.Log;
        Restore(target);
        Vrm.VrmSwap.Restore(target);

        GameObject src = null;
        try
        {
            src = ResourceManager.Instance.LoadResouceInactive(source.prefabPath);
            if (src == null) throw new InvalidOperationException($"プレハブを読み込めません: {source.prefabPath}");

            // 今のキャラの骨 (名前 → Transform)。同じ名前が複数あるときは最初のもの
            var bones = new Dictionary<string, Transform>();
            foreach (var t in target.GetComponentsInChildren<Transform>(true))
                if (!bones.ContainsKey(t.name)) bones[t.name] = t;

            var holder = new GameObject(HolderName);
            holder.transform.SetParent(target.transform, false);

            int made = 0, missing = 0;
            var created = new List<GameObject>();
            var cloned = new Dictionary<IntPtr, Transform>(); // 元の骨 → 作った骨
            var mine = new List<SkinnedMeshRenderer>();

            foreach (var r in src.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (IsEquipment(r.transform, src.transform)) continue; // 装備は今のキャラのものを使う

                var go = new GameObject(r.gameObject.name);
                go.transform.SetParent(holder.transform, false);
                var smr = go.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = r.sharedMesh;

                // 材質はこのキャラ専用に複製する (元の体から値を写すため)
                var mats = r.sharedMaterials;
                var copies = new Material[mats.Length];
                for (int m = 0; m < mats.Length; m++) copies[m] = mats[m] != null ? new Material(mats[m]) : null;
                smr.sharedMaterials = copies;

                smr.updateWhenOffscreen = true;
                smr.shadowCastingMode = r.shadowCastingMode;
                smr.receiveShadows = r.receiveShadows;

                var srcBones = r.bones;
                var mapped = new Transform[srcBones.Length];
                for (int i = 0; i < srcBones.Length; i++)
                {
                    mapped[i] = MapBone(srcBones[i], bones, cloned, created, out bool exact);
                    if (!exact) missing++;
                }
                smr.bones = mapped;
                smr.rootBone = MapBone(r.rootBone, bones, cloned, created, out _);
                mine.Add(smr);
                made++;
            }

            // 元の体を隠す (装備・武器・エフェクトは残す)
            var hidden = new List<Renderer>();
            foreach (var r in target.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                if (IsEquipment(r.transform, target.transform) || r.transform.IsChildOf(holder.transform)) continue;
                if (!r.enabled) continue;
                r.enabled = false;
                hidden.Add(r);
            }
            Hidden[target.Pointer] = hidden;
            Created[target.Pointer] = created;

            // 同じ役割 (顔・髪・体) の元の Renderer と組にする
            var origs = hidden.OfType<SkinnedMeshRenderer>().ToList();
            foreach (var m in mine)
            {
                var orig = origs.FirstOrDefault(h => Role(h.name) == Role(m.name)) ?? origs.FirstOrDefault();
                if (orig != null) Sync.Add((m, orig));
            }
            LogMaterials(origs.FirstOrDefault(h => Role(h.name) == "face"));

            log?.Info($"Model: {CharacterNames.Get(target.GetPlayerId())} の見た目を {CharacterNames.Get(source)} に " +
                      $"(メッシュ {made} 個、作り足した骨の束 {created.Count} / 名前が無かった骨の参照 {missing}、" +
                      $"隠した元のメッシュ {hidden.Count} 個)");
        }
        catch (Exception e)
        {
            log?.Error($"Model: 見た目の入れ替えに失敗: {e}");
            Restore(target);
        }
        finally
        {
            if (src != null) Object.Destroy(src);
        }
    }

    public static void Restore(PlayerController target)
    {
        if (target == null) return;
        var holder = target.transform.Find(HolderName);
        if (holder != null) Object.Destroy(holder.gameObject);
        if (Created.TryGetValue(target.Pointer, out var created))
        {
            foreach (var go in created)
                if (go != null) Object.Destroy(go);
            Created.Remove(target.Pointer);
        }
        if (Hidden.TryGetValue(target.Pointer, out var list))
        {
            foreach (var r in list)
                if (r != null) r.enabled = true;
            Hidden.Remove(target.Pointer);
        }
        Sync.RemoveAll(x => x.mine == null || x.mine.transform.IsChildOf(target.transform));
    }

    /// <summary>
    /// 名前で対応する骨。今のキャラに無い骨 (髪やスカートの揺れ用など) は、元のモデルと同じ位置関係で作り足す
    /// (親の骨に付けてしまうと、その骨に付いた頂点が別の場所に引っ張られて伸びるため)
    /// </summary>
    private static Transform MapBone(Transform srcBone, Dictionary<string, Transform> bones,
        Dictionary<IntPtr, Transform> cloned, List<GameObject> created, out bool exact)
    {
        exact = true;
        if (srcBone == null) return null;
        // 名前で付け替えるのは Biped の本体の骨 (Bip001 ...) だけ。スカートや装飾の骨 (BN_ / Bone...) は
        // キャラごとに作りが違い、同じ名前でも別の場所の骨があるので、元のモデルから作り足す
        if (IsCoreBone(srcBone.name) && bones.TryGetValue(srcBone.name, out var found)) return found;
        exact = false;
        if (cloned.TryGetValue(srcBone.Pointer, out var made)) return made;

        var srcParent = srcBone.parent;
        var parent = srcParent != null ? MapBone(srcParent, bones, cloned, created, out _) : null;
        if (parent == null) return bones.TryGetValue("Bip001", out var root) ? root : null;

        var go = new GameObject(srcBone.name);
        var t = go.transform;
        t.SetParent(parent, false);
        t.localPosition = srcBone.localPosition;
        t.localRotation = srcBone.localRotation;
        t.localScale = srcBone.localScale;
        cloned[srcBone.Pointer] = t;
        // 今のキャラの骨の直下に作った骨だけ覚えておけば、消すときに子もまとめて消える
        if (srcParent == null || !cloned.ContainsKey(srcParent.Pointer)) created.Add(go);
        return t;
    }

    private static bool IsCoreBone(string name) => name.StartsWith("Bip001");

    private static string Role(string name)
    {
        var n = name.ToLowerInvariant();
        if (n.Contains("face")) return "face";
        if (n.Contains("hair")) return "hair";
        return "body";
    }

    /// <summary>装備 (WeaponHolder の下) のメッシュか</summary>
    private static bool IsEquipment(Transform t, Transform root)
    {
        for (var c = t; c != null && c != root; c = c.parent)
            if (c.name.StartsWith("WeaponHolder") || c.name.StartsWith("Equip_")) return true;
        return false;
    }

    // ---- 材質の値の同期
    // 顔の陰影などは、ゲームが実行中に材質へ値 (顔の向きなど) を入れて描いていると考えられる。
    // 複製した材質にはそれが届かないので、元の体の材質から Vector 型の値を毎フレーム写す。

    public static void Tick()
    {
        for (int k = Sync.Count - 1; k >= 0; k--)
        {
            var (mine, orig) = Sync[k];
            if (mine == null || orig == null) { Sync.RemoveAt(k); continue; }
            try
            {
                var src = orig.sharedMaterials;
                var dst = mine.sharedMaterials;
                for (int i = 0; i < dst.Length; i++)
                {
                    var d = dst[i];
                    var o = i < src.Length ? src[i] : src.Length > 0 ? src[0] : null;
                    if (d == null || o == null || d.shader != o.shader) continue;
                    CopyVectors(o, d);
                }
            }
            catch { }
        }
    }

    private static void CopyVectors(Material from, Material to)
    {
        var sh = to.shader;
        int n = sh.GetPropertyCount();
        for (int i = 0; i < n; i++)
        {
            if (sh.GetPropertyType(i) != ShaderPropertyType.Vector) continue;
            int id = sh.GetPropertyNameId(i);
            to.SetVector(id, from.GetVector(id));
        }
    }

    /// <summary>調査用: 元の顔の材質の値 (実行中の値) をログに出す</summary>
    private static void LogMaterials(SkinnedMeshRenderer face)
    {
        if (face == null) return;
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Model: 元の顔の材質の値 (実行中)");
            foreach (var m in face.sharedMaterials)
            {
                if (m == null) continue;
                var sh = m.shader;
                sb.AppendLine($"  材質 '{m.name}' キーワード [{string.Join(" ", m.shaderKeywords)}]");
                for (int i = 0; i < sh.GetPropertyCount(); i++)
                {
                    var type = sh.GetPropertyType(i);
                    int id = sh.GetPropertyNameId(i);
                    string v = type switch
                    {
                        ShaderPropertyType.Vector => m.GetVector(id).ToString(),
                        ShaderPropertyType.Color => m.GetColor(id).ToString(),
                        ShaderPropertyType.Float or ShaderPropertyType.Range => m.GetFloat(id).ToString("0.###"),
                        ShaderPropertyType.Texture => m.GetTexture(id)?.name ?? "-",
                        _ => "?",
                    };
                    sb.AppendLine($"    {sh.GetPropertyName(i)} ({type}) = {v}");
                }
            }
            ModelLab.Ctx?.Log.Info(sb.ToString());
        }
        catch (Exception e) { ModelLab.Ctx?.Log.Warning($"Model: 材質の値を読めません: {e.Message}"); }
    }

    /// <summary>見た目にできるキャラ (プレハブのある操作キャラ)</summary>
    public static List<MotionManager> Characters()
    {
        var result = new List<MotionManager>();
        try
        {
            var list = GameUtil.Instance?.GetCharacterContainer()?.characters;
            if (list == null) return result;
            for (int i = 0; i < list.Count; i++)
            {
                var m = list[i];
                if (m != null && m.crtType == CharacterType.Character && !string.IsNullOrEmpty(m.prefabPath)) result.Add(m);
            }
        }
        catch { }
        return result;
    }
}
