using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using UnityEngine;

namespace RusK.Mods.Model.Vrm;

/// <summary>
/// VRM の揺れ物 (SpringBone)。髪やスカートの骨を、硬さ・重力・空気抵抗・当たり判定の設定に従って揺らす。
/// 計算は VRM の標準実装 (UniVRM の VRMSpringBoneLogic) と同じ考え方:
///   骨の先 (tail) の位置を覚えておき、慣性・元の向きへ戻る力・重力で動かしてから、骨の長さに戻し、
///   当たり判定 (球・カプセル) にめり込んだら押し出して、骨をその方向へ向ける。
/// </summary>
internal sealed class SpringBoneSystem
{
    internal sealed class Collider
    {
        public Transform Node;
        public Vector3 Offset;
        public Vector3 Tail;     // カプセルのもう一方の端 (球なら Offset と同じ)
        public bool Capsule;
        public float Radius;
    }

    private sealed class Joint
    {
        public Transform Bone;
        public bool Skirt;                // 腰の下の揺れ物 (スカートなど)。当たり判定の太さの調整はこれだけに掛ける
        public Quaternion LocalRotation0; // 基準の向き (親から見た)
        public Vector3 BoneAxis;          // 骨の向き (自分の空間)
        public float Length;
        public Vector3 CurrentTail, PrevTail;
        public float Stiffness, GravityPower, Drag, HitRadius;
        public Vector3 GravityDir;
        public List<Collider> Colliders;
    }

    private readonly List<Joint> _joints = new();
    private Transform _root;
    private Vector3 _lastRootPos;

    public int JointCount => _joints.Count;

    /// <summary>揺れ物の列の最初の骨 (親が揺れ物ではない骨)</summary>
    public IEnumerable<Transform> Roots
    {
        get
        {
            var set = new HashSet<Transform>(_joints.Select(j => j.Bone));
            return _joints.Where(j => j.Bone.parent == null || !set.Contains(j.Bone.parent)).Select(j => j.Bone).ToList();
        }
    }

    /// <summary>1 フレームを何回に分けて計算するか (スカートが当たり判定をすり抜けないように)</summary>
    public const int SubSteps = 2;


    /// <summary>空の揺れ物 (PMX の剛体から組み立てる用。Add で骨を足す)</summary>
    public static SpringBoneSystem Create(Transform root) => new() { _root = root };

