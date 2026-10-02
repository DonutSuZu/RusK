"""
RusK Custom Motion 用の Blender の部品 (Blender 4.2 以降。5.2 で確認)。

ゲームのキャラの骨格 (Model Lab の「骨格を glb で書き出す」で作る rig_キャラ名.glb) を読み込み、
骨をワールドの向きで回してキーを打ち、アニメーション入りの glb に書き出す。

    import rusk_motion as rm
    rig = rm.load_rig(r"...\\rig_PinkLight_Show.glb")
    rm.animate(rig, frames=72, fps=30, pose=lambda t: [("Bip001 R UpperArm", rm.R(rm.Y, 60))])
    rm.export(rig, r"...\\RusK\\motions\\MyMotion.glb", "MyMotion")

向き (Blender のワールド): X = キャラの左、-Y = キャラの前、Z = 上。
R(Y, +角度) は前から見て時計回り (右腕なら上がる)。
"""
import math

import bpy
from mathutils import Matrix, Quaternion, Vector

X, Y, Z = (1, 0, 0), (0, 1, 0), (0, 0, 1)

# ねじれ用の骨 → 付いていく骨 (ゲームのアニメーションはねじれの骨も一緒に動かしている。付いていかせないと袖が伸びる)
FOLLOW = {
    "Bip001 RUpArmTwist": "Bip001 R UpperArm", "Bip001 LUpArmTwist": "Bip001 L UpperArm",
    "Bip001 R ForeTwist": "Bip001 R Forearm", "Bip001 L ForeTwist": "Bip001 L Forearm",
    "Bip001 RThighTwist": "Bip001 R Thigh", "Bip001 LThighTwist": "Bip001 L Thigh",
    "Bip001 RCalfTwist": "Bip001 R Calf", "Bip001 LCalfTwist": "Bip001 L Calf",
}


def R(axis, deg):
    """ワールドの軸まわりの回転 (度)"""
    return Quaternion(Vector(axis), math.radians(deg))


def load_rig(path):
    """骨格の glb を読み込んで、アーマチュアを返す (シーンは空にしてから)"""
    bpy.ops.wm.read_factory_settings(use_empty=True)
    bpy.ops.import_scene.gltf(filepath=path)
    arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")
    bpy.context.view_layer.objects.active = arm
    bpy.ops.object.mode_set(mode="POSE")
    for pb in arm.pose.bones:
        pb.rotation_mode = "QUATERNION"
    return arm


def _turn(arm, pb, world_rot):
    """骨をワールドの向きで回す (根元の位置は保つ)。glTF の読み込みでアーマチュア自体が回っているので、軸を直してから"""
    rot3 = arm.matrix_world.to_3x3().normalized()
    axis, angle = world_rot.to_axis_angle()
    q = Quaternion(rot3.inverted() @ axis, angle)
    m = pb.matrix.copy()
    head = m.to_translation()
    pb.matrix = Matrix.Translation(head) @ q.to_matrix().to_4x4() @ Matrix.Translation(-head) @ m


def set_pose(arm, rotations):
    """基準の姿勢から、[(骨の名前, ワールドの回転), ...] を親から順にかける。動かした骨 (とお供のねじれの骨) の名前を返す"""
    for pb in arm.pose.bones:
        pb.rotation_quaternion = (1, 0, 0, 0)
        pb.location = (0, 0, 0)
    bpy.context.view_layer.update()
    follow = {k: v for k, v in FOLLOW.items() if k in arm.pose.bones}
    moved = []
    for name, rot in rotations:
        if name not in arm.pose.bones:
            print(f"[rusk] 骨がありません: {name}")
            continue
        d = arm.pose.bones[name]
        twists = [arm.pose.bones[t] for t, drv in follow.items() if drv == name]
        old_d = d.matrix.copy()
        old_t = [t.matrix.copy() for t in twists]
        _turn(arm, d, rot)
        bpy.context.view_layer.update()
        corr = d.matrix @ old_d.inverted()
        for t, m in zip(twists, old_t):
            t.matrix = corr @ m
            bpy.context.view_layer.update()
            moved.append(t.name)
        moved.append(name)
    return moved


def animate(arm, frames, fps, pose, name="RusK_Motion"):
    """pose(t) (t = 0〜1) が返す回転の並びで、0〜frames のキーを打つ"""
    scene = bpy.context.scene
    scene.render.fps = fps
    scene.frame_start, scene.frame_end = 0, frames
    for f in range(frames + 1):
        scene.frame_set(f)
        for bone in set_pose(arm, pose(f / frames)):
            pb = arm.pose.bones[bone]
            pb.keyframe_insert("rotation_quaternion", frame=f)
            pb.keyframe_insert("location", frame=f)
    arm.animation_data.action.name = name


def preview(arm, path, frames=(0,), camera=(0.0, -2.6, 1.0), size=(420, 600)):
    """正面から撮った画像を書き出す (path の .png の前に _番号 を付ける)"""
    scene = bpy.context.scene
    bpy.ops.object.mode_set(mode="OBJECT")
    scene.render.engine = "BLENDER_WORKBENCH"
    scene.display.shading.color_type = "TEXTURE"
    cam = bpy.data.objects.get("rusk_cam")
    if cam is None:
        cam = bpy.data.objects.new("rusk_cam", bpy.data.cameras.new("rusk_cam"))
        scene.collection.objects.link(cam)
    cam.location = camera
    cam.rotation_euler = (math.radians(88), 0, 0)
    scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = size
    files = []
    for i, f in enumerate(frames):
        scene.frame_set(f)
        scene.render.filepath = path.replace(".png", f"_{i}.png")
        bpy.ops.render.render(write_still=True)
        files.append(scene.render.filepath)
    return files


def export(arm, path, name=None):
    """骨格と動きだけを glb に書き出す (メッシュ・テクスチャは入れない。ゲームのモデルを配らないため)"""
    if bpy.context.object and bpy.context.object.mode != "OBJECT":
        bpy.ops.object.mode_set(mode="OBJECT")
    if name and arm.animation_data and arm.animation_data.action:
        arm.animation_data.action.name = name
    for o in bpy.data.objects:
        o.select_set(False)
    arm.select_set(True)
    bpy.ops.export_scene.gltf(filepath=path, use_selection=True, export_animations=True,
                              export_skins=False, export_format="GLB")
    print(f"[rusk] 書き出しました: {path}")
