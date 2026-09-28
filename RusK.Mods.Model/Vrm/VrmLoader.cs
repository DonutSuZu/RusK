using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace RusK.Mods.Model.Vrm;

/// <summary>読み込んだ VRM モデル</summary>
internal sealed class VrmModel
{
    public GameObject Root;
    public string Title;
    /// <summary>VRM のバージョン (0 = VRM 0.x, 1 = VRM 1.0)</summary>
    public int Version;
    /// <summary>人型の骨 (Unity の HumanBodyBones → このモデルの骨)</summary>
    public readonly Dictionary<HumanBodyBones, Transform> Human = new();
    public readonly List<Renderer> Renderers = new();
    public readonly List<Object> Assets = new(); // 作ったメッシュ・材質・テクスチャ (破棄用)
    /// <summary>glTF のノード番号 → 骨</summary>
    public Transform[] Nodes;
    /// <summary>glTF のメッシュ番号 → そのメッシュを描く SkinnedMeshRenderer (表情のため)</summary>
    public readonly Dictionary<int, List<SkinnedMeshRenderer>> MeshRenderers = new();
    /// <summary>glTF のノード番号 → そのノードのメッシュを描く SkinnedMeshRenderer (VRM 1.0 の表情のため)</summary>
    public readonly Dictionary<int, SkinnedMeshRenderer> NodeRenderers = new();
    /// <summary>揺れ物</summary>
    public SpringBoneSystem Springs;
    /// <summary>表情</summary>
    public Expressions Expressions;
    /// <summary>スカートを脚の動きに合わせて開く</summary>
    public SkirtFollow Skirt;

    /// <summary>透明部分を切り抜く材質 (切り抜きの方式をあとから切り替えるため)</summary>
    public readonly List<MaskMaterial> Masks = new();

    /// <summary>作った材質 (影の濃さをあとから変えるため)</summary>
    public readonly List<Material> Materials = new();
    /// <summary>
    /// 見た目のメッシュ → 影を落とす用のメッシュ (両面表示のために足した裏向きの面を含まない)。
    /// 裏向きの面は法線が逆なので、影の補正で逆に押し出されてキャラ自身に影が落ちる
    /// </summary>
    public readonly Dictionary<IntPtr, Mesh> ShadowMeshes = new();
    /// <summary>元にしたゲームの材質の、落ちる影の濃さ (_ReceiveShadowMappingAmount)</summary>
    public float ReceiveShadowBase = -1f;

    public void Destroy()
    {
        if (Root != null) Object.Destroy(Root);
        foreach (var a in Assets)
            if (a != null) Object.Destroy(a);
        Assets.Clear();
    }
}

/// <summary>
/// VRM (0.x / 1.0) を読み込んで、Unity のモデル (骨・メッシュ・材質) を組み立てる。
///
/// 座標: glTF は右手系、Unity は左手系なので 1 軸を反転する。VRM 0.x はモデルが -Z を向いているので Z を、
/// VRM 1.0 は +Z を向いているので X を反転する (どちらも Unity では +Z を向く)。
/// 材質はゲームのトゥーンシェーダーの材質を元に複製し、テクスチャと色だけ差し替える。
/// </summary>
internal static class VrmLoader
{
    public sealed class Templates
    {
        public Material Opaque;      // 不透明 (体)
        public Material Transparent; // 半透明 (頬の赤みなど)
        public float OutlineWidth = 0f; // 輪郭線の太さ (0 で消す)
    }

