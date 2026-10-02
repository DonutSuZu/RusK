"""
RusK Custom Motion 用の「操作用の目印 (コントローラー)」で動きを作る部品 (Blender 5.2 で確認)。

ゲームのキャラの骨格と動作 (Model Lab の「骨格と動作を glb で書き出す」で作る rig_キャラ名_motions.glb) を読み込み、
- 手・足は目印 (Empty) の位置に IK で届かせる (肘・膝の向きは pole の目印)
- 手の向きは「刃の向き」で決める (武器の骨 WeaponHolder_0 の X が刃)
- 体 (腰・背骨・頭) は向きを数字で決める
動きを 1 コマずつ打ってから、ふつうの骨のキーフレームに焼き付けて (bake) glb に書き出す。

向き (Blender のワールド): X = キャラの左、-Y = キャラの前、Z = 上。
"""
import math
import os

import bpy
from mathutils import Matrix, Quaternion, Vector

X, Y, Z = Vector((1, 0, 0)), Vector((0, 1, 0)), Vector((0, 0, 1))
LEFT, RIGHT, FRONT, BACK, UP, DOWN = X, -X, -Y, Y, Z, -Z

# ねじれの骨 → 付いていく骨 (焼き付けた後で、付いていく骨と一緒に回す。そうしないと袖が伸びる)
TWISTS = {
    "Bip001 RUpArmTwist": "Bip001 R UpperArm", "Bip001 LUpArmTwist": "Bip001 L UpperArm",
    "Bip001 R ForeTwist": "Bip001 R Forearm", "Bip001 L ForeTwist": "Bip001 L Forearm",
    "Bip001 RThighTwist": "Bip001 R Thigh", "Bip001 LThighTwist": "Bip001 L Thigh",
    "Bip001 RCalfTwist": "Bip001 R Calf", "Bip001 LCalfTwist": "Bip001 L Calf",
}


def R(axis, deg):
    """ワールドの軸まわりの回転 (度)"""
    return Quaternion(Vector(axis), math.radians(deg))


# ---------------------------------------------------------------- 読み込み・姿勢

def load(glb):
    """骨格と動作の glb を読み込む (シーンは空にしてから)。読み込みで NLA に積まれた動作は外す"""
    if bpy.app.background:
        bpy.ops.wm.read_factory_settings(use_empty=True)
    else:
        # 画面のある Blender (MCP でつないだ Blender など) では初期化しない (アドオンが止まる)。シーンの中身だけ消す
        if bpy.context.object and bpy.context.object.mode != "OBJECT":
            bpy.ops.object.mode_set(mode="OBJECT")
        for o in list(bpy.data.objects):
            bpy.data.objects.remove(o, do_unlink=True)
        for coll in (bpy.data.meshes, bpy.data.armatures, bpy.data.actions, bpy.data.materials, bpy.data.images, bpy.data.cameras):
            for d in list(coll):
                if d.users == 0:
                    coll.remove(d)
    bpy.ops.import_scene.gltf(filepath=glb)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE" and "Bip001" in o.data.bones)
    arm.animation_data_create()
    for tr in list(arm.animation_data.nla_tracks):
        arm.animation_data.nla_tracks.remove(tr)
    arm.animation_data.action = None
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="POSE")
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
    bpy.context.scene.render.fps = 30
    return arm


def action(name):
    return bpy.data.actions.get(name) or next((a for a in bpy.data.actions if a.name.startswith(name)), None)


# 姿勢は「骨組みの中の位置と向き」(pose bone の matrix) で持つ。骨の基準の向き (fix_chains で変える) に左右されない

def sample(arm, action_name, nt):
    """ゲームの動作の、進み具合 nt (0〜1) の姿勢 {骨: 骨組みの中の行列}"""
    act = action(action_name)
    arm.animation_data.action = act
    if hasattr(act, "slots") and len(act.slots) > 0:
        arm.animation_data.action_slot = act.slots[0]
    f0, f1 = act.frame_range
    bpy.context.scene.frame_set(round(f0 + (f1 - f0) * nt))
    result = {pb.name: pb.matrix.copy() for pb in arm.pose.bones}
    arm.animation_data.action = None
    return result


