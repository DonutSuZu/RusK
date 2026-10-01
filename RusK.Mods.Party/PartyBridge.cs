using System;
using System.Collections.Generic;
using System.Linq;
using RusK.API;
using UnityEngine;

namespace RusK.Mods.Party;

/// <summary>
/// ほかの Mod (Chain Attack・Party Op.2 など) から Party を使うための入口。
///
/// Mod は 1 つずつ別の AssemblyLoadContext で読み込まれ、互いの DLL を直接参照できないので、
/// 相手側はこのクラスをリフレクションで探して呼ぶ (型の名前・メソッドの形を変えるときは相手側も直すこと)。
/// 引数・戻り値は、ゲームの型 (interop)・UnityEngine・RusK.API・BCL など全 Mod で共有される型だけにする
/// </summary>
public static class PartyBridge
{
    /// <summary>入口の版。互換性のない変更をしたら上げる (2: バトルスタイルとエンドフィールド用の入口)</summary>
    public const int Version = 3; // 3: 編成 (Party Formation 用)

    /// <summary>パーティのキャラ (操作中のキャラと控え)</summary>
    public static List<PlayerController> Members()
    {
        PartyManager.Refresh();
        return PartyManager.Members.Where(m => m != null).ToList();
    }

    /// <summary>操作中のキャラ</summary>
    public static PlayerController Current() => PartyManager.Current;

    /// <summary>キャラの ID (控えは作った時点の ID)</summary>
    public static double Id(PlayerController p) => PartyManager.Id(p);

    /// <summary>戦闘ステージか</summary>
    public static bool InFight() => PartyManager.InFight;

    /// <summary>キャラの設定 (MotionManager)</summary>
    public static MotionManager FindCharacter(double id) => PartyManager.FindCharacter(id);

    /// <summary>戦闘不能 (このランでは交代できない) か</summary>
    public static bool IsDown(PlayerController p) => PartyManager.IsDown(p);

    /// <summary>クールタイムや安全策を無視して切り替える (後処理も Party がする)</summary>
    public static bool Switch(PlayerController next) => PartyManager.Switch(next, ignoreCooldown: true, force: true);

    /// <summary>
    /// 切り替えキーと同じように next に切り替える (クールタイム・安全策・ジャスト切り替えの判定つき)。
    /// エンドフィールドスタイルでは、その場で切り替え (キャラは動かさず操作だけ移す)
    /// </summary>
    public static bool SwitchTo(PlayerController next) => PartyManager.SwitchByKey(next);

    /// <summary>戦闘ステージにいて、ロード中でもウィンドウ表示中でもない</summary>
    public static bool OnField() => PartyHud.OnField();

    public static string Name(PlayerController p) => PartyManager.Name(p);

    /// <summary>キャラの顔アイコン (正方形)。無ければ null</summary>
    public static Texture Portrait(PlayerController p) =>
        p == null ? null : PartyHud.Portrait(PartyManager.FindCharacter(PartyManager.Id(p)));

    /// <summary>カメラの追従・HUD (HP・スキル UI など) を p に付け替える</summary>
    public static void RebindCamera(PlayerController p) => PartyManager.RebindCamera(p);

    public static void RefreshHud(PlayerController p) => PartyManager.RefreshHud(p);

    /// <summary>Party の「次へ」「前へ」のキー</summary>
    public static Hotkey NextKey => PartyModule.NextKeyValue;
    public static Hotkey PrevKey => PartyModule.PrevKeyValue;

    /// <summary>true の間、操作中のキャラは攻撃もダメージも受けない (切り替えガードも働かない)</summary>
    public static bool BlockHits;

    /// <summary>true の間、Party の切り替えキーで切り替えない (ほかの Mod がそのキーを使う)</summary>
    public static bool SuppressSwitchKeys;

    /// <summary>パーティ HUD の下に描き足すもの (x, y, 拡大率)。HUD の透明度は GUI.color に掛かっている</summary>
    public static Action<float, float, float> HudExtras;

    // ------------------------------------------------------------------ バトルスタイル (版 2)

    /// <summary>Party の設定のバトルスタイル (0: ゼンゼロ、1: エンドフィールド)</summary>
    public static int BattleStyle => PartyModule.StyleValue;

    /// <summary>エンドフィールドスタイルが動いているか (設定がエンドフィールドで、Op.2 が入口を登録している)</summary>
    public static bool EndfieldActive => PartyModule.StyleValue == 1 && EndfieldUpdate != null;

    /// <summary>RusK UI のボタン HUD を隠すか (エンドフィールドスタイルでは Op.2 のスキルボタンを出す)</summary>
    public static bool HideButtonHud => EndfieldActive;

    /// <summary>Op.2 が登録する: 毎フレームの処理 / 画面の描画 / スタイルをやめたときの後片付け</summary>
    public static Action EndfieldUpdate, EndfieldGui, EndfieldStop;

    /// <summary>Op.2 が登録する: 操作するキャラが変わった (前, 新しい)</summary>
    public static Action<PlayerController, PlayerController> ControlSwitched;

    /// <summary>Op.2 が登録する: このキャラは攻撃もダメージも受けないか (フィールドにいる操作していないキャラ)</summary>
    public static Func<PlayerController, bool> Shielded;

    // ------------------------------------------------------------------ 編成 (版 3、Party Formation 用)

    /// <summary>仲間の最大人数 (リーダーを除く)</summary>
    public static int MaxCompanions() => PartyManager.MaxCompanions;

    /// <summary>仲間に選んだキャラの ID (並び順)</summary>
    public static List<double> Companions() => PartyManager.Companions.ToList();

    /// <summary>仲間の slot 番目 (0 から) を id のキャラにする。戦闘中・リーダーと同じキャラなら false</summary>
    public static bool SetCompanion(int slot, double id) => PartyManager.SetCompanion(slot, id);

    public static void RemoveCompanion(double id) => PartyManager.RemoveCompanion(id);

    /// <summary>リーダー (戦闘中は最初に操作していたキャラ、戦闘外は今のキャラ)</summary>
    public static PlayerController Leader() => PartyManager.Leader;

    /// <summary>選べるキャラ (解放済みで、戦闘用のプレハブがあるもの)</summary>
    public static List<MotionManager> Characters() => PartyManager.Characters();

    public static string DisplayName(MotionManager mm) => PartyManager.DisplayName(mm);

    /// <summary>キャラの顔アイコン (正方形) とテーマカラー</summary>
    public static Texture PortraitOf(MotionManager mm) => PartyHud.Portrait(mm);

    public static Color ThemeOf(MotionManager mm) => PartyHud.ThemeColor(mm);

    /// <summary>バトルスタイルを変える (0: ゼンゼロ、1: エンドフィールド)。Party の設定にも保存される</summary>
    public static void SetBattleStyle(int style) => PartyModule.Instance?.SetStyle(style);

    /// <summary>Party Op.2 (エンドフィールドスタイル) が入っているか</summary>
    public static bool EndfieldInstalled() =>
        EndfieldUpdate != null || AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "RuskPartyOp2");

    internal static bool IsShielded(PlayerController p)
    {
        try { return p != null && Shielded != null && Shielded(p); }
        catch { return false; }
    }
}
