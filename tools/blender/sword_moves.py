"""
剣の技 (通常攻撃の 1〜5 段目) を作る。片手剣・大剣のキャラ用 (ゲームの骨格 Bip001 と、武器の骨 WeaponHolder_0)。

    blender --background --python sword_moves.py -- <rig_キャラ名_motions.glb> <書き出すフォルダ> [<プレビューのフォルダ>] [技の名前 ...]

技の名前を省くと全部作る。書き出すファイルは Sword<名前>.glb、アニメーションの名前は RusK_Sword<名前>。

作り方 (rusk_rig.py):
  - 基準の姿勢はゲームの立ち姿勢 (Idle)、刀の握りと右手の指は土台のキャラの 1 段目 (RedLightCombo0) から借りる
  - 右手は「肩から見た水平の角度・距離・高さ」、刃は「水平の角度・上下の角度」で決める (角度で補間すると弧を描く)
  - 左手は肩から見た位置か、"grip" (両手持ち: 柄を握る)
  - 足は床に固定 (step で踏み込み、lift で跳ぶときに足を上げる)、体は腰のひねり・前かがみ・沈み込み (drop、負なら跳ぶ)
時間割はゲームの動作 (motions.txt / Model Lab の書き出しの extras) に合わせる:
  当たる瞬間 HIT は Custom Motion がゲームの攻撃判定の時間に合わせて伸び縮みさせる。その後は実際の速さ
  (2 回目の当たりは HIT + (2 回目 - 1 回目の進み具合) × ゲームの動作の長さ)。
  次の段へつなげられる時間 (ComboRunAble) までに振り抜きの姿勢で止まり、最後は立ち姿勢に戻る。
"""
import math
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import rusk_rig as rr  # noqa: E402

F, B, L, Rt, U, D = rr.FRONT, rr.BACK, rr.LEFT, rr.RIGHT, rr.UP, rr.DOWN
FPS = 30


def P(drop=0.0, twist=(0, 0, 0), lean=0.0, head=0.0, rhand=(-20, 0.30, -0.40), blade=(-15, -35),
      lhand=None, lstep=None, rstep=None, lift=0.0):
    """姿勢。rhand = (角度, 肩からの距離, 肩からの高さ)、blade = (角度, 上下の角度)。角度は 0 = 前、+ = 左 (度)"""
    return dict(drop=drop, twist=twist, lean=lean, head=head, rhand=rhand, blade=blade,
                lhand=lhand if lhand is not None else L * 0.10 + F * 0.10 + D * 0.45,
                lstep=lstep if lstep is not None else F * 0, rstep=rstep if rstep is not None else F * 0, lift=lift)


READY = P()

# ---------------------------------------------------------------- 1 段目: 横なぎ (右 → 左)
SWEEP_HOLD = P(drop=0.07, twist=(12, 18, 16), lean=8, head=-15, rhand=(55, 0.42, -0.15), blade=(105, -12),
               lhand=L * 0.25 + B * 0.10 + D * 0.38)
SWEEP = dict(hit=0.30, length=1.80, poses={
    "ready": READY,
    "windup": P(drop=0.06, twist=(-15, -20, -20), lean=8, head=25, rhand=(-125, 0.42, -0.02), blade=(-145, 5),
                lhand=L * 0.15 + F * 0.35 + D * 0.15),
    "hit": P(drop=0.08, twist=(5, 8, 8), lean=12, head=-5, rhand=(5, 0.50, -0.08), blade=(25, 0),
             lhand=L * 0.30 + B * 0.10 + D * 0.30),
    "follow": P(drop=0.09, twist=(15, 22, 20), lean=12, head=-20, rhand=(70, 0.45, -0.08), blade=(115, 0),
                lhand=L * 0.30 + B * 0.20 + D * 0.32),
    "hold": SWEEP_HOLD,
}, keys=[(0.00, "ready", "smooth"), (0.18, "windup", "smooth"), (0.30, "hit", "fast"), (0.38, "follow", "out"),
         (0.80, "hold", "smooth"), (1.40, "ready", "smooth"), (1.80, "ready", "smooth")])

# ---------------------------------------------------------------- 2 段目: 返しの横なぎ (左 → 右)
BACK_HOLD = P(drop=0.06, twist=(-14, -20, -18), lean=8, head=15, rhand=(-75, 0.42, -0.12), blade=(-115, -10),
              lhand=L * 0.20 + F * 0.20 + D * 0.30)
