"""
片手で横なぎに斬る (右から左へ)。通常攻撃の 1 段目用。

    blender --background --python sword_sweep.py -- <rig_キャラ名_motions.glb> <書き出す .glb> [<プレビューの .png>]

ゲームの動作の時間割 (Model Lab の書き出しの extras / motions.txt) に合わせて作る:
  当たる瞬間 = HIT 秒 (Custom Motion がゲームの攻撃判定の時間に合わせて伸び縮みさせる。その後は実際の速さ)
  次の段へつなげられる時間 (ComboRunAble) までに振り抜きの姿勢で止まる。最後は立ち姿勢 (Idle) に戻る。
刀の握りは、土台のキャラの 1 段目の順手の握り (BN_weapon_01・指) を借りる。
"""
import math
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import rusk_rig as rr  # noqa: E402
from mathutils import Vector  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:]
rig_path, out_path = argv[0], argv[1]
preview_path = argv[2] if len(argv) > 2 else None

FPS = 30
HIT = 0.30      # 当たる瞬間 (秒)
LENGTH = 1.80   # 全体 (秒)

arm = rr.load(rig_path)
BODY = [pb.name for pb in arm.pose.bones if pb.name.startswith("Bip001") and pb.name not in rr.TWISTS]
WEAPON = ["BN_weapon_01", "WeaponHolder_0"]

# ---- 基準: 立ち姿勢 (Idle の最初) + 1 段目の握り (刀の骨・右手の指)
base = rr.sample(arm, "Idle", 0.0)
grip_bones = WEAPON + [n for n in BODY if n.startswith("Bip001 R Finger")]
grip = rr.sample(arm, "RedLightCombo0", 0.1, grip_bones)
base.update(grip)
rr.apply(arm, base)

shoulder_r = rr.world(arm, "Bip001 R UpperArm")
shoulder_l = rr.world(arm, "Bip001 L UpperArm")
foot_r, foot_l = rr.world(arm, "Bip001 R Foot"), rr.world(arm, "Bip001 L Foot")
hips = rr.world(arm, "Bip001 Pelvis")
print(f"[rusk] 右肩 {tuple(round(v, 2) for v in shoulder_r)} 左肩 {tuple(round(v, 2) for v in shoulder_l)} 腰 {tuple(round(v, 2) for v in hips)}")
print(f"[rusk] 右足 {tuple(round(v, 2) for v in foot_r)} 左足 {tuple(round(v, 2) for v in foot_l)}")
foot_rot_r, foot_rot_l = rr.world_rot(arm, "Bip001 R Foot").to_quaternion(), rr.world_rot(arm, "Bip001 L Foot").to_quaternion()

# ---- 目印
F, B, L, Rt, U, D = rr.FRONT, rr.BACK, rr.LEFT, rr.RIGHT, rr.UP, rr.DOWN
hand_r = rr.empty("ctl_hand_R")
hand_r_rot = rr.empty("ctl_hand_R_rot")
pole_r = rr.empty("pole_elbow_R")
hand_l = rr.empty("ctl_hand_L")
pole_l = rr.empty("pole_elbow_L")
ft_r = rr.empty("ctl_foot_R", foot_r)
ft_l = rr.empty("ctl_foot_L", foot_l)
ft_r.rotation_quaternion, ft_l.rotation_quaternion = foot_rot_r, foot_rot_l
knee_r = rr.empty("pole_knee_R", foot_r + F * 0.8 + U * 0.5)
knee_l = rr.empty("pole_knee_L", foot_l + F * 0.8 + U * 0.5)

# ---- 姿勢 (キーになる瞬間)。横なぎは円を描くので、右手と刃は「水平の角度」で決める (角度で補間すると弧を描く)
#   角度: 0 = 前、+ = 左、- = 右 (度)。rhand = (角度, 肩からの距離 m, 肩からの高さ m)、blade = (角度, 上下の角度)
#   体のひねりは度 (+ = 左へ向く)、lean は前かがみ (度)、drop は腰を落とす量 (m)
POSES = {
    "ready": dict(drop=0.00, twist=(0, 0, 0), lean=0, head=0,
                  rhand=(-20, 0.30, -0.40), blade=(-15, -35),
                  lhand=L * 0.10 + F * 0.10 + D * 0.45),
    "windup": dict(drop=0.06, twist=(-15, -20, -20), lean=8, head=25,
                   rhand=(-125, 0.42, -0.02), blade=(-145, 5),
                   lhand=L * 0.15 + F * 0.35 + D * 0.15),
    "hit": dict(drop=0.08, twist=(5, 8, 8), lean=12, head=-5,
                rhand=(5, 0.50, -0.08), blade=(25, 0),
                lhand=L * 0.30 + B * 0.10 + D * 0.30),
    "follow": dict(drop=0.09, twist=(15, 22, 20), lean=12, head=-20,
                   rhand=(70, 0.45, -0.08), blade=(115, 0),
                   lhand=L * 0.30 + B * 0.20 + D * 0.32),
    "hold": dict(drop=0.07, twist=(12, 18, 16), lean=8, head=-15,
                 rhand=(55, 0.42, -0.15), blade=(105, -12),
                 lhand=L * 0.25 + B * 0.10 + D * 0.38),
}


