using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RusK.Mods.Model.Vrm;

/// <summary>
/// ゲームのキャラの動きを VRM に写す (リターゲット)。
///
/// Unity の人型の仕組み (AvatarBuilder / HumanPoseHandler) は、このゲーム (IL2CPP) からだと
/// 渡すデータが壊れてクラッシュするので使わず、骨の回転を直接写す:
///   両方のモデルについて、基準の姿勢 (T ポーズ) での骨の向きを覚えておき、毎フレーム
///   「ゲームの骨が基準から回った分」を VRM の同じ役割の骨の基準の向きに掛ける。
///   腰の位置は、腰の高さの比で縮めて写す。
/// ゲームのキャラの基準の姿勢は、体のメッシュのバインドポーズ (メッシュを骨に付けたときの姿勢) から求め、
/// 腕を水平 (T ポーズ) に直す。VRM は仕様で T ポーズが基準。
/// </summary>
internal static class Humanoid
{
    /// <summary>Biped (3ds Max) の骨の名前 → 人型の骨</summary>
    public static readonly (HumanBodyBones bone, string name)[] BipedMap =
    {
        (HumanBodyBones.Hips, "Bip001 Pelvis"),
        (HumanBodyBones.Spine, "Bip001 Spine"),
        (HumanBodyBones.Chest, "Bip001 Spine1"),
        (HumanBodyBones.UpperChest, "Bip001 Spine2"),
        (HumanBodyBones.Neck, "Bip001 Neck"),
        (HumanBodyBones.Head, "Bip001 Head"),
        (HumanBodyBones.LeftShoulder, "Bip001 L Clavicle"),
        (HumanBodyBones.LeftUpperArm, "Bip001 L UpperArm"),
        (HumanBodyBones.LeftLowerArm, "Bip001 L Forearm"),
        (HumanBodyBones.LeftHand, "Bip001 L Hand"),
        (HumanBodyBones.RightShoulder, "Bip001 R Clavicle"),
        (HumanBodyBones.RightUpperArm, "Bip001 R UpperArm"),
        (HumanBodyBones.RightLowerArm, "Bip001 R Forearm"),
        (HumanBodyBones.RightHand, "Bip001 R Hand"),
        (HumanBodyBones.LeftUpperLeg, "Bip001 L Thigh"),
        (HumanBodyBones.LeftLowerLeg, "Bip001 L Calf"),
        (HumanBodyBones.LeftFoot, "Bip001 L Foot"),
        (HumanBodyBones.LeftToes, "Bip001 L Toe0"),
        (HumanBodyBones.RightUpperLeg, "Bip001 R Thigh"),
        (HumanBodyBones.RightLowerLeg, "Bip001 R Calf"),
        (HumanBodyBones.RightFoot, "Bip001 R Foot"),
        (HumanBodyBones.RightToes, "Bip001 R Toe0"),
        (HumanBodyBones.LeftThumbProximal, "Bip001 L Finger0"),
        (HumanBodyBones.LeftThumbIntermediate, "Bip001 L Finger01"),
        (HumanBodyBones.LeftThumbDistal, "Bip001 L Finger02"),
        (HumanBodyBones.LeftIndexProximal, "Bip001 L Finger1"),
        (HumanBodyBones.LeftIndexIntermediate, "Bip001 L Finger11"),
        (HumanBodyBones.LeftIndexDistal, "Bip001 L Finger12"),
        (HumanBodyBones.LeftMiddleProximal, "Bip001 L Finger2"),
        (HumanBodyBones.LeftMiddleIntermediate, "Bip001 L Finger21"),
        (HumanBodyBones.LeftMiddleDistal, "Bip001 L Finger22"),
        (HumanBodyBones.LeftRingProximal, "Bip001 L Finger3"),
        (HumanBodyBones.LeftRingIntermediate, "Bip001 L Finger31"),
        (HumanBodyBones.LeftRingDistal, "Bip001 L Finger32"),
        (HumanBodyBones.LeftLittleProximal, "Bip001 L Finger4"),
        (HumanBodyBones.LeftLittleIntermediate, "Bip001 L Finger41"),
        (HumanBodyBones.LeftLittleDistal, "Bip001 L Finger42"),
        (HumanBodyBones.RightThumbProximal, "Bip001 R Finger0"),
        (HumanBodyBones.RightThumbIntermediate, "Bip001 R Finger01"),
        (HumanBodyBones.RightThumbDistal, "Bip001 R Finger02"),
        (HumanBodyBones.RightIndexProximal, "Bip001 R Finger1"),
        (HumanBodyBones.RightIndexIntermediate, "Bip001 R Finger11"),
        (HumanBodyBones.RightIndexDistal, "Bip001 R Finger12"),
        (HumanBodyBones.RightMiddleProximal, "Bip001 R Finger2"),
        (HumanBodyBones.RightMiddleIntermediate, "Bip001 R Finger21"),
        (HumanBodyBones.RightMiddleDistal, "Bip001 R Finger22"),
        (HumanBodyBones.RightRingProximal, "Bip001 R Finger3"),
        (HumanBodyBones.RightRingIntermediate, "Bip001 R Finger31"),
        (HumanBodyBones.RightRingDistal, "Bip001 R Finger32"),
        (HumanBodyBones.RightLittleProximal, "Bip001 R Finger4"),
        (HumanBodyBones.RightLittleIntermediate, "Bip001 R Finger41"),
        (HumanBodyBones.RightLittleDistal, "Bip001 R Finger42"),
    };

