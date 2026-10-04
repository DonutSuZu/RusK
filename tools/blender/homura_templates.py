"""
ホムラ (キャラの Mod Pack「Pyra」) の動きの雛形を、ゲームの動作ぜんぶ (赤悠 1006 の 34 個) について作る。
Blender で手直しするための「原型」: 大事なコマにだけキーを打ち、当たる・受付・刀をしまう時刻をマーカーにする。

    blender --background --python homura_templates.py -- <rig_RedLight_skin_motions.glb> <書き出すフォルダ> [プレビュー]

書き出すもの (書き出すフォルダの中):
  homura_templates.blend   … 骨格 + 体 + 雛形の動作 (Homura_〈ゲームの動作〉) + ゲームの元の動作 (参考)。ゲームのモデル入りなので配らない
  Homura_Templates.glb     … 雛形の動作だけ (骨格と動き。メッシュなし) → Pack の motions\ に置いてすぐ試せる
  homura_bindings.json     … character.json の "motions" に貼る割り当て (当たる瞬間の秒つき)
  preview_*.png            … (プレビューを付けたとき) いくつかの動作のコマの絵

作り方:
  - 攻撃 10 個: 両手持ちの大剣の技 (sword_moves.py の通常攻撃 5 段 + ここで足すダッシュ攻撃・スペシャル・連携 3 つ)。
    手は IK、刃筋は剣の動く方向に合わせる。頭は胸のひねりに半分ついていく
  - 立ち姿勢 (Idle): 右手は剣を下げて持ち、左手を胸の前に添える。ゆっくり呼吸するループ (3 秒)
  - ほか 23 個: ゲームの動作を 0.15〜0.3 秒ごと (と、受付などの時刻) に打ち直し、ホムラの姿勢 (胸を張る・首を少しかしげる) を重ねる
"""
import json
import math
import os
import struct
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import rusk_rig as rr  # noqa: E402
import sword_moves as sm  # noqa: E402

bpy = rr.bpy
F, B, L, Rt, U, D = rr.FRONT, rr.BACK, rr.LEFT, rr.RIGHT, rr.UP, rr.DOWN
ZERO = F * 0
FPS = sm.FPS
P = sm.P
READY = sm.READY
SW, BH, KS, TH, SL = (sm.MOVES[n]["poses"] for n in ("Sweep", "Backhand", "Kesa", "Thrust", "Slam"))


def W(pose, **kw):
    """姿勢を写して一部だけ変える"""
    q = dict(pose)
    q.update(kw)
    return q


# ---------------------------------------------------------------- 立ち姿勢
IDLE = P(drop=0.0, lean=-2, tilt=2, head=6, hands=(-25, 0.28, -0.55), blade=(-15, -40), two=0.0,
         lhand=Rt * 0.10 + F * 0.17 + D * 0.22, lstep=ZERO, rstep=ZERO)
IDLE_BREATH = W(IDLE, drop=-0.008, lean=-4, tilt=3, head=8, lhand=Rt * 0.10 + F * 0.18 + D * 0.20)
IDLE_MOVE = dict(hit=None, length=3.0, poses={"idle": IDLE, "breath": IDLE_BREATH},
                 keys=[(0.0, "idle", "smooth"), (1.5, "breath", "smooth"), (3.0, "idle", "smooth")])

# ---------------------------------------------------------------- 技の部品 (通常攻撃の姿勢を借りる)
CHARGE = P(drop=0.18, lean=-4, twist=(0, 6, 8), head=0, hands=(0, 0.10, 0.40), blade=(180, 50))       # 剣を頭上に振りかぶる
SLAM_HIT = P(drop=0.28, lean=28, shift=F * 0.08, clav=10, hands=(0, 0.44, -0.45), blade=(0, -45))      # 真上から叩きつける
SLAM_HOLD = W(SLAM_HIT, drop=0.27, lean=26, hands=(0, 0.44, -0.50), blade=(0, -52))
RISE = P(drop=0.12, lean=6, twist=(-12, -20, -24), tilt=-4, head=-4, hands=(-10, 0.38, 0.15), blade=(-20, 50))  # 斬り上げ
CROUCH = P(drop=0.22, lean=20, twist=(-10, -14, -16), shift=B * 0.05, hands=(-40, 0.22, -0.30), blade=(-30, -20))
LUNGE = L * 0.10 + F * 0.42