BACKHAND = dict(hit=0.22, length=1.80, poses={
    "start": SWEEP_HOLD,
    "windup": P(drop=0.07, twist=(18, 26, 24), lean=6, head=-20, rhand=(85, 0.36, 0.0), blade=(135, 8),
                lhand=L * 0.30 + B * 0.15 + D * 0.30),
    "hit": P(drop=0.08, twist=(-2, -4, -4), lean=12, head=0, rhand=(-5, 0.52, -0.06), blade=(-25, 0),
             lhand=L * 0.25 + F * 0.15 + D * 0.30),
    "follow": P(drop=0.08, twist=(-15, -22, -20), lean=10, head=15, rhand=(-80, 0.45, -0.06), blade=(-120, 0),
                lhand=L * 0.20 + F * 0.25 + D * 0.28),
    "hold": BACK_HOLD,
    "ready": READY,
}, keys=[(0.00, "start", "smooth"), (0.12, "windup", "smooth"), (0.22, "hit", "fast"), (0.29, "follow", "out"),
         (0.86, "hold", "smooth"), (1.45, "ready", "smooth"), (1.80, "ready", "smooth")])

# ---------------------------------------------------------------- 3 段目: 袈裟斬り (右上 → 左下) → 斬り上げ (左下 → 右上)。2 回当たる
KESA_HIT2 = 0.24 + (0.107 - 0.054) * 3.33   # ゲームの 2 回目の当たり
KESA_HOLD = P(drop=0.05, twist=(-10, -14, -12), lean=0, head=8, rhand=(-55, 0.32, 0.30), blade=(-70, 55),
              lhand=L * 0.20 + F * 0.15 + D * 0.30)
KESA = dict(hit=0.24, length=1.90, poses={
    "start": BACK_HOLD,
    "windup": P(drop=0.04, twist=(-12, -18, -16), lean=-4, head=10, rhand=(-35, 0.22, 0.38), blade=(-30, 62),
                lhand=L * 0.15 + F * 0.25 + D * 0.10),
    "hit": P(drop=0.10, twist=(6, 10, 10), lean=16, head=-4, rhand=(15, 0.48, -0.22), blade=(35, -40),
             lhand=L * 0.30 + B * 0.10 + D * 0.32),
    "low": P(drop=0.12, twist=(12, 18, 16), lean=18, head=-8, rhand=(40, 0.42, -0.42), blade=(60, -60),
             lhand=L * 0.30 + B * 0.10 + D * 0.32),
    "hit2": P(drop=0.06, twist=(-6, -10, -10), lean=6, head=4, rhand=(-25, 0.46, 0.10), blade=(-45, 35),
              lhand=L * 0.25 + F * 0.10 + D * 0.32),
    "follow2": KESA_HOLD,
    "ready": READY,
}, keys=[(0.00, "start", "smooth"), (0.15, "windup", "smooth"), (0.24, "hit", "fast"), (0.31, "low", "out"),
         (KESA_HIT2, "hit2", "fast"), (KESA_HIT2 + 0.08, "follow2", "out"), (0.90, "follow2", "smooth"),
         (1.55, "ready", "smooth"), (1.90, "ready", "smooth")])

# ---------------------------------------------------------------- 4 段目: 踏み込んで突き
THRUST_HOLD = P(drop=0.16, twist=(-6, -8, -8), lean=18, head=0, rhand=(-5, 0.55, -0.12), blade=(0, -4),
                lhand=L * 0.30 + B * 0.25 + D * 0.25, lstep=F * 0.30 + L * 0.05)
THRUST = dict(hit=0.32, length=2.30, poses={
    "start": KESA_HOLD,
    "windup": P(drop=0.18, twist=(-25, -35, -32), lean=10, head=30, rhand=(-70, 0.22, -0.32), blade=(-5, -2),
                lhand=L * 0.10 + F * 0.40 + D * 0.15, lstep=F * 0.10),
    "hit": P(drop=0.18, twist=(-4, -6, -6), lean=22, head=0, rhand=(-2, 0.60, -0.10), blade=(0, -3),
             lhand=L * 0.30 + B * 0.30 + D * 0.20, lstep=F * 0.32 + L * 0.05),
    "hold": THRUST_HOLD,
    "ready": READY,
}, keys=[(0.00, "start", "smooth"), (0.20, "windup", "smooth"), (0.32, "hit", "fast"), (0.40, "hold", "out"),
         (1.04, "hold", "smooth"), (1.85, "ready", "smooth"), (2.30, "ready", "smooth")])

