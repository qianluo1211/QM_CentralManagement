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
        internal static bool ShipLoadoutsEnabled { get; set; } = true;
        internal static float LoadoutBarOffsetY { get; set; } = 0f;
        internal static bool ShuttleManifestsEnabled { get; set; } = true;
        // On by default: it does nothing at all until the player has saved a
        // manifest and selected it, so creating one IS the opt-in.
        internal static bool ShuttleAutoRestock { get; set; } = true;

        private static AccessTools.FieldRef<ArsenalScreen, ItemTabsView>
            _inventoryTabsView;
        private static AccessTools.FieldRef<AfterRaidScreen,
            NoPlayerInventoryView> _afterRaidInventoryView;

        private static void PatchShipLoadout(Harmony harmony)
        {
            // Registered before the patches: an input gate that silently
            // failed to register would let typed keys reach the game.
            ModInputGate.Register(() => ShipLoadoutBar.AnyInputCaptured);
            _inventoryTabsView = AccessTools.FieldRefAccess<ArsenalScreen,
                ItemTabsView>("_inventoryTabsView");
            // _shuttleCargoStorageView is bound in PatchCentralArsenal, which
            // runs first; both features read it through ShuttleCargoViewOf.
            _afterRaidInventoryView = AccessTools.FieldRefAccess<
                AfterRaidScreen, NoPlayerInventoryView>("_inventoryView");
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

            // The post-extraction screen is the second place a loadout is
            // worth applying: it already exists to sort what came back, and
            // applying a preset there puts the run's gear back on and drops
            // everything the preset does not name into the hold -- the
            // unload-to-cargo button, but selective.
            PatchRequired(harmony, typeof(AfterRaidScreen),
                nameof(AfterRaidScreen.Configure),
                postfix: nameof(AfterRaidLoadoutPostfix),
                argumentTypes: new[] { typeof(Mercenary) });
            PatchRequired(harmony, typeof(AfterRaidScreen),
                nameof(AfterRaidScreen.RefreshView),
                postfix: nameof(AfterRaidLoadoutPostfix),
                argumentTypes: Type.EmptyTypes);
        }

        /// <summary>
        /// The screen's own inventory view. ArsenalScreen and AfterRaidScreen
        /// name the same field but do not share a base that declares it.
        /// </summary>
        internal static NoPlayerInventoryView LoadoutInventoryViewOf(
            ScreenWithShipCargo screen)
        {
            if (screen is ArsenalScreen arsenal)
                return _inventoryView(arsenal);
            if (screen is AfterRaidScreen afterRaid)
                return _afterRaidInventoryView(afterRaid);
            return null;
        }

        /// <summary>
        /// The panel the bar anchors itself above. ArsenalScreen exposes it
        /// as a field because central mode has to switch it on and off;
        /// AfterRaidScreen does not, but on both screens it is simply the
        /// inventory view's parent.
        /// </summary>
        internal static GameObject LoadoutWindowOf(ScreenWithShipCargo screen)
        {
            if (screen is ArsenalScreen arsenal)
                return _inventoryWindow(arsenal);
            var view = LoadoutInventoryViewOf(screen);
            var parent = view == null ? null : view.transform.parent;
            return parent == null ? null : parent.gameObject;
        }

        /// <summary>
        /// Only the pre-departure arsenal has a tab strip above the window,
        /// and only there does the shuttle hold exist to be manifested.
        /// </summary>
        internal static ItemTabsView LoadoutTabsViewOf(
            ScreenWithShipCargo screen)
        {
            return screen is ArsenalScreen arsenal
                ? _inventoryTabsView(arsenal)
                : null;
        }

        internal static ShuttleCargoInSpaceStorageView LoadoutShuttleViewOf(
            ScreenWithShipCargo screen)
        {
            return screen is ArsenalScreen arsenal
                ? ShuttleCargoViewOf(arsenal)
                : null;
        }

        private static void AfterRaidLoadoutPostfix(AfterRaidScreen __instance)
        {
            try
            {
                ShipLoadoutBar.RefreshFor(__instance);
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix
                               + "after-raid loadout hook failed: " + e);
            }
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

        internal static ShuttleCargoInSpaceStorageView ShuttleCargoViewOf(
            ArsenalScreen screen)
            => _shuttleCargoStorageView(screen);

        internal static PerkFactory ScreenPerkFactoryOf(
            ScreenWithShipCargo screen)
            => _screenPerkFactory(screen);


        private static void ShipLoadoutConfigurePostfix(
            ArsenalScreen __instance, bool showShuttle)
        {
            try
            {
                // Configure is the "screen opened" moment, so an automatic
                // top-up happens once per visit rather than on every refresh.
                // showShuttle is the game's own test for "this is the
                // pre-departure arsenal": MercenariesScreen and
                // SpaceshipScreen both pass false and have no shuttle to fill.
                if (showShuttle)
                    ShipLoadoutBar.AutoRestock(__instance);
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

    }
}