# ---------------------------------------------------------------- ダッシュ攻撃: 踏み込みの突き → 横なぎ → 返し → 斬り上げ (4 回当たる)
DASH = dict(hit=0.30, length=2.8, poses={
    "ready": READY, "crouch": CROUCH,
    "h1": W(TH["hit"]), "wind2": W(SW["windup"], lstep=LUNGE),
    "h2": W(SW["hit"], lstep=LUNGE), "f2": W(SW["follow"], lstep=LUNGE),
    "h3": W(BH["hit"], lstep=LUNGE), "h4": W(RISE, lstep=LUNGE),
    "hold": W(KS["follow2"], lstep=LUNGE),
}, keys=[(0.00, "ready", "smooth"), (0.14, "crouch", "smooth"), (0.30, "h1", "fast"), (0.55, "wind2", "smooth"),
         (0.69, "h2", "fast"), (0.80, "f2", "out"), (0.91, "h3", "fast"), (1.02, "h4", "fast"), (1.51, "hold", "out"),
         (2.30, "ready", "smooth"), (2.80, "ready", "smooth")])

# ---------------------------------------------------------------- スペシャル: 振りかぶって叩きつけ → 斬り上げ → 回転斬り → 連撃 → 突き (7 回)
SPECIAL = dict(hit=0.40, length=3.4, poses={
    "ready": READY, "charge": CHARGE, "h1": SLAM_HIT, "h2": RISE,
    "wind3": SW["windup"], "h3": SW["hit"], "f3": SW["follow"],
    "h4": BH["hit"], "h5": KS["hit"], "h6": KS["hit2"], "h7": TH["hit"], "hold": TH["hold"],
}, keys=[(0.00, "ready", "smooth"), (0.25, "charge", "smooth"), (0.40, "h1", "fast"), (0.54, "h2", "fast"),
         (0.72, "wind3", "smooth"), (0.85, "h3", "fast"), (0.98, "f3", "out"), (1.21, "h4", "fast"),
         (1.33, "h5", "fast"), (1.44, "h6", "fast"), (1.54, "h7", "fast"), (2.32, "hold", "out"),
         (3.00, "ready", "smooth"), (3.40, "ready", "smooth")])

# ---------------------------------------------------------------- 連携のスペシャル: 大きく振りかぶって叩きつける
QTE_SPECIAL = dict(hit=0.60, length=2.4, poses={"ready": READY, "charge": W(CHARGE, drop=0.24, lean=-8), "hit": SLAM_HIT, "hold": SLAM_HOLD},
                   keys=[(0.00, "ready", "smooth"), (0.38, "charge", "smooth"), (0.60, "hit", "fast"), (1.18, "hold", "out"),
                         (2.00, "ready", "smooth"), (2.40, "ready", "smooth")])

# ---------------------------------------------------------------- 連携の通常攻撃: 速い 3 連撃 (横なぎ → 振り抜き → 返し)
QTE_NORMAL = dict(hit=0.45, length=2.2, poses={"ready": READY, "wind": SW["windup"], "h1": SW["hit"], "h2": SW["follow"],
                                               "h3": BH["hit"], "hold": BH["hold"]},
                  keys=[(0.00, "ready", "smooth"), (0.30, "wind", "smooth"), (0.45, "h1", "fast"), (0.54, "h2", "out"),
                        (0.64, "h3", "fast"), (0.96, "hold", "out"), (1.80, "ready", "smooth"), (2.20, "ready", "smooth")])

