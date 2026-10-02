using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace RusK.Mods.Model.Vrm.Pmx;

/// <summary>
/// PMX (MMD のモデル) を、VRM と同じ形 (VrmModel) に組み立てる。付けるところ (VrmSwap) は VRM と同じ仕組みを使う。
///
/// 座標: PMX も Unity も左手系。MMD のモデルは -Z を向いているので、Y 軸で 180 度回して +Z 向きにする (x, z を反転)。
/// 大きさは 1 = 8cm (初音ミクの身長 約 20 で 1.6m)。
/// 骨は位置だけで向きを持たない (すべて回転なし) ので、読み込み直後の骨の回転はすべて単位回転。
/// MMD のモデルは A ポーズ (腕が斜め下) なので、動きを写すときの基準 (T ポーズ) は、腕を水平に向けた姿勢を別に計算して渡す。
/// 付与 (ほかの骨の回転を写す: 足D・肩C・腕捩1 など) は毎フレーム計算し、物理 (剛体) は VRM の揺れ物に置き換える
/// </summary>
internal static class PmxLoader
{
    /// <summary>VrmModel.Version に入れる値 (PMX の印)</summary>
    public const int PmxVersion = 100;

    private const float Unit = 0.08f;

    private static Vector3 P(Vector3 v) => new Vector3(-v.x, v.y, -v.z) * Unit;
    private static Vector3 Dir(Vector3 v) => new(-v.x, v.y, -v.z);

    /// <param name="prop">小物 (武器など、人の形でないもの): 人型の骨・基準の姿勢・揺れ物を作らない</param>
    public static VrmModel Load(string path, VrmLoader.Templates templates, Action<string> log, bool prop = false)
    {
        var pmx = PmxFile.Load(path);
        var dir = Path.GetDirectoryName(path)!;
        var model = new VrmModel { Title = Path.GetFileNameWithoutExtension(path), Version = PmxVersion };
        model.Root = new GameObject("RusK_PMX_" + model.Title);

        try
        {
            var bones = BuildBones(pmx, model);
            model.Nodes = bones;
            if (!prop) MapHumanoid(pmx, bones, model, log);

            // ---- 材質 (テクスチャは使うものだけ読む)
            var textures = new Dictionary<int, (Texture2D tex, bool alpha)>();
            var alphaCache = new Dictionary<IntPtr, Texture2D>();
            var alphaBytes = new Dictionary<IntPtr, (byte[] a, int w, int h)>();
            var materials = new List<Material>();
            var masks = new Dictionary<int, MaskMaterial>();
            foreach (var m in pmx.Materials)
            {
                var (tex, texAlpha) = LoadTexture(pmx, m.Texture, dir, textures, model, log);
                int before = model.Masks.Count;
                materials.Add(MakeMaterial(m, tex, texAlpha, templates, model, alphaCache, alphaBytes));
                if (model.Masks.Count > before) masks[materials.Count - 1] = model.Masks[model.Masks.Count - 1];
            }

            // ---- メッシュ・表情
            var smr = BuildMesh(pmx, bones, materials, masks, model, out var dupSource);
            VrmLoader.FinishMasks(model, log);
            model.Expressions = BuildExpressions(pmx, smr, dupSource, log);

            if (prop)
            {
                log?.Invoke($"PMX {pmx.Version:0.0} '{model.Title}' ({pmx.Name}) を小物として: 頂点 {pmx.Vertices.Count}、骨 {bones.Length}、材質 {materials.Count}");
                return model;
            }

            // ---- 基準の姿勢・付与・揺れ物 (読み込み直後の、根元が原点の姿勢で)
            model.RestOverride = TPoseRest(model);
            model.AfterPose = BuildInherits(pmx, bones, model);
            try
            {
                model.Springs = BuildSprings(pmx, bones, model, out int colliders);
                int legs = model.Springs.AddLegColliders(model.Human);
                model.Skirt = SkirtFollow.Build(model.Human, model.Springs.Roots);
                log?.Invoke($"PMX の剛体から: 揺れ物の骨 {model.Springs.JointCount}、当たり判定 {colliders}、脚の当たり判定を足した骨 {legs}");
            }
            catch (Exception e) { log?.Invoke($"揺れ物を作れません: {e.Message}"); }

            log?.Invoke($"PMX {pmx.Version:0.0} '{model.Title}' ({pmx.Name}): 頂点 {pmx.Vertices.Count}、骨 {bones.Length}、材質 {materials.Count}、" +
                        $"テクスチャ {textures.Count(t => t.Value.tex != null)}/{textures.Count}、人型の骨 {model.Human.Count}、" +
                        $"表情 {model.Expressions?.Count ?? 0}、剛体 {pmx.RigidBodies.Count}");
            return model;
        }
        catch
        {
            model.Destroy();
            throw;
        }
    }

