using System;
using System.Collections.Generic;
using System.Linq;
using MGSC;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace QM_CentralManagement
{
    internal sealed partial class CentralManagementPanel
    {
        private enum PresetPopupMode
        {
            None,
            Save,
            Apply,
            ForceApply,
            Delete,
            Message
        }

        private GameObject _presetBarRoot;
        private GameObject _presetDropdownRoot;
        private GameObject _presetOverlayRoot;
        private GameObject _presetPopupRoot;
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
        private TextMeshProUGUI _presetPopupTitle;
        private TextMeshProUGUI _presetPopupBody;
        private TMP_InputField _presetNameInput;
        private TextMeshProUGUI _presetNameText;
        private TMP_Text _presetNamePlaceholder;
        private TextMeshProUGUI _presetPopupConfirmLabel;
        private TextMeshProUGUI _presetPopupCancelLabel;
        private PresetPopupMode _presetPopupMode;
        private string _presetPendingId;

        private void BuildPresetUi(Transform barParent, Transform overlayParent)
        {
            if (_presetBarRoot != null)
                return;
            // The bar lives inside the inventory window subtree at its FIRST
            // sibling slot, so the auto-feeder panel (which lives deeper in
            // the same subtree) always draws above it, while the bar itself
            // keeps its exact visual position above the window.
            _presetBarRoot = CreateUiObject("QM_LoadoutPresets", barParent);
            var rect = (RectTransform)_presetBarRoot.transform;
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(1f, 0.5f);
            // Window-local equivalent of the old screen position (34, 102):
            // the window's centre is (-123, -20), so (34 + 123, 102 + 20).
            rect.anchoredPosition = new Vector2(157f, 122f);
            rect.sizeDelta = new Vector2(314f, 46f);
            _presetBarRoot.transform.SetSiblingIndex(0);
            var panel = VanillaSkin.Slice(_presetBarRoot,
                VanillaSkin.PanelFrame, PanelColor);
            panel.raycastTarget = true;

            _presetTitle = CreateText("Title", rect, 4f, -2f,
                66f, 14f, TextContext.IgnoreSize, 7f, BrightColor,
                TextAlignmentOptions.MidlineLeft);
            _presetSelectedRoot = CreateDropdownTrigger("Selected", rect,
                72f, -2f, 144f, 14f, out _presetSelectedLabel);
            _presetSelectedButton = ButtonOf(_presetSelectedRoot);
            BindClick(_presetSelectedRoot, TogglePresetDropdown);

            var applyRoot = CreateButtonRoot("Apply", rect, 218f, -2f,
                44f, 14f, "qmcentral.tip.preset_apply",
                out _, out _presetApplyLabel);
            _presetApplyButton = ButtonOf(applyRoot);
            BindClick(applyRoot, OpenApplyPresetPopup);
            var saveRoot = CreateButtonRoot("Save", rect, 264f, -2f,
                46f, 14f, "qmcentral.tip.preset_save",
                out _, out _presetSaveLabel);
            _presetSaveButton = ButtonOf(saveRoot);
            BindClick(saveRoot, OpenSavePresetPopup);

            _presetSummary = CreateText("Summary", rect, 4f, -18f,
                258f, 24f, TextContext.IgnoreSize, 6f, OffColor,
                TextAlignmentOptions.MidlineLeft);
            _presetDeleteLabel = null;
            var deleteRoot = CreateButtonRoot("Delete", rect, 264f, -18f,
                46f, 24f, "qmcentral.tip.preset_delete",
                out _, out _presetDeleteLabel);
            MakeDangerButton(deleteRoot, _presetDeleteLabel);
            _presetDeleteButton = ButtonOf(deleteRoot);
            BindClick(deleteRoot, OpenDeletePresetPopup);

            // Parented to the bar and anchored just under the trigger, so
            // it opens where the control is instead of at a fixed offset from
            // the panel centre -- which is what made it cover the summary line.
            _presetDropdownRoot = CreateUiObject("PresetDropdown", rect);
            var dropdownRect = (RectTransform)_presetDropdownRoot.transform;
            SetTopLeft(dropdownRect, 72f, -17f, 220f, 20f);
            var dropdownBackground = VanillaSkin.Slice(_presetDropdownRoot,
                VanillaSkin.ListBackground, PanelColor);
            dropdownBackground.raycastTarget = true;
            _presetDropdownRoot.SetActive(false);

            BuildPresetPopup(overlayParent);
            _presetBarRoot.SetActive(false);
        }

        private void BuildPresetPopup(Transform parent)
        {
            _presetOverlayRoot = CreateUiObject("PresetOverlay", parent);
            var overlayRect = (RectTransform)_presetOverlayRoot.transform;
            overlayRect.anchorMin = Vector2.zero;
            overlayRect.anchorMax = Vector2.one;
            overlayRect.offsetMin = Vector2.zero;
            overlayRect.offsetMax = Vector2.zero;
            var overlay = _presetOverlayRoot.AddComponent<Image>();
            overlay.color = VanillaSkin.Palette.Scrim;
            overlay.raycastTarget = true;
            var overlayButton = _presetOverlayRoot.AddComponent<Button>();
            ConfigureButton(overlayButton, overlay);
            overlayButton.onClick.AddListener(ClosePresetPopup);

            _presetPopupRoot = CreateUiObject("PresetPopup",
                _presetOverlayRoot.transform);
            var popupRect = (RectTransform)_presetPopupRoot.transform;
            popupRect.anchorMin = new Vector2(0.5f, 0.5f);
            popupRect.anchorMax = new Vector2(0.5f, 0.5f);
            popupRect.pivot = new Vector2(0.5f, 0.5f);
            popupRect.anchoredPosition = Vector2.zero;
            popupRect.sizeDelta = new Vector2(260f, 136f);
            var popup = VanillaSkin.Slice(_presetPopupRoot,
                VanillaSkin.PanelFrame, PanelColor);
            popup.raycastTarget = true;

            _presetPopupTitle = CreateText("Title", popupRect,
                8f, -6f, 244f, 18f, TextContext.WindowCaption, BrightColor,
                TextAlignmentOptions.MidlineLeft);
            _presetPopupBody = CreateText("Body", popupRect,
                8f, -27f, 244f, 52f, TextContext.IgnoreSize, 7f, ValueColor,
                TextAlignmentOptions.TopLeft);
            _presetPopupBody.enableWordWrapping = true;
            _presetPopupBody.overflowMode = TextOverflowModes.Ellipsis;

            var inputRoot = CreateUiObject("Name", popupRect);
            SetTopLeft((RectTransform)inputRoot.transform,
                8f, -82f, 244f, 20f);
            var inputBackground = VanillaSkin.Slice(inputRoot,
                VanillaSkin.ListBackground, CardColor);
            var viewport = CreateUiObject("Viewport", inputRoot.transform);
            var viewportRect = (RectTransform)viewport.transform;
            Stretch(viewportRect);
            viewportRect.offsetMin = new Vector2(4f, 1f);
            viewportRect.offsetMax = new Vector2(-4f, -1f);
            viewport.AddComponent<RectMask2D>();
            _presetNameText = CreateText("Text", viewport.transform,
                0f, 0f, 236f, 18f, TextContext.IgnoreSize, 7f, BrightColor,
                TextAlignmentOptions.MidlineLeft);
            var namePlaceholder = CreateText("Placeholder",
                viewport.transform, 0f, 0f, 236f, 18f,
                TextContext.IgnoreSize, 7f, OffColor,
                TextAlignmentOptions.MidlineLeft);
            namePlaceholder.fontStyle = FontStyles.Italic;
            _presetNamePlaceholder = namePlaceholder;
            _presetNameInput = inputRoot.AddComponent<TMP_InputField>();
            _presetNameInput.targetGraphic = inputBackground;
            _presetNameInput.textViewport = viewportRect;
            _presetNameInput.textComponent = _presetNameText;
            _presetNameInput.placeholder = _presetNamePlaceholder;
            _presetNameInput.lineType = TMP_InputField.LineType.SingleLine;
            _presetNameInput.characterLimit = 28;
            _presetNameInput.richText = false;
            _presetNameInput.onSelect.AddListener(_ =>
                Input.imeCompositionMode = IMECompositionMode.On);
            _presetNameInput.onDeselect.AddListener(_ =>
                _blockedInputReleaseFrame = Time.frameCount);

            var cancelRoot = CreateButtonRoot("Cancel", popupRect,
                8f, -108f, 118f, 20f, out _,
                out _presetPopupCancelLabel);
            BindClick(cancelRoot, ClosePresetPopup);
            var confirmRoot = CreateButtonRoot("Confirm", popupRect,
                134f, -108f, 118f, 20f, out _,
                out _presetPopupConfirmLabel);
            BindClick(confirmRoot, ConfirmPresetPopup);
            _presetOverlayRoot.SetActive(false);
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
            if (_presetOverlayRoot != null)
                _presetOverlayRoot.SetActive(false);
        }

        private void DiscardPresetUi()
        {
            if (_presetBarRoot != null)
                Destroy(_presetBarRoot);
            if (_presetDropdownRoot != null)
                Destroy(_presetDropdownRoot);
            if (_presetOverlayRoot != null)
                Destroy(_presetOverlayRoot);
            _presetBarRoot = null;
            _presetDropdownRoot = null;
            _presetOverlayRoot = null;
            _presetPopupRoot = null;
            _presetDropdownRows.Clear();
        }

        private void RefreshPresetBar()
        {
            if (_presetBarRoot == null)
                return;
            ApplyPresetLayout();
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
                         _presetDeleteLabel, _presetPopupTitle,
                         _presetPopupBody, _presetNameText,
                         _presetNamePlaceholder, _presetPopupConfirmLabel,
                         _presetPopupCancelLabel
                     })
            {
                if (text != null)
                    text.font = font;
            }
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
            CloseFilterDropdowns();
            CloseDropdown(_operatorDropdownRoot);
            RebuildPresetDropdown();
            _presetDropdownRoot.SetActive(true);
            _presetDropdownRoot.transform.SetAsLastSibling();
        }

        private void RebuildPresetDropdown()
        {
            ClearDropdownRows(_presetDropdownRows);
            var presets = LoadoutPresetRepository.Data.Presets
                .Where(p => p != null)
                .OrderByDescending(p => p.UpdatedUtcTicks).ToList();
            const float width = 220f;
            const float rowHeight = 17f;
            var height = Mathf.Max(rowHeight + 4f,
                presets.Count * rowHeight + 4f);
            SetTopLeft((RectTransform)_presetDropdownRoot.transform,
                72f, -17f, width, height);
            for (var i = 0; i < presets.Count; i++)
            {
                var preset = presets[i];
                var row = CreateButtonRoot("Preset" + i,
                    _presetDropdownRoot.transform, 2f,
                    -2f - i * rowHeight, width - 4f, rowHeight - 1f,
                    out var background, out var label);
                label.text = preset.Name + "   "
                             + LoadoutPresetService.GetSummary(preset);
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.margin = new Vector4(4f, 0f, 2f, 0f);
                label.fontSize = 6f;
                SetSurfaceSelected(background, preset.Id
                    == LoadoutPresetRepository.Data.SelectedId);
                BindClick(row, () =>
                {
                    LoadoutPresetRepository.Select(preset.Id);
                    CloseDropdown(_presetDropdownRoot);
                    RefreshPresetBar();
                });
                _presetDropdownRows.Add(row);
            }
        }

        private void OpenSavePresetPopup()
        {
            if (_mercenary == null)
                return;
            OpenPresetPopup(PresetPopupMode.Save,
                Localization.Get("qmcentral.preset_save_title"),
                Localization.Get("qmcentral.preset_save_body"),
                Localization.Get("qmcentral.preset_save_confirm"),
                LoadoutPresetRepository.Selected?.Name
                ?? LoadoutPresetRepository.SuggestName());
        }

        private void OpenApplyPresetPopup()
        {
            var preset = LoadoutPresetRepository.Selected;
            if (preset == null || _mercenary == null)
                return;
            var validation = LoadoutPresetService.Check(preset, _mercenary,
                _cargo, _progression);
            if (!validation.Success)
            {
                var issues = validation.AllIssues.ToList();
                // Leave enough room for the force-apply explanation. It is the
                // decision the player is making, so it must never be pushed
                // outside the fixed-height vanilla-style popup by diagnostics.
                var visibleIssueCount = validation.CanForce ? 4 : 7;
                var lines = issues.Take(visibleIssueCount).ToList();
                if (issues.Count > lines.Count)
                    lines.Add("…");
                if (validation.CanForce)
                {
                    _presetPendingId = preset.Id;
                    lines.Add(Localization.Get(
                        "qmcentral.preset_force_explanation"));
                    OpenPresetPopup(PresetPopupMode.ForceApply,
                        Localization.Get("qmcentral.preset_force_title"),
                        string.Join("\n", lines),
                        Localization.Get("qmcentral.preset_force_confirm"));
                }
                else
                {
                    OpenPresetPopup(PresetPopupMode.Message,
                        Localization.Get("qmcentral.preset_missing_title"),
                        string.Join("\n", lines),
                        Localization.Get("qmcentral.preset_close"));
                }
                return;
            }
            _presetPendingId = preset.Id;
            OpenPresetPopup(PresetPopupMode.Apply,
                Localization.Get("qmcentral.preset_apply_title"),
                string.Format(Localization.Get(
                    "qmcentral.preset_apply_body"), preset.Name,
                    LoadoutPresetService.GetSummary(preset)),
                Localization.Get("qmcentral.preset_apply_confirm"));
        }

        private void OpenDeletePresetPopup()
        {
            var preset = LoadoutPresetRepository.Selected;
            if (preset == null)
                return;
            _presetPendingId = preset.Id;
            OpenPresetPopup(PresetPopupMode.Delete,
                Localization.Get("qmcentral.preset_delete_title"),
                string.Format(Localization.Get(
                    "qmcentral.preset_delete_body"), preset.Name),
                Localization.Get("qmcentral.preset_delete_confirm"));
        }

        private void OpenPresetPopup(PresetPopupMode mode, string title,
            string body, string confirm, string input = null)
        {
            if (_presetOverlayRoot == null)
                return;
            CloseDropdown(_presetDropdownRoot);
            CloseFilterDropdowns();
            CloseDropdown(_operatorDropdownRoot);
            _presetPopupMode = mode;
            _presetPopupTitle.text = title;
            _presetPopupBody.text = body;
            _presetPopupConfirmLabel.text = confirm;
            _presetPopupCancelLabel.text = Localization.Get(
                "qmcentral.preset_cancel");
            var showInput = mode == PresetPopupMode.Save;
            _presetNameInput.gameObject.SetActive(showInput);
            if (showInput)
            {
                _presetNamePlaceholder.text = Localization.Get(
                    "qmcentral.preset_name_placeholder");
                _presetNameInput.SetTextWithoutNotify(input ?? string.Empty);
            }
            _presetPopupCancelLabel.transform.parent.gameObject.SetActive(
                mode != PresetPopupMode.Message);
            _presetOverlayRoot.SetActive(true);
            _presetOverlayRoot.transform.SetAsLastSibling();
            ApplyPresetFonts();
            UI.Drag.Pause(0.18f);
            if (showInput)
            {
                _presetNameInput.ActivateInputField();
                _presetNameInput.Select();
            }
        }

        private void ConfirmPresetPopup()
        {
            var mode = _presetPopupMode;
            if (mode == PresetPopupMode.None)
                return;
            if (mode == PresetPopupMode.Message)
            {
                ClosePresetPopup();
                return;
            }
            if (mode == PresetPopupMode.Save)
            {
                var snapshot = LoadoutPresetService.Capture(_mercenary);
                if (snapshot != null)
                {
                    LoadoutPresetRepository.Save(_presetNameInput.text,
                        snapshot);
                    ClosePresetPopup();
                    RefreshPresetBar();
                }
                return;
            }
            var preset = LoadoutPresetRepository.Data.Presets.FirstOrDefault(
                p => p != null && p.Id == _presetPendingId);
            if (mode == PresetPopupMode.Delete)
            {
                LoadoutPresetRepository.Delete(_presetPendingId);
                ClosePresetPopup();
                RefreshPresetBar();
                return;
            }
            if ((mode == PresetPopupMode.Apply
                 || mode == PresetPopupMode.ForceApply) && preset != null)
            {
                var result = LoadoutPresetService.Apply(preset, _mercenary,
                    _cargo, _progression, _spaceTime, _perkFactory,
                    mode == PresetPopupMode.ForceApply);
                ClosePresetPopup();
                _screen.RefreshView();
                RebuildAndRefresh();
                RefreshPresetBar();
                if (result.Success)
                    PlayPresetAppliedSounds(preset);
                if (!result.Success)
                {
                    OpenPresetPopup(PresetPopupMode.Message,
                        Localization.Get("qmcentral.preset_error_title"),
                        result.Message,
                        Localization.Get("qmcentral.preset_close"));
                }
            }
        }

        private static void PlayPresetAppliedSounds(LoadoutPreset preset)
        {
            var controller = SingletonMonoBehaviour<SoundController>.Instance;
            var sounds = SingletonMonoBehaviour<SoundsStorage>.Instance;
            if (controller == null || sounds == null)
                return;

            var equipmentIds = (preset?.Equipment
                                ?? new List<LoadoutEquipmentEntry>())
                .Where(entry => entry != null
                                && !string.IsNullOrWhiteSpace(entry.ItemId))
                .Select(entry => entry.ItemId)
                .ToList();
            var hasWeapon = equipmentIds.Any(id =>
                (Data.Items.GetRecord(id) as CompositeItemRecord)
                ?.GetRecord<WeaponRecord>() != null);
            var hasOrdinaryEquipment = equipmentIds.Any(id =>
                (Data.Items.GetRecord(id) as CompositeItemRecord)
                ?.GetRecord<WeaponRecord>() == null);
            var hasBodyEquipment = (preset?.Augmentations?.Count ?? 0) > 0
                                   || (preset?.Implants?.Count ?? 0) > 0;

            if (hasWeapon)
                controller.PlayUiSound(sounds.EquipWeapon, isUnique: true);
            if (hasOrdinaryEquipment || hasBodyEquipment
                || preset?.IncludesCarriedItems == true)
                controller.PlayUiSound(sounds.TakeItem, isUnique: true);
        }

        private void ClosePresetPopup()
        {
            if (_presetOverlayRoot == null)
                return;
            var visible = _presetOverlayRoot.activeInHierarchy;
            _presetNameInput?.DeactivateInputField();
            _presetOverlayRoot.SetActive(false);
            _presetPopupMode = PresetPopupMode.None;
            _presetPendingId = null;
            if (visible)
                ConsumePointerRelease();
        }
    }
}
