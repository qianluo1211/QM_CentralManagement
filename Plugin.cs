using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using MGSC;
using UnityEngine;

namespace QM_CentralManagement
{
    public static partial class Plugin
    {
        internal const string TechId = "qm_central_management";
        internal const string LogPrefix = "[CentralManagement] ";

        private static readonly List<UnityEngine.Object> OwnedAssets =
            new List<UnityEngine.Object>();

        private static bool _ready;
        private static bool _debugLogging;
        private static string _modContentPath;
        // Obsolete since batch recycling moved to the game's ConfirmDialogWindow.
        // Still parsed so an existing config.txt does not start reporting an
        // unknown option, but it no longer affects anything.
        private static float _recycleConfirmSeconds = 4f;
        private static KeyCode _centralShortcutKey = KeyCode.C;

        internal static State GameState { get; private set; }
        internal static Sprite FastAccessSprite { get; private set; }
        internal static Sprite ActionNormalSprite { get; private set; }
        internal static Sprite ActionHoverSprite { get; private set; }
        internal static Sprite ActionPressedSprite { get; private set; }
        internal static KeyCode CentralShortcutKey => _centralShortcutKey;

        [Hook(ModHookType.AfterConfigsLoaded)]
        public static void AfterConfigsLoaded(IModContext context)
        {
            try
            {
                LoadConfig(context.ModContentPath);
                RegisterTechnology(context.ModContentPath);
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + "technology registration failed: " + e);
            }
        }

