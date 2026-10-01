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
    public static float FollowStart = 2.5f;  // これより離れたら、ついて歩き始める
    public static float FollowStop = 1.2f;   // ここまで近づいたら止まる
    public static float RunSpeed = 6f;       // 走る速さ (m/秒)。遠いときは速くする
    public static float WarpDistance = 25f;
    public static float AttackInterval = 0.18f;

    /// <summary>操作キャラが最後に殴った敵 (みんなで狙う)</summary>
    public static EnemyController PlayerTarget;

    private sealed class Brain
    {
        public float NextAttack;
        public float LastAttack = -999f;
        // 近づけているかの見張り (2 秒で 0.5m も近づけなければワープ)
        public float WatchDist = float.MaxValue;
        public float WatchSince;
        // 動作の見張り (終わったのに戻らない動作を待機に戻す)
        public string Motion = "";
        public float MotionSince;
        public float EndedSince = -1f;
        public ActionAnimController Anim;
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

    // 敵の一覧 (FindObjectsOfType は重いので 0.5 秒に 1 回だけ作り直す)
    private static readonly List<EnemyController> Enemies = new();
    private static float _enemiesAt = -999f;

    private static List<EnemyController> AliveEnemies()
    {
        if (Time.unscaledTime - _enemiesAt >= 0.5f)
        {
            _enemiesAt = Time.unscaledTime;
            Enemies.Clear();
            try
            {
                foreach (var e in Object.FindObjectsOfType<EnemyController>())
                    if (e != null) Enemies.Add(e);
            }
            catch { }
        }
        return Enemies;
    }

    private static EnemyController PickTarget(PlayerController player)
    {
        if (Alive(PlayerTarget) && Dist(PlayerTarget.transform.position, player.transform.position) < SearchRange * 1.5f)
            return PlayerTarget;
        EnemyController best = null;
        float bestDist = SearchRange;
        foreach (var e in AliveEnemies())
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

        // 操作していたキャラがオートになった直後など、走る動作のままなら「走っている」扱いにする (止められるように)
        if (!b.Moving && motion.IndexOf("Run", StringComparison.OrdinalIgnoreCase) >= 0) b.Moving = true;

        // 特殊攻撃・追加攻撃・回避なども、アニメが終わっても動作のまま止まる (戻すのはキー入力の処理らしい)。
        // アニメが最後まで進んで 0.3 秒たつか、同じ動作のまま 8 秒たったら待機に戻す
        if (ReleaseFinished(p, b, motion))
        {
            motion = p.GetCurMotion()?.name ?? "";
            busy = IsBusy(motion);
        }

        // 殴るのをやめてもコンボの動作のまま止まることがある (コンボを終わらせるのはキー入力の処理らしい)。
        // 最後の Attack から 0.5 秒たってもコンボのままなら、コンボを終わらせて待機に戻す
        if (motion.IndexOf("Combo", StringComparison.OrdinalIgnoreCase) >= 0 && Time.unscaledTime - b.LastAttack > 0.5f)
        {
            FieldSwap.Run(p, () =>
            {
                try { p.GetMotionController()?.ResetCombo(); } catch { }
                p.ChangeMotion("Idle", true, 0.15f, default);
            });
            b.Moving = false;
            motion = p.GetCurMotion()?.name ?? "";
            busy = IsBusy(motion);
            PartyManager.Log?.Info($"Party Lab オート: {PartyManager.Name(p)} のコンボを終わらせた → 動作='{motion}'");
        }

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
                if (!busy && !CheckStuck(p, b, d, target.transform.position - to.normalized * (AttackRange * 0.8f)))
                    MoveToward(p, b, to);
                Act(p, b, $"敵 '{target.name}' へ走る ({d:0.0}m)");
                return;
            }
            Stop(p, b);
            if (Time.unscaledTime >= b.NextAttack)
            {
                b.NextAttack = Time.unscaledTime + AttackInterval;
                b.LastAttack = Time.unscaledTime;
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
        float need = b.Moving ? FollowStop : FollowStart;
        if (toSlot.magnitude > need && !busy)
        {
            if (!CheckStuck(p, b, toSlot.magnitude, slot)) MoveToward(p, b, toSlot);
            Act(p, b, $"ついて歩く ({toSlot.magnitude:0.0}m)");
        }
        else
        {
            Stop(p, b);
            b.WatchDist = float.MaxValue;
            Act(p, b, "待機");
        }
    }

    private static bool ReleaseFinished(PlayerController p, Brain b, string motion)
    {
        float now = Time.unscaledTime;
        if (motion != b.Motion)
        {
            b.Motion = motion;
            b.MotionSince = now;
            b.EndedSince = -1f;
        }
        if (!IsBusy(motion) || motion.IndexOf("Combo", StringComparison.OrdinalIgnoreCase) >= 0) return false;

        float norm = 0f;
        try
        {
            b.Anim ??= p.GetComponentInChildren<ActionAnimController>(true);
            if (b.Anim != null) norm = b.Anim.GetCurAnimNormalizedTime();
        }
        catch { }
        if (norm >= 0.98f) { if (b.EndedSince < 0f) b.EndedSince = now; }
        else b.EndedSince = -1f;

        bool ended = b.EndedSince >= 0f && now - b.EndedSince > 0.3f;
        bool tooLong = now - b.MotionSince > 8f;
        if (!ended && !tooLong) return false;

        FieldSwap.Run(p, () =>
        {
            try { p.GetMotionController()?.ResetCombo(); } catch { }
            p.ChangeMotion("Idle", true, 0.15f, default);
        });
        b.Moving = false;
        PartyManager.Log?.Info($"Party Lab オート: {PartyManager.Name(p)} の '{motion}' が{(ended ? "終わったのに戻らない" : " 8 秒続いた")}ので待機に戻した");
        return true;
    }

    /// <summary>攻撃・回避・被弾などの最中 (移動で上書きしない)</summary>
    private static bool IsBusy(string motion) =>
        motion.IndexOf("Attack", StringComparison.OrdinalIgnoreCase) >= 0 ||
        motion.IndexOf("Combo", StringComparison.OrdinalIgnoreCase) >= 0 ||
        motion.IndexOf("QTE", StringComparison.OrdinalIgnoreCase) >= 0 ||
        motion.IndexOf("Dash", StringComparison.OrdinalIgnoreCase) >= 0 ||
        motion.IndexOf("Parry", StringComparison.OrdinalIgnoreCase) >= 0 ||
        motion.IndexOf("Hit", StringComparison.OrdinalIgnoreCase) >= 0;

    /// <summary>
    /// 走る。アニメ (Run) だけゲームに再生させ、向きと移動はこちらで CharacterController を直接動かす
    /// (ゲームの MovementController.Move は意図した方向に進まなかった)
    /// </summary>
    private static void MoveToward(PlayerController p, Brain b, Vector3 dir)
    {
        var d = dir;
        d.y = 0f;
        float dist = d.magnitude;
        if (dist < 0.01f) return;
        d /= dist;
        if (!b.Moving)
        {
            FieldSwap.Run(p, () =>
            {
                try
                {
                    var mc = p.GetMovementController();
                    mc?.SetMoveAble(true);
                    mc?.SetTurnAble(true);
                }
                catch { }
                p.ChangeMotion("Run", true, 0.1f, default);
            });
            b.Moving = true;
        }

        float dt = Mathf.Min(Time.deltaTime, 0.05f);
        p.transform.rotation = Quaternion.RotateTowards(p.transform.rotation, Quaternion.LookRotation(d), 720f * dt);
        float speed = RunSpeed * (dist > 8f ? 1.4f : 1f);
        var step = d * Mathf.Min(speed * dt, dist) + Vector3.down * 4f * dt; // 少し下に押して地面に沿わせる
        var cc = p.GetComponent<CharacterController>();
        if (cc != null && cc.enabled) cc.Move(step);
        else p.transform.position += step;
    }

    /// <summary>2 秒たっても目標に 0.5m も近づけていなければ (引っかかった・逆に走った)、目標の近くへワープして true</summary>
    private static bool CheckStuck(PlayerController p, Brain b, float dist, Vector3 goal)
    {
        float now = Time.unscaledTime;
        if (dist < b.WatchDist - 0.5f)
        {
            b.WatchDist = dist;
            b.WatchSince = now;
            return false;
        }
        if (now - b.WatchSince < 2f) return false;
        PartyManager.Log?.Info($"Party Lab オート: {PartyManager.Name(p)} が 2 秒近づけない (あと {dist:0.0}m、向き {p.transform.forward}) → ワープ");
        Teleport(p, goal);
        b.WatchDist = float.MaxValue;
        b.WatchSince = now;
        return true;
    }

    private static void Stop(PlayerController p, Brain b)
    {
        if (!b.Moving) return;
        b.Moving = false;
        FieldSwap.Run(p, () =>
        {
            string m = p.GetCurMotion()?.name ?? "";
            // 走る系の動作 (Run / RunStop / RunTurn / FastRun...) なら、強制で待機に
            if (m.IndexOf("Run", StringComparison.OrdinalIgnoreCase) >= 0) p.ChangeMotion("Idle", true, 0.15f, default);
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
        float best = float.MaxValue;
        foreach (var hit in Physics.RaycastAll(pos + Vector3.up * 3f, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore))
        {
            var col = hit.collider;
            if (col == null || col.GetComponentInParent<PlayerController>() != null || col.GetComponentInParent<EnemyController>() != null) continue;
            if (hit.distance < best) { best = hit.distance; pos.y = hit.point.y; }
        }
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
