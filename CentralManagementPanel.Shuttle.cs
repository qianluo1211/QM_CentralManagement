using System;
using System.Collections.Generic;
using System.Linq;
using MGSC;
using TMPro;
using UnityEngine;

namespace QM_CentralManagement
{
    /// <summary>
    /// The central panel's left-hand pane selector, and the shuttle hold it
    /// can put there.
    ///
    /// The header used to carry a single button whose meaning changed with the
    /// mode -- "show gear", "hide gear", "install augments", "back to gear" --
    /// so what it would do next was never visible until it had already done
    /// it. It is a dropdown now: the trigger names what is on screen and the
    /// list names every place you can go.
    ///
    /// The shuttle entry shows the game's OWN shuttle grid
    /// (ArsenalScreen._shuttleCargoStorageView), the same view the
    /// pre-departure screen puts behind its cargo-shuttle tab. It is a sibling
    /// of the inventory view inside InventoryWindow and occupies the same
    /// 314x150 rect, so activating one and deactivating the other swaps the
    /// window's content exactly the way the vanilla tab does -- with vanilla
    /// drag and drop, vanilla capacity, and vanilla stacking, none of which a
    /// hand-built item picker could have reproduced.
    /// </summary>
    internal sealed partial class CentralManagementPanel
    {
        private enum LeftPane
        {
            Hidden,
            Equipment,
            Shuttle,
            Augmentation
        }

        /// <summary>
        /// Carries "open the shuttle pane" across the screen switch that
        /// leaving augmentation mode requires. Static because the panel that
        /// reads it is a different component instance on a different screen.
        /// </summary>
        private static bool _pendingShuttlePane;

        private bool _shuttleViewVisible;

        private GameObject _paneDropdownRoot;
        private readonly List<GameObject> _paneDropdownRows =
            new List<GameObject>();

        private bool ShuttlePaneAvailable =>
            Plugin.ShuttleManifestsEnabled
            && ShuttleManifestService.StorageOf(_progression) != null;

        private LeftPane CurrentPane
        {
            get
            {
                if (_augmentationMode)
                    return LeftPane.Augmentation;
                if (!_operatorPanelVisible)
                    return LeftPane.Hidden;
                return _shuttleViewVisible
                    ? LeftPane.Shuttle
                    : LeftPane.Equipment;
            }
        }

        private string PaneLabel(LeftPane pane)
        {
            switch (pane)
            {
                case LeftPane.Equipment:
                    return Localization.Get("qmcentral.pane_equipment");
                case LeftPane.Shuttle:
                    return Localization.Get("qmcentral.pane_shuttle");
                case LeftPane.Augmentation:
                    return Localization.Get("qmcentral.pane_augmentation");
                default:
                    return Localization.Get("qmcentral.pane_hidden");
            }
        }

        // ------------------------------------------------------------------
        // Pane dropdown.
        // ------------------------------------------------------------------

        private void BuildPaneDropdown(Transform parent)
        {
            _paneDropdownRoot = PanelUi.CreateDropdownRoot("PaneDropdown",
                parent, 0f, 0f, GearW, 20f);
        }

        private void DiscardShuttleUi()
        {
            _paneDropdownRoot = null;
            _paneDropdownRows.Clear();
            _shuttleViewVisible = false;
        }

        private void TogglePaneDropdown()
        {
            if (_paneDropdownRoot == null || UI.Drag.IsDragging)
                return;
            if (_paneDropdownRoot.activeSelf)
            {
                CloseDropdown(_paneDropdownRoot);
                return;
            }
            CloseAllDropdowns();
            RebuildPaneDropdown();
            _paneDropdownRoot.SetActive(true);
            _paneDropdownRoot.transform.SetAsLastSibling();
        }

        private List<LeftPane> AvailablePanes()
        {
            var panes = new List<LeftPane> { LeftPane.Equipment };
            if (ShuttlePaneAvailable)
                panes.Add(LeftPane.Shuttle);
            if (_mercenary != null)
                panes.Add(LeftPane.Augmentation);
            panes.Add(LeftPane.Hidden);
            return panes;
        }

