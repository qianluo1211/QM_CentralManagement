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

        internal static void ParseStationTradeConfig(string key, string value)
        {
            if (key.Equals("stationTrade", StringComparison.OrdinalIgnoreCase))
            {
                if (!bool.TryParse(value, out _stationTradeEnabled))
                {
                    Debug.LogWarning(LogPrefix
                                     + "stationTrade must be true or false.");
                }
            }
            else if (key.Equals("tradeConfirm",
                         StringComparison.OrdinalIgnoreCase)
                     || key.Equals("buyConfirm",
                         StringComparison.OrdinalIgnoreCase)
                     || key.Equals("sellConfirm",
                         StringComparison.OrdinalIgnoreCase))
            {
                // buyConfirm / sellConfirm are kept parsed so old config
                // files do not warn; both now map to the combined trade
                // confirmation.
                if (!bool.TryParse(value, out _tradeConfirm))
                {
                    Debug.LogWarning(LogPrefix
                                     + "tradeConfirm must be true or false.");
                }
            }
            else if (key.Equals("debugTradeLayout",
                         StringComparison.OrdinalIgnoreCase))
            {
                if (!bool.TryParse(value, out _debugTradeLayout))
                {
                    Debug.LogWarning(LogPrefix
                                     + "debugTradeLayout must be true or false.");
                }
            }
            else if (key.Equals("preventTradeArbitrage",
                         StringComparison.OrdinalIgnoreCase))
            {
                // Obsolete: the anti-arbitrage floor was removed, so prices
                // follow the vanilla formulas again. Still parsed so an old
                // config.txt does not report an unknown option.
            }
            else if (key.Equals("tradeWithoutTech",
                         StringComparison.OrdinalIgnoreCase))
            {
                // Obsolete: the trade screen is now gated on the central
                // management technology alone (autoUnlockTech grants that
                // technology). Still parsed so an old config.txt does not
                // report an unknown option.
            }
            else if (key.Equals("autoUnlockTech",
                         StringComparison.OrdinalIgnoreCase))
            {
                if (!bool.TryParse(value, out _autoUnlockTech))
                {
                    Debug.LogWarning(LogPrefix
                                     + "autoUnlockTech must be true or false.");
                }
            }
            else if (key.Equals("quantityShiftStep",
                         StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, out _quantityShiftStep)
                    || _quantityShiftStep < 1 || _quantityShiftStep > 9999)
                {
                    Debug.LogWarning(LogPrefix
                                     + "quantityShiftStep must be between 1 and 9999; keeping "
                                     + _quantityShiftStep + ".");
                }
            }
            else if (key.Equals("quantityCtrlStep",
                         StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, out _quantityCtrlStep)
                    || _quantityCtrlStep < 1 || _quantityCtrlStep > 9999)
                {
                    Debug.LogWarning(LogPrefix
                                     + "quantityCtrlStep must be between 1 and 9999; keeping "
                                     + _quantityCtrlStep + ".");
                }
            }
            else if (key.Equals("quantityCtrlShiftStep",
                         StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(value, out _quantityCtrlShiftStep)
                    || _quantityCtrlShiftStep < 1
                    || _quantityCtrlShiftStep > 9999)
                {
                    Debug.LogWarning(LogPrefix
                                     + "quantityCtrlShiftStep must be between 1 and 9999; keeping "
                                     + _quantityCtrlShiftStep + ".");
                }
            }
            else if (key.Equals("shortcutTogglePane",
                         StringComparison.OrdinalIgnoreCase))
            {
                ParseShortcutKey(value, ref _shortcutTogglePane,
                    "shortcutTogglePane");
            }
            else if (key.Equals("shortcutPrevPage",
                         StringComparison.OrdinalIgnoreCase))
            {
                ParseShortcutKey(value, ref _shortcutPrevPage,
                    "shortcutPrevPage");
            }
            else if (key.Equals("shortcutNextPage",
                         StringComparison.OrdinalIgnoreCase))
            {
                ParseShortcutKey(value, ref _shortcutNextPage,
                    "shortcutNextPage");
            }
            else if (key.Equals("shortcutTrade",
                         StringComparison.OrdinalIgnoreCase))
            {
                ParseShortcutKey(value, ref _shortcutTrade, "shortcutTrade");
            }
            else if (key.Equals("shortcutClearCart",
                         StringComparison.OrdinalIgnoreCase))
            {
                ParseShortcutKey(value, ref _shortcutClearCart,
                    "shortcutClearCart");
            }
            else if (key.Equals("shortcutSelectAll",
                         StringComparison.OrdinalIgnoreCase))
            {
                ParseShortcutKey(value, ref _shortcutSelectAll,
                    "shortcutSelectAll");
            }
        }

        private static void ParseShortcutKey(string value, ref KeyCode target,
            string name)
        {
            if (!Enum.TryParse<KeyCode>(value, true, out var parsed)
                || parsed == KeyCode.None)
            {
                Debug.LogWarning(LogPrefix + name + " '" + value
                                 + "' is not a valid Unity KeyCode; keeping "
                                 + target + ".");
                return;
            }
            target = parsed;
        }

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

            // The panel covers the whole screen (first-release layout), so
            // the vanilla cargo window and both station pages are hidden and
            // the screen's Update/Process are isolated exactly like central
            // mode isolates its host screen.
            PatchRequired(harmony, typeof(ScreenWithShipCargo), "OnEnable",
                postfix: nameof(StationTradeOnEnablePostfix),
                argumentTypes: Type.EmptyTypes);
            PatchRequired(harmony, typeof(ScreenWithShipCargo), "OnDisable",
                prefix: nameof(StationTradeOnDisablePrefix),
                argumentTypes: Type.EmptyTypes);
            PatchRequired(harmony, typeof(ScreenWithShipCargo), "Update",
                prefix: nameof(StationTradeUpdatePrefix),
                argumentTypes: Type.EmptyTypes);
            PatchRequired(harmony, typeof(ScreenWithShipCargo), "Process",
                prefix: nameof(StationTradeProcessPrefix),
                argumentTypes: new[] { typeof(bool).MakeByRefType() });
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

        private static void HideVanillaTradeUi(FastTradeScreen screen)
        {
            if (screen == null)
                return;
            _cargoWindow(screen)?.SetActive(false);
            _ftTabsView(screen)?.gameObject.SetActive(false);
            _ftCargoTradePage(screen)?.gameObject.SetActive(false);
            _ftExchangePage(screen)?.gameObject.SetActive(false);
        }

        private static void StationTradeOnEnablePostfix(
            ScreenWithShipCargo __instance)
        {
            if (!(__instance is FastTradeScreen screen))
                return;
            try
            {
                var panel = screen.GetComponent<CentralStationTradePanel>();
                if (panel == null || !panel.IsPanelActive)
                    return;
                HideVanillaTradeUi(screen);
                panel.ShowPanel();
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix
                               + "station trade enable failed: " + e);
            }
        }

        private static void StationTradeOnDisablePrefix(
            ScreenWithShipCargo __instance)
        {
            try
            {
                __instance.GetComponent<CentralStationTradePanel>()
                    ?.OnScreenClosed();
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix
                               + "station trade cleanup failed: " + e);
            }
        }

        private static bool StationTradeUpdatePrefix(
            ScreenWithShipCargo __instance)
        {
            try
            {
                var panel = __instance.GetComponent<CentralStationTradePanel>();
                return panel == null || !panel.IsPanelActive;
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix
                               + "station trade update isolation failed: " + e);
                return true;
            }
        }

        private static bool StationTradeProcessPrefix(
            ScreenWithShipCargo __instance, out bool interruptProcessing)
        {
            // false means "nothing consumed here", so the rest of the UI
            // input chain carries on exactly as it would without this screen.
            interruptProcessing = false;
            try
            {
                var panel = __instance.GetComponent<CentralStationTradePanel>();
                return panel == null || !panel.IsPanelActive;
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix
                               + "station trade process isolation failed: " + e);
                return true;
            }
        }
    }
}