    /// <summary>
    /// VRM を読み込む。plainGltf が true なら、VRM ではない普通の glb (武器・装備品など) も読める
    /// (Version = -1。人型の骨・揺れ物・表情は無し)
    /// </summary>
    public static VrmModel Load(string path, Templates templates, Action<string> log, bool plainGltf = false)
    {
        using var glb = Glb.Load(path);
        var json = glb.Json;
        var model = new VrmModel { Title = Path.GetFileNameWithoutExtension(path) };

        // ---- バージョン
        JsonElement ext = default;
        bool hasExt = json.TryGetProperty("extensions", out ext);
        if (hasExt && ext.TryGetProperty("VRMC_vrm", out _)) model.Version = 1;
        else if (hasExt && ext.TryGetProperty("VRM", out _)) model.Version = 0;
        else if (plainGltf) model.Version = -1;
        else throw new InvalidDataException("VRM ではありません (VRM の拡張がない glb)");
        // 普通の glTF は +Z が前 (VRM 1.0 と同じ) なので X を反転する
        bool flipX = model.Version != 0;

        // ---- 骨 (ノード)
        var nodesEl = json.GetProperty("nodes");
        int nodeCount = nodesEl.GetArrayLength();
        var nodes = new Transform[nodeCount];
        var used = new HashSet<string>();
        model.Root = new GameObject("RusK_VRM_" + model.Title);
        for (int i = 0; i < nodeCount; i++)
        {
            var n = nodesEl[i];
            string name = n.TryGetProperty("name", out var ne) ? ne.GetString() : null;
            if (string.IsNullOrEmpty(name)) name = $"node{i}";
            // 人型の Avatar は骨の名前で探すので、名前を重複させない
            string unique = name;
            for (int k = 2; !used.Add(unique); k++) unique = $"{name}_{k}";
            nodes[i] = new GameObject(unique).transform;
        }
        var hasParent = new bool[nodeCount];
        for (int i = 0; i < nodeCount; i++)
        {
            if (!nodesEl[i].TryGetProperty("children", out var ch)) continue;
            foreach (var c in ch.EnumerateArray())
            {
                nodes[c.GetInt32()].SetParent(nodes[i], false);
                hasParent[c.GetInt32()] = true;
            }
        }
        for (int i = 0; i < nodeCount; i++)
        {
            if (!hasParent[i]) nodes[i].SetParent(model.Root.transform, false);
            SetLocal(nodes[i], nodesEl[i], flipX);
        }

        // ---- テクスチャ・材質
        var textures = new Dictionary<int, Texture2D>();
        var materials = new List<Material>();
        var alphaCache = new Dictionary<IntPtr, Texture2D>();
        var alphaBytes = new Dictionary<IntPtr, (byte[] a, int w, int h)>();
        var masksByMaterial = new Dictionary<int, MaskMaterial>();
        // 両面表示の材質 (裏向きの面をメッシュに足す)
        var doubleSidedMats = json.TryGetProperty("materials", out var dsEl)
            ? dsEl.EnumerateArray().Select(m => m.TryGetProperty("doubleSided", out var ds) && ds.GetBoolean()).ToArray()
            : Array.Empty<bool>();
        // VRM 0.x の MToon の設定 (材質と同じ順番)。影の付き方を合わせるのに使う
        JsonElement mtoon0 = default;
        bool hasMtoon0 = model.Version == 0 && ext.GetProperty("VRM").TryGetProperty("materialProperties", out mtoon0);
        if (json.TryGetProperty("materials", out var matsEl))
            foreach (var m in matsEl.EnumerateArray())
            {
                int before = model.Masks.Count;
                float? shadeShift = null;
                if (hasMtoon0 && materials.Count < mtoon0.GetArrayLength()
                    && mtoon0[materials.Count].TryGetProperty("floatProperties", out var fp)
                    && fp.TryGetProperty("_ShadeShift", out var ss))
                    shadeShift = ss.GetSingle();
                else if (m.TryGetProperty("extensions", out var me1) && me1.TryGetProperty("VRMC_materials_mtoon", out var mt1)
                         && mt1.TryGetProperty("shadingShiftFactor", out var sf))
                    shadeShift = sf.GetSingle();
                materials.Add(MakeMaterial(glb, m, templates, textures, alphaCache, alphaBytes, model));
                model.Materials.Add(materials[materials.Count - 1]);
                // MToon で「影をほぼ付けない」設定 (VRoid の顔など) は、光の向きによる陰影を付けない
                if (shadeShift is float sh && sh <= -0.9f) SetFloat(materials[materials.Count - 1], "_MainLightIgnoreCelShade", 1f);
                if (model.Masks.Count > before) masksByMaterial[materials.Count - 1] = model.Masks[model.Masks.Count - 1];
            }

        // ---- メッシュ
        var meshCache = new Dictionary<int, (Mesh mesh, int[] matIdx)>();
        for (int i = 0; i < nodeCount; i++)
        {
            var n = nodesEl[i];
            if (!n.TryGetProperty("mesh", out var meshEl)) continue;
            int meshIndex = meshEl.GetInt32();
            if (!meshCache.TryGetValue(meshIndex, out var built))
            {
                built = BuildMesh(glb, meshIndex, flipX, (mat, uv, tris) =>
                {
                    if (masksByMaterial.TryGetValue(mat, out var mm)) CheckTransparency(mm, uv, tris);
                }, mat => mat >= 0 && mat < doubleSidedMats.Length && doubleSidedMats[mat], out var shadowMesh);
                if (shadowMesh != null)
                {
                    model.Assets.Add(shadowMesh);
                    model.ShadowMeshes[built.mesh.Pointer] = shadowMesh;
                }
                model.Assets.Add(built.mesh);
                meshCache[meshIndex] = built;
            }

            var mats = built.matIdx.Select(k => k >= 0 && k < materials.Count ? materials[k] : templates.Opaque).ToArray();
            if (n.TryGetProperty("skin", out var skinEl))
            {
                // スキンメッシュは骨の空間で頂点が書かれているので、モデルの根元の直下に置く
                var go = new GameObject(nodes[i].name + "_mesh");
                go.transform.SetParent(model.Root.transform, false);
                var smr = go.AddComponent<SkinnedMeshRenderer>();
                ApplySkin(glb, skinEl.GetInt32(), built.mesh, smr, nodes, flipX);
                if (model.ShadowMeshes.TryGetValue(built.mesh.Pointer, out var sm)) sm.bindposes = built.mesh.bindposes;
                smr.sharedMesh = built.mesh;
                smr.sharedMaterials = mats;
                smr.updateWhenOffscreen = true;
                model.Renderers.Add(smr);
                if (!model.MeshRenderers.TryGetValue(meshIndex, out var list)) model.MeshRenderers[meshIndex] = list = new();
                list.Add(smr);
                model.NodeRenderers[i] = smr;
            }
            else
            {
                var mf = nodes[i].gameObject.AddComponent<MeshFilter>();
                mf.sharedMesh = built.mesh;
                var mr = nodes[i].gameObject.AddComponent<MeshRenderer>();
                mr.sharedMaterials = mats;
                model.Renderers.Add(mr);
            }
        }

        // ---- 切り抜きの材質: 使う部分に透明が無ければゲームのシェーダーのまま、あれば切り抜きの方式にする
        int solid = 0;
        foreach (var mm in model.Masks)
        {
            mm.Solid = mm.Texture == null ? mm.Color.a >= mm.Cutoff : mm.AlphaBytes != null && !mm.TransparentHit;
            mm.AlphaBytes = null;
            if (mm.Solid) solid++;
            else MaskModes.Apply(mm, MaskModes.Current);
        }
        if (model.Masks.Count > 0)
            log?.Invoke($"切り抜きの材質 {model.Masks.Count} 個のうち、透明な部分が無くゲームのシェーダーで描くもの {solid} 個");

        // ---- 人型の骨・揺れ物・表情 (VRM だけ)
        model.Nodes = nodes;
        if (model.Version < 0)
        {
            AddPropShadows(model);
            log?.Invoke($"glb '{model.Title}': 骨 {nodeCount}、メッシュ {meshCache.Count}、材質 {materials.Count}、テクスチャ {textures.Count}");
            return model;
        }
        ReadHumanoid(ext, model, nodes);
        try
        {
            model.Springs = SpringBoneSystem.Read(ext, model, flipX);
            int legs = model.Springs.AddLegColliders(model.Human);
            model.Skirt = SkirtFollow.Build(model.Human, model.Springs.Roots);
            log?.Invoke($"脚の当たり判定を足した揺れ物の骨: {legs}、脚に合わせて開くスカートの付け根: {model.Skirt.Count}");
        }
        catch (Exception e) { log?.Invoke($"揺れ物を読めません: {e.Message}"); }
        try { model.Expressions = Expressions.Read(ext, model); }
        catch (Exception e) { log?.Invoke($"表情を読めません: {e.Message}"); }
        log?.Invoke($"VRM {model.Version}.x '{model.Title}': 骨 {nodeCount}、メッシュ {meshCache.Count}、材質 {materials.Count}、" +
                    $"テクスチャ {textures.Count}、人型の骨 {model.Human.Count}、揺れ物の骨 {model.Springs?.JointCount ?? 0}、" +
                    $"表情 {model.Expressions?.Count ?? 0}");
        return model;
    }

