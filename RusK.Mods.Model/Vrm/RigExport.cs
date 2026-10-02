using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace RusK.Mods.Model.Vrm;

/// <summary>
/// ゲームのキャラの骨格 (と体のメッシュ・テクスチャ) を glb に書き出す。Blender でゲームと同じ骨格を見ながら
/// モーションや装備を作るため。
///
/// 姿勢はメッシュを骨に付けたときの姿勢 (バインドポーズ)。メッシュに使われていない骨は今の姿勢のまま。
/// 座標: Unity (左手系) → glTF (右手系) は X を反転する (キャラは +Z を向いたまま)。三角形の向きも逆にする。
/// 骨の名前とつながり (Bip001/Bip001 Pelvis/...) はゲームのまま (アニメーションは名前で骨を探すため)
/// </summary>
internal static class RigExport
{
    private static Vector3 P(Vector3 v) => new(-v.x, v.y, v.z);
    private static Quaternion Q(Quaternion q) => new(q.x, -q.y, -q.z, q.w);

    /// <summary>X を反転した空間の行列 (S M S)</summary>
    private static Matrix4x4 M(Matrix4x4 m)
    {
        for (int i = 0; i < 4; i++)
        {
            if (i == 0) continue;
            m[0, i] = -m[0, i];
            m[i, 0] = -m[i, 0];
        }
        return m;
    }

    /// <summary>一緒に書き出す動作 (ゲームのアニメーションのクリップ)。Extras は glTF の animation.extras に入れる</summary>
    public sealed class Anim
    {
        public string Name;
        public AnimationClip Clip;
        public Dictionary<string, object> Extras;
    }

    /// <param name="anims">一緒に書き出す動作 (無ければ骨格だけ)</param>
    /// <param name="animRoot">クリップを当てはめる物 (Animator の付いた物。クリップの骨の道筋はここから数える)</param>
    /// <summary>キャラの下に無いが、骨に付けて書き出したい固いメッシュ (しまってある武器など)。BoneName の骨に、Local の位置関係で付ける</summary>
    public sealed class Attach
    {
        public MeshRenderer Renderer;
        public string BoneName;
        public Matrix4x4 Local; // メッシュ → 骨
    }

    public static string Export(Transform character, IList<SkinnedMeshRenderer> body, string file, Action<string> log,
        IList<Anim> anims = null, GameObject animRoot = null, IList<Attach> attach = null)
    {
        // ---- 基準の姿勢 (根元の空間)
        var rootW2L = character.worldToLocalMatrix;
        var bind = new Dictionary<IntPtr, Matrix4x4>();
        foreach (var smr in body)
        {
            var bones = smr.bones;
            var poses = smr.sharedMesh?.bindposes;
            if (bones == null || poses == null) continue;
            var smrToRoot = rootW2L * smr.transform.localToWorldMatrix;
            for (int i = 0; i < bones.Length && i < poses.Length; i++)
                if (bones[i] != null && !bind.ContainsKey(bones[i].Pointer))
                    bind[bones[i].Pointer] = smrToRoot * poses[i].inverse;
        }

        // 書き出す骨: 根元の下の全部 (描画の部品の付いた物・装備は除く)
        var nodes = new List<Transform>();
        var index = new Dictionary<IntPtr, int>();
        void Collect(Transform t)
        {
            if (t != character)
            {
                if (t.name.StartsWith("RusK_") || t.name.StartsWith("Equip_")) return;
                index[t.Pointer] = nodes.Count;
                nodes.Add(t);
            }
            for (int i = 0; i < t.childCount; i++) Collect(t.GetChild(i));
        }
        Collect(character);

        var rest = new Matrix4x4[nodes.Count];
        for (int i = 0; i < nodes.Count; i++)
        {
            var t = nodes[i];
            var parentRest = t.parent != character && t.parent != null && index.TryGetValue(t.parent.Pointer, out var pi)
                ? rest[pi] : Matrix4x4.identity;
            rest[i] = bind.TryGetValue(t.Pointer, out var b) ? b : parentRest * Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale);
        }

