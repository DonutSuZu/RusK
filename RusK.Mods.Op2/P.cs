using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RusK.API;
using UnityEngine;

namespace RusK.Mods.Op2;

/// <summary>
/// Party Mod の入口 (RusK.Mods.Party.PartyBridge、版 2 以上) をリフレクションで使う窓口。
/// Mod は 1 つずつ別の AssemblyLoadContext で読み込まれ、互いの DLL を直接参照できないため。
/// Op2Entry.Attach (Party から呼ばれる) で Bind する
/// </summary>
internal static class P
{
    private const string AssemblyName = "RuskParty";
    private const string BridgeType = "RusK.Mods.Party.PartyBridge";
    public const int NeededVersion = 2;

    public static IRuskLogger Log;
    public static Type Bridge { get; private set; }

    private static Func<List<PlayerController>> _members;
    private static Func<PlayerController> _current;
    private static Func<PlayerController, double> _id;
    private static Func<bool> _inFight, _onField;
    private static Func<double, MotionManager> _findCharacter;
    private static Func<PlayerController, bool> _isDown;
    private static Func<PlayerController, string> _name;
    private static Func<PlayerController, Texture> _portrait;
    private static Action<PlayerController> _rebindCamera, _refreshHud;
    private static Func<PlayerController, bool> _switchTo;
    private static PropertyInfo _nextKey, _prevKey;
    private static Func<int, Hotkey> _fieldSkillKey, _fieldSwitchKey; // Party 1.5.0 から (無ければ 1〜3 / F1〜F3)

    public static bool Bound => Bridge != null;

    public static bool Bind()
    {
        if (Bridge != null) return true;
        var asm = AppDomain.CurrentDomain.GetAssemblies().LastOrDefault(a => a.GetName().Name == AssemblyName);
        var t = asm?.GetType(BridgeType);
        if (t == null) return false;
        const BindingFlags st = BindingFlags.Public | BindingFlags.Static;
        var version = t.GetField("Version", st)?.GetValue(null);
        if (version is not int v || v < NeededVersion)
        {
            Log?.Warning($"Op.2: Party の入口の版が古い ({version})。Party Mod を更新してください");
            return false;
        }

        T Method<T>(string name, params Type[] args) where T : Delegate =>
            (T)Delegate.CreateDelegate(typeof(T), t.GetMethod(name, st, null, args, null)
                                                  ?? throw new MissingMethodException(BridgeType, name));
        _members = Method<Func<List<PlayerController>>>("Members");
        _current = Method<Func<PlayerController>>("Current");
        _id = Method<Func<PlayerController, double>>("Id", typeof(PlayerController));
        _inFight = Method<Func<bool>>("InFight");
        _onField = Method<Func<bool>>("OnField");
        _findCharacter = Method<Func<double, MotionManager>>("FindCharacter", typeof(double));
        _isDown = Method<Func<PlayerController, bool>>("IsDown", typeof(PlayerController));
        _name = Method<Func<PlayerController, string>>("Name", typeof(PlayerController));
        _portrait = Method<Func<PlayerController, Texture>>("Portrait", typeof(PlayerController));
        _rebindCamera = Method<Action<PlayerController>>("RebindCamera", typeof(PlayerController));
        _refreshHud = Method<Action<PlayerController>>("RefreshHud", typeof(PlayerController));
        _switchTo = Method<Func<PlayerController, bool>>("SwitchTo", typeof(PlayerController));
        _nextKey = t.GetProperty("NextKey", st);
        _prevKey = t.GetProperty("PrevKey", st);
        if (t.GetMethod("FieldSkillKey", st, null, new[] { typeof(int) }, null) != null)
        {
            _fieldSkillKey = Method<Func<int, Hotkey>>("FieldSkillKey", typeof(int));
            _fieldSwitchKey = Method<Func<int, Hotkey>>("FieldSwitchKey", typeof(int));
        }
        Bridge = t;
        return true;
    }

    /// <summary>PartyBridge の静的フィールドに値を入れる (Op.2 の処理の登録)</summary>
    public static void Set(string field, object value)
    {
        var f = Bridge?.GetField(field, BindingFlags.Public | BindingFlags.Static);
        if (f == null) throw new MissingFieldException(BridgeType, field);
        f.SetValue(null, value);
    }

    public static List<PlayerController> Members => Bound ? _members() ?? new() : new();
    public static PlayerController Current => Bound ? _current() : GameUtil.Instance?.GetPlayer();
    public static double Id(PlayerController p) => Bound && p != null ? _id(p) : -1;
    public static bool InFight => Bound && _inFight();

    /// <summary>戦闘ステージにいて、ロード中・ポーズ中・ウィンドウ表示中でなく、ゲームの HP バーが出ている (HUD を出してよい)</summary>
    public static bool OnField => Bound && _onField();
    public static MotionManager FindCharacter(double id) => Bound ? _findCharacter(id) : null;
    public static bool IsDown(PlayerController p) => Bound && _isDown(p);
    public static string Name(PlayerController p) => Bound ? _name(p) : p?.name ?? "";
    public static Texture Portrait(PlayerController p) => Bound ? _portrait(p) : null;
    public static void RebindCamera(PlayerController p) { if (Bound) _rebindCamera(p); }
    public static void RefreshHud(PlayerController p) { if (Bound) _refreshHud(p); }
    public static bool SwitchTo(PlayerController p) => Bound && _switchTo(p);
    public static Hotkey NextKey => Bound ? (Hotkey)_nextKey.GetValue(null) : new Hotkey(KeyCode.C);
    public static Hotkey PrevKey => Bound ? (Hotkey)_prevKey.GetValue(null) : new Hotkey(KeyCode.Z);

    /// <summary>slot 番 (0〜2) のキャラのスキルのキー (Party の設定。古い Party なら 1〜3)</summary>
    public static Hotkey FieldSkillKey(int slot) => _fieldSkillKey?.Invoke(slot) ?? new Hotkey(KeyCode.Alpha1 + slot);

    /// <summary>slot 番 (0〜2) のキャラに切り替えるキー (Party の設定。古い Party なら F1〜F3)</summary>
    public static Hotkey FieldSwitchKey(int slot) => _fieldSwitchKey?.Invoke(slot) ?? new Hotkey(KeyCode.F1 + slot);
}