    // ------------------------------------------------------------------ 座標の変換

    internal static Vector3 Pos(float x, float y, float z, bool flipX) => flipX ? new Vector3(-x, y, z) : new Vector3(x, y, -z);

    private static Quaternion Rot(float x, float y, float z, float w, bool flipX) =>
        flipX ? new Quaternion(x, -y, -z, w) : new Quaternion(-x, -y, z, w);

    /// <summary>行列の変換: S * M * S (S は反転する軸が -1 の対角行列)</summary>
    private static Matrix4x4 Mat(float[] m, int o, bool flipX)
    {
        var r = new Matrix4x4();
        for (int col = 0; col < 4; col++)
            for (int row = 0; row < 4; row++)
                r[row, col] = m[o + col * 4 + row]; // glTF は列優先
        int axis = flipX ? 0 : 2;
        for (int i = 0; i < 4; i++)
        {
            if (i != axis) { r[axis, i] = -r[axis, i]; r[i, axis] = -r[i, axis]; }
        }
        return r;
    }

    private static void SetLocal(Transform t, JsonElement n, bool flipX)
    {
        if (n.TryGetProperty("matrix", out var mat))
        {
            var m = mat.EnumerateArray().Select(e => e.GetSingle()).ToArray();
            var u = Mat(m, 0, flipX);
            t.localPosition = new Vector3(u.m03, u.m13, u.m23);
            t.localRotation = u.rotation;
            t.localScale = u.lossyScale;
            return;
        }
        if (n.TryGetProperty("translation", out var tr))
            t.localPosition = Pos(tr[0].GetSingle(), tr[1].GetSingle(), tr[2].GetSingle(), flipX);
        if (n.TryGetProperty("rotation", out var ro))
            t.localRotation = Rot(ro[0].GetSingle(), ro[1].GetSingle(), ro[2].GetSingle(), ro[3].GetSingle(), flipX);
        if (n.TryGetProperty("scale", out var sc))
            t.localScale = new Vector3(sc[0].GetSingle(), sc[1].GetSingle(), sc[2].GetSingle());
    }

    // ------------------------------------------------------------------ メッシュ

    /// <summary>
    /// glb (武器・装備品) で両面表示の裏向きの面を足したメッシュは、見た目では影を落とさず、
    /// 裏向きの面の無いメッシュを「影だけ」で重ねる (裏向きの面の影が物自体に落ちて黒くなるのを防ぐ)
    /// </summary>
    private static void AddPropShadows(VrmModel model)
    {
        foreach (var r in model.Renderers.ToList())
        {
            Mesh mesh = null;
            var mf = r.GetComponent<MeshFilter>();
            var skinned = r.TryCast<SkinnedMeshRenderer>();
            if (mf != null) mesh = mf.sharedMesh;
            else if (skinned != null) mesh = skinned.sharedMesh;
            if (mesh == null || !model.ShadowMeshes.TryGetValue(mesh.Pointer, out var shadow)) continue;

            var go = new GameObject(r.gameObject.name + "_shadow");
            go.transform.SetParent(r.transform, false);
            Renderer made;
            if (skinned != null)
            {
                var smr = go.AddComponent<SkinnedMeshRenderer>();
                smr.sharedMesh = shadow;
                smr.bones = skinned.bones;
                smr.rootBone = skinned.rootBone;
                smr.updateWhenOffscreen = true;
                made = smr;
            }
            else
            {
                go.AddComponent<MeshFilter>().sharedMesh = shadow;
                made = go.AddComponent<MeshRenderer>();
            }
            made.sharedMaterials = r.sharedMaterials;
            made.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
            made.receiveShadows = false;
            r.shadowCastingMode = ShadowCastingMode.Off;
        }
    }