        var w = new GlbWriter();
        var jsonNodes = new List<Dictionary<string, object>>();
        var rootChildren = new List<int>();
        for (int i = 0; i < nodes.Count; i++)
        {
            var t = nodes[i];
            bool hasParent = t.parent != character && t.parent != null && index.ContainsKey(t.parent.Pointer);
            var parentRest = hasParent ? rest[index[t.parent.Pointer]] : Matrix4x4.identity;
            var local = parentRest.inverse * rest[i];
            var pos = P(new Vector3(local.m03, local.m13, local.m23));
            var rot = Q(local.rotation);
            var node = new Dictionary<string, object>
            {
                ["name"] = t.name,
                ["translation"] = new[] { pos.x, pos.y, pos.z },
                ["rotation"] = new[] { rot.x, rot.y, rot.z, rot.w },
            };
            var children = new List<int>();
            for (int c = 0; c < t.childCount; c++)
                if (index.TryGetValue(t.GetChild(c).Pointer, out var ci)) children.Add(ci);
            if (children.Count > 0) node["children"] = children;
            jsonNodes.Add(node);
            if (!hasParent) rootChildren.Add(i);
        }

        // ---- メッシュ (読めるものだけ)
        var meshes = new List<object>();
        var skins = new List<object>();
        var materials = new List<object>();
        var textures = new List<object>();
        var images = new List<object>();
        var texCache = new Dictionary<IntPtr, int>();
        int meshCount = 0, skipped = 0;
        foreach (var smr in body)
        {
            var mesh = smr.sharedMesh;
            if (mesh == null) continue;
            MeshData md;
            try { md = MeshData.Read(mesh); }
            catch (Exception e) { skipped++; log?.Invoke($"メッシュ {mesh.name} を読めないので骨だけ書き出します: {e.Message}"); continue; }

            var toRoot = rootW2L * smr.transform.localToWorldMatrix;
            var verts = md.Vertices;
            var normals = md.Normals;
            var uv = md.Uv;
            var weights = md.Weights;
            var bones = smr.bones;
            int n = verts.Length;

            var pos = new float[n * 3];
            var nor = normals.Length == n ? new float[n * 3] : null;
            for (int i = 0; i < n; i++)
            {
                var v = P(toRoot.MultiplyPoint3x4(verts[i]));
                pos[i * 3] = v.x; pos[i * 3 + 1] = v.y; pos[i * 3 + 2] = v.z;
                if (nor != null)
                {
                    var nn = P(toRoot.MultiplyVector(normals[i]).normalized);
                    nor[i * 3] = nn.x; nor[i * 3 + 1] = nn.y; nor[i * 3 + 2] = nn.z;
                }
            }
            var attrs = new Dictionary<string, object> { ["POSITION"] = w.Floats(pos, 3, true) };
            if (nor != null) attrs["NORMAL"] = w.Floats(nor, 3, false);
            if (uv.Length == n)
            {
                var tc = new float[n * 2];
                for (int i = 0; i < n; i++) { tc[i * 2] = uv[i].x; tc[i * 2 + 1] = 1f - uv[i].y; }
                attrs["TEXCOORD_0"] = w.Floats(tc, 2, false);
            }

            // スキン: 骨の番号はこのメッシュの bones の並び
            bool skinned = weights.Length == n && bones != null && bones.Length > 0 && bones.All(b => b != null && index.ContainsKey(b.Pointer));
            if (skinned)
            {
                var joints = new ushort[n * 4];
                var wts = new float[n * 4];
                for (int i = 0; i < n; i++)
                {
                    var bw = weights[i];
                    joints[i * 4] = (ushort)bw.boneIndex0; joints[i * 4 + 1] = (ushort)bw.boneIndex1;
                    joints[i * 4 + 2] = (ushort)bw.boneIndex2; joints[i * 4 + 3] = (ushort)bw.boneIndex3;
                    wts[i * 4] = bw.weight0; wts[i * 4 + 1] = bw.weight1; wts[i * 4 + 2] = bw.weight2; wts[i * 4 + 3] = bw.weight3;
                }
                attrs["JOINTS_0"] = w.UShorts(joints, 4);
                attrs["WEIGHTS_0"] = w.Floats(wts, 4, false);
                var ibm = new float[bones.Length * 16];
                for (int j = 0; j < bones.Length; j++)
                {
                    var inv = M(rest[index[bones[j].Pointer]].inverse);
                    for (int c = 0; c < 4; c++)
                        for (int r = 0; r < 4; r++) ibm[j * 16 + c * 4 + r] = inv[r, c]; // 列優先
                }
                skins.Add(new Dictionary<string, object>
                {
                    ["joints"] = bones.Select(b => index[b.Pointer]).ToArray(),
                    ["inverseBindMatrices"] = w.Floats(ibm, 16, false, "MAT4"),
                });
            }

            var prims = new List<object>();
            var mats = smr.sharedMaterials;
            for (int s = 0; s < md.Triangles.Length; s++)
            {
                var tris = md.Triangles[s];
                for (int i = 0; i + 2 < tris.Length; i += 3) (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]); // X の反転で裏表が逆になる
                var prim = new Dictionary<string, object> { ["attributes"] = attrs, ["indices"] = w.UInts(tris) };
                var mat = s < mats.Length ? mats[s] : null;
                prim["material"] = Material(mat, materials, textures, images, texCache, w);
                prims.Add(prim);
            }
            meshes.Add(new Dictionary<string, object> { ["name"] = mesh.name, ["primitives"] = prims });

