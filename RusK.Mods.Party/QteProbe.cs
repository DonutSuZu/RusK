using System;
using RusK.API;
using UnityEngine;

namespace RusK.Mods.Party;

/// <summary>
/// 調査用 (Party Lab): 追加攻撃 (QTE) を好きなタイミングで撃てるか、切り替え直後に撃てるかを試す。
/// PlayerController.QTEAttack() は「追加攻撃の動作を探して ChangeMotion する」作り (MethodXrefScanCache で確認)。
/// 撃てる条件 (受付中の印 m_qteListen) を中で見ていそうなので、3 通りの撃ち方を比べる
/// </summary>
internal static class QteProbe
{
    public static readonly string[] Modes =
    {
        "A: QTEAttack() だけ",
        "B: SetQTEListen(true) → QTEAttack()",
        "C: 追加攻撃の動作へ直接 ChangeMotion",
        "D: 動作の一覧から QTE の動作を名前で探して ChangeMotion",
    };

    public static int Mode = 3;
    public static int SwitchDelayFrames = 2;

    public static readonly Hotkey FireKey = new(KeyCode.F6);
    public static readonly Hotkey SwitchFireKey = new(KeyCode.F7);

    private static PlayerController _pendingFire;
    private static int _fireFrame;
    private static PlayerController _check;
    private static float _checkAt;
    private static string _label;

    public static string LastResult = "(まだ試していません)";
    private static bool _announced;

    /// <summary>ウィンドウのボタン用: クリックの直後はメニュー操作中のことがあるので、少し待ってから撃つ</summary>
    public static void FireLater(float seconds)
    {
        _delayedFire = Time.unscaledTime + seconds;
    }

    private static float _delayedFire = -1f;

    /// <summary>Party Lab を開いている間、毎フレーム呼ぶ</summary>
    public static void Tick()
    {
        if (!_announced)
        {
            _announced = true;
            PartyManager.Log?.Info("Party Lab QTE: 試し撃ちの待ち受けを開始 (F6 / F7、またはウィンドウのボタン)");
        }
        if (RuskInput.WasPressed(FireKey)) Fire(PartyManager.Current, "F6");
        else if (RuskInput.WasPressed(SwitchFireKey)) SwitchAndFire();

        if (_delayedFire > 0f && Time.unscaledTime >= _delayedFire)
        {
            _delayedFire = -1f;
            Fire(PartyManager.Current, "ボタン");
        }

        if (_pendingFire != null && Time.frameCount >= _fireFrame)
        {
            var p = _pendingFire;
            _pendingFire = null;
            Fire(p, $"切り替えの {SwitchDelayFrames} フレーム後");
        }

        if (_check != null && Time.unscaledTime >= _checkAt)
        {
            var p = _check;
            _check = null;
            Report(p, _label + " → 0.25 秒後");
        }
    }

    public static void SwitchAndFire()
    {
        PartyManager.Refresh();
        var cur = PartyManager.Current;
        if (cur == null || PartyManager.Members.Count < 2)
        {
            LastResult = "控えがいません (戦闘ステージで仲間を用意してください)";
            PartyManager.Log?.Info("Party Lab QTE: " + LastResult);
            return;
        }
        int i = PartyManager.Members.FindIndex(m => m != null && m.Pointer == cur.Pointer);
        PlayerController next = null;
        for (int k = 1; k < PartyManager.Members.Count && next == null; k++)
        {
            var m = PartyManager.Members[(i + k) % PartyManager.Members.Count];
            if (m != null && m.Pointer != cur.Pointer && !PartyManager.IsDown(m)) next = m;
        }
        if (next == null || !PartyManager.Switch(next, ignoreCooldown: true, force: true))
        {
            LastResult = "切り替えに失敗";
            PartyManager.Log?.Info("Party Lab QTE: " + LastResult);
            return;
        }
        _pendingFire = next;
        _fireFrame = Time.frameCount + Mathf.Max(0, SwitchDelayFrames);
        if (SwitchDelayFrames <= 0)
        {
            _pendingFire = null;
            Fire(next, "切り替え直後 (同じフレーム)");
        }
    }

    public static void Fire(PlayerController p, string when)
    {
        if (p == null)
        {
            LastResult = "プレイヤーがいません";
            return;
        }
        Report(p, $"[{Modes[Mode]}] {when} 撃つ前");
        try
        {
            switch (Mode)
            {
                case 0:
                    p.QTEAttack();
                    break;
                case 1:
                    p.SetQTEListen(true);
                    p.QTEAttack();
                    break;
                case 3:
                    var name = FindQteMotion(p);
                    if (name == null)
                    {
                        PartyManager.Log?.Info("Party Lab QTE: 動作の一覧に QTE の動作がありません");
                        break;
                    }
                    try { p.SetQTEListen(false); } catch { } // 受付中の演出が残らないように
                    p.ChangeMotion(name, true, 0.05f, default);
                    break;
                default:
                    var motion = p.GetQTEAttack();
                    if (motion == null)
                    {
                        PartyManager.Log?.Info("Party Lab QTE: GetQTEAttack() が null (追加攻撃の動作がない)");
                        break;
                    }
                    p.ChangeMotion(motion.name, true, 0.1f, default);
                    break;
            }
        }
        catch (Exception e)
        {
            PartyManager.Log?.Warning($"Party Lab QTE: 撃てませんでした: {e}");
        }
        Report(p, $"[{Modes[Mode]}] {when} 撃った直後");
        _check = p;
        _checkAt = Time.unscaledTime + 0.25f;
        _label = $"[{Modes[Mode]}] {when}";
    }

    private static readonly System.Collections.Generic.HashSet<IntPtr> Listed = new();

    /// <summary>そのキャラの追加攻撃の動作名。"NormalAttack_QTE_" を優先し、無ければ "QTE" を含む動作</summary>
    public static string FindQteMotion(PlayerController p)
    {
        var all = new System.Collections.Generic.List<string>();
        try
        {
            var list = p.GetMotionList();
            for (int i = 0; list != null && i < list.Count; i++)
            {
                var n = list[i]?.name;
                if (!string.IsNullOrEmpty(n) && n.IndexOf("QTE", StringComparison.OrdinalIgnoreCase) >= 0) all.Add(n);
            }
        }
        catch { }
        if (Listed.Add(p.Pointer))
            PartyManager.Log?.Info($"Party Lab QTE: {PartyManager.Name(p)} の QTE の動作: {string.Join(", ", all)}");
        foreach (var n in all) if (n.StartsWith("NormalAttack_QTE")) return n;
        return all.Count > 0 ? all[0] : null;
    }

    private static void Report(PlayerController p, string label)
    {
        string text;
        try
        {
            text = $"{PartyManager.Name(p)} 受付中(m_qteListen)={p.m_qteListen} QTEAttackAble={p.QTEAttackAble()} " +
                   $"動作='{p.GetCurMotion()?.name}' 追加攻撃中={p.IsCurMotionQTE()} 追加攻撃の動作='{p.GetQTEAttack()?.name}'";
        }
        catch (Exception e)
        {
            text = $"(状態を読めません: {e.Message})";
        }
        PartyManager.Log?.Info($"Party Lab QTE: {label}: {text}");
        if (label.Contains("0.25 秒後")) LastResult = $"{label}: {text}";
    }
}