    /// <summary>
    /// 切り抜きの材質のテクスチャを、メッシュが使う UV の位置で調べる (頂点・辺の中点・三角形の中の数点)。
    /// 透明 (切り抜きの閾値より下) の画素に当たれば、切り抜きが要る材質
    /// </summary>
    private static void CheckTransparency(MaskMaterial m, List<Vector2> uv, int[] tris)
    {
        if (m.TransparentHit || m.AlphaBytes == null) return;
        int w = m.AlphaWidth, h = m.AlphaHeight;
        byte cut = (byte)Mathf.Clamp(Mathf.CeilToInt(m.Cutoff * 255f), 0, 255);
        bool Transparent(Vector2 p)
        {
            float u = p.x - Mathf.Floor(p.x), v = p.y - Mathf.Floor(p.y);
            int x = Mathf.Clamp((int)(u * w), 0, w - 1), y = Mathf.Clamp((int)(v * h), 0, h - 1);
            return m.AlphaBytes[y * w + x] < cut;
        }
        for (int i = 0; i + 2 < tris.Length; i += 3)
        {
            if (tris[i] >= uv.Count || tris[i + 1] >= uv.Count || tris[i + 2] >= uv.Count) continue;
            Vector2 a = uv[tris[i]], b = uv[tris[i + 1]], c = uv[tris[i + 2]];
            if (Transparent(a) || Transparent(b) || Transparent(c)
                || Transparent((a + b) * 0.5f) || Transparent((b + c) * 0.5f) || Transparent((c + a) * 0.5f)
                || Transparent((a + b + c) / 3f)
                || Transparent((4f * a + b + c) / 6f) || Transparent((a + 4f * b + c) / 6f) || Transparent((a + b + 4f * c) / 6f))
            {
                m.TransparentHit = true;
                return;
            }
        }
    }

    private static (Mesh, int[]) BuildMesh(Glb glb, int meshIndex, bool flipX, Action<int, List<Vector2>, int[]> onSubmesh,
        Func<int, bool> doubleSided, out Mesh shadowMesh)
    {
        var meshEl = glb.Json.GetProperty("meshes")[meshIndex];
        var prims = meshEl.GetProperty("primitives").EnumerateArray().ToList();

        // すべてのプリミティブが同じ頂点データを使っていれば共有し、違えばつなげる
        int Attr(JsonElement p, string key) =>
            p.GetProperty("attributes").TryGetProperty(key, out var a) ? a.GetInt32() : -1;
        bool shared = prims.All(p => Attr(p, "POSITION") == Attr(prims[0], "POSITION"));
        var sources = shared ? new List<JsonElement> { prims[0] } : prims;

        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var weights = new List<BoneWeight>();
        var offsets = new List<int>();
        bool hasNormals = true, hasUv = true, hasSkin = true;

        foreach (var p in sources)
        {
            offsets.Add(verts.Count);
            var pos = glb.ReadFloats(Attr(p, "POSITION"), out _);
            int count = pos.Length / 3;
            for (int i = 0; i < count; i++) verts.Add(Pos(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2], flipX));

            int na = Attr(p, "NORMAL");
            if (na >= 0)
            {
                var nor = glb.ReadFloats(na, out _);
                for (int i = 0; i < count; i++) normals.Add(Pos(nor[i * 3], nor[i * 3 + 1], nor[i * 3 + 2], flipX));
            }
            else hasNormals = false;

            int ua = Attr(p, "TEXCOORD_0");
            if (ua >= 0)
            {
                var uv = glb.ReadFloats(ua, out _);
                for (int i = 0; i < count; i++) uvs.Add(new Vector2(uv[i * 2], 1f - uv[i * 2 + 1])); // glTF は上が 0
            }
            else hasUv = false;

            int ja = Attr(p, "JOINTS_0"), wa = Attr(p, "WEIGHTS_0");
            if (ja >= 0 && wa >= 0)
            {
                var j = glb.ReadInts(ja, out _);
                var w = glb.ReadFloats(wa, out _);
                for (int i = 0; i < count; i++)
                {
                    float sum = w[i * 4] + w[i * 4 + 1] + w[i * 4 + 2] + w[i * 4 + 3];
                    if (sum <= 0f) sum = 1f;
                    weights.Add(new BoneWeight
                    {
                        boneIndex0 = j[i * 4], weight0 = w[i * 4] / sum,
                        boneIndex1 = j[i * 4 + 1], weight1 = w[i * 4 + 1] / sum,
                        boneIndex2 = j[i * 4 + 2], weight2 = w[i * 4 + 2] / sum,
                        boneIndex3 = j[i * 4 + 3], weight3 = w[i * 4 + 3] / sum,
                    });
                }
            }
            else hasSkin = false;
        }

