using System;
using HarmonyLib;
using MGSC;
using UnityEngine;

namespace QM_CentralManagement
{
    /// <summary>
    /// Station trade takeover.
    ///
    /// The vanilla flow: SpaceStationsWindow.StationSelected decides between
    /// a raid screen and the trade screen (FastTradeScreen) and chains the
    /// chosen view. When the central management technology is unlocked and
    /// the game's own FastTrade option is on, this mod opens the very same
    /// FastTradeScreen -- so every vanilla service, quest guard and
    /// save-game touch stays intact -- and swaps its content for the mod's
    /// CentralStationTradePanel, exactly like central mode swaps the content
    /// of ArsenalScreen.
    /// </summary>
    public static partial class Plugin
    {
        // Config.
        private static bool _stationTradeEnabled = true;
        private static bool _tradeConfirm;
        private static bool _debugTradeLayout;
        private static bool _autoUnlockTech;
        private static int _quantityShiftStep = 10;
        private static int _quantityCtrlStep = 100;
        private static int _quantityCtrlShiftStep = 1000;
        // Keyboard shortcuts for the trade panel. Chosen to avoid every
        // vanilla binding (I/Tab/S/E/Z/X/R/Space/C/T/M/P/H/Q/V/F/G/J/Y/N/
        // Return/Escape/1-9/arrows/PageUp/PageDown/Shift-Ctrl-Alt combos).
        private static KeyCode _shortcutTogglePane = KeyCode.B;
        private static KeyCode _shortcutPrevPage = KeyCode.LeftBracket;
        private static KeyCode _shortcutNextPage = KeyCode.RightBracket;
        private static KeyCode _shortcutTrade = KeyCode.D;
        private static KeyCode _shortcutClearCart = KeyCode.Delete;
        private static KeyCode _shortcutSelectAll = KeyCode.A;

        internal static bool StationTradeEnabled => _stationTradeEnabled;
        internal static bool TradeConfirm => _tradeConfirm;
        internal static bool DebugTradeLayout => _debugTradeLayout;
        internal static int QuantityShiftStep => _quantityShiftStep;
        internal static int QuantityCtrlStep => _quantityCtrlStep;
        internal static int QuantityCtrlShiftStep => _quantityCtrlShiftStep;
        internal static KeyCode ShortcutTogglePane => _shortcutTogglePane;
        internal static KeyCode ShortcutPrevPage => _shortcutPrevPage;
        internal static KeyCode ShortcutNextPage => _shortcutNextPage;
        internal static KeyCode ShortcutTrade => _shortcutTrade;
        internal static KeyCode ShortcutClearCart => _shortcutClearCart;
        internal static KeyCode ShortcutSelectAll => _shortcutSelectAll;



        // SpaceStationsWindow privates.
        private static AccessTools.FieldRef<SpaceStationsWindow, bool>
            _swAllowClicks;
        private static AccessTools.FieldRef<SpaceStationsWindow, TravelMetadata>
            _swTravel;
        private static AccessTools.FieldRef<SpaceStationsWindow, Missions>
            _swMissions;
        private static AccessTools.FieldRef<SpaceStationsWindow, StoryTriggers>
            _swTriggers;
        private static AccessTools.FieldRef<SpaceStationsWindow, Factions>
            _swFactions;

        // FastTradeScreen privates.
        private static AccessTools.FieldRef<FastTradeScreen, Station>
            _ftStation;
        private static AccessTools.FieldRef<FastTradeScreen, ItemTabsView>
            _ftTabsView;
        private static AccessTools.FieldRef<FastTradeScreen, StationCargoTradePage>
            _ftCargoTradePage;
        private static AccessTools.FieldRef<FastTradeScreen, StationExchangePage>
            _ftExchangePage;
        private static AccessTools.FieldRef<FastTradeScreen, StoryTriggers>
            _ftStoryTriggers;
        private static AccessTools.FieldRef<StationCargoTradePage, ExchangeView>
            _ctpExchangeView;
        private static AccessTools.FieldRef<StationCargoTradePage, ExtraChargeView>
            _ctpExtraChargeView;