    /// <summary>VRM の設定から揺れ物を組み立てる (モデルが基準の姿勢のうちに呼ぶ)</summary>
    public static SpringBoneSystem Read(JsonElement ext, VrmModel model, bool flipX)
    {
        var sys = new SpringBoneSystem { _root = model.Root.transform };
        var nodes = model.Nodes;
        // VRM 0.x の揺れ物の値 (当たり判定の位置・重力の向き) は Unity の座標のまま保存されている (UniVRM も変換せずに読む)。
        // メッシュと同じく前後を反転すると、頭の当たり判定が前に出て前髪がおでこから跳ねる。VRM 1.0 は glTF の座標なので変換する
        Vector3 P(float x, float y, float z) => model.Version == 0 ? new Vector3(x, y, z) : VrmLoader.Pos(x, y, z, flipX);
        Vector3 V(JsonElement e) => e.ValueKind == JsonValueKind.Array
            ? P(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle())
            : P(e.GetProperty("x").GetSingle(), e.GetProperty("y").GetSingle(), e.GetProperty("z").GetSingle());
        float F(JsonElement e, string name, float def) => e.TryGetProperty(name, out var v) ? v.GetSingle() : def;

        if (model.Version == 0)
        {
            if (!ext.GetProperty("VRM").TryGetProperty("secondaryAnimation", out var sec)) return sys;

            var groups = new List<List<Collider>>();
            if (sec.TryGetProperty("colliderGroups", out var cgs))
                foreach (var cg in cgs.EnumerateArray())
                {
                    var node = nodes[cg.GetProperty("node").GetInt32()];
                    var list = new List<Collider>();
                    foreach (var c in cg.GetProperty("colliders").EnumerateArray())
                    {
                        var off = V(c.GetProperty("offset"));
                        list.Add(new Collider { Node = node, Offset = off, Tail = off, Radius = F(c, "radius", 0f) });
                    }
                    groups.Add(list);
                }

            if (sec.TryGetProperty("boneGroups", out var bgs))
                foreach (var bg in bgs.EnumerateArray())
                {
                    var colliders = new List<Collider>();
                    if (bg.TryGetProperty("colliderGroups", out var refs))
                        foreach (var r in refs.EnumerateArray())
                            if (r.GetInt32() < groups.Count) colliders.AddRange(groups[r.GetInt32()]);

                    float stiffness = F(bg, "stiffiness", 1f); // VRM 0.x の綴り (stiffiness)
                    float gravity = F(bg, "gravityPower", 0f);
                    var gravityDir = bg.TryGetProperty("gravityDir", out var gd) ? V(gd) : Vector3.down;
                    float drag = F(bg, "dragForce", 0.4f);
                    float hit = F(bg, "hitRadius", 0.02f);

                    // 指定された骨から下を全部揺らす
                    foreach (var b in bg.GetProperty("bones").EnumerateArray())
                        sys.AddSubtree(nodes[b.GetInt32()], stiffness, gravity, gravityDir, drag, hit, colliders);
                }
        }
        else
        {
            if (!model.Root.transform || !TryGet(ext, "VRMC_springBone", out var sb)) return sys;

            var colliders = new List<Collider>();
            if (sb.TryGetProperty("colliders", out var cs))
                foreach (var c in cs.EnumerateArray())
                {
                    var node = nodes[c.GetProperty("node").GetInt32()];
                    var shape = c.GetProperty("shape");
                    if (shape.TryGetProperty("sphere", out var sp))
                    {
                        var off = sp.TryGetProperty("offset", out var o) ? V(o) : Vector3.zero;
                        colliders.Add(new Collider { Node = node, Offset = off, Tail = off, Radius = F(sp, "radius", 0f) });
                    }
                    else if (shape.TryGetProperty("capsule", out var cp))
                    {
                        colliders.Add(new Collider
                        {
                            Node = node, Capsule = true, Radius = F(cp, "radius", 0f),
                            Offset = cp.TryGetProperty("offset", out var o) ? V(o) : Vector3.zero,
                            Tail = cp.TryGetProperty("tail", out var t) ? V(t) : Vector3.zero,
                        });
                    }
                    else colliders.Add(new Collider { Node = node });
                }

            var groups = new List<List<Collider>>();
            if (sb.TryGetProperty("colliderGroups", out var cgs))
                foreach (var g in cgs.EnumerateArray())
                    groups.Add(g.GetProperty("colliders").EnumerateArray().Select(i => colliders[i.GetInt32()]).ToList());

            if (sb.TryGetProperty("springs", out var springs))
                foreach (var spring in springs.EnumerateArray())
                {
                    var cols = new List<Collider>();
                    if (spring.TryGetProperty("colliderGroups", out var refs))
                        foreach (var r in refs.EnumerateArray())
                            if (r.GetInt32() < groups.Count) cols.AddRange(groups[r.GetInt32()]);

                    // VRM 1.0 は揺れる骨の並びを直接書く。最後の骨は先端 (揺らさない)
                    var joints = spring.GetProperty("joints").EnumerateArray().ToList();
                    for (int i = 0; i < joints.Count - 1; i++)
                    {
                        var j = joints[i];
                        var bone = nodes[j.GetProperty("node").GetInt32()];
                        var tail = nodes[joints[i + 1].GetProperty("node").GetInt32()];
                        sys.Add(bone, bone.InverseTransformPoint(tail.position), F(j, "stiffness", 1f), F(j, "gravityPower", 0f),
                            j.TryGetProperty("gravityDir", out var gd) ? V(gd) : Vector3.down,
                            F(j, "dragForce", 0.5f), F(j, "hitRadius", 0f), cols);
                    }
                }
        }
        return sys;
    }