def horizontal(yaw, pitch=0.0):
    """水平の角度 (0 = 前、+ = 左) と上下の角度から向き"""
    return rr.R(rr.Z, yaw) @ rr.R(rr.X, 0) @ (rr.R(rr.Z, 0) @ (F * math.cos(math.radians(pitch)) + U * math.sin(math.radians(pitch))))


# 時間割 (秒, 姿勢, その区間の動き方)
KEYS = [(0.00, "ready", "smooth"), (0.18, "windup", "smooth"), (HIT, "hit", "fast"), (0.38, "follow", "out"),
        (0.80, "hold", "smooth"), (1.40, "ready", "smooth"), (LENGTH, "ready", "smooth")]


def ease(x, kind):
    if kind == "fast":
        return x * x          # 加速しながら振り抜く
    if kind == "out":
        return 1 - (1 - x) ** 2  # 勢いが抜けていく
    return x * x * (3 - 2 * x)


def lerp(a, b, x):
    if isinstance(a, tuple):
        return tuple(lerp(p, q, x) for p, q in zip(a, b))
    return a + (b - a) * x


def pose_at(t):
    for (t0, a, _), (t1, b, kind) in zip(KEYS, KEYS[1:]):
        if t <= t1:
            x = 0.0 if t1 <= t0 else ease((t - t0) / (t1 - t0), kind)
            pa, pb = POSES[a], POSES[b]
            return {k: lerp(pa[k], pb[k], x) for k in pa}
    return POSES[KEYS[-1][1]]


# ---- 1 コマずつ: 体 (向き・沈み込み) は骨を直接、手は目印を動かす
frames = round(LENGTH * FPS)
scene = rr.bpy.context.scene
scene.frame_start, scene.frame_end = 0, frames
for f in range(frames + 1):
    p = pose_at(f / FPS)
    rr.apply(arm, base)
    rr.move(arm, "Bip001", D * p["drop"])
    pel, sp, sp1 = p["twist"]
    rr.turn(arm, "Bip001 Pelvis", rr.R(rr.Z, pel))
    rr.turn(arm, "Bip001 Spine", rr.R(rr.Z, sp - pel) @ rr.R(rr.X, -p["lean"] * 0.5))
    rr.turn(arm, "Bip001 Spine1", rr.R(rr.Z, sp1 - sp) @ rr.R(rr.X, -p["lean"] * 0.5))
    rr.turn(arm, "Bip001 Head", rr.R(rr.Z, p["head"] - sp1 * 0.5))
    rr.key(arm, BODY + WEAPON, f)

    yaw, dist, h = p["rhand"]
    hand_r.location = shoulder_r + horizontal(yaw) * dist + U * h
    hand_l.location = shoulder_l + p["lhand"]
    pole_r.location = shoulder_r + Rt * 0.35 + B * 0.25 + D * 0.45
    pole_l.location = shoulder_l + L * 0.35 + B * 0.25 + D * 0.45
    for e in (hand_r, hand_l, pole_r, pole_l):
        e.keyframe_insert("location", frame=f)

# ---- IK (肘・膝) と足の固定
scene.frame_set(0)
rr.ik(arm, "Bip001 R Forearm", hand_r, pole_r)
rr.ik(arm, "Bip001 L Forearm", hand_l, pole_l)
rr.ik(arm, "Bip001 R Calf", ft_r, knee_r)
rr.ik(arm, "Bip001 L Calf", ft_l, knee_l)
rr.copy_rot(arm, "Bip001 R Foot", ft_r)
rr.copy_rot(arm, "Bip001 L Foot", ft_l)
c = rr.copy_rot(arm, "Bip001 R Hand", hand_r_rot)

# 手の向き: IK で腕が決まった後の、刃の向きから決める (1 コマずつ)
for f in range(frames + 1):
    scene.frame_set(f)
    p = pose_at(f / FPS)
    c.influence = 0.0
    rr.bpy.context.view_layer.update()
    hand_r_rot.rotation_quaternion = rr.hand_rot_for_blade(arm, "Bip001 R Hand", "WeaponHolder_0", horizontal(*p["blade"]), rr.UP)
    hand_r_rot.keyframe_insert("rotation_quaternion", frame=f)
c.influence = 1.0

# ---- 焼き付け、ねじれの骨
rr.bake(arm, BODY + WEAPON, 0, frames, "RusK_SwordSweep")
rr.follow_twists(arm, base, 0, frames)

if preview_path:
    shots = [0, round(0.18 * FPS), round(0.25 * FPS), round(HIT * FPS), round(0.38 * FPS), round(0.8 * FPS), round(1.4 * FPS)]
    rr.sheet(arm, preview_path, shots, labels=[f"{s / FPS:.2f}s" for s in shots])
rr.export(arm, out_path, "RusK_SwordSweep")
