using System;
using System.Globalization;
using HarmonyLib;
using MGSC;
using UnityEngine;

namespace QM_CentralManagement
{
    public static partial class Plugin
    {
        // config.txt switches, parsed in LoadConfig.
        internal static bool ShipLoadoutsEnabled { get; private set; } = true;
        internal static float LoadoutBarOffsetY { get; private set; } = 0f;

        private static AccessTools.FieldRef<ArsenalScreen, ItemTabsView>
            _inventoryTabsView;

        private static void PatchShipLoadout(Harmony harmony)
        {
            _inventoryTabsView = AccessTools.FieldRefAccess<ArsenalScreen,
                ItemTabsView>("_inventoryTabsView");
            PatchRequired(harmony, typeof(ArsenalScreen),
                nameof(ArsenalScreen.Configure),
                postfix: nameof(ShipLoadoutConfigurePostfix),
                argumentTypes: new[] { typeof(Mercenary), typeof(bool) });
            PatchRequired(harmony, typeof(ArsenalScreen),
                nameof(ArsenalScreen.RefreshView),
                postfix: nameof(ShipLoadoutRefreshViewPostfix),
                argumentTypes: Type.EmptyTypes);
            PatchRequired(harmony, typeof(ArsenalScreen),
                nameof(ArsenalScreen.Refresh),
                postfix: nameof(ShipLoadoutRefreshPostfix),
                argumentTypes: new[] { typeof(bool), typeof(ItemTab) });
            PatchRequired(harmony, typeof(ScreenWithShipCargo),
                "OnDisable",
                prefix: nameof(ShipLoadoutScreenDisabledPrefix),
                argumentTypes: Type.EmptyTypes);
        }

        // Thin accessors for the FieldRefs bound in PatchCentralArsenal, so
        // ShipLoadoutBar does not need to reach into Plugin's privates.
        internal static Mercenary ScreenMercenaryOf(ScreenWithShipCargo screen)
            => _screenMercenary(screen);
        internal static MagnumCargo ScreenCargoOf(ScreenWithShipCargo screen)
            => _screenCargo(screen);
        internal static SpaceTime ScreenSpaceTimeOf(ScreenWithShipCargo screen)
            => _screenSpaceTime(screen);
        internal static MagnumProgression ScreenProgressionOf(
            ScreenWithShipCargo screen)
            => _screenProgression(screen);
        internal static ItemStorage ActiveShipCargoOf(
            ScreenWithShipCargo screen)
            => _activeShipCargo(screen);
        internal static GameObject InventoryWindowOf(ArsenalScreen screen)
            => _inventoryWindow(screen);
        internal static NoPlayerInventoryView InventoryViewOf(
            ArsenalScreen screen)
            => _inventoryView(screen);

        internal static ItemTabsView InventoryTabsViewOf(
            ArsenalScreen screen)
            => _inventoryTabsView(screen);

        internal static PerkFactory ScreenPerkFactoryOf(
            ScreenWithShipCargo screen)
            => _screenPerkFactory(screen);

        internal static void ParseShipLoadoutConfig(string key, string value)
        {
            if (key.Equals("shipLoadouts",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (!bool.TryParse(value, out var enabled))
                {
                    Debug.LogWarning(LogPrefix
                                     + "shipLoadouts must be true or false.");
                    return;
                }
                ShipLoadoutsEnabled = enabled;
            }
            else if (key.Equals("loadoutBarOffsetY",
                         StringComparison.OrdinalIgnoreCase))
            {
                if (!float.TryParse(value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var offset)
                    || offset < -100f || offset > 100f)
                {
                    Debug.LogWarning(LogPrefix
                                     + "loadoutBarOffsetY must be between "
                                     + "-100 and 100; keeping "
                                     + LoadoutBarOffsetY + ".");
                    return;
                }
                LoadoutBarOffsetY = offset;
            }
        }

        private static void ShipLoadoutConfigurePostfix(
            ArsenalScreen __instance)
        {
            try
            {
                ShipLoadoutBar.RefreshFor(__instance);
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix
                               + "ship loadout configure hook failed: " + e);
            }
        }

        private static void ShipLoadoutRefreshViewPostfix(
            ArsenalScreen __instance)
        {
            try
            {
                ShipLoadoutBar.RefreshFor(__instance);
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix
                               + "ship loadout refresh hook failed: " + e);
            }
        }

        private static void ShipLoadoutRefreshPostfix(
            ArsenalScreen __instance)
        {
            try
            {
                ShipLoadoutBar.RefreshFor(__instance);
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix
                               + "ship loadout tab hook failed: " + e);
            }
        }

        private static void ShipLoadoutScreenDisabledPrefix(
            ScreenWithShipCargo __instance)
        {
            try
            {
                ShipLoadoutBar.HideFor(__instance);
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix
                               + "ship loadout cleanup hook failed: " + e);
            }
        }
    }
}
