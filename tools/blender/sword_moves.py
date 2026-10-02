"""
剣の技 (通常攻撃の 1〜5 段目) を作る。片手剣・大剣のキャラ用 (ゲームの骨格 Bip001 と、武器の骨 WeaponHolder_0)。

    blender --background --python sword_moves.py -- <rig_キャラ名_motions.glb> <書き出すフォルダ> [<プレビューのフォルダ>] [技の名前 ...]

技の名前を省くと全部作る。書き出すファイルは Sword<名前>.glb、アニメーションの名前は RusK_Sword<名前>。

作り方 (rusk_rig.py):
  - 基準の姿勢はゲームの立ち姿勢 (Idle)、刀の握りと右手の指は土台のキャラの 1 段目 (RedLightCombo0) から借りる
  - 右手は「肩から見た水平の角度・距離・高さ」、刃は「水平の角度・上下の角度」で決める (角度で補間すると弧を描く)
  - 左手は肩から見た位置か、"grip" (両手持ち: 柄を握る)
  - 体: 腰のひねり・前かがみ・横への傾き・沈み込み (drop、負なら跳ぶ)・体重移動 (shift)・右肩の前後 (clav)。
    首は前を見続ける (head は首の向き、体のひねりと逆に回す)
  - 足は床に固定。振る間は戦いの構え (左足を半歩前、右足を後ろ)。足を動かす区間は少し持ち上げる (床を滑らない)
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
ZERO = F * 0
FPS = 30

# 戦いの構え: 左足を半歩前、右足を後ろへ開く
STANCE_L = L * 0.08 + F * 0.16
STANCE_R = Rt * 0.06 + B * 0.14


def P(drop=0.12, twist=(0, 0, 0), lean=0.0, tilt=0.0, head=0.0, shift=None, clav=0.0,
      rhand=(-20, 0.30, -0.40), blade=(-15, -35), lhand=None, lstep=None, rstep=None, lift=0.0):
    """
    姿勢。rhand = (角度, 肩からの距離, 肩からの高さ)、blade = (角度, 上下の角度)。角度は 0 = 前、+ = 左 (度)。
    twist = (腰, 背骨, 胸) の向き (+ = 左)、head = 首の向き (+ = 左。体のひねりに関係なく)、lean = 前かがみ、tilt = 左へ傾く、
    shift = 腰の動き (体重移動)、clav = 右肩を前へ (度)
    """
    return dict(drop=drop, twist=twist, lean=lean, tilt=tilt, head=head, shift=shift if shift is not None else ZERO, clav=clav,
                rhand=rhand, blade=blade,
                lhand=lhand if lhand is not None else L * 0.15 + F * 0.15 + D * 0.40,
                lstep=lstep if lstep is not None else STANCE_L, rstep=rstep if rstep is not None else STANCE_R, lift=lift)


# 立ち姿勢 (ゲームの Idle と同じ。始まりと終わり)
READY = P(drop=0.0, lstep=ZERO, rstep=ZERO, lhand=L * 0.10 + F * 0.10 + D * 0.45)

# ---------------------------------------------------------------- 1 段目: 横なぎ (右 → 左)
SWEEP_HOLD = P(drop=0.13, twist=(20, 34, 40), lean=10, tilt=4, head=10, shift=L * 0.05 + F * 0.04, clav=10,
               rhand=(62, 0.40, -0.18), blade=(105, -15), lhand=L * 0.30 + B * 0.15 + D * 0.36)
SWEEP = dict(hit=0.30, length=1.80, poses={
    "ready": READY,
    "windup": P(drop=0.13, twist=(-25, -40, -48), lean=10, tilt=-5, head=-10, shift=Rt * 0.07 + B * 0.02, clav=-15,
                rhand=(-120, 0.40, 0.0), blade=(-145, 5), lhand=L * 0.15 + F * 0.35 + D * 0.15),
    "hit": P(drop=0.15, twist=(10, 16, 18), lean=16, tilt=3, head=4, shift=L * 0.03 + F * 0.08, clav=10,
             rhand=(5, 0.46, -0.08), blade=(25, 0), lhand=L * 0.35 + B * 0.10 + D * 0.28),
    "follow": P(drop=0.15, twist=(26, 44, 52), lean=15, tilt=6, head=14, shift=L * 0.07 + F * 0.05, clav=18,
                rhand=(78, 0.40, -0.10), blade=(122, -5), lhand=L * 0.35 + B * 0.25 + D * 0.30),
    "hold": SWEEP_HOLD,
}, keys=[(0.00, "ready", "smooth"), (0.18, "windup", "smooth"), (0.30, "hit", "fast"), (0.38, "follow", "out"),
         (0.80, "hold", "smooth"), (1.40, "ready", "smooth"), (1.80, "ready", "smooth")])

# ---------------------------------------------------------------- 2 段目: 返しの横なぎ (左 → 右)
BACK_HOLD = P(drop=0.13, twist=(-20, -32, -38), lean=10, tilt=-4, head=-10, shift=Rt * 0.04 + F * 0.03, clav=-8,
              rhand=(-72, 0.40, -0.14), blade=(-110, -12), lhand=L * 0.25 + F * 0.20 + D * 0.30)
BACKHAND = dict(hit=0.22, length=1.80, poses={
    "start": SWEEP_HOLD,
    "windup": P(drop=0.14, twist=(28, 48, 56), lean=8, tilt=5, head=16, shift=L * 0.06, clav=16,
                rhand=(92, 0.34, 0.02), blade=(140, 8), lhand=L * 0.30 + B * 0.20 + D * 0.28),
    "hit": P(drop=0.15, twist=(-8, -12, -14), lean=15, tilt=-2, head=-4, shift=Rt * 0.02 + F * 0.07, clav=-4,
             rhand=(-5, 0.46, -0.06), blade=(-25, 0), lhand=L * 0.30 + F * 0.15 + D * 0.30),
    "follow": P(drop=0.15, twist=(-26, -42, -50), lean=12, tilt=-5, head=-14, shift=Rt * 0.06 + F * 0.04, clav=-12,
                rhand=(-82, 0.42, -0.06), blade=(-122, 0), lhand=L * 0.25 + F * 0.25 + D * 0.28),
    "hold": BACK_HOLD,
    "ready": READY,
}, keys=[(0.00, "start", "smooth"), (0.12, "windup", "smooth"), (0.22, "hit", "fast"), (0.29, "follow", "out"),
         (0.86, "hold", "smooth"), (1.45, "ready", "smooth"), (1.80, "ready", "smooth")])

# ---------------------------------------------------------------- 3 段目: 袈裟斬り (右上 → 左下) → 斬り上げ (左下 → 右上)。2 回当たる
KESA_HIT2 = 0.24 + (0.107 - 0.054) * 3.33   # ゲームの 2 回目の当たり
KESA_HOLD = P(drop=0.10, twist=(-14, -24, -28), lean=2, tilt=-4, head=-6, shift=Rt * 0.04, clav=-6,
              rhand=(-50, 0.30, 0.32), blade=(-70, 55), lhand=L * 0.20 + F * 0.15 + D * 0.30)
KESA = dict(hit=0.24, length=1.90, poses={
    "start": BACK_HOLD,
    "windup": P(drop=0.10, twist=(-20, -30, -36), lean=-6, tilt=-8, head=-8, shift=Rt * 0.05 + B * 0.02, clav=-10,
                rhand=(-35, 0.20, 0.40), blade=(-30, 62), lhand=L * 0.15 + F * 0.25 + D * 0.10),
    "hit": P(drop=0.17, twist=(8, 14, 16), lean=22, tilt=6, head=4, shift=L * 0.04 + F * 0.08, clav=12,
             rhand=(20, 0.42, -0.20), blade=(35, -40), lhand=L * 0.30 + B * 0.10 + D * 0.32),
    "low": P(drop=0.19, twist=(16, 26, 30), lean=25, tilt=8, head=8, shift=L * 0.06 + F * 0.08, clav=16,
             rhand=(40, 0.36, -0.40), blade=(60, -60), lhand=L * 0.30 + B * 0.10 + D * 0.32),
    "hit2": P(drop=0.11, twist=(-10, -18, -20), lean=5, tilt=-6, head=-4, shift=Rt * 0.03 + F * 0.04, clav=-6,
              rhand=(-25, 0.42, 0.12), blade=(-45, 35), lhand=L * 0.25 + F * 0.10 + D * 0.32),
    "follow2": KESA_HOLD,
    "ready": READY,
}, keys=[(0.00, "start", "smooth"), (0.15, "windup", "smooth"), (0.24, "hit", "fast"), (0.31, "low", "out"),
         (KESA_HIT2, "hit2", "fast"), (KESA_HIT2 + 0.08, "follow2", "out"), (0.90, "follow2", "smooth"),
         (1.55, "ready", "smooth"), (1.90, "ready", "smooth")])

# ---------------------------------------------------------------- 4 段目: 踏み込んで突き
THRUST_LUNGE = L * 0.10 + F * 0.42
THRUST_HOLD = P(drop=0.22, twist=(-8, -12, -12), lean=22, head=0, shift=F * 0.14, clav=12,
                rhand=(-3, 0.52, -0.12), blade=(0, -4), lhand=L * 0.35 + B * 0.28 + D * 0.22, lstep=THRUST_LUNGE)
THRUST = dict(hit=0.32, length=2.30, poses={
    "start": KESA_HOLD,
    "windup": P(drop=0.20, twist=(-35, -52, -56), lean=8, tilt=-4, head=-8, shift=Rt * 0.03 + B * 0.08, clav=-18,
                rhand=(-80, 0.20, -0.30), blade=(-3, -2), lhand=L * 0.05 + F * 0.45 + D * 0.10),
    "hit": P(drop=0.23, twist=(-8, -12, -12), lean=25, head=0, shift=F * 0.16, clav=14,
             rhand=(-2, 0.54, -0.10), blade=(0, -3), lhand=L * 0.35 + B * 0.30 + D * 0.20, lstep=THRUST_LUNGE),
    "hold": THRUST_HOLD,
    "ready": READY,
}, keys=[(0.00, "start", "smooth"), (0.20, "windup", "smooth"), (0.32, "hit", "fast"), (0.40, "hold", "out"),
         (1.04, "hold", "smooth"), (1.85, "ready", "smooth"), (2.30, "ready", "smooth")])

# ---------------------------------------------------------------- 5 段目: 跳び上がって両手で振り下ろし
SLAM = dict(hit=0.70, length=2.80, poses={
    "start": THRUST_HOLD,
    "crouch": P(drop=0.26, twist=(-10, -14, -16), lean=22, head=-4, shift=B * 0.04, rhand=(-15, 0.30, -0.40), blade=(-10, -20),
                lhand="grip", lstep=L * 0.08 + F * 0.10, rstep=Rt * 0.08 + B * 0.06),
    "rise": P(drop=-0.70, twist=(0, 0, 0), lean=-6, head=0, rhand=(-10, 0.10, 0.45), blade=(0, 80),
              lhand="grip", lstep=L * 0.08 + F * 0.10, rstep=Rt * 0.08 + B * 0.06, lift=0.45),
    "apex": P(drop=-1.10, twist=(0, 0, 0), lean=-12, head=0, rhand=(0, 0.05, 0.52), blade=(180, 50),
              lhand="grip", lstep=L * 0.08 + F * 0.10, rstep=Rt * 0.08 + B * 0.06, lift=0.60),
    "fall": P(drop=-0.55, twist=(0, 0, 0), lean=10, head=0, rhand=(0, 0.30, 0.30), blade=(0, 60),
              lhand="grip", lstep=L * 0.08 + F * 0.10, rstep=Rt * 0.08 + B * 0.06, lift=0.35),
    "hit": P(drop=0.32, twist=(0, 0, 0), lean=32, head=0, shift=F * 0.10, clav=10, rhand=(0, 0.50, -0.70), blade=(0, -55),
             lhand="grip", lstep=L * 0.08 + F * 0.30, rstep=Rt * 0.08 + B * 0.10),
    "hold": P(drop=0.30, twist=(0, 0, 0), lean=30, head=0, shift=F * 0.10, clav=10, rhand=(0, 0.50, -0.72), blade=(0, -60),
              lhand="grip", lstep=L * 0.08 + F * 0.30, rstep=Rt * 0.08 + B * 0.10),
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
            raw = 0.0 if t1 <= t0 else (t - t0) / (t1 - t0)
            x = ease(raw, kind)
            pa, pb = move["poses"][a], move["poses"][b]
            p = {k: lerp(pa[k], pb[k], x) for k in pa}
            # 足を動かす区間は少し持ち上げる (床を滑らない)
            for k in ("lstep", "rstep"):
                if (pa[k] - pb[k]).length > 0.03:
                    p[k] = p[k] + U * 0.07 * math.sin(math.pi * raw)
            return p
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
        body_move = U * -p["drop"] + p["shift"]
        rr.move(arm, "Bip001", body_move)
        pel, sp, sp1 = p["twist"]
        lean, tilt = p["lean"] * 0.5, p["tilt"] * 0.5
        rr.turn(arm, "Bip001 Pelvis", rr.R(rr.Z, pel))
        # X (キャラの左) まわりに + で前へ倒れる、前 (-Y) まわりに + で左へ傾く
        rr.turn(arm, "Bip001 Spine", rr.R(rr.Z, sp - pel) @ rr.R(rr.X, lean) @ rr.R(F, tilt))
        rr.turn(arm, "Bip001 Spine1", rr.R(rr.Z, sp1 - sp) @ rr.R(rr.X, lean) @ rr.R(F, tilt))
        # 首は前を見続ける: 胸のひねりを打ち消して、head の向きにする
        rr.turn(arm, "Bip001 Head", rr.R(rr.Z, p["head"] - sp1))
        rr.turn(arm, "Bip001 R Clavicle", rr.R(rr.Z, p["clav"]))
        rr.key(arm, body + weapon, f)

        yaw, dist, h = p["rhand"]
        hand_r.location = shoulder_r + body_move + horizontal(yaw) * dist + U * h
        if not isinstance(p["lhand"], str):
            hand_l.location = shoulder_l + body_move + p["lhand"]
        pole_r.location = shoulder_r + body_move + Rt * 0.35 + B * 0.25 + D * 0.45
        pole_l.location = shoulder_l + body_move + L * 0.35 + B * 0.25 + D * 0.45
        lift = U * p["lift"] + B * p["lift"] * 0.3
        ft_l.location = foot_l + p["lstep"] + lift
        ft_r.location = foot_r + p["rstep"] + lift
        knee_l.location = foot_l + p["lstep"] + lift + F * 0.8 + U * 0.5 + L * 0.15
        knee_r.location = foot_r + p["rstep"] + lift + F * 0.8 + U * 0.5 + Rt * 0.15
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