        // サブメッシュ (プリミティブごと)。1 軸反転で裏表が逆になるので、三角形の向きを戻す
        var matIdx = new int[prims.Count];
        var subIdx = new int[prims.Count][];
        for (int s = 0; s < prims.Count; s++)
        {
            var p = prims[s];
            int baseVertex = shared ? 0 : offsets[s];
            int[] idx;
            if (p.TryGetProperty("indices", out var ie)) idx = glb.ReadInts(ie.GetInt32(), out _);
            else
            {
                int count = glb.ReadFloats(Attr(p, "POSITION"), out _).Length / 3;
                idx = Enumerable.Range(0, count).ToArray();
            }
            for (int i = 0; i + 2 < idx.Length; i += 3)
            {
                int a = idx[i] + baseVertex, b = idx[i + 1] + baseVertex, c = idx[i + 2] + baseVertex;
                idx[i] = a; idx[i + 1] = c; idx[i + 2] = b;
            }
            subIdx[s] = idx;
            matIdx[s] = p.TryGetProperty("material", out var me) ? me.GetInt32() : -1;
            if (hasUv) onSubmesh?.Invoke(matIdx[s], uvs, idx);
        }

        // 両面表示の材質は、裏向きの面をメッシュに足す (頂点を複製して法線を反転し、三角形の向きを逆にする)。
        // ゲームのシェーダーは裏面の法線を反転しないので、両面表示のまま描くと帽子のつばの裏などが真っ黒になる
        int originalCount = verts.Count;
        var dupSource = new List<int>();
        var frontIdx = (int[][])subIdx.Clone();
        if (doubleSided != null)
        {
            var map = new Dictionary<int, int>();
            int Dup(int v)
            {
                if (!map.TryGetValue(v, out var d))
                {
                    d = originalCount + dupSource.Count;
                    dupSource.Add(v);
                    map[v] = d;
                }
                return d;
            }
            for (int s = 0; s < prims.Count; s++)
            {
                if (!doubleSided(matIdx[s])) continue;
                var idx = subIdx[s];
                var both = new int[idx.Length * 2];
                Array.Copy(idx, both, idx.Length);
                for (int i = 0; i + 2 < idx.Length; i += 3)
                {
                    both[idx.Length + i] = Dup(idx[i]);
                    both[idx.Length + i + 1] = Dup(idx[i + 2]);
                    both[idx.Length + i + 2] = Dup(idx[i + 1]);
                }
                subIdx[s] = both;
            }
            foreach (int v in dupSource)
            {
                verts.Add(verts[v]);
                if (hasNormals) normals.Add(-normals[v]);
                if (hasUv) uvs.Add(uvs[v]);
                if (hasSkin) weights.Add(weights[v]);
            }
        }

        var mesh = new Mesh { name = meshEl.TryGetProperty("name", out var mn) ? mn.GetString() : $"mesh{meshIndex}" };
        if (verts.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
        mesh.vertices = verts.ToArray();
        if (hasNormals) mesh.normals = normals.ToArray();
        if (hasUv) mesh.uv = uvs.ToArray();
        if (hasSkin) mesh.boneWeights = weights.ToArray();
        mesh.subMeshCount = prims.Count;
        for (int s = 0; s < prims.Count; s++) mesh.SetTriangles(subIdx[s], s);
        if (!hasNormals) mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        AddBlendShapes(glb, meshEl, prims, sources, offsets, originalCount, dupSource, mesh, flipX);

        // 影を落とす用: 裏向きの面を足していないメッシュ (表情は影にはほぼ出ないので付けない)
        shadowMesh = null;
        if (dupSource.Count > 0)
        {
            shadowMesh = new Mesh { name = mesh.name + "_shadow", indexFormat = mesh.indexFormat };
            shadowMesh.vertices = verts.ToArray();
            if (hasNormals) shadowMesh.normals = normals.ToArray();
            if (hasUv) shadowMesh.uv = uvs.ToArray();
            if (hasSkin) shadowMesh.boneWeights = weights.ToArray();
            shadowMesh.subMeshCount = prims.Count;
            for (int s = 0; s < prims.Count; s++) shadowMesh.SetTriangles(frontIdx[s], s);
            if (!hasNormals) shadowMesh.RecalculateNormals();
            shadowMesh.RecalculateBounds();
        }
        return (mesh, matIdx);
    }

    /// <summary>モーフ (表情など) をブレンドシェイプにする</summary>
    private static void AddBlendShapes(Glb glb, JsonElement meshEl, List<JsonElement> prims, List<JsonElement> sources,
        List<int> offsets, int vertexCount, List<int> dupSource, Mesh mesh, bool flipX)
    {
        if (!prims[0].TryGetProperty("targets", out var targets0)) return;
        int targetCount = targets0.GetArrayLength();
        string[] names = null;
        if (meshEl.TryGetProperty("extras", out var ex) && ex.TryGetProperty("targetNames", out var tn))
            names = tn.EnumerateArray().Select(e => e.GetString()).ToArray();
        else if (prims[0].TryGetProperty("extras", out var pex) && pex.TryGetProperty("targetNames", out var ptn))
            names = ptn.EnumerateArray().Select(e => e.GetString()).ToArray();

        var usedNames = new HashSet<string>();
        for (int t = 0; t < targetCount; t++)
        {
            var dv = new Vector3[vertexCount];
            var dn = new Vector3[vertexCount];
            for (int s = 0; s < sources.Count; s++)
            {
                if (!sources[s].TryGetProperty("targets", out var targets) || t >= targets.GetArrayLength()) continue;
                var target = targets[t];
                int off = offsets[s];
                if (target.TryGetProperty("POSITION", out var tp))
                {
                    var d = glb.ReadFloats(tp.GetInt32(), out _);
                    for (int i = 0; i < d.Length / 3; i++) dv[off + i] = Pos(d[i * 3], d[i * 3 + 1], d[i * 3 + 2], flipX);
                }
                if (target.TryGetProperty("NORMAL", out var tnor))
                {
                    var d = glb.ReadFloats(tnor.GetInt32(), out _);
                    for (int i = 0; i < d.Length / 3; i++) dn[off + i] = Pos(d[i * 3], d[i * 3 + 1], d[i * 3 + 2], flipX);
                }
            }
            string name = names != null && t < names.Length && !string.IsNullOrEmpty(names[t]) ? names[t] : $"morph{t}";
            for (int k = 2; !usedNames.Add(name); k++) name = $"{name}_{k}";
            if (dupSource.Count > 0)
            {
                // 裏向きに複製した頂点も同じように動かす (法線の変化は反転)
                Array.Resize(ref dv, vertexCount + dupSource.Count);
                Array.Resize(ref dn, vertexCount + dupSource.Count);
                for (int i = 0; i < dupSource.Count; i++)
                {
                    dv[vertexCount + i] = dv[dupSource[i]];
                    dn[vertexCount + i] = -dn[dupSource[i]];
                }
            }
            mesh.AddBlendShapeFrame(name, 100f, dv, dn, null);
        }
    }

