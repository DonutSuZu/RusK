using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RusK.Mods.Party;

/// <summary>
/// 切り替えの後、敵の AI (Behavior Designer の BehaviorTree、EnemyController.m_bhvTree) の変数のうち、
/// 前のキャラ (Transform / GameObject) を指しているものを今のキャラに付け替える。
///
/// 切り替えの後に敵が立ち尽くし、攻撃されるまで反応しない問題の対策。
/// 「気付き」(m_observePlayer) は外れていなかったので、AI が覚えたプレイヤーが古いままなのが原因と見ている
/// (AI の処理 TestBehavior などが SharedTransform player を持っている)
/// </summary>
internal static class EnemyAiRetarget
{
    /// <summary>全部の敵の AI の変数で、今のキャラ以外のパーティのキャラを指しているものを付け替える。付け替えた数を返す</summary>
    public static int Retarget(PlayerController cur, string when)
    {
        if (cur == null) return 0;
        var others = new HashSet<IntPtr>(PartyManager.Members.Where(m => m != null && m.Pointer != cur.Pointer)
            .SelectMany(m => new[] { m.transform.Pointer, m.gameObject.Pointer }));
        if (others.Count == 0) return 0;

        int changed = 0;
        var names = new HashSet<string>();
        foreach (var e in Object.FindObjectsOfType<EnemyController>())
        {
            if (e == null || !e.gameObject.activeInHierarchy) continue;
            try
            {
                var bt = e.m_bhvTree;
                var vars = bt?.GetAllVariables();
                if (vars == null) continue;
                for (int i = 0; i < vars.Count; i++)
                {
                    var v = vars[i];
                    var val = v?.GetValue();
                    if (val == null) continue;
                    var tr = val.TryCast<Transform>();
                    if (tr != null)
                    {
                        if (!others.Contains(tr.Pointer) && !IsOtherMember(tr, cur)) continue;
                        v.SetValue(cur.transform);
                        changed++;
                        names.Add(v.Name);
                        continue;
                    }
                    var go = val.TryCast<GameObject>();
                    if (go != null && (others.Contains(go.Pointer) || IsOtherMember(go.transform, cur)))
                    {
                        v.SetValue(cur.gameObject);
                        changed++;
                        names.Add(v.Name);
                    }
                }
            }
            catch (Exception ex)
            {
                PartyManager.Log?.Warning($"Party: 敵の AI の付け替えに失敗: {ex.Message}");
            }
        }
        if (changed > 0)
            PartyManager.Log?.Info($"Party: 敵の AI が前のキャラを見ていたので付け替えた ({when}、{changed} 個、変数 {string.Join(", ", names)})");
        return changed;
    }

    /// <summary>その Transform が、今のキャラ以外のパーティのキャラの子 (骨など) か</summary>
    private static bool IsOtherMember(Transform t, PlayerController cur)
    {
        try
        {
            var p = t.GetComponentInParent<PlayerController>(true);
            return p != null && p.Pointer != cur.Pointer && PartyManager.Members.Any(m => m != null && m.Pointer == p.Pointer);
        }
        catch { return false; }
    }

    /// <summary>調査用: 近くの敵の AI の変数を全部ログに出す</summary>
    public static void Dump(int maxEnemies = 5)
    {
        var cur = PartyManager.Current;
        var sb = new StringBuilder();
        sb.AppendLine($"Party: 敵の AI の変数 (操作中 {PartyManager.Name(cur)})");
        var enemies = Object.FindObjectsOfType<EnemyController>()
            .Where(e => e != null && e.gameObject.activeInHierarchy)
            .OrderBy(e => cur == null ? 0f : Vector3.Distance(e.transform.position, cur.transform.position))
            .Take(maxEnemies);
        foreach (var e in enemies)
        {
            sb.AppendLine($"  敵 '{e.name}' 気付き={Safe(() => e.GetObservePlayer())} 動作='{Safe(() => e.m_curState?.name)}'" +
                          $" AI='{Safe(() => e.m_bhvTree?.BehaviorName)}' 有効={Safe(() => e.m_bhvTree?.enabled)}");
            try
            {
                var vars = e.m_bhvTree?.GetAllVariables();
                for (int i = 0; vars != null && i < vars.Count; i++)
                {
                    var v = vars[i];
                    var val = v?.GetValue();
                    string desc = val == null ? "null" : Describe(val, cur);
                    sb.AppendLine($"    {v?.Name} ({v?.GetIl2CppType()?.Name}) = {desc}");
                }
            }
            catch (Exception ex) { sb.AppendLine($"    (変数を読めません: {ex.Message})"); }
        }
        PartyManager.Log?.Info(sb.ToString());
    }

    private static string Describe(Il2CppSystem.Object val, PlayerController cur)
    {
        var tr = val.TryCast<Transform>() ?? val.TryCast<GameObject>()?.transform;
        if (tr != null)
        {
            var p = tr.GetComponentInParent<PlayerController>(true);
            string who = p == null ? "" : p.Pointer == cur?.Pointer ? " ← 操作中のキャラ" : $" ← 別のキャラ {PartyManager.Name(p)} (active={p.gameObject.activeSelf})";
            return $"'{tr.name}'{who}";
        }
        try { return val.ToString(); } catch { return "?"; }
    }

    private static string Safe(Func<object> f)
    {
        try { return f()?.ToString() ?? "null"; }
        catch { return "?"; }
    }
}