        [Hook(ModHookType.AfterBootstrap)]
        public static void AfterBootstrap(IModContext context)
        {
            GameState = context.State;
            try
            {
                PatchGame();
                _ready = true;
                Debug.Log(LogPrefix + "ready. Technology = " + TechId + ".");
                RegisterWithMcm();
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + "initialization failed: " + e);
            }
        }

        [Hook(ModHookType.AfterSaveLoaded)]
        public static void AfterSaveLoaded(IModContext context)
        {
            GameState = context.State;
            RegisterWithMcm();
            TryAutoUnlockTech();
        }

        // A brand new game never goes through AfterSaveLoaded (see
        // GameModeStateMachine.ProcessStartGame: the new-game branch creates
        // the components and jumps straight into SpaceGameMode). SpaceStarted
        // fires at the end of SpaceGameMode.Run for every save, new or old,
        // so autoUnlockTech is applied there as well; AddPerk is idempotent.
        [Hook(ModHookType.SpaceStarted)]
        public static void SpaceStarted(IModContext context)
        {
            GameState = context.State;
            TryAutoUnlockTech();
        }

        /// <summary>
        /// autoUnlockTech: the Central Logistics Matrix perk is granted to
        /// every loaded save without researching it. Idempotent -- the game
        /// simply ignores an already purchased perk.
        /// </summary>
        private static void TryAutoUnlockTech()
        {
            if (!_autoUnlockTech)
                return;
            try
            {
                var progression = GameState?.Get<MagnumProgression>();
                if (progression == null)
                    return;
                if (!progression.IsPerkPurchased(TechId))
                    progression.AddPerk(TechId);
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix
                               + "could not auto-unlock the technology: " + e);
            }
        }

        internal static bool IsTechnologyUnlocked()
        {
            return _ready
                   && GameState?.Get<MagnumProgression>()
                       ?.IsPerkPurchased(TechId) == true;
        }

        internal static void DebugLog(string message)
        {
            if (_debugLogging)
                Debug.Log(LogPrefix + message);
        }

        private static void LoadConfig(string modContentPath)
        {
            _modContentPath = modContentPath;
            var path = Path.Combine(modContentPath ?? ".", "config.txt");
            if (File.Exists(path))
            {
                ParseConfigLines(File.ReadAllLines(path));
            }
            // Values saved through the Mod Configuration Menu override the
            // shipped defaults from config.txt.
            LoadMcmSettings();
        }

        private static void ParseConfigLines(string[] lines)
        {
            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")
                                     || line.StartsWith("//"))
                {
                    continue;
                }

                var separator = line.IndexOf('=');
                if (separator < 0)
                    continue;
                var key = line.Substring(0, separator).Trim();
                var value = line.Substring(separator + 1).Trim();
                if (key.Equals("debugLogging",
                        StringComparison.OrdinalIgnoreCase))
                {
                    if (!bool.TryParse(value, out _debugLogging))
                    {
                        Debug.LogWarning(LogPrefix
                                         + "debugLogging must be true or false.");
                    }
                }
                else if (key.Equals("recycleConfirmSeconds",
                             StringComparison.OrdinalIgnoreCase))
                {
                    if (!float.TryParse(value, NumberStyles.Float,
                            CultureInfo.InvariantCulture, out var seconds)
                        || seconds < 1f || seconds > 15f)
                    {
                        Debug.LogWarning(LogPrefix
                                         + "recycleConfirmSeconds must be between 1 and 15; keeping "
                                         + _recycleConfirmSeconds + ".");
                    }
                    else
                    {
                        _recycleConfirmSeconds = seconds;
                    }
                }
                else if (key.Equals("shipLoadouts",
                             StringComparison.OrdinalIgnoreCase)
                         || key.Equals("loadoutBarOffsetY",
                             StringComparison.OrdinalIgnoreCase))
                {
                    ParseShipLoadoutConfig(key, value);
                }
                else if (key.Equals("shortcutKey",
                             StringComparison.OrdinalIgnoreCase))
                {
                    if (!Enum.TryParse(value, true, out KeyCode shortcut))
                    {
                        Debug.LogWarning(LogPrefix + "shortcutKey '" + value
                                         + "' is not a valid Unity KeyCode; keeping "
                                         + _centralShortcutKey + ".");
                    }
                    else
                    {
                        _centralShortcutKey = shortcut;
                    }
                }
                else if (key.Equals("stationTrade",
                             StringComparison.OrdinalIgnoreCase)
                         || key.Equals("tradeConfirm",
                             StringComparison.OrdinalIgnoreCase)
                         || key.Equals("buyConfirm",
                             StringComparison.OrdinalIgnoreCase)
                         || key.Equals("sellConfirm",
                             StringComparison.OrdinalIgnoreCase)
                         || key.Equals("debugTradeLayout",
                             StringComparison.OrdinalIgnoreCase)
                         || key.Equals("preventTradeArbitrage",
                             StringComparison.OrdinalIgnoreCase)
                         || key.Equals("quantityShiftStep",
                             StringComparison.OrdinalIgnoreCase)
                         || key.Equals("quantityCtrlStep",
                             StringComparison.OrdinalIgnoreCase)
                         || key.Equals("quantityCtrlShiftStep",
                             StringComparison.OrdinalIgnoreCase)
                         || key.Equals("shortcutTogglePane",
                             StringComparison.OrdinalIgnoreCase)
                         || key.Equals("shortcutPrevPage",
                             StringComparison.OrdinalIgnoreCase)
                         || key.Equals("shortcutNextPage",
                             StringComparison.OrdinalIgnoreCase)
                         || key.Equals("shortcutTrade",
                             StringComparison.OrdinalIgnoreCase)
                         || key.Equals("shortcutClearCart",
                             StringComparison.OrdinalIgnoreCase)
                         || key.Equals("shortcutSelectAll",
                             StringComparison.OrdinalIgnoreCase)
                         || key.Equals("tradeWithoutTech",
                             StringComparison.OrdinalIgnoreCase)
                         || key.Equals("autoUnlockTech",
                             StringComparison.OrdinalIgnoreCase))
                {
                    ParseStationTradeConfig(key, value);
                }
                else
                {
                    Debug.LogWarning(LogPrefix + "unknown config key '"
                                     + key + "'.");
                }
            }
        }
    }
}
