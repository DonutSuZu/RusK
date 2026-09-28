using System;
using System.Diagnostics;
using RusK.API;
using Module = RusK.API.Module;

namespace RusK.Mods.Party;

/// <summary>ON で Party Lab (デバッグ用ウィンドウ) を開く</summary>
public sealed class PartyLabModule : Module
{
    private readonly PartyLabWindow _window;

    public PartyLabModule(IModContext context) : base("PartyLab", "Party", "パーティのデバッグ用ウィンドウを開く")
    {
        _window = new PartyLabWindow();
        context.RegisterWindow(_window);
        _window.VisibleChanged += w =>
        {
            Enabled = w.Visible;
            PartyManager.Verbose = w.Visible; // 開いている間は切り替えの詳しいログを出す
        };
    }

    public override bool VisibleInArrayList => false;
    public override void OnEnable() => _window.Visible = true;
    public override void OnDisable() => _window.Visible = false;
}

/// <summary>Party Lab の画面。控えの手動作成・切り替え・状態の記録</summary>
internal sealed class PartyLabWindow : RuskWindow
{
    public PartyLabWindow() : base("lab", "Party Lab (デバッグ)", 480f, 560f)
    {
        MinWidth = 380f;
    }

    public override void Draw(WindowGui gui)
    {
        PartyManager.Refresh();
        var cur = PartyManager.Current;

        gui.Label("控えの手動作成や切り替えを試す画面です。開いている間は詳しいログが記録されます。",
            RuskStyle.TextDim, small: true);
        gui.Header("今のプレイヤー", PartyManager.Name(cur));
        gui.Label($"戦闘ステージ: {(PartyManager.InFight ? "はい" : "いいえ")}  クールタイム残り {PartyManager.CooldownRemaining:0.0} 秒",
            RuskStyle.TextDim, small: true);
        if (gui.Button("状態をログに出す", accent: true)) PartyManager.LogState("手動");

        gui.Space(6f);
        gui.Header("パーティ", $"{PartyManager.Members.Count} 人");
        foreach (var m in PartyManager.Members.ToArray())
        {
            if (m == null) continue;
            bool isCur = cur != null && m.Pointer == cur.Pointer;
            string hp = $"HP {SafeHp(m)}";
            gui.BeginRow(4f, 1.3f, 1.3f);
            gui.Selectable(PartyManager.Name(m), isCur, isCur ? $"操作中  {hp}" : hp);
            if (gui.Button("Switch", enabled: !isCur, accent: true)) PartyManager.Switch(m, ignoreCooldown: true);
            if (gui.Button("Despawn", enabled: !isCur)) PartyManager.Despawn(m);
        }

        gui.Space(6f);
        gui.Header("キャラを作る");
        var list = PartyManager.Characters();
        if (list.Count == 0) gui.Label("キャラ一覧を取得できません", RuskStyle.TextDim);
        foreach (var c in list)
        {
            gui.BeginRow(4f, 1.3f, 2f);
            gui.Label($"{PartyManager.DisplayName(c)}  (id {c.id:0})");
            if (gui.Button("Spawn", accent: true)) PartyManager.Spawn(c);
            if (gui.Button("ChangeCharacter")) GameChangeCharacter(c.id);
        }
    }

    /// <summary>比較用: ゲーム本来のキャラ変更</summary>
    private static void GameChangeCharacter(double id)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            GameUtil.Instance.ChangeCharacter(id);
            PartyManager.Log?.Info($"Party Lab: ChangeCharacter({id}) {sw.ElapsedMilliseconds}ms");
        }
        catch (Exception e)
        {
            PartyManager.Log?.Error($"Party Lab: ChangeCharacter 失敗: {e}");
        }
        PartyManager.LogState("ChangeCharacter の後");
    }

    private static string SafeHp(PlayerController p)
    {
        try { return $"{p.GetCurHp():0}/{p.GetMaxHp():0}"; }
        catch { return "?"; }
    }
}