    /// <summary>骨の基準の姿勢 (根元の空間での回転と位置)</summary>
    public struct Rest
    {
        public Quaternion Rotation;
        public Vector3 Position;
    }

    /// <summary>
    /// ゲームのキャラの人型の骨と、基準の姿勢 (T ポーズ)。skinned はバインドポーズを読む体のメッシュ
    /// </summary>
    public static Dictionary<HumanBodyBones, (Transform bone, Rest rest)> BipedRest(
        Transform root, IEnumerable<SkinnedMeshRenderer> skinned)
    {
        var byName = new Dictionary<string, Transform>();
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (!byName.ContainsKey(t.name)) byName[t.name] = t;

        var human = new List<(HumanBodyBones bone, Transform t)>();
        foreach (var (bone, name) in BipedMap)
            if (byName.TryGetValue(name, out var t)) human.Add((bone, t));
        if (!human.Any(h => h.bone == HumanBodyBones.Hips) || !human.Any(h => h.bone == HumanBodyBones.Head))
            throw new InvalidOperationException("Biped の骨 (Bip001 Pelvis / Head) が見つかりません");

        // バインドポーズ (根元の空間での、メッシュを付けたときの骨の姿勢)
        var rootW2L = root.worldToLocalMatrix;
        var bind = new Dictionary<IntPtr, Matrix4x4>();
        foreach (var smr in skinned)
        {
            var bones = smr.bones;
            var poses = smr.sharedMesh?.bindposes;
            if (bones == null || poses == null) continue;
            var smrToRoot = rootW2L * smr.transform.localToWorldMatrix;
            for (int i = 0; i < bones.Length && i < poses.Length; i++)
                if (bones[i] != null && !bind.ContainsKey(bones[i].Pointer))
                    bind[bones[i].Pointer] = smrToRoot * poses[i].inverse;
        }

        // 根元から人型の骨までを、基準の姿勢で別の階層として作り直す (元の骨は動かさない)。
        // 腕を水平に直すときに、子の骨も一緒に回るようにするため
        var shadowRoot = new GameObject("RusK_RestPose");
        try
        {
            var shadow = new Dictionary<IntPtr, Transform> { [root.Pointer] = shadowRoot.transform };
            var restRoot = new Dictionary<IntPtr, Matrix4x4> { [root.Pointer] = Matrix4x4.identity };
            foreach (var (_, t) in human) MakeShadow(t, shadow, restRoot, bind);

            EnforceTPose(shadow, human);
            LogHands(root, shadow, human, bind);

            var result = new Dictionary<HumanBodyBones, (Transform, Rest)>();
            foreach (var (bone, t) in human)
            {
                var s = shadow[t.Pointer];
                result[bone] = (t, new Rest { Rotation = s.rotation, Position = s.position });
            }
            return result;
        }
        finally
        {
            Object.Destroy(shadowRoot);
        }
    }

