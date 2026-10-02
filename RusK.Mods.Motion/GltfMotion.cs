using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using RusK.Mods.Model.Vrm;
using UnityEngine;

namespace RusK.Mods.Motion;

/// <summary>
/// glb のアニメーション 1 つ分。骨 (ノード) の基準の姿勢と、キーフレームを持ち、好きな時間の姿勢を計算する。
/// 座標は glTF → Unity (X を反転)。Blender などはどの骨にもキーを書くので、値が変わらない (基準の姿勢のまま) チャンネルは
/// 「動いていない」として扱う (ゲームの動きのまま残す)
/// </summary>
internal sealed class GltfMotion
{
    private sealed class Channel
    {
        public int Node;
        public int Path;        // 0: 位置、1: 回転
        public float[] Times;
        public float[] Values;  // 位置は 3 つ、回転は 4 つずつ
        public bool Step;
    }

    public string Name;
    public string File;
    public float Length;
    public string[] Names;
    public int[] Parent;
    public Vector3[] RestPos;
    public Quaternion[] RestRot;
    /// <summary>骨の大きさ (同じ倍率で扱う。Mixamo はセンチなので 0.01 が付いている)</summary>
    public float[] RestScale;
    /// <summary>人型の骨 → ノード (VRMA の対応表、Mixamo・VRoid の名前から)。ゲームと違う骨格の動きを載せ替えるのに使う</summary>
    public Dictionary<HumanBodyBones, int> Human = new();
    /// <summary>この骨自体に動きのキーがある (この骨だけを上書きし、子はゲームの動きのまま親に付いていく)</summary>
    public bool[] Keyed;
    /// <summary>この骨か、その親のどれかが動いている</summary>
    public bool[] Animated;
    /// <summary>この骨の位置が動いている</summary>
    public bool[] Moves;

    private readonly List<Channel> _channels = new();
    private float[] _worldScale;

    private static Vector3 P(float x, float y, float z) => new(-x, y, z);
    private static Quaternion Q(float x, float y, float z, float w) => new(x, -y, -z, w);

