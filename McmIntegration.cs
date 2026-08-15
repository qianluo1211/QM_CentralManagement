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

        private static bool _mcmRegistered;
        private static bool _mcmBroken;

        internal static bool AutoUnlockTech => _autoUnlockTech;

        internal static void RegisterWithMcm()
        {
            if (_mcmRegistered || _mcmBroken)
                return;
            try
            {
                var config = new List<IConfigValue>();
                foreach (var option in ConfigOptions)
                {
                    if (!option.InMcm)
                        continue;
                    config.Add(new ConfigValue(option.Key,
                        option.ReadFlag(), McmHeaderKey,
                        option.McmNeedsRestart, option.McmTipKey,
                        option.McmLabelKey));
                }
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
                foreach (var option in ConfigOptions)
                {
                    if (option.InMcm
                        && config.TryGetValue(option.Key, out var value))
                    {
                        option.WriteFlag(Convert.ToBoolean(value));
                    }
                }
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
                    var option = FindConfigOption(key);
                    if (option != null && option.InMcm)
                        option.Parse(value);
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
                var lines = new List<string>
                {
                    "# Written by QM_CentralManagement via the Mod Configuration Menu.",
                };
                foreach (var option in ConfigOptions)
                {
                    if (option.InMcm)
                        lines.Add(option.Key + "=" + option.ReadFlag());
                }
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
