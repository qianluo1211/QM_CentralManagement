using System;
using HarmonyLib;
using MGSC;
using UnityEngine;

namespace QM_CentralManagement
{
    /// <summary>
    /// A mod panel that takes over a ScreenWithShipCargo.
    ///
    /// Implemented by MonoBehaviours attached to the screen itself, so the
    /// lifecycle patches find them with GetComponents and never need to know
    /// which panels exist.
    /// </summary>
    internal interface IScreenPanel
    {
        /// <summary>
        /// True while this panel is in charge: the vanilla screen's own Update
        /// and Process are suppressed, because the widgets they drive are
        /// hidden underneath the panel.
        /// </summary>
        bool OwnsScreen { get; }

        /// <summary>The pooled screen was shown again.</summary>
        void OnScreenEnabled();

        /// <summary>The pooled screen was hidden.</summary>
        void OnScreenDisabled();
    }

    /// <summary>
    /// The single set of patches on ScreenWithShipCargo's lifecycle.
    ///
    /// These four methods used to be patched by every feature separately --
    /// OnDisable carried three independent prefixes, and Update and Process
    /// each carried two that both decided whether to suppress the original,
    /// with nothing but registration order deciding who answered first. There
    /// is now one patch per method and one definition of "who owns this
    /// screen", so adding a panel means implementing IScreenPanel, not adding
    /// a fourth prefix to a method that already has three.
    /// </summary>
    public static partial class Plugin
    {
        private static void PatchScreenPanels(Harmony harmony)
        {
            PatchRequired(harmony, typeof(ScreenWithShipCargo), "OnEnable",
                postfix: nameof(ScreenPanelOnEnablePostfix),
                argumentTypes: Type.EmptyTypes);
            PatchRequired(harmony, typeof(ScreenWithShipCargo), "OnDisable",
                prefix: nameof(ScreenPanelOnDisablePrefix),
                argumentTypes: Type.EmptyTypes);
            PatchRequired(harmony, typeof(ScreenWithShipCargo), "Update",
                prefix: nameof(ScreenPanelUpdatePrefix),
                argumentTypes: Type.EmptyTypes);
            // Process is where the cargo screen claims Alpha1..Alpha9 for its
            // own tab strip (TryProcessTabSelectionInput). UI.Process still
            // routes to it while a panel is up, so without this, pressing 1-9
            // switched the tabs of a strip that is hidden. Its other two jobs,
            // the equip hotkey and drop-to-tab, target that same strip.
            PatchRequired(harmony, typeof(ScreenWithShipCargo),
                nameof(ScreenWithShipCargo.Process),
                prefix: nameof(ScreenPanelProcessPrefix),
                argumentTypes: new[] { typeof(bool).MakeByRefType() });
        }

        private static bool AnyPanelOwns(ScreenWithShipCargo screen)
        {
            if (screen == null)
                return false;
            foreach (var panel in screen.GetComponents<IScreenPanel>())
            {
                if (panel != null && panel.OwnsScreen)
                    return true;
            }
            return false;
        }

        private static void ScreenPanelOnEnablePostfix(
            ScreenWithShipCargo __instance)
        {
            foreach (var panel in __instance.GetComponents<IScreenPanel>())
            {
                if (panel == null || !panel.OwnsScreen)
                    continue;
                try
                {
                    panel.OnScreenEnabled();
                }
                catch (Exception e)
                {
                    Debug.LogError(LogPrefix + "panel enable failed: " + e);
                }
            }
        }

        private static void ScreenPanelOnDisablePrefix(
            ScreenWithShipCargo __instance)
        {
            // Unconditional, unlike the enable path: a panel that is being
            // torn down has to run its cleanup even though it no longer
            // reports itself as the owner.
            foreach (var panel in __instance.GetComponents<IScreenPanel>())
            {
                if (panel == null)
                    continue;
                try
                {
                    panel.OnScreenDisabled();
                }
                catch (Exception e)
                {
                    Debug.LogError(LogPrefix + "panel cleanup failed: " + e);
                }
            }
            try
            {
                ShipLoadoutBar.HideFor(__instance);
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix
                               + "ship loadout cleanup failed: " + e);
            }
        }

        private static bool ScreenPanelUpdatePrefix(
            ScreenWithShipCargo __instance)
        {
            try
            {
                return !AnyPanelOwns(__instance);
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + "update isolation failed: " + e);
                return true;
            }
        }

        private static bool ScreenPanelProcessPrefix(
            ScreenWithShipCargo __instance, out bool interruptProcessing)
        {
            // false means "nothing consumed here", so the rest of the UI input
            // chain carries on exactly as it would if this screen had no
            // hotkeys -- only the cargo screen's own bindings are declined.
            interruptProcessing = false;
            try
            {
                return !AnyPanelOwns(__instance);
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + "process isolation failed: " + e);
                return true;
            }
        }
    }
}
