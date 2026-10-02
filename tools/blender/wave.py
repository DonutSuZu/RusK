"""
見本: 右手で手を振る (2.4 秒でループ)。

    blender --background --python wave.py -- <rig_キャラ名.glb> <書き出す .glb> [<プレビューの .png>]

肩も一緒に上げ、ひじを曲げて前腕を立て、手首は少し遅れて返す。頭は腕の方へ傾け、体は反対へ少し傾ける。
"""
import math
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import rusk_motion as rm  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:]
rig_path, out_path = argv[0], argv[1]
preview_path = argv[2] if len(argv) > 2 else None
X, Y, Z, R = rm.X, rm.Y, rm.Z, rm.R


def pose(t):
    w = math.sin(t * math.pi * 4)            # 1 周で 2 往復
    w_lag = math.sin(t * math.pi * 4 - 0.7)  # 少し遅れて (手首)
    bob = math.sin(t * math.pi * 2)
    return [  # 親から順に
        ("Bip001 Spine1", R(Y, 3) @ R(Z, -4 + 2 * bob)),
        ("Bip001 Head", R(Y, -7 + 1.5 * bob) @ R(X, -4)),
        ("Bip001 R Clavicle", R(Y, 10 + 2 * w)),
        ("Bip001 R UpperArm", R(Y, 62 + 4 * w) @ R(Z, 25)),
        ("Bip001 R Forearm", R(Y, 62 + 22 * w)),
        ("Bip001 R Hand", R(Y, -18 * w_lag) @ R(X, -10)),
    ]


arm = rm.load_rig(rig_path)
rm.animate(arm, frames=72, fps=30, pose=pose, name="RusK_Wave")
if preview_path:
    rm.preview(arm, preview_path, frames=(0, 9, 18), camera=(-0.1, -2.2, 1.3), size=(420, 560))
rm.export(arm, out_path, "RusK_Wave")