# ---------------------------------------------------------------- 連携のダッシュ攻撃: 低く構えて一気に突き抜ける
QTE_DASH = dict(hit=0.35, length=1.8, poses={"ready": READY, "crouch": CROUCH, "h1": TH["hit"],
                                             "h2": W(TH["hit"], drop=0.25, lean=26, hands=(-3, 0.50, -0.14)), "hold": TH["hold"]},
                keys=[(0.00, "ready", "smooth"), (0.18, "crouch", "smooth"), (0.35, "h1", "fast"), (0.42, "h2", "out"),
                      (0.60, "hold", "out"), (1.40, "ready", "smooth"), (1.80, "ready", "smooth")])

# ゲームの動作 → (雛形, ホムラの技の説明)
ATTACKS = {
    "RedLightCombo0": (sm.MOVES["Sweep"], "通常攻撃 1 段目: 両手で右から左へ横なぎ"),
    "RedLightCombo1": (sm.MOVES["Backhand"], "通常攻撃 2 段目: 返しの横なぎ (左 → 右)"),
    "RedLightCombo2": (sm.MOVES["Kesa"], "通常攻撃 3 段目: 袈裟斬り → 斬り上げ (2 回当たる)"),
    "RedLightCombo3": (sm.MOVES["Thrust"], "通常攻撃 4 段目: 踏み込んで突き"),
    "RedLightCombo4": (sm.MOVES["Slam"], "通常攻撃 5 段目: 跳び上がって振り下ろす"),
    "DashAttack": (DASH, "ダッシュ攻撃: 踏み込みの突き → 横なぎ → 返し → 斬り上げ (4 回)"),
    "SpecialAttack": (SPECIAL, "スペシャル: 頭上から叩きつけ → 斬り上げ → 回転斬り → 連撃 → 突き (7 回)。炎の演出を想像して大きく"),
    "SpecialAttack_QTE_RedLight": (QTE_SPECIAL, "連携のスペシャル: 大きく振りかぶって叩きつける"),
    "NormalAttack_QTE_RedLight": (QTE_NORMAL, "連携の通常攻撃: 速い 3 連撃"),
    "DashAttack_QTE_RedLight": (QTE_DASH, "連携のダッシュ攻撃: 低く構えて一気に突き抜ける"),
}

# ゲームの動作を打ち直すもの → 説明 (手直しの方針)
FROM_GAME = {
    "Run": "走る (ループ)。ホムラ: 腕の振りを小さめ、胸を張る",
    "FastRun": "速く走る (ループ)。剣を持たない前傾の全力走り",
    "RunStop": "走るのをやめる",
    "FastRunStop": "速く走るのをやめる",
    "RunTurn": "走りながら振り向く",
    "DashFront": "前へ回避。低く滑り込む",
    "DashBack": "後ろへ回避。軽く跳んで下がる",
    "Defence_1": "防御の構え。剣を横に構えて受ける",
    "Defence_2": "防御の構え (2)",
    "DefenceAccept": "防御で受けた",
    "Parry_Left": "パリィ (左)", "Parry_Right": "パリィ (右)",
    "Parry_LeftDown": "パリィ (左下)", "Parry_RightDown": "パリィ (右下)",
    "RedLight_HitLeft": "攻撃を受けた (左)", "RedLight_HitMiddle": "攻撃を受けた (正面)",
    "RedLight_HitRight_Official": "攻撃を受けた (右)",
    "RedLight_HitHeavy_Middile_Official": "大きく攻撃を受けた (正面)",
    "RedLight_HitHeavy_Right_Official": "大きく攻撃を受けた (右)",
    "Die": "倒れる。膝から崩れる",
    "Born": "登場 (10 秒)。剣を呼び出して構える演出に",
    "JumpNextLevel": "次の場所へ跳ぶ",
    "Interection": "調べる・話しかける",
}


