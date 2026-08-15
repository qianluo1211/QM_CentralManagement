using System;
using System.Collections.Generic;
using HarmonyLib;
using MGSC;
using UnityEngine;

namespace QM_CentralManagement
{
    public static partial class Plugin
    {
        private static bool _centralAugmentationOpenRequested;
        private static bool _centralOpenWithOperatorPanel;
        private static AccessTools.FieldRef<ScreenWithShipCargo, GameObject>
            _cargoWindow;
        private static AccessTools.FieldRef<ScreenWithShipCargo, ItemStorage>
            _activeShipCargo;
        private static AccessTools.FieldRef<ScreenWithShipCargo, MagnumCargo>
            _screenCargo;
        private static AccessTools.FieldRef<ScreenWithShipCargo, SpaceTime>
            _screenSpaceTime;
        private static AccessTools.FieldRef<ScreenWithShipCargo, MagnumProgression>
            _screenProgression;
        private static AccessTools.FieldRef<ScreenWithShipCargo, Mercenary>
            _screenMercenary;
        private static AccessTools.FieldRef<ScreenWithShipCargo, PerkFactory>
            _screenPerkFactory;
        private static AccessTools.FieldRef<ArsenalScreen, GameObject>
            _inventoryWindow;
        private static AccessTools.FieldRef<ArsenalScreen, GameObject>
            _arsenalVestGrid;
        private static AccessTools.FieldRef<ArsenalScreen, NoPlayerInventoryView>
            _inventoryView;
        private static AccessTools.FieldRef<DragController, Action<ItemSlot>>
            _controlClickCallback;
        private static AccessTools.FieldRef<ScreenWithShipCargo, ItemSlot>
            _contextMenuItemSlot;

        private static void PatchCentralArsenal(Harmony harmony)
        {
            _cargoWindow = AccessTools.FieldRefAccess<ScreenWithShipCargo,
                GameObject>("_cargoWindow");
            _activeShipCargo = AccessTools.FieldRefAccess<ScreenWithShipCargo,
                ItemStorage>("_activeShipCargo");
            _screenCargo = AccessTools.FieldRefAccess<ScreenWithShipCargo,
                MagnumCargo>("_magnumCargo");
            _screenSpaceTime = AccessTools.FieldRefAccess<ScreenWithShipCargo,
                SpaceTime>("_spaceTime");
            _screenProgression = AccessTools.FieldRefAccess<ScreenWithShipCargo,
                MagnumProgression>("_magnumSpaceship");
            _screenMercenary = AccessTools.FieldRefAccess<ScreenWithShipCargo,
                Mercenary>("_merc");
            _screenPerkFactory = AccessTools.FieldRefAccess<ScreenWithShipCargo,
                PerkFactory>("_perkFactory");
            _inventoryWindow = AccessTools.FieldRefAccess<ArsenalScreen,
                GameObject>("_inventoryWindow");
            _arsenalVestGrid = AccessTools.FieldRefAccess<ArsenalScreen,
                GameObject>("_vestGrid");
            _inventoryView = AccessTools.FieldRefAccess<ArsenalScreen,
                NoPlayerInventoryView>("_inventoryView");
            _controlClickCallback = AccessTools.FieldRefAccess<DragController,
                Action<ItemSlot>>("_controlClickCallback");
            _contextMenuItemSlot = AccessTools.FieldRefAccess<ScreenWithShipCargo,
                ItemSlot>("_contextMenuItemSlot");

            PatchRequired(harmony, typeof(ArsenalScreen),
                nameof(ArsenalScreen.Configure),
                postfix: nameof(ArsenalConfigurePostfix),
                argumentTypes: new[] { typeof(Mercenary), typeof(bool) });
            PatchRequired(harmony, typeof(ArsenalScreen),
                nameof(ArsenalScreen.RefreshView),
                postfix: nameof(ArsenalRefreshViewPostfix),
                argumentTypes: Type.EmptyTypes);
            PatchRequired(harmony, typeof(AugmentationScreen),
                nameof(AugmentationScreen.Configure),
                postfix: nameof(AugmentationConfigurePostfix),
                argumentTypes: new[] { typeof(Mercenary) });
            PatchRequired(harmony, typeof(AugmentationScreen),
                nameof(AugmentationScreen.RefreshView),
                postfix: nameof(AugmentationRefreshViewPostfix),
                argumentTypes: Type.EmptyTypes);
            PatchRequired(harmony, typeof(ScreenWithShipCargo), "OnEnable",
                postfix: nameof(ShipCargoOnEnablePostfix),
                argumentTypes: Type.EmptyTypes);
            PatchRequired(harmony, typeof(ScreenWithShipCargo), "OnDisable",
                prefix: nameof(ShipCargoOnDisablePrefix),
                argumentTypes: Type.EmptyTypes);
            PatchRequired(harmony, typeof(ScreenWithShipCargo), "Update",
                prefix: nameof(ShipCargoUpdatePrefix),
                argumentTypes: Type.EmptyTypes);
            // Update was isolated but Process was not, and Process is where the
            // cargo screen claims Alpha1..Alpha9 for its own tab strip
            // (TryProcessTabSelectionInput). UI.Process still routes to it while
            // central mode is up, so pressing 1-9 in this panel switched the
            // tabs of the screen underneath -- a strip that is hidden here.
            // The other two things Process does, the equip hotkey and
            // drop-to-tab, target that same hidden strip.
            PatchRequired(harmony, typeof(ScreenWithShipCargo),
                nameof(ScreenWithShipCargo.Process),
                prefix: nameof(ShipCargoProcessPrefix),
                argumentTypes: new[] { typeof(bool).MakeByRefType() });
            PatchRequired(harmony, typeof(ScreenWithShipCargo),
                "ContextMenuOnSplitStackConfirmed",
                prefix: nameof(CentralSplitPrefix),
                postfix: nameof(CentralSplitPostfix),
                argumentTypes: new[] { typeof(int), typeof(int) });
        }

