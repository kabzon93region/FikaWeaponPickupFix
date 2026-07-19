using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using Fika.Core.Main.Players;
using HarmonyLib;
using UnityEngine;

namespace FikaWeaponPickupFix.Patches
{
    /// <summary>
    /// Patches Player.Proceed(Weapon, ...) on the BASE class to catch ALL weapon equip calls.
    /// When the local player equips a weapon, scans all other players in the world
    /// and fully destroys any orphaned FirearmController referencing the same weapon.
    /// Also monitors HandsController state for debugging "broken hands" issues.
    /// </summary>
    internal class PlayerWeaponPickupPatch : IPatch
    {
        private static readonly FieldInfo HandsControllerField =
            AccessTools.Field(typeof(Player), "_handsController");

        private static int _prefixCallCount = 0;
        private static int _postfixCallCount = 0;

        public MethodBase GetTargetMethod()
        {
            // Target FikaPlayer.Proceed — the actual override that fires for local player.
            // Player.Proceed (base) is never called because FikaPlayer overrides it
            // without calling base.Proceed().
            return AccessTools.Method(typeof(FikaPlayer), nameof(FikaPlayer.Proceed),
                new[] { typeof(Weapon), typeof(Callback<IFirearmHandsController>), typeof(bool) });
        }

        public MethodInfo GetPrefix() =>
            AccessTools.Method(typeof(PlayerWeaponPickupPatch), nameof(Prefix));

        public MethodInfo GetPostfix() =>
            AccessTools.Method(typeof(PlayerWeaponPickupPatch), nameof(Postfix));

        public MethodInfo GetFinalizer() => null;