def graft(arm, base, src, bones):
    """base の姿勢に、src の姿勢の bones の骨を「親から見た形」のまま付け替える (握り・指を別の動作から借りるとき)"""
    out = dict(base)
    for pb in sorted((arm.pose.bones[b] for b in bones if b in arm.pose.bones), key=lambda b: len(b.parent_recursive)):
        if pb.parent is None:
            out[pb.name] = src[pb.name]
            continue
        rel = src[pb.parent.name].inverted() @ src[pb.name]
        out[pb.name] = out[pb.parent.name] @ rel
    return out


def apply(arm, mats):
    """骨組みの中の行列の姿勢にする (親から順に、基準の姿勢との差 matrix_basis を計算して入れる)"""
    for pb in sorted(arm.pose.bones, key=lambda b: len(b.parent_recursive)):
        m = mats.get(pb.name)
        if m is None:
            continue
        rest = pb.bone.matrix_local
        if pb.parent is not None:
            pm = mats.get(pb.parent.name, pb.parent.matrix)
            parent_part = pm @ pb.parent.bone.matrix_local.inverted() @ rest
        else:
            parent_part = rest
        pb.matrix_basis = parent_part.inverted() @ m
    bpy.context.view_layer.update()


# IK で曲げる骨 → その先の骨 (骨の先端をそろえる)
CHAINS = [("Bip001 L Thigh", "Bip001 L Calf"), ("Bip001 L Calf", "Bip001 L Foot"),
          ("Bip001 R Thigh", "Bip001 R Calf"), ("Bip001 R Calf", "Bip001 R Foot"),
          ("Bip001 L UpperArm", "Bip001 L Forearm"), ("Bip001 L Forearm", "Bip001 L Hand"),
          ("Bip001 R UpperArm", "Bip001 R Forearm"), ("Bip001 R Forearm", "Bip001 R Hand")]


def fix_chains(arm, *poses):
    """
    IK で曲げる骨の先端 (tail) を、次の骨の根元 (膝・足首・肘・手首) にそろえる。
    glTF から読み込んだ骨の先端は Blender の推測で、すねの先が前を向いているなどして IK が届かない。
    骨の基準の向き (軸) が変わるので、先に sample で取っておいた姿勢 (poses) は、その分を補正する (同じ形のまま)。
    手・刀の骨は変えない (Custom Motion が親から見た形でそのまま写す骨なので)。
    ゲームには差分で写すので、もも・すねなどの基準の向きが変わっても結果は同じ
    """
    bpy.ops.object.mode_set(mode="OBJECT")
    for o in bpy.context.view_layer.objects:
        o.select_set(o == arm)
    bpy.context.view_layer.objects.active = arm
    old = {a: arm.data.bones[a].matrix_local.copy() for a, _ in CHAINS if a in arm.data.bones}
    bpy.ops.object.mode_set(mode="EDIT")
    eb = arm.data.edit_bones
    n = 0
    for a, b in CHAINS:
        if a in eb and b in eb:
            eb[a].use_connect = False
            eb[b].use_connect = False
            eb[a].tail = eb[b].head.copy()
            n += 1
    bpy.ops.object.mode_set(mode="POSE")
    # 姿勢の補正: 骨の行列 = 形 × 軸の取り方。軸の取り方の差 (古い基準 → 新しい基準) を右からかける
    for name, m_old in old.items():
        c = m_old.inverted() @ arm.data.bones[name].matrix_local
        for pose in poses:
            if name in pose:
                pose[name] = pose[name] @ c
    print(f"[rusk] 骨の先端をそろえました: {n} 本")
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"


def world(arm, bone, tail=False):
    pb = arm.pose.bones[bone]
    return arm.matrix_world @ (pb.tail if tail else pb.head)


def arm_matrix(arm, bone):
    """骨のワールドの行列"""
    return arm.matrix_world @ arm.pose.bones[bone].matrix


def world_rot(arm, bone):
    return (arm.matrix_world.to_3x3().normalized() @ arm.pose.bones[bone].matrix.to_3x3().normalized())


