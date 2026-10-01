using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace RusK.Mods.Ui;

/// <summary>
/// Party Mod のバトルスタイルを見る (RusK.Mods.Party.PartyBridge.HideButtonHud をリフレクションで読む)。
/// エンドフィールドスタイルでは、Custom Battle System and Ui for Op.2 がスキルボタンを出すので、ボタン HUD は隠す。
/// Party が入っていなければ常に false (Mod は別々の AssemblyLoadContext で読み込まれ、直接は参照できない)
/// </summary>
internal static class PartyStyle
{
    private static PropertyInfo _hide;
    private static float _nextFind;
    private static float _nextRead;
    private static bool _cached;

    public static bool HideButtonHud
    {
        get
        {
            if (Time.unscaledTime < _nextRead) return _cached;
            _nextRead = Time.unscaledTime + 0.5f;
            if (_hide == null && Time.unscaledTime >= _nextFind)
            {
                _nextFind = Time.unscaledTime + 5f;
                try
                {
                    var asm = AppDomain.CurrentDomain.GetAssemblies().LastOrDefault(a => a.GetName().Name == "RuskParty");
                    _hide = asm?.GetType("RusK.Mods.Party.PartyBridge")
                        ?.GetProperty("HideButtonHud", BindingFlags.Public | BindingFlags.Static);
                }
                catch { _hide = null; }
            }
            try { _cached = _hide != null && (bool)_hide.GetValue(null); }
            catch { _cached = false; _hide = null; }
            return _cached;
        }
    }
}