    private static Transform MakeShadow(Transform t, Dictionary<IntPtr, Transform> shadow,
        Dictionary<IntPtr, Matrix4x4> restRoot, Dictionary<IntPtr, Matrix4x4> bind)
    {
        if (shadow.TryGetValue(t.Pointer, out var s)) return s;
        var parent = t.parent;
        var sp = MakeShadow(parent, shadow, restRoot, bind);

        var parentRest = restRoot[parent.Pointer];
        var rest = bind.TryGetValue(t.Pointer, out var b)
            ? b
            : parentRest * Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale);
        if (!bind.ContainsKey(t.Pointer))
        {
            // メッシュに使われていない骨は、今のアニメーションの姿勢になってしまう (腕のねじれがずれる)。
            // Biped は上腕・前腕の代わりにねじれ用の子の骨 (UpArmTwist / ForeTwist) にメッシュを付けることがある (霜色)。
            // ねじれ用の骨は基準の姿勢で親と同じ向きなので、その向きを使う (位置は親からの位置のまま)
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (!c.name.Contains("Twist") || !bind.TryGetValue(c.Pointer, out var cb)) continue;
                var pos = new Vector3(rest.m03, rest.m13, rest.m23);
                rest = Matrix4x4.TRS(pos, cb.rotation, Vector3.one);
                break;
            }
        }
        restRoot[t.Pointer] = rest;

        var local = parentRest.inverse * rest;
        s = new GameObject(t.name).transform;
        s.SetParent(sp, false);
        s.localPosition = new Vector3(local.m03, local.m13, local.m23);
        s.localRotation = local.rotation;
        shadow[t.Pointer] = s;
        return s;
    }

    /// <summary>
    /// 調査用: 基準の姿勢での手の向き・手のひらの向き・指の曲がりをログに出す (キャラによって手がねじれる件)。
    /// bind なし = その骨がメッシュに使われておらず、今のアニメーションの姿勢を基準にしてしまっている
    /// </summary>
    private static void LogHands(Transform root, Dictionary<IntPtr, Transform> shadow, List<(HumanBodyBones bone, Transform t)> human,
        Dictionary<IntPtr, Matrix4x4> bind)
    {
        try
        {
            Transform S(HumanBodyBones b)
            {
                var h = human.FirstOrDefault(x => x.bone == b);
                return h.t != null && shadow.TryGetValue(h.t.Pointer, out var s) ? s : null;
            }
            bool B(HumanBodyBones b)
            {
                var h = human.FirstOrDefault(x => x.bone == b);
                return h.t != null && bind.ContainsKey(h.t.Pointer);
            }
            string V(Vector3 v) => $"({v.x:0.00},{v.y:0.00},{v.z:0.00})";
            var sb = new System.Text.StringBuilder($"Model: 手の基準の姿勢 '{root.name}'");
            foreach (var (side, hand, lower, index, middle, little, thumb) in new[]
                     {
                         ("左", HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftIndexProximal,
                             HumanBodyBones.LeftMiddleProximal, HumanBodyBones.LeftLittleProximal, HumanBodyBones.LeftThumbProximal),
                         ("右", HumanBodyBones.RightHand, HumanBodyBones.RightLowerArm, HumanBodyBones.RightIndexProximal,
                             HumanBodyBones.RightMiddleProximal, HumanBodyBones.RightLittleProximal, HumanBodyBones.RightThumbProximal),
                     })
            {
                var h = S(hand); var m = S(middle); var i = S(index); var l = S(little); var lo = S(lower);
                if (h == null || m == null || i == null || l == null || lo == null) { sb.Append($" / {side}: 骨が足りない"); continue; }
                var arm = (h.position - lo.position).normalized;
                var dir = (m.position - h.position).normalized;
                var palm = Vector3.Cross(i.position - h.position, l.position - h.position).normalized;
                sb.Append($" / {side}: 腕 {V(arm)} 手 {V(dir)} (腕との角度 {Vector3.Angle(arm, dir):0}) 手のひら {V(palm)}");
                sb.Append($" bind 手={B(hand)} 指={B(middle)}");
                var sh = side == "左" ? HumanBodyBones.LeftShoulder : HumanBodyBones.RightShoulder;
                var up = side == "左" ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm;
                sb.Append($" 肩={B(sh)} 上腕={B(up)} 前腕={B(lower)}");
                // 腕まわりの骨 (ねじれの骨など) の名前
                var upT = human.FirstOrDefault(x => x.bone == up).t;
                if (upT != null)
                    sb.Append(" 上腕の子=[" + string.Join(",", Enumerable.Range(0, upT.childCount).Select(k => upT.GetChild(k).name)) + "]");
                var loT = human.FirstOrDefault(x => x.bone == lower).t;
                if (loT != null)
                    sb.Append(" 前腕の子=[" + string.Join(",", Enumerable.Range(0, loT.childCount).Select(k => loT.GetChild(k).name)) + "]");
                // 中指の曲がり (付け根 → 2 節目の向きと手の向きの角度)
                var m2 = human.FirstOrDefault(x => x.bone == middle + 1);
                if (m2.t != null && shadow.TryGetValue(m2.t.Pointer, out var s2))
                    sb.Append($" 中指の曲がり {Vector3.Angle(dir, (s2.position - m.position).normalized):0}°");
            }
            VrmEnv.Ctx?.Log.Info(sb.ToString());
        }
        catch (Exception e) { VrmEnv.Ctx?.Log.Warning($"Model: 手の調査に失敗: {e.Message}"); }
    }

    /// <summary>腕 (上腕・前腕) を、根元から見て真横 (左手は -X、右手は +X) に向ける</summary>
    private static void EnforceTPose(Dictionary<IntPtr, Transform> shadow, List<(HumanBodyBones bone, Transform t)> human)
    {
        Transform S(HumanBodyBones b)
        {
            var h = human.FirstOrDefault(x => x.bone == b);
            return h.t != null && shadow.TryGetValue(h.t.Pointer, out var s) ? s : null;
        }

        void Aim(Transform bone, Transform child, Vector3 dir)
        {
            if (bone == null || child == null) return;
            var cur = child.position - bone.position;
            if (cur.sqrMagnitude < 1e-8f) return;
            bone.rotation = Quaternion.FromToRotation(cur, dir) * bone.rotation;
        }

        // 影の階層は根元が原点・回転なしなので、ワールドの向き = 根元から見た向き
        Aim(S(HumanBodyBones.LeftUpperArm), S(HumanBodyBones.LeftLowerArm), Vector3.left);
        Aim(S(HumanBodyBones.LeftLowerArm), S(HumanBodyBones.LeftHand), Vector3.left);
        Aim(S(HumanBodyBones.RightUpperArm), S(HumanBodyBones.RightLowerArm), Vector3.right);
        Aim(S(HumanBodyBones.RightLowerArm), S(HumanBodyBones.RightHand), Vector3.right);
    }

    /// <summary>VRM の人型の骨と基準の姿勢。モデルの根元が原点・回転なしの状態 (読み込み直後) で呼ぶ</summary>
    public static Dictionary<HumanBodyBones, (Transform bone, Rest rest)> VrmRest(VrmModel model)
    {
        var root = model.Root.transform;
        var result = new Dictionary<HumanBodyBones, (Transform, Rest)>();
        foreach (var kv in model.Human)
        {
            var t = kv.Value;
            result[kv.Key] = (t, new Rest
            {
                Rotation = Quaternion.Inverse(root.rotation) * t.rotation,
                Position = root.InverseTransformPoint(t.position),
            });
        }
        return result;
    }
}

