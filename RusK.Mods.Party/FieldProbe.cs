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
            var pos = cur.transform.position + side;
            // 地面の高さに合わせる (坂で埋まらないように)
            // (キャラや敵の当たり判定は除いて、上からいちばん近い地面)
            float best = float.MaxValue;
            foreach (var hit in Physics.RaycastAll(pos + Vector3.up * 3f, Vector3.down, 10f, ~0, QueryTriggerInteraction.Ignore))
            {
                var col = hit.collider;
                if (col == null || col.GetComponentInParent<PlayerController>() != null || col.GetComponentInParent<EnemyController>() != null) continue;
                if (hit.distance < best) { best = hit.distance; pos.y = hit.point.y; }
            }
            p.transform.SetPositionAndRotation(pos, cur.transform.rotation);
            p.gameObject.SetActive(true);
            if (!IsFielded(p)) Fielded.Add(p);
            PartyManager.Log?.Info($"Party Lab 場: {PartyManager.Name(p)} をフィールドに置いた (表示したまま) 動作 {MotionCount(p)} 個");
            _initTarget = p;
            _initFrame = Time.frameCount + 2; // Awake / Start が走った後に、準備ができたか確かめる
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

    private static PlayerController _initTarget;
    private static int _initFrame;

    private static int MotionCount(PlayerController p)
    {
        try { return p.GetMotionList()?.Count ?? -1; }
        catch { return -1; }
    }

    /// <summary>
    /// 置いたキャラの準備 (動作の一覧・アニメーション) ができていなければやり直す。
    /// A: 動作の一覧とアニメーションの準備だけ。B: それでもだめなら SetData を丸ごと (HUD を操作キャラに戻す)
    /// </summary>
    private static void EnsureInitialized(PlayerController p)
    {
        int before = MotionCount(p);
        if (before > 0)
        {
            PartyManager.Log?.Info($"Party Lab 場: {PartyManager.Name(p)} は準備済み (動作 {before} 個)");
            LogMotions(p);
            return;
        }
        try
        {
            p.InitialMotionList();
            p.InitialSetting();
            try { p.GetMotionController()?.InitialData(); } catch (Exception e) { PartyManager.Log?.Info($"Party Lab 場: MotionController.InitialData 失敗: {e.Message}"); }
        }
        catch (Exception e) { PartyManager.Log?.Info($"Party Lab 場: 準備 A で例外: {e.Message}"); }
        int a = MotionCount(p);
        PartyManager.Log?.Info($"Party Lab 場: {PartyManager.Name(p)} 準備 A (InitialMotionList / InitialSetting / MotionController.InitialData) → 動作 {before} → {a} 個");
        if (a <= 0)
        {
            try
            {
                var mm = PartyManager.FindCharacter(PartyManager.Id(p));
                if (mm != null) p.SetData(mm.name, mm.id);
            }
            catch (Exception e) { PartyManager.Log?.Info($"Party Lab 場: 準備 B で例外: {e.Message}"); }
            var cur = PartyManager.Current;
            if (cur != null) try { PartyManager.RefreshHud(cur); } catch { }
            PartyManager.Log?.Info($"Party Lab 場: {PartyManager.Name(p)} 準備 B (SetData) → 動作 {MotionCount(p)} 個");
        }
        if (MotionCount(p) <= 0)
        {
            // C: 準備 (SetData) は操作キャラのときしか働かないらしいので、一瞬だけ操作キャラにして準備させ、すぐ戻す
            var cur = PartyManager.Current;
            try
            {
                var mm = PartyManager.FindCharacter(PartyManager.Id(p));
                GameUtil.Instance.ChangePlayer(p);
                if (mm != null) p.SetData(mm.name, mm.id);
            }
            catch (Exception e) { PartyManager.Log?.Info($"Party Lab 場: 準備 C で例外: {e.Message}"); }
            finally
            {
                if (cur != null)
                {
                    try { GameUtil.Instance.ChangePlayer(cur); } catch { }
                    try { PartyManager.RebindCamera(cur); } catch { }
                    try { cur.SetCamBind(); } catch { }
                    try { PartyManager.RefreshHud(cur); } catch { }
                }
            }
            PartyManager.Log?.Info($"Party Lab 場: {PartyManager.Name(p)} 準備 C (一瞬だけ操作キャラにして SetData) → 動作 {MotionCount(p)} 個、操作中 {PartyManager.Name(PartyManager.Current)}");
        }
        try
        {
            var idle = FindMotion(p, "Idle", "Long", "Show", "Talk", "Near");
            if (idle != null) p.ChangeMotion(idle, true, 0.1f, default);
            PartyManager.Log?.Info($"Party Lab 場: {PartyManager.Name(p)} を待機 '{idle}' に → 今の動作 '{p.GetCurMotion()?.name}'");
        }
        catch (Exception e) { PartyManager.Log?.Info($"Party Lab 場: 待機にできません: {e.Message}"); }
        LogMotions(p);
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
            // 入力と同じ入り口 (コンボを進める)。置いたキャラを「今のプレイヤー」にしている間に呼ぶ
            FieldSwap.Run(p, () =>
            {
                if (kind == "攻撃") p.Attack();
                else if (kind == "特殊攻撃") p.SpecialAttack();
                else p.ChangeMotion(name, true, 0.05f, default);
            });
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
        if (_initTarget != null && Time.frameCount >= _initFrame)
        {
            var t = _initTarget;
            _initTarget = null;
            EnsureInitialized(t);
        }
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
                          $"HP {p.GetCurHp():0}/{p.GetMaxHp():0} 1 秒で {moved:0.00}m 動いた {AnimState(p)}");
            }
            catch (Exception e) { sb.Append($" | (読めません: {e.Message})"); }
        }
        if (HitsBy.Count > 0) sb.Append($" || 敵に当てた: {string.Join(", ", HitsBy.Select(kv => $"{kv.Key} {kv.Value} 回"))}");
        if (HitsOn.Count > 0) sb.Append($" || 敵の攻撃を受けた: {string.Join(", ", HitsOn.Select(kv => $"{kv.Key} {kv.Value} 回"))}");
        HitsBy.Clear();
        HitsOn.Clear();
        PartyManager.Log?.Info(sb.ToString());
    }

    private static ActionAnimController Anim(PlayerController p)
    {
        try { return p.GetComponentInChildren<ActionAnimController>(true); }
        catch { return null; }
    }

    private static string AnimState(PlayerController p)
    {
        var a = Anim(p);
        if (a == null) return "(アニメ部品なし)";
        try
        {
            string graph = "?";
            try
            {
                var anim = a.m_anim;
                var animator = anim?.Animator;
                graph = $"準備={anim?.IsPlayableInitialized} 再生中={(anim != null && anim.IsPlayableInitialized ? anim.Playable.IsGraphPlaying : false)} " +
                        $"Animator 有効={animator?.enabled} 速さ={animator?.speed:0.##} カリング={animator?.cullingMode}";
            }
            catch (Exception e) { graph = $"(Animancer を読めません: {e.Message})"; }
            return $"[アニメ 有効={a.enabled} 速さ={a.GetAnimSpeed():0.##} 係数={a.m_animSpeedFactor:0.##} 止め={a.m_freezeAnimCnt} " +
                   $"'{a.GetCurAnimName()}' 進み={a.GetCurAnimNormalizedTime():0.00} {graph}]";
        }
        catch (Exception e) { return $"(アニメを読めません: {e.Message})"; }
    }

    /// <summary>置いたキャラのアニメの再生を再開する (Animator を有効に、常に動かす、グラフの一時停止を解く)</summary>
    public static void ResumeAnim()
    {
        foreach (var p in Fielded)
        {
            var a = Anim(p);
            if (a == null) continue;
            try
            {
                var anim = a.m_anim;
                var animator = anim?.Animator;
                if (animator != null)
                {
                    animator.enabled = true;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    animator.speed = 1f;
                }
                if (anim != null)
                {
                    if (!anim.IsPlayableInitialized) anim.InitializePlayable();
                    anim.Playable.UnpauseGraph();
                    anim.Playable.Speed = 1f;
                }
                PartyManager.Log?.Info($"Party Lab 場: {PartyManager.Name(p)} のアニメの再生を再開 → {AnimState(p)}");
            }
            catch (Exception e) { PartyManager.Log?.Info($"Party Lab 場: 再開できません: {e.Message}"); }
        }
    }

    /// <summary>置いたキャラのアニメの速さを 1 にする (止まっているか確かめる用)</summary>
    public static void ResetAnimSpeed()
    {
        foreach (var p in Fielded)
        {
            var a = Anim(p);
            if (a == null) continue;
            try
            {
                a.SetAnimSpeed(1f);
                PartyManager.Log?.Info($"Party Lab 場: {PartyManager.Name(p)} のアニメの速さを 1 に → {AnimState(p)}");
            }
            catch (Exception e) { PartyManager.Log?.Info($"Party Lab 場: 速さを変えられません: {e.Message}"); }
        }
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
        if (FieldSwap.InSwap) return false; // 置いたキャラを「今のプレイヤー」に差し替えている間は、キー入力に反応しない
        if (!FieldProbe.BlockOthersInput || FieldProbe.Fielded.Count == 0) return true;
        var cur = PartyManager.Current;
        return cur == null || __instance.Pointer == cur.Pointer; // 操作中でないキャラはキーに反応しない
    }
}

[HarmonyPatch(typeof(EnemyController), nameof(EnemyController.GetHit))]
internal static class FieldProbeEnemyHitPatch
{
    private static void Postfix(EnemyController __instance, Transform atker)
    {
        try
        {
            FieldProbe.OnEnemyHit(atker);
            // 操作キャラが殴った敵を、オートの仲間の狙いにする
            var p = atker != null ? atker.GetComponentInParent<PlayerController>(true) : null;
            if (p != null && p.Pointer == PartyManager.Current?.Pointer && !FieldSwap.InSwap) FieldAi.PlayerTarget = __instance;
        }
        catch { }
    }
}
