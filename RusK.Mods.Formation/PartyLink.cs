using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace RusK.Mods.Formation;

/// <summary>
/// Party Mod の入口 (RusK.Mods.Party.PartyBridge、版 3 以上) をリフレクションで使う窓口。
/// Mod は 1 つずつ別の AssemblyLoadContext で読み込まれ、互いの DLL を直接参照できないため
/// </summary>
internal static class PartyLink
{
    private const string AssemblyName = "RuskParty";
    private const string BridgeType = "RusK.Mods.Party.PartyBridge";
    private const int NeededVersion = 3;

    private static Type _bridge;
    private static float _nextTry;
    public static string Problem;

    private static Func<int> _maxCompanions, _battleStyle;
    private static Func<List<double>> _companions;
    private static Func<int, double, bool> _setCompanion;
    private static Action<double> _removeCompanion;
    private static Func<PlayerController> _leader, _current;
    private static Func<List<MotionManager>> _characters;
    private static Func<List<PlayerController>> _members;
    private static Func<MotionManager, string> _displayName;
    private static Func<MotionManager, Texture> _portraitOf;
    private static Func<MotionManager, Color> _themeOf;
    private static Action<int> _setBattleStyle;
    private static Func<bool> _endfieldInstalled, _inFight;
    private static Func<PlayerController, double> _id;
    private static Func<PlayerController, bool> _isDown;
    private static Func<double, MotionManager> _findCharacter;

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
            var asm = AppDomain.CurrentDomain.GetAssemblies().LastOrDefault(a => a.GetName().Name == AssemblyName);
            var t = asm?.GetType(BridgeType);
            if (t == null) { Problem = L.T("Party Mod が見つかりません"); return false; }
            const BindingFlags st = BindingFlags.Public | BindingFlags.Static;
            var version = t.GetField("Version", st)?.GetValue(null);
            if (version is not int v || v < NeededVersion) { Problem = L.T("Party Mod が古いです。更新してください"); return false; }

            T M<T>(string name, params Type[] args) where T : Delegate =>
                (T)Delegate.CreateDelegate(typeof(T), t.GetMethod(name, st, null, args, null)
                                                      ?? throw new MissingMethodException(BridgeType, name));
            _maxCompanions = M<Func<int>>("MaxCompanions");
            _companions = M<Func<List<double>>>("Companions");
            _setCompanion = M<Func<int, double, bool>>("SetCompanion", typeof(int), typeof(double));
            _removeCompanion = M<Action<double>>("RemoveCompanion", typeof(double));
            _leader = M<Func<PlayerController>>("Leader");
            _current = M<Func<PlayerController>>("Current");
            _characters = M<Func<List<MotionManager>>>("Characters");
            _members = M<Func<List<PlayerController>>>("Members");
            _displayName = M<Func<MotionManager, string>>("DisplayName", typeof(MotionManager));
            _portraitOf = M<Func<MotionManager, Texture>>("PortraitOf", typeof(MotionManager));
            _themeOf = M<Func<MotionManager, Color>>("ThemeOf", typeof(MotionManager));
            _setBattleStyle = M<Action<int>>("SetBattleStyle", typeof(int));
            _endfieldInstalled = M<Func<bool>>("EndfieldInstalled");
            _inFight = M<Func<bool>>("InFight");
            _id = M<Func<PlayerController, double>>("Id", typeof(PlayerController));
            _isDown = M<Func<PlayerController, bool>>("IsDown", typeof(PlayerController));
            _findCharacter = M<Func<double, MotionManager>>("FindCharacter", typeof(double));
            var style = t.GetProperty("BattleStyle", st);
            _battleStyle = () => (int)style.GetValue(null);
            _bridge = t;
            Problem = null;
            return true;
        }
        catch (Exception e)
        {
            Problem = L.T("Party Mod とつなげません: {0}", e.Message);
            return false;
        }
    }

    public static int MaxCompanions => Available ? _maxCompanions() : 2;
    public static List<double> Companions => Available ? _companions() : new();
    public static bool SetCompanion(int slot, double id) => Available && _setCompanion(slot, id);
    public static void RemoveCompanion(double id) { if (Available) _removeCompanion(id); }
    public static PlayerController Leader => Available ? _leader() : null;
    public static PlayerController Current => Available ? _current() : null;
    public static List<MotionManager> Characters => Available ? _characters() : new();
    public static List<PlayerController> Members => Available ? _members() : new();
    public static string DisplayName(MotionManager mm) => Available ? _displayName(mm) : mm?.name ?? "";
    public static Texture PortraitOf(MotionManager mm) => Available ? _portraitOf(mm) : null;
    public static Color ThemeOf(MotionManager mm) => Available ? _themeOf(mm) : new Color(0.36f, 0.62f, 1f);
    public static int BattleStyle => Available ? _battleStyle() : 0;
    public static void SetBattleStyle(int style) { if (Available) _setBattleStyle(style); }
    public static bool EndfieldInstalled => Available && _endfieldInstalled();
    public static bool InFight => Available && _inFight();
    public static double Id(PlayerController p) => Available && p != null ? _id(p) : -1;
    public static bool IsDown(PlayerController p) => Available && _isDown(p);
    public static MotionManager FindCharacter(double id) => Available ? _findCharacter(id) : null;

    public static bool Same(double a, double b) => Math.Abs(a - b) < 0.5;
}
