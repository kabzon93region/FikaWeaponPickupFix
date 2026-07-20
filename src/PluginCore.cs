using BepInEx;
using BepInEx.Logging;

namespace FikaWeaponPickupFix
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.NAME, PluginInfo.VERSION)]
    [BepInDependency("com.fika.core", BepInDependency.DependencyFlags.HardDependency)]
    public class PluginCore : BaseUnityPlugin
    {
        internal static ManualLogSource Log;

        private void Awake()
        {
            Log = Logger;
            Log.LogInfo($"[{PluginInfo.NAME}] v{PluginInfo.VERSION} loaded (timer-based orphan cleanup)");
        }

        private void Update()
        {
            Patches.OrphanControllerCleanup.Update();
        }
    }
}