# ---------------------------------------------------------------- ゲームの時間割 (glb の extras)

def game_info(glb):
    """動作の名前 → {length, playSpeed, loop, hits, accept, weapon_off} (進み具合 0〜1)"""
    with open(glb, "rb") as f:
        head = f.read(20)
        n = struct.unpack("<I", head[12:16])[0]
        j = json.loads(f.read(n))
    out = {}
    for a in j.get("animations", []):
        e = a.get("extras", {})
        ev = e.get("events", [])
        first = lambda key: next((float(x.split()[0]) for x in ev if key in x), None)
        out[a["name"]] = dict(length=float(e.get("length", 1)), speed=float(e.get("playSpeed", 1) or 1), loop=bool(e.get("loop")),
                              hits=[float(h) for h in e.get("hits", [])], accept=first("ComboRunAble"), weapon_off=first("WeaponOff"))
    return out


# ---------------------------------------------------------------- キーを減らす・マーカー

def fcurves(act):
    try:
        return list(act.fcurves)
    except AttributeError:
        pass
    out = []
    for layer in act.layers:
        for strip in layer.strips:
            for bag in strip.channelbags:
                out.extend(bag.fcurves)
    return out


def thin(act, frames):
    """frames のコマのキーだけ残す (手直ししやすいように)。間はなめらかにつなぐ"""
    keep = set(frames)
    for fc in fcurves(act):
        pts = fc.keyframe_points
        for i in range(len(pts) - 1, -1, -1):
            if round(pts[i].co.x) not in keep:
                pts.remove(pts[i])
        for kp in pts:
            kp.interpolation = "BEZIER"
            kp.handle_left_type = kp.handle_right_type = "AUTO_CLAMPED"
        fc.update()


def mark(act, marks):
    for label, t in marks:
        m = act.pose_markers.new(label)
        m.frame = round(t * FPS)


def finish(name, frames, marks, note):
    act = bpy.data.objects[ARM_NAME].animation_data.action
    act.name = name
    act.use_fake_user = True
    thin(act, frames)
    mark(act, marks)
    act["rusk_note"] = note
    return act


def cleanup():
    """目印 (Empty) とその動作を消す"""
    for o in list(bpy.data.objects):
        if o.type == "EMPTY":
            bpy.data.objects.remove(o, do_unlink=True)
    for a in list(bpy.data.actions):
        if a.users == 0 and not a.use_fake_user:
            bpy.data.actions.remove(a)


# ---------------------------------------------------------------- 攻撃 (と立ち姿勢): sword_moves の作り方 (IK) で、骨格を読み直さずに