    // ------------------------------------------------------------------ 骨

    private static Transform[] BuildBones(PmxFile pmx, VrmModel model)
    {
        int n = pmx.Bones.Count;
        var bones = new Transform[n];
        var used = new HashSet<string>();
        for (int i = 0; i < n; i++)
        {
            var name = string.IsNullOrEmpty(pmx.Bones[i].Name) ? $"bone{i}" : pmx.Bones[i].Name;
            var unique = name;
            for (int k = 2; !used.Add(unique); k++) unique = $"{name}_{k}";
            bones[i] = new GameObject(unique).transform;
            bones[i].SetParent(model.Root.transform, false);
            bones[i].localPosition = P(pmx.Bones[i].Position);
        }
        // 親子をつなぐ (位置はそのまま。親の番号が壊れている・輪になっているものは根元の下のまま)
        for (int i = 0; i < n; i++)
        {
            int p = pmx.Bones[i].Parent;
            if (p < 0 || p >= n || p == i || bones[p].IsChildOf(bones[i])) continue;
            bones[i].SetParent(bones[p], true);
        }
        return bones;
    }

    /// <summary>名前を比べやすくする (全角の数字・英字を半角に)</summary>
    private static string Norm(string s)
    {
        var c = s.Trim().ToCharArray();
        for (int i = 0; i < c.Length; i++)
            if (c[i] >= '０' && c[i] <= '９') c[i] = (char)('0' + (c[i] - '０'));
            else if (c[i] >= 'Ａ' && c[i] <= 'Ｚ') c[i] = (char)('A' + (c[i] - 'Ａ'));
            else if (c[i] >= 'ａ' && c[i] <= 'ｚ') c[i] = (char)('a' + (c[i] - 'ａ'));
        return new string(c);
    }

    /// <summary>人型の骨 → MMD の骨の名前 (前にあるものを優先。足D などの「D」は実際にメッシュを動かす骨)</summary>
    private static readonly (HumanBodyBones bone, string[] names)[] HumanNames =
    {
        (HumanBodyBones.Spine, new[] { "上半身" }),
        (HumanBodyBones.Chest, new[] { "上半身2" }),
        (HumanBodyBones.UpperChest, new[] { "上半身3" }),
        (HumanBodyBones.Neck, new[] { "首" }),
        (HumanBodyBones.Head, new[] { "頭" }),
        (HumanBodyBones.LeftEye, new[] { "左目", "Left Eye", "LeftEye" }),
        (HumanBodyBones.RightEye, new[] { "右目", "Right Eye", "RightEye" }),
        (HumanBodyBones.LeftShoulder, new[] { "左肩" }),
        (HumanBodyBones.LeftUpperArm, new[] { "左腕" }),
        (HumanBodyBones.LeftLowerArm, new[] { "左ひじ", "左肘" }),
        (HumanBodyBones.LeftHand, new[] { "左手首" }),
        (HumanBodyBones.RightShoulder, new[] { "右肩" }),
        (HumanBodyBones.RightUpperArm, new[] { "右腕" }),
        (HumanBodyBones.RightLowerArm, new[] { "右ひじ", "右肘" }),
        (HumanBodyBones.RightHand, new[] { "右手首" }),
        (HumanBodyBones.LeftUpperLeg, new[] { "左足D", "左足" }),
        (HumanBodyBones.LeftLowerLeg, new[] { "左ひざD", "左ひざ", "左膝D", "左膝" }),
        (HumanBodyBones.LeftFoot, new[] { "左足首D", "左足首" }),
        (HumanBodyBones.LeftToes, new[] { "左足先EX", "左つま先" }),
        (HumanBodyBones.RightUpperLeg, new[] { "右足D", "右足" }),
        (HumanBodyBones.RightLowerLeg, new[] { "右ひざD", "右ひざ", "右膝D", "右膝" }),
        (HumanBodyBones.RightFoot, new[] { "右足首D", "右足首" }),
        (HumanBodyBones.RightToes, new[] { "右足先EX", "右つま先" }),
    };