    private static bool TryGet(JsonElement e, string name, out JsonElement v)
    {
        v = default;
        return e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out v);
    }

    /// <summary>root から下の骨を全部揺らす (VRM 0.x)。子のない骨は、親からの向きに 7cm 伸ばした先を先端にする</summary>
    private void AddSubtree(Transform root, float stiffness, float gravity, Vector3 gravityDir, float drag, float hit,
        List<Collider> colliders)
    {
        var stack = new Stack<Transform>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var t = stack.Pop();
            Vector3 localTail;
            if (t.childCount > 0) localTail = t.GetChild(0).localPosition;
            else
            {
                var dir = t.parent != null ? (t.position - t.parent.position) : Vector3.up;
                localTail = t.InverseTransformPoint(t.position + dir.normalized * 0.07f);
            }
            Add(t, localTail, stiffness, gravity, gravityDir, drag, hit, colliders);
            for (int i = t.childCount - 1; i >= 0; i--) stack.Push(t.GetChild(i));
        }
    }

    internal void Add(Transform bone, Vector3 localTail, float stiffness, float gravity, Vector3 gravityDir, float drag,
        float hit, List<Collider> colliders)
    {
        if (_joints.Any(j => j.Bone == bone)) return;
        var tail = bone.TransformPoint(localTail);
        float length = (tail - bone.position).magnitude;
        if (length < 1e-5f) return;
        _joints.Add(new Joint
        {
            Bone = bone,
            LocalRotation0 = bone.localRotation,
            BoneAxis = localTail.normalized,
            Length = length,
            CurrentTail = tail,
            PrevTail = tail,
            Stiffness = stiffness,
            GravityPower = gravity,
            GravityDir = gravityDir.sqrMagnitude > 0f ? gravityDir.normalized : Vector3.down,
            Drag = drag,
            HitRadius = hit,
            Colliders = colliders,
        });
    }

    /// <summary>
    /// 脚全体 (太もも: 腰～膝、すね: 膝～足首) にカプセルの当たり判定を足す。
    /// VRoid のスカートの当たり判定は太ももの付け根だけのことが多く、ゲームの走りのように脚を大きく振ると
    /// 膝から下が裾をすり抜ける。腰の下にある揺れ物 (スカートなど) だけに足し、髪などには影響させない
    /// </summary>
    public int AddLegColliders(Dictionary<HumanBodyBones, Transform> human)
    {
        if (!human.TryGetValue(HumanBodyBones.Hips, out var hips)) return 0;
        var legs = new List<Collider>();
        foreach (var (upper, lower, foot) in new[]
                 {
                     (HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot),
                     (HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot),
                 })
        {
            if (!human.TryGetValue(upper, out var u) || !human.TryGetValue(lower, out var l) || !human.TryGetValue(foot, out var f))
                continue;
            float legLength = (u.position - l.position).magnitude + (l.position - f.position).magnitude;
            // 太さは脚の長さから見積もる (脚の長さ 0.75m なら太ももの半径 約 7cm、すね 約 5cm。当たり判定の倍率 1.15 は計算時に掛かる)。Radius は骨の空間の大きさで持つ
            legs.Add(new Collider
            {
                Node = u, Capsule = true, Offset = Vector3.zero, Tail = l.localPosition,
                Radius = legLength * 0.09f / Scale(u),
            });
            legs.Add(new Collider
            {
                Node = l, Capsule = true, Offset = Vector3.zero, Tail = f.localPosition,
                Radius = legLength * 0.065f / Scale(l),
            });
        }
        if (legs.Count == 0) return 0;

        human.TryGetValue(HumanBodyBones.Spine, out var spine);
        human.TryGetValue(HumanBodyBones.LeftUpperLeg, out var lu);
        human.TryGetValue(HumanBodyBones.RightUpperLeg, out var ru);
        int count = 0;
        foreach (var j in _joints)
        {
            var b = j.Bone;
            bool underHips = b.IsChildOf(hips) && b != hips;
            bool other = (spine != null && b.IsChildOf(spine)) || (lu != null && b.IsChildOf(lu)) || (ru != null && b.IsChildOf(ru));
            if (!underHips || other) continue;
            // 当たり判定のリストは骨の組で共有されていることがあるので、骨ごとに新しいリストにする
            j.Colliders = new List<Collider>(j.Colliders);
            j.Colliders.AddRange(legs);
            j.Skirt = true;
            count++;
        }
        return count;
    }

    /// <summary>先端の位置を、今の姿勢に合わせてやり直す (ワープしたときなど)</summary>
    public void Reset()
    {
        foreach (var j in _joints)
        {
            j.Bone.localRotation = j.LocalRotation0;
            j.CurrentTail = j.PrevTail = j.Bone.TransformPoint(j.BoneAxis * (j.Length / Scale(j.Bone)));
        }
        if (_root != null) _lastRootPos = _root.position;
    }

    private static float Scale(Transform t) => Mathf.Max(1e-5f, t.lossyScale.x);

    /// <summary>毎フレーム (モデルの骨に動きを写した後) 呼ぶ</summary>
    public void Update(float dt)
    {
        if (_joints.Count == 0 || _root == null) return;

        // ワープ (ステージの移動・大きく飛ぶ演出) のときは揺れを持ち越さない
        if ((_root.position - _lastRootPos).sqrMagnitude > 4f) Reset();
        _lastRootPos = _root.position;
        if (dt <= 0f) return;
        dt = Mathf.Min(dt, 1f / 30f) / SubSteps;
        for (int step = 0; step < SubSteps; step++) Step(dt);
    }

    private void Step(float dt)
    {
        foreach (var j in _joints)
        {
            var bone = j.Bone;
            var parentRot = bone.parent != null ? bone.parent.rotation : Quaternion.identity;
            var restRot = parentRot * j.LocalRotation0;
            var pos = bone.position;

            // 慣性 + 元の向きへ戻る力 + 重力
            var next = j.CurrentTail
                       + (j.CurrentTail - j.PrevTail) * (1f - j.Drag)
                       + restRot * j.BoneAxis * (j.Stiffness * dt)
                       + j.GravityDir * (j.GravityPower * dt);
            next = pos + (next - pos).normalized * j.Length;

            // 当たり判定から押し出す。太さの調整はスカートだけ (髪に掛けると頭の当たり判定が太って髪が跳ねる)
            float colliderScale = j.Skirt ? SkirtTuning.Collider : 1f;
            foreach (var c in j.Colliders)
            {
                if (c.Node == null) continue;
                float scale = Scale(c.Node);
                float r = j.HitRadius + c.Radius * scale * colliderScale;
                var a = c.Node.TransformPoint(c.Offset);
                var center = a;
                if (c.Capsule)
                {
                    var b = c.Node.TransformPoint(c.Tail);
                    var ab = b - a;
                    float t = ab.sqrMagnitude > 0f ? Mathf.Clamp01(Vector3.Dot(next - a, ab) / ab.sqrMagnitude) : 0f;
                    center = a + ab * t;
                }
                var d = next - center;
                if (d.sqrMagnitude < r * r && d.sqrMagnitude > 1e-10f)
                {
                    next = center + d.normalized * r;
                    next = pos + (next - pos).normalized * j.Length;
                }
            }

            j.PrevTail = j.CurrentTail;
            j.CurrentTail = next;
            bone.rotation = Quaternion.FromToRotation(restRot * j.BoneAxis, next - pos) * restRot;
        }
    }
}