def build_moves(arm, base, geo, name, move):
    body = [pb.name for pb in arm.pose.bones if pb.name.startswith("Bip001") and pb.name not in rr.TWISTS]
    weapon = ["BN_weapon_01", "WeaponHolder_0"]
    arm.animation_data.action = None
    shoulder_r, shoulder_l, chest = geo["shoulder_r"], geo["shoulder_l"], geo["chest"]
    foot_r, foot_l = geo["foot_r"], geo["foot_l"]

    hand_r, hand_r_rot, pole_r = rr.empty("ctl_hand_R"), rr.empty("ctl_hand_R_rot"), rr.empty("pole_elbow_R")
    hand_l, pole_l = rr.empty("ctl_hand_L"), rr.empty("pole_elbow_L")
    ft_r, ft_l = rr.empty("ctl_foot_R", foot_r), rr.empty("ctl_foot_L", foot_l)
    ft_r.rotation_quaternion, ft_l.rotation_quaternion = geo["foot_rot_r"], geo["foot_rot_l"]
    knee_r, knee_l = rr.empty("pole_knee_R"), rr.empty("pole_knee_L")

    scene = bpy.context.scene
    frames = round(move["length"] * FPS)
    scene.frame_start, scene.frame_end = 0, frames
    free_l = []
    for f in range(frames + 1):
        p = sm.pose_at(move, f / FPS)
        rr.apply(arm, base)
        body_move = U * -p["drop"] + p["shift"]
        rr.move(arm, "Bip001", body_move)
        pel, sp, sp1 = p["twist"]
        lean, tilt = p["lean"] * 0.5, p["tilt"] * 0.5
        rr.turn(arm, "Bip001 Pelvis", rr.R(rr.Z, pel))
        rr.turn(arm, "Bip001 Spine", rr.R(rr.Z, sp - pel) @ rr.R(rr.X, lean) @ rr.R(F, tilt))
        rr.turn(arm, "Bip001 Spine1", rr.R(rr.Z, sp1 - sp) @ rr.R(rr.X, lean) @ rr.R(F, tilt))
        # 頭は胸のひねりに半分ついていき (固まって見えないように)、head の分だけ向く。少しかしげる
        rr.turn(arm, "Bip001 Head", rr.R(rr.Z, p["head"] - sp1 * 0.5) @ rr.R(F, 3))
        rr.turn(arm, "Bip001 R Clavicle", rr.R(rr.Z, p["clav"]))
        rr.key(arm, body + weapon, f)

        yaw, dist, h = p["hands"]
        hand_r.location = chest + body_move + sm.horizontal(yaw) * dist + U * h
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

    blades = [sm.horizontal(*sm.pose_at(move, f / FPS)["blade"]) for f in range(frames + 1)]
    edge = rr.UP.copy()
    edges = []
    for f in range(frames + 1):
        a, b = blades[max(0, f - 1)], blades[min(frames, f + 1)]
        v = b - a
        v = v - blades[f] * v.dot(blades[f])
        if v.length > 0.02:
            edge = v.normalized()
        edges.append(edge.copy())

    for f in range(frames + 1):
        scene.frame_set(f)
        p = sm.pose_at(move, f / FPS)
        c.influence = 0.0
        bpy.context.view_layer.update()
        hand_r_rot.rotation_quaternion = rr.hand_rot_for_blade(arm, "Bip001 R Hand", "WeaponHolder_0", blades[f], edges[f])
        hand_r_rot.keyframe_insert("rotation_quaternion", frame=f)
        c.influence = 1.0
        bpy.context.view_layer.update()
        holder = rr.arm_matrix(arm, "WeaponHolder_0")
        blade = (holder.to_3x3() @ rr.X).normalized()
        grip = holder.translation - blade * sm.GRIP
        hand_l.location = free_l[f].lerp(grip, max(0.0, min(1.0, p["two"])))
        hand_l.keyframe_insert("location", frame=f)

    rr.bake(arm, body + weapon, 0, frames, name)
    rr.follow_twists(arm, base, 0, frames)
    # 残すコマ: 姿勢を決めたコマと、長い区間の真ん中 (弧を保つ)
    keys = [t for t, _, _ in move["keys"]]
    times = set(keys)
    for t0, t1 in zip(keys, keys[1:]):
        if t1 - t0 > 0.2:
            times.add((t0 + t1) / 2)
    return sorted({round(t * FPS) for t in times})


# ---------------------------------------------------------------- ゲームの動作を打ち直す

def homura_posture(arm):
    """ホムラの姿勢: 胸を少し張り、首を少しかしげる"""
    rr.turn(arm, "Bip001 Spine1", rr.R(rr.X, -3))
    rr.turn(arm, "Bip001 Head", rr.R(F, 3))


def build_from_game(arm, samples, bones):
    arm.animation_data.action = None
    frames = []
    for t, mats in samples:
        f = round(t * FPS)
        rr.apply(arm, mats)
        homura_posture(arm)
        rr.key(arm, bones, f)
        frames.append(f)
    return frames


# ---------------------------------------------------------------- 本体

argv = sys.argv[sys.argv.index("--") + 1:]
rig_path, out_dir = argv[0], argv[1]
preview = len(argv) > 2
os.makedirs(out_dir, exist_ok=True)
info = game_info(rig_path)