            var meshNode = new Dictionary<string, object> { ["name"] = smr.name + "_mesh", ["mesh"] = meshes.Count - 1 };
            if (skinned) meshNode["skin"] = skins.Count - 1;
            jsonNodes.Add(meshNode);
            rootChildren.Add(jsonNodes.Count - 1);
            meshCount++;
        }

        // ---- 武器など、骨に付いた固いメッシュ (MeshRenderer)。一番近い骨 1 本だけに付ける (Blender で刃の向きを見ながら動きを作るため)
        var rigid = new List<(MeshRenderer mr, Transform bone, Matrix4x4 local)>();
        foreach (var mr in character.GetComponentsInChildren<MeshRenderer>(true)) // しまっている武器 (非表示) も
        {
            var bone = mr.transform.parent;
            while (bone != null && bone != character && !index.ContainsKey(bone.Pointer)) bone = bone.parent;
            if (bone == null || bone == character) continue;
            rigid.Add((mr, bone, bone.worldToLocalMatrix * mr.transform.localToWorldMatrix));
        }
        if (attach != null)
            foreach (var a in attach)
            {
                var bone = nodes.FirstOrDefault(t => t.name == a.BoneName);
                if (a.Renderer != null && bone != null) rigid.Add((a.Renderer, bone, a.Local));
            }
        foreach (var (mr, bone, local) in rigid)
        {
            var mesh = mr.GetComponent<MeshFilter>()?.sharedMesh;
            if (mesh == null) continue;
            MeshData md;
            try { md = MeshData.Read(mesh); }
            catch (Exception e) { skipped++; log?.Invoke($"メッシュ {mesh.name} を読めません: {e.Message}"); continue; }
            int bi = index[bone.Pointer];
            // 骨の基準の姿勢に、骨との位置関係のまま付ける
            var toRoot = rest[bi] * local;
            int n = md.Vertices.Length;
            var pos = new float[n * 3];
            var nor = md.Normals.Length == n ? new float[n * 3] : null;
            for (int i = 0; i < n; i++)
            {
                var v = P(toRoot.MultiplyPoint3x4(md.Vertices[i]));
                pos[i * 3] = v.x; pos[i * 3 + 1] = v.y; pos[i * 3 + 2] = v.z;
                if (nor != null)
                {
                    var nn = P(toRoot.MultiplyVector(md.Normals[i]).normalized);
                    nor[i * 3] = nn.x; nor[i * 3 + 1] = nn.y; nor[i * 3 + 2] = nn.z;
                }
            }
            var attrs = new Dictionary<string, object> { ["POSITION"] = w.Floats(pos, 3, true) };
            if (nor != null) attrs["NORMAL"] = w.Floats(nor, 3, false);
            if (md.Uv.Length == n)
            {
                var tc = new float[n * 2];
                for (int i = 0; i < n; i++) { tc[i * 2] = md.Uv[i].x; tc[i * 2 + 1] = 1f - md.Uv[i].y; }
                attrs["TEXCOORD_0"] = w.Floats(tc, 2, false);
            }
            // スキンの骨は、一番上の骨からこの骨までの道筋全部にする (この骨だけだと、Blender が体とは別の骨組みにしてしまう)
            var chain = new List<int>();
            for (var c = bone; c != null && index.TryGetValue(c.Pointer, out var ci); c = c.parent) chain.Insert(0, ci);
            var joints = new ushort[n * 4];
            var wts = new float[n * 4];
            for (int i = 0; i < n; i++) { joints[i * 4] = (ushort)(chain.Count - 1); wts[i * 4] = 1f; }
            attrs["JOINTS_0"] = w.UShorts(joints, 4);
            attrs["WEIGHTS_0"] = w.Floats(wts, 4, false);
            var ibm = new float[chain.Count * 16];
            for (int j = 0; j < chain.Count; j++)
            {
                var inv = M(rest[chain[j]].inverse);
                for (int c = 0; c < 4; c++)
                    for (int r = 0; r < 4; r++) ibm[j * 16 + c * 4 + r] = inv[r, c];
            }
            skins.Add(new Dictionary<string, object> { ["joints"] = chain.ToArray(), ["inverseBindMatrices"] = w.Floats(ibm, 16, false, "MAT4") });

            var prims = new List<object>();
            var mats = mr.sharedMaterials;
            for (int sub = 0; sub < md.Triangles.Length; sub++)
            {
                var tris = md.Triangles[sub];
                for (int i = 0; i + 2 < tris.Length; i += 3) (tris[i + 1], tris[i + 2]) = (tris[i + 2], tris[i + 1]);
                var prim = new Dictionary<string, object> { ["attributes"] = attrs, ["indices"] = w.UInts(tris) };
                prim["material"] = Material(sub < mats.Length ? mats[sub] : null, materials, textures, images, texCache, w);
                prims.Add(prim);
            }
            meshes.Add(new Dictionary<string, object> { ["name"] = mesh.name, ["primitives"] = prims });
            jsonNodes.Add(new Dictionary<string, object> { ["name"] = mr.name + "_mesh", ["mesh"] = meshes.Count - 1, ["skin"] = skins.Count - 1 });
            rootChildren.Add(jsonNodes.Count - 1);
            meshCount++;
            log?.Invoke($"固いメッシュ {mr.name} ({mesh.name}) を骨 {bone.name} に付けました");
        }

        // ---- 動作: クリップを 30 コマ/秒でキャラに当てはめて、骨ごとの位置・向き・大きさを記録する (最後に元の姿勢に戻す)
        var animations = new List<object>();
        if (anims != null && anims.Count > 0 && animRoot != null)
            animations = Animations(nodes, anims, animRoot, w, log);

        // 根元 (キャラの名前)
        jsonNodes.Add(new Dictionary<string, object> { ["name"] = character.name, ["children"] = rootChildren });
        var doc = new Dictionary<string, object>
        {
            ["asset"] = new Dictionary<string, object> { ["version"] = "2.0", ["generator"] = "RusK RigExport" },
            ["scene"] = 0,
            ["scenes"] = new[] { new Dictionary<string, object> { ["nodes"] = new[] { jsonNodes.Count - 1 } } },
            ["nodes"] = jsonNodes,
        };
        if (meshes.Count > 0) doc["meshes"] = meshes;
        if (animations.Count > 0) doc["animations"] = animations;
        if (skins.Count > 0) doc["skins"] = skins;
        if (materials.Count > 0) doc["materials"] = materials;
        if (textures.Count > 0)
        {
            doc["textures"] = textures;
            doc["images"] = images;
            doc["samplers"] = new[] { new Dictionary<string, object>() };
        }
        w.Write(file, doc);
        log?.Invoke($"骨格を書き出しました: 骨 {nodes.Count}、メッシュ {meshCount} (読めない {skipped})、テクスチャ {images.Count}、動作 {animations.Count} → {file}");
        return file;
    }

    private static List<object> Animations(List<Transform> nodes, IList<Anim> anims, GameObject animRoot, GlbWriter w, Action<string> log)
    {
        const float fps = 30f;
        var result = new List<object>();
        var saved = nodes.Select(t => (t.localPosition, t.localRotation, t.localScale)).ToArray();
        void Restore()
        {
            for (int i = 0; i < nodes.Count; i++)
            {
                if (nodes[i] == null) continue;
                nodes[i].localPosition = saved[i].localPosition;
                nodes[i].localRotation = saved[i].localRotation;
                nodes[i].localScale = saved[i].localScale;
            }
        }
        try
        {
            foreach (var a in anims)
            {
                if (a?.Clip == null) continue;
                try
                {
                    int frames = Mathf.Max(1, Mathf.CeilToInt(a.Clip.length * fps));
                    var times = new float[frames + 1];
                    var pos = new float[nodes.Count][];
                    var rot = new float[nodes.Count][];
                    var scl = new float[nodes.Count][];
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        pos[i] = new float[(frames + 1) * 3];
                        rot[i] = new float[(frames + 1) * 4];
                        scl[i] = new float[(frames + 1) * 3];
                    }
                    for (int f = 0; f <= frames; f++)
                    {
                        float t = Mathf.Min(f / fps, a.Clip.length);
                        times[f] = t;
                        // クリップが動かさない骨は元の姿勢のまま (前のコマ・前のクリップの姿勢が残らないように、毎回戻してから当てはめる)
                        Restore();
                        a.Clip.SampleAnimation(animRoot, t);
                        for (int i = 0; i < nodes.Count; i++)
                        {
                            var p = P(nodes[i].localPosition);
                            var q = Q(nodes[i].localRotation);
                            var sc = nodes[i].localScale;
                            pos[i][f * 3] = p.x; pos[i][f * 3 + 1] = p.y; pos[i][f * 3 + 2] = p.z;
                            rot[i][f * 4] = q.x; rot[i][f * 4 + 1] = q.y; rot[i][f * 4 + 2] = q.z; rot[i][f * 4 + 3] = q.w;
                            scl[i][f * 3] = sc.x; scl[i][f * 3 + 1] = sc.y; scl[i][f * 3 + 2] = sc.z;
                        }
                    }
                    int input = w.Floats(times, 1, true);
                    var samplers = new List<object>();
                    var channels = new List<object>();
                    for (int i = 0; i < nodes.Count; i++)
                    {
                        foreach (var (path, data, comps) in new[] { ("translation", pos[i], 3), ("rotation", rot[i], 4), ("scale", scl[i], 3) })
                        {
                            // 動かない骨の位置・大きさは書かない (ファイルを小さく)。向きは全部書く
                            bool moves = path == "rotation";
                            for (int k = comps; k < data.Length && !moves; k++) moves = Mathf.Abs(data[k] - data[k % comps]) > 1e-5f;
                            if (!moves) continue;
                            samplers.Add(new Dictionary<string, object> { ["input"] = input, ["output"] = w.Floats(data, comps, false), ["interpolation"] = "LINEAR" });
                            channels.Add(new Dictionary<string, object>
                            {
                                ["sampler"] = samplers.Count - 1,
                                ["target"] = new Dictionary<string, object> { ["node"] = i, ["path"] = path },
                            });
                        }
                    }
                    var anim = new Dictionary<string, object> { ["name"] = a.Name, ["samplers"] = samplers, ["channels"] = channels };
                    if (a.Extras != null) anim["extras"] = a.Extras;
                    result.Add(anim);
                }
                catch (Exception e) { log?.Invoke($"動作 {a.Name} を書き出せません: {e.Message}"); }
            }
        }
        finally { Restore(); }
        return result;
    }

    private static int Material(UnityEngine.Material mat, List<object> materials, List<object> textures, List<object> images,
        Dictionary<IntPtr, int> texCache, GlbWriter w)
    {
        var m = new Dictionary<string, object> { ["name"] = mat != null ? mat.name : "material" };
        var pbr = new Dictionary<string, object> { ["metallicFactor"] = 0f, ["roughnessFactor"] = 1f };
        Texture tex = null;
        if (mat != null)
        {
            if (mat.HasProperty("_MainTex")) tex = mat.GetTexture("_MainTex");
            if (tex == null && mat.HasProperty("_BaseMap")) tex = mat.GetTexture("_BaseMap");
        }
        if (tex != null)
        {
            if (!texCache.TryGetValue(tex.Pointer, out var ti))
            {
                ti = -1;
                var png = Png(tex);
                if (png != null)
                {
                    images.Add(new Dictionary<string, object> { ["name"] = tex.name, ["mimeType"] = "image/png", ["bufferView"] = w.View(png) });
                    textures.Add(new Dictionary<string, object> { ["source"] = images.Count - 1, ["sampler"] = 0 });
                    ti = textures.Count - 1;
                }
                texCache[tex.Pointer] = ti;
            }
            if (ti >= 0) pbr["baseColorTexture"] = new Dictionary<string, object> { ["index"] = ti };
        }
        m["pbrMetallicRoughness"] = pbr;
        materials.Add(m);
        return materials.Count - 1;
    }

    /// <summary>テクスチャを PNG にする (読めないテクスチャも、描き写して読む)</summary>
    private static byte[] Png(Texture tex)
    {
        RenderTexture rt = null;
        Texture2D copy = null;
        var prev = RenderTexture.active;
        try
        {
            rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            copy = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            copy.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            copy.Apply();
            return ImageConversion.EncodeToPNG(copy);
        }
        catch { return null; }
        finally
        {
            RenderTexture.active = prev;
            if (rt != null) RenderTexture.ReleaseTemporary(rt);
            if (copy != null) Object.Destroy(copy);
        }
    }
}

