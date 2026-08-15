using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace QM_CentralManagement
{
    /// <summary>
    /// One configurable option: how to parse it from config.txt, and -- for the
    /// ones the Mod Configuration Menu shows -- how to read and write it live.
    /// </summary>
    internal sealed class ConfigOption
    {
        private readonly Action<string> _parse;

        internal string Key { get; private set; }
        internal string[] Aliases { get; private set; }
        internal string McmLabelKey { get; private set; }
        internal string McmTipKey { get; private set; }
        internal bool McmNeedsRestart { get; private set; }
        internal Func<bool> ReadFlag { get; private set; }
        internal Action<bool> WriteFlag { get; private set; }

        /// <summary>Options with no MCM label stay config.txt-only.</summary>
        internal bool InMcm => McmLabelKey != null && ReadFlag != null;

        private ConfigOption(string key, Action<string> parse)
        {
            Key = key;
            _parse = parse;
            Aliases = EmptyAliases;
        }

        private static readonly string[] EmptyAliases = new string[0];

        internal bool Matches(string key)
        {
            if (string.Equals(Key, key, StringComparison.OrdinalIgnoreCase))
                return true;
            foreach (var alias in Aliases)
            {
                if (string.Equals(alias, key, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        internal void Parse(string value)
        {
            _parse(value);
        }

        // ------------------------------------------------------------------
        // Builders. Each owns its own validation and warning text, so a new
        // option cannot silently skip the range check the others all do.
        // ------------------------------------------------------------------

        internal static ConfigOption Flag(string key, Func<bool> read,
            Action<bool> write, string mcmLabelKey = null,
            string mcmTipKey = null, bool mcmNeedsRestart = false,
            params string[] aliases)
        {
            var option = new ConfigOption(key, value =>
            {
                if (bool.TryParse(value, out var parsed))
                    write(parsed);
                else
                    Warn(key + " must be true or false.");
            })
            {
                ReadFlag = read,
                WriteFlag = write,
                McmLabelKey = mcmLabelKey,
                McmTipKey = mcmTipKey,
                McmNeedsRestart = mcmNeedsRestart,
            };
            if (aliases != null && aliases.Length > 0)
                option.Aliases = aliases;
            return option;
        }

        internal static ConfigOption Count(string key, int min, int max,
            Func<int> read, Action<int> write)
        {
            return new ConfigOption(key, value =>
            {
                if (int.TryParse(value, NumberStyles.Integer,
                        CultureInfo.InvariantCulture, out var parsed)
                    && parsed >= min && parsed <= max)
                {
                    write(parsed);
                    return;
                }
                Warn(key + " must be between " + min + " and " + max
                     + "; keeping " + read() + ".");
            });
        }

        internal static ConfigOption Decimal(string key, float min, float max,
            Func<float> read, Action<float> write)
        {
            return new ConfigOption(key, value =>
            {
                if (float.TryParse(value, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out var parsed)
                    && parsed >= min && parsed <= max)
                {
                    write(parsed);
                    return;
                }
                Warn(key + " must be between " + min + " and " + max
                     + "; keeping " + read() + ".");
            });
        }

        /// <summary>
        /// A Unity KeyCode name. <paramref name="allowNone"/> is the difference
        /// between the spaceship shortcut, which config.txt documents as
        /// disable-able with "None", and the trade panel shortcuts, where None
        /// would just silently kill the binding.
        /// </summary>
        internal static ConfigOption Key_(string key, bool allowNone,
            Func<KeyCode> read, Action<KeyCode> write)
        {
            return new ConfigOption(key, value =>
            {
                if (Enum.TryParse<KeyCode>(value, true, out var parsed)
                    && (allowNone || parsed != KeyCode.None))
                {
                    write(parsed);
                    return;
                }
                Warn(key + " '" + value + "' is not a valid Unity KeyCode"
                     + (allowNone ? "" : " (and None is not allowed here)")
                     + "; keeping " + read() + ".");
            });
        }

        private static void Warn(string message)
        {
            Debug.LogWarning(Plugin.LogPrefix + message);
        }
    }

    public static partial class Plugin
    {
        /// <summary>
        /// Every option this mod has, in one table.
        ///
        /// It used to take five edits in three files to add one: the dispatch
        /// chain in ParseConfigLines, a second chain in ParseStationTradeConfig,
        /// then a key constant, a ConfigValue, a read branch and a write line in
        /// McmIntegration. Everything below reads this list instead.
        ///
        /// Built lazily: several of the getters touch fields whose initialisers
        /// run in other partial-class files, and a static field initialiser
        /// ordering bug there would be invisible.
        /// </summary>
        private static List<ConfigOption> _configOptions;

        internal static List<ConfigOption> ConfigOptions =>
            _configOptions ?? (_configOptions = BuildConfigOptions());

        private static List<ConfigOption> BuildConfigOptions()
        {
            return new List<ConfigOption>
            {
                ConfigOption.Flag("debugLogging",
                    () => _debugLogging, v => _debugLogging = v),
                ConfigOption.Key_("shortcutKey", allowNone: true,
                    () => _centralShortcutKey, v => _centralShortcutKey = v),

                // --- pre-departure ship loadout strip ---------------------
                ConfigOption.Flag("shipLoadouts",
                    () => ShipLoadoutsEnabled, v => ShipLoadoutsEnabled = v),
                ConfigOption.Decimal("loadoutBarOffsetY", -100f, 100f,
                    () => LoadoutBarOffsetY, v => LoadoutBarOffsetY = v),

                // --- station trade screen ----------------------------------
                ConfigOption.Flag("stationTrade",
                    () => _stationTradeEnabled, v => _stationTradeEnabled = v,
                    "qmcentral.mcm.stationTrade",
                    "qmcentral.mcm.stationTrade.tip", true),
                ConfigOption.Flag("autoUnlockTech",
                    () => _autoUnlockTech, v => _autoUnlockTech = v,
                    "qmcentral.mcm.autoUnlockTech",
                    "qmcentral.mcm.autoUnlockTech.tip"),
                // buyConfirm / sellConfirm predate the combined single-step
                // trade; both now mean the one confirmation.
                ConfigOption.Flag("tradeConfirm",
                    () => _tradeConfirm, v => _tradeConfirm = v,
                    "qmcentral.mcm.tradeConfirm",
                    "qmcentral.mcm.tradeConfirm.tip", false,
                    "buyConfirm", "sellConfirm"),
                ConfigOption.Flag("debugTradeLayout",
                    () => _debugTradeLayout, v => _debugTradeLayout = v,
                    "qmcentral.mcm.debugTradeLayout",
                    "qmcentral.mcm.debugTradeLayout.tip"),

                ConfigOption.Count("quantityShiftStep", 1, 9999,
                    () => _quantityShiftStep, v => _quantityShiftStep = v),
                ConfigOption.Count("quantityCtrlStep", 1, 9999,
                    () => _quantityCtrlStep, v => _quantityCtrlStep = v),
                ConfigOption.Count("quantityCtrlShiftStep", 1, 9999,
                    () => _quantityCtrlShiftStep,
                    v => _quantityCtrlShiftStep = v),

                ConfigOption.Key_("shortcutTogglePane", false,
                    () => _shortcutTogglePane, v => _shortcutTogglePane = v),
                ConfigOption.Key_("shortcutPrevPage", false,
                    () => _shortcutPrevPage, v => _shortcutPrevPage = v),
                ConfigOption.Key_("shortcutNextPage", false,
                    () => _shortcutNextPage, v => _shortcutNextPage = v),
                ConfigOption.Key_("shortcutTrade", false,
                    () => _shortcutTrade, v => _shortcutTrade = v),
                ConfigOption.Key_("shortcutClearCart", false,
                    () => _shortcutClearCart, v => _shortcutClearCart = v),
                ConfigOption.Key_("shortcutSelectAll", false,
                    () => _shortcutSelectAll, v => _shortcutSelectAll = v),
            };
        }

        private static ConfigOption FindConfigOption(string key)
        {
            foreach (var option in ConfigOptions)
            {
                if (option.Matches(key))
                    return option;
            }
            return null;
        }
    }
}
