"""
大剣の技 (通常攻撃の 1〜5 段目) を両手持ちで作る (ゲームの骨格 Bip001 と、武器の骨 WeaponHolder_0)。

    blender --background --python sword_moves.py -- <rig_キャラ名_motions.glb> <書き出すフォルダ> [<プレビューのフォルダ>] [技の名前 ...]

技の名前を省くと全部作る。書き出すファイルは Sword<名前>.glb、アニメーションの名前は RusK_Sword<名前>。

作り方 (rusk_rig.py):
  - 基準の姿勢はゲームの立ち姿勢 (Idle)、刀の握りと右手の指は土台のキャラの 1 段目 (RedLightCombo0) から借りる
  - 両手持ち: 右手は柄の上、左手は柄の下を握る (two = 1)。立ち姿勢 (始まりと終わり) は右手だけ (two = 0)
  - 手の位置は「胸の前から見た水平の角度・距離・高さ」(肘が曲がる近さ)、刃は「水平の角度・上下の角度」で決める
    (角度で補間すると弧を描く)
  - 刃筋: 1 コマごとに剣が動く方向を求め、刃のふち (剣の幅の向き) をそちらへ向ける (平らな面で叩かない)
  - 体: 腰のひねり・前かがみ・横への傾き・沈み込み (drop、負なら跳ぶ)・体重移動 (shift)・右肩の前後 (clav)。
    首は前を見続ける (head は首の向き)
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
GRIP = 0.17   # 柄の、右手から左手までの長さ (m)

# 戦いの構え: 左足を半歩前、右足を後ろへ開く
STANCE_L = L * 0.08 + F * 0.16
STANCE_R = Rt * 0.06 + B * 0.14


def P(drop=0.13, twist=(0, 0, 0), lean=8.0, tilt=0.0, head=0.0, shift=None, clav=0.0,
      hands=(0, 0.30, -0.25), blade=(0, 30), two=1.0, lhand=None, lstep=None, rstep=None, lift=0.0):
    """
    姿勢。hands = 右手の位置 (胸の前から見た角度, 距離, 高さ)、blade = (角度, 上下の角度)。角度は 0 = 前、+ = 左 (度)。
    two = 両手持ちの度合い (1 = 左手が柄を握る)、lhand = 片手のときの左手 (左肩から見た位置)。
    twist = (腰, 背骨, 胸) の向き (+ = 左)、head = 首の向き、lean = 前かがみ、tilt = 左へ傾く、shift = 腰の動き、clav = 右肩を前へ
    """
    return dict(drop=drop, twist=twist, lean=lean, tilt=tilt, head=head, shift=shift if shift is not None else ZERO, clav=clav,
                hands=hands, blade=blade, two=two,
                lhand=lhand if lhand is not None else L * 0.10 + F * 0.10 + D * 0.45,
                lstep=lstep if lstep is not None else STANCE_L, rstep=rstep if rstep is not None else STANCE_R, lift=lift)


# 立ち姿勢 (ゲームの Idle と同じ。始まりと終わり。右手だけで剣を下げて持つ)
READY = P(drop=0.0, lean=0, hands=(-25, 0.28, -0.55), blade=(-15, -40), two=0.0, lstep=ZERO, rstep=ZERO)

# ---------------------------------------------------------------- 1 段目: 横なぎ (右 → 左)
SWEEP_HOLD = P(drop=0.14, twist=(22, 36, 44), lean=10, tilt=4, head=12, shift=L * 0.05 + F * 0.04, clav=12,
               hands=(55, 0.30, -0.22), blade=(118, -12))
SWEEP = dict(hit=0.30, length=1.80, poses={
    "ready": READY,
    "windup": P(drop=0.14, twist=(-28, -44, -52), lean=8, tilt=-5, head=-10, shift=Rt * 0.07 + B * 0.02, clav=-14,
                hands=(-78, 0.28, -0.10), blade=(-150, 18)),
    "hit": P(drop=0.16, twist=(8, 14, 16), lean=14, tilt=2, head=4, shift=L * 0.03 + F * 0.08, clav=10,
             hands=(0, 0.42, -0.20), blade=(32, -4)),
    "follow": P(drop=0.16, twist=(28, 46, 56), lean=13, tilt=6, head=14, shift=L * 0.07 + F * 0.05, clav=18,
                hands=(68, 0.32, -0.20), blade=(132, -10)),
    "hold": SWEEP_HOLD,
}, keys=[(0.00, "ready", "smooth"), (0.18, "windup", "smooth"), (0.30, "hit", "fast"), (0.38, "follow", "out"),
         (0.80, "hold", "smooth"), (1.40, "ready", "smooth"), (1.80, "ready", "smooth")])

# ---------------------------------------------------------------- 2 段目: 返しの横なぎ (左 → 右)
BACK_HOLD = P(drop=0.14, twist=(-22, -36, -44), lean=10, tilt=-4, head=-12, shift=Rt * 0.05 + F * 0.03, clav=-10,
              hands=(-58, 0.30, -0.20), blade=(-118, -12))
BACKHAND = dict(hit=0.22, length=1.80, poses={
    "start": SWEEP_HOLD,
    "windup": P(drop=0.15, twist=(30, 50, 58), lean=8, tilt=5, head=16, shift=L * 0.06, clav=16,
                hands=(75, 0.26, -0.05), blade=(150, 16)),
    "hit": P(drop=0.16, twist=(-8, -14, -16), lean=14, tilt=-2, head=-4, shift=Rt * 0.02 + F * 0.07, clav=-4,
             hands=(0, 0.42, -0.18), blade=(-32, -4)),
    "follow": P(drop=0.16, twist=(-28, -46, -56), lean=12, tilt=-5, head=-14, shift=Rt * 0.07 + F * 0.04, clav=-12,
                hands=(-68, 0.32, -0.18), blade=(-132, -10)),
    "hold": BACK_HOLD,
    "ready": READY,
}, keys=[(0.00, "start", "smooth"), (0.12, "windup", "smooth"), (0.22, "hit", "fast"), (0.29, "follow", "out"),
         (0.86, "hold", "smooth"), (1.45, "ready", "smooth"), (1.80, "ready", "smooth")])

# ---------------------------------------------------------------- 3 段目: 袈裟斬り (右上 → 左下) → 斬り上げ (左下 → 右上)。2 回当たる
KESA_HIT2 = 0.24 + (0.107 - 0.054) * 3.33   # ゲームの 2 回目の当たり
KESA_HOLD = P(drop=0.10, twist=(-14, -24, -30), lean=4, tilt=-4, head=-8, shift=Rt * 0.04, clav=-6,
              hands=(-35, 0.24, 0.18), blade=(-60, 58))
KESA = dict(hit=0.24, length=1.90, poses={
    "start": BACK_HOLD,
    "windup": P(drop=0.11, twist=(-24, -36, -44), lean=0, tilt=-8, head=-10, shift=Rt * 0.05 + B * 0.03, clav=-10,
                hands=(-30, 0.14, 0.30), blade=(-170, 45)),
    "hit": P(drop=0.18, twist=(8, 14, 18), lean=22, tilt=6, head=4, shift=L * 0.04 + F * 0.09, clav=12,
             hands=(10, 0.40, -0.22), blade=(25, -38)),
    "low": P(drop=0.20, twist=(18, 28, 34), lean=26, tilt=8, head=8, shift=L * 0.06 + F * 0.09, clav=16,
             hands=(35, 0.32, -0.38), blade=(55, -62)),
    "hit2": P(drop=0.12, twist=(-10, -18, -22), lean=8, tilt=-6, head=-4, shift=Rt * 0.03 + F * 0.05, clav=-6,
              hands=(-15, 0.36, 0.02), blade=(-40, 32)),
    "follow2": KESA_HOLD,
    "ready": READY,
}, keys=[(0.00, "start", "smooth"), (0.15, "windup", "smooth"), (0.24, "hit", "fast"), (0.31, "low", "out"),
         (KESA_HIT2, "hit2", "fast"), (KESA_HIT2 + 0.08, "follow2", "out"), (0.90, "follow2", "smooth"),
         (1.55, "ready", "smooth"), (1.90, "ready", "smooth")])

# ---------------------------------------------------------------- 4 段目: 踏み込んで突き
THRUST_LUNGE = L * 0.10 + F * 0.42
THRUST_HOLD = P(drop=0.22, twist=(-10, -14, -16), lean=20, head=0, shift=F * 0.14, clav=10,
                hands=(-5, 0.44, -0.16), blade=(0, -4), lstep=THRUST_LUNGE)
THRUST = dict(hit=0.32, length=2.30, poses={
    "start": KESA_HOLD,
    "windup": P(drop=0.20, twist=(-38, -55, -60), lean=8, tilt=-4, head=-8, shift=Rt * 0.03 + B * 0.08, clav=-16,
                hands=(-55, 0.18, -0.24), blade=(-2, -2)),
    "hit": P(drop=0.23, twist=(-10, -14, -16), lean=24, head=0, shift=F * 0.16, clav=12,
             hands=(-3, 0.48, -0.14), blade=(0, -3), lstep=THRUST_LUNGE),
    "hold": THRUST_HOLD,
    "ready": READY,
}, keys=[(0.00, "start", "smooth"), (0.20, "windup", "smooth"), (0.32, "hit", "fast"), (0.40, "hold", "out"),
         (1.04, "hold", "smooth"), (1.85, "ready", "smooth"), (2.30, "ready", "smooth")])

# ---------------------------------------------------------------- 5 段目: 跳び上がって振り下ろし
JUMP_L, JUMP_R = L * 0.08 + F * 0.10, Rt * 0.08 + B * 0.06
SLAM = dict(hit=0.70, length=2.80, poses={
    "start": THRUST_HOLD,
    "crouch": P(drop=0.28, twist=(-10, -14, -16), lean=24, head=-4, shift=B * 0.04, hands=(-15, 0.24, -0.35), blade=(-20, -30),
                lstep=JUMP_L, rstep=JUMP_R),
    "rise": P(drop=-0.70, lean=-4, hands=(0, 0.14, 0.30), blade=(0, 80), lstep=JUMP_L, rstep=JUMP_R, lift=0.45),
    "apex": P(drop=-1.10, lean=-8, hands=(0, 0.06, 0.42), blade=(180, 45), lstep=JUMP_L, rstep=JUMP_R, lift=0.60),
    "fall": P(drop=-0.55, lean=10, hands=(0, 0.24, 0.28), blade=(0, 70), lstep=JUMP_L, rstep=JUMP_R, lift=0.35),
    "hit": P(drop=0.34, lean=34, shift=F * 0.10, clav=10, hands=(0, 0.44, -0.55), blade=(0, -50),
             lstep=L * 0.08 + F * 0.30, rstep=Rt * 0.08 + B * 0.10),
    "hold": P(drop=0.32, lean=32, shift=F * 0.10, clav=10, hands=(0, 0.44, -0.58), blade=(0, -55),
              lstep=L * 0.08 + F * 0.30, rstep=Rt * 0.08 + B * 0.10),
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
    chest = (shoulder_r + shoulder_l) * 0.5 + D * 0.08
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
    free_l = []
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

        yaw, dist, h = p["hands"]
        hand_r.location = chest + body_move + horizontal(yaw) * dist + U * h
        free_l.append(shoulder_l + body_move + p["lhand"])
        hand_l.location = free_l[-1]
        pole_r.location = shoulder_r + body_move + Rt * 0.40 + B * 0.10 + D * 0.40
        pole_l.location = shoulder_l + body_move + L * 0.40 + B * 0.10 + D * 0.40
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

    # 刃の向き: 剣が動く方向 (刃の向きの変化) に刃のふちを向ける。動きが小さい・刃の向きと同じ (突き) なら前の向きのまま
    blades = [horizontal(*pose_at(move, f / FPS)["blade"]) for f in range(frames + 1)]
    edge = rr.UP.copy()
    edges = []
    for f in range(frames + 1):
        a, b = blades[max(0, f - 1)], blades[min(frames, f + 1)]
        v = b - a
        v = v - blades[f] * v.dot(blades[f])
        if v.length > 0.02:
            edge = v.normalized()
        edges.append(edge.copy())

    # 手の向き (刃の向きから)、両手持ちなら左手を柄へ (1 コマずつ、IK で腕が決まった後に)
    for f in range(frames + 1):
        scene.frame_set(f)
        p = pose_at(move, f / FPS)
        c.influence = 0.0
        rr.bpy.context.view_layer.update()
        hand_r_rot.rotation_quaternion = rr.hand_rot_for_blade(arm, "Bip001 R Hand", "WeaponHolder_0", blades[f], edges[f])
        hand_r_rot.keyframe_insert("rotation_quaternion", frame=f)
        c.influence = 1.0
        rr.bpy.context.view_layer.update()
        holder = rr.arm_matrix(arm, "WeaponHolder_0")
        blade = (holder.to_3x3() @ rr.X).normalized()
        grip = holder.translation - blade * GRIP  # 柄の、右手より下
        hand_l.location = free_l[f].lerp(grip, max(0.0, min(1.0, p["two"])))
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