/// <summary>
/// メッシュの頂点・面を読む。ゲームのメッシュは読み取り禁止 (isReadable = false) なので、GPU に送られた頂点バッファ・
/// インデックスバッファを読み戻して、頂点の並び (属性の形式とオフセット) に従ってほどく
/// </summary>
internal sealed class MeshData
{
    public Vector3[] Vertices, Normals;
    public Vector2[] Uv;
    public BoneWeight[] Weights;
    public int[][] Triangles;

    public static MeshData Read(Mesh mesh)
    {
        var d = new MeshData();
        int n = mesh.vertexCount;
        var streams = new byte[mesh.vertexBufferCount][];
        for (int st = 0; st < streams.Length; st++) streams[st] = Buffer(mesh.GetVertexBuffer(st));

        float[] Attr(VertexAttribute attr, out int dim)
        {
            dim = 0;
            if (!mesh.HasVertexAttribute(attr)) return null;
            int st = mesh.GetVertexAttributeStream(attr);
            int off = mesh.GetVertexAttributeOffset(attr);
            var fmt = mesh.GetVertexAttributeFormat(attr);
            dim = mesh.GetVertexAttributeDimension(attr);
            int stride = mesh.GetVertexBufferStride(st);
            var b = streams[st];
            var r = new float[n * dim];
            int size = Size(fmt);
            for (int i = 0; i < n; i++)
                for (int c = 0; c < dim; c++)
                    r[i * dim + c] = Value(b, i * stride + off + c * size, fmt);
            return r;
        }

        var pos = Attr(VertexAttribute.Position, out _);
        d.Vertices = new Vector3[n];
        for (int i = 0; i < n; i++) d.Vertices[i] = new Vector3(pos[i * 3], pos[i * 3 + 1], pos[i * 3 + 2]);
        var nor = Attr(VertexAttribute.Normal, out _);
        d.Normals = nor == null ? Array.Empty<Vector3>()
            : Enumerable.Range(0, n).Select(i => new Vector3(nor[i * 3], nor[i * 3 + 1], nor[i * 3 + 2])).ToArray();
        var uv = Attr(VertexAttribute.TexCoord0, out int uvDim);
        d.Uv = uv == null ? Array.Empty<Vector2>()
            : Enumerable.Range(0, n).Select(i => new Vector2(uv[i * uvDim], uv[i * uvDim + 1])).ToArray();

        // ウェイト: 骨の数は 1～4 (BlendWeight が無ければ 1 本の骨に全部)
        var bi = Attr(VertexAttribute.BlendIndices, out int biDim);
        var bw = Attr(VertexAttribute.BlendWeight, out int bwDim);
        if (bi != null)
        {
            d.Weights = new BoneWeight[n];
            for (int i = 0; i < n; i++)
            {
                var idx = new int[4];
                var wt = new float[4];
                for (int c = 0; c < biDim && c < 4; c++) idx[c] = (int)bi[i * biDim + c];
                if (bw == null) wt[0] = 1f;
                else
                {
                    for (int c = 0; c < bwDim && c < 4; c++) wt[c] = bw[i * bwDim + c];
                    // 重みが骨より 1 つ少なく書かれている形式 (最後の重み = 1 - 残りの合計)
                    if (bwDim < biDim && bwDim < 4) wt[bwDim] = Mathf.Max(0f, 1f - wt.Take(bwDim).Sum());
                }
                d.Weights[i] = new BoneWeight
                {
                    boneIndex0 = idx[0], weight0 = wt[0], boneIndex1 = idx[1], weight1 = wt[1],
                    boneIndex2 = idx[2], weight2 = wt[2], boneIndex3 = idx[3], weight3 = wt[3],
                };
            }
        }
        else d.Weights = Array.Empty<BoneWeight>();

        // 面
        var ib = Buffer(mesh.GetIndexBuffer());
        bool u16 = mesh.indexFormat == IndexFormat.UInt16;
        d.Triangles = new int[mesh.subMeshCount][];
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            var sm = mesh.GetSubMesh(s);
            var tris = new int[sm.indexCount];
            for (int i = 0; i < sm.indexCount; i++)
            {
                int k = sm.indexStart + i;
                tris[i] = (u16 ? BitConverter.ToUInt16(ib, k * 2) : (int)BitConverter.ToUInt32(ib, k * 4)) + sm.baseVertex;
            }
            d.Triangles[s] = tris;
        }
        return d;
    }

    private static byte[] Buffer(GraphicsBuffer buf)
    {
        try
        {
            int len = buf.count * buf.stride;
            var data = new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppStructArray<byte>(len);
            // GetData はゲームから削られているので、中の呼び出しを直接使う
            buf.InternalGetData(new Il2CppSystem.Array(data.Pointer), 0, 0, len, 1);
            var r = new byte[len];
            System.Runtime.InteropServices.Marshal.Copy(data.Pointer + 4 * IntPtr.Size, r, 0, len); // il2cpp の配列はヘッダの後ろにデータ
            return r;
        }
        finally { buf.Dispose(); }
    }

    private static int Size(VertexAttributeFormat f) => f switch
    {
        VertexAttributeFormat.Float32 or VertexAttributeFormat.UInt32 or VertexAttributeFormat.SInt32 => 4,
        VertexAttributeFormat.Float16 or VertexAttributeFormat.UNorm16 or VertexAttributeFormat.SNorm16
            or VertexAttributeFormat.UInt16 or VertexAttributeFormat.SInt16 => 2,
        _ => 1,
    };

    private static float Value(byte[] b, int o, VertexAttributeFormat f) => f switch
    {
        VertexAttributeFormat.Float32 => BitConverter.ToSingle(b, o),
        VertexAttributeFormat.Float16 => Mathf.HalfToFloat(BitConverter.ToUInt16(b, o)),
        VertexAttributeFormat.UNorm8 => b[o] / 255f,
        VertexAttributeFormat.SNorm8 => Mathf.Max(-1f, (sbyte)b[o] / 127f),
        VertexAttributeFormat.UNorm16 => BitConverter.ToUInt16(b, o) / 65535f,
        VertexAttributeFormat.SNorm16 => Mathf.Max(-1f, BitConverter.ToInt16(b, o) / 32767f),
        VertexAttributeFormat.UInt8 => b[o],
        VertexAttributeFormat.SInt8 => (sbyte)b[o],
        VertexAttributeFormat.UInt16 => BitConverter.ToUInt16(b, o),
        VertexAttributeFormat.SInt16 => BitConverter.ToInt16(b, o),
        VertexAttributeFormat.UInt32 => BitConverter.ToUInt32(b, o),
        VertexAttributeFormat.SInt32 => BitConverter.ToInt32(b, o),
        _ => 0f,
    };
}

