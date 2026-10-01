using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;

namespace RusK.Mods.Op2;

/// <summary>
/// エンドフィールドスタイルのフィールド: パーティの全員をフィールドに出し、操作していないキャラをオートで動かす。
///
/// Party Lab での試作で分かったこと:
///   - 控え (一度も操作キャラになっていないキャラ) は準備 (SetData) が空振りしていて T ポーズ。
///     SetData は操作キャラのときしか働かないので、一瞬だけ操作キャラにして準備させ、すぐ戻す
///   - キャラの部品は「今のプレイヤー」(GameUtil.m_curPlayer) が自分でないと動かない → FieldSwap で差し替える
///   - PlayerController.Update は毎フレーム KeyRespond (移動・走り) を呼ぶので、操作していないキャラは止める
///   - 操作していないキャラは攻撃もダメージも受けない (PartyBridge.Shielded)
/// </summary>
internal static class Field
{
    /// <summary>フィールドにいる、操作していないキャラ (オートで動く)</summary>
    public static readonly List<PlayerController> Fielded = new();

    public static bool IsFielded(PlayerController p)
    {
        if (p == null || Fielded.Count == 0) return false;
        var ptr = p.Pointer;
        for (int i = 0; i < Fielded.Count; i++)
        {
            var f = Fielded[i];
            if (f != null && f.Pointer == ptr) return true;
        }
        return false;
    }

    /// <summary>攻撃もダメージも受けないキャラ (フィールドにいる、操作していないキャラ)</summary>
    public static bool IsShielded(PlayerController p) => p != null && IsFielded(p) && p.Pointer != P.Current?.Pointer;

    private static float _nextPlace;

    /// <summary>毎フレーム (Party から、エンドフィールドスタイルのときだけ)</summary>
    public static void Update()
    {
        Fielded.RemoveAll(f => f == null);
        if (!P.InFight)
        {
            if (Fielded.Count > 0) Stop();
            FieldSkills.UpdateFade();
            return;
        }

        TickInit();

        // パーティの全員をフィールドに (0.5 秒に 1 回確かめる)。戦闘不能のキャラはしまう
        if (Time.unscaledTime >= _nextPlace)
        {
            _nextPlace = Time.unscaledTime + 0.5f;
            var cur = P.Current;
            if (cur != null)
            {
                foreach (var m in P.Members)
                {
                    if (m == null || m.Pointer == cur.Pointer) continue;
                    bool down = P.IsDown(m);
                    if (down && IsFielded(m)) Remove(m);
                    else if (!down && !IsFielded(m)) Place(m, cur);
                }
                foreach (var f in Fielded.ToArray())
                    if (f != null && !P.Members.Any(m => m != null && m.Pointer == f.Pointer)) Fielded.Remove(f);
            }
        }

        FieldAi.Tick();
        FieldSkills.Tick();
        FieldSkills.UpdateFade();
    }

    /// <summary>スタイルをやめた・戦闘ステージを出た: 操作していないキャラをしまう</summary>
    public static void Stop()
    {
        foreach (var p in Fielded.ToArray()) Remove(p);
        Fielded.Clear();
        _initQueue.Clear();
    }

    /// <summary>操作するキャラが変わった (Party のその場で切り替えから)。新しいキャラはオートから外し、前のキャラをオートに</summary>
    public static void OnControlSwitched(PlayerController oldCur, PlayerController next)
    {
        Fielded.RemoveAll(f => f == null || (next != null && f.Pointer == next.Pointer));
        if (oldCur != null && !IsFielded(oldCur) && !P.IsDown(oldCur)) Fielded.Add(oldCur);
    }

    private static void Place(PlayerController p, PlayerController cur)
    {
        try
        {
            int index = Fielded.Count;
            var side = cur.transform.right * (index % 2 == 0 ? 1.8f : -1.8f) - cur.transform.forward * (1f + index / 2);
            var pos = Ground(cur.transform.position + side);
            p.transform.SetPositionAndRotation(pos, cur.transform.rotation);
            p.gameObject.SetActive(true);
            Fielded.Add(p);
            _initQueue.Add((p, Time.frameCount + 2)); // Awake / Start が走った後に、準備ができたか確かめる
            P.Log?.Info($"Op.2: {P.Name(p)} をフィールドに出しました");
        }
        catch (Exception e) { P.Log?.Warning($"Op.2: {P.Name(p)} をフィールドに出せません: {e.Message}"); }
    }

