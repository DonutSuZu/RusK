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
/// 上書きした骨の「お供の骨」(ねじれ用の骨・腕のバンドなど、子ではないのに一緒に動く骨) も同じだけ回す。
/// ゲームのアニメーションはお供の骨も一緒に動かしているが、glb の動きは上書きした骨しか動かさないため
/// (粉光の上腕のねじれの骨は肩の子なので、腕を上げると袖が待機の位置に残って伸びていた)
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
    /// <summary>画面から手で再生した (割り当ての自動の出し入れで止めない)</summary>
    public bool Manual;

    /// <summary>今の混ざり具合 (0 = ゲームの動き、1 = glb の動き)。Target に向かって FadeTime 秒で変わる</summary>
    public float Weight;
    public float Target = 1f;
    public float FadeTime = 0.25f;
    /// <summary>消えきった (止めて、フェードが終わった)</summary>
    public bool Finished => Target <= 0f && Weight <= 0f;

    private readonly Pair[] _pairs;
    /// <summary>お供の骨と、付いていく相手 (_pairs の番号)</summary>
    private readonly (Transform bone, int driver)[] _followers;
    private readonly Quaternion[] _oldRot;
    private readonly Vector3[] _oldPos;
    private readonly Quaternion[] _srcRestInv;
    private readonly Vector3[] _srcRestPos;
    private readonly Vector3[] _lp, _wp;
    private readonly Quaternion[] _lr, _wr;

    public int PairCount => _pairs.Length;
    public int FollowerCount => _followers.Length;
    public string FollowerNames => string.Join(", ", _followers.Select(f => $"{f.bone.name}→{_pairs[f.driver].Bone.name}"));

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
        _oldRot = new Quaternion[_pairs.Length];
        _oldPos = new Vector3[_pairs.Length];
        _followers = FindFollowers(root, rest);
    }

    /// <summary>ねじれ用の骨の名前 → 付いていく骨の名前</summary>
    private static readonly (string twist, string driver)[] TwistNames =
    {
        ("UpArmTwist", "UpperArm"), ("ForeTwist", "Forearm"), ("ThighTwist", "Thigh"), ("CalfTwist", "Calf"),
    };

    /// <summary>
    /// お供の骨を探す: 上書きする骨の子ではない骨のうち、
    /// 名前がねじれ用の骨 (Bip001 RUpArmTwist → Bip001 R UpperArm など) か、
    /// 基準の姿勢で上書きする骨の線 (骨の根元 → 子の根元) のすぐ近くにある骨 (腕のバンドなど)
    /// </summary>
    private (Transform, int)[] FindFollowers(Transform root, Dictionary<IntPtr, Matrix4x4> rest)
    {
        var result = new List<(Transform, int)>();
        if (_pairs.Length == 0) return result.ToArray();
        var keyed = new HashSet<IntPtr>(_pairs.Select(p => p.Bone.Pointer));
        bool UnderKeyed(Transform t)
        {
            for (var p = t; p != null && p != root; p = p.parent)
                if (keyed.Contains(p.Pointer)) return true;
            return false;
        }
        Vector3 RestPos(Transform t)
        {
            var m = Rest(t, root, rest);
            return new Vector3(m.m03, m.m13, m.m23);
        }
        // 上書きする骨の線 (子の骨の根元まで)
        var segments = new List<(int pair, Vector3 a, Vector3 b)>();
        for (int i = 0; i < _pairs.Length; i++)
        {
            var bone = _pairs[i].Bone;
            var a = RestPos(bone);
            for (int c = 0; c < bone.childCount; c++)
            {
                var child = bone.GetChild(c);
                if (!child.name.StartsWith("Bip001")) continue;
                segments.Add((i, a, RestPos(child)));
            }
        }

        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == root || keyed.Contains(t.Pointer)) continue;
            if (t.parent != null && result.Any(f => f.Item1 == t.parent)) continue; // お供の子はお供に付いていく
            int driver = -1;
            // ねじれ用の骨は名前で (上書きする骨の子でも。Bip001 R ForeTwist は上腕の子だが、前腕に付いていく)
            char side = t.name.StartsWith("Bip001 ") && t.name.Length > 7 ? t.name[7] : '?';
            foreach (var (twist, target) in TwistNames)
            {
                if (!t.name.Contains(twist)) continue;
                for (int i = 0; i < _pairs.Length && driver < 0; i++)
                {
                    var name = _pairs[i].Bone.name;
                    if (name.EndsWith(target) && name.Length > 7 && name[7] == side && !t.IsChildOf(_pairs[i].Bone)) driver = i;
                }
                break;
            }
            if (driver < 0 && UnderKeyed(t)) continue;
            if (driver < 0 && !t.name.StartsWith("Bip001") && !t.name.StartsWith("WeaponHolder") && segments.Count > 0)
            {
                // 骨の線からの距離 (根元から先の間にあるものだけ)
                var pos = RestPos(t);
                float best = 0.05f;
                foreach (var (pair, a, b) in segments)
                {
                    var ab = b - a;
                    float len2 = ab.sqrMagnitude;
                    if (len2 < 1e-6f) continue;
                    float u = Vector3.Dot(pos - a, ab) / len2;
                    if (u < 0f || u > 1f) continue;
                    // 親が上書きする骨の親 (兄弟の枝) か、その上にある骨だけ
                    if (t.parent == null || !_pairs[pair].Bone.IsChildOf(t.parent)) continue;
                    float d = (a + ab * u - pos).magnitude;
                    if (d < best) { best = d; driver = pair; }
                }
            }
            if (driver >= 0) result.Add((t, driver));
        }
        return result.ToArray();
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
        Weight = Mathf.MoveTowards(Weight, Target, FadeTime > 0f ? dt / FadeTime : 1f);
        if (Weight <= 0f) return;
        // ゆっくり始まりゆっくり終わる混ぜ方
        float w = Weight * Weight * (3f - 2f * Weight);
        float len = Mathf.Max(Motion.Length, 1e-3f);
        float t = Loop ? Time % len : Mathf.Min(Time, len);
        Motion.Sample(t, _lp, _lr);
        Motion.World(_lp, _lr, _wp, _wr);
        var rootRot = Root.rotation;
        for (int i = 0; i < _pairs.Length; i++)
        {
            var p = _pairs[i];
            if (p.Bone == null) continue;
            _oldRot[i] = p.Bone.rotation;
            _oldPos[i] = p.Bone.position;
            var delta = _wr[p.Node] * _srcRestInv[p.Node];
            var rot = rootRot * delta * p.DstRest;
            p.Bone.rotation = w >= 1f ? rot : Quaternion.Slerp(_oldRot[i], rot, w);
            if (p.Move)
            {
                var pos = Root.TransformPoint(p.DstRestPos + (_wp[p.Node] - _srcRestPos[p.Node]));
                p.Bone.position = w >= 1f ? pos : Vector3.Lerp(_oldPos[i], pos, w);
            }
        }
        // お供の骨: 付いていく骨を動かした分 (ゲームの姿勢から上書きした姿勢へ) だけ、同じ点を中心に動かす
        foreach (var (bone, driver) in _followers)
        {
            var d = _pairs[driver].Bone;
            if (bone == null || d == null) continue;
            var corr = d.rotation * Quaternion.Inverse(_oldRot[driver]);
            bone.position = d.position + corr * (bone.position - _oldPos[driver]);
            bone.rotation = corr * bone.rotation;
        }
    }
}