/// <summary>glb を書く (バイナリをまとめて、アクセサーを作る)</summary>
internal sealed class GlbWriter
{
    private readonly MemoryStream _bin = new();
    private readonly List<object> _views = new();
    private readonly List<object> _accessors = new();

    public int View(byte[] data, int? target = null)
    {
        while (_bin.Length % 4 != 0) _bin.WriteByte(0);
        var view = new Dictionary<string, object> { ["buffer"] = 0, ["byteOffset"] = (int)_bin.Length, ["byteLength"] = data.Length };
        if (target != null) view["target"] = target.Value;
        _bin.Write(data, 0, data.Length);
        _views.Add(view);
        return _views.Count - 1;
    }

    public int Floats(float[] values, int components, bool minMax, string type = null)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        var acc = new Dictionary<string, object>
        {
            ["bufferView"] = View(bytes), ["componentType"] = 5126, ["count"] = values.Length / components,
            ["type"] = type ?? (components == 1 ? "SCALAR" : components == 2 ? "VEC2" : components == 3 ? "VEC3" : "VEC4"),
        };
        if (minMax)
        {
            var min = new float[components];
            var max = new float[components];
            for (int c = 0; c < components; c++) { min[c] = float.MaxValue; max[c] = float.MinValue; }
            for (int i = 0; i < values.Length; i++)
            {
                int c = i % components;
                min[c] = Math.Min(min[c], values[i]);
                max[c] = Math.Max(max[c], values[i]);
            }
            acc["min"] = min;
            acc["max"] = max;
        }
        _accessors.Add(acc);
        return _accessors.Count - 1;
    }

    public int UShorts(ushort[] values, int components)
    {
        var bytes = new byte[values.Length * 2];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        _accessors.Add(new Dictionary<string, object>
        {
            ["bufferView"] = View(bytes), ["componentType"] = 5123, ["count"] = values.Length / components,
            ["type"] = components == 4 ? "VEC4" : "SCALAR",
        });
        return _accessors.Count - 1;
    }

    public int UInts(int[] values)
    {
        var bytes = new byte[values.Length * 4];
        Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length);
        _accessors.Add(new Dictionary<string, object>
        {
            ["bufferView"] = View(bytes), ["componentType"] = 5125, ["count"] = values.Length, ["type"] = "SCALAR",
        });
        return _accessors.Count - 1;
    }

    public void Write(string file, Dictionary<string, object> doc)
    {
        while (_bin.Length % 4 != 0) _bin.WriteByte(0);
        doc["buffers"] = new[] { new Dictionary<string, object> { ["byteLength"] = (int)_bin.Length } };
        if (_views.Count > 0) doc["bufferViews"] = _views;
        if (_accessors.Count > 0) doc["accessors"] = _accessors;
        var json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(doc));
        int jsonLen = (json.Length + 3) / 4 * 4;
        var bin = _bin.ToArray();
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var f = new BinaryWriter(File.Create(file));
        f.Write(0x46546C67u); // glTF
        f.Write(2u);
        f.Write((uint)(12 + 8 + jsonLen + 8 + bin.Length));
        f.Write((uint)jsonLen);
        f.Write(0x4E4F534Au); // JSON
        f.Write(json);
        for (int i = json.Length; i < jsonLen; i++) f.Write((byte)' ');
        f.Write((uint)bin.Length);
        f.Write(0x004E4942u); // BIN
        f.Write(bin);
    }
}
