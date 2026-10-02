using System;
using System.IO;

namespace RusK.Mods.Character;

/// <summary>
/// 開発用の指示ファイル: RusK/data/character/dev.txt に 1 行ずつ指示を書くと、ゲームが読んで実行し、ファイルを消す
/// (画面を操作せずに、作ったキャラ・動き・武器を確かめるため)。
///   switch 〈ID〉      そのキャラに切り替える
///   capture           今のキャラの絵の素材を撮り直す
///   motion 〈名前〉    今のキャラにゲームの動作をさせる (攻撃なら攻撃判定・武器を持つのもゲームのまま)
///   bones             主な骨の位置・向き (キャラの根元から見て) と、今の動作をログに出す
/// </summary>
internal static class DevCommands
{
    private static float _next;

    private static string FilePath => Path.Combine(CharacterMod.Ctx.DataDirectory, "dev.txt");

    private static void Bones()
    {
        var pl = RusK.Mods.Shared.PlayerRef.Current;
        if (pl == null) return;
        var root = pl.transform;
        var sb = new System.Text.StringBuilder($"Character: (骨) 動作 '{pl.m_animController?.m_animMotion?.name}' 進み {pl.m_animController?.GetCurAnimNormalizedTime():0.000}");
        foreach (var t in root.GetComponentsInChildren<UnityEngine.Transform>(true))
        {
            if (t.name is not ("Bip001" or "Bip001 Pelvis" or "Bip001 Spine" or "Bip001 Head" or "Bip001 R Foot" or "Bip001 L Foot" or "Bip001 R Hand" or "BN_weapon_01")) continue;
            var lp = root.InverseTransformPoint(t.position);
            var lr = (UnityEngine.Quaternion.Inverse(root.rotation) * t.rotation).eulerAngles;
            sb.Append($"\n  {t.name}: 位置 {lp.x:0.000},{lp.y:0.000},{lp.z:0.000} 向き {lr.x:0},{lr.y:0},{lr.z:0} / ローカル {t.localPosition.x:0.000},{t.localPosition.y:0.000},{t.localPosition.z:0.000} {t.localEulerAngles.x:0},{t.localEulerAngles.y:0},{t.localEulerAngles.z:0}");
        }
        // 付けているモデル (Custom VRM Loader の RusK_PMX_* / RusK_VRM_*) の右腕
        foreach (var go in UnityEngine.Object.FindObjectsOfType<UnityEngine.SkinnedMeshRenderer>())
        {
            var mr = go.transform.root;
            if (!(mr.name.StartsWith("RusK_PMX_") || mr.name.StartsWith("RusK_VRM_")) || (mr.position - root.position).magnitude > 1f) continue;
            foreach (var t in mr.GetComponentsInChildren<UnityEngine.Transform>(true))
            {
                if (t.name is not ("右腕" or "右ひじ" or "右手首" or "J_Bip_R_UpperArm" or "J_Bip_R_LowerArm" or "J_Bip_R_Hand")) continue;
                var lp = root.InverseTransformPoint(t.position);
                sb.Append($"\n  (モデル) {t.name}: 位置 {lp.x:0.000},{lp.y:0.000},{lp.z:0.000}");
            }
            break;
        }
        foreach (var t in root.GetComponentsInChildren<UnityEngine.Transform>(true))
        {
            if (t.name is not ("Bip001 R UpperArm" or "Bip001 R Forearm")) continue;
            var lp = root.InverseTransformPoint(t.position);
            sb.Append($"\n  {t.name}: 位置 {lp.x:0.000},{lp.y:0.000},{lp.z:0.000}");
        }
        CharacterMod.Ctx.Log.Info(sb.ToString());
    }

    public static void Tick()
    {
        if (UnityEngine.Time.unscaledTime < _next) return;
        _next = UnityEngine.Time.unscaledTime + 0.5f;
        if (!File.Exists(FilePath)) return;
        string[] lines;
        try { lines = File.ReadAllLines(FilePath); File.Delete(FilePath); }
        catch { return; }
        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#")) continue;
            var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            try
            {
                switch (p[0])
                {
                    case "switch":
                        GameUtil.Instance.ChangeCharacter(double.Parse(p[1]));
                        break;
                    case "capture":
                        var pl = RusK.Mods.Shared.PlayerRef.Current;
                        if (pl != null) CharacterCapture.Request((long)Math.Round(pl.GetPlayerId()));
                        break;
                    case "motion":
                        var mp = RusK.Mods.Shared.PlayerRef.Current;
                        mp?.GetMotionController()?.ChangeMotion(p[1], true, 0.1f, default);
                        break;
                    case "bones":
                        Bones();
                        break;
                    default:
                        CharacterMod.Ctx.Log.Warning($"Character: dev.txt の指示が分かりません: {line}");
                        continue;
                }
                CharacterMod.Ctx.Log.Info($"Character: dev.txt: {line}");
            }
            catch (Exception e) { CharacterMod.Ctx.Log.Warning($"Character: dev.txt の {line} に失敗: {e.Message}"); }
        }
    }
}
