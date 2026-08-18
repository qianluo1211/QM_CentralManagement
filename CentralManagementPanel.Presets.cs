using System;
using System.Collections.Generic;
using System.Linq;
using MGSC;
using TMPro;
using UnityEngine;

namespace QM_CentralManagement
{
    /// <summary>
    /// The loadout preset bar that sits above the agent's equipment window
    /// while the central panel shows it. The bar and its dropdown are here;
    /// the save / apply / delete modal is <see cref="LoadoutPresetPopup"/>,
    /// shared with the pre-departure ship strip.
    /// </summary>
    internal sealed partial class CentralManagementPanel
    {
        private GameObject _presetBarRoot;
        private GameObject _presetDropdownRoot;
        private readonly List<GameObject> _presetDropdownRows =
            new List<GameObject>();
        private TextMeshProUGUI _presetTitle;
        private TextMeshProUGUI _presetSelectedLabel;
        private TextMeshProUGUI _presetSummary;
        private CommonButton _presetSelectedButton;
        private GameObject _presetSelectedRoot;
        private CommonButton _presetApplyButton;
        private CommonButton _presetSaveButton;
        private CommonButton _presetDeleteButton;
        private TextMeshProUGUI _presetApplyLabel;
        private TextMeshProUGUI _presetSaveLabel;
        private TextMeshProUGUI _presetDeleteLabel;
        private LoadoutPresetPopup _presetPopup;

        private bool IsPresetPopupOpen => _presetPopup?.IsOpen == true;

        private LoadoutPresetPopup PresetPopup
        {
            get
            {
                if (_presetPopup == null)
                {
                    _presetPopup = new LoadoutPresetPopup(
                        LoadoutPresetTextSet.Central,
                        () => new LoadoutPresetContext
                        {
                            Mercenary = _mercenary,
                            Cargo = _cargo,
                            Progression = _progression,
                            SpaceTime = _spaceTime,
                            PerkFactory = _perkFactory,
                        },
                        RefreshPresetBar,
                        OnPresetApplied,
                        ModInputGate.ConsumePointerRelease,
                        CloseAllDropdowns);
                }
                return _presetPopup;
            }
        }

        /// <summary>
        /// Applying a preset moves items between cargo and the agent, so both
        /// the vanilla equipment view and this panel's own aggregate index are
        /// stale afterwards.
        /// </summary>
        private void OnPresetApplied()
        {
            _screen.RefreshView();
            RebuildAndRefresh();
        }

        private void BuildPresetUi(Transform barParent, Transform overlayParent)
        {
            if (_presetBarRoot != null)
                return;
            // The bar lives inside the inventory window subtree at its FIRST
            // sibling slot, so the auto-feeder panel (which lives deeper in
            // the same subtree) always draws above it, while the bar itself
            // keeps its exact visual position above the window.
            _presetBarRoot = PanelUi.CreateUiObject("QM_LoadoutPresets",
                barParent);
            var rect = (RectTransform)_presetBarRoot.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            // Window-local equivalent of the old screen position (34, 102):
            // the window's centre is (-123, -20), so (34 + 123, 102 + 20).
            rect.anchoredPosition = new Vector2(157f, 122f);
            rect.sizeDelta = new Vector2(314f, 46f);
            _presetBarRoot.transform.SetSiblingIndex(0);
            VanillaSkin.Slice(_presetBarRoot, VanillaSkin.PanelFrame,
                PanelUi.PanelColor).raycastTarget = true;

            _presetTitle = PanelUi.CreateText("Title", rect, 4f, -2f,
                66f, 14f, TextContext.IgnoreSize, 7f, PanelUi.BrightColor,
                TextAlignmentOptions.MidlineLeft);
            _presetSelectedRoot = PanelUi.CreateDropdownTrigger("Selected",
                rect, 72f, -2f, 144f, 14f, out _presetSelectedLabel);
            VanillaSkin.AddHint(_presetSelectedRoot,
                "qmcentral.tip.preset_select");
            _presetSelectedButton = PanelUi.ButtonOf(_presetSelectedRoot);
            PanelUi.BindClick(_presetSelectedRoot, TogglePresetDropdown);

            var applyRoot = PanelUi.CreateButtonRoot("Apply", rect, 218f, -2f,
                44f, 14f, "qmcentral.tip.preset_apply",
                out _, out _presetApplyLabel);
            _presetApplyButton = PanelUi.ButtonOf(applyRoot);
            // Bound once to dispatchers: CommonButton.OnClick has no
            // unsubscribe, so the mode is read at click time.
            PanelUi.BindClick(applyRoot, OnPresetApplyClicked);
            var saveRoot = PanelUi.CreateButtonRoot("Save", rect, 264f, -2f,
                46f, 14f, "qmcentral.tip.preset_save",
                out _, out _presetSaveLabel);
            _presetSaveButton = PanelUi.ButtonOf(saveRoot);
            PanelUi.BindClick(saveRoot, OnPresetSaveClicked);

            _presetSummary = PanelUi.CreateText("Summary", rect, 4f, -18f,
                258f, 24f, TextContext.IgnoreSize, 6f, PanelUi.OffColor,
                TextAlignmentOptions.MidlineLeft);
            var deleteRoot = PanelUi.CreateButtonRoot("Delete", rect, 264f,
                -18f, 46f, 24f, "qmcentral.tip.preset_delete",
                out _, out _presetDeleteLabel);
            PanelUi.MakeDangerButton(deleteRoot, _presetDeleteLabel);
            _presetDeleteButton = PanelUi.ButtonOf(deleteRoot);
            PanelUi.BindClick(deleteRoot, OnPresetDeleteClicked);

            // Parented to the bar and anchored just under the trigger, so
            // it opens where the control is instead of at a fixed offset from
            // the panel centre -- which is what made it cover the summary line.
            _presetDropdownRoot = PanelUi.CreateUiObject("PresetDropdown",
                rect);
            PanelUi.SetTopLeft((RectTransform)_presetDropdownRoot.transform,
                72f, -17f, 220f, 20f);
            VanillaSkin.Slice(_presetDropdownRoot, VanillaSkin.ListBackground,
                PanelUi.PanelColor).raycastTarget = true;
            _presetDropdownRoot.SetActive(false);

            PresetPopup.Build(overlayParent);
            _presetBarRoot.SetActive(false);
        }

