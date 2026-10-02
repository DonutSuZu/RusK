using System;
using System.IO;
using System.Linq;

namespace RusK.Mods.Character;

/// <summary>
/// 開発用の指示ファイル: RusK/data/character/dev.txt に 1 行ずつ指示を書くと、ゲームが読んで実行し、ファイルを消す
/// (画面を操作せずに、作ったキャラ・動き・武器を確かめるため)。
///   switch 〈ID〉      そのキャラに切り替える
///   capture           今のキャラの絵の素材を撮り直す
///   motion 〈名前〉    今のキャラにゲームの動作をさせる (攻撃なら攻撃判定・武器を持つのもゲームのまま)
///   bones             主な骨の位置・向き (キャラの根元から見て) と、今の動作をログに出す
///   ui                キャラを並べる画面のカードの並び (親の部品・大きさ・スクロール) をログに出す
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

    private static void Ui()
    {
        var sb = new System.Text.StringBuilder("Character: (画面)");
        void Walk(UnityEngine.Transform t, int depth, int max)
        {
            var rt = t.TryCast<UnityEngine.RectTransform>();
            var comps = string.Join(",", t.GetComponents<UnityEngine.Component>().Select(c => c.GetIl2CppType().Name).Where(n => n != "RectTransform" && n != "Transform"));
            sb.Append("\n" + new string(' ', depth * 2) + $"{t.name} [{comps}] on={t.gameObject.activeSelf}" +
                      (rt != null ? $" size={rt.rect.width:0}x{rt.rect.height:0} pos={rt.anchoredPosition.x:0},{rt.anchoredPosition.y:0}" : ""));
            if (depth < max) for (int i = 0; i < t.childCount && i < 14; i++) Walk(t.GetChild(i), depth + 1, max);
        }
        foreach (var w in UnityEngine.Object.FindObjectsOfType<WindowCharacterShow>())
        {
            var cards = w.m_crtCards;
            if (cards == null || cards.Count == 0) continue;
            var parent = cards[0].transform.parent;
            sb.Append($"\n== WindowCharacterShow cards={cards.Count} 親の列:");
            for (var t = parent; t != null && t != w.transform.parent; t = t.parent)
            {
                var rt = t.TryCast<UnityEngine.RectTransform>();
                sb.Append($"\n  {t.name} [{string.Join(",", t.GetComponents<UnityEngine.Component>().Select(c => c.GetIl2CppType().Name))}]" +
                          (rt != null ? $" size={rt.rect.width:0}x{rt.rect.height:0}" : ""));
            }
            sb.Append("\n== 並び:");
            Walk(parent, 0, 1);
        }
        foreach (var w in UnityEngine.Object.FindObjectsOfType<WindowBattleCharacterChoose>())
        {
            var cards = w.m_crtCards;
            if (cards == null || cards.Count == 0 || cards[0] == null) continue;
            var parent = cards[0].transform.parent;
            sb.Append($"\n== WindowBattleCharacterChoose cards={cards.Count} 親の列:");
            for (var t = parent; t != null && t != w.transform.parent; t = t.parent)
            {
                var rt = t.TryCast<UnityEngine.RectTransform>();
                sb.Append($"\n  {t.name} [{string.Join(",", t.GetComponents<UnityEngine.Component>().Select(c => c.GetIl2CppType().Name))}]" +
                          (rt != null ? $" size={rt.rect.width:0}x{rt.rect.height:0} pos={rt.anchoredPosition.x:0},{rt.anchoredPosition.y:0}" : ""));
            }
            sb.Append("\n== 並び:");
            Walk(parent, 0, 1);
        }
        CharacterMod.Ctx.Log.Info(sb.ToString());
    }

    /// <summary>
    /// Mod Pack の資料用: data/character/catalog に、ゲームの全部の声 (日本語・中国語の対、長さ) と、
    /// 操作できるキャラごとの動作の一覧 (名前・クリップ・秒・攻撃判定) を書き出す
    /// </summary>
    private static void Catalog()
    {
        var dir = Path.Combine(CharacterMod.Ctx.DataDirectory, "catalog");
        Directory.CreateDirectory(dir);
        var sb = new System.Text.StringBuilder("# 日本語の声\t中国語の声\t秒\n");
        int voices = 0;
        foreach (var map in UnityEngine.Resources.FindObjectsOfTypeAll<VoiceLanguageClipMap>())
        {
            if (map?.pairs == null) continue;
            foreach (var pr in map.pairs)
            {
                if (pr == null) continue;
                var jp = pr.japaneseClip;
                var cn = pr.chineseClip;
                sb.Append($"{(jp != null ? jp.name : "-")}\t{(cn != null ? cn.name : "-")}\t{(jp != null ? jp.length : cn != null ? cn.length : 0f):0.00}\n");
                voices++;
            }
        }
        File.WriteAllText(Path.Combine(dir, "voices.tsv"), sb.ToString());
        int chars = 0;
        foreach (var mm in GameUtil.Instance.GetCharacterContainer().characters)
        {
            if (mm == null || mm.id < 1000 || mm.id >= 2000 || mm.motions == null) continue;
            var m2 = new System.Text.StringBuilder($"# {mm.name} (ID {mm.id}, {GameUtil.GetLocale("ActorName_" + mm.id)})\n# 名前\tクリップ\t秒\t攻撃判定\tループ\n");
            foreach (var m in mm.motions)
            {
                if (m == null) continue;
                var clip = m.bindAnimClip?.Clip;
                float sec = clip != null ? clip.length / UnityEngine.Mathf.Max(0.05f, m.playSpeed) : 0f;
                var hits = new System.Collections.Generic.List<float>();
                if (m.animEvent != null)
                    foreach (var e in m.animEvent)
                        if (e?.callBack != null)
                            foreach (var f in e.callBack)
                                if (f?.funcName != null && f.funcName.EndsWith("AttackBoxOn")) hits.Add(e.playProcess);
                m2.Append($"{m.name}\t{(clip != null ? clip.name : "-")}\t{sec:0.00}\t{(hits.Count > 0 ? string.Join(",", hits.OrderBy(h => h).Select(h => h.ToString("0.00"))) : "-")}\t{(clip != null && clip.isLooping ? "loop" : "")}\n");
            }
            File.WriteAllText(Path.Combine(dir, $"motions_{mm.id:0}.txt"), m2.ToString());
            chars++;
        }
        CharacterMod.Ctx.Log.Info($"Character: 資料を書き出しました (声 {voices}、キャラ {chars}) → {dir}");
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
                    case "catalog":
                        Catalog();
                        break;
                    case "ui":
                        Ui();
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
