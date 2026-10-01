using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace RusK.Mods.Party;

/// <summary>
/// 調査用 (Party Lab、エンドフィールド風の戦闘の試作): フィールドに置いたキャラの部品が動く間だけ、
/// ゲームの「今のプレイヤー」(GameUtil.m_curPlayer) をそのキャラに差し替え、終わったら戻す。
///
/// キャラの部品 (ActionAnimController のアニメ、MovementController の移動・地面、PlayerController の攻撃など) は
/// 今のプレイヤーかどうかを見て、そうでなければ何もしないらしい
/// (置いたキャラは動作が SpecialAttack に変わってもアニメが待機のまま進まず、地面に埋まった)
/// </summary>
internal static class FieldSwap
{
    public static bool Enabled = true;

    private static readonly Stack<PlayerController> Saved = new();
    private static readonly Stack<double> SavedCrtId = new();

    /// <summary>
    /// セーブの「今のキャラ」(GameSave.lastCrtId) も差し替えるか。装備の能力値などはこの番号で引くらしく、
    /// m_curPlayer だけ差し替えると置いたキャラがリーダーの装備でダメージを計算していた (180 → 17000)
    /// </summary>
    public static bool SwapCharacterId = true;
    private static readonly Dictionary<IntPtr, PlayerController> Owners = new();
    private static readonly Dictionary<IntPtr, (IntPtr owner, PlayerController player)> BoxOwners = new();

    /// <summary>差し替え中か (このときはキー入力の処理を止める)</summary>
    public static bool InSwap => Saved.Count > 0;

    private static PlayerController Owner(Component c)
    {
        if (c == null) return null;
        // 攻撃の当たり判定は使い回されて持ち主が変わるので、毎回 m_owner から調べる
        // (持ち主の Transform が前と同じなら、前の結果を使い回す。敵の分も含めて数が多いので)
        var box = c.TryCast<AttackBoxController>();
        if (box != null)
        {
            try
            {
                var owner = box.m_owner;
                if (owner == null) return null;
                if (BoxOwners.TryGetValue(c.Pointer, out var cached) && cached.owner == owner.Pointer) return cached.player;
                var player = owner.GetComponentInParent<PlayerController>(true);
                BoxOwners[c.Pointer] = (owner.Pointer, player);
                return player;
            }
            catch { return null; }
        }
        if (Owners.TryGetValue(c.Pointer, out var p) && p != null) return p;
        try { p = c.TryCast<PlayerController>() ?? c.GetComponentInParent<PlayerController>(true); }
        catch { p = null; }
        Owners[c.Pointer] = p;
        return p;
    }

    /// <summary>c の持ち主が置いたキャラなら、今のプレイヤーを差し替えて true</summary>
    public static bool Begin(Component c)
    {
        if (!Enabled || FieldProbe.Fielded.Count == 0) return false;
        var owner = Owner(c);
        if (owner == null || !FieldProbe.IsFielded(owner)) return false;
        var util = GameUtil.Instance;
        if (util == null) return false;
        var cur = util.m_curPlayer;
        if (cur != null && cur.Pointer == owner.Pointer) return false;
        Saved.Push(cur);
        util.m_curPlayer = owner;
        double crt = double.NaN;
        if (SwapCharacterId)
        {
            try
            {
                var save = GameUtil.m_gameSaveCache;
                if (save != null)
                {
                    crt = save.lastCrtId;
                    save.lastCrtId = PartyManager.Id(owner);
                }
            }
            catch { crt = double.NaN; }
        }
        SavedCrtId.Push(crt);
        return true;
    }

    public static void End(bool swapped)
    {
        if (!swapped || Saved.Count == 0) return;
        var prev = Saved.Pop();
        double crt = SavedCrtId.Count > 0 ? SavedCrtId.Pop() : double.NaN;
        try
        {
            var util = GameUtil.Instance;
            util.m_curPlayer = prev;
            if (!double.IsNaN(crt) && GameUtil.m_gameSaveCache != null) GameUtil.m_gameSaveCache.lastCrtId = crt;
        }
        catch { }
    }

    /// <summary>置いたキャラに何かをさせる間だけ差し替える</summary>
    public static void Run(PlayerController p, Action action)
    {
        bool swapped = Begin(p);
        try { action(); }
        finally { End(swapped); }
    }
}

// void SkillController.SkillUpdateCheck(PlayerController playerCon): PlayerController.Update から毎フレーム。
// 画面のスキル UI は操作キャラの分しか無いので、置いたキャラ (差し替え中) で呼ぶと SkillUIController.SkillUIUpdate が
// NullReferenceException で落ち、その Update の残りも飛ぶ (毎フレーム 1 万回以上のエラー)。差し替え中は飛ばす
[HarmonyPatch(typeof(SkillController), nameof(SkillController.SkillUpdateCheck))]
internal static class FieldSwapSkillUiPatch
{
    private static bool Prefix() => !FieldSwap.InSwap;
}

[HarmonyPatch]
internal static class FieldSwapPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        var list = new (Type type, string name)[]
        {
            (typeof(PlayerController), "Update"),
            (typeof(PlayerController), "FixedUpdate"),
            (typeof(PlayerController), "LateUpdate"),
            (typeof(ActionAnimController), "Update"),
            (typeof(ActionAnimController), "OnAnimatorMove"),
            (typeof(MotionController), "Update"),
            (typeof(MovementController), "Update"),
            (typeof(MovementController), "FixedUpdate"),
            (typeof(MovementController), "LateUpdate"),
        };
        foreach (var (type, name) in list)
        {
            var m = type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, Type.EmptyTypes, null);
            if (m != null) yield return m;
        }
        // 攻撃の当たり判定が敵に触れたときの処理 (風禾の攻撃が敵に効かなかった)
        foreach (var m in typeof(AttackBoxController).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (m.Name is "TryTriggerHit" or "OnTriggerEnter" or "OnTriggerStay" or "Update" or "FixedUpdate" or "LateUpdate")
                yield return m;
        }
    }

    private static void Prefix(Component __instance, out bool __state) => __state = FieldSwap.Begin(__instance);

    private static void Finalizer(bool __state) => FieldSwap.End(__state);
}