    private static void MapHumanoid(PmxFile pmx, Transform[] bones, VrmModel model, Action<string> log)
    {
        var byName = new Dictionary<string, Transform>();
        for (int i = 0; i < bones.Length; i++)
        {
            var k = Norm(pmx.Bones[i].Name ?? "");
            if (k.Length > 0 && !byName.ContainsKey(k)) byName[k] = bones[i];
        }
        Transform Find(params string[] names)
        {
            foreach (var n in names)
                if (byName.TryGetValue(n, out var t)) return t;
            return null;
        }

        foreach (var (bone, names) in HumanNames)
        {
            var t = Find(names);
            if (t != null) model.Human[bone] = t;
        }

        // 腰: 上半身と下半身の両方の親になる骨。「腰」があればそれ。無ければ下半身を腰にして、上半身をその子にする
        // (動きは骨ごとにワールドの向きで写すので、親子を変えても見た目は変わらない。腰の位置を動かしたときに上半身も付いてくるようにするため)
        var upper = Find("上半身");
        var lower = Find("下半身");
        var waist = Find("腰");
        if (waist != null && upper != null && lower != null && upper.IsChildOf(waist) && lower.IsChildOf(waist))
            model.Human[HumanBodyBones.Hips] = waist;
        else if (lower != null)
        {
            if (upper != null && !upper.IsChildOf(lower)) upper.SetParent(lower, true);
            model.Human[HumanBodyBones.Hips] = lower;
        }

        // 指: 親指は 0 (付け根) があれば 0・1・2、無ければ 1・2 を第 1・第 2 関節に
        foreach (var side in new[] { "左", "右" })
        {
            bool left = side == "左";
            var thumb0 = Find(side + "親指0");
            if (thumb0 != null)
            {
                Set(model, left ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal, thumb0);
                Set(model, left ? HumanBodyBones.LeftThumbIntermediate : HumanBodyBones.RightThumbIntermediate, Find(side + "親指1"));
                Set(model, left ? HumanBodyBones.LeftThumbDistal : HumanBodyBones.RightThumbDistal, Find(side + "親指2"));
            }
            else
            {
                Set(model, left ? HumanBodyBones.LeftThumbIntermediate : HumanBodyBones.RightThumbIntermediate, Find(side + "親指1"));
                Set(model, left ? HumanBodyBones.LeftThumbDistal : HumanBodyBones.RightThumbDistal, Find(side + "親指2"));
            }
            foreach (var (finger, first) in new[]
                     {
                         ("人指", left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal),
                         ("中指", left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal),
                         ("薬指", left ? HumanBodyBones.LeftRingProximal : HumanBodyBones.RightRingProximal),
                         ("小指", left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal),
                     })
                for (int k = 0; k < 3; k++)
                    Set(model, first + k, Find(side + finger + (k + 1)));
        }

        if (!model.Human.ContainsKey(HumanBodyBones.Hips) || !model.Human.ContainsKey(HumanBodyBones.Head))
            throw new InvalidDataException("MMD の標準の骨 (下半身・上半身・頭) が見つかりません");
        log?.Invoke($"PMX の人型の骨: 腰 = {model.Human[HumanBodyBones.Hips].name}、" +
                    $"足 = {(model.Human.TryGetValue(HumanBodyBones.LeftUpperLeg, out var l) ? l.name : "なし")}、" +
                    $"見つからない骨: {string.Join(" ", HumanNames.Where(h => !model.Human.ContainsKey(h.bone)).Select(h => h.names[0]))}");
    }

    private static void Set(VrmModel model, HumanBodyBones bone, Transform t)
    {
        if (t != null) model.Human[bone] = t;
    }

