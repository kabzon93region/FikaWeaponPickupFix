using System;
using System.Reflection;
using BepInEx.Logging;
using EFT;
using HarmonyLib;

namespace FikaWeaponPickupFix.Patches
{
    /// <summary>
    /// FirearmController.Destroy() calls FirearmsAnimator.SetBoltCatch()
    /// which accesses UnityEngine.Animator.SetBool() on an already-destroyed
    /// Animator component → NullReferenceException during bot OnDestroy cleanup.
    /// 
    /// This finalizer catches the NullRef so it doesn't corrupt game state.
    /// </summary>
    internal class FirearmControllerDestroyPatch : IPatch
    {
        public MethodBase GetTargetMethod()
        {
            // EFT.Player+FirearmController.Destroy()
            var fcType = typeof(Player.FirearmController);
            return AccessTools.Method(fcType, "Destroy");
        }

        public MethodInfo GetPrefix() => null;

        public MethodInfo GetPostfix() => null;

        public MethodInfo GetFinalizer() =>
            AccessTools.Method(typeof(FirearmControllerDestroyPatch), nameof(Finalizer));

        private static Exception Finalizer(Exception __exception)
        {
            if (__exception is NullReferenceException)
            {
                PluginCore.Log.LogInfo(
                    "[FIKA_PICKUP_FIX] Caught NullRef in FirearmController.Destroy() (animator already disposed)");
                return null; // swallow the exception
            }
            return __exception; // re-throw other exceptions
        }
    }
}
