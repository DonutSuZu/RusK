"""
動作を調べる: アニメーション入りの glb (Model Lab の「骨格と動作を glb で書き出す」や、自分で作った動き) を読み込み、
指定した動作の、指定した瞬間 (進み具合 0〜1) の姿を画像にし、右手の速さ・体の移動を表にする。

    blender --background --python inspect_motion.py -- <glb> <動作の名前> <書き出す .png> [<進み具合,進み具合,...>]

画像は 前から / 横から の 2 枚 (path_front.png, path_side.png)。各コマの上に進み具合を書く。
"""
import math
import os
import sys

import bpy
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
glb, anim_name, out_png = argv[0], argv[1], argv[2]
points = [float(x) for x in argv[3].split(",")] if len(argv) > 3 else [0, 0.05, 0.1, 0.15, 0.2, 0.3, 0.5, 0.75, 1.0]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=glb)
arm = next(o for o in bpy.data.objects if o.type == "ARMATURE")

# 動作を選ぶ (読み込みで名前の後ろに何か付くことがあるので、前方一致も見る)
action = bpy.data.actions.get(anim_name) or next((a for a in bpy.data.actions if a.name.startswith(anim_name)), None)
if action is None:
    print("[rusk] 動作がありません:", anim_name, "/ ある動作:", ", ".join(a.name for a in bpy.data.actions))
    sys.exit(1)
arm.animation_data_create()
# 読み込みで NLA に積まれた動作を止めて、選んだ動作だけにする
for tr in list(arm.animation_data.nla_tracks):
    arm.animation_data.nla_tracks.remove(tr)
arm.animation_data.action = action
if hasattr(arm.animation_data, "action_slot") and action.slots if hasattr(action, "slots") else False:
    arm.animation_data.action_slot = action.slots[0]
f0, f1 = action.frame_range
fps = bpy.context.scene.render.fps
scene = bpy.context.scene
print(f"[rusk] {action.name}: フレーム {f0:.0f}〜{f1:.0f} ({(f1 - f0) / fps:.2f} 秒, {fps} fps)")


def bone_world(name):
    pb = arm.pose.bones.get(name)
    return arm.matrix_world @ pb.head if pb else None


# ---- 表: 右手の速さ (腰から見て)・腰の位置
hand, hips = "Bip001 R Hand", "Bip001 Pelvis"
prev = None
rows = []
for f in range(int(f0), int(f1) + 1):
    scene.frame_set(f)
    h, p = bone_world(hand), bone_world(hips)
    rel = h - p
    speed = (rel - prev).length * fps if prev is not None else 0.0
    prev = rel
    rows.append((f, (f - f0) / max(1, f1 - f0), speed, p.copy(), h.copy()))
top = sorted(rows, key=lambda r: -r[2])[:5]
print("[rusk] 右手が速い瞬間 (進み具合 / 秒 / 速さ m/s):")
for f, nt, sp, p, h in sorted(top, key=lambda r: r[0]):
    print(f"    {nt:.3f}  {(f - f0) / fps:5.2f}s  {sp:5.2f}")
print("[rusk] 腰の位置 (進み具合: 前後 / 上下 / 左右。前 = -Y):")
for f, nt, sp, p, h in rows[:: max(1, len(rows) // 16)]:
    print(f"    {nt:.3f}  {-p.y:+.2f} {p.z:+.2f} {p.x:+.2f}   右手 {-h.y:+.2f} {h.z:+.2f} {h.x:+.2f}")

# ---- 画像: 進み具合ごとのコマを横に並べる
scene.render.engine = "BLENDER_WORKBENCH"
scene.display.shading.color_type = "TEXTURE"
scene.render.resolution_x, scene.render.resolution_y = 300, 420
cam = bpy.data.objects.new("rusk_cam", bpy.data.cameras.new("rusk_cam"))
cam.data.type = "ORTHO"
cam.data.ortho_scale = 2.4
scene.collection.objects.link(cam)
scene.camera = cam
frames = []
for nt in points:
    f = round(f0 + (f1 - f0) * nt)
    frames.append((nt, f))

base, _ = os.path.splitext(out_png)
for view, offset in (("front", Vector((0, -4, 0))), ("side", Vector((-4, 0, 0)))):
    files = []
    for i, (nt, f) in enumerate(frames):
        scene.frame_set(f)
        center = bone_world(hips)
        target = Vector((center.x, center.y, 0.95))
        cam.location = target + offset
        cam.rotation_euler = (target - cam.location).to_track_quat("-Z", "Y").to_euler()
        scene.render.filepath = f"{base}_{view}_{i}.png"
        bpy.ops.render.render(write_still=True)
        files.append((nt, scene.render.filepath))
    # 横に並べる (Blender の画像で)
    w, h = scene.render.resolution_x, scene.render.resolution_y
    sheet = bpy.data.images.new("sheet", w * len(files), h, alpha=True)
    px = [0.0] * (w * len(files) * h * 4)
    for k, (nt, path) in enumerate(files):
        im = bpy.data.images.load(path)
        src = list(im.pixels)
        for y in range(h):
            row = src[y * w * 4:(y + 1) * w * 4]
            start = (y * w * len(files) + k * w) * 4
            px[start:start + w * 4] = row
        bpy.data.images.remove(im)
        os.remove(path)
    sheet.pixels = px
    sheet.filepath_raw = f"{base}_{view}.png"
    sheet.file_format = "PNG"
    sheet.save()
    print("[rusk] 書き出しました:", sheet.filepath_raw, "(左から", ", ".join(f"{nt:g}" for nt, _ in files), ")")