        // ScreenWithShipCargo services the panel reads.
        private static AccessTools.FieldRef<ScreenWithShipCargo, Factions>
            _scFactions;
        private static AccessTools.FieldRef<ScreenWithShipCargo, ItemsPrices>
            _scItemsPrices;
        private static AccessTools.FieldRef<ScreenWithShipCargo, Statistics>
            _scStatistics;
        private static AccessTools.FieldRef<ScreenWithShipCargo, Difficulty>
            _scDifficulty;

        private static bool _centralStationTradeRequested;

        internal static ItemStorage ActiveCargoOf(ScreenWithShipCargo screen)
        {
            return screen == null ? null : _activeShipCargo(screen);
        }

        internal static GameObject CargoWindowOf(ScreenWithShipCargo screen)
        {
            return screen == null ? null : _cargoWindow(screen);
        }

        internal static ExchangeView ExchangeViewOf(FastTradeScreen screen)
        {
            if (screen == null)
                return null;
            var page = _ftCargoTradePage(screen);
            return page == null ? null : _ctpExchangeView(page);
        }

        internal static ExtraChargeView ExtraChargeViewOf(FastTradeScreen screen)
        {
            if (screen == null)
                return null;
            var page = _ftCargoTradePage(screen);
            return page == null ? null : _ctpExtraChargeView(page);
        }

        internal static StationExchangePage ExchangePageOf(
            FastTradeScreen screen)
        {
            return screen == null ? null : _ftExchangePage(screen);
        }

        internal static StationCargoTradePage CargoTradePageOf(
            FastTradeScreen screen)
        {
            return screen == null ? null : _ftCargoTradePage(screen);
        }

        private static void PatchStationTrade(Harmony harmony)
        {
            ModInputGate.Register(() => CentralStationTradePanel.CapturesInput);
            _swAllowClicks = AccessTools.FieldRefAccess<SpaceStationsWindow,
                bool>("_allowClicks");
            _swTravel = AccessTools.FieldRefAccess<SpaceStationsWindow,
                TravelMetadata>("_travelMetadata");
            _swMissions = AccessTools.FieldRefAccess<SpaceStationsWindow,
                Missions>("_missions");
            _swTriggers = AccessTools.FieldRefAccess<SpaceStationsWindow,
                StoryTriggers>("_storyTriggers");
            _swFactions = AccessTools.FieldRefAccess<SpaceStationsWindow,
                Factions>("_factions");

            _ftStation = AccessTools.FieldRefAccess<FastTradeScreen,
                Station>("_station");
            _ftTabsView = AccessTools.FieldRefAccess<FastTradeScreen,
                ItemTabsView>("_stationTabsView");
            _ftCargoTradePage = AccessTools.FieldRefAccess<FastTradeScreen,
                StationCargoTradePage>("_cargoTradePage");
            _ftExchangePage = AccessTools.FieldRefAccess<FastTradeScreen,
                StationExchangePage>("_exchangePage");
            _ftStoryTriggers = AccessTools.FieldRefAccess<FastTradeScreen,
                StoryTriggers>("_storyTriggers");
            _ctpExchangeView = AccessTools.FieldRefAccess<
                StationCargoTradePage, ExchangeView>("_exchangeView");
            _ctpExtraChargeView = AccessTools.FieldRefAccess<
                StationCargoTradePage, ExtraChargeView>("_extraChargeView");

            _scFactions = AccessTools.FieldRefAccess<ScreenWithShipCargo,
                Factions>("_factions");
            _scItemsPrices = AccessTools.FieldRefAccess<ScreenWithShipCargo,
                ItemsPrices>("_itemsPrices");
            _scStatistics = AccessTools.FieldRefAccess<ScreenWithShipCargo,
                Statistics>("_statistics");
            _scDifficulty = AccessTools.FieldRefAccess<ScreenWithShipCargo,
                Difficulty>("_difficulty");

            // The single decision point: a station click that vanilla would
            // turn into the trade screen becomes our trade screen instead.
            PatchRequired(harmony, typeof(SpaceStationsWindow),
                "StationSelected",
                prefix: nameof(StationSelectedPrefix),
                argumentTypes: new[] { typeof(Station) });
            PatchRequired(harmony, typeof(FastTradeScreen),
                nameof(FastTradeScreen.Configure),
                postfix: nameof(FastTradeConfigurePostfix),
                argumentTypes: new[] { typeof(Station) });

            // The panel covers the whole screen, so the vanilla cargo window
            // and both station pages are hidden and the screen's Update and
            // Process are isolated -- all of which now runs through the shared
            // IScreenPanel dispatch in PatchScreenPanels.
        }

