using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace RusK.Mods.Party;

/// <summary>
/// 戦闘中に獲得したパッシブバフをパーティ内で共有する。
///
/// ゲームの仕組み (MethodXrefScanCache で確認):
///   - バフの獲得は PlayerController.AddBuffInSetting(Buff) (選択画面・単体バフ画面・リトライの復元が全部ここを通る)。
///     渡したバフは中でコピーされるので、別のキャラのバフをそのまま渡してよい
///   - 取り消し (単体バフの「あきらめる」、バフ変換) は PlayerController.RemoveBuff(buffId)
///   - パッシブかどうかは GameUtil.IsBuffPassive (特殊バフ・キャラ固有スキルを除く)
///
/// バフを得たり失ったりするのは操作中のキャラだけなので、操作中のキャラの増減を控えに写す。
/// 初めて見るキャラがいるとき (控えの作成・機能を有効にした直後) は、全員のパッシブバフを持ち寄って揃える。
/// ウィンドウ表示中は写さない (単体バフ画面は、あきらめると付けたバフを外すため、結果が出てから写す)
/// </summary>
internal static class BuffShare
{
    public static bool Enabled = true;

    private const float Interval = 0.5f;
    private static float _next;

    /// <summary>キャラごとの、前回そろえた時点のパッシブバフ (buffId)</summary>
    private static readonly Dictionary<IntPtr, HashSet<double>> Snapshot = new();

    private static readonly HashSet<string> Warned = new();

    public static void Reset() => Snapshot.Clear();

    /// <summary>毎フレーム呼ぶ (中で 0.5 秒に 1 回に間引く)</summary>
    public static void Tick()
    {
        if (!Enabled)
        {
            Snapshot.Clear();
            return;
        }
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + Interval;
        if (!PartyHud.OnField()) return;

        var util = GameUtil.Instance;
        var cur = PartyManager.Current;
        if (util == null || cur == null) return;
        var members = PartyManager.Members.Where(m => m != null).ToList();
        if (members.Count < 2 || !members.Any(m => m.Pointer == cur.Pointer)) return;

        // 片付けられたキャラの記録を捨てる
        foreach (var key in Snapshot.Keys.Where(k => members.All(m => m.Pointer != k)).ToList()) Snapshot.Remove(key);

        try
        {
            if (members.Any(m => !Snapshot.ContainsKey(m.Pointer))) Merge(util, members);
            else Propagate(util, cur, members);
        }
        catch (Exception e)
        {
            Warn("tick", $"Party: バフ共有に失敗: {e.Message}");
        }

        foreach (var m in members) Snapshot[m.Pointer] = PassiveIds(util, m);
    }

    /// <summary>全員のパッシブバフを持ち寄って、全員に足りないものを付ける</summary>
    private static void Merge(GameUtil util, List<PlayerController> members)
    {
        var pool = new Dictionary<double, (Buff buff, PlayerController owner)>();
        foreach (var m in members)
            foreach (var b in Passive(util, m))
                if (!pool.ContainsKey(b.buffId)) pool[b.buffId] = (b, m);

        int added = 0;
        foreach (var m in members)
        {
            var have = PassiveIds(util, m);
            foreach (var (id, (buff, owner)) in pool)
                if (owner.Pointer != m.Pointer && !have.Contains(id) && Give(util, m, buff)) added++;
        }
        if (added > 0)
            PartyManager.Log?.Info($"Party: バフ共有 — パーティのパッシブバフをそろえました ({added} 個付けた)");
    }

    /// <summary>操作中のキャラが前回から得た / 失ったパッシブバフを、控えにも付ける / 外す</summary>
    private static void Propagate(GameUtil util, PlayerController cur, List<PlayerController> members)
    {
        var before = Snapshot[cur.Pointer];
        var now = Passive(util, cur).ToList();
        var nowIds = new HashSet<double>(now.Select(b => b.buffId));
        var gained = now.Where(b => !before.Contains(b.buffId)).ToList();
        var lost = before.Where(id => !nowIds.Contains(id)).ToList();
        if (gained.Count == 0 && lost.Count == 0) return;

        foreach (var m in members)
        {
            if (m.Pointer == cur.Pointer) continue;
            var have = PassiveIds(util, m);
            foreach (var b in gained)
                if (!have.Contains(b.buffId)) Give(util, m, b);
            foreach (var id in lost)
            {
                if (!have.Contains(id)) continue;
                try
                {
                    m.RemoveBuff(id);
                    PartyManager.Log?.Info($"Party: バフ共有 — {PartyManager.Name(m)} から {BuffName(util, id)} を外した");
                }
                catch (Exception e) { Warn("remove" + id, $"Party: {BuffName(util, id)} を外せません: {e.Message}"); }
            }
        }
    }

    private static bool Give(GameUtil util, PlayerController to, Buff buff)
    {
        if (!CanUse(util, to, buff)) return false;
        try
        {
            to.AddBuffInSetting(buff);
            PartyManager.Log?.Info($"Party: バフ共有 — {PartyManager.Name(to)} に {BuffName(util, buff.buffId)} を付けた");
            return true;
        }
        catch (Exception e)
        {
            Warn("give" + buff.buffId, $"Party: {BuffName(util, buff.buffId)} を {PartyManager.Name(to)} に付けられません: {e.Message}");
            return false;
        }
    }

    /// <summary>特定のキャラ専用のバフは、そのキャラ以外に付けない</summary>
    private static bool CanUse(GameUtil util, PlayerController to, Buff buff)
    {
        try
        {
            var item = util.GetBuffItemFromContainer(buff.buffId);
            if (item == null || item.bindCharacterId <= 0) return true;
            return PartyManager.Same(item.bindCharacterId, PartyManager.Id(to));
        }
        catch { return true; }
    }

    private static IEnumerable<Buff> Passive(GameUtil util, PlayerController p)
    {
        var result = new List<Buff>();
        try
        {
            var list = p.GetPlayerBuff(true); // 装備のバフは除く (キャラごとの装備に付いたもの)
            if (list == null) return result;
            for (int i = 0; i < list.Count; i++)
            {
                var b = list[i];
                if (b != null && util.IsBuffPassive(b)) result.Add(b);
            }
        }
        catch (Exception e) { Warn("list", $"Party: バフの一覧を取れません: {e.Message}"); }
        return result;
    }

    private static HashSet<double> PassiveIds(GameUtil util, PlayerController p) =>
        new(Passive(util, p).Select(b => b.buffId));

    private static string BuffName(GameUtil util, double id)
    {
        try
        {
            var name = GameUtil.GetBuffName(id);
            if (!string.IsNullOrEmpty(name)) return $"{name} ({id:0})";
        }
        catch { }
        return $"#{id:0}";
    }

    private static void Warn(string key, string message)
    {
        if (Warned.Add(key)) PartyManager.Log?.Warning(message);
    }
}