/// <summary>ゲームのキャラの動きを、毎フレーム VRM に写す</summary>
internal sealed class Retargeter
{
    private readonly Transform _srcRoot, _dstRoot;
    private readonly List<(HumanBodyBones bone, Transform src, Quaternion srcRestInv, Transform dst, Quaternion dstRest)> _pairs = new();
    private readonly Transform _srcHips, _dstHips;
    private readonly Vector3 _srcHipsRest, _dstHipsRest;
    private readonly float _scale = 1f;

    public int PairCount => _pairs.Count;

    /// <summary>動きを写しているゲームの骨 (調査用)</summary>
    public IEnumerable<Transform> MappedSources => _pairs.Select(p => p.src);

    // 骨に付いている物 (武器・装備品・エフェクト) を VRM の同じ骨の位置に合わせてずらす
    private readonly List<(Transform src, Transform dst)> _attachBones = new();
    private readonly Dictionary<IntPtr, (Transform t, Vector3 baseLocal, Vector3 written)> _attached = new();

    public Retargeter(Transform srcRoot, Dictionary<HumanBodyBones, (Transform bone, Humanoid.Rest rest)> src,
        Transform dstRoot, Dictionary<HumanBodyBones, (Transform bone, Humanoid.Rest rest)> dst)
    {
        _srcRoot = srcRoot;
        _dstRoot = dstRoot;

        // 親から子の順に写す (HumanBodyBones の番号は体幹 → 手足 → 指の順)
        foreach (var bone in src.Keys.Intersect(dst.Keys).OrderBy(b => (int)b))
        {
            var s = src[bone];
            var d = dst[bone];
            _pairs.Add((bone, s.bone, Quaternion.Inverse(s.rest.Rotation), d.bone, d.rest.Rotation));
        }

        // 武器は手、装備品は頭・胸・腰などの骨の下 (WeaponHolder_1～4) に付いている
        foreach (var b in AttachBones)
            if (src.TryGetValue(b, out var s) && dst.TryGetValue(b, out var d))
                _attachBones.Add((s.bone, d.bone));

        if (src.TryGetValue(HumanBodyBones.Hips, out var sh) && dst.TryGetValue(HumanBodyBones.Hips, out var dh))
        {
            _srcHips = sh.bone;
            _dstHips = dh.bone;
            _srcHipsRest = sh.rest.Position;
            _dstHipsRest = dh.rest.Position;
            // 腰の高さの比で、腰の動き (しゃがむ・跳ぶ) を VRM の体格に合わせる
            _scale = _srcHipsRest.y > 0.01f ? _dstHipsRest.y / _srcHipsRest.y : 1f;
        }
    }

