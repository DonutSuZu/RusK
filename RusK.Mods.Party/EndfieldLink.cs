using System;
using System.Linq;
using System.Reflection;
using RusK.API;
using UnityEngine;

namespace RusK.Mods.Party;

/// <summary>
/// エンドフィールドスタイル (Party Op.2) とのつなぎ。
///
/// Op.2 はモジュールを登録しない (メニューに出さない) ので、毎フレームの処理と画面の描画は Party から呼ぶ。
/// Mod の読み込み順に左右されないよう、Party の側から Op.2 (RusK.Mods.Op2.Op2Entry.Attach) を探して呼び、
/// Op.2 は PartyBridge に自分の処理を登録する
/// </summary>
internal static class EndfieldLink
{
    private const string AssemblyName = "RuskPartyOp2";
    private const string EntryType = "RusK.Mods.Op2.Op2Entry";

    private static float _nextTry;
    private static bool _warned;
    private static bool _wasActive;
    private static float _faultAt = -999f;

    public static void Tick()
    {
        bool active = PartyBridge.EndfieldActive;
        if (_wasActive && !active)
        {
            // スタイルを戻した: フィールドのキャラを片付けてもらう
            try { PartyBridge.EndfieldStop?.Invoke(); }
            catch (Exception e) { Fault("後片付け", e); }
        }
        _wasActive = active;

        if (PartyModule.StyleValue != 1 || PartyBridge.EndfieldUpdate != null) return;
        if (Time.unscaledTime < _nextTry) return;
        _nextTry = Time.unscaledTime + 2f;
        if (Attach()) return;
        if (!_warned && Time.unscaledTime > 10f)
        {
            _warned = true;
            PartyManager.Ctx?.Notify(L.T("エンドフィールドスタイルには Party Op.2 が必要です (ゼンゼロのまま動きます)"), NotifyLevel.Warning);
        }
    }

    private static bool Attach()
    {
        try
        {
            var asm = AppDomain.CurrentDomain.GetAssemblies().LastOrDefault(a => a.GetName().Name == AssemblyName);
            var attach = asm?.GetType(EntryType)?.GetMethod("Attach", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
            if (attach == null) return false;
            attach.Invoke(null, null);
            bool ok = PartyBridge.EndfieldUpdate != null;
            PartyManager.Log?.Info($"Party: エンドフィールドスタイル (Op.2) と{(ok ? "つながりました" : "つながりませんでした")}");
            return ok;
        }
        catch (Exception e)
        {
            PartyManager.Log?.Warning($"Party: Op.2 とつなげません: {e.InnerException?.Message ?? e.Message}");
            return false;
        }
    }

    /// <summary>Op.2 の処理でエラー (ログが埋まらないよう 5 秒に 1 回)</summary>
    public static void Fault(string where, Exception e)
    {
        if (Time.unscaledTime - _faultAt < 5f) return;
        _faultAt = Time.unscaledTime;
        PartyManager.Log?.Warning($"Party: Op.2 の{where}でエラー: {e}");
    }
}
