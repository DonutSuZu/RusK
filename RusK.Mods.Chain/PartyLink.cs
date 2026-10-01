using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RusK.API;
using UnityEngine;

namespace RusK.Mods.Chain;

/// <summary>
/// Party Mod の入口 (RusK.Mods.Party.PartyBridge) をリフレクションで探して使う。
/// Mod は 1 つずつ別の AssemblyLoadContext で読み込まれ、互いの DLL を直接参照できないため。
/// Party が入っていなければ Available が false になり、連携攻撃は何もしない
/// </summary>
internal static class PartyLink
{
    private const string AssemblyName = "RuskParty";
    private const string BridgeType = "RusK.Mods.Party.PartyBridge";
    private const int NeededVersion = 1;

    private static Type _bridge;
    private static float _nextTry;

    private static Func<List<PlayerController>> _members;
    private static Func<PlayerController, bool> _isDown;
    private static Func<PlayerController, bool> _switch;
    private static Func<bool> _onField;
    private static Func<PlayerController, string> _name;
    private static Func<PlayerController, Texture> _portrait;
    private static PropertyInfo _nextKey, _prevKey;
    private static FieldInfo _blockHits, _suppressKeys, _hudExtras;
    private static PropertyInfo _endfield; // 版 2 から (無ければ常に false)

    /// <summary>Party の入口が見つかったか (見つからなければ 2 秒ごとに探し直す)</summary>
    public static bool Available
    {
        get
        {
            if (_bridge != null) return true;
            if (Time.unscaledTime < _nextTry) return false;
            _nextTry = Time.unscaledTime + 2f;
            return Bind();
        }
    }

    private static bool Bind()
    {
        try
        {
            // 読み直された場合に備えて、最後に読み込まれたものを使う
            var asm = AppDomain.CurrentDomain.GetAssemblies().LastOrDefault(a => a.GetName().Name == AssemblyName);
            var t = asm?.GetType(BridgeType);
            if (t == null) return false;
            const BindingFlags st = BindingFlags.Public | BindingFlags.Static;
            var version = t.GetField("Version", st)?.GetValue(null);
            if (version is not int v || v < NeededVersion)
            {
                ChainMod.Log?.Warning($"Chain: Party の入口の版が古い ({version})。Party Mod を更新してください");
                return false;
            }

            T Method<T>(string name, params Type[] args) where T : Delegate =>
                (T)Delegate.CreateDelegate(typeof(T), t.GetMethod(name, st, null, args, null)
                                                      ?? throw new MissingMethodException(BridgeType, name));
            _members = Method<Func<List<PlayerController>>>("Members");
            _isDown = Method<Func<PlayerController, bool>>("IsDown", typeof(PlayerController));
            _switch = Method<Func<PlayerController, bool>>("Switch", typeof(PlayerController));
            _onField = Method<Func<bool>>("OnField");
            _name = Method<Func<PlayerController, string>>("Name", typeof(PlayerController));
            _portrait = Method<Func<PlayerController, Texture>>("Portrait", typeof(PlayerController));
            _nextKey = t.GetProperty("NextKey", st);
            _prevKey = t.GetProperty("PrevKey", st);
            _blockHits = t.GetField("BlockHits", st);
            _suppressKeys = t.GetField("SuppressSwitchKeys", st);
            _hudExtras = t.GetField("HudExtras", st);
            _endfield = t.GetProperty("EndfieldActive", st);
            _bridge = t;
            ChainMod.Log?.Info($"Chain: Party の入口を見つけました (版 {v})");
            return true;
        }
        catch (Exception e)
        {
            ChainMod.Log?.Warning($"Chain: Party の入口を使えません: {e.Message}");
            return false;
        }
    }

    public static List<PlayerController> Members() => Available ? _members() ?? new() : new();
    public static bool IsDown(PlayerController p) => Available && _isDown(p);
    public static bool Switch(PlayerController p) => Available && _switch(p);
    public static bool OnField() => Available && _onField();
    public static string Name(PlayerController p) => Available ? _name(p) : p?.name ?? "";
    public static Texture Portrait(PlayerController p) => Available ? _portrait(p) : null;
    public static Hotkey NextKey => Available ? (Hotkey)_nextKey.GetValue(null) : new Hotkey(KeyCode.C);
    public static Hotkey PrevKey => Available ? (Hotkey)_prevKey.GetValue(null) : new Hotkey(KeyCode.Z);

    /// <summary>Party がエンドフィールドスタイルで動いているか (このときは連携攻撃をしない)</summary>
    public static bool EndfieldActive
    {
        get
        {
            try { return Available && _endfield != null && (bool)_endfield.GetValue(null); }
            catch { return false; }
        }
    }

    public static void SetBlockHits(bool on)
    {
        if (_bridge != null) _blockHits.SetValue(null, on);
    }

    public static void SetSuppressSwitchKeys(bool on)
    {
        if (_bridge != null) _suppressKeys.SetValue(null, on);
    }

    public static void SetHudExtras(Action<float, float, float> draw)
    {
        if (_bridge != null) _hudExtras.SetValue(null, draw);
    }
}