# ---------------------------------------------------------------- 5 段目: 跳び上がって両手で振り下ろし
SLAM = dict(hit=0.70, length=2.80, poses={
    "start": THRUST_HOLD,
    "crouch": P(drop=0.22, twist=(0, 0, 0), lean=20, head=-10, rhand=(-10, 0.30, -0.40), blade=(-10, -20),
                lhand="grip"),
    "rise": P(drop=-0.70, twist=(0, 0, 0), lean=-6, head=-15, rhand=(-10, 0.10, 0.45), blade=(0, 80),
              lhand="grip", lift=0.45),
    "apex": P(drop=-1.10, twist=(0, 0, 0), lean=-10, head=-20, rhand=(0, 0.05, 0.52), blade=(180, 50),
              lhand="grip", lift=0.60),
    "fall": P(drop=-0.55, twist=(0, 0, 0), lean=10, head=0, rhand=(0, 0.30, 0.30), blade=(0, 60),
              lhand="grip", lift=0.35),
    "hit": P(drop=0.30, twist=(0, 0, 0), lean=30, head=10, rhand=(0, 0.50, -0.70), blade=(0, -55),
             lhand="grip", lstep=F * 0.20),
    "hold": P(drop=0.28, twist=(0, 0, 0), lean=28, head=10, rhand=(0, 0.50, -0.72), blade=(0, -60),
              lhand="grip", lstep=F * 0.20),
    "ready": READY,
}, keys=[(0.00, "start", "smooth"), (0.12, "crouch", "smooth"), (0.30, "rise", "out"), (0.40, "apex", "out"),
         (0.58, "fall", "smooth"), (0.70, "hit", "fast"), (0.78, "hold", "out"), (1.44, "hold", "smooth"),
         (2.30, "ready", "smooth"), (2.80, "ready", "smooth")])

MOVES = {"Sweep": SWEEP, "Backhand": BACKHAND, "Kesa": KESA, "Thrust": THRUST, "Slam": SLAM}


# ---------------------------------------------------------------- 作る

def horizontal(yaw, pitch=0.0):
    """水平の角度 (0 = 前、+ = 左) と上下の角度 (+ = 上) から向き"""
    c, s = math.cos(math.radians(pitch)), math.sin(math.radians(pitch))
    return rr.R(rr.Z, yaw) @ (F * c + U * s)


def ease(x, kind):
    if kind == "fast":
        return x * x          # 加速しながら振り抜く
    if kind == "out":
        return 1 - (1 - x) ** 2  # 勢いが抜けていく
    return x * x * (3 - 2 * x)


def lerp(a, b, x):
    if isinstance(a, str) or isinstance(b, str):
        return a if x < 0.5 else b
    if isinstance(a, tuple):
        return tuple(lerp(p, q, x) for p, q in zip(a, b))
    return a + (b - a) * x


def pose_at(move, t):
    keys = move["keys"]
    for (t0, a, _), (t1, b, kind) in zip(keys, keys[1:]):
        if t <= t1:
            x = 0.0 if t1 <= t0 else ease((t - t0) / (t1 - t0), kind)
            pa, pb = move["poses"][a], move["poses"][b]
            return {k: lerp(pa[k], pb[k], x) for k in pa}
    return move["poses"][keys[-1][1]]