    private static void ApplySkin(Glb glb, int skinIndex, Mesh mesh, SkinnedMeshRenderer smr, Transform[] nodes, bool flipX)
    {
        var skin = glb.Json.GetProperty("skins")[skinIndex];
        var joints = skin.GetProperty("joints").EnumerateArray().Select(e => e.GetInt32()).ToArray();
        var bones = joints.Select(j => nodes[j]).ToArray();
        var bindposes = new Matrix4x4[joints.Length];
        if (skin.TryGetProperty("inverseBindMatrices", out var ibm))
        {
            var m = glb.ReadFloats(ibm.GetInt32(), out _);
            for (int i = 0; i < joints.Length; i++) bindposes[i] = Mat(m, i * 16, flipX);
        }
        else
        {
            for (int i = 0; i < joints.Length; i++) bindposes[i] = bones[i].worldToLocalMatrix;
        }
        mesh.bindposes = bindposes;
        smr.bones = bones;
        smr.rootBone = skin.TryGetProperty("skeleton", out var sk) ? nodes[sk.GetInt32()] : bones.FirstOrDefault();
    }

    // ------------------------------------------------------------------ 材質とテクスチャ

    private static Material MakeMaterial(Glb glb, JsonElement m, Templates templates, Dictionary<int, Texture2D> textures,
        Dictionary<IntPtr, Texture2D> alphaCache, Dictionary<IntPtr, (byte[] a, int w, int h)> alphaBytes, VrmModel model)
    {
        string alpha = m.TryGetProperty("alphaMode", out var am) ? am.GetString() : "OPAQUE";
        var template = alpha == "BLEND" && templates.Transparent != null ? templates.Transparent
            : templates.Opaque;
        var mat = new Material(template) { name = m.TryGetProperty("name", out var n) ? n.GetString() : "vrm" };
        model.Assets.Add(mat);

        Color color = Color.white;
        Texture2D tex = null;
        if (m.TryGetProperty("pbrMetallicRoughness", out var pbr))
        {
            if (pbr.TryGetProperty("baseColorFactor", out var f))
                color = new Color(f[0].GetSingle(), f[1].GetSingle(), f[2].GetSingle(), f[3].GetSingle());
            if (pbr.TryGetProperty("baseColorTexture", out var bt))
                tex = Texture(glb, bt.GetProperty("index").GetInt32(), textures, model);
        }

        // テクスチャが無い材質は白にする (元にしたゲームの材質のテクスチャが残らないように)
        SetTex(mat, "_MainTex", tex != null ? tex : Texture2D.whiteTexture);
        SetTex(mat, "_BaseMap", tex);
        // 色は線形の値なので、ゲームの材質 (ガンマの色として保存) に合わせて変換する
        SetColor(mat, "_BaseColor", color.gamma);
        SetColor(mat, "_Color", color.gamma);

        if (alpha == "MASK")
        {
            float cutoff = m.TryGetProperty("alphaCutoff", out var ac) ? ac.GetSingle() : 0.5f;
            // 切り抜きのやり方はまだ定まっていないので、方式を切り替えられるようにしておく (MaskModes)
            var mask = new MaskMaterial
            {
                Material = mat, Template = template, Texture = tex, AlphaTexture = AlphaMap(tex, alphaCache, alphaBytes, model),
                Color = color, Cutoff = cutoff,
                DoubleSided = m.TryGetProperty("doubleSided", out var mds) && mds.GetBoolean(),
            };
            if (tex != null && alphaBytes.TryGetValue(tex.Pointer, out var ab))
                (mask.AlphaBytes, mask.AlphaWidth, mask.AlphaHeight) = ab;
            // 切り抜きの方式を当てるのは、メッシュを読んで透明な部分を使うか調べてから (Load の中)
            model.Masks.Add(mask);
        }
        // 元にしたゲームの材質の発光 (装飾品の光る部分など) は残さない。glb に発光があればそれを使う
        Texture2D emissiveTex = null;
        if (m.TryGetProperty("emissiveTexture", out var et))
            emissiveTex = Texture(glb, et.GetProperty("index").GetInt32(), textures, model);
        var emissive = Color.black;
        if (m.TryGetProperty("emissiveFactor", out var ef))
            emissive = new Color(ef[0].GetSingle(), ef[1].GetSingle(), ef[2].GetSingle(), 1f);
        else if (emissiveTex != null) emissive = Color.white;
        float strength = 1f;
        if (m.TryGetProperty("extensions", out var mext) && mext.TryGetProperty("KHR_materials_emissive_strength", out var es)
            && es.TryGetProperty("emissiveStrength", out var esv))
            strength = esv.GetSingle();
        SetEmission(mat, emissive.maxColorComponent > 0f ? emissive.gamma * strength : Color.black, emissiveTex);
        // ゲームのシェーダーは発光を _EmissionFactor (25) 倍する。VRM (MToon) の発光は 1 倍が前提なので合わせる
        // (浴衣の柄などがまぶしく光っていた)。glb (Custom Item Model) の発光は今の光り方のまま
        if (model.Version >= 0) SetFloat(mat, "_EmissionFactor", 1f);

        RemoveTemplateMaps(mat);

        // 髪や帽子が落とす影の濃さ (調整できる)
        if (model.ReceiveShadowBase < 0f && template.HasProperty("_ReceiveShadowMappingAmount"))
            model.ReceiveShadowBase = template.GetFloat("_ReceiveShadowMappingAmount");
        if (model.ReceiveShadowBase >= 0f)
            SetFloat(mat, "_ReceiveShadowMappingAmount", model.ReceiveShadowBase * MaskModes.ShadowStrength);

        // 輪郭線 (ゲームの体の材質では元から無効)
        SetFloat(mat, "_OutlineWidth", templates.OutlineWidth);
        if (templates.OutlineWidth <= 0f) SetFloat(mat, "_GroupOutline", 0f);

        // 両面表示の材質も裏面は描かない (裏向きの面をメッシュに足してある)
        SetFloat(mat, "_Cull", 2f);
        return mat;
    }

