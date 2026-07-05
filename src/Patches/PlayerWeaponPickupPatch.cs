using System;
using System.Collections.Generic;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using Fika.Core.Main.Players;
using Fika.Core.Main.BotClasses;

namespace FikaWeaponPickupFix.Patches
{
    /// <summary>
    /// When the local player equips a weapon (Proceed), scan all bots in the world
    /// and destroy any orphaned BotFirearmController referencing the same weapon item.
    /// This is a safety net in case BotDeadCleanupPatch missed something.
    /// </summary>
    internal class PlayerWeaponPickupPatch : IPatch
    {
        public MethodBase GetTargetMethod()
        {
            // FikaPlayer.Proceed(Weapon, Callback<IFirearmHandsController>, bool)
            return AccessTools.Method(typeof(FikaPlayer), nameof(FikaPlayer.Proceed),
                new[] { typeof(Weapon), typeof(Callback<IFirearmHandsController>), typeof(bool) });
        }

        public MethodInfo GetPrefix() =>
            AccessTools.Method(typeof(PlayerWeaponPickupPatch), nameof(Prefix));

        public MethodInfo GetPostfix() => null;

        public MethodInfo GetFinalizer() => null;

        private static void Prefix(FikaPlayer __instance, Weapon weapon)
        {
            try
            {
                if (!__instance.IsYourPlayer) return;
                if (weapon == null) return;

                var weaponId = weapon.Id;
                if (string.IsNullOrEmpty(weaponId)) return;

                var gameWorld = Singleton<GameWorld>.Instance;
                if (gameWorld == null) return;

                var cleanedCount = 0;

                // Scan all registered players for bots with orphaned controllers
                foreach (var registered in gameWorld.RegisteredPlayers)
                {
                    if (registered == null) continue;
                    var player = registered as Player;
                    if (player == null) continue;
                    if (ReferenceEquals(player, __instance)) continue; // skip local player
                    if (player.IsYourPlayer) continue;  // skip other local players

                    var hc = player.HandsController;
                    if (hc == null) continue;

                    // Check if this bot has a FirearmController with the same weapon
                    var fc = hc as Player.FirearmController;
                    if (fc == null) continue;

                    var fcItem = fc.Item;
                    if (fcItem == null) continue;
                    if (fcItem.Id != weaponId) continue;

                    // Found an orphaned controller! Detach it.
                    // Do NOT call fc.Destroy() — it triggers
                    // FirearmsAnimator.SetBoltCatch() → Animator NullRef.
                    var typeName = hc.GetType().Name;
                    var botName = player.Profile?.Info?.Nickname ?? "unknown";

                    // Null out the field to disconnect the controller
                    var field = AccessTools.Field(typeof(Player), "_handsController");
                    if (field != null)
                    {
                        field.SetValue(player, null);
                    }

                    cleanedCount++;
                    PluginCore.Log.LogInfo(
                        $"[FIKA_PICKUP_FIX] Cleaned orphaned {typeName} from bot '{botName}' for weapon {weaponId}");
                }

                if (cleanedCount > 0)
                {
                    PluginCore.Log.LogInfo(
                        $"[FIKA_PICKUP_FIX] Cleaned {cleanedCount} orphaned controllers for weapon {weaponId}");
                }
            }
            catch (Exception ex)
            {
                PluginCore.Log.LogWarning(
                    $"[FIKA_PICKUP_FIX] PlayerWeaponPickup cleanup failed: {ex.GetType().Name}: {ex.Message}");
            }
        }
    }
}
