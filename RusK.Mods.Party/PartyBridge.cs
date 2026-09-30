using System.Collections.Generic;
using System.Linq;
using RusK.API;
using UnityEngine;

namespace RusK.Mods.Party;

/// <summary>
/// ほかの Mod (Chain Attack など) から Party を使うための入口。
///
/// Mod は 1 つずつ別の AssemblyLoadContext で読み込まれ、互いの DLL を直接参照できないので、
/// 相手側はこのクラスをリフレクションで探して呼ぶ (型の名前・メソッドの形を変えるときは相手側も直すこと)。
/// 引数・戻り値は、ゲームの型 (interop)・UnityEngine・RusK.API・BCL など全 Mod で共有される型だけにする
/// </summary>
public static class PartyBridge
{
    /// <summary>入口の版。互換性のない変更をしたら上げる</summary>
    public const int Version = 1;

    /// <summary>パーティのキャラ (操作中のキャラと控え)</summary>
    public static List<PlayerController> Members()
    {
        PartyManager.Refresh();
        return PartyManager.Members.Where(m => m != null).ToList();
    }

    /// <summary>戦闘不能 (このランでは交代できない) か</summary>
    public static bool IsDown(PlayerController p) => PartyManager.IsDown(p);

    /// <summary>クールタイムや安全策を無視して切り替える (後処理も Party がする)</summary>
    public static bool Switch(PlayerController next) => PartyManager.Switch(next, ignoreCooldown: true, force: true);

    /// <summary>戦闘ステージにいて、ロード中でもウィンドウ表示中でもない</summary>
    public static bool OnField() => PartyHud.OnField();

    public static string Name(PlayerController p) => PartyManager.Name(p);

    /// <summary>キャラの顔アイコン (正方形)。無ければ null</summary>
    public static Texture Portrait(PlayerController p) =>
        p == null ? null : PartyHud.Portrait(PartyManager.FindCharacter(PartyManager.Id(p)));

    /// <summary>Party の「次へ」「前へ」のキー</summary>
    public static Hotkey NextKey => PartyModule.NextKeyValue;
    public static Hotkey PrevKey => PartyModule.PrevKeyValue;

    /// <summary>true の間、操作中のキャラは攻撃もダメージも受けない (切り替えガードも働かない)</summary>
    public static bool BlockHits;

    /// <summary>true の間、Party の切り替えキーで切り替えない (ほかの Mod がそのキーを使う)</summary>
    public static bool SuppressSwitchKeys;

    /// <summary>パーティ HUD の下に描き足すもの (x, y, 拡大率)。HUD の透明度は GUI.color に掛かっている</summary>
    public static System.Action<float, float, float> HudExtras;
}
