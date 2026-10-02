"""
FBX (Mixamo など) のアニメーションを、Custom Motion で読める glb に変換する。

    blender --background --python fbx_to_glb.py -- <入力の .fbx> <書き出す .glb> [<アニメーションの名前>]

骨とアニメーションだけを書き出す (メッシュは入れない)。Mixamo は「Without Skin」でダウンロードしたものでも OK。
ゲームでは、骨の名前 (mixamorig:Hips など) から人型の骨に対応させて、ゲームのキャラの骨格に載せ替える。
"""
import os
import sys

import bpy

argv = sys.argv[sys.argv.index("--") + 1:]
src, out = argv[0], argv[1]
name = argv[2] if len(argv) > 2 else os.path.splitext(os.path.basename(src))[0]

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=src, automatic_bone_orientation=False, ignore_leaf_bones=True)
arms = [o for o in bpy.data.objects if o.type == "ARMATURE"]
if not arms:
    raise SystemExit("アーマチュアがありません")
arm = arms[0]
if arm.animation_data and arm.animation_data.action:
    arm.animation_data.action.name = name
    fr = arm.animation_data.action.frame_range
    bpy.context.scene.frame_start, bpy.context.scene.frame_end = int(fr[0]), int(fr[1])
print("[rusk] bones", len(arm.data.bones), [b.name for b in arm.data.bones][:8])
for o in bpy.data.objects:
    o.select_set(o == arm)
bpy.context.view_layer.objects.active = arm
bpy.ops.export_scene.gltf(filepath=out, use_selection=True, export_animations=True, export_skins=False,
                          export_format="GLB", export_force_sampling=True)
print(f"[rusk] 書き出しました: {out}")
