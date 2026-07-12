using System;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;

namespace FikaWeaponPickupFix
{
    [BepInPlugin(PluginInfo.GUID, PluginInfo.NAME, PluginInfo.VERSION)]
    [BepInDependency("com.fika.core", BepInDependency.DependencyFlags.HardDependency)]
    public class PluginCore : BaseUnityPlugin
    {
        internal static ManualLogSource Log;
        private Harmony _harmony;

        private void Awake()
        {
            Log = Logger;
            _harmony = new Harmony(PluginInfo.GUID);

            // Patch Player.Proceed (base class) to catch ALL weapon equip calls.
            // When the local player equips a weapon, clean up orphaned FirearmControllers
            // from other players referencing the same weapon.
            ApplyPatch(typeof(Patches.PlayerWeaponPickupPatch));

            Log.LogInfo($"[{PluginInfo.NAME}] v{PluginInfo.VERSION} loaded (1 patch)");
        }

        private void ApplyPatch(Type patchType)
        {
            try
            {
                var patch = (IPatch)Activator.CreateInstance(patchType);
                var method = patch.GetTargetMethod();
                if (method == null)
                {
                    Log.LogWarning($"[{PluginInfo.NAME}] {patchType.Name}: target method not found!");
                    return;
                }
                var prefix = patch.GetPrefix();
                var postfix = patch.GetPostfix();
                var finalizer = patch.GetFinalizer();

                if (finalizer != null)
                {
                    // Use named params for finalizer (old 4-arg overload is obsolete)
                    _harmony.Patch(method,
                        prefix: prefix != null ? new HarmonyMethod(prefix) : null,
                        postfix: postfix != null ? new HarmonyMethod(postfix) : null,
                        finalizer: new HarmonyMethod(finalizer));
                }
                else
                {
                    _harmony.Patch(method,
                        prefix != null ? new HarmonyMethod(prefix) : null,
                        postfix != null ? new HarmonyMethod(postfix) : null);
                }
                Log.LogInfo($"[{PluginInfo.NAME}] {patchType.Name} applied");
            }
            catch (Exception ex)
            {
                Log.LogError($"[{PluginInfo.NAME}] {patchType.Name} failed: {ex.Message}");
            }
        }
    }

    public interface IPatch
    {
        MethodBase GetTargetMethod();
        MethodInfo GetPrefix();
        MethodInfo GetPostfix();
        MethodInfo GetFinalizer();
    }
}
