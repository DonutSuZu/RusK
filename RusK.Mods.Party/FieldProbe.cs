using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HarmonyLib;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RusK.Mods.Party;

/// <summary>
/// 調査用 (Party Lab、エンドフィールド風の戦闘の試作): 控えのキャラを表示したままフィールドに置き、
/// 攻撃の動作をさせて、ゲームが混乱しないかを確かめる。
///
/// 確かめたいこと:
///   - 置いたキャラが、プレイヤーのキー入力で勝手に動かないか
///     (PlayerController.Update が毎フレーム KeyRespond を呼ぶので、操作中でないキャラの KeyRespond を止めてみる)
///   - 置いたキャラの攻撃で敵にダメージが入るか (EnemyController.GetHit の攻撃者を記録)
///   - 敵の攻撃が置いたキャラに当たるか (PlayerController.GetHit を記録)
/// </summary>
internal static class FieldProbe
{
    /// <summary>フィールドに置いた (表示したままの) 控え</summary>
    public static readonly List<PlayerController> Fielded = new();

    /// <summary>操作中でないキャラのキー入力 (KeyRespond) を止める</summary>
    public static bool BlockOthersInput = true;

    private static readonly HashSet<IntPtr> Listed = new();
    private static float _nextReport;
    private static readonly Dictionary<IntPtr, Vector3> LastPos = new();
    private static readonly Dictionary<string, int> HitsBy = new();
    private static readonly Dictionary<string, int> HitsOn = new();

    public static bool IsFielded(PlayerController p) => p != null && Fielded.Any(f => f != null && f.Pointer == p.Pointer);

    public static void Place(PlayerController p)
    {
        var cur = PartyManager.Current;
        if (p == null || cur == null || p.Pointer == cur.Pointer) return;
        try
        {
            int index = Fielded.Count;
            var side = cur.transform.right * (index % 2 == 0 ? 1.8f : -1.8f) - cur.transform.forward * (1f + index / 2);
            p.transform.SetPositionAndRotation(cur.transform.position + side, cur.transform.rotation);
            p.gameObject.SetActive(true);
            if (!IsFielded(p)) Fielded.Add(p);
            PartyManager.Log?.Info($"Party Lab 場: {PartyManager.Name(p)} をフィールドに置いた (表示したまま)");
            LogMotions(p);
        }
        catch (Exception e) { PartyManager.Log?.Warning($"Party Lab 場: 置けません: {e}"); }
    }

    public static void Remove(PlayerController p)
    {
        if (p == null) return;
        Fielded.RemoveAll(f => f == null || f.Pointer == p.Pointer);
        if (p.Pointer == PartyManager.Current?.Pointer) return;
        try { p.gameObject.SetActive(false); } catch { }
        PartyManager.Log?.Info($"Party Lab 場: {PartyManager.Name(p)} をしまった");
    }

    public static void RemoveAll()
    {
        foreach (var p in Fielded.ToArray()) Remove(p);
        Fielded.Clear();
    }

    /// <summary>名前に keyword を含む動作のうち、いちばん短い名前のもの (除外語を含むものは除く)</summary>
    public static string FindMotion(PlayerController p, string keyword, params string[] exclude)
    {
        string best = null;
        try
        {
            var list = p.GetMotionList();
            for (int i = 0; list != null && i < list.Count; i++)
            {
                var n = list[i]?.name;
                if (string.IsNullOrEmpty(n) || n.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (exclude.Any(x => n.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0)) continue;
                if (best == null || n.Length < best.Length) best = n;
            }
        }
        catch { }
        return best;
    }

    /// <summary>置いたキャラに動作をさせる (いちばん近い敵の方を向けてから)</summary>
    public static void Act(PlayerController p, string kind)
    {
        if (p == null) return;
        string name = kind switch
        {
            "攻撃" => FindMotion(p, "Combo", "QTE", "Charge", "Guard"),
            "特殊攻撃" => FindMotion(p, "SpecialAttack", "QTE"),
            "追加攻撃" => FindMotion(p, "NormalAttack_QTE"),
            "回避" => FindMotion(p, "Dash", "Attack", "QTE", "Perfect"),
            _ => null,
        };
        if (name == null)
        {
            PartyManager.Log?.Info($"Party Lab 場: {PartyManager.Name(p)} に「{kind}」の動作が見つかりません");
            return;
        }
        FaceNearestEnemy(p);
        try
        {
            p.ChangeMotion(name, true, 0.05f, default);
            PartyManager.Log?.Info($"Party Lab 場: {PartyManager.Name(p)} に「{kind}」'{name}' をさせた → 今の動作 '{p.GetCurMotion()?.name}'");
        }
        catch (Exception e) { PartyManager.Log?.Warning($"Party Lab 場: 動作をさせられません: {e.Message}"); }
    }

    private static void FaceNearestEnemy(PlayerController p)
    {
        try
        {
            EnemyController best = null;
            float bestDist = 30f;
            foreach (var e in Object.FindObjectsOfType<EnemyController>())
            {
                if (e == null || !e.gameObject.activeInHierarchy || !e.IsAlive()) continue;
                float d = Vector3.Distance(p.transform.position, e.transform.position);
                if (d < bestDist) { bestDist = d; best = e; }
            }
            if (best == null) return;
            var look = best.transform.position - p.transform.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.01f) p.transform.rotation = Quaternion.LookRotation(look);
        }
        catch { }
    }