    /// <summary>
    /// 動きを写すときの基準の姿勢: 骨を別に複製して、腕 (上腕・前腕) を真横に向けた T ポーズにする。
    /// モデルの骨そのものは A ポーズのまま (メッシュはこの姿勢で骨に付いている)
    /// </summary>
    private static Dictionary<HumanBodyBones, (Transform bone, Quaternion rotation, Vector3 position)> TPoseRest(VrmModel model)
    {
        var shadow = new Dictionary<IntPtr, Transform>();
        var shadowRoot = new GameObject("RusK_PmxRest").transform;
        try
        {
            void Copy(Transform src, Transform dstParent)
            {
                var s = new GameObject(src.name).transform;
                s.SetParent(dstParent, false);
                s.localPosition = src.localPosition;
                s.localRotation = src.localRotation;
                shadow[src.Pointer] = s;
                for (int i = 0; i < src.childCount; i++) Copy(src.GetChild(i), s);
            }
            for (int i = 0; i < model.Root.transform.childCount; i++) Copy(model.Root.transform.GetChild(i), shadowRoot);

            Transform S(HumanBodyBones b) => model.Human.TryGetValue(b, out var t) && shadow.TryGetValue(t.Pointer, out var s) ? s : null;
            void Aim(Transform bone, Transform child, Vector3 dir)
            {
                if (bone == null || child == null) return;
                var cur = child.position - bone.position;
                if (cur.sqrMagnitude < 1e-8f) return;
                bone.rotation = Quaternion.FromToRotation(cur, dir) * bone.rotation;
            }
            Aim(S(HumanBodyBones.LeftUpperArm), S(HumanBodyBones.LeftLowerArm), Vector3.left);
            Aim(S(HumanBodyBones.LeftLowerArm), S(HumanBodyBones.LeftHand), Vector3.left);
            Aim(S(HumanBodyBones.RightUpperArm), S(HumanBodyBones.RightLowerArm), Vector3.right);
            Aim(S(HumanBodyBones.RightLowerArm), S(HumanBodyBones.RightHand), Vector3.right);

            var result = new Dictionary<HumanBodyBones, (Transform, Quaternion, Vector3)>();
            foreach (var kv in model.Human)
            {
                var s = shadow[kv.Value.Pointer];
                result[kv.Key] = (kv.Value, s.rotation, s.position);
            }
            return result;
        }
        finally
        {
            Object.Destroy(shadowRoot.gameObject);
        }
    }

    /// <summary>
    /// 付与 (ほかの骨の回転を、倍率を掛けて写す)。人型の骨と、その上にある骨 (腰キャンセルなど) は
    /// 動きを写した向きを崩さないよう対象にしない。付与元が先に決まるよう、付与の段数・骨の深さの順に計算する
    /// </summary>
    private static Action BuildInherits(PmxFile pmx, Transform[] bones, VrmModel model)
    {
        var mapped = new HashSet<IntPtr>(model.Human.Values.Select(t => t.Pointer));
        bool HasMappedBelow(Transform t)
        {
            foreach (var h in model.Human.Values)
                if (h != t && h.IsChildOf(t)) return true;
            return false;
        }
        int Depth(Transform t)
        {
            int d = 0;
            for (var p = t; p != null; p = p.parent) d++;
            return d;
        }
        int Chain(int i)
        {
            int c = 0;
            for (int k = i; k >= 0 && k < pmx.Bones.Count && pmx.Bones[k].InheritRotation && c < 16; k = pmx.Bones[k].InheritParent) c++;
            return c;
        }

        var list = new List<(Transform bone, Transform source, float weight, int order)>();
        for (int i = 0; i < pmx.Bones.Count; i++)
        {
            var b = pmx.Bones[i];
            if (!b.InheritRotation || b.InheritParent < 0 || b.InheritParent >= bones.Length || b.InheritParent == i) continue;
            var t = bones[i];
            if (mapped.Contains(t.Pointer) || HasMappedBelow(t)) continue;
            list.Add((t, bones[b.InheritParent], b.InheritWeight, Chain(i) * 1000 + Depth(t)));
        }
        if (list.Count == 0) return null;
        var ordered = list.OrderBy(x => x.order).Select(x => (x.bone, x.source, x.weight)).ToArray();
        return () =>
        {
            foreach (var (bone, source, weight) in ordered)
                bone.localRotation = Power(source.localRotation, weight); // 基準の回転はどの骨も単位回転
        };
    }

    /// <summary>回転を weight 倍する (負の倍率は逆向き)</summary>
    private static Quaternion Power(Quaternion q, float weight)
    {
        if (Mathf.Approximately(weight, 1f)) return q;
        q.ToAngleAxis(out float angle, out var axis);
        if (float.IsNaN(axis.x) || axis.sqrMagnitude < 1e-8f) return Quaternion.identity;
        if (angle > 180f) angle -= 360f;
        return Quaternion.AngleAxis(angle * weight, axis);
    }

    // ------------------------------------------------------------------ テクスチャ・材質

