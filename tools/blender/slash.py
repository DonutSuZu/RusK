"""
見本: 右手で横なぎに斬る (攻撃の動作に割り当てる用)。

    blender --background --python slash.py -- <rig_キャラ名.glb> <書き出す .glb> [<プレビューの .png>] [<長さ (秒)>] [<当たる瞬間 (0〜1)>]

攻撃の動作に割り当てると、ゲームの動作の進み具合 (0〜1) に合わせて再生される。
なので、この動きの長さ全体 = ゲームの動作の長さ全体。当たる瞬間 (攻撃判定の出る時間) に振り抜きが来るように作る。
既定は粉光の 1 段目 (PinkLightCombo_0: 6.67 秒 ÷ 速さ 1.3 = 5.13 秒、当たるのは 0.10)。
"""
import math
import os
import sys

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import rusk_motion as rm  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:]
rig_path, out_path = argv[0], argv[1]
preview_path = argv[2] if len(argv) > 2 and argv[2] else None
length = float(argv[3]) if len(argv) > 3 else 5.13
hit = float(argv[4]) if len(argv) > 4 else 0.10
X, Y, Z, R = rm.X, rm.Y, rm.Z, rm.R
I = R(Z, 0)

# 姿勢 (骨 → ワールドの回転)。体のひねりは Z 軸 (+ = 左へ向く)、腕を上げるのは Y 軸 (+ = 右腕が上がる)
READY = {  # 構え (少し右腕を前に)
    "Bip001 Spine1": R(Z, 5), "Bip001 Head": R(Z, -3), "Bip001 R Clavicle": I,
    "Bip001 R UpperArm": R(Z, 15), "Bip001 R Forearm": R(Y, 15), "Bip001 R Hand": I,
}
WINDUP = {  # 振りかぶり: 体を右へひねり、右腕を後ろ上へ
    "Bip001 Spine1": R(Z, -28) @ R(Y, -4), "Bip001 Head": R(Z, 18), "Bip001 R Clavicle": R(Y, 12),
    "Bip001 R UpperArm": R(Z, -35) @ R(Y, 75), "Bip001 R Forearm": R(Y, 45), "Bip001 R Hand": R(X, 15),
}
STRIKE = {  # 当たる瞬間: 体を左へひねりながら、腕を前へまっすぐ振り抜く
    "Bip001 Spine1": R(Z, 30) @ R(Y, 3), "Bip001 Head": R(Z, -15), "Bip001 R Clavicle": R(Y, 4),
    "Bip001 R UpperArm": R(Z, 75) @ R(Y, 35), "Bip001 R Forearm": R(Y, 0), "Bip001 R Hand": R(X, -10),
}
FOLLOW = {  # 振り抜いた後: さらに左へ流れる
    "Bip001 Spine1": R(Z, 42) @ R(Y, 5), "Bip001 Head": R(Z, -22), "Bip001 R Clavicle": R(Y, 2),
    "Bip001 R UpperArm": R(Z, 100) @ R(Y, 10), "Bip001 R Forearm": R(Y, 25), "Bip001 R Hand": R(X, -20),
}
ORDER = ["Bip001 Spine1", "Bip001 Head", "Bip001 R Clavicle", "Bip001 R UpperArm", "Bip001 R Forearm", "Bip001 R Hand"]

# 時間割 (動作の進み具合 0〜1)。振りかぶりはゆっくり、振り抜きは速く、戻りはゆっくり
KEYS = [(0.0, READY), (hit * 0.6, WINDUP), (hit, STRIKE), (hit + 0.04, FOLLOW), (hit + 0.15, READY), (1.0, READY)]


def ease(x, kind):
    if kind == "fast":   # 振り抜き: 最後に速く (加速)
        return x * x * x
    return x * x * (3 - 2 * x)  # なめらか


def pose(t):
    for (t0, a), (t1, b) in zip(KEYS, KEYS[1:]):
        if t <= t1:
            x = 0.0 if t1 <= t0 else (t - t0) / (t1 - t0)
            x = ease(x, "fast" if b is STRIKE else "smooth")
            return [(n, a[n].slerp(b[n], x)) for n in ORDER]
    return [(n, KEYS[-1][1][n]) for n in ORDER]


fps = 30
frames = max(2, round(length * fps))
arm = rm.load_rig(rig_path)
rm.animate(arm, frames=frames, fps=fps, pose=pose, name="RusK_Slash")
if preview_path:
    shots = [round(frames * k) for k in (hit * 0.6, hit, hit + 0.04)]
    rm.preview(arm, preview_path, frames=shots, camera=(-0.3, -2.6, 1.1), size=(420, 560))
    rm.preview(arm, preview_path.replace(".png", "_top.png"), frames=shots, camera=(-1.6, -1.6, 2.6), size=(420, 420), look_at=(0, 0, 1.1))
rm.export(arm, out_path, "RusK_Slash")
