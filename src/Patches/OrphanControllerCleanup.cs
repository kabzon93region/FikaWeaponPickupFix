using System;
using System.Reflection;
using Comfort.Common;
using EFT;
using EFT.InventoryLogic;
using HarmonyLib;
using UnityEngine;

namespace FikaWeaponPickupFix.Patches
{
    /// <summary>
    /// Timer-based orphaned FirearmController cleanup.
    /// Scans all players every few seconds. If a dead player's HandsController
    /// references a weapon that is now in the local player's inventory,
    /// destroys that orphaned controller to prevent state conflicts.
    /// 
    /// Does NOT patch Proceed — avoids interfering with Fika's weapon equip pipeline.
    /// </summary>
    internal class OrphanControllerCleanup
    {
        private static readonly FieldInfo HandsControllerField =
            AccessTools.Field(typeof(Player), "_handsController");

        private static float _nextScanTime;
        private const float SCAN_INTERVAL = 3.0f;

        internal static void Update()
        {
            if (Time.realtimeSinceStartup < _nextScanTime)
                return;
            _nextScanTime = Time.realtimeSinceStartup + SCAN_INTERVAL;

            try
            {
                var gameWorld = Singleton<GameWorld>.Instance;
                if (gameWorld == null) return;

                var localPlayer = gameWorld.MainPlayer;
                if (localPlayer == null) return;

                // Build set of weapon IDs in local player's inventory
                var inventoryController = localPlayer.InventoryController;
                if (inventoryController == null) return;

                foreach (var registered in gameWorld.RegisteredPlayers)
                {
                    if (registered == null) continue;
                    var player = registered as Player;
                    if (player == null) continue;
                    if (ReferenceEquals(player, localPlayer)) continue;

                    var hc = player.HandsController;
                    if (hc == null) continue;

                    var fc = hc as Player.FirearmController;
                    if (fc == null) continue;

                    var fcItem = fc.Item;
                    if (fcItem == null) continue;

                    // Only clean up if the player is dead
                    var isAlive = player.HealthController != null && player.HealthController.IsAlive;
                    if (isAlive) continue;

                    // Check if the weapon is now in local player's inventory
                    bool inLocalInventory = false;
                    try
                    {
                        var localItems = localPlayer.Inventory?.AllRealPlayerItems;
                        if (localItems != null)
                        {
                            foreach (var item in localItems)
                            {
                                if (item != null && item.Id == fcItem.Id)
                                {
                                    inLocalInventory = true;
                                    break;
                                }
                            }
                        }
                    }
                    catch { }

                    if (!inLocalInventory) continue;

                    var botName = player.Profile?.Info?.Nickname ?? "unknown";
                    var weaponName = fcItem.ShortName ?? "unknown";

                    PluginCore.Log.LogInfo(
                        $"[ORPHAN_CLEANUP] Cleaning dead '{botName}' controller for weapon {weaponName}({fcItem.Id})");

                    FullCleanup(player, hc);
                }
            }
            catch (Exception ex)
            {
                PluginCore.Log.LogWarning($"[ORPHAN_CLEANUP] Scan error: {ex.Message}");
            }
        }

        private static void FullCleanup(Player player, IHandsController controller)
        {
            try { controller.OnPlayerDead(); } catch { }
            try
            {
                if (controller is Player.AbstractHandsController ac)
                {
                    int max = 10;
                    while (max-- > 0)
                    {
                        var before = ac;
                        ac.FastForwardCurrentState();
                        if (ReferenceEquals(ac, before)) break;
                    }
                }
            }
            catch { }
            try { controller.Destroy(); } catch { }
            try
            {
                if (controller is UnityEngine.Object o)
                    UnityEngine.Object.Destroy(o);
            }
            catch { }
            if (HandsControllerField != null)
            {
                try { HandsControllerField.SetValue(player, null); } catch { }
            }
        }
    }
}
