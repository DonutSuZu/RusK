"""
Blender で、新しいキャラの見た目のまま動きを作る場面を組み立てる (MCP でつないだ Blender で使う)。

    import rusk_preview as rp
    rp.setup(rig_glb, pmx, sword_pmx=None, sword_scale=1.2 / 0.45, save=None)

- ゲームの骨格と動作 (Model Lab の「骨格と動作を glb で書き出す」) を読み込み、ゲームの体は隠す
- キャラの PMX を MMD Tools で読み込み、ゲームの骨に付いていくようにつなぐ
  (腕・脚・背骨はゲームの次の関節の方を向き、頭・手首・足首・腰はゲームの骨と同じだけ回る)
- 剣の PMX があれば、その材質を書き出した剣に貼る (書き出した剣の材質にはテクスチャが入っていない)。
  sword_scale は、書き出したときの大きさから今のゲームの大きさへの倍率

ゲームの骨格の骨は書き換えない (書き換えると、ゲームの動作を流したときに姿勢が崩れる)。
キャラの PMX・ゲームのモデルは配らないこと (保存した .blend も)。
"""
import os

import bpy
from mathutils import Matrix

import rusk_rig as rr

AIM = {"上半身": "Bip001 Spine1", "上半身2": "Bip001 Neck", "首": "Bip001 Head",
       "肩.L": "Bip001 L UpperArm", "腕.L": "Bip001 L Forearm", "ひじ.L": "Bip001 L Hand",
       "肩.R": "Bip001 R UpperArm", "腕.R": "Bip001 R Forearm", "ひじ.R": "Bip001 R Hand",
       "足.L": "Bip001 L Calf", "ひざ.L": "Bip001 L Foot", "足.R": "Bip001 R Calf", "ひざ.R": "Bip001 R Foot"}
ROT = {"頭": "Bip001 Head", "手首.L": "Bip001 L Hand", "手首.R": "Bip001 R Hand",
       "足首.L": "Bip001 L Foot", "足首.R": "Bip001 R Foot", "下半身": "Bip001 Pelvis"}
FINGERS = {"親指": "0", "人指": "1", "中指": "2", "薬指": "3", "小指": "4"}


def game_armature():
    return next(o for o in bpy.data.objects if o.type == "ARMATURE" and "Bip001" in o.data.bones)


def play(action_name, nt=0.0):
    """ゲームの骨格に動作を流して、進み具合 nt のコマにする"""
    arm = game_armature()
    act = rr.action(action_name)
    for pb in arm.pose.bones:
        pb.matrix_basis.identity()
    arm.animation_data.action = act
    if hasattr(act, "slots") and len(act.slots) > 0:
        arm.animation_data.action_slot = act.slots[0]
    f0, f1 = act.frame_range
    sc = bpy.context.scene
    sc.frame_start, sc.frame_end = int(f0), int(f1)
    sc.frame_set(round(f0 + (f1 - f0) * nt))
    return act


def _import_pmx(path):
    before = set(bpy.data.objects)
    bpy.ops.mmd_tools.import_model(filepath=path, scale=0.08, types={"MESH", "ARMATURE", "MORPHS"})
    return [o for o in bpy.data.objects if o not in before]


def setup(rig_glb, pmx, sword_pmx=None, sword_scale=1.0, save=None):
    game = rr.load(rig_glb)
    bpy.ops.object.mode_set(mode="OBJECT")
    for o in bpy.data.objects:
        if o.type in ("EMPTY", "CAMERA", "LIGHT") or o.name.startswith("Icosphere"):
            o.hide_set(True)
    for b in game.data.bones:
        b.hide = not (b.name.startswith("Bip001") or b.name in ("BN_weapon_01", "WeaponHolder_0"))
    game.data.display_type = "STICK"
    # 立ち姿勢でつなぐ (キャラの腕が下りている姿勢に近い方が、つないだときの誤差が少ない)
    play("Idle", 0.0)

    # ---- キャラ
    new = _import_pmx(pmx)
    py = next(o for o in new if o.type == "ARMATURE")
    for pb in py.pose.bones:
        for c in list(pb.constraints):
            if c.type in ("IK", "LIMIT_ROTATION") or (c.type == "DAMPED_TRACK" and pb.name.startswith(("足首", "つま先"))):
                pb.constraints.remove(c)
    for pn, gn in AIM.items():
        if pn in py.pose.bones:
            c = py.pose.bones[pn].constraints.new("DAMPED_TRACK")
            c.name, c.target, c.subtarget = "rusk_aim", game, gn
    bpy.context.view_layer.update()
    coll = bpy.data.collections.get("rusk_link") or bpy.data.collections.new("rusk_link")
    if coll.name not in bpy.context.scene.collection.children:
        bpy.context.scene.collection.children.link(coll)

    def anchor(name, gbone, mat):
        e = bpy.data.objects.new("link_" + name, None)
        coll.objects.link(e)
        e.parent, e.parent_type, e.parent_bone = game, "BONE", gbone
        bpy.context.view_layer.update()
        e.matrix_world = mat
        e.hide_set(True)
        return e

    for pn, gn in ROT.items():
        if pn in py.pose.bones:
            e = anchor(pn, gn, py.matrix_world @ py.pose.bones[pn].matrix)
            c = py.pose.bones[pn].constraints.new("COPY_ROTATION")
            c.name, c.target = "rusk_rot", e
    # 指 (MMD は 1 本 2 節、ゲームは 3 節。根元の 2 節をつなぐ)
    for side in ("L", "R"):
        for jp, num in FINGERS.items():
            for seg, gn in (("１", f"Bip001 {side} Finger{num}"), ("２", f"Bip001 {side} Finger{num}1")):
                pn = f"{jp}{seg}.{side}"
                if pn in py.pose.bones and gn in game.pose.bones:
                    e = anchor(pn, gn, py.matrix_world @ py.pose.bones[pn].matrix)
                    c = py.pose.bones[pn].constraints.new("COPY_ROTATION")
                    c.name, c.target = "rusk_rot", e
    e = anchor("センター", "Bip001 Pelvis", py.matrix_world @ py.pose.bones["センター"].matrix)
    c = py.pose.bones["センター"].constraints.new("COPY_LOCATION")
    c.name, c.target = "rusk_loc", e
    for o in bpy.data.objects:
        if o.type == "MESH" and o.find_armature() == game and "sword" not in o.name.lower():
            o.hide_set(True)

    # ---- 剣
    sw = next((o for o in bpy.data.objects if o.type == "MESH" and "sword" in o.name.lower() and o.find_armature() == game), None)
    if sw is not None:
        H = game.matrix_world @ game.data.bones["WeaponHolder_0"].matrix_local
        sw.data.transform(sw.matrix_world.inverted() @ H @ Matrix.Scale(sword_scale, 4) @ H.inverted() @ sw.matrix_world)
        if sword_pmx:
            snew = _import_pmx(sword_pmx)
            m = next(o for o in snew if o.type == "MESH")
            mats = [s.material for s in m.material_slots]
            for i, s in enumerate(sw.material_slots):
                if i < len(mats):
                    s.material = mats[i]
            for o in snew:
                bpy.data.objects.remove(o, do_unlink=True)

    bpy.context.view_layer.update()
    if save:
        os.makedirs(os.path.dirname(save), exist_ok=True)
        bpy.ops.wm.save_as_mainfile(filepath=save)
    return game, py