/// <summary>再生中の動き (キャラの根元ごと)。毎フレーム、ゲームのアニメーションの後・VRM に写す前に呼ぶ</summary>
internal static class MotionPlayers
{
    /// <summary>キャラの根元 → 再生中の動き (古い順。前の動きを消しながら次の動きを出すので、切り替えの間は 2 つ)</summary>
    public static readonly Dictionary<IntPtr, List<MotionPlayer>> Active = new();
    private static int _lastFrame = -1;

    public static bool IsPlaying(Transform root) =>
        root != null && Active.TryGetValue(root.Pointer, out var list) && list.Any(p => p.Target > 0f);

    /// <summary>画面から手で再生している動きがある</summary>
    public static bool IsManual(Transform root) =>
        root != null && Active.TryGetValue(root.Pointer, out var list) && list.Any(p => p.Manual && p.Target > 0f);

    public static MotionPlayer Play(Transform root, GltfMotion motion, float fade = 0.25f)
    {
        var player = new MotionPlayer(root, motion) { FadeTime = fade };
        if (!Active.TryGetValue(root.Pointer, out var list)) Active[root.Pointer] = list = new List<MotionPlayer>();
        foreach (var old in list) { old.Target = 0f; old.FadeTime = fade; }
        list.Add(player);
        MotionMod.Ctx?.Log.Info($"Motion: {root.name} で '{motion.Name}' を再生 (動かす骨 {player.PairCount}、お供の骨 {player.FollowerCount}、長さ {motion.Length:0.00} 秒) お供: {player.FollowerNames}");
        return player;
    }

    /// <summary>止める (フェードで消す)</summary>
    public static void Stop(Transform root, float fade = 0.25f)
    {
        if (root == null || !Active.TryGetValue(root.Pointer, out var list)) return;
        foreach (var p in list) { p.Target = 0f; p.FadeTime = fade; }
    }

    public static void StopAll(bool immediately = false)
    {
        if (immediately) { Active.Clear(); return; }
        foreach (var list in Active.Values)
            foreach (var p in list) p.Target = 0f;
    }

    public static void Tick()
    {
        if (UnityEngine.Time.frameCount == _lastFrame) return;
        _lastFrame = UnityEngine.Time.frameCount;
        MotionBinder.Update();
        if (Active.Count == 0) return;
        float dt = UnityEngine.Time.deltaTime;
        foreach (var key in Active.Keys.ToList())
        {
            var list = Active[key];
            try
            {
                if (list.Count == 0 || list[0].Root == null) { Active.Remove(key); continue; }
                if (!list[0].Root.gameObject.activeInHierarchy) continue;
                // 古い順に重ねる (前の動きを薄めた姿勢から、次の動きへ混ぜる)
                foreach (var p in list) p.Apply(dt);
                list.RemoveAll(p => p.Finished);
                if (list.Count == 0) Active.Remove(key);
            }
            catch (Exception e)
            {
                MotionMod.Ctx?.Log.Warning($"Motion: 動きを写せません: {e.Message}");
                Active.Remove(key);
            }
        }
    }
}