        private void RebuildPaneDropdown()
        {
            PanelUi.ClearDropdownRows(_paneDropdownRows);
            var panes = AvailablePanes();
            const float rowHeight = 15f;
            var width = Mathf.Max(GearW, 92f);
            var height = panes.Count * rowHeight + 4f;
            // Hangs directly under the trigger, which sits in the header row.
            PanelUi.SetTopLeft((RectTransform)_paneDropdownRoot.transform,
                _paneDropdownX, _paneDropdownY, width, height);
            for (var i = 0; i < panes.Count; i++)
            {
                var pane = panes[i];
                var row = PanelUi.CreateButtonRoot("Pane" + pane,
                    _paneDropdownRoot.transform, 2f, -2f - i * rowHeight,
                    width - 4f, rowHeight - 1f, out var background,
                    out var label);
                label.text = PaneLabel(pane);
                label.fontSize = 6f;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.margin = new Vector4(4f, 0f, 2f, 0f);
                PanelUi.SetSurfaceSelected(background, pane == CurrentPane);
                PanelUi.BindClick(row, () => SelectPane(pane));
                _paneDropdownRows.Add(row);
            }
        }

        // Written by LayoutHeader: the trigger is placed right-to-left from
        // the header's inner edge, so the list can only follow it after the
        // header has been measured.
        private float _paneDropdownX;
        private float _paneDropdownY;

        private void SelectPane(LeftPane pane)
        {
            CloseDropdown(_paneDropdownRoot);
            if (UI.Drag.IsDragging || pane == CurrentPane)
                return;

            if (pane == LeftPane.Augmentation)
            {
                Plugin.OpenCentralAugmentation(_mercenary);
                return;
            }
            if (_augmentationMode)
            {
                // The body panel lives on a different screen; everything else
                // needs the arsenal back.
                _pendingShuttlePane = pane == LeftPane.Shuttle;
                Plugin.OpenCentralArsenal(_mercenary,
                    showOperatorPanel: pane != LeftPane.Hidden);
                return;
            }

            if (pane == LeftPane.Hidden)
            {
                ShowShuttlePane(false);
                SetOperatorPane(false);
                return;
            }
            // Both remaining panes need the window open; which view fills it
            // is the only difference.
            if (!SetOperatorPane(true))
                return;
            ShowShuttlePane(pane == LeftPane.Shuttle);
            RefreshPresetBar();
        }

        private bool SetOperatorPane(bool visible)
        {
            var applied = Plugin.SetCentralOperatorPanelVisible(_screen,
                visible) && visible;
            _operatorPanelVisible = applied;
            _scrollRow = 0;
            ApplyResponsiveLayout();
            RefreshFiltered();
            _root?.transform.SetAsLastSibling();
            return applied;
        }

        /// <summary>
        /// Swaps the inventory window's content between the operator's
        /// equipment and the shuttle hold, the way ArsenalScreen.Refresh does
        /// it for its own tabs.
        /// </summary>
        private void ShowShuttlePane(bool visible)
        {
            var wanted = visible && ShuttlePaneAvailable;
            if (!Plugin.SetCentralShuttleViewVisible(_screen, wanted))
                wanted = false;
            _shuttleViewVisible = wanted;
            RefreshPresetBar();
        }

        /// <summary>
        /// Re-applies the pane after the panel rebuilds or the screen is
        /// reconfigured. Called from Configure, which is also where a pane
        /// requested from the augmentation screen lands.
        /// </summary>
        private void RestorePaneAfterConfigure()
        {
            var wantShuttle = _pendingShuttlePane;
            _pendingShuttlePane = false;
            _shuttleViewVisible = false;
            if (wantShuttle && _operatorPanelVisible)
                ShowShuttlePane(true);
            else
                Plugin.SetCentralShuttleViewVisible(_screen, false);
        }

        // ------------------------------------------------------------------
        // The preset bar's shuttle-manifest mode.
        // ------------------------------------------------------------------

        private bool ManifestBarMode => _shuttleViewVisible;

        private void RefreshManifestBar()
        {
            _presetTitle.text = Localization.Get("qmcentral.shuttle_title");
            _presetApplyLabel.text = Localization.Get(
                "qmcentral.shuttle_restock");
            _presetSaveLabel.text = Localization.Get("qmcentral.preset_save");
            _presetDeleteLabel.text = Localization.Get(
                "qmcentral.preset_delete");

            var manifest = ShuttleManifestRepository.Selected;
            _presetSelectedLabel.text = manifest?.Name
                                        ?? Localization.Get(
                                            "qmcentral.shuttle_none");
            _presetSummary.text = ShuttleManifestService.Summary(manifest);

            var shuttle = ShuttleManifestService.StorageOf(_progression);
            var hasLines = manifest?.Items != null && manifest.Items.Count > 0;
            // Greyed out once the hold already matches: the button would do
            // nothing, and saying so beats a popup that says nothing happened.
            _presetApplyButton.SetInteractable(hasLines && shuttle != null
                && _cargo != null
                && ShuttleManifestService.Shortfall(manifest, shuttle).Count > 0);
            _presetDeleteButton.SetInteractable(manifest != null);
            _presetSaveButton.SetInteractable(shuttle != null);
            _presetSelectedButton.SetInteractable(
                ShuttleManifestRepository.All.Count > 0);
        }