        private static void PatchDragStateSafety(Harmony harmony)
        {
            PatchRequired(harmony, typeof(DragController),
                nameof(DragController.CanPutInSlot),
                prefix: nameof(CanPutInSlotSafetyPrefix),
                argumentTypes: new[] { typeof(ItemSlot) });
        }

        /// <summary>
        /// Guards InventoryWeightPanel.Initialize against a null mercenary.
        ///
        /// The central panel hides the vanilla operator view while its
        /// NoPlayerInventoryView can still be subscribed to
        /// Inventory.OnWeightChanged. When a limb install changes the bare-hand
        /// weapon, the weight event reaches that view with _merc == null and
        /// vanilla throws inside AugmentationSystem.Augment -- which then
        /// aborts before its caller's RefreshView, leaving every screen stale.
        /// Skipping the update for a null mercenary is always safe: there is
        /// nothing to display.
        /// </summary>
        private static void PatchInventoryWeightGuard(Harmony harmony)
        {
            PatchRequired(harmony, typeof(InventoryWeightPanel),
                nameof(InventoryWeightPanel.Initialize),
                prefix: nameof(InventoryWeightPanelInitializePrefix),
                argumentTypes: new[] { typeof(Mercenary) });
        }

        private static bool InventoryWeightPanelInitializePrefix(
            Mercenary merc)
        {
            try
            {
                return merc != null;
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + "weight panel guard failed: " + e);
                return true;
            }
        }

        private static bool CanPutInSlotSafetyPrefix(DragController __instance,
            ItemSlot slot, ref bool __result)
        {
            try
            {
                if (slot == null || slot.Storage == null)
                {
                    __result = false;
                    return false;
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + "drag state guard failed: " + e);
                __result = false;
                return false;
            }
        }

