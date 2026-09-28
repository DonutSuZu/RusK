using System.Linq;
using RusK.API;
using UnityEngine;

namespace RusK.Mods.Party;

/// <summary>
/// 仲間の選択画面。リーダー (拠点で選んだキャラ) に加えて、一緒に戦う仲間を 2 人まで選ぶ。
/// 戦闘中は編成を固定し、実際のパーティを表示する
/// </summary>
public sealed class PartySelectWindow : RuskWindow
{
    public PartySelectWindow() : base("select", "パーティ編成", 400f, 480f)
    {
        MinWidth = 320f;
    }

    public override void Draw(WindowGui gui)
    {
        if (PartyManager.InFight) DrawInFight(gui);
        else DrawEdit(gui);

        gui.Space(6f);
        if (gui.Button("閉じる", accent: true)) Visible = false;
    }

    /// <summary>戦闘中: 今のパーティ (変更不可)</summary>
    private static void DrawInFight(WindowGui gui)
    {
        gui.Label("戦闘中は編成を変更できません。拠点に戻ると変更できます。", RuskStyle.TextDim, small: true);

        var cur = PartyManager.Current;
        var leader = PartyManager.Leader;
        var members = PartyManager.Members.Where(m => m != null).ToList();
        gui.Header("今のパーティ", $"{members.Count} 人");
        foreach (var m in members)
        {
            bool isCur = cur != null && m.Pointer == cur.Pointer;
            string role = leader != null && m.Pointer == leader.Pointer ? "リーダー" : "仲間";
            string state = PartyManager.IsDown(m) ? "戦闘不能" : isCur ? "操作中" : "控え";
            gui.Selectable(PartyManager.Name(m), isCur, $"{role}  {state}",
                PartyManager.IsDown(m) ? RuskStyle.TextDim : (Color?)null);
        }

        // 設定したのに参加していない仲間 (リーダーと同じキャラなど)
        foreach (var id in PartyManager.Companions)
        {
            if (members.Any(m => PartyManager.Same(PartyManager.Id(m), id))) continue;
            gui.Selectable(PartyManager.DisplayName(PartyManager.FindCharacter(id)), false, "不参加", RuskStyle.TextDim);
        }
    }

    /// <summary>戦闘外: 仲間の選択</summary>
    private static void DrawEdit(WindowGui gui)
    {
        var leader = PartyManager.Leader;
        double leaderId = leader != null ? PartyManager.Id(leader) : -1;

        gui.Label($"リーダー (拠点で選んだキャラ) に加えて、仲間を {PartyManager.MaxCompanions} 人まで選べます。" +
                  "仲間は戦闘ステージに入ると控えに用意されます。", RuskStyle.TextDim, small: true);

        gui.Header("編成", $"{PartyManager.Companions.Count} / {PartyManager.MaxCompanions}");
        gui.Label($"リーダー: {(leader != null ? PartyManager.Name(leader) : "(ゲーム中に表示)")}");
        for (int i = 0; i < PartyManager.MaxCompanions; i++)
        {
            string name = i < PartyManager.Companions.Count
                ? PartyManager.DisplayName(PartyManager.FindCharacter(PartyManager.Companions[i]))
                : "—";
            gui.Label($"仲間 {i + 1}: {name}");
        }

        gui.Space(6f);
        gui.Header("キャラ", "クリックで仲間に入れる / 外す");
        var list = PartyManager.Characters();
        if (list.Count == 0) gui.Label("キャラ一覧を取得できません (ゲームに入ってから開いてください)", RuskStyle.TextDim);

        foreach (var c in list)
        {
            bool isLeader = PartyManager.Same(c.id, leaderId);
            int slot = PartyManager.Companions.FindIndex(id => PartyManager.Same(id, c.id));
            string right = isLeader ? "リーダー" : slot >= 0 ? $"仲間 {slot + 1}" : null;

            // リーダーは仲間に選べない
            if (gui.Selectable(PartyManager.DisplayName(c), slot >= 0 || isLeader, right,
                    isLeader ? RuskStyle.TextDim : (Color?)null) && !isLeader)
                PartyManager.ToggleCompanion(c.id);
        }

        gui.Space(6f);
        if (gui.Button("仲間を全員外す", enabled: PartyManager.Companions.Count > 0))
            foreach (var id in PartyManager.Companions.ToArray()) PartyManager.ToggleCompanion(id);
    }
}