arm = rr.load(rig_path)
ARM_NAME = arm.name
game_actions = [a.name for a in bpy.data.actions]

# 先にゲームの姿勢を全部取っておく (fix_chains で骨の向きを変える前に。変えた分は fix_chains が補正する)
base = rr.sample(arm, "Idle", 0.0)
grip_bones = ["BN_weapon_01", "WeaponHolder_0"] + [pb.name for pb in arm.pose.bones if pb.name.startswith("Bip001 R Finger")]
base = rr.graft(arm, base, rr.sample(arm, "RedLightCombo0", 0.1), grip_bones)
game_samples = {}
for name in FROM_GAME:
    gi = info.get(name)
    if gi is None or rr.action(name) is None:
        print("[rusk] ゲームの動作がありません:", name)
        continue
    seconds = gi["length"] / gi["speed"]
    step = 0.15 if seconds <= 3.0 else 0.3
    ts = {0.0, seconds}
    t = step
    while t < seconds - step * 0.5:
        ts.add(round(t, 3))
        t += step
    for ev in (gi["accept"], gi["weapon_off"]):
        if ev is not None and 0 < ev < 1:
            ts.add(round(ev * seconds, 3))
    ts = sorted(ts)
    samples = [(t, rr.sample(arm, name, t / seconds)) for t in ts]
    if gi["loop"]:
        samples[-1] = (seconds, samples[0][1])  # ループ: 最後を最初と同じ姿勢に
    game_samples[name] = (seconds, gi, samples)

rr.fix_chains(arm, base, *[m for _, _, s in game_samples.values() for _, m in s])
rr.apply(arm, base)
geo = dict(shoulder_r=rr.world(arm, "Bip001 R UpperArm"), shoulder_l=rr.world(arm, "Bip001 L UpperArm"),
           foot_r=rr.world(arm, "Bip001 R Foot"), foot_l=rr.world(arm, "Bip001 L Foot"),
           foot_rot_r=rr.world_rot(arm, "Bip001 R Foot").to_quaternion(), foot_rot_l=rr.world_rot(arm, "Bip001 L Foot").to_quaternion())
geo["chest"] = (geo["shoulder_r"] + geo["shoulder_l"]) * 0.5 + D * 0.08

made = []        # (ゲームの動作, 雛形の名前, 当たる瞬間の秒)
notes = []

# 立ち姿勢
frames = build_moves(arm, base, geo, "Homura_Idle", IDLE_MOVE)
finish("Homura_Idle", frames, [("loop", 0.0), ("loop", 3.0)], "立っている (ループ 3 秒)。右手は剣を下げて持ち、左手を胸の前に添えて、ゆっくり呼吸する")
cleanup()
made.append(("Idle", "Homura_Idle", None))
notes.append(("Idle", "立っている (ループ 3 秒)。右手は剣を下げて持ち、左手を胸の前に添えて、ゆっくり呼吸する", 3.0, []))
print("[rusk] 作りました: Homura_Idle")

# 攻撃
for game, (move, note) in ATTACKS.items():
    name = "Homura_" + game
    frames = build_moves(arm, base, geo, name, move)
    gi = info.get(game, {})
    secs = gi.get("length", 1) / gi.get("speed", 1)
    hits = gi.get("hits", [])
    marks = []
    if hits:
        h0 = hits[0]
        for i, h in enumerate(hits):
            marks.append((f"当たる{i + 1}" if len(hits) > 1 else "当たる", move["hit"] + (h - h0) * secs))
        if gi.get("accept") is not None:
            marks.append(("受付", move["hit"] + (gi["accept"] - h0) * secs))
        if gi.get("weapon_off") is not None and move["hit"] + (gi["weapon_off"] - h0) * secs <= move["length"]:
            marks.append(("刀をしまう", move["hit"] + (gi["weapon_off"] - h0) * secs))
    finish(name, frames, marks, note)
    cleanup()
    made.append((game, name, move["hit"]))
    notes.append((game, note, move["length"], marks))
    print("[rusk] 作りました:", name)