    /// <summary>glb の中のアニメーションを全部読む</summary>
    public static List<GltfMotion> Load(string path)
    {
        using var glb = Glb.Load(path);
        var json = glb.Json;
        var result = new List<GltfMotion>();
        if (!json.TryGetProperty("animations", out var anims) || anims.GetArrayLength() == 0) return result;

        var nodesEl = json.GetProperty("nodes");
        int n = nodesEl.GetArrayLength();
        var names = new string[n];
        var parent = Enumerable.Repeat(-1, n).ToArray();
        var restPos = new Vector3[n];
        var restRot = new Quaternion[n];
        var restScale = Enumerable.Repeat(1f, n).ToArray();
        for (int i = 0; i < n; i++)
        {
            var node = nodesEl[i];
            names[i] = node.TryGetProperty("name", out var ne) ? ne.GetString() : $"node{i}";
            restRot[i] = Quaternion.identity;
            if (node.TryGetProperty("matrix", out var mat))
            {
                var m = new Matrix4x4();
                for (int c = 0; c < 4; c++)
                    for (int r = 0; r < 4; r++) m[r, c] = mat[c * 4 + r].GetSingle();
                restPos[i] = P(m.m03, m.m13, m.m23);
                var q = m.rotation;
                restRot[i] = Q(q.x, q.y, q.z, q.w);
            }
            if (node.TryGetProperty("translation", out var t)) restPos[i] = P(t[0].GetSingle(), t[1].GetSingle(), t[2].GetSingle());
            if (node.TryGetProperty("rotation", out var ro)) restRot[i] = Q(ro[0].GetSingle(), ro[1].GetSingle(), ro[2].GetSingle(), ro[3].GetSingle());
            if (node.TryGetProperty("scale", out var sc)) restScale[i] = sc[0].GetSingle();
            if (node.TryGetProperty("children", out var ch))
                foreach (var c in ch.EnumerateArray()) parent[c.GetInt32()] = i;
        }

        var human = ReadHuman(json, names);

        foreach (var anim in anims.EnumerateArray())
        {
            string animName = anim.TryGetProperty("name", out var an) ? an.GetString() : null;
            // VRMA は名前が "animation" だけなので、ファイルの名前にする
            if (string.IsNullOrEmpty(animName) || animName == "animation")
                animName = anims.GetArrayLength() == 1 ? Path.GetFileNameWithoutExtension(path) : $"{Path.GetFileNameWithoutExtension(path)}_{result.Count}";
            var motion = new GltfMotion
            {
                Name = animName,
                File = path, Names = names, Parent = parent, RestPos = restPos, RestRot = restRot, RestScale = restScale,
                Animated = new bool[n], Keyed = new bool[n], Moves = new bool[n], Human = human,
            };
            var samplers = anim.GetProperty("samplers");
            foreach (var ch in anim.GetProperty("channels").EnumerateArray())
            {
                var target = ch.GetProperty("target");
                if (!target.TryGetProperty("node", out var nodeEl)) continue;
                string path0 = target.GetProperty("path").GetString();
                int kind = path0 == "translation" ? 0 : path0 == "rotation" ? 1 : -1;
                if (kind < 0) continue; // 大きさ・表情は使わない
                var s = samplers[ch.GetProperty("sampler").GetInt32()];
                string interp = s.TryGetProperty("interpolation", out var ie) ? ie.GetString() : "LINEAR";
                var times = glb.ReadFloats(s.GetProperty("input").GetInt32(), out _);
                var raw = glb.ReadFloats(s.GetProperty("output").GetInt32(), out _);
                int dim = kind == 0 ? 3 : 4;
                // 3 次スプラインは (入りの接線, 値, 出の接線) の並び。値だけ使う (直線でつなぐ)
                var values = raw;
                if (interp == "CUBICSPLINE")
                {
                    values = new float[times.Length * dim];
                    for (int k = 0; k < times.Length; k++) Array.Copy(raw, (k * 3 + 1) * dim, values, k * dim, dim);
                }
                int node = nodeEl.GetInt32();
                var c = new Channel { Node = node, Path = kind, Times = times, Values = Convert(values, kind), Step = interp == "STEP" };
                motion.Length = Mathf.Max(motion.Length, times.Length > 0 ? times[times.Length - 1] : 0f);
                if (!Changes(c, motion)) continue;
                motion._channels.Add(c);
                if (kind == 0) motion.Moves[node] = true;
                motion.Animated[node] = true;
                motion.Keyed[node] = true;
            }
            // 親が動いていれば子も動く
            for (int i = 0; i < n; i++)
                for (int p = parent[i]; p >= 0 && !motion.Animated[i]; p = parent[p])
                    if (motion.Animated[p]) motion.Animated[i] = true;
            result.Add(motion);
        }
        return result;
    }

    /// <summary>人型の骨: VRMA (VRMC_vrm_animation) の対応表、無ければ骨の名前 (Mixamo・VRoid)</summary>
    private static Dictionary<HumanBodyBones, int> ReadHuman(JsonElement json, string[] names)
    {
        var map = new Dictionary<HumanBodyBones, int>();
        if (json.TryGetProperty("extensions", out var ext) && ext.TryGetProperty("VRMC_vrm_animation", out var vrma)
            && vrma.TryGetProperty("humanoid", out var hum) && hum.TryGetProperty("humanBones", out var bones))
        {
            foreach (var b in bones.EnumerateObject())
            {
                if (!b.Value.TryGetProperty("node", out var nodeEl)) continue;
                // VRM 1.0 は親指の名前が 1 段ずれている (Metacarpal / Proximal / Distal → Unity の Proximal / Intermediate / Distal)
                var name = b.Name.Replace("ThumbProximal", "ThumbIntermediate").Replace("ThumbMetacarpal", "ThumbProximal");
                if (Enum.TryParse<HumanBodyBones>(name, true, out var hb) && hb != HumanBodyBones.LastBone) map[hb] = nodeEl.GetInt32();
            }
            if (map.Count > 0) return map;
        }
        for (int i = 0; i < names.Length; i++)
            if (HumanName(names[i]) is HumanBodyBones hb && !map.ContainsKey(hb)) map[hb] = i;
        return map;
    }