        private static void Prefix(Player __instance, Weapon weapon)
        {
            try
            {
                _prefixCallCount++;
                var playerType = __instance.GetType().Name;
                var isYour = __instance.IsYourPlayer;
                var weaponId = weapon?.Id ?? "null";
                var weaponName = weapon?.ShortName ?? "null";

                PluginCore.Log.LogInfo(
                    $"[FIKA_PICKUP_FIX] Prefix #{_prefixCallCount} called: " +
                    $"playerType={playerType} isYourPlayer={isYour} " +
                    $"weapon={weaponName} weaponId={weaponId}");

                // Only clean up orphaned controllers for the local player
                if (!isYour)
                {
                    PluginCore.Log.LogInfo(
                        $"[FIKA_PICKUP_FIX] Prefix: skipping non-local player {playerType}");
                    return;
                }

                if (weapon == null) return;

                var wId = weapon.Id;
                if (string.IsNullOrEmpty(wId)) return;

                var gameWorld = Singleton<GameWorld>.Instance;
                if (gameWorld == null)
                {
                    PluginCore.Log.LogWarning("[FIKA_PICKUP_FIX] Prefix: GameWorld is null");
                    return;
                }

                var cleanedCount = 0;
                var scannedCount = 0;

                foreach (var registered in gameWorld.RegisteredPlayers)
                {
                    if (registered == null) continue;
                    var player = registered as Player;
                    if (player == null) continue;
                    if (ReferenceEquals(player, __instance)) continue;
                    if (player.IsYourPlayer) continue;

                    scannedCount++;

                    var hc = player.HandsController;
                    if (hc == null) continue;

                    var fc = hc as Player.FirearmController;
                    if (fc == null) continue;

                    var fcItem = fc.Item;
                    if (fcItem == null) continue;
                    if (fcItem.Id != wId) continue;

                    var typeName = hc.GetType().Name;
                    var botName = player.Profile?.Info?.Nickname ?? "unknown";
                    var isDead = player.HealthController == null || !player.HealthController.IsAlive;
                    var deadTag = isDead ? " [DEAD]" : "";

                    FullCleanup(player, hc);

                    cleanedCount++;
                    PluginCore.Log.LogInfo(
                        $"[FIKA_PICKUP_FIX] Disposed orphaned {typeName} from bot '{botName}'{deadTag} " +
                        $"for weapon {wId}");
                }

                PluginCore.Log.LogInfo(
                    $"[FIKA_PICKUP_FIX] Weapon pickup scan: scanned={scannedCount} cleaned={cleanedCount} " +
                    $"weapon={weaponName}({wId})");

                // Log current hands controller state before Proceed
                var currentHc = __instance.HandsController;
                var currentHcType = currentHc?.GetType().Name ?? "null";
                var currentHcItem = (currentHc as Player.FirearmController)?.Item;
                PluginCore.Log.LogInfo(
                    $"[FIKA_PICKUP_FIX] Pre-Proceed state: currentHandsController={currentHcType} " +
                    $"currentItem={currentHcItem?.ShortName ?? "null"}({currentHcItem?.Id ?? "null"})");
            }
            catch (Exception ex)
            {
                PluginCore.Log.LogWarning(
                    $"[FIKA_PICKUP_FIX] Prefix failed: " +
                    $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        private static void Postfix(Player __instance, Weapon weapon)
        {
            try
            {
                _postfixCallCount++;

                if (!__instance.IsYourPlayer) return;

                // Log hands controller state after Proceed
                var hc = __instance.HandsController;
                var hcType = hc?.GetType().Name ?? "null";
                var hcItem = (hc as Player.FirearmController)?.Item;
                var hcItemName = hcItem?.ShortName ?? "null";
                var hcItemId = hcItem?.Id ?? "null";

                PluginCore.Log.LogInfo(
                    $"[FIKA_PICKUP_FIX] Postfix #{_postfixCallCount}: " +
                    $"handsController={hcType} " +
                    $"item={hcItemName}({hcItemId}) " +
                    $"weapon={weapon?.ShortName ?? "null"}({weapon?.Id ?? "null"})");

                // Check if the hands controller item matches the requested weapon
                if (weapon != null && hcItem != null && hcItem.Id != weapon.Id)
                {
                    PluginCore.Log.LogWarning(
                        $"[FIKA_PICKUP_FIX] MISMATCH! HandsController item ({hcItemName}) " +
                        $"!= requested weapon ({weapon.ShortName})");
                }

                // Check if the hands controller is null after Proceed (should not happen)
                if (hc == null)
                {
                    PluginCore.Log.LogWarning(
                        "[FIKA_PICKUP_FIX] HandsController is NULL after Proceed!");
                }
            }
            catch (Exception ex)
            {
                PluginCore.Log.LogWarning(
                    $"[FIKA_PICKUP_FIX] Postfix failed: " +
                    $"{ex.GetType().Name}: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Properly disposes a bot's HandsController following the canonical
        /// Player.OnDead() cleanup sequence: OnPlayerDead -> FastForward -> Destroy -> Unity Destroy -> null.
        /// </summary>
        private static void FullCleanup(Player player, IHandsController controller)
        {
            // Step 1: OnPlayerDead — stops aiming, breaks firing loops, unsubscribes events
            try { controller.OnPlayerDead(); }
            catch { /* controller may already be disposed */ }

            // Step 2: FastForwardCurrentState — completes or cancels pending operations
            // Must loop because FastForward can trigger a state transition
            // FastForwardCurrentState is on AbstractHandsController, not IHandsController
            try
            {
                if (controller is Player.AbstractHandsController ac)
                {
                    int maxIterations = 10;
                    while (maxIterations-- > 0)
                    {
                        var before = ac;
                        ac.FastForwardCurrentState();
                        if (ReferenceEquals(ac, before)) break;
                    }
                }
            }
            catch { /* ignore */ }

            // Step 3: Destroy — cleans up internal state, unsubscribes network packets
            try { controller.Destroy(); }
            catch { /* ignore */ }

            // Step 4: Unity Object.Destroy — destroys the underlying MonoBehaviour
            try
            {
                if (controller is UnityEngine.Object unityObj)
                    UnityEngine.Object.Destroy(unityObj);
            }
            catch { /* ignore */ }

            // Step 5: Null the _handsController field on the Player
            if (HandsControllerField != null)
            {
                try { HandsControllerField.SetValue(player, null); }
                catch { /* ignore */ }
            }
        }
    }
}
