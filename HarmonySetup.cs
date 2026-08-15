using System;
using HarmonyLib;
using MGSC;
using UnityEngine;

namespace QM_CentralManagement
{
    public static partial class Plugin
    {
        private static void PatchGame()
        {
            var harmony = new Harmony("QM_CentralManagement");
            TryPatch("localization", () => PatchLocalization(harmony));
            TryPatch("spaceship action button", () => PatchSpaceshipButton(harmony));
            TryPatch("screen panel dispatch", () => PatchScreenPanels(harmony));
            TryPatch("central arsenal mode", () => PatchCentralArsenal(harmony));
            TryPatch("inventory view guards", () => PatchInventoryViewGuards(harmony));
            TryPatch("drag state safety", () => PatchDragStateSafety(harmony));
            TryPatch("search input isolation", () => PatchSearchInputIsolation(harmony));
            TryPatch("split-stack mouse wheel", () => PatchSplitStackWheel(harmony));
            TryPatch("ship loadout bar", () => PatchShipLoadout(harmony));
            TryPatch("station trade screen", () => PatchStationTrade(harmony));
        }

        private static void TryPatch(string feature, Action action)
        {
            try
            {
                action();
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + feature + " patch failed: " + e);
            }
        }

        private static void PatchRequired(Harmony harmony, Type targetType,
            string methodName, string prefix = null, string postfix = null,
            Type[] argumentTypes = null)
        {
            var original = AccessTools.Method(targetType, methodName,
                argumentTypes);
            if (original == null)
                throw new MissingMethodException(targetType.FullName, methodName);
            harmony.Patch(original,
                prefix: prefix == null
                    ? null
                    : new HarmonyMethod(typeof(Plugin), prefix),
                postfix: postfix == null
                    ? null
                    : new HarmonyMethod(typeof(Plugin), postfix));
        }
    }
}