def turn(arm, bone, rot):
    """骨をワールドの向きで回す (根元の位置は保つ)"""
    pb = arm.pose.bones[bone]
    rot3 = arm.matrix_world.to_3x3().normalized()
    axis, angle = rot.to_axis_angle()
    q = Quaternion(rot3.inverted() @ axis, angle)
    m = pb.matrix.copy()
    head = m.to_translation()
    pb.matrix = Matrix.Translation(head) @ q.to_matrix().to_4x4() @ Matrix.Translation(-head) @ m
    bpy.context.view_layer.update()


def move(arm, bone, delta):
    """骨をワールドで delta だけ動かす"""
    pb = arm.pose.bones[bone]
    d = arm.matrix_world.inverted().to_3x3() @ Vector(delta)
    m = pb.matrix.copy()
    m.translation = m.translation + d
    pb.matrix = m
    bpy.context.view_layer.update()


# ---------------------------------------------------------------- 目印と IK

def empty(name, loc=(0, 0, 0)):
    e = bpy.data.objects.new(name, None)
    e.empty_display_size = 0.05
    e.location = loc
    e.rotation_mode = "QUATERNION"
    bpy.context.scene.collection.objects.link(e)
    return e


def ik(arm, bone, target, pole, chain=2):
    """
    bone (手・足の骨) の根元 (手首・足首) を target に届かせる。肘・膝は pole の方へ曲げる (pole の角度は自動で合わせる)。
    骨の先端 (tail) ではなく根元を使う: glTF から読み込んだ骨の先端は Blender が推測した位置で、次の骨の根元とずれていることがある
    """
    pb = arm.pose.bones[bone]
    c = pb.constraints.new("IK")
    c.target = target
    c.pole_target = pole
    c.use_tail = False
    c.chain_count = chain + 1  # 根元を使うときは、IK を付けた骨自身も数に入る (曲げるのはその親 chain 本)
    mid = pb.parent          # 前腕・すね
    top = mid.parent         # 上腕・もも
    # pole の角度: 曲がる関節 (肘・膝) が pole の方を向く角度を、何通りか試して選ぶ
    best, best_angle = None, 0.0
    for deg in range(-180, 180, 15):
        c.pole_angle = math.radians(deg)
        bpy.context.view_layer.update()
        joint = arm.matrix_world @ mid.head
        root = arm.matrix_world @ top.head
        tip = arm.matrix_world @ pb.head
        axis = (tip - root).normalized()
        bend = joint - root - axis * (joint - root).dot(axis)
        want = pole.location - root - axis * (pole.location - root).dot(axis)
        score = bend.normalized().dot(want.normalized()) if bend.length > 1e-4 and want.length > 1e-4 else -1
        if best is None or score > best:
            best, best_angle = score, c.pole_angle
    c.pole_angle = best_angle
    bpy.context.view_layer.update()
    return c


def copy_rot(arm, bone, target):
    c = arm.pose.bones[bone].constraints.new("COPY_ROTATION")
    c.target = target
    return c


def hand_rot_for_blade(arm, hand, holder, blade_dir, up):
    """刃 (holder の X) が blade_dir を向き、刃の面の上 (holder の Z) がなるべく up を向く手のワールドの向き"""
    rh = world_rot(arm, hand)
    rw = world_rot(arm, holder)
    h = rh.inverted() @ rw  # 手から見た握りの骨の向き
    x = Vector(blade_dir).normalized()
    zz = Vector(up) - x * Vector(up).dot(x)
    if zz.length < 1e-4:
        zz = Z - x * Z.dot(x)
    zz.normalize()
    yy = zz.cross(x)
    b = Matrix((x, yy, zz)).transposed()  # 列が軸
    return (b @ h.inverted()).to_quaternion()


# ---------------------------------------------------------------- 焼き付け・ねじれ・書き出し

def key(arm, bones, frame):
    for name in bones:
        pb = arm.pose.bones[name]
        pb.keyframe_insert("rotation_quaternion", frame=frame)
        pb.keyframe_insert("location", frame=frame)


def bake(arm, bones, f0, f1, name):
    """目印・IK で決まった姿勢を、bones の骨のキーフレームに焼き付ける (目印・IK は外す)"""
    bpy.ops.pose.select_all(action="DESELECT")
    for pb in arm.pose.bones:
        # Blender 5 では選択は PoseBone に、4 までは Bone にある
        target = pb if hasattr(pb, "select") else pb.bone
        target.select = pb.name in bones
    bpy.ops.nla.bake(frame_start=f0, frame_end=f1, only_selected=True, visual_keying=True,
                     clear_constraints=True, use_current_action=True, bake_types={"POSE"})
    arm.animation_data.action.name = name


