using System;
using System.Collections.Generic;
using System.Linq;
using MGSC;
using TMPro;
using UnityEngine;

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

        // Horizontal layout, right-anchored: the buttons stick to the bar's
        // right edge and the preset selector stretches between the title and
        // the button group, so the strip aligns to any inventory window width.
        private const float TitleWidth = 78f;
        private const float SelectedLeft = 82f;
        private const float RightGroupWidth = 150f;

        private static ShipLoadoutBar _active;

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
        private LoadoutPresetPopup _popup;

        /// <summary>
        /// The save / apply / delete modal, shared with the central panel's
        /// preset bar. This host uses the SHIP wording: it only moves gear
        /// and carried items, so promising that the body is left alone is
        /// true here and would be a lie in the central panel.
        /// </summary>
        private LoadoutPresetPopup Popup
        {
            get
            {
                if (_popup == null)
                {
                    _popup = new LoadoutPresetPopup(
                        LoadoutPresetTextSet.Ship,
                        () => new LoadoutPresetContext
                        {
                            Mercenary = _mercenary,
                            Cargo = _cargo,
                            Progression = _progression,
                            SpaceTime = _spaceTime,
                            PerkFactory = _screen == null
                                ? null
                                : Plugin.ScreenPerkFactoryOf(_screen),
                        },
                        RefreshLabels,
                        () => _screen?.RefreshView(),
                        ModInputGate.ConsumePointerRelease,
                        () => CloseDropdown(_dropdownRoot));
                }
                return _popup;
            }
        }

        /// <summary>
        /// Blocks the game's own hotkey handling while this bar owns the
        /// keyboard, contributing to the shared ModInputGate.
        /// </summary>
        internal static bool AnyInputCaptured =>
            (_active != null && _active._visible
             && (_active._popup?.IsInputFocused == true
                 || (_active._dropdownRoot != null
                     && _active._dropdownRoot.activeSelf)
                 || _active._popup?.IsOpen == true))
            || ModInputGate.IsFrameBlocked;

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
            _popup?.Close();
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

                _root = PanelUi.CreateUiObject("QM_ShipLoadoutBar", parent);
                var rect = (RectTransform)_root.transform;
                rect.anchorMin = new Vector2(0.5f, 1f);
                rect.anchorMax = new Vector2(0.5f, 1f);
                rect.pivot = new Vector2(0.5f, 1f);
                rect.sizeDelta = new Vector2(BarWidth, BarHeight);
                rect.localScale = Vector3.one;
                rect.localRotation = Quaternion.identity;
                var panel = VanillaSkin.Slice(_root,
                    VanillaSkin.PanelFrame, PanelUi.PanelColor);
                panel.raycastTarget = true;

                _titleLabel = PanelUi.CreateText("Title", rect, 2f, 0f,
                    TitleWidth, BarHeight, TextContext.IgnoreSize, 7f,
                    PanelUi.BrightColor, TextAlignmentOptions.MidlineLeft);
                _selectedRoot = PanelUi.CreateDropdownTriggerStretched("Selected", rect,
                    SelectedLeft, 2f, RightGroupWidth, 18f,
                    out _selectedLabel);
                _selectedButton = PanelUi.ButtonOf(_selectedRoot);
                PanelUi.BindClick(_selectedRoot, ToggleDropdown);

                _applyRoot = PanelUi.CreateButtonRoot("Apply", rect, 0f, 0f,
                    44f, 18f, "qmcentral.tip.preset_apply", out _,
                    out _applyLabel);
                PanelUi.SetTopRight((RectTransform)_applyRoot.transform, 104f, 2f,
                    44f, 18f);
                _applyButton = PanelUi.ButtonOf(_applyRoot);
                PanelUi.BindClick(_applyRoot, () => Popup.OpenApply());
                _saveRoot = PanelUi.CreateButtonRoot("Save", rect, 0f, 0f,
                    52f, 18f, "qmcentral.tip.preset_save", out _,
                    out _saveLabel);
                PanelUi.SetTopRight((RectTransform)_saveRoot.transform, 50f, 2f,
                    52f, 18f);
                _saveButton = PanelUi.ButtonOf(_saveRoot);
                PanelUi.BindClick(_saveRoot, () => Popup.OpenSave());
                _deleteRoot = PanelUi.CreateButtonRoot("Delete", rect, 0f, 0f,
                    46f, 18f, "qmcentral.tip.preset_delete", out _,
                    out _deleteLabel);
                PanelUi.SetTopRight((RectTransform)_deleteRoot.transform, 2f, 2f,
                    46f, 18f);
                PanelUi.MakeDangerButton(_deleteRoot, _deleteLabel);
                _deleteButton = PanelUi.ButtonOf(_deleteRoot);
                PanelUi.BindClick(_deleteRoot, () => Popup.OpenDelete());

                _dropdownRoot = PanelUi.CreateUiObject("PresetDropdown", rect);
                var dropdownRect = (RectTransform)_dropdownRoot.transform;
                PanelUi.SetTopLeft(dropdownRect, SelectedLeft, -21f, 200f, 21f);
                var dropdownBackground = VanillaSkin.Slice(_dropdownRoot,
                    VanillaSkin.ListBackground, PanelUi.PanelColor);
                dropdownBackground.raycastTarget = true;
                _dropdownRoot.SetActive(false);

                Popup.Build(UI.ScreenRoot != null
                    ? UI.ScreenRoot
                    : parent);

                _built = true;
                _root.SetActive(false);
            }
            catch
            {
                Discard();
                throw;
            }
        }


        private void Discard()
        {
            if (_root != null)
                Destroy(_root);
            _popup?.Discard();
            _popup = null;
            _root = null;
            _dropdownRoot = null;
            _dropdownOriginalParent = null;
            _dropdownRows.Clear();
            _built = false;
        }

        private void OnDestroy()
        {
            // The popup's scrim is parented to the global UI.ScreenRoot, which
            // outlives this pooled (non-singleton) screen. Destroy it here
            // or every space/dungeon loop switch leaks one popup group.
            _popup?.Discard();
            _popup = null;
            _dropdownOriginalParent = null;
            if (ReferenceEquals(_active, this))
                _active = null;
        }

        /// <summary>
        /// While the modal owns the screen it also owns Escape and Enter, and
        /// the raw drag controller has to stay paused underneath it.
        /// </summary>
        private void Update()
        {
            if (_popup?.IsOpen == true)
            {
                UI.Drag.Pause(0.08f);
                _popup.HandleKeys();
                return;
            }
            if (_dropdownRoot != null && _dropdownRoot.activeInHierarchy)
                UI.Drag.Pause(0.08f);
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
        /// Window, bar and screen geometry in world corners, so a misaligned
        /// strip can be diagnosed from Player.log alone.
        /// </summary>
        private void LogPlacement()
        {
            var rect = (RectTransform)_root.transform;
            var text = "ship loadout bar placed: window world="
                       + LayoutDebug.World(_windowRect)
                       + " (local=" + LayoutDebug.Local(_windowRect.rect)
                       + ", anchor=" + _windowRect.anchorMin + "/"
                       + _windowRect.anchorMax + ", pos="
                       + _windowRect.anchoredPosition + ")"
                       + " | bar world=" + LayoutDebug.World(rect)
                       + " (pivot=" + rect.pivot + ", pos="
                       + rect.anchoredPosition + ", size=" + rect.sizeDelta
                       + ")";
            var caption = _windowRect.Find("CaptionBlock") as RectTransform;
            if (caption != null)
                text += " | caption world=" + LayoutDebug.World(caption);
            var screenRoot = UI.ScreenRoot;
            if (screenRoot != null)
                text += " | screen world=" + LayoutDebug.World(screenRoot);
            LayoutDebug.LogChanged(ref _lastPlacement, text);
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
                         _saveLabel, _deleteLabel,
                     })
            {
                if (text != null)
                    text.font = font;
            }
            _popup?.ApplyFont(font);
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
            PanelUi.ClearDropdownRows(_dropdownRows);
            var presets = LoadoutPresetRepository.Data.Presets
                .Where(p => p != null)
                .OrderByDescending(p => p.UpdatedUtcTicks).ToList();
            const float width = 200f;
            const float rowHeight = 17f;
            var height = Mathf.Max(rowHeight + 4f,
                presets.Count * rowHeight + 4f);
            PanelUi.SetTopLeft((RectTransform)_dropdownRoot.transform,
                SelectedLeft, -21f, width, height);
            for (var i = 0; i < presets.Count; i++)
            {
                var preset = presets[i];
                var row = PanelUi.CreateButtonRoot("Preset" + i,
                    _dropdownRoot.transform, 2f, -2f - i * rowHeight,
                    width - 4f, rowHeight - 1f, out var background,
                    out var label);
                label.text = preset.Name + "   "
                             + LoadoutPresetService.GetSummary(preset);
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.margin = new Vector4(4f, 0f, 2f, 0f);
                label.fontSize = 6f;
                PanelUi.SetSurfaceSelected(background,
                    preset.Id == LoadoutPresetRepository.Data.SelectedId);
                PanelUi.BindClick(row, () =>
                {
                    LoadoutPresetRepository.Select(preset.Id);
                    CloseDropdown(_dropdownRoot);
                    RefreshLabels();
                });
                _dropdownRows.Add(row);
            }
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
                PanelUi.SetTopLeft((RectTransform)dropdown.transform,
                    SelectedLeft, -21f, 200f, 21f);
                _dropdownOriginalParent = null;
            }
            if (wasVisible)
                ModInputGate.ConsumePointerRelease();
        }

    }
}