        private static void ArsenalConfigurePostfix(ArsenalScreen __instance)
        {
            if (!_centralOpenRequested)
            {
                // A pooled ArsenalScreen may previously have hosted central
                // mode.  Always restore every vanilla object before a regular
                // Arsenal/Cargo opening so this mod cannot leak UI state.
                __instance.GetComponent<CentralManagementPanel>()
                    ?.RestoreVanillaUi();
                return;
            }
            _centralOpenRequested = false;
            try
            {
                ConfigureCentralPanel(__instance, augmentationMode: false,
                    _centralOpenWithOperatorPanel);
                _centralOpenWithOperatorPanel = false;
            }
            catch (Exception e)
            {
                _centralOpenWithOperatorPanel = false;
                Debug.LogError(LogPrefix + "central arsenal setup failed: " + e);
            }
        }

        private static void AugmentationConfigurePostfix(
            AugmentationScreen __instance)
        {
            if (!_centralAugmentationOpenRequested)
            {
                __instance.GetComponent<CentralManagementPanel>()
                    ?.RestoreVanillaUi();
                return;
            }
            _centralAugmentationOpenRequested = false;
            try
            {
                ConfigureCentralPanel(__instance, augmentationMode: true);
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix
                               + "central augmentation setup failed: " + e);
            }
        }

        private static void ConfigureCentralPanel(ScreenWithShipCargo screen,
            bool augmentationMode, bool showOperatorPanel = false)
        {
            var cargo = _screenCargo(screen);
            if (cargo == null || cargo.ShipCargo.Count == 0)
                throw new InvalidOperationException(
                    "ship cargo is unavailable");
            _activeShipCargo(screen) = cargo.ShipCargo[0];
            var panel = screen.GetComponent<CentralManagementPanel>();
            if (panel == null)
                panel = screen.gameObject.AddComponent<CentralManagementPanel>();
            panel.Configure(screen, cargo, _screenSpaceTime(screen),
                _screenProgression(screen), _screenPerkFactory(screen),
                _screenMercenary(screen),
                _cargoWindow(screen), augmentationMode, showOperatorPanel);
            _cargoWindow(screen)?.SetActive(false);
            InstallCentralControlClickGuard(screen);
        }

        private static void InstallCentralControlClickGuard(
            ScreenWithShipCargo screen)
        {
            var original = _controlClickCallback(UI.Drag);
            if (original == null)
                return;
            _controlClickCallback(UI.Drag) = slot =>
            {
                var panel = screen.GetComponent<CentralManagementPanel>();
                if (panel != null && panel.IsCentralMode
                    && !panel.TryConsumeCentralControlClick(slot))
                {
                    return;
                }
                original(slot);
            };
        }

        private static void ArsenalRefreshViewPostfix(ArsenalScreen __instance)
        {
            try
            {
                var panel = __instance.GetComponent<CentralManagementPanel>();
                if (panel == null || !panel.IsCentralMode)
                    return;
                _cargoWindow(__instance)?.SetActive(false);
                panel.RequestRefresh();
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + "central refresh failed: " + e);
            }
        }

        private static void AugmentationRefreshViewPostfix(
            AugmentationScreen __instance)
        {
            try
            {
                var panel = __instance.GetComponent<CentralManagementPanel>();
                if (panel == null || !panel.IsCentralMode)
                    return;
                _cargoWindow(__instance)?.SetActive(false);
                panel.RequestRefresh();
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix
                               + "central augmentation refresh failed: " + e);
            }
        }

        private static void ShipCargoOnEnablePostfix(ScreenWithShipCargo __instance)
        {
            try
            {
                var panel = __instance.GetComponent<CentralManagementPanel>();
                if (panel == null || !panel.IsCentralMode)
                    return;
                _cargoWindow(__instance)?.SetActive(false);
                panel.ShowPanel();
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + "central enable failed: " + e);
            }
        }

        private static void ShipCargoOnDisablePrefix(ScreenWithShipCargo __instance)
        {
            try
            {
                __instance.GetComponent<CentralManagementPanel>()?.LeaveCentralMode();
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + "central cleanup failed: " + e);
            }
        }

        private static bool ShipCargoUpdatePrefix(ScreenWithShipCargo __instance)
        {
            try
            {
                var panel = __instance.GetComponent<CentralManagementPanel>();
                return panel == null || !panel.IsCentralMode;
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + "central update isolation failed: " + e);
                return true;
            }
        }

        private static bool ShipCargoProcessPrefix(ScreenWithShipCargo __instance,
            out bool interruptProcessing)
        {
            // false means "nothing consumed here", so the rest of the UI input
            // chain carries on exactly as it would if this screen had no
            // hotkeys -- we only decline the cargo screen's own bindings.
            interruptProcessing = false;
            try
            {
                var panel = __instance.GetComponent<CentralManagementPanel>();
                return panel == null || !panel.IsCentralMode;
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix
                               + "central process isolation failed: " + e);
                return true;
            }
        }

        private static void CentralSplitPrefix(ScreenWithShipCargo __instance,
            int leftVal, out CentralManagementPanel __state)
        {
            __state = null;
            try
            {
                var panel = __instance.GetComponent<CentralManagementPanel>();
                var slot = _contextMenuItemSlot(__instance);
                if (panel == null || !panel.IsCentralItemSlot(slot))
                    return;
                panel.CaptureCentralSplit(slot.Item, leftVal);
                __state = panel;
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + "central split capture failed: " + e);
            }
        }

        private static void CentralSplitPostfix(CentralManagementPanel __state)
        {
            try
            {
                if (__state == null || !__state.IsCentralMode)
                    return;
                __state.CompleteCentralSplit();
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + "central split completion failed: " + e);
            }
        }

        internal static bool SetCentralOperatorPanelVisible(
            ScreenWithShipCargo screen, bool visible)
        {
            if (screen == null)
                return false;
            if (!(screen is ArsenalScreen arsenal))
                return screen is AugmentationScreen && visible;
            try
            {
                var mercenary = _screenMercenary(screen);
                var view = _inventoryView(arsenal);
                if (view == null || mercenary == null)
                {
                    _inventoryWindow(arsenal)?.SetActive(false);
                    _arsenalVestGrid(arsenal)?.SetActive(false);
                    return false;
                }

                if (!visible)
                {
                    // The vanilla OnDisable unsubscribes the view from the
                    // current mercenary. Keep the view itself intact so no
                    // original Arsenal layout or item grid is reconstructed.
                    _inventoryWindow(arsenal)?.SetActive(false);
                    _arsenalVestGrid(arsenal)?.SetActive(false);
                    return true;
                }

                var needsInitialize = !view.gameObject.activeInHierarchy;
                _inventoryWindow(arsenal)?.SetActive(true);
                _arsenalVestGrid(arsenal)?.SetActive(true);
                view.gameObject.SetActive(true);
                if (needsInitialize)
                {
                    // The vanilla ArsenalScreen.Configure initializes the view
                    // while the pooled screen is still inactive, and that first
                    // Initialize subscribes OnWeightChanged without any
                    // OnDisable in between. Subscribing again here would stack
                    // a second handler; after the next hide only one is removed,
                    // and the leftover fires RefreshWeight with a null merc
                    // (NRE inside AugmentationSystem.Augment, skipping its
                    // caller's RefreshView). Turning the view off once -- with
                    // its parents already active -- guarantees OnDisable runs
                    // and drops the old binding before we initialize fresh.
                    view.gameObject.SetActive(false);
                    view.gameObject.SetActive(true);
                    mercenary.CreatureData.Inventory.InitializeItemsOnFloor(screen);
                    view.Initialize(mercenary,
                        Data.MercenaryProfiles.GetRecord(mercenary.ProfileId));
                    screen.RefreshView();
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + "operator panel toggle failed: " + e);
                return false;
            }
        }

        internal static bool SwitchCentralOperator(ArsenalScreen screen,
            Mercenary mercenary, bool showOperatorPanel)
        {
            if (screen == null || mercenary == null || UI.Drag.IsDragging)
                return false;
            try
            {
                var previous = _screenMercenary(screen);
                var view = _inventoryView(screen);
                if (view == null)
                    return false;

                // Activate the view's parents BEFORE turning the view off:
                // SetActive(false) only runs the vanilla OnDisable (which
                // unsubscribes the previous operator's inventory events) when
                // the object is active in hierarchy. With the panel hidden the
                // view is not, so the old subscription would survive and the
                // Initialize below would stack a second handler.
                _inventoryWindow(screen)?.SetActive(true);
                _arsenalVestGrid(screen)?.SetActive(true);
                view.gameObject.SetActive(true);
                view.gameObject.SetActive(false);
                if (previous != null)
                {
                    previous.CreatureData.Inventory.InitializeItemsOnFloor(null);
                    SingletonMonoBehaviour<TooltipFactory>.Instance
                        .ReleaseCurrentMercenary(previous);
                }

                _screenMercenary(screen) = mercenary;
                SingletonMonoBehaviour<ItemFactory>.Instance
                    .SetConsumablesStackBonus(
                        mercenary.CreatureData.ConsumablesStackBonus);
                SingletonMonoBehaviour<ItemFactory>.Instance
                    .SetWeaponDurabilityMult(
                        mercenary.CreatureData.WeaponDurabilityMult);
                SingletonMonoBehaviour<ItemFactory>.Instance
                    .SetArmorDurabilityMult(
                        mercenary.CreatureData.ArmorDurabilityMult);

                _inventoryWindow(screen)?.SetActive(true);
                _arsenalVestGrid(screen)?.SetActive(true);
                mercenary.CreatureData.Inventory.InitializeItemsOnFloor(screen);
                view.gameObject.SetActive(true);
                view.Initialize(mercenary,
                    Data.MercenaryProfiles.GetRecord(mercenary.ProfileId));
                UI.Drag.Enable(_screenSpaceTime(screen), mercenary,
                    _screenPerkFactory(screen), DragReloadMode.AllWeapons);
                SingletonMonoBehaviour<TooltipFactory>.Instance
                    .SetCurrentMercenary(mercenary);
                screen.RefreshView();
                if (!showOperatorPanel)
                    SetCentralOperatorPanelVisible(screen, false);
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix + "operator switch failed: " + e);
                return false;
            }
        }

        internal static bool SwitchCentralOperator(ScreenWithShipCargo screen,
            Mercenary mercenary, bool showOperatorPanel)
        {
            if (screen is ArsenalScreen arsenal)
                return SwitchCentralOperator(arsenal, mercenary,
                    showOperatorPanel);
            if (screen is AugmentationScreen)
                return OpenCentralAugmentation(mercenary);
            return false;
        }

        internal static bool OpenCentralAugmentation(Mercenary mercenary)
        {
            if (mercenary == null || UI.Drag.IsDragging)
                return false;
            try
            {
                _centralAugmentationOpenRequested = true;
                UI.Hide<ArsenalScreen>();
                UI.Hide<AugmentationScreen>();
                UI.Chain<AugmentationScreen>().Invoke(v =>
                {
                    v.Configure(mercenary);
                }).Show().Fallback<SpaceshipScreen>();
                return true;
            }
            catch (Exception e)
            {
                _centralAugmentationOpenRequested = false;
                Debug.LogError(LogPrefix
                               + "could not open central augmentation mode: "
                               + e);
                return false;
            }
        }

        internal static bool OpenCentralArsenal(Mercenary mercenary,
            bool showOperatorPanel)
        {
            if (UI.Drag.IsDragging)
                return false;
            try
            {
                _centralOpenWithOperatorPanel = showOperatorPanel;
                _centralOpenRequested = true;
                UI.Hide<AugmentationScreen>();
                UI.Hide<ArsenalScreen>();
                UI.Chain<ArsenalScreen>().Invoke(v =>
                {
                    v.Configure(mercenary, showShuttle: false);
                }).Show().Fallback<SpaceshipScreen>();
                return true;
            }
            catch (Exception e)
            {
                _centralOpenWithOperatorPanel = false;
                _centralOpenRequested = false;
                Debug.LogError(LogPrefix
                               + "could not return to central arsenal: " + e);
                return false;
            }
        }
    }
}