def build(rig_path, name, move, out_dir, preview_dir):
    arm = rr.load(rig_path)
    body = [pb.name for pb in arm.pose.bones if pb.name.startswith("Bip001") and pb.name not in rr.TWISTS]
    weapon = ["BN_weapon_01", "WeaponHolder_0"]

    # 基準: 立ち姿勢 + 1 段目の握り (刀の骨・右手の指)
    base = rr.sample(arm, "Idle", 0.0)
    grip_bones = weapon + [n for n in body if n.startswith("Bip001 R Finger")]
    base = rr.graft(arm, base, rr.sample(arm, "RedLightCombo0", 0.1), grip_bones)
    rr.fix_chains(arm, base)
    rr.apply(arm, base)
    shoulder_r, shoulder_l = rr.world(arm, "Bip001 R UpperArm"), rr.world(arm, "Bip001 L UpperArm")
    foot_r, foot_l = rr.world(arm, "Bip001 R Foot"), rr.world(arm, "Bip001 L Foot")
    foot_rot_r = rr.world_rot(arm, "Bip001 R Foot").to_quaternion()
    foot_rot_l = rr.world_rot(arm, "Bip001 L Foot").to_quaternion()

    hand_r, hand_r_rot, pole_r = rr.empty("ctl_hand_R"), rr.empty("ctl_hand_R_rot"), rr.empty("pole_elbow_R")
    hand_l, pole_l = rr.empty("ctl_hand_L"), rr.empty("pole_elbow_L")
    ft_r, ft_l = rr.empty("ctl_foot_R", foot_r), rr.empty("ctl_foot_L", foot_l)
    ft_r.rotation_quaternion, ft_l.rotation_quaternion = foot_rot_r, foot_rot_l
    knee_r, knee_l = rr.empty("pole_knee_R"), rr.empty("pole_knee_L")

    scene = rr.bpy.context.scene
    frames = round(move["length"] * FPS)
    scene.frame_start, scene.frame_end = 0, frames
    for f in range(frames + 1):
        p = pose_at(move, f / FPS)
        rr.apply(arm, base)
        body_up = U * -p["drop"]
        rr.move(arm, "Bip001", body_up)
        pel, sp, sp1 = p["twist"]
        rr.turn(arm, "Bip001 Pelvis", rr.R(rr.Z, pel))
        rr.turn(arm, "Bip001 Spine", rr.R(rr.Z, sp - pel) @ rr.R(rr.X, p["lean"] * 0.5))  # X (キャラの左) まわりに + で前へ倒れる
        rr.turn(arm, "Bip001 Spine1", rr.R(rr.Z, sp1 - sp) @ rr.R(rr.X, p["lean"] * 0.5))
        rr.turn(arm, "Bip001 Head", rr.R(rr.Z, p["head"] - sp1 * 0.5))
        rr.key(arm, body + weapon, f)

        yaw, dist, h = p["rhand"]
        hand_r.location = shoulder_r + body_up + horizontal(yaw) * dist + U * h
        if not isinstance(p["lhand"], str):
            hand_l.location = shoulder_l + body_up + p["lhand"]
        pole_r.location = shoulder_r + body_up + Rt * 0.35 + B * 0.25 + D * 0.45
        pole_l.location = shoulder_l + body_up + L * 0.35 + B * 0.25 + D * 0.45
        lift = U * p["lift"] + B * p["lift"] * 0.3
        ft_l.location = foot_l + p["lstep"] + lift
        ft_r.location = foot_r + p["rstep"] + lift
        knee_l.location = foot_l + p["lstep"] + lift + F * 0.8 + U * 0.5
        knee_r.location = foot_r + p["rstep"] + lift + F * 0.8 + U * 0.5
        for e in (hand_r, hand_l, pole_r, pole_l, ft_l, ft_r, knee_l, knee_r):
            e.keyframe_insert("location", frame=f)

    scene.frame_set(0)
    rr.ik(arm, "Bip001 R Hand", hand_r, pole_r)
    rr.ik(arm, "Bip001 L Hand", hand_l, pole_l)
    rr.ik(arm, "Bip001 R Foot", ft_r, knee_r)
    rr.ik(arm, "Bip001 L Foot", ft_l, knee_l)
    rr.copy_rot(arm, "Bip001 R Foot", ft_r)
    rr.copy_rot(arm, "Bip001 L Foot", ft_l)
    c = rr.copy_rot(arm, "Bip001 R Hand", hand_r_rot)

    # 手の向き (刃の向きから)、両手持ちなら左手を柄へ (1 コマずつ、IK で腕が決まった後に)
    for f in range(frames + 1):
        scene.frame_set(f)
        p = pose_at(move, f / FPS)
        c.influence = 0.0
        rr.bpy.context.view_layer.update()
        hand_r_rot.rotation_quaternion = rr.hand_rot_for_blade(arm, "Bip001 R Hand", "WeaponHolder_0", horizontal(*p["blade"]), rr.UP)
        hand_r_rot.keyframe_insert("rotation_quaternion", frame=f)
        c.influence = 1.0
        if isinstance(p["lhand"], str):
            rr.bpy.context.view_layer.update()
            holder = rr.arm_matrix(arm, "WeaponHolder_0")
            blade = holder.to_3x3() @ rr.X
            hand_l.location = holder.translation - blade.normalized() * 0.16  # 柄の、右手より少し下
            hand_l.keyframe_insert("location", frame=f)

    anim = "RusK_Sword" + name
    rr.bake(arm, body + weapon, 0, frames, anim)
    rr.follow_twists(arm, base, 0, frames)
    if preview_dir:
        hit2 = [k[0] for k in move["keys"] if k[1] == "hit2"]
        times = sorted({0.0, move["keys"][1][0], move["hit"], *hit2, move["hit"] + 0.08, move["keys"][-3][0]})
        shots = [round(t * FPS) for t in times]
        rr.sheet(arm, os.path.join(preview_dir, f"Sword{name}.png"), shots, labels=[f"{t:.2f}s" for t in times])
    rr.export(arm, os.path.join(out_dir, f"Sword{name}.glb"), anim)


argv = sys.argv[sys.argv.index("--") + 1:]
rig, out = argv[0], argv[1]
preview = argv[2] if len(argv) > 2 and argv[2] not in MOVES else None
names = [a for a in argv[2:] if a in MOVES] or list(MOVES)
for n in names:
    build(rig, n, MOVES[n], out, preview)
