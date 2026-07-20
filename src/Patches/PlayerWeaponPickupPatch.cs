using System;
using System.Collections;
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
    /// Patches FikaPlayer.Proceed(Weapon, ...) to fix "broken hands" issue
    /// when picking up weapons from corpses in Fika coop.
    ///
    /// Root cause: Fika's Process<> pipeline sometimes fails to complete the
    /// HandsController switch (especially weapon-to-weapon or non-firearm-to-weapon).
    /// The HandsController stays on the old weapon, causing "broken arms".
    ///
    /// Solution: Monitor after Proceed — if HandsController hasn't switched within
    /// a timeout, force-destroy old controller and re-call Proceed.
    /// </summary>
    internal class PlayerWeaponPickupPatch : IPatch
    {
        private static readonly FieldInfo HandsControllerField =
            AccessTools.Field(typeof(Player), "_handsController");

        private static int _prefixCallCount = 0;
        private static int _postfixCallCount = 0;

        // Monitoring state
        private static Player _monitoredPlayer;
        private static Weapon _monitoredWeapon;
        private static float _monitorStartTime;
        private static bool _monitoring;
        private static int _retryCount;

        private const float MONITOR_TIMEOUT = 2.0f;
        private const int MAX_RETRIES = 2;

        public MethodBase GetTargetMethod()
        {
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
                    $"[FIKA_PICKUP_FIX] Prefix #{_prefixCallCount}: " +
                    $"player={playerType} local={isYour} " +
                    $"weapon={weaponName}({weaponId})");

                if (!isYour || weapon == null) return;

                // Cancel any previous monitor (new Proceed supersedes it)
                _monitoring = false;

                var wId = weapon.Id;
                if (string.IsNullOrEmpty(wId)) return;

                var gameWorld = Singleton<GameWorld>.Instance;
                if (gameWorld == null) return;

                // Scan and clean orphaned controllers from other players
                var cleanedCount = 0;
                foreach (var registered in gameWorld.RegisteredPlayers)
                {
                    if (registered == null) continue;
                    var player = registered as Player;
                    if (player == null) continue;
                    if (ReferenceEquals(player, __instance)) continue;
                    if (player.IsYourPlayer) continue;

                    var hc = player.HandsController;
                    if (hc == null) continue;
                    var fc = hc as Player.FirearmController;
                    if (fc == null) continue;
                    if (fc.Item?.Id != wId) continue;

                    var botName = player.Profile?.Info?.Nickname ?? "unknown";
                    FullCleanup(player, hc);
                    cleanedCount++;
                    PluginCore.Log.LogInfo(
                        $"[FIKA_PICKUP_FIX] Cleaned orphaned controller from '{botName}' for {wId}");
                }

                var currentHc = __instance.HandsController;
                var currentHcType = currentHc?.GetType().Name ?? "null";
                var currentHcItem = (currentHc as Player.FirearmController)?.Item;

                PluginCore.Log.LogInfo(
                    $"[FIKA_PICKUP_FIX] Pre-Proceed: hc={currentHcType} " +
                    $"item={currentHcItem?.ShortName ?? "null"}({currentHcItem?.Id ?? "null"}) " +
                    $"cleaned={cleanedCount}");
            }
            catch (Exception ex)
            {
                PluginCore.Log.LogWarning(
                    $"[FIKA_PICKUP_FIX] Prefix error: {ex.Message}");
            }
        }

        private static void Postfix(Player __instance, Weapon weapon)
        {
            try
            {
                _postfixCallCount++;
                if (!__instance.IsYourPlayer || weapon == null) return;

                var hc = __instance.HandsController;
                var hcType = hc?.GetType().Name ?? "null";
                var hcItem = (hc as Player.FirearmController)?.Item;

                PluginCore.Log.LogInfo(
                    $"[FIKA_PICKUP_FIX] Postfix #{_postfixCallCount}: " +
                    $"hc={hcType} item={hcItem?.ShortName ?? "null"}({hcItem?.Id ?? "null"}) " +
                    $"target={weapon.ShortName}({weapon.Id})");

                // Check if switch happened immediately
                if (hcItem != null && hcItem.Id == weapon.Id)
                {
                    PluginCore.Log.LogInfo("[FIKA_PICKUP_FIX] Switch OK (immediate)");
                    return;
                }

                // Switch didn't happen yet — start monitoring
                if (PluginCore.ForceCleanupBeforeProceed.Value)
                {
                    _monitoredPlayer = __instance;
                    _monitoredWeapon = weapon;
                    _monitorStartTime = Time.realtimeSinceStartup;
                    _monitoring = true;
                    _retryCount = 0;

                    PluginCore.Log.LogWarning(
                        $"[FIKA_PICKUP_FIX] MONITORING started: waiting {MONITOR_TIMEOUT}s for switch to {weapon.ShortName}");
                }
            }
            catch (Exception ex)
            {
                PluginCore.Log.LogWarning(
                    $"[FIKA_PICKUP_FIX] Postfix error: {ex.Message}");
            }
        }

        /// <summary>
        /// Called from PluginCore.Update() every frame. Monitors HandsController switch
        /// and forces retry if it doesn't complete within timeout.
        /// </summary>
        internal static void MonitorUpdate()
        {
            if (!_monitoring || _monitoredPlayer == null || _monitoredWeapon == null)
                return;

            var elapsed = Time.realtimeSinceStartup - _monitorStartTime;

            // Check if switch happened
            var hc = _monitoredPlayer.HandsController;
            var hcItem = (hc as Player.FirearmController)?.Item;

            if (hcItem != null && hcItem.Id == _monitoredWeapon.Id)
            {
                PluginCore.Log.LogInfo(
                    $"[FIKA_PICKUP_FIX] MONITOR: switch completed after {elapsed:F1}s");

                // Diagnostic: check weapon transform and IK state after switch
                try
                {
                    var fc = hc as Player.FirearmController;
                    if (fc != null)
                    {
                        var controllerObj = fc.ControllerGameObject;
                        var weaponPrefab = controllerObj?.GetComponent<WeaponPrefab>();
                        var weaponGO = weaponPrefab?.gameObject;
                        var parent = weaponGO?.transform?.parent;
                        var parentName = parent?.name ?? "null";
                        var parentParent = parent?.parent?.name ?? "null";

                        PluginCore.Log.LogInfo(
                            $"[FIKA_PICKUP_FIX] DIAG: weaponGO={weaponGO?.name ?? "null"} " +
                            $"parent={parentName} grandParent={parentParent} " +
                            $"controllerGO={controllerObj?.name ?? "null"}");

                        // Check if weapon is parented to the player's hands hierarchy
                        if (parent != null)
                        {
                            bool isPlayerChild = false;
                            var check = parent;
                            for (int i = 0; i < 10 && check != null; i++)
                            {
                                if (check.gameObject == _monitoredPlayer.gameObject)
                                {
                                    isPlayerChild = true;
                                    break;
                                }
                                check = check.parent;
                            }
                            if (!isPlayerChild)
                            {
                                PluginCore.Log.LogWarning(
                                    $"[FIKA_PICKUP_FIX] DIAG: WEAPON NOT PARENTED TO PLAYER! " +
                                    $"parent={parentName}");
                            }
                        }

                        // Check ProceduralWeaponAnimation
                        var pwa = _monitoredPlayer.ProceduralWeaponAnimation;
                        if (pwa != null)
                        {
                            var handsPos = pwa.HandsContainer?.HandsPosition?.Get();
                            var handsRot = pwa.HandsContainer?.HandsRotation?.Get();
                            PluginCore.Log.LogInfo(
                                $"[FIKA_PICKUP_FIX] DIAG: PWA handsPos={handsPos} handsRot={handsRot} " +
                                $"overlap={pwa.TurnAway?.OverlapDepth ?? -1f}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    PluginCore.Log.LogWarning($"[FIKA_PICKUP_FIX] DIAG error: {ex.Message}");
                }

                _monitoring = false;
                return;
            }

            // Timeout — force retry
            if (elapsed >= MONITOR_TIMEOUT)
            {
                _retryCount++;

                if (_retryCount > MAX_RETRIES)
                {
                    PluginCore.Log.LogWarning(
                        $"[FIKA_PICKUP_FIX] MONITOR: GAVE UP after {MAX_RETRIES} retries. " +
                        $"HandsController stuck on {hcItem?.ShortName ?? "null"}");
                    _monitoring = false;
                    return;
                }

                PluginCore.Log.LogWarning(
                    $"[FIKA_PICKUP_FIX] MONITOR: TIMEOUT after {elapsed:F1}s. " +
                    $"Force retry #{_retryCount}. Destroying old controller...");

                // Force-destroy the stuck controller
                if (hc != null)
                {
                    FullCleanup(_monitoredPlayer, hc);
                }

                // Re-call Proceed to create new controller
                _monitorStartTime = Time.realtimeSinceStartup;
                try
                {
                    _monitoredPlayer.Proceed(_monitoredWeapon,
                        new Callback<IFirearmHandsController>(result =>
                        {
                            PluginCore.Log.LogInfo(
                                $"[FIKA_PICKUP_FIX] RETRY Proceed callback: {result.Succeed}");
                        }), true);
                }
                catch (Exception ex)
                {
                    PluginCore.Log.LogWarning(
                        $"[FIKA_PICKUP_FIX] RETRY Proceed error: {ex.Message}");
                    _monitoring = false;
                }
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
            } catch { }
            try { controller.Destroy(); } catch { }
            try
            {
                if (controller is UnityEngine.Object o)
                    UnityEngine.Object.Destroy(o);
            } catch { }
            if (HandsControllerField != null)
            {
                try { HandsControllerField.SetValue(player, null); } catch { }
            }
        }
    }
}