def follow_twists(arm, base, f0, f1):
    """ねじれの骨を、付いていく骨と一緒に回す (基準の姿勢 base のときの位置関係を保つ)"""
    apply(arm, base)
    rest = {pb.name: pb.matrix.copy() for pb in arm.pose.bones}
    pairs = [(t, d) for t, d in TWISTS.items() if t in arm.pose.bones and d in arm.pose.bones]
    for f in range(f0, f1 + 1):
        bpy.context.scene.frame_set(f)
        for t, d in pairs:
            corr = arm.pose.bones[d].matrix @ rest[d].inverted()
            arm.pose.bones[t].matrix = corr @ rest[t]
            bpy.context.view_layer.update()
            key(arm, [t], f)


def sheet(arm, path, frames, labels=None, size=(300, 420), views=(("front", (0, -4, 0)), ("side", (-4, 0, 0)), ("top", (-2.5, -2.5, 3.0)))):
    """コマを横に並べた画像 (前・横・斜め上から)。path の .png の前に _front などを付ける"""
    scene = bpy.context.scene
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    scene.render.engine = "BLENDER_WORKBENCH"
    # 素材の色で塗る (テクスチャだと、半透明の刃 (光る剣など) が映らない)
    scene.display.shading.color_type = "MATERIAL"
    scene.render.resolution_x, scene.render.resolution_y = size
    scene.render.film_transparent = False
    cam = bpy.data.objects.get("rusk_cam") or bpy.data.objects.new("rusk_cam", bpy.data.cameras.new("rusk_cam"))
    if cam.name not in scene.collection.objects:
        scene.collection.objects.link(cam)
    cam.data.type = "ORTHO"
    cam.data.ortho_scale = 2.6
    scene.camera = cam
    base, _ = os.path.splitext(path)
    out = []
    for view, offset in views:
        w, h = size
        px = [1.0] * (w * len(frames) * h * 4)
        for k, f in enumerate(frames):
            scene.frame_set(f)
            c = world(arm, "Bip001 Pelvis")
            target = Vector((c.x, c.y, c.z - 0.1))
            cam.location = target + Vector(offset)
            cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
            tmp = f"{base}_{view}_tmp.png"
            scene.render.filepath = tmp
            bpy.ops.render.render(write_still=True)
            im = bpy.data.images.load(tmp)
            src = list(im.pixels)
            for y in range(h):
                start = (y * w * len(frames) + k * w) * 4
                px[start:start + w * 4] = src[y * w * 4:(y + 1) * w * 4]
            bpy.data.images.remove(im)
            os.remove(tmp)
        img = bpy.data.images.new("sheet_" + view, w * len(frames), h, alpha=True)
        img.pixels = px
        img.filepath_raw = f"{base}_{view}.png"
        img.file_format = "PNG"
        img.save()
        out.append(img.filepath_raw)
    print("[rusk] 画像:", ", ".join(out), "(コマ", ", ".join(labels or [str(f) for f in frames]), ")")
    return out


def export(arm, path, name):
    """骨格と動きだけを glb に書き出す (メッシュ・テクスチャ・ほかの動作は入れない)"""
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    keep = arm.animation_data.action
    keep.name = name
    for a in list(bpy.data.actions):
        if a != keep:
            bpy.data.actions.remove(a)
    for o in list(bpy.data.objects):
        if o != arm and o.type in ("MESH", "EMPTY", "CAMERA"):
            bpy.data.objects.remove(o)
    for o in bpy.data.objects:
        o.select_set(o == arm)
    bpy.context.view_layer.objects.active = arm
    bpy.ops.export_scene.gltf(filepath=path, use_selection=True, export_animations=True,
                              export_skins=False, export_format="GLB", export_animation_mode="ACTIONS")  # 動作の名前のまま (ACTIVE_ACTIONS だと "Animation" になる)
    print(f"[rusk] 書き出しました: {path}")
