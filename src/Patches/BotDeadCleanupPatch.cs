using System;
using System.Reflection;
using BepInEx.Logging;
using EFT;
using HarmonyLib;
using Fika.Core.Main.Players;
using UnityEngine;

namespace FikaWeaponPickupFix.Patches
{
    /// <summary>
    /// When a FikaBot dies, destroy its hands controller to prevent orphaned
    /// BotFirearmController from interfering with player weapon pickup later.
    /// </summary>
    internal class BotDeadCleanupPatch : IPatch
    {
        public MethodBase GetTargetMethod()
        {
            return AccessTools.Method(typeof(FikaBot), nameof(FikaBot.OnDead));
        }

        public MethodInfo GetPrefix() => null;

        public MethodInfo GetPostfix() =>
            AccessTools.Method(typeof(BotDeadCleanupPatch), nameof(Postfix));

        public MethodInfo GetFinalizer() => null;

        private static void Postfix(FikaBot __instance)
        {
            try
            {
                var hc = __instance.HandsController;
                if (hc == null) return;

                var typeName = hc.GetType().Name;

                // Do NOT call hc.Destroy() — it internally calls
                // FirearmsAnimator.SetBoltCatch() → Animator.SetBool() on
                // an already-destroyed Animator → NullRef that corrupts
                // the player's animation state.
                //
                // Instead, just null out the field to disconnect the
                // orphaned controller from the bot. Unity GC will clean up.
                var field = AccessTools.Field(typeof(Player), "_handsController");
                if (field != null)
                {
                    field.SetValue(__instance, null);
                }

                PluginCore.Log.LogInfo(
                    $"[FIKA_PICKUP_FIX] Bot '{__instance.Profile?.Info?.Nickname}' dead: detached {typeName}");
            }
            catch (Exception ex)
            {
                PluginCore.Log.LogWarning(
                    $"[FIKA_PICKUP_FIX] BotDeadCleanup failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
