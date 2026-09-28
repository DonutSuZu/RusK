using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RusK.Mods.Model.Vrm;

/// <summary>
/// スカートを脚の動きに合わせて開く。
///
/// VRoid のスカートは、腰のまわりに数本の骨の列 (前・横・後ろ × 左右) があり、腰に近い段は揺れ物ではなく腰に固定されている。
/// そのままだと、ゲームの走りのように脚を大きく振ると、太ももが固定の段にめり込み、脚が列の間を押し分けて切れ目が開く。
/// そこで、揺れ物の列の付け根 (固定の段) を太ももの振りに合わせて回す:
///   前の布は、前に出ている方の脚に合わせて左右そろって前に (真ん中の切れ目が開かないように)
///   後ろの布は、後ろに引いている方の脚に合わせて左右そろって後ろに
///   横の布は、同じ側の脚に弱めに付いていく
/// </summary>
internal sealed class SkirtFollow
{
    private enum Side { Front, Back, Left, Right }

    private sealed class Panel
    {
        public Transform Bone;
        public Quaternion RestInHips; // 腰の空間での基準の向き
        public Side Side;
    }


    private readonly List<Panel> _panels = new();
    private Transform _hips, _leftUpper, _leftLower, _rightUpper, _rightLower;
    private Quaternion _hipsRestInv;
    private Vector3 _leftRestDir, _rightRestDir;

    public int Count => _panels.Count;

    /// <summary>モデルが基準の姿勢のうちに作る。springRoots は揺れ物の列の最初の骨</summary>
    public static SkirtFollow Build(Dictionary<HumanBodyBones, Transform> human, IEnumerable<Transform> springRoots)
    {
        var f = new SkirtFollow();
        if (!human.TryGetValue(HumanBodyBones.Hips, out f._hips)
            || !human.TryGetValue(HumanBodyBones.LeftUpperLeg, out f._leftUpper)
            || !human.TryGetValue(HumanBodyBones.LeftLowerLeg, out f._leftLower)
            || !human.TryGetValue(HumanBodyBones.RightUpperLeg, out f._rightUpper)
            || !human.TryGetValue(HumanBodyBones.RightLowerLeg, out f._rightLower))
            return f;
        human.TryGetValue(HumanBodyBones.Spine, out var spine);

        f._hipsRestInv = Quaternion.Inverse(f._hips.rotation);
        f._leftRestDir = f.LegDir(f._leftUpper, f._leftLower);
        f._rightRestDir = f.LegDir(f._rightUpper, f._rightLower);

        // 揺れ物の列の付け根の親 (腰の下にある固定の段) を集める
        var parents = new HashSet<Transform>();
        var roots = springRoots.ToList();
        foreach (var r in roots)
        {
            var p = r.parent;
            if (p == null || p == f._hips || !p.IsChildOf(f._hips)) continue;
            if ((spine != null && p.IsChildOf(spine)) || p.IsChildOf(f._leftUpper) || p.IsChildOf(f._rightUpper)) continue;
            parents.Add(p);
        }

        foreach (var p in parents)
        {
            // 腰から見た位置で、前・後ろ・左・右に分ける (モデルは +Z が前、+X が右)
            var local = Quaternion.Inverse(f._hips.rotation) * (p.position - f._hips.position);
            var flat = new Vector2(local.x, local.z);
            if (flat.sqrMagnitude < 1e-6f) continue;
            flat.Normalize();
            Side side = flat.y > 0.5f ? Side.Front : flat.y < -0.5f ? Side.Back : flat.x < 0f ? Side.Left : Side.Right;
            f._panels.Add(new Panel { Bone = p, RestInHips = f._hipsRestInv * p.rotation, Side = side });
        }
        return f;
    }

    /// <summary>太ももの向き (腰の空間で、脚の付け根から膝へ)</summary>
    private Vector3 LegDir(Transform upper, Transform lower) =>
        (Quaternion.Inverse(_hips.rotation) * (lower.position - upper.position)).normalized;

    /// <summary>毎フレーム、骨に動きを写した後・揺れ物の前に呼ぶ</summary>
    public void Update()
    {
        if (_panels.Count == 0 || _hips == null) return;

        var leftDir = LegDir(_leftUpper, _leftLower);
        var rightDir = LegDir(_rightUpper, _rightLower);
        var leftSwing = Quaternion.FromToRotation(_leftRestDir, leftDir);
        var rightSwing = Quaternion.FromToRotation(_rightRestDir, rightDir);

        // 前に出ている脚 (膝が前 = +Z が大きい) と、後ろに引いている脚
        bool leftForward = leftDir.z >= rightDir.z;
        var forwardSwing = leftForward ? leftSwing : rightSwing;
        var backSwing = leftForward ? rightSwing : leftSwing;

        foreach (var p in _panels)
        {
            if (p.Bone == null) continue;
            var swing = p.Side switch
            {
                Side.Front => Quaternion.Slerp(Quaternion.identity, forwardSwing, SkirtTuning.FrontBack),
                Side.Back => Quaternion.Slerp(Quaternion.identity, backSwing, SkirtTuning.FrontBack),
                Side.Left => Quaternion.Slerp(Quaternion.identity, leftSwing, SkirtTuning.Side),
                _ => Quaternion.Slerp(Quaternion.identity, rightSwing, SkirtTuning.Side),
            };
            p.Bone.rotation = _hips.rotation * swing * p.RestInHips;
        }
    }
}