        /// <summary>
        /// Takes over exactly the branch vanilla would turn into
        /// FastTradeScreen, with the identical guards (click gating, travel
        /// lock, mission priority, visit permission, the game's own FastTrade
        /// option). Any other case falls through to vanilla untouched.
        /// </summary>
        private static bool StationSelectedPrefix(
            SpaceStationsWindow __instance, Station station)
        {
            // The trade screen needs the central management technology,
            // nothing else (autoUnlockTech grants it on every save).
            if (!_stationTradeEnabled || !IsTechnologyUnlocked()
                || station == null)
            {
                return true;
            }
            try
            {
                if (!_swAllowClicks(__instance) || _swTravel(__instance).IsInTravel)
                    return true;
                var mission = _swMissions(__instance).Get(station.Id,
                    reversed: false);
                if (mission != null)
                    return true;
                if (!StorySystem.CanVisitStation(_swTriggers(__instance),
                        _swFactions(__instance), station,
                        out var ignoreFastTrade))
                {
                    return true;
                }
                if (!SingletonMonoBehaviour<GameSettings>.Instance.FastTrade
                    || ignoreFastTrade)
                {
                    return true;
                }

                _centralStationTradeRequested = true;
                UI.BackToDefault();
                UI.Chain<FastTradeScreen>().Invoke(v =>
                {
                    v.Configure(station);
                }).HideAll().Show();
                return false;
            }
            catch (Exception e)
            {
                _centralStationTradeRequested = false;
                Debug.LogError(LogPrefix
                               + "could not open the station trade screen: " + e);
                return true;
            }
        }

        private static void FastTradeConfigurePostfix(FastTradeScreen __instance)
        {
            if (!_centralStationTradeRequested)
            {
                // A pooled screen may previously have hosted our panel.
                // Always take it down for a regular vanilla opening.
                __instance.GetComponent<CentralStationTradePanel>()
                    ?.RestoreVanillaUi();
                return;
            }
            _centralStationTradeRequested = false;
            try
            {
                var panel = __instance.GetComponent<CentralStationTradePanel>();
                if (panel == null)
                {
                    panel = __instance.gameObject
                        .AddComponent<CentralStationTradePanel>();
                }
                var cargo = _screenCargo(__instance);
                if (cargo == null || cargo.ShipCargo.Count == 0)
                    throw new InvalidOperationException(
                        "ship cargo is unavailable");
                panel.Configure(__instance, _ftStation(__instance),
                    _scFactions(__instance), _scItemsPrices(__instance),
                    _scStatistics(__instance), _scDifficulty(__instance),
                    cargo, _screenSpaceTime(__instance),
                    _screenProgression(__instance),
                    _ftStoryTriggers(__instance),
                    _activeShipCargo(__instance));
                HideVanillaTradeUi(__instance);
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix
                               + "central station trade setup failed: " + e);
            }
        }

        internal static void HideVanillaTradeUi(FastTradeScreen screen)
        {
            screen?.GetComponent<CentralStationTradePanel>()?.HideVanillaUi();
        }




    }
}