    private static (Texture2D, bool) LoadTexture(PmxFile pmx, int index, string dir,
        Dictionary<int, (Texture2D, bool)> cache, VrmModel model, Action<string> log)
    {
        if (index < 0 || index >= pmx.Textures.Count) return (null, false);
        if (cache.TryGetValue(index, out var c)) return c;
        (Texture2D, bool) result = (null, false);
        var rel = pmx.Textures[index].Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar).Trim();
        try
        {
            var file = Path.Combine(dir, rel);
            if (!File.Exists(file)) file = Path.Combine(dir, Path.GetFileName(rel)); // フォルダの付け方が違うモデル
            if (!File.Exists(file)) log?.Invoke($"テクスチャが見つかりません: {rel}");
            else
            {
                var tex = ImageDecoder.Load(file, out bool alpha);
                if (tex != null)
                {
                    model.Assets.Add(tex);
                    result = (tex, alpha);
                }
            }
        }
        catch (Exception e) { log?.Invoke($"テクスチャを読めません ({rel}): {e.Message}"); }
        cache[index] = result;
        return result;
    }

    private static Material MakeMaterial(PmxFile.Material m, Texture2D tex, bool texAlpha, VrmLoader.Templates templates,
        VrmModel model, Dictionary<IntPtr, Texture2D> alphaCache, Dictionary<IntPtr, (byte[] a, int w, int h)> alphaBytes)
    {
        bool transparent = m.Diffuse.a < 0.99f;
        var template = transparent && templates.Transparent != null ? templates.Transparent : templates.Opaque;
        var mat = new Material(template) { name = string.IsNullOrEmpty(m.Name) ? "pmx" : m.Name };
        model.Assets.Add(mat);
        model.Materials.Add(mat);

        // MMD の色 = 環境色 + 拡散色 × ライト (標準 0.6)。テクスチャがあれば掛ける。ガンマの値のまま (MMD に線形の考えは無い)
        var color = new Color(
            Mathf.Clamp01(m.Ambient.x + m.Diffuse.r * 0.6f),
            Mathf.Clamp01(m.Ambient.y + m.Diffuse.g * 0.6f),
            Mathf.Clamp01(m.Ambient.z + m.Diffuse.b * 0.6f),
            Mathf.Clamp01(m.Diffuse.a));
        VrmLoader.SetTex(mat, "_MainTex", tex != null ? tex : Texture2D.whiteTexture);
        VrmLoader.SetTex(mat, "_BaseMap", tex);
        VrmLoader.SetColor(mat, "_BaseColor", color);
        VrmLoader.SetColor(mat, "_Color", color);

        // 透明度のあるテクスチャ (髪の毛先・まつ毛など) は切り抜きにする (VRM の MASK と同じ仕組み)
        if (tex != null && texAlpha && !transparent)
        {
            var mask = new MaskMaterial
            {
                Material = mat, Template = template, Texture = tex,
                AlphaTexture = VrmLoader.AlphaMap(tex, alphaCache, alphaBytes, model),
                Color = color.linear, Cutoff = 0.5f, DoubleSided = m.DoubleSided,
            };
            if (alphaBytes.TryGetValue(tex.Pointer, out var ab)) (mask.AlphaBytes, mask.AlphaWidth, mask.AlphaHeight) = ab;
            model.Masks.Add(mask);
        }

        VrmLoader.SetEmission(mat, Color.black, null);
        VrmLoader.FinishMaterial(mat, template, templates, model);
        return mat;
    }

    // ------------------------------------------------------------------ メッシュ

    private static SkinnedMeshRenderer BuildMesh(PmxFile pmx, Transform[] bones, List<Material> materials,
        Dictionary<int, MaskMaterial> masks, VrmModel model, out List<int> dupSource)
    {
        int vcount = pmx.Vertices.Count;
        var verts = new List<Vector3>(vcount);
        var normals = new List<Vector3>(vcount);
        var uvs = new List<Vector2>(vcount);
        var weights = new List<BoneWeight>(vcount);
        int nb = bones.Length;
        foreach (var v in pmx.Vertices)
        {
            verts.Add(P(v.Position));
            normals.Add(Dir(v.Normal).normalized);
            uvs.Add(new Vector2(v.Uv.x, 1f - v.Uv.y)); // PMX は上が 0
            weights.Add(Weight(v, nb));
        }

        // 材質ごとの三角形 (面は材質の順に並んでいる)
        int subCount = pmx.Materials.Count;
        var sub = new int[subCount][];
        int at = 0;
        for (int s = 0; s < subCount; s++)
        {
            int count = Math.Max(0, Math.Min(pmx.Materials[s].IndexCount, pmx.Indices.Length - at));
            count -= count % 3;
            // 不透明度 0 の材質は描かない (材質モーフで出し入れする包帯・照れ・別の色の目など。最初は隠れている)
            if (pmx.Materials[s].Diffuse.a <= 0.004f) count = 0;
            sub[s] = new int[count];
            for (int i = 0; i < count; i++)
            {
                int idx = pmx.Indices[at + i];
                sub[s][i] = idx >= 0 && idx < vcount ? idx : 0;
            }
            at += pmx.Materials[s].IndexCount;
            if (masks.TryGetValue(s, out var mm)) VrmLoader.CheckTransparency(mm, uvs, sub[s]);
        }

        // 両面描画の材質は、裏向きの面をメッシュに足す (VRM と同じ。ゲームのシェーダーは裏面の法線を反転しないため)
        var front = (int[][])sub.Clone();
        dupSource = new List<int>();
        var dups = dupSource;
        var map = new Dictionary<int, int>();
        for (int s = 0; s < subCount; s++)
        {
            if (!pmx.Materials[s].DoubleSided || sub[s].Length == 0) continue;
            var idx = sub[s];
            var both = new int[idx.Length * 2];
            Array.Copy(idx, both, idx.Length);
            int Dup(int v)
            {
                if (!map.TryGetValue(v, out var d))
                {
                    d = vcount + dups.Count;
                    dups.Add(v);
                    map[v] = d;
                }
                return d;
            }
            for (int i = 0; i + 2 < idx.Length; i += 3)
            {
                both[idx.Length + i] = Dup(idx[i]);
                both[idx.Length + i + 1] = Dup(idx[i + 2]);
                both[idx.Length + i + 2] = Dup(idx[i + 1]);
            }
            sub[s] = both;
        }
        foreach (int v in dupSource)
        {
            verts.Add(verts[v]);
            normals.Add(-normals[v]);
            uvs.Add(uvs[v]);
            weights.Add(weights[v]);
        }

        var bindposes = bones.Select(b => b.worldToLocalMatrix * model.Root.transform.localToWorldMatrix).ToArray();
        Mesh Make(string name, int[][] tris)
        {
            var mesh = new Mesh { name = name };
            if (verts.Count > 65535) mesh.indexFormat = IndexFormat.UInt32;
            mesh.vertices = verts.ToArray();
            mesh.normals = normals.ToArray();
            mesh.uv = uvs.ToArray();
            mesh.boneWeights = weights.ToArray();
            mesh.bindposes = bindposes;
            mesh.subMeshCount = subCount;
            for (int s = 0; s < subCount; s++) mesh.SetTriangles(tris[s], s);
            mesh.RecalculateBounds();
            model.Assets.Add(mesh);
            return mesh;
        }
        var body = Make(model.Title, sub);
        if (dupSource.Count > 0) model.ShadowMeshes[body.Pointer] = Make(model.Title + "_shadow", front);

        var go = new GameObject(model.Title + "_mesh");
        go.transform.SetParent(model.Root.transform, false);
        var smr = go.AddComponent<SkinnedMeshRenderer>();
        smr.bones = bones;
        smr.rootBone = model.Human.TryGetValue(HumanBodyBones.Hips, out var hips) ? hips : bones.FirstOrDefault();
        smr.sharedMesh = body;
        smr.sharedMaterials = materials.ToArray();
        smr.updateWhenOffscreen = true;
        model.Renderers.Add(smr);
        model.MeshRenderers[0] = new List<SkinnedMeshRenderer> { smr };
        return smr;
    }

    private static BoneWeight Weight(PmxFile.Vertex v, int boneCount)
    {
        var list = new List<(int b, float w)>(4);
        void Add(int b, float w)
        {
            if (b < 0 || b >= boneCount || w <= 0f) return;
            for (int i = 0; i < list.Count; i++)
                if (list[i].b == b) { list[i] = (b, list[i].w + w); return; }
            list.Add((b, w));
        }
        Add(v.B0, v.W0); Add(v.B1, v.W1); Add(v.B2, v.W2); Add(v.B3, v.W3);
        if (list.Count == 0) list.Add((Math.Max(0, Math.Min(v.B0, boneCount - 1)), 1f));
        list.Sort((a, b) => b.w.CompareTo(a.w));
        float sum = list.Sum(x => x.w);
        (int b, float w) G(int i) => i < list.Count ? (list[i].b, list[i].w / sum) : (0, 0f);
        return new BoneWeight
        {
            boneIndex0 = G(0).b, weight0 = G(0).w,
            boneIndex1 = G(1).b, weight1 = G(1).w,
            boneIndex2 = G(2).b, weight2 = G(2).w,
            boneIndex3 = G(3).b, weight3 = G(3).w,
        };
    }

    // ------------------------------------------------------------------ 表情

    /// <summary>VRM の表情の名前 → MMD のモーフの名前 (よく使われるもの)</summary>
    private static readonly (string expression, string[] morphs)[] ExpressionNames =
    {
        ("blink", new[] { "まばたき", "瞬き", "blink" }),
        ("blink_l", new[] { "ウィンク", "ウインク", "ｳｨﾝｸ", "wink" }),
        ("blink_r", new[] { "ウィンク右", "ウインク右", "ｳｨﾝｸ右", "wink_r", "wink right" }),
        ("a", new[] { "あ", "a" }),
        ("i", new[] { "い", "i" }),
        ("u", new[] { "う", "u" }),
        ("e", new[] { "え", "e" }),
        ("o", new[] { "お", "o" }),
        ("joy", new[] { "笑い", "smile" }),
        ("fun", new[] { "にこり", "にっこり", "なごみ" }),
        ("angry", new[] { "怒り", "angry" }),
        ("sorrow", new[] { "困る", "悲しい", "sad" }),
        ("surprised", new[] { "びっくり", "驚き", "surprised" }),
    };

    /// <summary>表情で使うモーフだけを、メッシュのブレンドシェイプとして足す (全部足すと重いため)。dup は裏向きに複製した頂点の元</summary>
    private static Expressions BuildExpressions(PmxFile pmx, SkinnedMeshRenderer smr, List<int> dup, Action<string> log)
    {
        var mesh = smr.sharedMesh;
        int vcount = pmx.Vertices.Count;
        var ex = new Expressions();

        var byName = new Dictionary<string, int>();
        for (int i = 0; i < pmx.Morphs.Count; i++)
        {
            var m = pmx.Morphs[i];
            foreach (var n in new[] { m.Name, m.NameEn })
            {
                var k = Norm(n ?? "").ToLowerInvariant();
                if (k.Length > 0 && !byName.ContainsKey(k)) byName[k] = i;
            }
        }

        var shapeOf = new Dictionary<int, int>(); // モーフの番号 → ブレンドシェイプの番号
        int Shape(int morph)
        {
            if (shapeOf.TryGetValue(morph, out var s)) return s;
            var m = pmx.Morphs[morph];
            var dv = new Vector3[vcount + dup.Count];
            foreach (var (v, off) in m.Vertices)
                if (v >= 0 && v < vcount) dv[v] = P(off);
            for (int i = 0; i < dup.Count; i++) dv[vcount + i] = dv[dup[i]];
            mesh.AddBlendShapeFrame($"{morph}_{m.Name}", 100f, dv, null, null);
            s = mesh.blendShapeCount - 1;
            shapeOf[morph] = s;
            return s;
        }
        // グループモーフは中の頂点モーフに分ける (倍率を掛けて)
        void Bind(string expression, int morph, float weight, int depth)
        {
            if (morph < 0 || morph >= pmx.Morphs.Count || depth > 4) return;
            var m = pmx.Morphs[morph];
            if (m.Type == 1 && m.Vertices != null && m.Vertices.Count > 0) ex.Bind(expression, smr, Shape(morph), weight * 100f);
            else if (m.Type == 0 && m.Group != null)
                foreach (var (child, w) in m.Group) Bind(expression, child, weight * w, depth + 1);
        }

        var found = new List<string>();
        foreach (var (expression, names) in ExpressionNames)
        {
            foreach (var n in names)
            {
                if (!byName.TryGetValue(Norm(n).ToLowerInvariant(), out var morph)) continue;
                Bind(expression, morph, 1f, 0);
                if (ex.Has(expression)) found.Add($"{expression}={pmx.Morphs[morph].Name}");
                break;
            }
        }
        log?.Invoke($"PMX の表情: {(found.Count > 0 ? string.Join(" ", found) : "なし")} (モーフ {pmx.Morphs.Count} 個のうち)");
        return ex;
    }

    // ------------------------------------------------------------------ 揺れ物 (剛体)

    /// <summary>
    /// MMD の物理 (剛体とジョイント) を、VRM の揺れ物で近く再現する:
    /// 物理で動く剛体の骨 → 揺れる骨、骨に付いて動く剛体 → 当たり判定 (当たらない組の設定も使う)
    /// </summary>
    private static SpringBoneSystem BuildSprings(PmxFile pmx, Transform[] bones, VrmModel model, out int colliderCount)
    {
        var sys = SpringBoneSystem.Create(model.Root.transform);
        var human = new HashSet<IntPtr>(model.Human.Values.Select(t => t.Pointer));

        // 当たり判定 (グループごと)
        var colliders = new List<(int group, SpringBoneSystem.Collider c)>();
        foreach (var rb in pmx.RigidBodies)
        {
            if (rb.Mode != 0 || rb.Bone < 0 || rb.Bone >= bones.Length) continue;
            var node = bones[rb.Bone];
            var center = P(rb.Position);
            var rot = Rotation(rb.Rotation);
            var c = new SpringBoneSystem.Collider { Node = node };
            switch (rb.Shape)
            {
                case 0: // 球
                    c.Radius = rb.Size.x * Unit;
                    c.Offset = c.Tail = node.InverseTransformPoint(center);
                    break;
                case 2: // カプセル (Y 方向に高さ)
                {
                    c.Capsule = true;
                    c.Radius = rb.Size.x * Unit;
                    var half = rot * Vector3.up * (rb.Size.y * Unit * 0.5f);
                    c.Offset = node.InverseTransformPoint(center - half);
                    c.Tail = node.InverseTransformPoint(center + half);
                    break;
                }
                default: // 箱: 一番長い向きのカプセルにする (太さは残りの短い方)
                {
                    var s = rb.Size * Unit; // 半分の大きさ
                    Vector3 axis; float length, radius;
                    if (s.x >= s.y && s.x >= s.z) { axis = Vector3.right; length = s.x; radius = Mathf.Min(s.y, s.z); }
                    else if (s.y >= s.z) { axis = Vector3.up; length = s.y; radius = Mathf.Min(s.x, s.z); }
                    else { axis = Vector3.forward; length = s.z; radius = Mathf.Min(s.x, s.y); }
                    c.Capsule = true;
                    c.Radius = radius;
                    var half = rot * axis * Mathf.Max(0f, length - radius);
                    c.Offset = node.InverseTransformPoint(center - half);
                    c.Tail = node.InverseTransformPoint(center + half);
                    break;
                }
            }
            colliders.Add((rb.Group, c));
        }
        colliderCount = colliders.Count;

        // 揺れる骨
        var dynamicBones = new HashSet<int>(pmx.RigidBodies.Where(r => r.Mode != 0 && r.Bone >= 0 && r.Bone < bones.Length).Select(r => r.Bone));
        foreach (var rb in pmx.RigidBodies)
        {
            if (rb.Mode == 0 || rb.Bone < 0 || rb.Bone >= bones.Length) continue;
            var bone = bones[rb.Bone];
            if (human.Contains(bone.Pointer)) continue;
            var info = pmx.Bones[rb.Bone];

            // 骨の先: PMX の「先」の設定、無ければ揺れる子の骨、それも無ければ親からの向きに少し伸ばす
            Vector3 tail;
            if (info.TailBone >= 0 && info.TailBone < bones.Length && info.TailBone != rb.Bone) tail = bones[info.TailBone].position;
            else if (info.TailBone < 0 && info.TailOffset.sqrMagnitude > 1e-8f) tail = bone.position + Dir(info.TailOffset) * Unit;
            else
            {
                Transform child = null;
                for (int i = 0; i < bone.childCount && child == null; i++)
                {
                    int ci = Array.IndexOf(bones, bone.GetChild(i));
                    if (ci >= 0 && dynamicBones.Contains(ci)) child = bone.GetChild(i);
                }
                var d = bone.parent != null ? bone.position - bone.parent.position : Vector3.down;
                tail = child != null ? child.position : bone.position + d.normalized * 0.05f;
            }

            var cols = colliders.Where(c => (rb.NoCollisionMask & (1 << c.group)) != 0 ? false : true).Select(c => c.c).ToList();
            float hit = Mathf.Clamp(rb.Size.x * Unit, 0.005f, 0.05f);
            // MMD の物理は重く垂れる。VRM の揺れ物の標準的な値に、少し重力を足す
            sys.Add(bone, bone.InverseTransformPoint(tail), 0.8f, 0.05f, Vector3.down, 0.4f, hit, cols);
        }
        return sys;
    }

    /// <summary>剛体の回転 (ラジアン、MMD の座標) → Unity の座標の回転</summary>
    private static Quaternion Rotation(Vector3 r)
    {
        var q = Quaternion.Euler(r.x * Mathf.Rad2Deg, r.y * Mathf.Rad2Deg, r.z * Mathf.Rad2Deg);
        return new Quaternion(-q.x, q.y, -q.z, q.w); // Y 軸で 180 度回した座標に直す
    }
}