        private void RestockShuttleFromBar()
        {
            var manifest = ShuttleManifestRepository.Selected;
            if (manifest == null)
                return;
            var result = ShuttleManifestService.Restock(manifest, _cargo,
                _progression, _spaceTime, ActiveCargoStorage());
            // Items left the ship's holds, so both the mod's aggregate index
            // and the vanilla shuttle grid are stale.
            RebuildAndRefresh();
            _screen?.RefreshView();
            RefreshShuttleGrid();
            RefreshPresetBar();
            ReportRestock(result);
        }

        private void ReportRestock(ShuttleRestockResult result)
        {
            if (result == null)
                return;
            if (!string.IsNullOrEmpty(result.Error))
            {
                PresetPopup.OpenCustom(
                    Localization.Get("qmcentral.shuttle_report_title"),
                    result.Error,
                    Localization.Get("qmcentral.preset_close"), null, null);
                return;
            }
            if (result.AlreadyStocked)
                return;
            var issues = result.AllIssues.ToList();
            if (issues.Count == 0)
                return;
            var lines = issues.Take(6).ToList();
            if (issues.Count > lines.Count)
                lines.Add("…");
            PresetPopup.OpenCustom(
                Localization.Get("qmcentral.shuttle_report_title"),
                string.Join("\n", lines),
                Localization.Get("qmcentral.preset_close"), null, null);
        }

        private void OpenSaveManifest()
        {
            var shuttle = ShuttleManifestService.StorageOf(_progression);
            if (shuttle == null)
                return;
            var captured = ShuttleManifestService.Capture(shuttle);
            if (captured.Count == 0)
            {
                PresetPopup.OpenCustom(
                    Localization.Get("qmcentral.shuttle_save_title"),
                    Localization.Get("qmcentral.shuttle_save_empty"),
                    Localization.Get("qmcentral.preset_close"), null, null);
                return;
            }
            var suggested = ShuttleManifestRepository.Selected?.Name
                            ?? ShuttleManifestRepository.SuggestName();
            PresetPopup.OpenCustom(
                Localization.Get("qmcentral.shuttle_save_title"),
                string.Format(
                    Localization.Get("qmcentral.shuttle_save_body"),
                    captured.Count, captured.Sum(e => e.Units)),
                Localization.Get("qmcentral.preset_save_confirm"),
                suggested,
                name =>
                {
                    ShuttleManifestRepository.Save(name, captured);
                    RefreshPresetBar();
                });
        }

        private void OpenDeleteManifest()
        {
            var manifest = ShuttleManifestRepository.Selected;
            if (manifest == null)
                return;
            var id = manifest.Id;
            PresetPopup.OpenCustom(
                Localization.Get("qmcentral.shuttle_delete_title"),
                string.Format(
                    Localization.Get("qmcentral.preset_delete_body"),
                    manifest.Name),
                Localization.Get("qmcentral.preset_delete_confirm"),
                null,
                _ =>
                {
                    ShuttleManifestRepository.Delete(id);
                    RefreshPresetBar();
                });
        }

        private ItemStorage ActiveCargoStorage()
        {
            var active = _screen == null
                ? null
                : Plugin.ActiveShipCargoOf(_screen);
            return active ?? _cargo?.ShipCargo.FirstOrDefault();
        }

        /// <summary>
        /// Re-binds the vanilla shuttle grid after the mod moved items into
        /// the hold. ArsenalScreen.RefreshView only refreshes the grid when
        /// its own view flag says the shuttle tab is showing, which it never
        /// does here -- central mode drives the view directly.
        /// </summary>
        private void RefreshShuttleGrid()
        {
            if (!_shuttleViewVisible)
                return;
            try
            {
                var view = _screen is ArsenalScreen arsenal
                    ? Plugin.ShuttleCargoViewOf(arsenal)
                    : null;
                if (view != null && view.gameObject.activeInHierarchy)
                    view.RefreshGrid();
            }
            catch (Exception e)
            {
                Debug.LogError(Plugin.LogPrefix
                               + "shuttle grid refresh failed: " + e);
            }
        }
    }
}
