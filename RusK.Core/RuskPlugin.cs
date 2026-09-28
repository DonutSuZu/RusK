using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace RusK.Core;

[BepInPlugin(Rusk.Guid, Rusk.Name, Rusk.Version)]
public class RuskPlugin : BasePlugin
{
    public override void Load()
    {
        Rusk.Initialize(this);

        // Update / OnGUI を受け取るための MonoBehaviour を IL2CPP 側に登録する。
        // Mod 側では MonoBehaviour を作らず、RusK がこのホストからモジュールへ配る
        AddComponent<RuskHost>();
    }

    public override bool Unload()
    {
        Rusk.Shutdown();
        return true;
    }
}