    private static void Remove(PlayerController p)
    {
        if (p == null) return;
        Fielded.RemoveAll(f => f == null || f.Pointer == p.Pointer);
        if (p.Pointer == P.Current?.Pointer) return;
        try { p.gameObject.SetActive(false); } catch { }
    }

    /// <summary>上からいちばん近い地面の高さ (キャラや敵の当たり判定は除く)</summary>
    public static Vector3 Ground(Vector3 pos)
    {
        float best = float.MaxValue;
        foreach (var hit in Physics.RaycastAll(pos + Vector3.up * 3f, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore))
        {
            var col = hit.collider;
            if (col == null || col.GetComponentInParent<PlayerController>() != null || col.GetComponentInParent<EnemyController>() != null) continue;
            if (hit.distance < best) { best = hit.distance; pos.y = hit.point.y; }
        }
        return pos;
    }

    // ---- 準備 (一度も操作キャラになっていない控えは、動作の一覧もアニメも準備されていない)

    private static readonly List<(PlayerController p, int frame)> _initQueue = new();

    private static void TickInit()
    {
        for (int i = _initQueue.Count - 1; i >= 0; i--)
        {
            var (p, frame) = _initQueue[i];
            if (Time.frameCount < frame) continue;
            _initQueue.RemoveAt(i);
            if (p != null && IsFielded(p)) EnsureInitialized(p);
        }
    }

    private static int MotionCount(PlayerController p)
    {
        try { return p.GetMotionList()?.Count ?? -1; }
        catch { return -1; }
    }

    private static void EnsureInitialized(PlayerController p)
    {
        if (MotionCount(p) <= 0)
        {
            // SetData は操作キャラのときしか働かないので、一瞬だけ操作キャラにして準備させ、すぐ戻す
            var cur = P.Current;
            try
            {
                var mm = P.FindCharacter(P.Id(p));
                GameUtil.Instance.ChangePlayer(p);
                if (mm != null) p.SetData(mm.name, mm.id);
            }
            catch (Exception e) { P.Log?.Warning($"Op.2: {P.Name(p)} の準備で例外: {e.Message}"); }
            finally
            {
                if (cur != null)
                {
                    try { GameUtil.Instance.ChangePlayer(cur); } catch { }
                    try { P.RebindCamera(cur); } catch { }
                    try { cur.SetCamBind(); } catch { }
                    try { P.RefreshHud(cur); } catch { }
                }
            }
            P.Log?.Info($"Op.2: {P.Name(p)} を準備しました (動作 {MotionCount(p)} 個)");
        }
        try
        {
            var idle = FindMotion(p, "Idle", "Long", "Show", "Talk", "Near");
            if (idle != null) FieldSwap.Run(p, () => p.ChangeMotion(idle, true, 0.1f, default));
        }
        catch { }
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
}

// void PlayerController.KeyRespond(): 毎フレームのキー入力への反応 (移動・走り)。操作していないキャラは反応しない
[HarmonyPatch(typeof(PlayerController), nameof(PlayerController.KeyRespond))]
internal static class FieldKeyPatch
{
    private static bool Prefix(PlayerController __instance)
    {
        if (FieldSwap.InSwap) return false; // 操作していないキャラの処理中 (「今のプレイヤー」を差し替え中)
        if (Field.Fielded.Count == 0) return true;
        var cur = P.Current;
        return cur == null || __instance.Pointer == cur.Pointer;
    }
}

// bool EnemyController.GetHit(...): 操作キャラが殴った敵をオートの狙いに、ヒットで必殺技ゲージと EP をためる
[HarmonyPatch(typeof(EnemyController), nameof(EnemyController.GetHit))]
internal static class FieldEnemyHitPatch
{
    private static void Postfix(EnemyController __instance, Transform atker)
    {
        if (Field.Fielded.Count == 0) return;
        try
        {
            var p = atker != null ? atker.GetComponentInParent<PlayerController>(true) : null;
            if (p == null) return;
            if (!FieldSwap.InSwap && p.Pointer == P.Current?.Pointer) FieldAi.PlayerTarget = __instance;
            FieldSkills.OnHit(p);
        }
        catch { }
    }
}