    /// <summary>骨の名前 → 人型の骨 (Mixamo の mixamorig:LeftArm、VRoid の J_Bip_L_UpperArm)</summary>
    private static HumanBodyBones? HumanName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var n = raw;
        int colon = n.LastIndexOf(':');
        if (colon >= 0) n = n.Substring(colon + 1);
        if (n.StartsWith("J_Bip_"))
        {
            // J_Bip_C_Hips / J_Bip_L_UpperArm / J_Bip_L_Thumb1
            var parts = n.Split('_');
            if (parts.Length < 4) return null;
            string side = parts[2] == "L" ? "Left" : parts[2] == "R" ? "Right" : "";
            string bone = parts[3] == "ToeBase" ? "Toes" : parts[3];
            foreach (var (f, uf) in new[] { ("Thumb", "Thumb"), ("Index", "Index"), ("Middle", "Middle"), ("Ring", "Ring"), ("Little", "Little") })
                if (bone.StartsWith(f) && bone.Length == f.Length + 1)
                    bone = uf + (f == "Thumb" ? bone[^1] switch { '1' => "Proximal", '2' => "Intermediate", _ => "Distal" }
                                              : bone[^1] switch { '1' => "Proximal", '2' => "Intermediate", _ => "Distal" });
            return Enum.TryParse<HumanBodyBones>(side + bone, true, out var r) ? r : null;
        }
        // Mixamo
        string s2 = n.StartsWith("Left") ? "Left" : n.StartsWith("Right") ? "Right" : "";
        var rest = n.Substring(s2.Length);
        string mapped = rest switch
        {
            "Hips" => "Hips", "Spine" => "Spine", "Spine1" => "Chest", "Spine2" => "UpperChest", "Neck" => "Neck", "Head" => "Head",
            "Shoulder" => "Shoulder", "Arm" => "UpperArm", "ForeArm" => "LowerArm", "Hand" => "Hand",
            "UpLeg" => "UpperLeg", "Leg" => "LowerLeg", "Foot" => "Foot", "ToeBase" => "Toes",
            _ => null,
        };
        if (mapped == null && rest.StartsWith("Hand") && rest.Length > 5 && char.IsDigit(rest[^1]))
        {
            var finger = rest.Substring(4, rest.Length - 5);
            finger = finger == "Pinky" ? "Little" : finger;
            mapped = finger + (rest[^1] switch { '1' => "Proximal", '2' => "Intermediate", _ => "Distal" });
        }
        if (mapped == null) return null;
        return Enum.TryParse<HumanBodyBones>(s2 + mapped, true, out var r2) ? r2 : null;
    }

    private static float[] Convert(float[] v, int kind)
    {
        var r = new float[v.Length];
        if (kind == 0)
            for (int i = 0; i + 2 < v.Length; i += 3) { r[i] = -v[i]; r[i + 1] = v[i + 1]; r[i + 2] = v[i + 2]; }
        else
            for (int i = 0; i + 3 < v.Length; i += 4) { r[i] = v[i]; r[i + 1] = -v[i + 1]; r[i + 2] = -v[i + 2]; r[i + 3] = v[i + 3]; }
        return r;
    }

    /// <summary>値が時間で変わるか、基準の姿勢と違うか</summary>
    private static bool Changes(Channel c, GltfMotion m)
    {
        int dim = c.Path == 0 ? 3 : 4;
        float[] rest = c.Path == 0
            ? new[] { m.RestPos[c.Node].x, m.RestPos[c.Node].y, m.RestPos[c.Node].z }
            : new[] { m.RestRot[c.Node].x, m.RestRot[c.Node].y, m.RestRot[c.Node].z, m.RestRot[c.Node].w };
        for (int k = 0; k < c.Values.Length / dim; k++)
        {
            float diff = 0f, flip = 0f;
            for (int d = 0; d < dim; d++)
            {
                diff = Mathf.Max(diff, Mathf.Abs(c.Values[k * dim + d] - rest[d]));
                flip = Mathf.Max(flip, Mathf.Abs(c.Values[k * dim + d] + rest[d])); // 回転は符号が逆でも同じ向き
            }
            if (Mathf.Min(diff, c.Path == 1 ? flip : diff) > 1e-4f) return true;
        }
        return false;
    }

    /// <summary>時間 t の各骨の位置・回転 (親から見た値)。動きの無い骨は基準の姿勢</summary>
    public void Sample(float t, Vector3[] pos, Quaternion[] rot)
    {
        Array.Copy(RestPos, pos, pos.Length);
        Array.Copy(RestRot, rot, rot.Length);
        foreach (var c in _channels)
        {
            var times = c.Times;
            int last = times.Length - 1;
            int k = 0;
            float f = 0f;
            if (t <= times[0]) k = 0;
            else if (t >= times[last]) k = last;
            else
            {
                int lo = 0, hi = last;
                while (hi - lo > 1) { int mid = (lo + hi) / 2; if (times[mid] <= t) lo = mid; else hi = mid; }
                k = lo;
                f = c.Step ? 0f : (t - times[lo]) / Mathf.Max(1e-6f, times[hi] - times[lo]);
            }
            int k2 = Mathf.Min(k + 1, last);
            var v = c.Values;
            if (c.Path == 0)
            {
                var a = new Vector3(v[k * 3], v[k * 3 + 1], v[k * 3 + 2]);
                var b = new Vector3(v[k2 * 3], v[k2 * 3 + 1], v[k2 * 3 + 2]);
                pos[c.Node] = Vector3.LerpUnclamped(a, b, f);
            }
            else
            {
                var a = new Quaternion(v[k * 4], v[k * 4 + 1], v[k * 4 + 2], v[k * 4 + 3]);
                var b = new Quaternion(v[k2 * 4], v[k2 * 4 + 1], v[k2 * 4 + 2], v[k2 * 4 + 3]);
                rot[c.Node] = Quaternion.Slerp(a, b, f);
            }
        }
    }

    /// <summary>
    /// 当たる瞬間の見当: 手 (右・左) が一番速く動く時間 (秒)。振り抜きの瞬間に近い。
    /// 人型の対応が無ければ、ゲームの骨格の手 (Bip001 R Hand など)
    /// </summary>
    public float AutoHit => _autoHit ??= SuggestHit();
    private float? _autoHit;

    public float SuggestHit()
    {
        var hands = new List<int>();
        foreach (var hb in new[] { HumanBodyBones.RightHand, HumanBodyBones.LeftHand })
            if (Human.TryGetValue(hb, out var i)) hands.Add(i);
        if (hands.Count == 0)
            for (int i = 0; i < Names.Length; i++)
                if (Names[i] == "Bip001 R Hand" || Names[i] == "Bip001 L Hand") hands.Add(i);
        if (hands.Count == 0 || Length <= 0f) return Length * 0.3f;

        int n = Names.Length;
        var lp = new Vector3[n]; var lr = new Quaternion[n]; var wp = new Vector3[n]; var wr = new Quaternion[n];
        // 腰からの位置で比べる (体ごと動く分は除く)
        Human.TryGetValue(HumanBodyBones.Hips, out var hips);
        const float step = 1f / 60f;
        float best = -1f, bestT = Length * 0.3f;
        var prev = new Vector3[hands.Count];
        for (float t = 0f; t <= Length; t += step)
        {
            Sample(t, lp, lr);
            World(lp, lr, wp, wr);
            for (int k = 0; k < hands.Count; k++)
            {
                var pos = wp[hands[k]] - (Human.ContainsKey(HumanBodyBones.Hips) ? wp[hips] : Vector3.zero);
                if (t > 0f)
                {
                    float speed = (pos - prev[k]).magnitude; // 位置は大きさを掛けた後なので、どの骨格でもメートル
                    if (speed > best) { best = speed; bestT = t; }
                }
                prev[k] = pos;
            }
        }
        return bestT;
    }

    /// <summary>各骨の根元の空間での位置・回転 (親から順に掛ける)</summary>
    public void World(Vector3[] localPos, Quaternion[] localRot, Vector3[] worldPos, Quaternion[] worldRot)
    {
        var done = new bool[Names.Length];
        _worldScale ??= new float[Names.Length];
        void Calc(int i)
        {
            if (done[i]) return;
            int p = Parent[i];
            if (p < 0)
            {
                worldPos[i] = localPos[i];
                worldRot[i] = localRot[i];
                _worldScale[i] = RestScale[i];
            }
            else
            {
                Calc(p);
                worldPos[i] = worldPos[p] + worldRot[p] * (localPos[i] * _worldScale[p]);
                worldRot[i] = worldRot[p] * localRot[i];
                _worldScale[i] = _worldScale[p] * RestScale[i];
            }
            done[i] = true;
        }
        for (int i = 0; i < Names.Length; i++) Calc(i);
    }
}
