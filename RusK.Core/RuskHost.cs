using System;
using UnityEngine;

namespace RusK.Core;

/// <summary>
/// IL2CPP 側に登録される唯一の MonoBehaviour。
/// Unity のイベントを受け取って、RusK の各マネージャに配る。
/// </summary>
internal class RuskHost : MonoBehaviour
{
    public RuskHost(IntPtr ptr) : base(ptr) { }

    private void Update()
    {
        try
        {
            Rusk.RunDeferred();
            Rusk.Modules.Update();
            Rusk.Ui.Update();
        }
        catch (Exception e)
        {
            Rusk.Log.LogError($"Update error: {e}");
        }
    }

    private void OnGUI()
    {
        try { Rusk.Ui.OnGUI(); }
        catch (Exception e) { Rusk.Log.LogError($"OnGUI error: {e}"); }
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused) Rusk.Ui.ReleaseKeys();
    }

    private void OnApplicationQuit()
    {
        Rusk.Config.Save();
    }
}
