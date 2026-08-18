using System;
using System.Collections.Generic;
using System.Linq;
using MGSC;
using UnityEngine;

namespace QM_CentralManagement
{
    /// <summary>
    /// The shuttle-manifest half of the pre-departure strip: what the bar's
    /// four controls do while the screen is showing the cargo-shuttle tab.
    ///
    /// Split out for the same reason CentralManagementPanel.Presets.cs is:
    /// the host file owns the strip's geometry and placement, this one owns a
    /// self-contained feature that happens to be driven from it.
    ///
    /// Why the feature exists: MagnumCargoSystem.ReturnShuttleItems empties
    /// the shuttle hold after every raid, so it is empty before every single
    /// deployment and any standing supply kit has to be re-packed by hand.
    /// </summary>
    internal sealed partial class ShipLoadoutBar
    {
        /// <summary>How many issue lines the result popup lists.</summary>
        private const int ReportedIssueLines = 6;

        private ItemStorage ShuttleStorage =>
            ShuttleManifestService.StorageOf(_progression);

        private void RefreshShuttleLabels()
        {
            _titleLabel.text = Localization.Get("qmcentral.shuttle_title");
            _applyLabel.text = Localization.Get("qmcentral.shuttle_restock");
            _saveLabel.text = Localization.Get("qmcentral.preset_save");
            _deleteLabel.text = Localization.Get("qmcentral.preset_delete");

            var manifest = ShuttleManifestRepository.Selected;
            _selectedLabel.text = manifest?.Name
                                  ?? Localization.Get(
                                      "qmcentral.shuttle_none");
            var shuttle = ShuttleStorage;
            var hasManifest = manifest != null
                              && manifest.Items != null
                              && manifest.Items.Count > 0;
            // Greyed out once the hold already matches: the button would be a
            // no-op, and saying so up front beats a popup that says nothing
            // happened.
            _applyButton.SetInteractable(hasManifest && shuttle != null
                && _cargo != null
                && ShuttleManifestService.Shortfall(manifest, shuttle).Count > 0);
            _deleteButton.SetInteractable(manifest != null);
            _saveButton.SetInteractable(shuttle != null);
            _selectedButton.SetInteractable(
                ShuttleManifestRepository.All.Count > 0);
        }

        // ------------------------------------------------------------------
        // Actions.
        // ------------------------------------------------------------------

        private void RestockShuttle()
        {
            var manifest = ShuttleManifestRepository.Selected;
            if (manifest == null)
                return;
            var result = ShuttleManifestService.Restock(manifest, _cargo,
                _progression, _spaceTime, ActiveCargo());
            // The grid was drawn before the items moved either way, and a
            // failed restock may still have moved part of the list.
            _screen?.RefreshView();
            RefreshLabels();
            ReportRestock(result);
        }

        private void ReportRestock(ShuttleRestockResult result)
        {
            if (result == null)
                return;
            if (!string.IsNullOrEmpty(result.Error))
            {
                Popup.OpenCustom(
                    Localization.Get("qmcentral.shuttle_report_title"),
                    result.Error,
                    Localization.Get("qmcentral.preset_close"), null, null);
                return;
            }
            if (result.AlreadyStocked)
                return;

            var issues = result.AllIssues.ToList();
            if (issues.Count == 0)
            {
                // Success is visible in the grid behind the bar, so it gets a
                // sound rather than a popup to dismiss before every raid.
                PlayRestockSound();
                return;
            }
            if (result.UnitsMoved > 0)
                PlayRestockSound();

            var lines = issues.Take(ReportedIssueLines).ToList();
            if (issues.Count > lines.Count)
                lines.Add("…");
            Popup.OpenCustom(
                Localization.Get("qmcentral.shuttle_report_title"),
                string.Join("\n", lines),
                Localization.Get("qmcentral.preset_close"), null, null);
        }