        private void ApplyPresetLayout()
        {
            if (_presetBarRoot == null)
                return;
            // Hidden in augmentation mode: the body panel has no use for the
            // loadout bar and the bar's window-anchored position would be
            // meaningless there.
            var visible = _centralMode && _operatorPanelVisible
                          && !_augmentationMode;
            _presetBarRoot.SetActive(visible);
            if (!visible)
            {
                CloseDropdown(_presetDropdownRoot);
                ClosePresetPopup();
                return;
            }

            var rect = (RectTransform)_presetBarRoot.transform;
            rect.anchoredPosition = new Vector2(157f, 122f);
            _presetBarRoot.transform.SetSiblingIndex(0);
            _root?.transform.SetAsLastSibling();
        }

        private void HidePresetUi()
        {
            if (_presetBarRoot != null)
                _presetBarRoot.SetActive(false);
            if (_presetDropdownRoot != null)
                _presetDropdownRoot.SetActive(false);
            _presetPopup?.Hide();
        }

        private void DiscardPresetUi()
        {
            if (_presetBarRoot != null)
                Destroy(_presetBarRoot);
            if (_presetDropdownRoot != null)
                Destroy(_presetDropdownRoot);
            _presetPopup?.Discard();
            _presetPopup = null;
            _presetBarRoot = null;
            _presetDropdownRoot = null;
            _presetTitle = null;
            _presetSelectedLabel = null;
            _presetSummary = null;
            _presetSelectedButton = null;
            _presetSelectedRoot = null;
            _presetApplyButton = null;
            _presetSaveButton = null;
            _presetDeleteButton = null;
            _presetApplyLabel = null;
            _presetSaveLabel = null;
            _presetDeleteLabel = null;
            _presetDropdownRows.Clear();
        }

        private void ClosePresetPopup()
        {
            _presetPopup?.Close();
        }

        private void ConfirmPresetPopup()
        {
            _presetPopup?.Confirm();
        }

        private void RefreshPresetBar()
        {
            if (_presetBarRoot == null)
                return;
            ApplyPresetLayout();
            // The bar drives shuttle manifests while the shuttle hold is the
            // pane on screen: same six controls, same shape, different list.
            // See CentralManagementPanel.Shuttle.cs.
            if (ManifestBarMode)
            {
                RefreshManifestBar();
                ApplyPresetFonts();
                return;
            }
            _presetTitle.text = Localization.Get("qmcentral.preset_title");
            _presetApplyLabel.text = Localization.Get(
                "qmcentral.preset_apply");
            _presetSaveLabel.text = Localization.Get(
                "qmcentral.preset_save");
            _presetDeleteLabel.text = Localization.Get(
                "qmcentral.preset_delete");
            var selected = LoadoutPresetRepository.Selected;
            _presetSelectedLabel.text = selected?.Name
                                        ?? Localization.Get(
                                            "qmcentral.preset_none");
            _presetSummary.text = LoadoutPresetService.GetSummary(selected);
            var hasPreset = selected != null;
            _presetApplyButton.SetInteractable(hasPreset && _mercenary != null);
            _presetDeleteButton.SetInteractable(hasPreset);
            _presetSaveButton.SetInteractable(_mercenary != null);
            _presetSelectedButton.SetInteractable(
                LoadoutPresetRepository.Data.Presets.Count > 0);
            ApplyPresetFonts();
        }