    private static void LogMotions(PlayerController p)
    {
        if (!Listed.Add(p.Pointer)) return;
        try
        {
            var names = new List<string>();
            var list = p.GetMotionList();
            for (int i = 0; list != null && i < list.Count; i++)
                if (!string.IsNullOrEmpty(list[i]?.name)) names.Add(list[i].name);
            PartyManager.Log?.Info($"Party Lab 場: {PartyManager.Name(p)} の動作の一覧 ({names.Count} 個): {string.Join(", ", names)}");
        }
        catch { }
    }

    /// <summary>Party Lab を開いている間、毎フレーム呼ぶ。1 秒ごとに、置いたキャラの様子をログに出す</summary>
    public static void Tick()
    {
        Fielded.RemoveAll(f => f == null);
        if (Fielded.Count == 0 || Time.unscaledTime < _nextReport) return;
        _nextReport = Time.unscaledTime + 1f;
        var sb = new StringBuilder("Party Lab 場: 様子");
        foreach (var p in Fielded)
        {
            try
            {
                var pos = p.transform.position;
                float moved = LastPos.TryGetValue(p.Pointer, out var last) ? Vector3.Distance(last, pos) : 0f;
                LastPos[p.Pointer] = pos;
                sb.Append($" | {PartyManager.Name(p)} 表示={p.gameObject.activeInHierarchy} 動作='{p.GetCurMotion()?.name}' " +
                          $"HP {p.GetCurHp():0}/{p.GetMaxHp():0} 1 秒で {moved:0.00}m 動いた");
            }
            catch (Exception e) { sb.Append($" | (読めません: {e.Message})"); }
        }
        if (HitsBy.Count > 0) sb.Append($" || 敵に当てた: {string.Join(", ", HitsBy.Select(kv => $"{kv.Key} {kv.Value} 回"))}");
        if (HitsOn.Count > 0) sb.Append($" || 敵の攻撃を受けた: {string.Join(", ", HitsOn.Select(kv => $"{kv.Key} {kv.Value} 回"))}");
        HitsBy.Clear();
        HitsOn.Clear();
        PartyManager.Log?.Info(sb.ToString());
    }

    public static void OnEnemyHit(Transform atker)
    {
        if (Fielded.Count == 0 || atker == null) return;
        try
        {
            var p = atker.GetComponentInParent<PlayerController>(true);
            string who = p == null ? $"'{atker.name}'" : PartyManager.Name(p) + (p.Pointer == PartyManager.Current?.Pointer ? "(操作中)" : "(置いたキャラ)");
            HitsBy[who] = HitsBy.GetValueOrDefault(who) + 1;
        }
        catch { }
    }

    public static void OnPlayerHit(PlayerController p)
    {
        if (Fielded.Count == 0 || p == null) return;
        string who = PartyManager.Name(p) + (p.Pointer == PartyManager.Current?.Pointer ? "(操作中)" : "(置いたキャラ)");
        HitsOn[who] = HitsOn.GetValueOrDefault(who) + 1;
    }
}

// void PlayerController.KeyRespond(): 毎フレームのキー入力への反応 (PlayerController.Update から)
[HarmonyPatch(typeof(PlayerController), nameof(PlayerController.KeyRespond))]
internal static class FieldProbeKeyPatch
{
    private static bool Prefix(PlayerController __instance)
    {
        if (!FieldProbe.BlockOthersInput || FieldProbe.Fielded.Count == 0) return true;
        var cur = PartyManager.Current;
        return cur == null || __instance.Pointer == cur.Pointer; // 操作中でないキャラはキーに反応しない
    }
}

[HarmonyPatch(typeof(EnemyController), nameof(EnemyController.GetHit))]
internal static class FieldProbeEnemyHitPatch
{
    private static void Postfix(Transform atker)
    {
        try { FieldProbe.OnEnemyHit(atker); } catch { }
    }
}
