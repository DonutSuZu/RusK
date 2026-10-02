using System;
using System.Linq;
using System.Reflection;

namespace RusK.Core.UI;

/// <summary>
/// ゲームパッドで RusK のメニューを操作している間、ゲーム側の操作 (InputController.SetLockInput) を止める
/// (十字キーや A でキャラが動いたり攻撃したりしないように)。
/// RusK 本体はゲームの DLL を参照しないので、リフレクションで探す。見つからなければ何もしない
/// </summary>
internal static class GameInputLock
{
    private static bool _resolved;
    private static PropertyInfo _instance;
    private static MethodInfo _get, _set;
    private static bool _locked;
    private static bool _prev;

    public static bool Locked => _locked;

    public static void Lock()
    {
        if (_locked || !Resolve()) return;
        try
        {
            var ic = _instance.GetValue(null);
            if (ic == null) return;
            _prev = _get != null && (bool)_get.Invoke(ic, null);
            _set.Invoke(ic, new object[] { true });
            _locked = true;
        }
        catch (Exception e)
        {
            Rusk.Log.LogWarning($"GameInputLock: {e.GetBaseException().Message}");
        }
    }

    public static void Unlock()
    {
        if (!_locked) return;
        _locked = false;
        try
        {
            var ic = _instance.GetValue(null);
            if (ic != null) _set.Invoke(ic, new object[] { _prev });
        }
        catch (Exception e)
        {
            Rusk.Log.LogWarning($"GameInputLock: {e.GetBaseException().Message}");
        }
    }

    private static bool Resolve()
    {
        if (_resolved) return _set != null && _instance != null;
        _resolved = true;
        try
        {
            var type = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name == "Assembly-CSharp")
                .Select(a => a.GetType("InputController"))
                .FirstOrDefault(t => t != null);
            if (type == null) return false;
            _instance = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
            _get = type.GetMethod("GetLockInput", BindingFlags.Public | BindingFlags.Instance, null, Type.EmptyTypes, null);
            _set = type.GetMethod("SetLockInput", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(bool) }, null);
        }
        catch (Exception e)
        {
            Rusk.Log.LogWarning($"GameInputLock: {e.GetBaseException().Message}");
        }
        if (_set == null || _instance == null)
            Rusk.Log.LogWarning("GameInputLock: InputController.SetLockInput が見つかりません (メニュー操作中もゲームが動きます)");
        return _set != null && _instance != null;
    }
}