    /// <summary>
    /// 切り抜き用の白黒のテクスチャ (R = G = B = A = 1 - 元の透明度。白い所を消す)。
    /// ゲームのシェーダーの _CharacterAlphaClipMap は「服に隠れる肌を消す」地図で、白い所が消える
    /// 画像のデータを直接読み書きする (1 画素ずつ Unity を呼ぶと遅いため)
    /// </summary>
    private static Texture2D AlphaMap(Texture2D src, Dictionary<IntPtr, Texture2D> cache,
        Dictionary<IntPtr, (byte[] a, int w, int h)> alphaBytes, VrmModel model)
    {
        if (src == null) return null;
        if (cache.TryGetValue(src.Pointer, out var done)) return done;
        Texture2D result = null;
        try
        {
            int alphaIndex = src.format switch
            {
                TextureFormat.RGBA32 => 3,
                TextureFormat.ARGB32 => 0,
                TextureFormat.BGRA32 => 3,
                _ => -1,
            };
            if (alphaIndex < 0) throw new NotSupportedException($"形式 {src.format}");

            var raw = src.GetRawTextureData();
            int length = raw.Length;
            var bytes = new byte[length];
            // il2cpp の配列は、先頭 (4 ポインタ分のヘッダ) の後ろにデータが並ぶ
            System.Runtime.InteropServices.Marshal.Copy(raw.Pointer + 4 * IntPtr.Size, bytes, 0, length);
            // 一番大きい画像の透明度を、材質が透明な部分を使うかの判定用に取っておく
            int pixels = src.width * src.height;
            if (pixels * 4 <= length)
            {
                var alpha = new byte[pixels];
                for (int i = 0; i < pixels; i++) alpha[i] = bytes[i * 4 + alphaIndex];
                alphaBytes[src.Pointer] = (alpha, src.width, src.height);
            }
            for (int i = 0; i + 3 < length; i += 4)
            {
                byte a = (byte)(255 - bytes[i + alphaIndex]);
                bytes[i] = a; bytes[i + 1] = a; bytes[i + 2] = a; bytes[i + 3] = a;
            }

            result = new Texture2D(src.width, src.height, src.format, src.mipmapCount > 1, true)
            {
                name = src.name + "_alpha",
            };
            result.LoadRawTextureData(bytes);
            result.Apply(false, true);
            model.Assets.Add(result);
        }
        catch (Exception e)
        {
            VrmEnv.Ctx?.Log.Warning($"Model: 透明度のテクスチャを作れません ({src.name}): {e.Message}");
            result = null;
        }
        cache[src.Pointer] = result;
        return result;
    }

    /// <summary>
    /// 発光を設定する (黒なら消す)。ゲームのシェーダーは _UseEmission / _EmissionColor / _EmissionMap。
    /// Custom Item Model の「発光」の設定からも呼ぶ
    /// </summary>
    internal static void SetEmission(Material mat, Color color, Texture tex)
    {
        bool on = color.maxColorComponent > 0f;
        SetFloat(mat, "_UseEmission", on ? 1f : 0f);
        SetColor(mat, "_EmissionColor", color);
        if (mat.HasProperty("_EmissionMap")) mat.SetTexture("_EmissionMap", on ? (tex != null ? tex : Texture2D.whiteTexture) : null);
        if (on)
        {
            mat.EnableKeyword("_USEEMISSION_ON");
            mat.EnableKeyword("_EMISSION");
        }
        else
        {
            mat.DisableKeyword("_USEEMISSION_ON");
            mat.DisableKeyword("_EMISSION");
        }
    }

