using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RusK.Mods.Motion;

/// <summary>
/// glb の動きを、ゲームのキャラの骨に写す。骨は名前 (パス) で対応させ、
/// 「glb の骨が基準の姿勢から回った分」(根元の空間) を、ゲームの骨の基準の姿勢に掛ける。
/// Blender で骨の向きが変わっていても、基準からの差で写すので同じ動きになる。
/// 上書きするのはキーが動いている骨だけ。その子の骨はゲームの動き (親から見た向き) のまま付いていくので、
/// 「手を振る」のような一部だけの動きも、ほかの部分はゲームの動きのまま残る。
/// ゲームの基準の姿勢は、メッシュを骨に付けたときの姿勢 (バインドポーズ。Model Lab の骨格の書き出しと同じ)
/// </summary>
internal sealed class MotionPlayer
{
    private readonly struct Pair
    {
        public readonly Transform Bone;
        public readonly int Node;
        public readonly Quaternion DstRest;
        public readonly Vector3 DstRestPos;
        public readonly bool Move;

        public Pair(Transform bone, int node, Quaternion dstRest, Vector3 dstRestPos, bool move)
        {
            Bone = bone; Node = node; DstRest = dstRest; DstRestPos = dstRestPos; Move = move;
        }
    }

    public readonly Transform Root;
    public readonly GltfMotion Motion;
    public bool Loop = true;
    public float Time;

    private readonly Pair[] _pairs;
    private readonly Quaternion[] _srcRestInv;
    private readonly Vector3[] _srcRestPos;
    private readonly Vector3[] _lp, _wp;
    private readonly Quaternion[] _lr, _wr;

    public int PairCount => _pairs.Length;

