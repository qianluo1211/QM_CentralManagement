using System;
using System.Collections.Generic;
using System.IO;
using MGSC;
using ModConfigMenu;
using ModConfigMenu.Contracts;
using ModConfigMenu.Objects;
using UnityEngine;

// The MCM assembly declares its ConfigStoredDelegate internal; Unity's Mono
// honours the IgnoresAccessChecksTo trick (the same one MCM itself uses
// against Assembly-CSharp), so internal MCM types are usable from mods.
[assembly: System.Runtime.CompilerServices.IgnoresAccessChecksTo("MCM")]

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    internal sealed class IgnoresAccessChecksToAttribute : Attribute
    {
        public IgnoresAccessChecksToAttribute(string assemblyName)
        {
        }
    }
}

namespace QM_CentralManagement
{
    /// <summary>
    /// Crynano's Mod Configuration Menu integration (soft dependency).
    ///
    /// The mod registers its options with MCM through the public API; when
    /// the player saves in the MCM screen the values are applied at once and
    /// persisted into the mod's own mcm_settings.txt (plain key=value), so
    /// they also survive sessions where MCM is not subscribed. Everything is
    /// wrapped so a missing MCM degrades to config.txt-only behaviour.
    /// </summary>
    public static partial class Plugin
    {
        // Display name shown in MCM's mod list (plain text, not localized).
        private const string McmModName = "Central Management";
        // MCM renders headers/labels/tooltips through the game's
        // LocalizableLabel, i.e. Localization.Get on the string, so passing
        // localization keys instead of hardcoded text makes every entry
        // follow the game language and refresh on language switch.
        private const string McmHeaderKey = "qmcentral.mcm.header";

        private const string McmAutoUnlockKey = "autoUnlockTech";
        private const string McmStationTradeKey = "stationTrade";
        private const string McmTradeConfirmKey = "tradeConfirm";
        private const string McmDebugLayoutKey = "debugTradeLayout";

        private static bool _mcmRegistered;
        private static bool _mcmBroken;

        internal static bool AutoUnlockTech => _autoUnlockTech;

        internal static void RegisterWithMcm()
        {
            if (_mcmRegistered || _mcmBroken)
                return;
            try
            {
                var config = new List<IConfigValue>
                {
                    new ConfigValue(McmAutoUnlockKey, _autoUnlockTech,
                        McmHeaderKey, false,
                        "qmcentral.mcm.autoUnlockTech.tip",
                        "qmcentral.mcm.autoUnlockTech"),
                    new ConfigValue(McmStationTradeKey,
                        _stationTradeEnabled,
                        McmHeaderKey, true,
                        "qmcentral.mcm.stationTrade.tip",
                        "qmcentral.mcm.stationTrade"),
                    new ConfigValue(McmTradeConfirmKey, _tradeConfirm,
                        McmHeaderKey, false,
                        "qmcentral.mcm.tradeConfirm.tip",
                        "qmcentral.mcm.tradeConfirm"),
                    new ConfigValue(McmDebugLayoutKey, _debugTradeLayout,
                        McmHeaderKey, false,
                        "qmcentral.mcm.debugTradeLayout.tip",
                        "qmcentral.mcm.debugTradeLayout"),
                };
                // The delegate is declared internal by MCM, so it must be
                // constructed explicitly (method group conversion does not
                // see past the accessibility).
                var callback =
                    new ModConfigMenu.ModConfigMenuAPI.ConfigStoredDelegate(
                        OnMcmConfigSaved);
                ModConfigMenuAPI.RegisterModConfig(McmModName, config,
                    callback);
                _mcmRegistered = true;
                Debug.Log(LogPrefix + "registered with Mod Configuration Menu.");
            }
            catch (Exception e)
            {
                _mcmBroken = true;
                Debug.LogWarning(LogPrefix
                                 + "MCM integration unavailable (continuing with config.txt only): "
                                 + e.Message);
            }
        }

        private static bool OnMcmConfigSaved(
            Dictionary<string, object> config, out string error)
        {
            error = null;
            try
            {
                if (config.TryGetValue(McmAutoUnlockKey, out var v))
                    _autoUnlockTech = Convert.ToBoolean(v);
                if (config.TryGetValue(McmStationTradeKey, out var v3))
                    _stationTradeEnabled = Convert.ToBoolean(v3);
                if (config.TryGetValue(McmTradeConfirmKey, out var v4))
                    _tradeConfirm = Convert.ToBoolean(v4);
                if (config.TryGetValue(McmDebugLayoutKey, out var v5))
                    _debugTradeLayout = Convert.ToBoolean(v5);
                PersistMcmSettings();
                if (GameState?.Get<MagnumProgression>() != null)
                    TryAutoUnlockTech();
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }

        /// <summary>
        /// The mod's own copy of the MCM settings, so they apply even when
        /// MCM is not subscribed on the next launch. Read after config.txt
        /// so the in-game edits always win.
        /// </summary>
        internal static void LoadMcmSettings()
        {
            var path = McmSettingsPath();
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;
            try
            {
                foreach (var raw in File.ReadAllLines(path))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#"))
                        continue;
                    var separator = line.IndexOf('=');
                    if (separator < 0)
                        continue;
                    var key = line.Substring(0, separator).Trim();
                    var value = line.Substring(separator + 1).Trim();
                    if (!bool.TryParse(value, out var parsed))
                        continue;
                    if (key.Equals(McmAutoUnlockKey,
                            StringComparison.OrdinalIgnoreCase))
                        _autoUnlockTech = parsed;
                    else if (key.Equals(McmStationTradeKey,
                                 StringComparison.OrdinalIgnoreCase))
                        _stationTradeEnabled = parsed;
                    else if (key.Equals(McmTradeConfirmKey,
                                 StringComparison.OrdinalIgnoreCase))
                        _tradeConfirm = parsed;
                    else if (key.Equals(McmDebugLayoutKey,
                                 StringComparison.OrdinalIgnoreCase))
                        _debugTradeLayout = parsed;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix
                                 + "could not load mcm_settings.txt: "
                                 + e.Message);
            }
        }

        private static void PersistMcmSettings()
        {
            var path = McmSettingsPath();
            if (string.IsNullOrEmpty(path))
                return;
            try
            {
                var lines = new[]
                {
                    "# Written by QM_CentralManagement via the Mod Configuration Menu.",
                    McmAutoUnlockKey + "=" + _autoUnlockTech,
                    McmStationTradeKey + "=" + _stationTradeEnabled,
                    McmTradeConfirmKey + "=" + _tradeConfirm,
                    McmDebugLayoutKey + "=" + _debugTradeLayout,
                };
                File.WriteAllLines(path, lines);
            }
            catch (Exception e)
            {
                Debug.LogWarning(LogPrefix
                                 + "could not write mcm_settings.txt: "
                                 + e.Message);
            }
        }

        private static string McmSettingsPath()
        {
            if (string.IsNullOrEmpty(_modContentPath))
                return null;
            return Path.Combine(_modContentPath, "mcm_settings.txt");
        }
    }
}