    public void Update()
    {
        var srcRootInv = Quaternion.Inverse(_srcRoot.rotation);
        var dstRootRot = _dstRoot.rotation;

        if (_srcHips != null && _dstHips != null)
        {
            var p = _srcRoot.InverseTransformPoint(_srcHips.position);
            _dstHips.position = _dstRoot.TransformPoint(p * _scale);
        }

        var hipsDelta = Quaternion.identity;
        foreach (var (bone, src, srcRestInv, dst, dstRest) in _pairs)
        {
            // 根元の空間で「基準から回った分」を求め、VRM の基準の向きに掛ける
            var delta = srcRootInv * src.rotation * srcRestInv;
            if (bone == HumanBodyBones.Hips) hipsDelta = delta;
            else if ((bone == HumanBodyBones.LeftUpperLeg || bone == HumanBodyBones.RightUpperLeg) && SkirtTuning.LegSwing < 0.999f)
            {
                // 太ももの振りを抑える (腰から見た回転を縮める。スカートから脚が出にくくなる)
                var rel = Quaternion.Inverse(hipsDelta) * delta;
                delta = hipsDelta * Quaternion.Slerp(Quaternion.identity, rel, SkirtTuning.LegSwing);
            }
            dst.rotation = dstRootRot * delta * dstRest;
        }

        MoveAttachments();
    }

    /// <summary>付いている物をずらす骨 (手の武器、頭・胸・腰などの装備品やエフェクト)</summary>
    private static readonly HumanBodyBones[] AttachBones =
    {
        HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest,
        HumanBodyBones.Neck, HumanBodyBones.Head,
        HumanBodyBones.LeftShoulder, HumanBodyBones.RightShoulder,
        HumanBodyBones.LeftUpperArm, HumanBodyBones.RightUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightLowerArm,
        HumanBodyBones.LeftHand, HumanBodyBones.RightHand,
        HumanBodyBones.LeftUpperLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightLowerLeg,
        HumanBodyBones.LeftFoot, HumanBodyBones.RightFoot,
    };

    /// <summary>
    /// ゲームの骨に付いている物 (武器・装備品・エフェクトの根元) を、VRM の同じ骨の位置へずらす。
    /// 骨からの距離は体格の比 (腰の高さの比) で縮める (背の低い VRM なら体に近づける)。
    /// 向きは両方の骨が基準から同じだけ回っているので変えない。
    /// アニメーションが位置を書き換えない子は、ずらした分が積み重ならないよう、元の位置から毎回計算する
    /// </summary>
    private void MoveAttachments()
    {
        foreach (var (srcBone, dstBone) in _attachBones)
        {
            for (int i = 0; i < srcBone.childCount; i++)
            {
                var c = srcBone.GetChild(i);
                if (c.name.StartsWith("Bip001")) continue; // 骨格の骨 (子の骨・指) は動かさない
                var local = c.localPosition;
                if (!_attached.TryGetValue(c.Pointer, out var st) || (local - st.written).sqrMagnitude > 1e-10f)
                    st.baseLocal = local; // 初めて見た子か、アニメーションが位置を書き換えた
                st.t = c;
                var fromBone = srcBone.TransformPoint(st.baseLocal) - srcBone.position;
                c.position = dstBone.position + fromBone * _scale;
                st.written = c.localPosition;
                _attached[c.Pointer] = st;
            }
        }
    }

    /// <summary>
    /// ずらした物を元の位置に戻す (VRM を外すとき)。戻さないと、次に VRM を付けたときに
    /// ずれた位置を「元の位置」と覚えてしまい、付け直すたびにずれが積み重なる
    /// </summary>
    public void RestoreAttachments()
    {
        foreach (var st in _attached.Values)
        {
            try
            {
                if (st.t == null) continue;
                if ((st.t.localPosition - st.written).sqrMagnitude > 1e-10f) continue; // アニメーションが書き換え済み
                st.t.localPosition = st.baseLocal;
            }
            catch { /* 壊れた物は無視 */ }
        }
        _attached.Clear();
    }
}