# ゲームの動作を打ち直すもの
bones = [pb.name for pb in arm.pose.bones if pb.name.startswith("Bip001") or pb.name in ("BN_weapon_01", "WeaponHolder_0")]
for game, (seconds, gi, samples) in game_samples.items():
    name = "Homura_" + game
    frames = build_from_game(arm, samples, bones)
    marks = []
    if gi["accept"] is not None:
        marks.append(("受付", gi["accept"] * seconds))
    if gi["weapon_off"] is not None:
        marks.append(("刀をしまう", gi["weapon_off"] * seconds))
    if gi["loop"]:
        marks += [("loop", 0.0), ("loop", seconds)]
    finish(name, frames, marks, FROM_GAME[game])
    cleanup()
    made.append((game, name, None))
    notes.append((game, FROM_GAME[game], seconds, marks))
    print("[rusk] 作りました:", name)

arm.animation_data.action = bpy.data.actions.get("Homura_Idle")

# 割り当て (character.json の "motions" に貼る)
glb_name = "Homura_Templates.glb"
bindings = {g: f"motions/{glb_name}#{n}" + (f"@{h:.2f}" if h is not None else "") for g, n, h in made}
with open(os.path.join(out_dir, "homura_bindings.json"), "w", encoding="utf-8") as f:
    json.dump({"motions": bindings}, f, ensure_ascii=False, indent=2)
with open(os.path.join(out_dir, "homura_notes.json"), "w", encoding="utf-8") as f:
    json.dump([dict(action=g, note=n, seconds=round(s, 2), markers=[(m, round(t, 2)) for m, t in mk]) for g, n, s, mk in notes],
              f, ensure_ascii=False, indent=2)

# プレビュー (いくつかの動作のコマ)
if preview:
    for game in ("Idle", "RedLightCombo0", "DashAttack", "SpecialAttack", "DashFront"):
        act = bpy.data.actions.get("Homura_" + game)
        if act is None:
            continue
        arm.animation_data.action = act
        if hasattr(act, "slots") and len(act.slots) > 0:
            arm.animation_data.action_slot = act.slots[0]
        f0, f1 = (int(x) for x in act.frame_range)
        shots = sorted({f0, *[m.frame for m in act.pose_markers if f0 <= m.frame <= f1][:5], f1})
        rr.sheet(arm, os.path.join(out_dir, f"preview_{game}.png"), shots, views=(("front", (0, -4, 0)), ("side", (-4, 0, 0))))
    arm.animation_data.action = bpy.data.actions.get("Homura_Idle")

# 保存。ゲームの元の動作は消す (fix_chains で手足の骨の向きを変えたので、この骨格では崩れて見える。
# 見比べたいときは rig_*_motions.glb を別に読み込む)
if bpy.context.object and bpy.context.object.mode != "OBJECT":
    bpy.ops.object.mode_set(mode="OBJECT")
for a in list(bpy.data.actions):
    if not a.name.startswith("Homura_"):
        bpy.data.actions.remove(a)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(out_dir, "homura_templates.blend"))
print("[rusk] 保存しました:", os.path.join(out_dir, "homura_templates.blend"))

# 雛形だけを glb に
for o in bpy.data.objects:
    o.select_set(o.name == ARM_NAME)
bpy.context.view_layer.objects.active = bpy.data.objects[ARM_NAME]
bpy.ops.export_scene.gltf(filepath=os.path.join(out_dir, glb_name), use_selection=True, export_animations=True,
                          export_skins=False, export_format="GLB", export_animation_mode="ACTIONS")
print("[rusk] 書き出しました:", os.path.join(out_dir, glb_name), f"(動作 {len(made)} 個)")