        private void ApplyPresetFonts()
        {
            var font = Localization.GetActualFont();
            foreach (var text in new TMP_Text[]
                     {
                         _presetTitle, _presetSelectedLabel, _presetSummary,
                         _presetApplyLabel, _presetSaveLabel,
                         _presetDeleteLabel,
                     })
            {
                if (text != null)
                    text.font = font;
            }
            _presetPopup?.ApplyFont(font);
        }

        private void OnPresetApplyClicked()
        {
            if (ManifestBarMode)
                RestockShuttleFromBar();
            else
                PresetPopup.OpenApply();
        }

        private void OnPresetSaveClicked()
        {
            if (ManifestBarMode)
                OpenSaveManifest();
            else
                PresetPopup.OpenSave();
        }

        private void OnPresetDeleteClicked()
        {
            if (ManifestBarMode)
                OpenDeleteManifest();
            else
                PresetPopup.OpenDelete();
        }

        private void TogglePresetDropdown()
        {
            if (_presetDropdownRoot == null || UI.Drag.IsDragging)
                return;
            if (_presetDropdownRoot.activeSelf)
            {
                CloseDropdown(_presetDropdownRoot);
                return;
            }
            CloseAllDropdowns();
            RebuildPresetDropdown();
            _presetDropdownRoot.SetActive(true);
            _presetDropdownRoot.transform.SetAsLastSibling();
        }

        /// <summary>One option of the selector, mode-independent.</summary>
        private sealed class PresetOption
        {
            internal string Id;
            internal string Label;
        }

        private void RebuildPresetDropdown()
        {
            // Only the source differs between the two modes; the rows are
            // laid out once rather than in two near-copies.
            var options = new List<PresetOption>();
            string selectedId;
            Action<string> onSelect;
            if (ManifestBarMode)
            {
                foreach (var manifest in ShuttleManifestRepository.All
                             .Where(m => m != null)
                             .OrderByDescending(m => m.UpdatedUtcTicks))
                {
                    options.Add(new PresetOption
                    {
                        Id = manifest.Id,
                        Label = manifest.Name + "   "
                                + ShuttleManifestService.Summary(manifest),
                    });
                }
                selectedId = LoadoutPresetRepository.Data.SelectedManifestId;
                onSelect = ShuttleManifestRepository.Select;
            }
            else
            {
                foreach (var preset in LoadoutPresetRepository.Data.Presets
                             .Where(p => p != null)
                             .OrderByDescending(p => p.UpdatedUtcTicks))
                {
                    options.Add(new PresetOption
                    {
                        Id = preset.Id,
                        Label = preset.Name + "   "
                                + LoadoutPresetService.GetSummary(preset),
                    });
                }
                selectedId = LoadoutPresetRepository.Data.SelectedId;
                onSelect = LoadoutPresetRepository.Select;
            }

            PanelUi.ClearDropdownRows(_presetDropdownRows);
            const float width = 220f;
            const float rowHeight = 17f;
            var height = Mathf.Max(rowHeight + 4f,
                options.Count * rowHeight + 4f);
            PanelUi.SetTopLeft((RectTransform)_presetDropdownRoot.transform,
                72f, -17f, width, height);
            for (var i = 0; i < options.Count; i++)
            {
                var option = options[i];
                var row = PanelUi.CreateButtonRoot("Option" + i,
                    _presetDropdownRoot.transform, 2f,
                    -2f - i * rowHeight, width - 4f, rowHeight - 1f,
                    out var background, out var label);
                label.text = option.Label;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.margin = new Vector4(4f, 0f, 2f, 0f);
                label.fontSize = 6f;
                PanelUi.SetSurfaceSelected(background,
                    option.Id == selectedId);
                PanelUi.BindClick(row, () =>
                {
                    onSelect(option.Id);
                    CloseDropdown(_presetDropdownRoot);
                    RefreshPresetBar();
                });
                _presetDropdownRows.Add(row);
            }
        }
    }
}