    public MotionPlayer(Transform root, GltfMotion motion)
    {
        Root = root;
        Motion = motion;
        int n = motion.Names.Length;
        _lp = new Vector3[n]; _wp = new Vector3[n];
        _lr = new Quaternion[n]; _wr = new Quaternion[n];

        // glb の基準の姿勢 (根元の空間)
        motion.World(motion.RestPos, motion.RestRot, _wp, _wr);
        _srcRestInv = _wr.Select(Quaternion.Inverse).ToArray();
        _srcRestPos = (Vector3[])_wp.Clone();

        // ゲームの骨: 根元からのパスで探す (同じ名前の骨が別の場所にあることがある)
        var byPath = new Dictionary<string, Transform>();
        var byName = new Dictionary<string, Transform>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == root) continue;
            byPath[PathOf(t, root)] = t;
            if (!byName.ContainsKey(t.name)) byName[t.name] = t;
        }
        var rest = GameRest(root);

        var pairs = new List<(Pair pair, int depth)>();
        for (int i = 0; i < n; i++)
        {
            if (!motion.Keyed[i]) continue;
            if (!byPath.TryGetValue(SrcPath(i), out var bone) && !byName.TryGetValue(motion.Names[i], out bone)) continue;
            var r = Rest(bone, root, rest);
            pairs.Add((new Pair(bone, i, r.rotation, new Vector3(r.m03, r.m13, r.m23), motion.Moves[i]), Depth(bone, root)));
        }
        // 親から順に写す (子の向きは親を動かした後に決める)
        _pairs = pairs.OrderBy(p => p.depth).Select(p => p.pair).ToArray();
    }

    private string SrcPath(int i)
    {
        var s = Motion.Names[i];
        for (int p = Motion.Parent[i]; p >= 0; p = Motion.Parent[p]) s = Motion.Names[p] + "/" + s;
        return s;
    }

    private static string PathOf(Transform t, Transform root)
    {
        var s = t.name;
        for (var p = t.parent; p != null && p != root; p = p.parent) s = p.name + "/" + s;
        return s;
    }

    private static int Depth(Transform t, Transform root)
    {
        int d = 0;
        for (var p = t; p != null && p != root; p = p.parent) d++;
        return d;
    }

    /// <summary>ゲームの骨の基準の姿勢 (メッシュに使われている骨はバインドポーズ。根元の空間)</summary>
    private static Dictionary<IntPtr, Matrix4x4> GameRest(Transform root)
    {
        var bind = new Dictionary<IntPtr, Matrix4x4>();
        var rootW2L = root.worldToLocalMatrix;
        foreach (var smr in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            bool equip = false;
            for (var c = smr.transform; c != null && c != root; c = c.parent)
                if (c.name.StartsWith("WeaponHolder") || c.name.StartsWith("Equip_") || c.name.StartsWith("RusK_")) equip = true;
            if (equip) continue;
            var bones = smr.bones;
            var poses = smr.sharedMesh?.bindposes;
            if (bones == null || poses == null) continue;
            var smrToRoot = rootW2L * smr.transform.localToWorldMatrix;
            for (int i = 0; i < bones.Length && i < poses.Length; i++)
                if (bones[i] != null && !bind.ContainsKey(bones[i].Pointer))
                    bind[bones[i].Pointer] = smrToRoot * poses[i].inverse;
        }
        return bind;
    }

    private static Matrix4x4 Rest(Transform t, Transform root, Dictionary<IntPtr, Matrix4x4> bind)
    {
        if (bind.TryGetValue(t.Pointer, out var b)) return b;
        if (t.parent == null || t.parent == root) return Matrix4x4.TRS(t.localPosition, t.localRotation, Vector3.one);
        var r = Rest(t.parent, root, bind) * Matrix4x4.TRS(t.localPosition, t.localRotation, Vector3.one);
        bind[t.Pointer] = r;
        return r;
    }

    /// <summary>時間を進めて、今の姿勢をゲームの骨に写す</summary>
    public void Apply(float dt)
    {
        if (Root == null) return;
        Time += dt;
        float len = Mathf.Max(Motion.Length, 1e-3f);
        float t = Loop ? Time % len : Mathf.Min(Time, len);
        Motion.Sample(t, _lp, _lr);
        Motion.World(_lp, _lr, _wp, _wr);
        var rootRot = Root.rotation;
        foreach (var p in _pairs)
        {
            if (p.Bone == null) continue;
            var delta = _wr[p.Node] * _srcRestInv[p.Node];
            p.Bone.rotation = rootRot * delta * p.DstRest;
            if (p.Move) p.Bone.position = Root.TransformPoint(p.DstRestPos + (_wp[p.Node] - _srcRestPos[p.Node]));
        }
    }
}

/// <summary>再生中の動き (キャラの根元ごと)。毎フレーム、ゲームのアニメーションの後・VRM に写す前に呼ぶ</summary>
internal static class MotionPlayers
{
    public static readonly Dictionary<IntPtr, MotionPlayer> Active = new();
    private static int _lastFrame = -1;

    public static MotionPlayer Play(Transform root, GltfMotion motion)
    {
        var player = new MotionPlayer(root, motion);
        Active[root.Pointer] = player;
        MotionMod.Ctx?.Log.Info($"Motion: {root.name} で '{motion.Name}' を再生 (動かす骨 {player.PairCount}、長さ {motion.Length:0.00} 秒)");
        return player;
    }

    public static void Stop(Transform root)
    {
        if (root != null) Active.Remove(root.Pointer);
    }

    public static void StopAll() => Active.Clear();

    public static void Tick()
    {
        if (UnityEngine.Time.frameCount == _lastFrame || Active.Count == 0) return;
        _lastFrame = UnityEngine.Time.frameCount;
        float dt = UnityEngine.Time.deltaTime;
        foreach (var key in Active.Keys.ToList())
        {
            var p = Active[key];
            try
            {
                if (p.Root == null) { Active.Remove(key); continue; }
                if (!p.Root.gameObject.activeInHierarchy) continue;
                p.Apply(dt);
            }
            catch (Exception e)
            {
                MotionMod.Ctx?.Log.Warning($"Motion: 動きを写せません: {e.Message}");
                Active.Remove(key);
            }
        }
    }
}