    private static Texture2D Texture(Glb glb, int index, Dictionary<int, Texture2D> cache, VrmModel model)
    {
        if (cache.TryGetValue(index, out var tex)) return tex;
        tex = null;
        try
        {
            var t = glb.Json.GetProperty("textures")[index];
            int source = t.GetProperty("source").GetInt32();
            var img = glb.Json.GetProperty("images")[source];
            if (img.TryGetProperty("bufferView", out var bv))
            {
                var bytes = glb.ViewBytes(bv.GetInt32());
                tex = new Texture2D(2, 2, TextureFormat.RGBA32, true)
                {
                    name = img.TryGetProperty("name", out var n) ? n.GetString() : $"tex{index}",
                };
                ImageConversion.LoadImage(tex, bytes);
                model.Assets.Add(tex);
            }
        }
        catch { tex = null; }
        cache[index] = tex;
        return tex;
    }

    private static void SetTex(Material m, string prop, Texture tex)
    {
        if (tex != null && m.HasProperty(prop)) m.SetTexture(prop, tex);
    }

    /// <summary>
    /// 元にしたゲームの材質の法線マップ・スペキュラーマップ・高さマップは、そのキャラの UV 用なので外す
    /// (残すと、ゲームのキャラの凹凸が VRM の顔にシワのような模様で出る)。ゲームの材質の設定を写し直したときにも呼ぶ
    /// </summary>
    internal static void RemoveTemplateMaps(Material mat)
    {
        // 輪郭線のパスは止める。両面表示のために足した裏向きの面を、輪郭線のパスが黒く描いてしまう
        // (体の横に黒い縁が出ていた)。VRM の材質は元から輪郭線を使っていない
        mat.SetShaderPassEnabled("Outline", false);

        // キーワード (_GROUPBUMP_ON / _GROUPSPECULAR_ON) は外さない。ゲームには使われているキーワードの組み合わせの
        // シェーダーしか入っていないので、外すと切り抜き (_USEALPHACLIPPING_ON) の入った組み合わせが見つからず、
        // 切り抜きなしのシェーダーが使われてしまう。テクスチャを外し、強さを 0 にして効かなくする
        ClearTex(mat, "_BumpMap");
        SetFloat(mat, "_BumpScale", 0f);
        ClearTex(mat, "_SpecTex");
        SetColor(mat, "_Specular", Color.black);
        SetFloat(mat, "_SpecularScale", 0f);
        SetFloat(mat, "_SpecularMultipler", 0f);
        SetFloat(mat, "_SpecularPBRStrength", 0f);
        ClearTex(mat, "_HeigtMap");
        SetFloat(mat, "_HEIGHTMAPSHADOW", 0f);
        mat.DisableKeyword("_HEIGHTMAPSHADOW");

        // 元のキャラのフェード (カメラが近いときなどに縞模様で消す: _AlphaMap = DitherStripes) と、
        // 元のキャラの体を切り抜く地図 (_CharacterAlphaClipMap) も外す。キャラクター画面・装備画面の元のキャラは
        // フェードの途中の設定のことがあり、それを写すと VRM の顔などが消えていた。値はゲームのキャラの普段の設定に合わせる
        ClearTex(mat, "_AlphaMap");
        SetFloat(mat, "_Cutoff", 0.5f);
        SetFloat(mat, "_ClipInterVal", 1f);
        SetFloat(mat, "_UseAlphaClipping", 0f);
        ClearTex(mat, "_CharacterAlphaClipMap");
        SetFloat(mat, "_UseCharacterAlphaClipMap", 0f);
    }

    private static void ClearTex(Material m, string prop)
    {
        if (m.HasProperty(prop)) m.SetTexture(prop, null);
    }

    private static void SetColor(Material m, string prop, Color c)
    {
        if (m.HasProperty(prop)) m.SetColor(prop, c);
    }

    private static void SetFloat(Material m, string prop, float v)
    {
        if (m.HasProperty(prop)) m.SetFloat(prop, v);
    }

    // ------------------------------------------------------------------ 人型の骨

    private static void ReadHumanoid(JsonElement ext, VrmModel model, Transform[] nodes)
    {
        if (model.Version == 1)
        {
            var bones = ext.GetProperty("VRMC_vrm").GetProperty("humanoid").GetProperty("humanBones");
            foreach (var b in bones.EnumerateObject())
                AddHuman(model, b.Name, b.Value.GetProperty("node").GetInt32(), nodes, vrm1: true);
        }
        else
        {
            var bones = ext.GetProperty("VRM").GetProperty("humanoid").GetProperty("humanBones");
            foreach (var b in bones.EnumerateArray())
                AddHuman(model, b.GetProperty("bone").GetString(), b.GetProperty("node").GetInt32(), nodes, vrm1: false);
        }
    }

    private static void AddHuman(VrmModel model, string vrmName, int node, Transform[] nodes, bool vrm1)
    {
        if (node < 0 || node >= nodes.Length) return;
        var name = vrmName;
        // VRM 1.0 は親指の名前が 1 段ずれている (Metacarpal / Proximal / Distal → Unity の Proximal / Intermediate / Distal)
        if (vrm1 && name.Contains("Thumb"))
            name = name.Replace("ThumbProximal", "ThumbIntermediate").Replace("ThumbMetacarpal", "ThumbProximal");
        if (Enum.TryParse<HumanBodyBones>(name, true, out var bone) && bone != HumanBodyBones.LastBone)
            model.Human[bone] = nodes[node];
    }
}
