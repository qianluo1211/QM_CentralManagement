using System;
using System.Collections.Generic;
using System.Linq;
using MGSC;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace QM_CentralManagement
{
    /// <summary>
    /// Pre-departure loadout strip anchored to ArsenalScreen's vanilla
    /// equipment window. It lives in the always-available ship arsenal (the
    /// "select equipment" step of PrepareRaidScreen) and is deliberately NOT
    /// part of the tech-gated central panel: a fresh run can save and apply
    /// loadouts from the very start.
    ///
    /// The bar is a sibling of the inventory window, placed against the
    /// window's real on-screen rect every time it is shown, so it follows the
    /// vanilla layout without depending on any hardcoded screen coordinates.
    /// </summary>
    internal sealed class ShipLoadoutBar : MonoBehaviour
    {
        private enum PopupMode
        {
            None,
            Save,
            Apply,
            ForceApply,
            Delete,
            Message
        }

        private const float BarWidth = 300f;
        private const float BarHeight = 22f;
        private const float MinBarWidth = 260f;
        // Clearance between the strip and the equipment window. One item
        // cell, mirroring the central panel's preset bar: the strip must
        // read as its own row above the inventory, never as part of it.
        private const float AboveGap = 24f;
        // When the inventory/shuttle tab strip is present above the window,
        // the bar hugs the tab strip's real top edge instead of keeping the
        // one-cell clearance -- the tabs are the bar's direct neighbour.
        private const float TabsGap = 3f;
        private const float BelowGap = 3f;
        private const float PopupWidth = 260f;
        private const float PopupHeight = 136f;

        // Horizontal layout, right-anchored: the buttons stick to the bar's
        // right edge and the preset selector stretches between the title and
        // the button group, so the strip aligns to any inventory window width.
        private const float TitleWidth = 78f;
        private const float SelectedLeft = 82f;
        private const float RightGroupWidth = 150f;

        private static ShipLoadoutBar _active;
        private static int _releaseBlockFrame = -1;

        private ArsenalScreen _screen;
        private Mercenary _mercenary;
        private MagnumCargo _cargo;
        private SpaceTime _spaceTime;
        private MagnumProgression _progression;
        private RectTransform _windowRect;
        private RectTransform _tabsRect;
        private bool _built;
        private bool _visible;

        private GameObject _root;
        private TextMeshProUGUI _titleLabel;
        private GameObject _selectedRoot;
        private TextMeshProUGUI _selectedLabel;
        private CommonButton _selectedButton;
        private GameObject _applyRoot;
        private TextMeshProUGUI _applyLabel;
        private CommonButton _applyButton;
        private GameObject _saveRoot;
        private TextMeshProUGUI _saveLabel;
        private CommonButton _saveButton;
        private GameObject _deleteRoot;
        private TextMeshProUGUI _deleteLabel;
        private CommonButton _deleteButton;
        private GameObject _dropdownRoot;
        private readonly List<GameObject> _dropdownRows =
            new List<GameObject>();
        private GameObject _overlayRoot;
        private GameObject _popupRoot;
        private TextMeshProUGUI _popupTitle;
        private TextMeshProUGUI _popupBody;
        private TextMeshProUGUI _popupConfirmLabel;
        private TextMeshProUGUI _popupCancelLabel;
        private TextMeshProUGUI _nameText;
        private TMP_Text _namePlaceholder;
        private TMP_InputField _nameInput;
        private PopupMode _popupMode;
        private string _pendingId;

        /// <summary>
        /// Blocks the game's own hotkey handling while this bar owns the
        /// keyboard, mirroring CentralManagementPanel.ShouldBlockGameInput.
        /// </summary>
        internal static bool AnyInputCaptured =>
            (_active != null && _active._visible
             && ((_active._nameInput != null
                  && _active._nameInput.isFocused)
                 || (_active._dropdownRoot != null
                     && _active._dropdownRoot.activeSelf)
                 || (_active._overlayRoot != null
                     && _active._overlayRoot.activeSelf)))
            || Time.frameCount == _releaseBlockFrame;

        internal static void RefreshFor(ArsenalScreen screen)
        {
            try
            {
                if (screen == null)
                    return;
                if (!Plugin.ShipLoadoutsEnabled)
                {
                    HideFor(screen);
                    return;
                }
                var bar = screen.GetComponent<ShipLoadoutBar>()
                          ?? screen.gameObject.AddComponent<ShipLoadoutBar>();
                bar.Refresh(screen);
            }
            catch (Exception e)
            {
                Debug.LogError(Plugin.LogPrefix
                               + "ship loadout bar refresh failed: " + e);
            }
        }

        internal static void HideFor(ScreenWithShipCargo screen)
        {
            try
            {
                if (screen == null)
                    return;
                screen.GetComponent<ShipLoadoutBar>()?.Hide();
            }
            catch (Exception e)
            {
                Debug.LogError(Plugin.LogPrefix
                               + "ship loadout bar cleanup failed: " + e);
            }
        }

        private void Refresh(ArsenalScreen screen)
        {
            _screen = screen;
            var panel = screen.GetComponent<CentralManagementPanel>();
            var centralActive = panel != null && panel.IsCentralMode;
            var window = Plugin.InventoryWindowOf(screen);
            var view = Plugin.InventoryViewOf(screen);
            var mercenary = Plugin.ScreenMercenaryOf(screen);
            var cargo = Plugin.ScreenCargoOf(screen);
            var windowActive = window != null && window.activeSelf;
            var viewActive = view != null && view.gameObject.activeSelf;
            var usable = !centralActive && windowActive && viewActive
                         && mercenary != null && cargo != null
                         && cargo.ShipCargo.Count > 0;

            if (!usable)
            {
                Hide();
                return;
            }

            try
            {
                if (!_built)
                    Build(window.transform);
            }
            catch (Exception e)
            {
                Debug.LogError(Plugin.LogPrefix
                               + "ship loadout bar build failed: " + e);
                _built = false;
                Hide();
                return;
            }
            if (_root == null)
            {
                Hide();
                return;
            }

            _mercenary = mercenary;
            _cargo = cargo;
            _spaceTime = Plugin.ScreenSpaceTimeOf(screen);
            _progression = Plugin.ScreenProgressionOf(screen);
            _windowRect = window.transform as RectTransform;
            _tabsRect = Plugin.InventoryTabsViewOf(screen)?.transform
                as RectTransform;

            _active = this;
            PlaceBar();
            // First child of the inventory window: the strip draws BELOW
            // every piece of window content (caption, feeder panel, grids),
            // so nothing that expands inside the window can ever be hidden
            // behind it. It sits outside the window rect anyway.
            _root.transform.SetSiblingIndex(0);
            RefreshLabels();
            _root.SetActive(true);
            _visible = true;
        }

        internal void Hide()
        {
            ClosePopup();
            CloseDropdown(_dropdownRoot);
            if (_root != null)
                _root.SetActive(false);
            _visible = false;
            if (ReferenceEquals(_active, this))
                _active = null;
        }

        private void Build(Transform parent)
        {
            if (_built)
                return;
            try
            {
                VanillaSkin.Seed(parent);

                _root = CreateUiObject("QM_ShipLoadoutBar", parent);
                var rect = (RectTransform)_root.transform;
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(BarWidth, BarHeight);
                rect.localScale = Vector3.one;
                rect.localRotation = Quaternion.identity;
                var panel = VanillaSkin.Slice(_root,
                    VanillaSkin.PanelFrame, PanelColor);
                panel.raycastTarget = true;

                _titleLabel = CreateText("Title", rect, 2f, 0f,
                    TitleWidth, BarHeight, TextContext.IgnoreSize, 7f,
                    BrightColor, TextAlignmentOptions.MidlineLeft);
                _selectedRoot = CreateDropdownTrigger("Selected", rect,
                    SelectedLeft, RightGroupWidth, out _selectedLabel);
                _selectedButton = ButtonOf(_selectedRoot);
                BindClick(_selectedRoot, ToggleDropdown);

                _applyRoot = CreateButtonRoot("Apply", rect, 0f, 0f,
                    44f, 18f, "qmcentral.tip.preset_apply", out _,
                    out _applyLabel);
                SetTopRight((RectTransform)_applyRoot.transform, 104f, 2f,
                    44f, 18f);
                _applyButton = ButtonOf(_applyRoot);
                BindClick(_applyRoot, OpenApplyPopup);
                _saveRoot = CreateButtonRoot("Save", rect, 0f, 0f,
                    52f, 18f, "qmcentral.tip.preset_save", out _,
                    out _saveLabel);
                SetTopRight((RectTransform)_saveRoot.transform, 50f, 2f,
                    52f, 18f);
                _saveButton = ButtonOf(_saveRoot);
                BindClick(_saveRoot, OpenSavePopup);
                _deleteRoot = CreateButtonRoot("Delete", rect, 0f, 0f,
                    46f, 18f, "qmcentral.tip.preset_delete", out _,
                    out _deleteLabel);
                SetTopRight((RectTransform)_deleteRoot.transform, 2f, 2f,
                    46f, 18f);
                MakeDangerButton(_deleteRoot, _deleteLabel);
                _deleteButton = ButtonOf(_deleteRoot);
                BindClick(_deleteRoot, OpenDeletePopup);

                _dropdownRoot = CreateUiObject("PresetDropdown", rect);
                var dropdownRect = (RectTransform)_dropdownRoot.transform;
                SetTopLeft(dropdownRect, SelectedLeft, -21f, 200f, 21f);
                var dropdownBackground = VanillaSkin.Slice(_dropdownRoot,
                    VanillaSkin.ListBackground, PanelColor);
                dropdownBackground.raycastTarget = true;
                _dropdownRoot.SetActive(false);

                BuildPopup(parent);

                _built = true;
                _root.SetActive(false);
            }
            catch
            {
                Discard();
                throw;
            }
        }

        private void BuildPopup(Transform screenParent)
        {
            // The modal scrim hangs off the global canvas root so it covers
            // the whole screen no matter how deep the equipment window sits.
            Transform overlayParent = screenParent;
            var screenRoot = UI.ScreenRoot;
            if (screenRoot != null)
                overlayParent = screenRoot;

            _overlayRoot = CreateUiObject("ShipLoadoutOverlay",
                overlayParent);
            var overlayRect = (RectTransform)_overlayRoot.transform;
            Stretch(overlayRect);
            var overlay = _overlayRoot.AddComponent<Image>();
            overlay.color = VanillaSkin.Palette.Scrim;
            overlay.raycastTarget = true;
            var overlayButton = _overlayRoot.AddComponent<Button>();
            ConfigureButton(overlayButton, overlay);
            overlayButton.onClick.AddListener(ClosePopup);

            _popupRoot = CreateUiObject("ShipLoadoutPopup",
                _overlayRoot.transform);
            var popupRect = (RectTransform)_popupRoot.transform;
            popupRect.anchorMin = new Vector2(0.5f, 0.5f);
            popupRect.anchorMax = new Vector2(0.5f, 0.5f);
            popupRect.pivot = new Vector2(0.5f, 0.5f);
            popupRect.anchoredPosition = Vector2.zero;
            popupRect.sizeDelta = new Vector2(PopupWidth, PopupHeight);
            var popup = VanillaSkin.Slice(_popupRoot,
                VanillaSkin.PanelFrame, PanelColor);
            popup.raycastTarget = true;

            _popupTitle = CreateText("Title", popupRect, 8f, -6f, 244f,
                18f, TextContext.WindowCaption, 0f, BrightColor,
                TextAlignmentOptions.MidlineLeft);
            _popupBody = CreateText("Body", popupRect, 8f, -27f, 244f, 52f,
                TextContext.IgnoreSize, 7f, ValueColor,
                TextAlignmentOptions.TopLeft);
            _popupBody.enableWordWrapping = true;
            _popupBody.overflowMode = TextOverflowModes.Ellipsis;

            var inputRoot = CreateUiObject("Name", popupRect);
            SetTopLeft((RectTransform)inputRoot.transform, 8f, -82f, 244f,
                20f);
            var inputBackground = VanillaSkin.Slice(inputRoot,
                VanillaSkin.ListBackground, CardColor);
            var viewport = CreateUiObject("Viewport", inputRoot.transform);
            var viewportRect = (RectTransform)viewport.transform;
            Stretch(viewportRect);
            viewportRect.offsetMin = new Vector2(4f, 1f);
            viewportRect.offsetMax = new Vector2(-4f, -1f);
            viewport.AddComponent<RectMask2D>();
            _nameText = CreateText("Text", viewport.transform, 0f, 0f,
                236f, 18f, TextContext.IgnoreSize, 7f, BrightColor,
                TextAlignmentOptions.MidlineLeft);
            var placeholder = CreateText("Placeholder",
                viewport.transform, 0f, 0f, 236f, 18f,
                TextContext.IgnoreSize, 7f, OffColor,
                TextAlignmentOptions.MidlineLeft);
            placeholder.fontStyle = FontStyles.Italic;
            _namePlaceholder = placeholder;
            _nameInput = inputRoot.AddComponent<TMP_InputField>();
            _nameInput.targetGraphic = inputBackground;
            _nameInput.textViewport = viewportRect;
            _nameInput.textComponent = _nameText;
            _nameInput.placeholder = _namePlaceholder;
            _nameInput.lineType = TMP_InputField.LineType.SingleLine;
            _nameInput.characterLimit = 28;
            _nameInput.richText = false;
            _nameInput.onSelect.AddListener(_ =>
                Input.imeCompositionMode = IMECompositionMode.On);
            _nameInput.onDeselect.AddListener(_ =>
                _releaseBlockFrame = Time.frameCount);

            var cancelRoot = CreateButtonRoot("Cancel", popupRect, 8f,
                -108f, 118f, 20f, out _, out _popupCancelLabel);
            BindClick(cancelRoot, ClosePopup);
            var confirmRoot = CreateButtonRoot("Confirm", popupRect, 134f,
                -108f, 118f, 20f, out _, out _popupConfirmLabel);
            BindClick(confirmRoot, ConfirmPopup);
            _overlayRoot.SetActive(false);
        }

        private void Discard()
        {
            if (_root != null)
                Destroy(_root);
            if (_overlayRoot != null)
                Destroy(_overlayRoot);
            _root = null;
            _dropdownRoot = null;
            _dropdownOriginalParent = null;
            _overlayRoot = null;
            _popupRoot = null;
            _dropdownRows.Clear();
            _built = false;
        }

        private void OnDestroy()
        {
            // The overlay is parented to the global UI.ScreenRoot, which
            // outlives this pooled (non-singleton) screen. Destroy it here
            // or every space/dungeon loop switch leaks one popup group.
            if (_overlayRoot != null)
                Destroy(_overlayRoot);
            _overlayRoot = null;
            _popupRoot = null;
            _dropdownOriginalParent = null;
            if (ReferenceEquals(_active, this))
                _active = null;
        }

        /// <summary>
        /// Positions the bar against the equipment window with local
        /// anchoring: the bar is a CHILD of the window, pinned to its top
        /// edge (falling back below when the window reaches the screen
        /// top), centred and at least as wide as the window. No world-space
        /// corner math is involved, so pooled-screen layout passes can
        /// never leave the bar floating over the inventory itself.
        /// </summary>
        private void PlaceBar()
        {
            if (_root == null || _windowRect == null)
                return;
            var rect = (RectTransform)_root.transform;
            var offset = AboveGap + Plugin.LoadoutBarOffsetY;

            // Clearance: the strip must clear both the window's top edge and
            // the inventory/shuttle tab strip above it when that strip is
            // visible (the pre-departure arsenal shows both tabs).
            var windowTop = _windowRect.rect.yMax;
            var clearanceTop = windowTop;
            if (_tabsRect != null && _tabsRect.gameObject.activeSelf)
            {
                var tabsCorners = new Vector3[4];
                _tabsRect.GetWorldCorners(tabsCorners);
                var tabsTop = _windowRect.InverseTransformPoint(
                    tabsCorners[1]).y;
                if (tabsTop > clearanceTop)
                    clearanceTop = tabsTop;
            }
            var raise = clearanceTop - windowTop;
            // Above the tab strip the bar sits snug against the strip's real
            // top edge; above a bare window it keeps the one-cell clearance.
            var effectiveOffset = (raise > 0f ? TabsGap : AboveGap)
                                  + Plugin.LoadoutBarOffsetY;

            if (HasRoomAbove(effectiveOffset + raise))
            {
                // Pivot at the bar's bottom edge, resting just above the
                // highest element it must clear.
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 0f);
                rect.anchoredPosition = new Vector2(0f,
                    effectiveOffset + raise);
            }
            else
            {
                // Pivot at the bar's top edge, hanging just under the
                // window's bottom edge.
                rect.anchorMin = new Vector2(0.5f, 0f);
                rect.anchorMax = new Vector2(0.5f, 0f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.anchoredPosition = new Vector2(0f,
                    -(BelowGap + Plugin.LoadoutBarOffsetY));
            }

            rect.sizeDelta = new Vector2(
                Mathf.Max(MinBarWidth, _windowRect.rect.width), BarHeight);
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
            LogPlacement();
        }

        private string _lastPlacement;

        /// <summary>
        /// Temporary always-on diagnostic: logs the window, bar and screen
        /// geometry in WORLD corners once per distinct placement, so
        /// misalignment can be diagnosed from Player.log alone.
        /// </summary>
        private void LogPlacement()
        {
            var rect = (RectTransform)_root.transform;
            var text = "ship loadout bar placed: window world="
                       + FormatWorld(_windowRect)
                       + " (local=" + FormatRect(_windowRect.rect)
                       + ", anchor=" + _windowRect.anchorMin + "/"
                       + _windowRect.anchorMax + ", pos="
                       + _windowRect.anchoredPosition + ")"
                       + " | bar world=" + FormatWorld(rect)
                       + " (pivot=" + rect.pivot + ", pos="
                       + rect.anchoredPosition + ", size=" + rect.sizeDelta
                       + ")";
            var caption = _windowRect.Find("CaptionBlock") as RectTransform;
            if (caption != null)
                text += " | caption world=" + FormatWorld(caption);
            var screenRoot = UI.ScreenRoot;
            if (screenRoot != null)
                text += " | screen world=" + FormatWorld(screenRoot);
            if (text == _lastPlacement)
                return;
            _lastPlacement = text;
            Plugin.DebugLog(text);
        }

        private static string FormatRect(Rect rect)
        {
            return string.Format("(x={0:F0},y={1:F0},w={2:F0},h={3:F0})",
                rect.x, rect.y, rect.width, rect.height);
        }

        private static string FormatWorld(RectTransform transform)
        {
            if (transform == null)
                return "null";
            var corners = new Vector3[4];
            transform.GetWorldCorners(corners);
            return string.Format("BL=({0:F0},{1:F0}) TR=({2:F0},{3:F0})",
                corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        /// <summary>
        /// Only the flip decision uses world corners, and only as a
        /// heuristic: a degenerate or unreadable rect defaults to "above",
        /// which is the correct answer for the arsenal window.
        /// </summary>
        private bool HasRoomAbove(float offset)
        {
            try
            {
                var scale = Mathf.Abs(_windowRect.lossyScale.y);
                if (scale < 0.001f)
                    scale = 1f;
                var corners = new Vector3[4];
                _windowRect.GetWorldCorners(corners);
                if (corners[1].y <= corners[0].y)
                    return true;
                var screenRoot = UI.ScreenRoot;
                if (screenRoot == null)
                    return true;
                var screen = new Vector3[4];
                screenRoot.GetWorldCorners(screen);
                if (screen[1].y <= screen[0].y)
                    return true;
                return corners[1].y + (offset + BarHeight) * scale
                       <= screen[1].y - 2f * scale;
            }
            catch
            {
                return true;
            }
        }

        private void RefreshLabels()
        {
            if (_root == null)
                return;
            _titleLabel.text = Localization.Get("qmcentral.preset_title");
            _applyLabel.text = Localization.Get("qmcentral.preset_apply");
            _saveLabel.text = Localization.Get("qmcentral.preset_save");
            _deleteLabel.text = Localization.Get("qmcentral.preset_delete");
            var selected = LoadoutPresetRepository.Selected;
            _selectedLabel.text = selected?.Name
                                  ?? Localization.Get(
                                      "qmcentral.preset_none");
            var hasPreset = selected != null;
            _applyButton.SetInteractable(hasPreset && _mercenary != null);
            _deleteButton.SetInteractable(hasPreset);
            _saveButton.SetInteractable(_mercenary != null);
            _selectedButton.SetInteractable(
                LoadoutPresetRepository.Data.Presets.Count > 0);
            ApplyFonts();
        }

        private void ApplyFonts()
        {
            var font = Localization.GetActualFont();
            foreach (var text in new[]
                     {
                         _titleLabel, _selectedLabel, _applyLabel,
                         _saveLabel, _deleteLabel, _popupTitle, _popupBody,
                         _nameText, _namePlaceholder, _popupConfirmLabel,
                         _popupCancelLabel
                     })
            {
                if (text != null)
                    text.font = font;
            }
        }

        private void ToggleDropdown()
        {
            if (_dropdownRoot == null || UI.Drag.IsDragging)
                return;
            if (_dropdownRoot.activeSelf)
            {
                CloseDropdown(_dropdownRoot);
                return;
            }
            RebuildDropdown();
            RaiseDropdownToTop();
            _dropdownRoot.SetActive(true);
            _dropdownRoot.transform.SetAsLastSibling();
        }

        private Transform _dropdownOriginalParent;

        /// <summary>
        /// While open, the list moves to UI.ScreenRoot as its LAST child
        /// (world position preserved), so it draws above the inventory window
        /// it visually overlaps -- the bar itself sits at sibling 0 and would
        /// otherwise leave its children underneath the window content.
        /// </summary>
        private void RaiseDropdownToTop()
        {
            if (_dropdownRoot == null || _dropdownOriginalParent != null)
                return;
            var screenRoot = UI.ScreenRoot;
            if (screenRoot == null)
                return;
            _dropdownOriginalParent = _dropdownRoot.transform.parent;
            var corners = new Vector3[4];
            ((RectTransform)_dropdownRoot.transform).GetWorldCorners(corners);
            var center = (corners[0] + corners[2]) * 0.5f;
            _dropdownRoot.transform.SetParent(screenRoot,
                worldPositionStays: false);
            var rect = (RectTransform)_dropdownRoot.transform;
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.position = new Vector3(center.x, center.y, corners[0].z);
        }

        private void RebuildDropdown()
        {
            ClearDropdownRows(_dropdownRows);
            var presets = LoadoutPresetRepository.Data.Presets
                .Where(p => p != null)
                .OrderByDescending(p => p.UpdatedUtcTicks).ToList();
            const float width = 200f;
            const float rowHeight = 17f;
            var height = Mathf.Max(rowHeight + 4f,
                presets.Count * rowHeight + 4f);
            SetTopLeft((RectTransform)_dropdownRoot.transform,
                SelectedLeft, -21f, width, height);
            for (var i = 0; i < presets.Count; i++)
            {
                var preset = presets[i];
                var row = CreateButtonRoot("Preset" + i,
                    _dropdownRoot.transform, 2f, -2f - i * rowHeight,
                    width - 4f, rowHeight - 1f, out var background,
                    out var label);
                label.text = preset.Name + "   "
                             + LoadoutPresetService.GetSummary(preset);
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.margin = new Vector4(4f, 0f, 2f, 0f);
                label.fontSize = 6f;
                SetSurfaceSelected(background,
                    preset.Id == LoadoutPresetRepository.Data.SelectedId);
                BindClick(row, () =>
                {
                    LoadoutPresetRepository.Select(preset.Id);
                    CloseDropdown(_dropdownRoot);
                    RefreshLabels();
                });
                _dropdownRows.Add(row);
            }
        }

        private void OpenSavePopup()
        {
            if (_mercenary == null)
                return;
            OpenPopup(PopupMode.Save,
                Localization.Get("qmcentral.preset_save_title"),
                Localization.Get("qmcentral.preset_save_body"),
                Localization.Get("qmcentral.preset_save_confirm"),
                LoadoutPresetRepository.Selected?.Name
                ?? LoadoutPresetRepository.SuggestName());
        }

        private void OpenApplyPopup()
        {
            var preset = LoadoutPresetRepository.Selected;
            if (preset == null || _mercenary == null)
                return;
            var validation = LoadoutPresetService.Check(preset, _mercenary,
                _cargo, _progression);
            if (!validation.Success)
            {
                var issues = validation.AllIssues.ToList();
                var visibleIssueCount = validation.CanForce ? 4 : 7;
                var lines = issues.Take(visibleIssueCount).ToList();
                if (issues.Count > lines.Count)
                    lines.Add("…");
                if (validation.CanForce)
                {
                    _pendingId = preset.Id;
                    lines.Add(Localization.Get(
                        "qmcentral.preset_force_explanation"));
                    OpenPopup(PopupMode.ForceApply,
                        Localization.Get("qmcentral.preset_force_title"),
                        string.Join("\n", lines),
                        Localization.Get("qmcentral.preset_force_confirm"));
                }
                else
                {
                    OpenPopup(PopupMode.Message,
                        Localization.Get("qmcentral.preset_missing_title"),
                        string.Join("\n", lines),
                        Localization.Get("qmcentral.preset_close"));
                }
                return;
            }
            _pendingId = preset.Id;
            OpenPopup(PopupMode.Apply,
                Localization.Get("qmcentral.preset_apply_title"),
                string.Format(Localization.Get(
                    "qmcentral.preset_apply_body"), preset.Name,
                    LoadoutPresetService.GetSummary(preset)),
                Localization.Get("qmcentral.preset_apply_confirm"));
        }

        private void OpenDeletePopup()
        {
            var preset = LoadoutPresetRepository.Selected;
            if (preset == null)
                return;
            _pendingId = preset.Id;
            OpenPopup(PopupMode.Delete,
                Localization.Get("qmcentral.preset_delete_title"),
                string.Format(Localization.Get(
                    "qmcentral.preset_delete_body"), preset.Name),
                Localization.Get("qmcentral.preset_delete_confirm"));
        }

        private void OpenPopup(PopupMode mode, string title, string body,
            string confirm, string input = null)
        {
            if (_overlayRoot == null)
                return;
            CloseDropdown(_dropdownRoot);
            _popupMode = mode;
            _popupTitle.text = title;
            _popupBody.text = body;
            _popupConfirmLabel.text = confirm;
            _popupCancelLabel.text = Localization.Get(
                "qmcentral.preset_cancel");
            var showInput = mode == PopupMode.Save;
            _nameInput.gameObject.SetActive(showInput);
            if (showInput)
            {
                _namePlaceholder.text = Localization.Get(
                    "qmcentral.preset_name_placeholder");
                _nameInput.SetTextWithoutNotify(input ?? string.Empty);
            }
            _popupCancelLabel.transform.parent.gameObject.SetActive(
                mode != PopupMode.Message);
            _overlayRoot.SetActive(true);
            _overlayRoot.transform.SetAsLastSibling();
            ApplyFonts();
            UI.Drag.Pause(0.18f);
            if (showInput)
            {
                _nameInput.ActivateInputField();
                _nameInput.Select();
            }
        }

        private void ConfirmPopup()
        {
            var mode = _popupMode;
            if (mode == PopupMode.None)
                return;
            if (mode == PopupMode.Message)
            {
                ClosePopup();
                return;
            }
            if (mode == PopupMode.Save)
            {
                var snapshot = LoadoutPresetService.Capture(_mercenary);
                if (snapshot != null)
                {
                    LoadoutPresetRepository.Save(_nameInput.text,
                        snapshot);
                    ClosePopup();
                    RefreshLabels();
                }
                return;
            }
            var preset = LoadoutPresetRepository.Data.Presets.FirstOrDefault(
                p => p != null && p.Id == _pendingId);
            if (mode == PopupMode.Delete)
            {
                LoadoutPresetRepository.Delete(_pendingId);
                ClosePopup();
                RefreshLabels();
                return;
            }
            if ((mode == PopupMode.Apply
                 || mode == PopupMode.ForceApply) && preset != null)
            {
                var result = LoadoutPresetService.Apply(preset, _mercenary,
                    _cargo, _progression, _spaceTime,
                    Plugin.ScreenPerkFactoryOf(_screen),
                    mode == PopupMode.ForceApply);
                ClosePopup();
                if (_screen != null)
                    _screen.RefreshView();
                RefreshLabels();
                if (result.Success)
                    PlayAppliedSounds(preset);
                if (!result.Success)
                {
                    OpenPopup(PopupMode.Message,
                        Localization.Get("qmcentral.preset_error_title"),
                        result.Message,
                        Localization.Get("qmcentral.preset_close"));
                }
            }
        }

        private void ClosePopup()
        {
            if (_overlayRoot == null)
                return;
            var wasVisible = _overlayRoot.activeInHierarchy;
            _nameInput?.DeactivateInputField();
            _overlayRoot.SetActive(false);
            _popupMode = PopupMode.None;
            _pendingId = null;
            if (wasVisible)
                ConsumePointerRelease();
        }

        private void Update()
        {
            if (_overlayRoot != null && _overlayRoot.activeInHierarchy)
            {
                UI.Drag.Pause(0.08f);
                if (Input.GetKeyDown(KeyCode.Escape))
                    ClosePopup();
                else if (Input.GetKeyDown(KeyCode.Return)
                         || Input.GetKeyDown(KeyCode.KeypadEnter))
                    ConfirmPopup();
                return;
            }
            if (_dropdownRoot != null && _dropdownRoot.activeInHierarchy)
                UI.Drag.Pause(0.08f);
        }

        private static void PlayAppliedSounds(LoadoutPreset preset)
        {
            var controller = SingletonMonoBehaviour<SoundController>.Instance;
            var sounds = SingletonMonoBehaviour<SoundsStorage>.Instance;
            if (controller == null || sounds == null)
                return;

            var hasWeapon = false;
            var hasOther = false;
            foreach (var entry in preset?.Equipment
                     ?? new List<LoadoutEquipmentEntry>())
            {
                if (entry == null
                    || string.IsNullOrWhiteSpace(entry.ItemId))
                {
                    continue;
                }
                var record = Data.Items.GetRecord(entry.ItemId)
                             as CompositeItemRecord;
                if (record?.GetRecord<WeaponRecord>() != null)
                    hasWeapon = true;
                else
                    hasOther = true;
            }
            if (hasWeapon)
                controller.PlayUiSound(sounds.EquipWeapon, isUnique: true);
            if (hasOther || preset?.IncludesCarriedItems == true)
                controller.PlayUiSound(sounds.TakeItem, isUnique: true);
        }

        // ------------------------------------------------------------------
        // Small UI kit. Mirrors the central panel's own helpers so the bar
        // renders with identical vanilla art, palette and button behaviour.
        // ------------------------------------------------------------------

        private static readonly Color PanelColor =
            new Color(0.006f, 0.018f, 0.015f, 0.99f);
        private static readonly Color CardColor =
            new Color(0.012f, 0.038f, 0.031f, 1f);
        private static Color SelectedColor => VanillaSkin.Palette.Recessed;
        private static Color BrightColor => VanillaSkin.Palette.Bright;
        private static Color ValueColor => VanillaSkin.Palette.Value;
        private static Color OffColor => VanillaSkin.Palette.Muted;
        private static Color DangerColor => VanillaSkin.Palette.Danger;

        private static GameObject CreateUiObject(string name,
            Transform parent)
        {
            var result = new GameObject(name, typeof(RectTransform),
                typeof(CanvasRenderer));
            result.transform.SetParent(parent, false);
            result.layer = parent.gameObject.layer;
            return result;
        }

        private static void SetTopLeft(RectTransform rect, float x,
            float y, float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void SetTopRight(RectTransform rect, float right,
            float top, float width, float height)
        {
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-right, -top);
            rect.sizeDelta = new Vector2(width, height);
        }

        private static void SetTopLeftRight(RectTransform rect, float left,
            float top, float right, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.offsetMin = new Vector2(left, -top - height);
            rect.offsetMax = new Vector2(-right, -top);
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static TextMeshProUGUI CreateText(string name,
            Transform parent, float x, float y, float width, float height,
            TextContext context, float sizeOverride, Color color,
            TextAlignmentOptions alignment)
        {
            var root = CreateUiObject(name, parent);
            SetTopLeft((RectTransform)root.transform, x, y, width, height);
            var text = root.AddComponent<TextMeshProUGUI>();
            if (sizeOverride > 0f)
                text.fontSize = sizeOverride;
            text.color = color;
            text.alignment = alignment;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            VanillaSkin.ApplyFont(text, context);
            return text;
        }

        private static GameObject CreateButtonRoot(string name,
            Transform parent, float x, float y, float width, float height,
            out Image background, out TextMeshProUGUI label)
        {
            return CreateButtonRoot(name, parent, x, y, width, height, null,
                out background, out label);
        }

        private static GameObject CreateButtonRoot(string name,
            Transform parent, float x, float y, float width, float height,
            string tooltipTag, out Image background,
            out TextMeshProUGUI label)
        {
            var root = CreateUiObject(name, parent);
            SetTopLeft((RectTransform)root.transform, x, y, width, height);

            // CommonButton.OnEnable dereferences background.sprite, so the
            // whole control is assembled inactive (mirrors the central panel).
            var wasActive = root.activeSelf;
            root.SetActive(false);

            background = VanillaSkin.Slice(root, VanillaSkin.ButtonNormal,
                CardColor);
            var captionSize = height >= 17f ? 8f : (height >= 14f ? 7f : 6f);
            label = CreateText("Label", root.transform, 1f, 0f,
                width - 2f, height, TextContext.IgnoreSize, captionSize,
                ValueColor, TextAlignmentOptions.Center);
            StretchInto(label.rectTransform, 2f);

            VanillaSkin.AddHint(root, tooltipTag);

            var button = root.AddComponent<CommonButton>();
            VanillaSkin.SuppressNavigation(button);
            button.background = background;
            button.captionText = label;
            button.normalBgSprite = background.sprite;
            button.hoverBgSprite = VanillaSkin.S(VanillaSkin.ButtonHover);
            button.pressedBgSprite = VanillaSkin.S(VanillaSkin.ButtonPressed);
            button.disabledBgSprite =
                VanillaSkin.S(VanillaSkin.ButtonDisabled);
            button.normalCaptionColor = ValueColor;
            button.hoverCaptionColor = BrightColor;
            button.pressedCaptionColor = VanillaSkin.Palette.CaptionPressed;
            button.disabledCaptionColor =
                VanillaSkin.Palette.CaptionDisabled;

            root.SetActive(wasActive);
            return root;
        }

        private static void StretchInto(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(inset, 0f);
            rect.offsetMax = new Vector2(-inset, 0f);
        }

        private static GameObject CreateDropdownTrigger(string name,
            Transform parent, float left, float right,
            out TextMeshProUGUI label)
        {
            const float top = 2f;
            const float height = 18f;
            var root = CreateUiObject(name, parent);
            SetTopLeftRight((RectTransform)root.transform, left, top,
                right, height);

            var wasActive = root.activeSelf;
            root.SetActive(false);

            var background = VanillaSkin.Slice(root,
                VanillaSkin.FieldBackground, CardColor);

            label = CreateText("Label", root.transform, 0f, 0f, 0f,
                height, TextContext.IgnoreSize, 7f, ValueColor,
                TextAlignmentOptions.MidlineLeft);
            var labelRect = label.rectTransform;
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.pivot = new Vector2(0.5f, 0.5f);
            labelRect.offsetMin = new Vector2(4f, 0f);
            labelRect.offsetMax = new Vector2(-VanillaSkin.CaretWellW, 0f);

            var caretObject = CreateUiObject("Caret", root.transform);
            var caretRect = (RectTransform)caretObject.transform;
            caretRect.anchorMin = new Vector2(1f, 0.5f);
            caretRect.anchorMax = new Vector2(1f, 0.5f);
            caretRect.pivot = new Vector2(0.5f, 0.5f);
            caretRect.anchoredPosition = new Vector2(-9f, 0f);
            caretRect.sizeDelta = new Vector2(VanillaSkin.CaretW,
                VanillaSkin.CaretH);
            VanillaSkin.Simple(caretObject, VanillaSkin.CaretArrow,
                    ValueColor)
                .raycastTarget = false;

            var button = root.AddComponent<CommonButton>();
            VanillaSkin.SuppressNavigation(button);
            button.background = background;
            button.captionText = label;
            button.normalBgSprite = background.sprite;
            button.hoverBgSprite = background.sprite;
            button.pressedBgSprite = background.sprite;
            button.disabledBgSprite =
                VanillaSkin.S(VanillaSkin.FieldBackgroundBlocked)
                ?? VanillaSkin.S(VanillaSkin.ButtonDisabled)
                ?? background.sprite;
            button.normalCaptionColor = ValueColor;
            button.hoverCaptionColor = BrightColor;
            button.pressedCaptionColor = BrightColor;
            button.disabledCaptionColor =
                VanillaSkin.Palette.CaptionDisabled;

            root.SetActive(wasActive);
            return root;
        }

        private static void MakeDangerButton(GameObject root,
            TextMeshProUGUI label)
        {
            var button = ButtonOf(root);
            if (button == null)
                return;
            button.normalCaptionColor = DangerColor;
            button.hoverCaptionColor = BrightColor;
            if (label != null)
                label.color = DangerColor;
        }

        private static CommonButton ButtonOf(GameObject root)
        {
            return root == null ? null : root.GetComponent<CommonButton>();
        }

        private static void BindClick(GameObject root, Action action)
        {
            var button = ButtonOf(root);
            if (button != null)
                button.OnClick += (_, __) => action();
        }

        /// <summary>
        /// Latched state via the button's own normal sprite, not Image.color:
        /// colour multiplies against the vanilla art and pointer exit would
        /// repaint over a naive Select (mirrors the central panel).
        /// </summary>
        private static void SetSurfaceSelected(Image image, bool selected)
        {
            if (image == null)
                return;
            var sprite = VanillaSkin.S(selected
                ? VanillaSkin.ButtonPressed
                : VanillaSkin.ButtonNormal);
            if (sprite == null)
            {
                image.color = selected ? SelectedColor : CardColor;
                return;
            }
            var button = image.GetComponent<CommonButton>();
            if (button != null)
                button.normalBgSprite = sprite;
            image.sprite = sprite;
            image.color = Color.white;
        }

        private static void ConfigureButton(Button button, Graphic graphic)
        {
            button.targetGraphic = graphic;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.2f, 1.2f, 1.2f, 1f);
            colors.pressedColor = new Color(0.65f, 0.9f, 0.78f, 1f);
            colors.disabledColor = new Color(0.32f, 0.37f, 0.35f, 0.55f);
            colors.fadeDuration = 0.04f;
            button.colors = colors;
            button.onClick.AddListener(PlayVanillaButtonClick);
        }

        private static void PlayVanillaButtonClick()
        {
            var controller = SingletonMonoBehaviour<SoundController>.Instance;
            var sounds = SingletonMonoBehaviour<SoundsStorage>.Instance;
            if (controller != null && sounds?.ButtonClick != null)
                controller.PlayUiSound(sounds.ButtonClick, isUnique: true);
        }

        private void CloseDropdown(GameObject dropdown)
        {
            if (dropdown == null)
                return;
            var wasVisible = dropdown.activeInHierarchy;
            dropdown.SetActive(false);
            if (_dropdownOriginalParent != null)
            {
                dropdown.transform.SetParent(_dropdownOriginalParent,
                    worldPositionStays: false);
                SetTopLeft((RectTransform)dropdown.transform,
                    SelectedLeft, -21f, 200f, 21f);
                _dropdownOriginalParent = null;
            }
            if (wasVisible)
                ConsumePointerRelease();
        }

        private static void ConsumePointerRelease()
        {
            // Button.onClick fires on mouse-up; after a popup closes, the
            // same release must not reach an ItemSlot below during this frame.
            UI.Drag.Pause(0.18f);
            _releaseBlockFrame = Time.frameCount;
        }

        private static void ClearDropdownRows(List<GameObject> rows)
        {
            foreach (var row in rows)
                if (row != null)
                    Destroy(row);
            rows.Clear();
        }
    }
}
