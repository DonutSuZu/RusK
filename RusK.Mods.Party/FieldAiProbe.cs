using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace RusK.Mods.Party;

/// <summary>
/// 調査用 (Party Lab、エンドフィールド風の戦闘の試作): フィールドに置いたキャラのオート。
///   - 狙う敵: 操作キャラが最後に殴った敵 (倒れたら、操作キャラの近くの敵)
///   - 敵から離れていれば走って近づき、近ければ敵の方を向いて Attack() を繰り返す (コンボはゲームに任せる)
///   - 敵がいなければ、操作キャラの斜め後ろについて歩く。遠すぎたら近くにワープ
/// 移動はキー入力と同じく MovementController.Rotate / Move (向き) を、「今のプレイヤー」を差し替えて呼ぶ
/// </summary>
internal static class FieldAi
{
    public static bool Enabled;

    public static float AttackRange = 2.2f;
    public static float SearchRange = 15f;
    public static float FollowStop = 1.5f;
    public static float WarpDistance = 25f;
    public static float AttackInterval = 0.18f;

    /// <summary>操作キャラが最後に殴った敵 (みんなで狙う)</summary>
    public static EnemyController PlayerTarget;

    private sealed class Brain
    {
        public float NextAttack;
        public bool Moving;
        public string LastAction = "";
        public float LastLog;
    }

    private static readonly Dictionary<IntPtr, Brain> Brains = new();

    public static void Tick()
    {
        if (!Enabled || FieldProbe.Fielded.Count == 0) return;
        var player = PartyManager.Current;
        if (player == null) return;

        var target = PickTarget(player);
        int index = 0;
        foreach (var p in FieldProbe.Fielded.ToArray())
        {
            if (p == null || p.Pointer == player.Pointer || !p.gameObject.activeInHierarchy) continue;
            if (!Brains.TryGetValue(p.Pointer, out var b)) Brains[p.Pointer] = b = new Brain();
            try { Think(p, b, player, target, index++); }
            catch (Exception e)
            {
                if (Time.unscaledTime - b.LastLog > 3f)
                {
                    b.LastLog = Time.unscaledTime;
                    PartyManager.Log?.Warning($"Party Lab オート: {PartyManager.Name(p)} でエラー: {e.Message}");
                }
            }
        }
    }

    private static EnemyController PickTarget(PlayerController player)
    {
        if (Alive(PlayerTarget) && Dist(PlayerTarget.transform.position, player.transform.position) < SearchRange * 1.5f)
            return PlayerTarget;
        EnemyController best = null;
        float bestDist = SearchRange;
        foreach (var e in Object.FindObjectsOfType<EnemyController>())
        {
            if (!Alive(e)) continue;
            float d = Dist(e.transform.position, player.transform.position);
            if (d < bestDist) { bestDist = d; best = e; }
        }
        return best;
    }

    private static void Think(PlayerController p, Brain b, PlayerController player, EnemyController target, int index)
    {
        string motion = p.GetCurMotion()?.name ?? "";
        bool busy = IsBusy(motion);

        // 遠すぎたら操作キャラの近くへワープ
        if (Dist(p.transform.position, player.transform.position) > WarpDistance)
        {
            Teleport(p, FollowPoint(player, index));
            Act(p, b, "ワープ");
            return;
        }

        if (target != null)
        {
            var to = target.transform.position - p.transform.position;
            to.y = 0f;
            float d = to.magnitude;
            if (d > AttackRange)
            {
                if (!busy) MoveToward(p, b, to);
                Act(p, b, $"敵 '{target.name}' へ走る ({d:0.0}m)");
                return;
            }
            Stop(p, b);
            if (Time.unscaledTime >= b.NextAttack)
            {
                b.NextAttack = Time.unscaledTime + AttackInterval;
                FieldSwap.Run(p, () =>
                {
                    if (!busy || motion.IndexOf("Combo", StringComparison.OrdinalIgnoreCase) >= 0)
                        p.GetMovementController()?.Rotate(to.normalized);
                    p.Attack();
                });
            }
            Act(p, b, $"敵 '{target.name}' を攻撃");
            return;
        }

        // 敵がいなければ、ついて歩く
        var slot = FollowPoint(player, index);
        var toSlot = slot - p.transform.position;
        toSlot.y = 0f;
        if (toSlot.magnitude > FollowStop && !busy)
        {
            MoveToward(p, b, toSlot);
            Act(p, b, $"ついて歩く ({toSlot.magnitude:0.0}m)");
        }
        else
        {
            Stop(p, b);
            Act(p, b, "待機");
        }
    }

    /// <summary>攻撃・回避・被弾などの最中 (移動で上書きしない)</summary>
    private static bool IsBusy(string motion) =>
        motion.IndexOf("Attack", StringComparison.OrdinalIgnoreCase) >= 0 ||
        motion.IndexOf("Combo", StringComparison.OrdinalIgnoreCase) >= 0 ||
        motion.IndexOf("QTE", StringComparison.OrdinalIgnoreCase) >= 0 ||
        motion.IndexOf("Dash", StringComparison.OrdinalIgnoreCase) >= 0 ||
        motion.IndexOf("Parry", StringComparison.OrdinalIgnoreCase) >= 0 ||
        motion.IndexOf("Hit", StringComparison.OrdinalIgnoreCase) >= 0;

    private static void MoveToward(PlayerController p, Brain b, Vector3 dir)
    {
        var d = dir.normalized;
        FieldSwap.Run(p, () =>
        {
            if (!b.Moving)
            {
                p.ChangeMotion("Run", false, 0.1f, default);
                b.Moving = true;
            }
            var mc = p.GetMovementController();
            mc?.Rotate(d);
            mc?.Move(d);
        });
    }

    private static void Stop(PlayerController p, Brain b)
    {
        if (!b.Moving) return;
        b.Moving = false;
        FieldSwap.Run(p, () =>
        {
            string m = p.GetCurMotion()?.name ?? "";
            if (m.StartsWith("Run") || m.StartsWith("FastRun")) p.ChangeMotion("Idle", false, 0.15f, default);
        });
    }

    private static Vector3 FollowPoint(PlayerController player, int index)
    {
        var t = player.transform;
        float side = index % 2 == 0 ? 1.6f : -1.6f;
        return t.position + t.right * side - t.forward * (1.4f + index / 2 * 1.2f);
    }

    private static void Teleport(PlayerController p, Vector3 pos)
    {
        var cc = p.GetComponent<CharacterController>();
        bool had = cc != null && cc.enabled;
        if (had) cc.enabled = false;
        p.transform.position = pos;
        if (had) cc.enabled = true;
    }

    private static void Act(PlayerController p, Brain b, string what)
    {
        // 行動が変わったときだけログに出す (数字の部分は比べない)
        string key = what.Split('(')[0];
        if (key == b.LastAction) return;
        b.LastAction = key;
        PartyManager.Log?.Info($"Party Lab オート: {PartyManager.Name(p)} → {what} 動作='{p.GetCurMotion()?.name}'");
    }

    private static bool Alive(EnemyController e)
    {
        try { return e != null && e.gameObject.activeInHierarchy && e.IsAlive(); }
        catch { return false; }
    }

    private static float Dist(Vector3 a, Vector3 b)
    {
        a.y = 0f;
        b.y = 0f;
        return Vector3.Distance(a, b);
    }
}