        private void OpenSaveManifest()
        {
            var shuttle = ShuttleStorage;
            if (shuttle == null)
                return;
            var captured = ShuttleManifestService.Capture(shuttle);
            if (captured.Count == 0)
            {
                // Saving an empty hold would produce a manifest that restocks
                // nothing, and the player would have no way to tell it apart
                // from a working one in the list.
                Popup.OpenCustom(
                    Localization.Get("qmcentral.shuttle_save_title"),
                    Localization.Get("qmcentral.shuttle_save_empty"),
                    Localization.Get("qmcentral.preset_close"), null, null);
                return;
            }

            var suggested = ShuttleManifestRepository.Selected?.Name
                            ?? ShuttleManifestRepository.SuggestName();
            Popup.OpenCustom(
                Localization.Get("qmcentral.shuttle_save_title"),
                string.Format(
                    Localization.Get("qmcentral.shuttle_save_body"),
                    captured.Count, captured.Sum(e => e.Units)),
                Localization.Get("qmcentral.preset_save_confirm"),
                suggested,
                name =>
                {
                    ShuttleManifestRepository.Save(name, captured);
                    RefreshLabels();
                });
        }

        private void OpenDeleteManifest()
        {
            var manifest = ShuttleManifestRepository.Selected;
            if (manifest == null)
                return;
            var id = manifest.Id;
            Popup.OpenCustom(
                Localization.Get("qmcentral.shuttle_delete_title"),
                string.Format(
                    Localization.Get("qmcentral.preset_delete_body"),
                    manifest.Name),
                Localization.Get("qmcentral.preset_delete_confirm"),
                null,
                _ =>
                {
                    ShuttleManifestRepository.Delete(id);
                    RefreshLabels();
                });
        }

        private ItemStorage ActiveCargo()
        {
            var active = _screen == null
                ? null
                : Plugin.ActiveShipCargoOf(_screen);
            return active ?? _cargo?.ShipCargo.FirstOrDefault();
        }

        private static void PlayRestockSound()
        {
            var controller = SingletonMonoBehaviour<SoundController>.Instance;
            var sounds = SingletonMonoBehaviour<SoundsStorage>.Instance;
            if (controller != null && sounds?.TakeItem != null)
                controller.PlayUiSound(sounds.TakeItem, isUnique: true);
        }

        // ------------------------------------------------------------------
        // Automatic restock.
        // ------------------------------------------------------------------

        /// <summary>
        /// Opt-in top-up when the pre-departure screen opens, driven from
        /// ArsenalScreen.Configure so it runs once per visit rather than on
        /// every refresh.
        ///
        /// Deliberately SILENT: it runs before the player has asked for
        /// anything, so a missing-stock popup on every single departure would
        /// be nagging. Shortfalls go to Player.log and are visible in the hold
        /// itself; the manual button is the one that reports.
        /// </summary>
        internal static void AutoRestock(ArsenalScreen screen)
        {
            try
            {
                if (screen == null || !Plugin.ShipLoadoutsEnabled
                    || !Plugin.ShuttleManifestsEnabled
                    || !Plugin.ShuttleAutoRestock)
                {
                    return;
                }
                var manifest = ShuttleManifestRepository.Selected;
                if (manifest == null)
                    return;
                var progression = Plugin.ScreenProgressionOf(screen);
                var cargo = Plugin.ScreenCargoOf(screen);
                if (cargo == null
                    || ShuttleManifestService.StorageOf(progression) == null)
                {
                    return;
                }
                var active = Plugin.ActiveShipCargoOf(screen)
                             ?? cargo.ShipCargo.FirstOrDefault();
                var result = ShuttleManifestService.Restock(manifest, cargo,
                    progression, Plugin.ScreenSpaceTimeOf(screen), active);
                if (result.AlreadyStocked || result.UnitsMoved <= 0)
                {
                    LogAutoRestock(manifest, result);
                    return;
                }
                LogAutoRestock(manifest, result);
                // The screen built its grids from the pre-restock hold.
                screen.RefreshView();
            }
            catch (Exception e)
            {
                Debug.LogError(Plugin.LogPrefix
                               + "shuttle auto-restock failed: " + e);
            }
        }

        private static void LogAutoRestock(ShuttleManifest manifest,
            ShuttleRestockResult result)
        {
            var issues = result.AllIssues.ToList();
            if (!string.IsNullOrEmpty(result.Error))
            {
                Debug.LogWarning(Plugin.LogPrefix + "auto-restock of '"
                                 + manifest.Name + "' aborted: "
                                 + result.Error);
                return;
            }
            Plugin.DebugLog("auto-restocked '" + manifest.Name + "': "
                            + result.UnitsMoved + " unit(s) moved"
                            + (issues.Count == 0
                                ? "."
                                : ", " + issues.Count + " issue(s): "
                                  + string.Join("; ", issues)));
        }
    }
}
