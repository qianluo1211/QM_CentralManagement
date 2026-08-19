using System;
using System.Collections.Generic;
using HarmonyLib;
using MGSC;
using UnityEngine;

namespace QM_CentralManagement
{
    /// <summary>
    /// Soft dependency on Traveler's Global Currency mod (LoC_GlobalCurrency).
    ///
    /// That mod stores every faction's trade points on Magnum and Harmony-
    /// patches the VANILLA station pages so they read Magnum's wallet. This
    /// panel never goes through those pages -- it reads Faction.PlayerTradePoints
    /// itself -- so without this bridge the header shows 0 and the TRADE
    /// button stays locked even though BuyStationItems / SellItems already
    /// debit and credit Magnum.
    ///
    /// Reached by reflection. The two mods ship separately; a compile-time
    /// reference would turn a missing Global Currency into a load failure.
    /// Missing type, missing fields, or any exception all degrade to "use
    /// the station faction's own points", which is vanilla.
    /// </summary>
    internal static class GlobalCurrencyBridge
    {
        private const string PluginTypeName = "GlobalCurrency.Plugin";
        private const string DefaultFactionId = "Magnum";
        private const string TerroristDisableKey =
            "Disable_Terrorist_GlobalCurrency_On";

        // Same three alliances Global Currency hardcodes. Used only when
        // their public list cannot be bound, so a PE station with that MCM
        // option on still spends the station wallet rather than Magnum.
        private static readonly string[] FallbackLegitAlliances =
        {
            "Hexarchy", "Corporation", "Pirates"
        };

        private static bool _resolved;
        private static bool _present;
        private static string _globalFactionId = DefaultFactionId;
        private static HashSet<string> _legitAlliances;
        private static bool _terroristDisable;

        internal static void Warmup()
        {
            Resolve();
        }

        private static void Resolve()
        {
            if (_resolved)
                return;
            _resolved = true;
            try
            {
                var type = AccessTools.TypeByName(PluginTypeName);
                if (type == null)
                {
                    Plugin.DebugLog("LoC_GlobalCurrency not present; station "
                                    + "trade uses each faction's own points.");
                    return;
                }

                var factionField = AccessTools.Field(type,
                    "global_currency_faction");
                var id = factionField?.GetValue(null) as string;
                if (!string.IsNullOrEmpty(id))
                    _globalFactionId = id;

                var allianceField = AccessTools.Field(type,
                    "legit_faction_alliance");
                if (allianceField?.GetValue(null) is IEnumerable<string> list)
                    _legitAlliances = new HashSet<string>(list);
                else
                    _legitAlliances = new HashSet<string>(
                        FallbackLegitAlliances);

                TryReadTerroristDisable(type);

                _present = true;
                Debug.Log(Plugin.LogPrefix
                          + "LoC_GlobalCurrency detected; station trade "
                          + "wallet = " + _globalFactionId
                          + (_terroristDisable
                              ? " (PE factions keep their own points)."
                              : "."));
            }
            catch (Exception e)
            {
                _present = false;
                Debug.LogWarning(Plugin.LogPrefix
                                 + "could not bind to LoC_GlobalCurrency: "
                                 + e);
            }
        }

        /// <summary>
        /// Their MCM flag is read once at their patch-class load, and they
        /// ask for a restart after changing it, so caching here is enough.
        /// </summary>
        private static void TryReadTerroristDisable(Type pluginType)
        {
            try
            {
                var config = AccessTools.Property(pluginType, "ConfigGeneral")
                    ?.GetValue(null);
                if (config == null)
                    return;
                var modData = AccessTools.Field(config.GetType(), "ModData")
                    ?.GetValue(config);
                if (modData == null)
                    return;
                var generic = AccessTools.Method(modData.GetType(),
                    "GetConfigValue");
                if (generic == null)
                    return;
                var closed = generic.MakeGenericMethod(typeof(bool));
                var value = closed.Invoke(modData,
                    new object[] { TerroristDisableKey, false });
                _terroristDisable = value is bool flag && flag;
            }
            catch (Exception e)
            {
                Debug.LogWarning(Plugin.LogPrefix
                                 + "could not read Global Currency's PE "
                                 + "option; treating it as off: " + e);
            }
        }

        /// <summary>
        /// The faction whose PlayerTradePoints this station currently
        /// spends and displays. Magnum when Global Currency is handling
        /// this alliance, otherwise the station owner.
        /// </summary>
        internal static Faction WalletFaction(Factions factions,
            Faction stationFaction)
        {
            if (stationFaction == null)
                return null;
            Resolve();
            if (!_present || factions == null)
                return stationFaction;
            try
            {
                if (_terroristDisable
                    && _legitAlliances != null
                    && !_legitAlliances.Contains(
                        stationFaction.CurrentAlliance ?? string.Empty))
                {
                    return stationFaction;
                }
                return factions.Get(_globalFactionId, false) ?? stationFaction;
            }
            catch (Exception e)
            {
                Debug.LogWarning(Plugin.LogPrefix
                                 + "Global Currency wallet lookup failed: "
                                 + e);
                return stationFaction;
            }
        }

        internal static int GetTradePoints(Factions factions,
            Faction stationFaction)
        {
            var wallet = WalletFaction(factions, stationFaction);
            return wallet != null ? wallet.PlayerTradePoints : 0;
        }
    }
}
