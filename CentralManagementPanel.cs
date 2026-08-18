using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using MGSC;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace QM_CentralManagement
{
    internal sealed partial class CentralManagementPanel : MonoBehaviour,
        IScreenPanel
    {
        bool IScreenPanel.OwnsScreen => _centralMode;

        void IScreenPanel.OnScreenEnabled()
        {
            // Vanilla's OnEnable turns its cargo window back on; the panel
            // stands where that window would be.
            Plugin.CargoWindowOf(_screen)?.SetActive(false);
            ShowPanel();
        }

        void IScreenPanel.OnScreenDisabled()
        {
            LeaveCentralMode();
        }

        // Card pool ceiling. The grid derives its real row count from the
        // measured panel at runtime, so this only has to be large enough for
        // the tallest canvas (4:3 gives 480 canvas units against 360 at 16:9).
        private const int MaxCardsPerPage = 48;

        // Layout module set, measured off vanilla rather than invented.
        // Vanilla's sizes are odd and irregular by design -- 13, 17, 19, 24 --
        // so there is no 4/8px grid to snap to. What matters is that every
        // value stays an INTEGER: the CanvasScaler runs at
        // referencePixelsPerUnit = 1, so fractional positions land glyphs and
        // point-filtered art on half pixels and produce visible seams.
        private const float PanelPad = 4f;    // window inner padding
        private const float Gap = 2f;         // gap between sibling controls
        private const float HeaderH = 19f;    // title bar
        private const float RowH = 17f;       // search field / footer controls
        private const float TabH = 13f;       // tab strip (vanilla ItemTab is 15)
        private const float SlotModule = 24f; // ItemSlot._defaultWidth/_defaultHeight
        private const float CardH = 32f;      // 24px slot + padding + a 7pt line
        private const float CardGap = 2f;
        private const float ChipW = 43f;      // the per-card select chip

        // ItemSlot.InitSlotSize resolves 24x24 for a normal item but *50*x24
        // whenever InventoryWidthSize == 2 (ItemSlot.cs:494).  The icon column
        // is therefore sized for the widest case and the slot is centred inside
        // it: a 24px icon sits at 13..37 and a 50px one at 0..50, so two-cell
        // items stay inside the card and every name still starts on the same x.
        private const float SlotColumnW = 50f;
        private const float NameX = 56f;      // 3 padding + 50 column + 3 gap

        // A lone detail tab must not stretch into a full-width bar; vanilla
        // tabs are chips, and a 616px-wide "ALL" reads as a heading.
        private const float MaxTabWidth = 96f;

        private enum CargoCategory
        {
            All,
            Weapons,
            Equipment,
            Ammo,
            // Grenades, mines and turrets used to ride along in Ammo, which
            // put a deployable turret under a tab labelled "ammunition" -- the
            // label was a strict subset of the contents. They are their own
            // tab now and Ammo means rounds only. NOT called Deployables:
            // CargoDetail already uses that name for PlaceableObstacle.
            Ordnance,
            Supplies,
            Augments,
            Materials,
            Barter,
            Special
        }

        private enum CargoDetail
        {
            Any,
            Ranged,
            Melee,
            Pistol,
            Shotgun,
            Smg,
            Rifle,
            Heavy,
            Head,
            Body,
            Legs,
            Boots,
            Backpack,
            Vest,
            // Ammunition retired with the Ammo tab's split: that tab now
            // holds one item class, so it has nothing left to sub-filter.
            Grenades,
            Mines,
            Turrets,
            Food,
            Drinks,
            Medicine,
            Repair,
            Bio,
            Cybernetic,
            Quasi,
            // The augment tab splits the way the game's own data does:
            // config_items #augmentations are body parts you swap (they carry
            // WoundSlotIds), #implants are things you slot into one (they carry
            // a SlotType). ImplantRecord and AugmentationRecord are siblings,
            // both straight off ItemRecord, and no item is in both sections.
            AugLimb,
            AugImplant,
            Parts,
            Data,
            Blueprints,
            Organs,
            Military,
            Science,
            Industrial,
            Valuable,
            Quest,
            Keys,
            Deployables,
            Cyborgs,
            Other
        }

        private enum SortMode
        {
            Name,
            Quantity,
            TechLevel,
            Set,
            TotalResist,
            TotalDamage,
            Blunt,
            Pierce,
            Laceration,
            Fire,
            Cold,
            Poison,
            Shock,
            Beam,
            Explosion,
            Plasma,
            Chaos,
            Proton,
            Cryo
        }

        private sealed class Card
        {
            public GameObject Root;
            public Transform SlotHost;
            public Image Background;
            public ItemSlot ItemSlot;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Quantity;
            public CommonButton SelectButton;
            public GameObject SelectRoot;
            public TextMeshProUGUI SelectLabel;
            public string ItemId;
        }

        private sealed class FilterTab
        {
            public GameObject Root;
            public Image Background;
            public TextMeshProUGUI Label;
            public CargoCategory Category;
            public CargoDetail Detail;
        }

        private static CentralManagementPanel _instance;

        private readonly CentralInventoryIndex _inventoryIndex =
            new CentralInventoryIndex();
        private Dictionary<string, CentralInventoryEntry> Index =>
            _inventoryIndex.Entries;
        private Dictionary<string, string> EquipmentSetKeys =>
            _inventoryIndex.EquipmentSetKeys;
        private readonly List<CentralInventoryEntry> _filtered =
            new List<CentralInventoryEntry>();
        private readonly Dictionary<string, int> _selectedForRecycle =
            new Dictionary<string, int>(StringComparer.Ordinal);
        private readonly Dictionary<string, BasePickupItem> _preferredStacks =
            new Dictionary<string, BasePickupItem>(StringComparer.Ordinal);
        private readonly HashSet<BasePickupItem> _itemsBeforeSplit =
            new HashSet<BasePickupItem>();
        private readonly Card[] _cards = new Card[MaxCardsPerPage];
        private readonly List<FilterTab> _categoryTabs =
            new List<FilterTab>();
        private readonly List<FilterTab> _detailTabs =
            new List<FilterTab>();
        private ScreenWithShipCargo _screen;
        private MagnumCargo _cargo;
        private SpaceTime _spaceTime;
        private MagnumProgression _progression;
        private PerkFactory _perkFactory;
        private Mercenary _mercenary;
        private GameObject _vanillaCargoWindow;
        private GameObject _root;
        private GameObject _cardWellRoot;
        private float _detailRowY = -59f;
        private GameObject _headerRoot;
        private GameObject _headerIconRoot;
        private GameObject _previousOperatorRoot;
        private GameObject _nextOperatorRoot;
        private GameObject _operatorPanelRoot;
        private GameObject _closeRoot;
        private GameObject _searchRoot;
        private GameObject _sortRoot;
        private GameObject _sortDropdownRoot;
        private GameObject _slotFilterRoot;
        private GameObject _slotFilterDropdownRoot;
        private GameObject _selectFilteredRoot;
        private GameObject _clearRoot;
        private GameObject _previousPageRoot;
        private GameObject _nextPageRoot;
        private GameObject _recycleRoot;
        private GameObject _repairRoot;
        private TMP_InputField _search;
        private TextMeshProUGUI _searchText;
        private TMP_Text _searchPlaceholder;
        private TextMeshProUGUI _sortLabel;
        private TextMeshProUGUI _slotFilterLabel;
        private readonly List<GameObject> _sortDropdownRows =
            new List<GameObject>();
        private readonly List<GameObject> _slotFilterDropdownRows =
            new List<GameObject>();
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _operator;
        private CommonButton _operatorDropdownButton;
        private GameObject _operatorRoot;
        private GameObject _operatorDropdownRoot;
        private readonly List<GameObject> _operatorDropdownRows =
            new List<GameObject>();
        private CommonButton _previousOperatorButton;
        private CommonButton _nextOperatorButton;
        private TextMeshProUGUI _count;
        private TextMeshProUGUI _page;
        private TextMeshProUGUI _empty;
        private GameObject _clearFiltersRoot;
        private TextMeshProUGUI _clearFiltersLabel;
        private TextMeshProUGUI _recycleLabel;
        private CommonButton _recycleButton;
        private TextMeshProUGUI _repairLabel;
        private CommonButton _repairButton;
        // Receipt for the batch repair, sharing the trade panel's item
        // list modal. Parented to the SCREEN, not to the panel rect,
        // which at 282 wide is narrower than the popup itself.
        private ExchangeSummaryPopup _repairSummary;
        private int _repairableCount;
        private TextMeshProUGUI _selectFilteredLabel;
        private TextMeshProUGUI _clearLabel;
        private TextMeshProUGUI _closeLabel;
        private TextMeshProUGUI _operatorPanelLabel;
        private CommonButton _operatorPanelButton;
        // Command ids for our own entries in the shared CommonContextMenu.
        // Vanilla's ContextMenuCommand spans 0..24 and it reserves 99999 and
        // 100000 for stack splitting; ScreenWithShipCargo.ContextMenuOnCmdSelected
        // has no default clause, so ids outside its cases are simply ignored.
        private const int CmdSelectAll = 90101;
        private const int CmdSelectAmount = 90102;
        private const int CmdDeselect = 90103;

        // Item id that Shift ranges extend from. Deliberately an id and not
        // a list index: any index would be invalidated by every RefreshFiltered,
        // and a background RequestRefresh could silently wipe the anchor between
        // two clicks -- which made Shift look like it did nothing at all.
        private string _selectionAnchorId;

        private CommonContextMenu _menu;
        private CentralInventoryEntry _menuEntry;
        private int _menuAvailable;
        private bool _menuCleanupPending;
        private CargoCategory _category = CargoCategory.All;
        private CargoDetail _detail = CargoDetail.Any;
        private SortMode _sortMode = SortMode.Name;
        private string _implantSlotFilter;
        // Index of the first VISIBLE ROW, not a page number. The grid
        // scrolls a row at a time; the arrows still jump a full screen.
        private int _scrollRow;
        private bool _refreshQueued;
        private bool _centralMode;
        private bool _operatorPanelVisible;
        private bool _augmentationMode;
        private bool _startWithOperatorPanel;
        private bool _panelSessionInitialized;
        private int _centralControlClickFrame = -1;
        private ItemSlot _centralControlClickSlot;
        private string _pendingSplitItemId;
        private int _pendingSplitUnits;

        internal bool IsCentralMode => _centralMode;
        // Grid shape is measured, not hardcoded: ApplyResponsiveLayout fills
        // these from the real panel rect. The defaults match the 16:9 wide
        // mode so paging math is sane before the first layout pass runs.
        private int _cardColumns = 4;
        private int _cardRows = 7;
        // Set by the layout pass: true when the footer readout has too little
        // room for the full "{0} TYPES / {1} UNITS" sentence.
        private bool _countCompact;

        private int CurrentPageSize =>
            Mathf.Clamp(_cardColumns * _cardRows, 1, MaxCardsPerPage);
        private int CurrentCardColumns => _cardColumns;
        private float CurrentPanelWidth => _operatorPanelVisible ? 282f : 624f;
        private float CurrentInnerWidth => CurrentPanelWidth - PanelPad * 2f;

        /// <summary>
        /// This panel's own contribution to <see cref="ModInputGate"/>. It
        /// used to OR in the ship bar and the trade panel as well, which made
        /// the central panel the arbiter for UI it has nothing to do with.
        /// </summary>
        internal static bool CapturesInput =>
            _instance != null && _instance._centralMode
            && _instance._root != null && _instance._root.activeInHierarchy
            && ((_instance._search != null && _instance._search.isFocused)
                || UI.IsShowing<CommonContextMenu>()
                || _instance.IsPresetPopupOpen
                || _instance.IsRepairSummaryOpen
                || _instance.HasOpenDropdown());

        private bool IsRepairSummaryOpen => _repairSummary?.IsOpen == true;

        // The panel's popup lists. HasOpenDropdown and CloseAllDropdowns are
        // the ONLY places that enumerate them -- a new list is added to both
        // and everything else (the input gate, the wheel guard, every "close
        // the others first" path) follows automatically.
        private bool HasOpenDropdown()
        {
            return IsDropdownOpen(_operatorDropdownRoot)
                   || IsDropdownOpen(_sortDropdownRoot)
                   || IsDropdownOpen(_slotFilterDropdownRoot)
                   || IsDropdownOpen(_presetDropdownRoot)
                   || IsDropdownOpen(_paneDropdownRoot);
        }

        private void CloseAllDropdowns()
        {
            CloseDropdown(_operatorDropdownRoot);
            CloseDropdown(_sortDropdownRoot);
            CloseDropdown(_slotFilterDropdownRoot);
            CloseDropdown(_presetDropdownRoot);
            CloseDropdown(_paneDropdownRoot);
        }

        private static bool IsDropdownOpen(GameObject dropdown)
        {
            return dropdown != null && dropdown.activeInHierarchy;
        }

        private static string _lastCentralRect;

        private static void LogCentralPanelRect()
        {
            if (_instance == null || _instance._root == null)
                return;
            LayoutDebug.LogChanged(ref _lastCentralRect,
                "central panel world "
                + LayoutDebug.World((RectTransform)_instance._root.transform));
        }

        internal void Configure(ScreenWithShipCargo screen, MagnumCargo cargo,
            SpaceTime spaceTime, MagnumProgression progression,
            PerkFactory perkFactory, Mercenary mercenary,
            GameObject vanillaCargoWindow,
            bool augmentationMode = false, bool showOperatorPanel = false)
        {
            _screen = screen;
            _cargo = cargo;
            _spaceTime = spaceTime;
            _progression = progression;
            _perkFactory = perkFactory;
            _mercenary = mercenary;
            _vanillaCargoWindow = vanillaCargoWindow;
            _augmentationMode = augmentationMode;
            _startWithOperatorPanel = showOperatorPanel;
            _panelSessionInitialized = false;
            _centralMode = false;
            try
            {
                EnsureBuilt(vanillaCargoWindow);
                _centralMode = true;
                _instance = this;
                ShowPanel();
            }
            catch
            {
                _centralMode = false;
                if (ReferenceEquals(_instance, this))
                    _instance = null;
                DiscardBuiltUi();
                if (_vanillaCargoWindow != null)
                    _vanillaCargoWindow.SetActive(true);
                throw;
            }
        }

        internal void ShowPanel()
        {
            if (!_centralMode || _root == null)
                return;
            // Configure() runs before the pooled screen is shown, then
            // ScreenWithShipCargo.OnEnable runs ShowPanel() once more.  The
            // second call must only re-assert the current layout; resetting
            // it would consume showOperatorPanel and immediately collapse the
            // agent equipment we just returned to.
            if (_panelSessionInitialized)
            {
                Plugin.SetCentralOperatorPanelVisible(_screen,
                    _operatorPanelVisible);
                // Re-assert the pane: the call above hands the window back to
                // the equipment view whenever it has to re-initialize it, so
                // a pooled re-enable would otherwise drop the shuttle hold.
                Plugin.SetCentralShuttleViewVisible(_screen,
                    _shuttleViewVisible);
                ApplyResponsiveLayout();
                // ApplyResponsiveLayout must leave the detail row in its
                // final category-specific geometry.  Refresh it immediately
                // as well so the pooled view never exposes an intermediate
                // layout while the queued inventory refresh is pending.
                RefreshTabs();
                _root.SetActive(true);
                _root.transform.SetAsLastSibling();
                LogCentralPanelRect();
                RefreshPresetBar();
                RequestRefresh();
                return;
            }

            // Leaving the augmentation screen returns to the same central
            // category with the agent's equipment expanded beside it.
            _operatorPanelVisible = _augmentationMode
                                    || _startWithOperatorPanel;
            Plugin.SetCentralOperatorPanelVisible(_screen,
                _operatorPanelVisible);
            _scrollRow = 0;
            // Returning from Augmentation should restore the agent equipment
            // panel only.  It must not also force the central inventory back
            // to the Augments category; those are independent UI states.
            _category = _augmentationMode
                ? CargoCategory.Augments
                : CargoCategory.All;
            _startWithOperatorPanel = false;
            _detail = CargoDetail.Any;
            _sortMode = SortMode.Name;
            _implantSlotFilter = null;
            ApplyResponsiveLayout();
            _selectedForRecycle.Clear();
            _selectionAnchorId = null;
            _preferredStacks.Clear();
            ClearPendingSplit();
            CloseAllDropdowns();
            _search.SetTextWithoutNotify(string.Empty);
            ApplyFont();
            RebuildAndRefresh();
            // After the window's visibility is settled: a shuttle pane
            // requested from the augmentation screen lands here.
            RestorePaneAfterConfigure();
            RefreshPresetBar();
            _panelSessionInitialized = true;
            // Reveal only after all labels, selection colours and rects have
            // reached their final state.  This prevents stale pooled UI from
            // flashing when Arsenal and Augmentation exchange screens.
            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
            LogCentralPanelRect();
        }

        internal void LeaveCentralMode()
        {
            CloseItemMenu();
            ClosePresetPopup();
            _repairSummary?.Hide();
            CloseAllDropdowns();
            ClearPendingSplit();
            _centralMode = false;
            _panelSessionInitialized = false;
            _refreshQueued = false;
            _search?.DeactivateInputField();
            if (_root != null)
                _root.SetActive(false);
            // The shuttle pane borrows a VANILLA view. Leaving it switched on
            // would hand the next ordinary Arsenal opening a shuttle grid
            // where its equipment should be.
            if (_shuttleViewVisible)
            {
                Plugin.SetCentralShuttleViewVisible(_screen, false);
                _shuttleViewVisible = false;
            }
            HidePresetUi();
            if (_vanillaCargoWindow != null)
                _vanillaCargoWindow.SetActive(true);
        }

        internal void RestoreVanillaUi()
        {
            LeaveCentralMode();
        }

        internal void RequestRefresh()
        {
            if (_centralMode)
                _refreshQueued = true;
        }

        private void EnsureBuilt(GameObject vanillaCargoWindow)
        {
            if (_root != null)
                return;
            try
            {
                var sourceRect = vanillaCargoWindow == null
                    ? null
                    : vanillaCargoWindow.transform as RectTransform;
                var parent = sourceRect == null ? _screen.transform : sourceRect.parent;

                // Collect the game's own UI art before anything is built: every
                // factory below asks VanillaSkin for sprites.
                VanillaSkin.Seed(parent);

                _root = PanelUi.CreateUiObject("QM_CentralManagement", parent);
                // Configure can run while a pooled screen is already active.
                // Build and lay out the complete panel off-screen, then let
                // ShowPanel reveal it atomically.
                _root.SetActive(false);
                var rect = (RectTransform)_root.transform;
                // This is an independent right-hand workbench, not a resized or
                // cloned CargoWindow. At the game's 640x360 reference canvas it
                // leaves the vanilla 314x150 operator panel unobstructed on the
                // left while using all available space up to the right edge.
                rect.anchorMin = new Vector2(0.5f, 0.5f);
                rect.anchorMax = new Vector2(0.5f, 0.5f);
                rect.pivot = new Vector2(0f, 0.5f);
                rect.anchoredPosition = new Vector2(38f, -10f);
                rect.sizeDelta = new Vector2(282f, 202f);
                rect.localScale = Vector3.one;
                rect.localRotation = Quaternion.identity;
                var panel = VanillaSkin.Slice(_root, VanillaSkin.PanelFrame,
                    PanelUi.PanelColor);
                panel.raycastTarget = true;

                BuildHeader(_root.transform);
                BuildSearch(_root.transform);
                BuildCategories(_root.transform);
                BuildDetailTabs(_root.transform);
                BuildCards(_root.transform);
                BuildFooter(_root.transform);
                var barParent = parent;
                if (_screen is ArsenalScreen arsenal)
                {
                    var window = Plugin.InventoryWindowOf(arsenal);
                    if (window != null)
                        barParent = window.transform;
                }
                BuildPresetUi(barParent, parent);
                _repairSummary = new ExchangeSummaryPopup(
                    ModInputGate.ConsumePointerRelease, CloseAllDropdowns);
                _repairSummary.Build(parent);
            }
            catch
            {
                DiscardBuiltUi();
                throw;
            }
        }

        private void DiscardBuiltUi()
        {
            ReleaseItemSlots();
            if (_root != null)
            {
                _root.SetActive(false);
                Destroy(_root);
                _root = null;
            }
            _categoryTabs.Clear();
            _detailTabs.Clear();
            _operatorDropdownRows.Clear();
            _sortDropdownRows.Clear();
            _slotFilterDropdownRows.Clear();
            for (var i = 0; i < _cards.Length; i++)
                _cards[i] = null;
            _headerRoot = null;
            _cardWellRoot = null;
            _headerIconRoot = null;
            _search = null;
            _operatorDropdownRoot = null;
            _sortRoot = null;
            _sortDropdownRoot = null;
            _repairRoot = null;
            _repairButton = null;
            _repairLabel = null;
            _slotFilterRoot = null;
            _slotFilterDropdownRoot = null;
            DiscardShuttleUi();
            DiscardPresetUi();
            _repairSummary?.Discard();
            _repairSummary = null;
        }

        private void BuildHeader(Transform parent)
        {
            _headerRoot = PanelUi.CreateUiObject("Header", parent);
            PanelUi.SetTopLeft((RectTransform)_headerRoot.transform, 2f, -2f, 278f, 17f);
            VanillaSkin.Slice(_headerRoot, VanillaSkin.HeaderFrame, PanelUi.HeaderColor);

            _headerIconRoot = PanelUi.CreateUiObject("Icon", _headerRoot.transform);
            PanelUi.SetTopLeft((RectTransform)_headerIconRoot.transform, 1f, -1f, 14f, 14f);
            var icon = _headerIconRoot.AddComponent<Image>();
            icon.sprite = Plugin.FastAccessSprite;
            icon.preserveAspect = true;
            icon.raycastTarget = false;

            _title = PanelUi.CreateText("Title", _headerRoot.transform, 17f, 0f,
                72f, 17f, TextContext.WindowCaption, PanelUi.BrightColor,
                TextAlignmentOptions.MidlineLeft);

            _previousOperatorRoot = PanelUi.CreateButtonRoot("PreviousOperator",
                _headerRoot.transform, 91f, -2f, 12f, 13f,
                "qmcentral.tip.previous_operator", out _, out var previousLabel);
            previousLabel.text = "<";
            _previousOperatorButton = PanelUi.ButtonOf(_previousOperatorRoot);
            PanelUi.BindClick(_previousOperatorRoot, () => CycleOperator(-1));
            _operatorRoot = PanelUi.CreateDropdownTrigger("Operator",
                _headerRoot.transform, 104f, 0f, 45f, 15f, out _operator);
            VanillaSkin.AddHint(_operatorRoot, "qmcentral.tip.operator");
            _operatorDropdownButton = PanelUi.ButtonOf(_operatorRoot);
            PanelUi.BindClick(_operatorRoot, ToggleOperatorDropdown);
            _nextOperatorRoot = PanelUi.CreateButtonRoot("NextOperator",
                _headerRoot.transform, 150f, -2f, 12f, 13f,
                "qmcentral.tip.next_operator", out _, out var nextLabel);
            nextLabel.text = ">";
            _nextOperatorButton = PanelUi.ButtonOf(_nextOperatorRoot);
            PanelUi.BindClick(_nextOperatorRoot, () => CycleOperator(1));

            // A dropdown, not a button: this control used to mean four
            // different things depending on the mode, and none of them were
            // visible before clicking. See CentralManagementPanel.Shuttle.cs.
            _operatorPanelRoot = PanelUi.CreateDropdownTrigger("PaneSelect",
                _headerRoot.transform, 164f, -2f, GearW, HeaderControlH,
                out _operatorPanelLabel);
            VanillaSkin.AddHint(_operatorPanelRoot,
                "qmcentral.tip.operator_panel");
            _operatorPanelButton = PanelUi.ButtonOf(_operatorPanelRoot);
            PanelUi.BindClick(_operatorPanelRoot, TogglePaneDropdown);

            _closeRoot = PanelUi.CreateButtonRoot("Close", _headerRoot.transform,
                232f, -2f, 44f, 13f, "qmcentral.tip.close",
                out _, out _closeLabel);
            PanelUi.BindClick(_closeRoot, ClosePanel);

            BuildOperatorDropdown(parent);
            BuildPaneDropdown(parent);
        }

        private void BuildOperatorDropdown(Transform parent)
        {
            _operatorDropdownRoot = PanelUi.CreateUiObject("OperatorDropdown", parent);
            var rect = (RectTransform)_operatorDropdownRoot.transform;
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = new Vector2(-46f, -23f);
            rect.sizeDelta = new Vector2(170f, 28f);
            var background = VanillaSkin.Slice(_operatorDropdownRoot,
                VanillaSkin.ListBackground, PanelUi.PanelColor);
            background.raycastTarget = true;
            _operatorDropdownRoot.SetActive(false);
        }

        private void BuildSearch(Transform parent)
        {
            _searchRoot = PanelUi.CreateUiObject("Search", parent);
            PanelUi.SetTopLeft((RectTransform)_searchRoot.transform, 4f, -21f, 274f, 14f);
            // dropdownListBackground, not dropdownBackground: the latter is
            // authored with a fixed 17px caret well on its right edge.
            var background = VanillaSkin.Slice(_searchRoot,
                VanillaSkin.ListBackground, PanelUi.CardColor);
            VanillaSkin.AddHint(_searchRoot, "qmcentral.tip.search");

            var viewport = PanelUi.CreateUiObject("Viewport", _searchRoot.transform);
            var viewportRect = (RectTransform)viewport.transform;
            PanelUi.Stretch(viewportRect);
            viewportRect.offsetMin = new Vector2(3f, 1f);
            viewportRect.offsetMax = new Vector2(-3f, -1f);
            viewport.AddComponent<RectMask2D>();

            var placeholderObject = PanelUi.CreateUiObject("Placeholder", viewport.transform);
            var placeholderRect = (RectTransform)placeholderObject.transform;
            PanelUi.Stretch(placeholderRect);
            var placeholder = placeholderObject.AddComponent<TextMeshProUGUI>();
            PanelUi.ConfigureText(placeholder, TextContext.IgnoreSize, 7f,
                PanelUi.OffColor, TextAlignmentOptions.MidlineLeft);
            placeholder.fontStyle = FontStyles.Italic;
            _searchPlaceholder = placeholder;

            var textObject = PanelUi.CreateUiObject("Text", viewport.transform);
            var textRect = (RectTransform)textObject.transform;
            PanelUi.Stretch(textRect);
            _searchText = textObject.AddComponent<TextMeshProUGUI>();
            PanelUi.ConfigureText(_searchText, TextContext.IgnoreSize, 7f, PanelUi.ValueColor,
                TextAlignmentOptions.MidlineLeft);

            _search = _searchRoot.AddComponent<TMP_InputField>();
            _search.targetGraphic = background;
            _search.textViewport = viewportRect;
            _search.textComponent = _searchText;
            _search.placeholder = _searchPlaceholder;
            _search.lineType = TMP_InputField.LineType.SingleLine;
            _search.contentType = TMP_InputField.ContentType.Standard;
            _search.characterValidation = TMP_InputField.CharacterValidation.None;
            _search.richText = false;
            _search.customCaretColor = true;
            _search.caretColor = PanelUi.AccentColor;
            _search.selectionColor = new Color(0.25f, 0.58f, 0.49f, 0.55f);
            _search.onValueChanged.AddListener(_ =>
            {
                _scrollRow = 0;
                RefreshFiltered();
            });
            _search.onSelect.AddListener(_ =>
                Input.imeCompositionMode = IMECompositionMode.On);
            _search.onDeselect.AddListener(_ =>
                ModInputGate.BlockThisFrame());

            // SORT and SLOT open lists too, so they carry the same frame and
            // caret as the operator and preset selectors.
            _slotFilterRoot = PanelUi.CreateDropdownTrigger("SlotFilter", parent,
                170f, -21f, 52f, RowH, out _slotFilterLabel);
            VanillaSkin.AddHint(_slotFilterRoot, "qmcentral.tip.slot_filter");
            PanelUi.BindClick(_slotFilterRoot, ToggleSlotFilterDropdown);

            _sortRoot = PanelUi.CreateDropdownTrigger("Sort", parent,
                224f, -21f, 54f, RowH, out _sortLabel);
            VanillaSkin.AddHint(_sortRoot, "qmcentral.tip.sort");
            PanelUi.BindClick(_sortRoot, ToggleSortDropdown);

            // Placeholder geometry only: ApplyResponsiveLayout owns the real
            // position and width of both lists, because they depend on the
            // panel width and on whether the slot filter is showing at all.
            _slotFilterDropdownRoot = PanelUi.CreateDropdownRoot(
                "SlotFilterDropdown", parent, PanelPad, -41f, 90f, 20f);
            _sortDropdownRoot = PanelUi.CreateDropdownRoot(
                "SortDropdown", parent, PanelPad, -41f, 90f, 20f);
        }

        private void BuildCategories(Transform parent)
        {
            var categories = (CargoCategory[])Enum.GetValues(
                typeof(CargoCategory));
            // Build-time geometry is a placeholder; LayoutCategoryTabs owns the
            // real positions because the row split depends on panel width.
            for (var i = 0; i < categories.Length; i++)
            {
                var category = categories[i];
                var root = PanelUi.CreateButtonRoot("Category" + category, parent,
                    PanelPad, -42f, 60f, TabH,
                    out var background, out var label);
                PanelUi.BindClick(root, () => SelectCategory(category));
                _categoryTabs.Add(new FilterTab
                {
                    Root = root,
                    Background = background,
                    Label = label,
                    Category = category,
                    Detail = CargoDetail.Any
                });
            }
        }

        private void BuildDetailTabs(Transform parent)
        {
            for (var i = 0; i < 12; i++)
            {
                var index = i;
                var root = PanelUi.CreateButtonRoot("Detail" + i, parent,
                    PanelPad, -61f, 60f, TabH,
                    out var background, out var label);
                PanelUi.BindClick(root, () => SelectDetail(index));
                _detailTabs.Add(new FilterTab
                {
                    Root = root,
                    Background = background,
                    Label = label,
                    Category = CargoCategory.All,
                    Detail = CargoDetail.Any
                });
            }
        }

        private void BuildCards(Transform parent)
        {
            // A recessed well behind the grid. Without it the panel is one flat
            // plane with a frame drawn on it; vanilla screens read as layers,
            // and this is what separates "content" from "chrome" here.
            // Created before the cards so it sits behind them in the hierarchy.
            _cardWellRoot = PanelUi.CreateUiObject("CardWell", parent);
            VanillaSkin.Slice(_cardWellRoot, VanillaSkin.ListBackground,
                PanelUi.CardColor).raycastTarget = false;

            for (var i = 0; i < MaxCardsPerPage; i++)
            {
                var index = i;
                var column = i % 3;
                var row = i / 3;
                var card = new Card();
                card.Root = PanelUi.CreateUiObject("Card" + i, parent);
                PanelUi.SetTopLeft((RectTransform)card.Root.transform,
                    4f + column * 92f, -72f - row * 34f, 90f, 32f);
                card.Background = VanillaSkin.Slice(card.Root,
                    VanillaSkin.SlotNormal, PanelUi.CardColor);
                card.Background.raycastTarget = true;

                var slotHost = PanelUi.CreateUiObject("SlotHost", card.Root.transform);
                PanelUi.SetTopLeft((RectTransform)slotHost.transform,
                    3f, -4f, SlotColumnW, SlotModule);
                // No RectMask2D: it clipped the vanilla slot's own hover
                // animation, which is part of why the icons looked wrong.
                card.SlotHost = slotHost.transform;

                // The name now owns a full-width line of its own, which is what
                // buys the vanilla 9pt body size -- the old 58px field could
                // only fit a name by shrinking the type to 6.2pt.
                card.Name = PanelUi.CreateText("Name", card.Root.transform,
                    NameX, -2f, 100f, 14f, TextContext.None, PanelUi.BrightColor,
                    TextAlignmentOptions.MidlineLeft);
                card.Quantity = PanelUi.CreateText("Quantity", card.Root.transform,
                    NameX, -16f, 60f, 12f, TextContext.SmallNumbers, PanelUi.AccentColor,
                    TextAlignmentOptions.MidlineLeft);

                // Deliberately no tooltip on the card chip.  All thirty of them
                // would say the same thing, and a tooltip that fires wherever
                // the pointer crosses the grid covers the cards behind it --
                // the vanilla ItemSlot icon already carries the real item
                // tooltip, which is the one worth reading here.
                var buttonRoot = PanelUi.CreateButtonRoot("Select", card.Root.transform,
                    100f, -17f, ChipW, TabH, out _, out card.SelectLabel);
                card.SelectRoot = buttonRoot;
                card.SelectButton = PanelUi.ButtonOf(buttonRoot);

                // The whole card is a click target, not just the 43x13 chip.
                // Clicks on the item icon still reach the vanilla ItemSlot --
                // it handles IPointerClickHandler itself, so dragging and the
                // Ctrl+click transfer are untouched -- and clicks on the chip
                // reach its own button. Everything else lands here.
                AddCardClickHandler(card.Root, index);
                AddCardClickHandler(buttonRoot, index);
                _cards[i] = card;
            }

            _empty = PanelUi.CreateText("Empty", parent, 4f, -95f,
                274f, 30f, TextContext.None, PanelUi.OffColor,
                TextAlignmentOptions.Center);
            _empty.gameObject.SetActive(false);

            // An empty result used to be a dead end: a line of grey text and no
            // indication of which of the four filters caused it. This is the
            // way out.
            _clearFiltersRoot = PanelUi.CreateButtonRoot("ClearFilters", parent,
                PanelPad, -120f, 96f, RowH, out _, out _clearFiltersLabel);
            PanelUi.BindClick(_clearFiltersRoot, ClearAllFilters);
            _clearFiltersRoot.SetActive(false);
        }

        private void BuildFooter(Transform parent)
        {
            _count = PanelUi.CreateText("Count", parent, 4f, -183f,
                55f, 17f, TextContext.IgnoreSize, 7f, PanelUi.AccentColor,
                TextAlignmentOptions.MidlineLeft);

            _selectFilteredRoot = PanelUi.CreateButtonRoot("SelectFiltered", parent,
                60f, -183f, 57f, 17f, "qmcentral.tip.select_filtered",
                out _, out _selectFilteredLabel);
            PanelUi.BindClick(_selectFilteredRoot, SelectFiltered);
            _clearRoot = PanelUi.CreateButtonRoot("Clear", parent,
                118f, -183f, 34f, 17f, "qmcentral.tip.clear",
                out _, out _clearLabel);
            PanelUi.BindClick(_clearRoot, ClearSelection);
            _previousPageRoot = PanelUi.CreateButtonRoot("Previous", parent,
                153f, -183f, 14f, 17f, "qmcentral.tip.previous_page",
                out _, out var previousLabel);
            previousLabel.text = "<";
            PanelUi.BindClick(_previousPageRoot, () => ChangePage(-1));
            _page = PanelUi.CreateText("Page", parent, 168f, -183f,
                30f, 17f, TextContext.SmallNumbers, PanelUi.ValueColor,
                TextAlignmentOptions.Center);
            _nextPageRoot = PanelUi.CreateButtonRoot("Next", parent,
                199f, -183f, 14f, 17f, "qmcentral.tip.next_page",
                out _, out var nextLabel);
            nextLabel.text = ">";
            PanelUi.BindClick(_nextPageRoot, () => ChangePage(1));

            // Maintenance sits beside destruction: both act on the whole
            // hold rather than on the page in view, and this is the row the
            // player already looks at for bulk actions.
            _repairRoot = PanelUi.CreateButtonRoot("Repair", parent,
                214f, -183f, RepairW, 17f, "qmcentral.tip.repair",
                out _, out _repairLabel);
            _repairButton = PanelUi.ButtonOf(_repairRoot);
            PanelUi.BindClick(_repairRoot, RequestRepairAll);

            _recycleRoot = PanelUi.CreateButtonRoot("Recycle", parent,
                214f, -183f, 64f, 17f, "qmcentral.tip.recycle",
                out _, out _recycleLabel);
            PanelUi.MakeDangerButton(_recycleRoot, _recycleLabel);
            _recycleButton = PanelUi.ButtonOf(_recycleRoot);
            PanelUi.BindClick(_recycleRoot, QueueSelectedForRecycle);

            // The hint strip is gone: vanilla never uses a persistent
            // instruction line, and this one was unreadable at 3.4pt and hidden
            // outright in side mode.  Its content now lives in the per-control
            // tooltips attached above.
        }

        private void RebuildAndRefresh()
        {
            BuildIndex();
            // Scanning every storage for damaged gear and pairing it against
            // every consumable is far too heavy for RefreshLabels, which runs
            // on each keystroke in the search box. The index rebuild is the
            // one moment the hold can actually have changed, so the count is
            // taken here and read from cache everywhere else.
            _repairableCount = CountRepairableItems();
            ClampRecycleSelection();
            RefreshFiltered();
        }

        private void ClampRecycleSelection()
        {
            foreach (var selection in _selectedForRecycle.ToArray())
            {
                if (!Index.TryGetValue(selection.Key, out var entry))
                {
                    _selectedForRecycle.Remove(selection.Key);
                    continue;
                }
                var available = CountRecyclableUnits(entry);
                var units = Mathf.Clamp(selection.Value, 0, available);
                if (units <= 0)
                    _selectedForRecycle.Remove(selection.Key);
                else
                    _selectedForRecycle[selection.Key] = units;
            }
        }

        private void BuildIndex()
        {
            _inventoryIndex.Rebuild(_cargo, _progression, _preferredStacks,
                IsSafeToRecycle);
        }

        private void RefreshFiltered()
        {
            _filtered.Clear();
            var query = (_search?.text ?? string.Empty).Trim();
            foreach (var entry in Index.Values)
            {
                if (!MatchesCategory(entry) || !MatchesDetail(entry)
                    || !MatchesImplantSlot(entry))
                    continue;
                if (query.Length > 0
                    && entry.Id.IndexOf(query,
                        StringComparison.OrdinalIgnoreCase) < 0
                    && entry.Name.IndexOf(query,
                        StringComparison.CurrentCultureIgnoreCase) < 0)
                {
                    continue;
                }
                _filtered.Add(entry);
            }
            EnsureSortModeValid();
            _filtered.Sort(CompareEntries);
            _scrollRow = Mathf.Clamp(_scrollRow, 0, MaxScrollRow);
            RefreshTabs();
            RefreshCards();
            RefreshLabels();
        }

        private void EnsureSortModeValid()
        {
            if (!GetAvailableSortModes().Contains(_sortMode))
                _sortMode = SortMode.Name;
        }

        private List<SortMode> GetAvailableSortModes()
        {
            // TechLevel joins Name and Quantity as a mode every category
            // offers: it reads straight off ItemRecord, which every indexed
            // item has, unlike the resist and damage modes that only exist
            // where the relevant record does.
            var modes = new List<SortMode>
            {
                SortMode.Name,
                SortMode.Quantity,
                SortMode.TechLevel
            };
            if (_category == CargoCategory.Equipment)
            {
                if (_detail == CargoDetail.Any
                    && EquipmentSetKeys.Count > 0)
                    modes.Add(SortMode.Set);
                AddCoreDamageTypes(modes, SortMode.TotalResist);
                return modes;
            }

            if (_category == CargoCategory.Weapons
                || _category == CargoCategory.Ammo)
            {
                AddCoreDamageTypes(modes, SortMode.TotalDamage);
                modes.Add(SortMode.Explosion);
                modes.Add(SortMode.Plasma);
                modes.Add(SortMode.Chaos);
                modes.Add(SortMode.Proton);
                modes.Add(SortMode.Cryo);
            }
            return modes;
        }

        private static void AddCoreDamageTypes(List<SortMode> modes,
            SortMode totalMode)
        {
            modes.Add(totalMode);
            modes.Add(SortMode.Blunt);
            modes.Add(SortMode.Pierce);
            modes.Add(SortMode.Laceration);
            modes.Add(SortMode.Fire);
            modes.Add(SortMode.Cold);
            modes.Add(SortMode.Poison);
            modes.Add(SortMode.Shock);
            modes.Add(SortMode.Beam);
        }

        private int CompareEntries(CentralInventoryEntry a,
            CentralInventoryEntry b)
        {
            var result = 0;
            switch (_sortMode)
            {
                case SortMode.Quantity:
                    result = b.Units.CompareTo(a.Units);
                    break;
                case SortMode.TechLevel:
                    result = GetTechLevel(b).CompareTo(GetTechLevel(a));
                    break;
                case SortMode.Set:
                    result = CompareEquipmentSets(a, b);
                    break;
                case SortMode.TotalResist:
                case SortMode.TotalDamage:
                case SortMode.Blunt:
                case SortMode.Pierce:
                case SortMode.Laceration:
                case SortMode.Fire:
                case SortMode.Cold:
                case SortMode.Poison:
                case SortMode.Shock:
                case SortMode.Beam:
                case SortMode.Explosion:
                case SortMode.Plasma:
                case SortMode.Chaos:
                case SortMode.Proton:
                case SortMode.Cryo:
                    result = IsDamageStrengthCategory()
                        ? GetDamageStrength(b, _sortMode).CompareTo(
                            GetDamageStrength(a, _sortMode))
                        : GetResistance(b, _sortMode).CompareTo(
                            GetResistance(a, _sortMode));
                    break;
            }
            return result != 0 ? result : CompareNames(a, b);
        }

        /// <summary>
        /// Highest tech first, and anything whose ItemRecord could not be
        /// resolved goes to the very bottom rather than pretending to be
        /// tech 0 -- an unknown is not the same as primitive.
        /// </summary>
        private static int GetTechLevel(CentralInventoryEntry entry)
        {
            return entry?.ItemRecord?.TechLevel ?? int.MinValue;
        }

        private static int CompareNames(CentralInventoryEntry a,
            CentralInventoryEntry b)
        {
            return string.Compare(a.Name, b.Name,
                StringComparison.CurrentCultureIgnoreCase);
        }

        private int CompareEquipmentSets(CentralInventoryEntry a,
            CentralInventoryEntry b)
        {
            var aHas = EquipmentSetKeys.TryGetValue(a.Id, out var aKey);
            var bHas = EquipmentSetKeys.TryGetValue(b.Id, out var bKey);
            if (aHas != bHas)
                return aHas ? -1 : 1;
            if (!aHas)
                return CompareNames(a, b);
            var keyCompare = string.Compare(aKey, bKey,
                StringComparison.OrdinalIgnoreCase);
            if (keyCompare != 0)
                return keyCompare;
            var pieceCompare = GetSetPieceOrder(a.ItemClass).CompareTo(
                GetSetPieceOrder(b.ItemClass));
            return pieceCompare != 0 ? pieceCompare : CompareNames(a, b);
        }

        private static int GetSetPieceOrder(ItemClass itemClass)
        {
            if (itemClass == ItemClass.Helmet) return 0;
            if (itemClass == ItemClass.Armor) return 1;
            if (itemClass == ItemClass.Leggings) return 2;
            if (itemClass == ItemClass.Boots) return 3;
            return 4;
        }

        private static float GetResistance(CentralInventoryEntry entry,
            SortMode mode)
        {
            var sheet = entry?.Resist?.ResistSheet;
            if (sheet == null)
                return float.MinValue;
            if (mode == SortMode.TotalResist)
            {
                var total = 0f;
                foreach (var resist in sheet)
                    total += resist.resistPercent;
                return total;
            }
            var damage = GetResistanceDamageId(mode);
            return damage == null ? 0f : entry.Resist.GetResist(damage);
        }

        private static string GetResistanceDamageId(SortMode mode)
        {
            switch (mode)
            {
                case SortMode.Blunt: return "blunt";
                case SortMode.Pierce: return "pierce";
                case SortMode.Laceration: return "lacer";
                case SortMode.Fire: return "fire";
                case SortMode.Cold: return "cold";
                case SortMode.Poison: return "poison";
                case SortMode.Shock: return "shock";
                case SortMode.Beam: return "beam";
                default: return null;
            }
        }

        private bool IsDamageStrengthCategory()
        {
            return _category == CargoCategory.Weapons
                   || _category == CargoCategory.Ammo;
        }

        private static bool IsDamageStrengthSort(SortMode mode)
        {
            return mode == SortMode.TotalDamage
                   || GetDamageTypeId(mode) != null;
        }

        private static float GetDamageStrength(CentralInventoryEntry entry,
            SortMode mode)
        {
            if (entry == null)
                return float.MinValue;

            string damageType;
            float strength;
            if (entry.Weapon != null)
            {
                var component = entry.Representative
                    ?.Comp<WeaponComponent>();
                var damage = component?.Damage ?? entry.Weapon.Damage;
                var ammo = component?.OverridenAmmo
                           ?? GetDefaultWeaponAmmo(entry.Weapon);
                damageType = !string.IsNullOrEmpty(ammo?.DmgType)
                    ? ammo.DmgType
                    : damage.damage;
                var multiplier = component?.CurrentFireMode == null
                    ? 1f
                    : component.CurrentFireMode.DamageMult;
                multiplier *= ammo == null ? 1f : ammo.DamageMult;
                if (component?.HasModifierAmmo == true
                    && component.CurrentAmmoType != null)
                {
                    multiplier *= component.CurrentAmmoType.DamageMult;
                }
                strength = (damage.minDmg + damage.maxDmg) * 0.5f
                           * multiplier;
            }
            else if (entry.Ammo != null)
            {
                damageType = entry.Ammo.DmgType;
                strength = entry.Ammo.DamageMult;
            }
            else
            {
                return float.MinValue;
            }

            var requestedType = GetDamageTypeId(mode);
            if (requestedType != null
                && !string.Equals(requestedType, damageType,
                    StringComparison.OrdinalIgnoreCase))
            {
                return float.MinValue;
            }
            return strength;
        }

        private static AmmoRecord GetDefaultWeaponAmmo(WeaponRecord weapon)
        {
            if (weapon == null)
                return null;
            string ammoId = null;
            if (weapon.OverrideAmmo != null && weapon.OverrideAmmo.Count > 0
                && !string.IsNullOrEmpty(weapon.OverrideAmmo[0]))
            {
                ammoId = weapon.OverrideAmmo[0];
            }
            else if (!string.IsNullOrEmpty(weapon.DefaultAmmoId))
            {
                ammoId = weapon.DefaultAmmoId;
            }
            return string.IsNullOrEmpty(ammoId)
                ? null
                : Data.Items.GetSimpleRecord<AmmoRecord>(ammoId);
        }

        private static string GetDamageTypeId(SortMode mode)
        {
            switch (mode)
            {
                case SortMode.Blunt: return "blunt";
                case SortMode.Pierce: return "pierce";
                case SortMode.Laceration: return "lacer";
                case SortMode.Fire: return "fire";
                case SortMode.Cold: return "cold";
                case SortMode.Poison: return "poison";
                case SortMode.Shock: return "shock";
                case SortMode.Beam: return "beam";
                case SortMode.Explosion: return "explosion";
                case SortMode.Plasma: return "plasma";
                case SortMode.Chaos: return "chaos";
                case SortMode.Proton: return "proton";
                case SortMode.Cryo: return "cryo";
                default: return null;
            }
        }

        private void RefreshTabs()
        {
            foreach (var tab in _categoryTabs)
            {
                var selected = tab.Category == _category;
                PanelUi.SetSurfaceSelected(tab.Background, selected);
                PanelUi.SetCaptionColor(tab.Label, selected ? PanelUi.AccentColor : PanelUi.ValueColor);
                tab.Label.text = Localization.Get("qmcentral.category."
                                                  + tab.Category.ToString()
                                                      .ToLowerInvariant());
                tab.Label.font = Localization.GetActualFont();
            }

            var details = GetDetails(_category);
            LayoutDetailTabs(details);
            for (var i = 0; i < _detailTabs.Count; i++)
            {
                var tab = _detailTabs[i];
                var visible = i < details.Length;
                tab.Root.SetActive(visible);
                if (!visible)
                    continue;
                tab.Detail = details[i];
                var selected = tab.Detail == _detail;
                PanelUi.SetSurfaceSelected(tab.Background, selected);
                PanelUi.SetCaptionColor(tab.Label, selected ? PanelUi.AccentColor : PanelUi.ValueColor);
                tab.Label.text = Localization.Get("qmcentral.detail."
                                                  + tab.Detail.ToString()
                                                      .ToLowerInvariant());
                tab.Label.font = Localization.GetActualFont();
            }
        }

        /// <summary>
        /// Lays out a tab strip as one or more edge-to-edge rows and returns
        /// the y of the bottom edge.  Each row's rounding remainder goes to its
        /// last tab, so a row is always flush on both sides and never ends in
        /// the empty cell a fixed column count leaves behind.
        /// </summary>
        private float LayoutTabRows(List<FilterTab> tabs, float topY,
            int[] rowCounts, float innerWidth)
        {
            var index = 0;
            var y = topY;
            foreach (var count in rowCounts)
            {
                if (count <= 0)
                    continue;
                var width = Mathf.Floor((innerWidth - Gap * (count - 1)) / count);
                var lastWidth = innerWidth - (width + Gap) * (count - 1);
                for (var column = 0; column < count && index < tabs.Count;
                     column++, index++)
                {
                    PanelUi.SetTopLeft((RectTransform)tabs[index].Root.transform,
                        PanelPad + column * (width + Gap), y,
                        column == count - 1 ? lastWidth : width, TabH);
                    tabs[index].Label.fontSize = 7f;
                }
                y -= TabH + Gap;
            }
            return y + Gap;
        }

        private void LayoutDetailTabs(CargoDetail[] details)
        {
            var visibleCount = details == null ? 0 : details.Length;
            if (visibleCount <= 0)
                return;

            var innerWidth = CurrentInnerWidth;
            // Cap the tab width and left-align when there is spare room.  A
            // single detail stretched across the whole panel stopped reading as
            // a tab and started reading as a section heading.
            var stretched = Mathf.Floor(
                (innerWidth - Gap * (visibleCount - 1)) / visibleCount);
            var width = Mathf.Min(stretched, MaxTabWidth);
            var flush = width >= stretched;
            var lastWidth = flush
                ? innerWidth - (width + Gap) * (visibleCount - 1)
                : width;

            for (var i = 0; i < _detailTabs.Count; i++)
            {
                if (i >= visibleCount)
                    continue;
                PanelUi.SetTopLeft((RectTransform)_detailTabs[i].Root.transform,
                    PanelPad + i * (width + Gap), _detailRowY,
                    i == visibleCount - 1 ? lastWidth : width, TabH);
                _detailTabs[i].Label.fontSize = 7f;
            }
        }

        private void RefreshCards()
        {
            for (var i = 0; i < MaxCardsPerPage; i++)
            {
                var card = _cards[i];
                var itemIndex = _scrollRow * _cardColumns + i;
                var visible = i < CurrentPageSize
                              && itemIndex < _filtered.Count;
                card.Root.SetActive(visible);
                card.ItemId = visible ? _filtered[itemIndex].Id : null;
                if (!visible)
                    continue;

                var entry = _filtered[itemIndex];
                if (card.ItemSlot == null)
                {
                    var slotObject = UI.Pools.ItemSlots.Take();
                    slotObject.name = "QM_CentralItemSlot";
                    slotObject.transform.SetParent(card.SlotHost, false);
                    card.ItemSlot = slotObject.GetComponent<ItemSlot>();
                }
                var slotRect = (RectTransform)card.ItemSlot.transform;
                // Match the original ItemSlot prefab's layout instead of the
                // top-left layout used by our hand-built controls. Keeping
                // these nine slots for this panel also prevents any central
                // layout state from leaking into the global Cargo grid pool.
                slotRect.anchorMin = new Vector2(0f, 0.5f);
                slotRect.anchorMax = new Vector2(0f, 0.5f);
                slotRect.pivot = new Vector2(0.5f, 0.5f);
                // Centre the slot in the icon column BEFORE Initialize, so the
                // hover cache it takes is already correct and no
                // CachedLocalPosition patch is needed afterwards.  Centring is
                // what keeps a 50px two-cell icon inside the card.
                slotRect.anchoredPosition = new Vector2(SlotColumnW * 0.5f, 0f);
                slotRect.localRotation = Quaternion.identity;
                // Vanilla NEVER scales a slot -- ItemSlot resolves 24x24, or
                // 50x24 for a two-cell item. The old 0.64 / 1.08 localScale
                // stretched point-filtered pixel art onto half pixels, which is
                // what made genuine vanilla icons look broken.
                slotRect.localScale = Vector3.one;
                card.ItemSlot.ConfigureDefaultSize(SlotModule, SlotModule);
                card.ItemSlot.CellPos = entry.Representative.InventoryPos;
                card.ItemSlot.Initialize(entry.Representative,
                    entry.Representative.Storage);
                card.ItemSlot.CachedLocalPosition = slotRect.localPosition;

                card.Name.text = entry.Name;
                card.Name.font = Localization.GetActualFont();
                var preferredUnits = GetPreferredStackUnits(entry);
                card.Quantity.text = preferredUnits > 0
                    ? string.Format(Localization.Get("qmcentral.ready_stack"),
                        entry.Units, preferredUnits)
                    : GetCardValueText(entry);
                card.Quantity.font = Localization.GetActualFont();
                var selectedUnits = GetSelectedUnits(entry.Id);
                var selected = selectedUnits > 0;
                VanillaSkin.SetSprite(card.Background,
                    selected ? VanillaSkin.SlotSelected : VanillaSkin.SlotNormal,
                    selected ? PanelUi.SelectedColor : PanelUi.CardColor);
                card.SelectButton.SetInteractable(CanSelectEntry(entry));
                card.SelectLabel.text = selected
                    ? "×" + selectedUnits
                    : Localization.Get("qmcentral.select");
                PanelUi.SetCaptionColor(card.SelectLabel, selected ? PanelUi.AccentColor : PanelUi.OffColor);
                card.SelectLabel.font = Localization.GetActualFont();
            }
            var showEmpty = _filtered.Count == 0;
            _empty.gameObject.SetActive(showEmpty);
            // Only offer it when there is actually something to clear -- an
            // empty ship should not suggest the filters are at fault.
            if (_clearFiltersRoot != null)
                _clearFiltersRoot.SetActive(showEmpty && HasActiveFilters());
        }

        private string GetCardValueText(CentralInventoryEntry entry)
        {
            if (IsDamageStrengthCategory()
                && IsDamageStrengthSort(_sortMode))
            {
                var strength = GetDamageStrength(entry, _sortMode);
                return strength <= float.MinValue
                    ? "×" + entry.Units
                    : string.Format(Localization.Get(
                        "qmcentral.damage_value"), entry.Units,
                        strength.ToString("0.##"));
            }
            if (_category == CargoCategory.Equipment
                && (_sortMode == SortMode.TotalResist
                    || GetResistanceDamageId(_sortMode) != null))
            {
                if (entry.Resist?.ResistSheet == null)
                    return "×" + entry.Units;
                return string.Format(Localization.Get(
                    "qmcentral.resist_value"), entry.Units,
                    Mathf.RoundToInt(GetResistance(entry, _sortMode)));
            }
            return "×" + entry.Units;
        }

        private void RefreshLabels()
        {
            _title.text = Localization.Get("qmcentral.title");
            _title.font = Localization.GetActualFont();
            _closeLabel.text = Localization.Get("qmcentral.close");
            _closeLabel.font = Localization.GetActualFont();
            _searchPlaceholder.text = Localization.Get("qmcentral.search");
            _searchPlaceholder.font = Localization.GetActualFont();
            if (_sortLabel != null)
            {
                _sortLabel.text = string.Format(
                    Localization.Get("qmcentral.sort_button"),
                    GetSortModeLabel(_sortMode));
                _sortLabel.font = Localization.GetActualFont();
            }
            if (_slotFilterLabel != null)
            {
                _slotFilterLabel.text = string.Format(
                    Localization.Get("qmcentral.slot_button"),
                    string.IsNullOrEmpty(_implantSlotFilter)
                        ? Localization.Get("qmcentral.slot_all")
                        : GetSlotDisplayName(_implantSlotFilter));
                _slotFilterLabel.font = Localization.GetActualFont();
            }
            _selectFilteredLabel.text = Localization.Get(
                AreAllFilteredSelected()
                    ? "qmcentral.deselect_filtered"
                    : "qmcentral.select_filtered");
            _selectFilteredLabel.font = Localization.GetActualFont();
            _clearLabel.text = Localization.Get("qmcentral.clear");
            _clearLabel.font = Localization.GetActualFont();
            _empty.text = Localization.Get("qmcentral.empty");
            _empty.font = Localization.GetActualFont();
            if (_clearFiltersLabel != null)
            {
                _clearFiltersLabel.text =
                    Localization.Get("qmcentral.clear_filters");
                _clearFiltersLabel.font = Localization.GetActualFont();
            }
            // Page numbers again: the wheel and the arrows both move a whole
            // screen, so a screen IS a page.
            var rows = Mathf.Max(1, _cardRows);
            var totalPages = Mathf.Max(1, Mathf.CeilToInt(TotalRows / (float)rows));
            var page = Mathf.Clamp(_scrollRow / rows + 1, 1, totalPages);
            _page.text = string.Format(Localization.Get("qmcentral.page"),
                page, totalPages);
            _page.font = Localization.GetActualFont();
            var units = 0;
            foreach (var entry in _filtered)
                units += entry.Units;
            // Grouped: a six-figure unit total is unreadable as a bare run of
            // digits, and this readout is the panel's headline number.
            var types = _filtered.Count.ToString("N0",
                CultureInfo.InvariantCulture);
            var unitText = units.ToString("N0", CultureInfo.InvariantCulture);
            _count.text = _countCompact
                ? types + " / " + unitText
                : string.Format(Localization.Get("qmcentral.count"),
                    types, unitText);
            _count.font = Localization.GetActualFont();

            _operator.text = GetOperatorDisplayName(_mercenary);
            _operator.font = Localization.GetActualFont();
            var switchableOperators = GetSwitchableOperators();
            var canSwitch = switchableOperators.Count > 1;
            if (_previousOperatorButton != null)
                _previousOperatorButton.SetInteractable(canSwitch);
            if (_nextOperatorButton != null)
                _nextOperatorButton.SetInteractable(canSwitch);
            if (_operatorDropdownButton != null)
                _operatorDropdownButton.SetInteractable(canSwitch);
            RefreshOperatorPanelButton();
            RefreshRecycleButton();
            RefreshRepairButton();
        }

        private void RefreshRecycleButton()
        {
            var count = CountSelectedRecyclableUnits();
            var busy = _cargo == null || _cargo.RecyclingInProgress
                       || _progression?.HasStoreConstructorDepartment != true;
            _recycleButton.SetInteractable(!busy && count > 0);
            if (busy)
                _recycleLabel.text = Localization.Get("qmcentral.recycle_busy");
            else if (count == 0)
                _recycleLabel.text = Localization.Get("qmcentral.recycle_none");
            else
                _recycleLabel.text = string.Format(
                    Localization.Get("qmcentral.recycle"), count);
            _recycleLabel.font = Localization.GetActualFont();
        }

        /// <summary>
        /// Scoped to the selected agent, so with no agent there is nothing to
        /// act on and the button says so rather than doing something broader.
        /// </summary>
        private void RefreshRepairButton()
        {
            if (_repairButton == null || _repairLabel == null)
                return;
            var count = _repairableCount;
            _repairButton.SetInteractable(count > 0);
            _repairLabel.text = Localization.Get(count == 0
                ? "qmcentral.repair_none"
                : "qmcentral.repair");
            _repairLabel.font = Localization.GetActualFont();
        }

        private int CountRepairableItems()
        {
            try
            {
                return CentralRepairService.CountRepairable(_mercenary,
                    _cargo, _progression);
            }
            catch (Exception e)
            {
                Debug.LogError(Plugin.LogPrefix
                               + "could not count repairable items: " + e);
                return 0;
            }
        }

        private void RequestRepairAll()
        {
            if (UI.Drag.IsDragging)
                return;
            // Recounted rather than read from cache: this is a one-shot
            // user action, and the number shown in the dialog is the one they
            // are agreeing to.
            var count = CountRepairableItems();
            _repairableCount = count;
            if (count == 0)
            {
                RefreshRepairButton();
                return;
            }
            try
            {
                // Same dialog the batch recycle uses, and for the same reason:
                // spending kits and grinding max durability off gear cannot be
                // undone, so it may not happen on one stray click.
                UI.Chain<ConfirmDialogWindow>().Invoke(v =>
                    v.Configure(OnRepairConfirmed,
                        "qmcentral.repair_dialog",
                        focusOnApply: false,
                        arg0: count.ToString("N0",
                            CultureInfo.InvariantCulture),
                        applyCaption: "qmcentral.repair_apply",
                        cancelCaption: "ui.dialog.no_option")).Show();
            }
            catch (Exception e)
            {
                Debug.LogError(Plugin.LogPrefix
                               + "could not open the repair confirmation: " + e);
            }
        }

        private void OnRepairConfirmed(ConfirmDialogWindow.Option option)
        {
            if (option != ConfirmDialogWindow.Option.Yes)
                return;
            // The dialog is modal but the panel behind it stays live, so the
            // hold may have changed while it was up.
            _repairableCount = CountRepairableItems();
            if (_repairableCount == 0)
            {
                RefreshRepairButton();
                return;
            }
            ExecuteRepairAll();
        }

        private void ExecuteRepairAll()
        {
            RepairOutcome outcome;
            try
            {
                outcome = CentralRepairService.RepairAll(_mercenary,
                    _cargo, _progression, _perkFactory);
            }
            catch (Exception e)
            {
                Debug.LogError(Plugin.LogPrefix + "batch repair failed: " + e);
                RebuildAndRefresh();
                return;
            }

            Debug.Log(Plugin.LogPrefix + "repaired " + outcome.ItemsRepaired
                      + " item(s) with " + outcome.Applications
                      + " application(s); " + outcome.Skipped
                      + " left untouched.");
            var sounds = SingletonMonoBehaviour<SoundsStorage>.Instance;
            var controller = SingletonMonoBehaviour<SoundController>.Instance;
            if (controller != null && sounds != null)
            {
                controller.PlayUiSound(outcome.DidSomething
                    ? sounds.Repair
                    : sounds.EmptyAttack, isUnique: true);
            }
            // The agent pane is a VANILLA inventory view and the condition
            // bars it draws just changed, so the screen has to repaint before
            // the panel's own index is rebuilt. Recycling never needed this --
            // it moves cargo the agent pane does not show.
            _screen?.RefreshView();
            RebuildAndRefresh();
            ShowRepairSummary(outcome);
        }

        /// <summary>
        /// What the pass actually spent. The condition bars on the cards show
        /// the gain, but nothing else would show the cost, and the cost is the
        /// half the player cannot see coming.
        /// </summary>
        private void ShowRepairSummary(RepairOutcome outcome)
        {
            if (!outcome.DidSomething || outcome.Consumed.Count == 0)
                return;
            try
            {
                var lines = new List<ExchangeItemLine>();
                foreach (var consumed in outcome.Consumed)
                {
                    lines.Add(new ExchangeItemLine
                    {
                        ItemId = consumed.Key,
                        Count = consumed.Value.Amount,
                        // Charges off a multi-use kit are not whole kits, and
                        // the default "x7" would claim they were.
                        AmountKey = consumed.Value.Charges
                            ? "qmcentral.repair_charges"
                            : null,
                    });
                }
                lines.Sort((a, b) => b.Count.CompareTo(a.Count));
                _repairSummary?.Show(string.Format(
                        Localization.Get("qmcentral.repair_summary_title"),
                        outcome.ItemsRepaired),
                    lines, "qmcentral.repair_summary_note");
            }
            catch (Exception e)
            {
                Debug.LogError(Plugin.LogPrefix
                               + "could not show the repair summary: " + e);
            }
        }

        private void SelectCategory(CargoCategory category)
        {
            if (_category == category)
                return;
            _category = category;
            _detail = CargoDetail.Any;
            _sortMode = SortMode.Name;
            _implantSlotFilter = null;
            CloseAllDropdowns();
            _scrollRow = 0;
            ApplyResponsiveLayout();
            RefreshFiltered();
            RefreshOperatorPanelButton();
        }

        private List<Mercenary> GetSwitchableOperators()
        {
            var result = new List<Mercenary>();
            var values = Plugin.GameState?.Get<Mercenaries>()?.Values;
            if (values == null)
                return result;
            foreach (var mercenary in values)
            {
                if (mercenary != null
                    && mercenary.State != MercenaryState.Cloning
                    && mercenary.State != MercenaryState.InRaid)
                {
                    result.Add(mercenary);
                }
            }
            return result;
        }

        private void ToggleOperatorDropdown()
        {
            if (_operatorDropdownRoot == null || UI.Drag.IsDragging)
                return;
            if (_operatorDropdownRoot.activeSelf)
            {
                CloseDropdown(_operatorDropdownRoot);
                return;
            }
            CloseAllDropdowns();
            RebuildOperatorDropdown();
            _operatorDropdownRoot.SetActive(true);
            _operatorDropdownRoot.transform.SetAsLastSibling();
        }

        private void RebuildOperatorDropdown()
        {
            foreach (var row in _operatorDropdownRows)
                Destroy(row);
            _operatorDropdownRows.Clear();

            var operators = GetSwitchableOperators();
            operators.Sort((a, b) => string.Compare(
                GetOperatorDisplayName(a), GetOperatorDisplayName(b),
                StringComparison.CurrentCultureIgnoreCase));
            const float rowHeight = 15f;
            const float width = 170f;
            var height = Mathf.Max(rowHeight + 4f,
                operators.Count * rowHeight + 4f);
            ((RectTransform)_operatorDropdownRoot.transform).sizeDelta =
                new Vector2(width, height);

            for (var i = 0; i < operators.Count; i++)
            {
                var mercenary = operators[i];
                var row = PanelUi.CreateButtonRoot("Operator" + i,
                    _operatorDropdownRoot.transform, 2f,
                    -2f - i * rowHeight, width - 4f, rowHeight - 1f,
                    out var rowBackground, out var label);
                label.text = GetOperatorDisplayName(mercenary);
                label.fontSize = 6f;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.margin = new Vector4(4f, 0f, 2f, 0f);
                PanelUi.SetSurfaceSelected(rowBackground,
                    ReferenceEquals(mercenary, _mercenary));
                PanelUi.BindClick(row, () => SelectOperatorFromDropdown(mercenary));
                _operatorDropdownRows.Add(row);
            }
        }

        private void SelectOperatorFromDropdown(Mercenary mercenary)
        {
            CloseDropdown(_operatorDropdownRoot);
            if (mercenary == null || ReferenceEquals(mercenary, _mercenary))
                return;
            if (Plugin.SwitchCentralOperator(_screen, mercenary,
                    _operatorPanelVisible))
            {
                _mercenary = mercenary;
                RefreshPresetBar();
                RefreshLabels();
            }
        }

        private void ToggleSortDropdown()
        {
            if (_sortDropdownRoot == null || UI.Drag.IsDragging)
                return;
            if (_sortDropdownRoot.activeSelf)
            {
                CloseDropdown(_sortDropdownRoot);
                return;
            }
            CloseAllDropdowns();
            RebuildSortDropdown();
            _sortDropdownRoot.SetActive(true);
            _sortDropdownRoot.transform.SetAsLastSibling();
        }

        private void RebuildSortDropdown()
        {
            PanelUi.ClearDropdownRows(_sortDropdownRows);
            var modes = GetAvailableSortModes();
            const float rowHeight = 14f;
            var width = _operatorPanelVisible ? 88f : 108f;
            ((RectTransform)_sortDropdownRoot.transform).sizeDelta =
                new Vector2(width, modes.Count * rowHeight + 4f);
            for (var i = 0; i < modes.Count; i++)
            {
                var mode = modes[i];
                var row = PanelUi.CreateButtonRoot("Sort" + mode,
                    _sortDropdownRoot.transform, 2f, -2f - i * rowHeight,
                    width - 4f, rowHeight - 1f, out var background,
                    out var label);
                label.text = GetSortModeLabel(mode);
                label.fontSize = 6f;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.margin = new Vector4(4f, 0f, 2f, 0f);
                PanelUi.SetSurfaceSelected(background, mode == _sortMode);
                PanelUi.BindClick(row, () => SelectSortMode(mode));
                _sortDropdownRows.Add(row);
            }
        }

        private void SelectSortMode(SortMode mode)
        {
            CloseDropdown(_sortDropdownRoot);
            _sortMode = mode;
            _scrollRow = 0;
            RefreshFiltered();
        }

        private string GetSortModeLabel(SortMode mode)
        {
            var prefix = IsDamageStrengthCategory()
                         && IsDamageStrengthSort(mode)
                ? "qmcentral.sort.damage."
                : "qmcentral.sort.";
            return Localization.Get(prefix
                                    + mode.ToString().ToLowerInvariant());
        }

        private void ToggleSlotFilterDropdown()
        {
            if (_slotFilterDropdownRoot == null || UI.Drag.IsDragging)
                return;
            if (_slotFilterDropdownRoot.activeSelf)
            {
                CloseDropdown(_slotFilterDropdownRoot);
                return;
            }
            CloseAllDropdowns();
            RebuildSlotFilterDropdown();
            _slotFilterDropdownRoot.SetActive(true);
            _slotFilterDropdownRoot.transform.SetAsLastSibling();
        }

        private void RebuildSlotFilterDropdown()
        {
            PanelUi.ClearDropdownRows(_slotFilterDropdownRows);
            var slots = GetAvailableImplantSlots();
            slots.Insert(0, null);
            const float rowHeight = 14f;
            var width = _operatorPanelVisible ? 88f : 108f;
            ((RectTransform)_slotFilterDropdownRoot.transform).sizeDelta =
                new Vector2(width, slots.Count * rowHeight + 4f);
            for (var i = 0; i < slots.Count; i++)
            {
                var slot = slots[i];
                var row = PanelUi.CreateButtonRoot("Slot" + i,
                    _slotFilterDropdownRoot.transform, 2f,
                    -2f - i * rowHeight, width - 4f, rowHeight - 1f,
                    out var background, out var label);
                label.text = slot == null
                    ? Localization.Get("qmcentral.slot_all")
                    : GetSlotDisplayName(slot);
                label.fontSize = 6f;
                label.alignment = TextAlignmentOptions.MidlineLeft;
                label.margin = new Vector4(4f, 0f, 2f, 0f);
                PanelUi.SetSurfaceSelected(background, string.Equals(slot,
                    _implantSlotFilter, StringComparison.OrdinalIgnoreCase));
                PanelUi.BindClick(row, () => SelectImplantSlot(slot));
                _slotFilterDropdownRows.Add(row);
            }
        }

        private void SelectImplantSlot(string slot)
        {
            CloseDropdown(_slotFilterDropdownRoot);
            _implantSlotFilter = slot;
            _scrollRow = 0;
            RefreshFiltered();
        }

        private List<string> GetAvailableImplantSlots()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in Index.Values)
            {
                if (!MatchesCategory(entry) || !MatchesDetail(entry))
                    continue;
                foreach (var slot in EnumerateImplantSlots(entry))
                    result.Add(slot);
            }
            var list = result.ToList();
            list.Sort((a, b) => string.Compare(GetSlotDisplayName(a),
                GetSlotDisplayName(b),
                StringComparison.CurrentCultureIgnoreCase));
            return list;
        }

        private static IEnumerable<string> EnumerateImplantSlots(
            CentralInventoryEntry entry)
        {
            if (!string.IsNullOrWhiteSpace(entry?.Implant?.SlotType))
                yield return entry.Implant.SlotType;
            var woundSlots = entry?.Augmentation?.WoundSlotIds;
            if (woundSlots == null)
                yield break;
            foreach (var woundSlotId in woundSlots)
            {
                var slot = Data.WoundSlots.GetRecord(woundSlotId)?.SlotType;
                if (!string.IsNullOrWhiteSpace(slot))
                    yield return slot;
            }
        }

        private static string GetSlotDisplayName(string slot)
        {
            if (string.IsNullOrWhiteSpace(slot))
                return Localization.Get("qmcentral.slot_all");
            var key = "tooltip.implant_" + slot;
            var localized = Localization.Get(key, false);
            return localized == key ? slot : localized;
        }

        private static void CloseDropdown(GameObject dropdown)
        {
            if (dropdown == null)
                return;
            var wasVisible = dropdown.activeInHierarchy;
            dropdown.SetActive(false);
            if (wasVisible)
                ModInputGate.ConsumePointerRelease();
        }


        private void CycleOperator(int direction)
        {
            if (UI.Drag.IsDragging)
                return;
            var operators = GetSwitchableOperators();
            if (operators.Count == 0)
                return;
            var current = operators.IndexOf(_mercenary);
            if (current < 0)
                current = direction >= 0 ? -1 : 0;
            var next = (current + direction) % operators.Count;
            if (next < 0)
                next += operators.Count;
            var selected = operators[next];
            if (ReferenceEquals(selected, _mercenary))
                return;
            if (Plugin.SwitchCentralOperator(_screen, selected,
                    _operatorPanelVisible))
            {
                _mercenary = selected;
                RefreshPresetBar();
                RefreshLabels();
            }
        }

        internal bool TryConsumeCentralControlClick(ItemSlot slot)
        {
            if (!_centralMode || slot == null)
                return true;
            if (!IsCentralItemSlot(slot))
                return true;
            if (ModInputGate.IsFrameBlocked)
                return false;

            // DragSweep calls again when a refreshed aggregate card receives
            // the next stack. One physical Ctrl+LMB press is one transfer.
            if (!Input.GetMouseButtonDown(0) && !Input.GetMouseButtonUp(0))
                return false;
            if (_centralControlClickFrame == Time.frameCount
                && ReferenceEquals(_centralControlClickSlot, slot))
            {
                return false;
            }
            _centralControlClickFrame = Time.frameCount;
            _centralControlClickSlot = slot;
            return true;
        }

        internal bool IsCentralItemSlot(ItemSlot slot)
        {
            if (!_centralMode || slot == null)
                return false;
            foreach (var card in _cards)
            {
                if (card != null && ReferenceEquals(card.ItemSlot, slot))
                    return true;
            }
            return false;
        }

        internal void CaptureCentralSplit(BasePickupItem source, int splitUnits)
        {
            ClearPendingSplit();
            if (!_centralMode || source == null || splitUnits <= 0)
                return;
            _pendingSplitItemId = source.Id;
            _pendingSplitUnits = splitUnits;
            foreach (var item in EnumerateCentralItems())
                _itemsBeforeSplit.Add(item);
        }

        internal void CompleteCentralSplit()
        {
            try
            {
                if (string.IsNullOrEmpty(_pendingSplitItemId))
                    return;
                BasePickupItem fallback = null;
                foreach (var item in EnumerateCentralItems())
                {
                    if (item == null || item.Id != _pendingSplitItemId
                        || _itemsBeforeSplit.Contains(item))
                    {
                        continue;
                    }
                    fallback ??= item;
                    if (Mathf.Max(1, item.StackCount) == _pendingSplitUnits)
                    {
                        fallback = item;
                        break;
                    }
                }
                if (fallback == null)
                {
                    RequestRefresh();
                    return;
                }

                _preferredStacks[_pendingSplitItemId] = fallback;
                // The vanilla split callback has already refreshed Arsenal.
                // Rebuild the aggregate cards synchronously so the card slot
                // points at the newly-created stack before starting the drag.
                _refreshQueued = false;
                RebuildAndRefresh();
                var splitSlot = FindCentralSlot(fallback);
                if (splitSlot == null)
                {
                    RequestRefresh();
                    return;
                }

                // Split confirmation is released over the context-menu button.
                // ManualStartDrag gives the player the split stack immediately;
                // Pause consumes that same mouse release so it cannot instantly
                // end the new drag on whatever happens to be underneath.
                UI.Drag.ManualStartDrag(splitSlot);
                UI.Drag.Pause(0.18f);
            }
            finally
            {
                ClearPendingSplit();
            }
        }

        private ItemSlot FindCentralSlot(BasePickupItem item)
        {
            if (item == null)
                return null;
            foreach (var card in _cards)
            {
                if (card?.ItemSlot != null
                    && ReferenceEquals(card.ItemSlot.Item, item))
                {
                    return card.ItemSlot;
                }
            }
            return null;
        }

        private IEnumerable<BasePickupItem> EnumerateCentralItems()
        {
            if (_cargo == null)
                yield break;
            foreach (var storage in _cargo.ShipCargo)
            {
                if (storage == null)
                    continue;
                foreach (var item in storage.Items)
                    if (item != null)
                        yield return item;
            }
            if (_progression?.HasStoreFridge == true
                && _cargo.FridgeStorage != null)
            {
                foreach (var item in _cargo.FridgeStorage.Items)
                    if (item != null)
                        yield return item;
            }
            if (_progression?.HasStoreConstructorDepartment == true
                && !_cargo.RecyclingInProgress
                && _cargo.RecyclingStorage != null)
            {
                foreach (var item in _cargo.RecyclingStorage.Items)
                    if (item != null)
                        yield return item;
            }
        }

        private void ClearPendingSplit()
        {
            _pendingSplitItemId = null;
            _pendingSplitUnits = 0;
            _itemsBeforeSplit.Clear();
        }

        private int GetPreferredStackUnits(CentralInventoryEntry entry)
        {
            return entry != null
                   && _preferredStacks.TryGetValue(entry.Id, out var preferred)
                   && preferred != null
                   && ReferenceEquals(entry.Representative, preferred)
                ? Mathf.Max(1, preferred.StackCount)
                : 0;
        }

        private void SelectDetail(int index)
        {
            if (index < 0 || index >= _detailTabs.Count
                || !_detailTabs[index].Root.activeSelf)
            {
                return;
            }
            _detail = _detailTabs[index].Detail;
            // The body-slot filter deliberately SURVIVES a sub-tab change:
            // picking "left arm" and then flipping between limbs and implants
            // is one continuous thought, and resetting it to All every time
            // meant re-selecting the slot after every switch. Leaving the
            // category still clears it (SelectCategory), which is the boundary
            // where the filter stops being meaningful.
            CloseAllDropdowns();
            _scrollRow = 0;
            ApplyResponsiveLayout();
            RefreshFiltered();
        }

        private void AddCardClickHandler(GameObject target, int index)
        {
            var trigger = target.GetComponent<EventTrigger>()
                          ?? target.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry
            {
                eventID = EventTriggerType.PointerClick,
                callback = new EventTrigger.TriggerEvent(),
            };
            entry.callback.AddListener(data =>
            {
                if (!(data is PointerEventData pointer))
                    return;
                if (pointer.button == PointerEventData.InputButton.Right)
                {
                    OpenCardQuantityPopup(index);
                    return;
                }
                if (pointer.button != PointerEventData.InputButton.Left)
                    return;
                // Shift, not Ctrl: LeftControl is bound to ForceCursorMove and
                // Ctrl+LMB on a card icon already drives the vanilla transfer
                // through the patched DragController control-click callback.
                // PointerEventData carries no modifier state, so read it live.
                if (Input.GetKey(KeyCode.LeftShift)
                    || Input.GetKey(KeyCode.RightShift))
                {
                    SelectRangeTo(index);
                }
                else
                {
                    ToggleCard(index);
                }
            });
            if (trigger.triggers == null)
                trigger.triggers = new List<EventTrigger.Entry>();
            trigger.triggers.Add(entry);
        }

        private int IndexOfAnchor()
        {
            if (string.IsNullOrEmpty(_selectionAnchorId))
                return -1;
            // An anchor only means anything while its own item is still
            // selected. That one rule covers every way a selection can go away
            // -- the Clear button, Select-all toggling off, the context menu,
            // or clicking items off by hand -- without having to catch each
            // mutation site separately.
            if (!_selectedForRecycle.ContainsKey(_selectionAnchorId))
            {
                _selectionAnchorId = null;
                return -1;
            }
            for (var i = 0; i < _filtered.Count; i++)
            {
                if (_filtered[i] != null && _filtered[i].Id == _selectionAnchorId)
                    return i;
            }
            return -1;
        }

        private int FilteredIndexOfCard(int index)
        {
            if (index < 0 || index >= _cards.Length)
                return -1;
            var filteredIndex = _scrollRow * _cardColumns + index;
            return filteredIndex >= 0 && filteredIndex < _filtered.Count
                ? filteredIndex
                : -1;
        }

        /// <summary>
        /// Selects every recyclable entry between the anchor and this card, at
        /// its full recyclable amount. Falls back to a plain toggle when there
        /// is no anchor yet.
        /// </summary>
        private void SelectRangeTo(int index)
        {
            var to = FilteredIndexOfCard(index);
            if (to < 0)
                return;

            var from = IndexOfAnchor();
            if (from < 0)
            {
                // Nothing to extend from yet, so this click becomes the anchor.
                ToggleCard(index);
                return;
            }

            var lo = Mathf.Min(from, to);
            var hi = Mathf.Max(from, to);
            for (var i = lo; i <= hi; i++)
            {
                var entry = _filtered[i];
                if (entry == null || !CanSelectEntry(entry))
                    continue;
                var available = SelectableUnits(entry);
                if (available > 0)
                    Selection[entry.Id] = available;
            }

            RefreshCards();
            RefreshLabels();
        }

        private void ToggleCard(int index)
        {
            if (index < 0 || index >= _cards.Length)
                return;
            var id = _cards[index].ItemId;
            if (string.IsNullOrEmpty(id)
                || !Index.TryGetValue(id, out var entry)
                || !CanSelectEntry(entry))
            {
                return;
            }
            var available = SelectableUnits(entry);
            if (available <= 0)
                return;
            if (GetSelectedUnits(entry.Id) >= available)
            {
                Selection.Remove(entry.Id);
                // Clicking the anchor back off leaves nothing to extend from.
                if (_selectionAnchorId == entry.Id)
                    _selectionAnchorId = null;
            }
            else
            {
                Selection[entry.Id] = available;
                _selectionAnchorId = entry.Id;
            }
            RefreshCards();
            RefreshLabels();
        }

        private void OpenCardQuantityPopup(int index)
        {
            if (index < 0 || index >= _cards.Length)
                return;
            var id = _cards[index].ItemId;
            if (string.IsNullOrEmpty(id)
                || !Index.TryGetValue(id, out var entry)
                || !CanSelectEntry(entry))
            {
                return;
            }
            OpenQuantityPopup(entry, _cards[index].Root.transform.position);
        }

        /// <summary>
        /// Opens the game's own context menu with its real slider, replacing a
        /// hand-built modal whose -10/-1/+1/+10 buttons and text field were
        /// imitating exactly this widget. MouseWheelSupport already drives the
        /// vanilla slider, so wheel-to-adjust comes along for free.
        /// </summary>
        private void OpenQuantityPopup(CentralInventoryEntry entry,
            Vector3 worldPosition)
        {
            if (entry == null)
                return;
            var available = SelectableUnits(entry);
            if (available <= 0)
                return;

            CommonContextMenu menu;
            try
            {
                menu = UI.Get<CommonContextMenu>();
            }
            catch (Exception e)
            {
                Debug.LogError(Plugin.LogPrefix
                               + "context menu unavailable: " + e);
                return;
            }
            if (menu == null)
                return;

            // No ClearBinds() here on purpose: the menu clears itself in
            // OnDisable, and calling it after SetupCommand would wipe the
            // commands we just registered.
            DetachMenu();
            _menu = menu;
            _menuEntry = entry;
            _menuAvailable = available;

            var selected = GetSelectedUnits(entry.Id);

            // ContextMenuSliderButton disables itself when max == min, so a
            // single-unit stack gets the plain commands only.
            if (available > 1)
            {
                menu.AddSliderCommand(
                    Mathf.Clamp(selected > 0 ? selected : available,
                        1, available), 1, available);
                menu.SetupCommand(
                    Localization.Get("qmcentral.menu_select_amount"),
                    CmdSelectAmount);
            }
            menu.SetupCommand(string.Format(
                Localization.Get("qmcentral.menu_select_all"),
                available.ToString("N0", CultureInfo.InvariantCulture)),
                CmdSelectAll);
            if (selected > 0)
            {
                menu.SetupCommand(Localization.Get("qmcentral.menu_deselect"),
                    CmdDeselect);
            }

            menu.OnCmdSelected += OnMenuCmd;
            menu.OnHide += OnMenuHide;
            UI.Chain<CommonContextMenu>().Show().SetBackgroundOrder(-1)
                .SetBackOnBackgroundClick(true)
                .Invoke(v => v.SnapToPosition(worldPosition));
        }

        private void OnMenuCmd(int cmd)
        {
            if (cmd < CmdSelectAll || cmd > CmdDeselect)
                return;                 // one of vanilla's, or another mod's
            var entry = _menuEntry;
            if (entry == null)
                return;

            int units;
            if (cmd == CmdSelectAll)
                units = _menuAvailable;
            else if (cmd == CmdDeselect)
                units = 0;
            else
            {
                // The menu is already hidden by the time this fires -- UI.Hide
                // runs before OnCmdSelected -- but Slider.value survives
                // SetActive(false), which is what vanilla reads here too.
                units = _menu == null
                    ? 0
                    : Mathf.Clamp(_menu.SliderButtonSelectedValue,
                        0, _menuAvailable);
            }

            if (units <= 0)
                Selection.Remove(entry.Id);
            else
                Selection[entry.Id] = units;

            RefreshCards();
            RefreshLabels();
        }

        private void CloseItemMenu()
        {
            if (_menu != null && UI.IsShowing<CommonContextMenu>())
                UI.Hide<CommonContextMenu>();
            DetachMenu();
        }

        private void OnMenuHide()
        {
            // Deliberately does NOT unsubscribe. CommonContextMenu raises
            // OnHide from OnDisable, and OnDisable runs BEFORE OnCmdSelected --
            // detaching here would drop the very command just clicked. Clean up
            // on the next Update instead.
            _menuCleanupPending = true;
        }

        private void DetachMenu()
        {
            _menuCleanupPending = false;
            if (_menu != null)
            {
                _menu.OnCmdSelected -= OnMenuCmd;
                _menu.OnHide -= OnMenuHide;
            }
            _menu = null;
            _menuEntry = null;
            _menuAvailable = 0;
        }

        private void SelectFiltered()
        {
            var clearCurrent = AreAllFilteredSelected();
            foreach (var entry in _filtered)
            {
                if (!CanSelectEntry(entry))
                    continue;
                if (clearCurrent)
                    Selection.Remove(entry.Id);
                else
                    Selection[entry.Id] = SelectableUnits(entry);
            }
            _selectionAnchorId = null;
            RefreshCards();
            RefreshLabels();
        }

        private bool AreAllFilteredSelected()
        {
            var found = false;
            foreach (var entry in _filtered)
            {
                if (!CanSelectEntry(entry))
                    continue;
                found = true;
                var available = SelectableUnits(entry);
                if (available <= 0
                    || GetSelectedUnits(entry.Id) < available)
                {
                    return false;
                }
            }
            return found;
        }

        /// <summary>
        /// Resets every filter at once -- search text, category, detail tab and
        /// body slot -- because when nothing matches, any of the four could be
        /// the reason and hunting for it one control at a time is the problem.
        /// </summary>
        private void ClearAllFilters()
        {
            _category = CargoCategory.All;
            _detail = CargoDetail.Any;
            _implantSlotFilter = null;
            _scrollRow = 0;
            _search?.SetTextWithoutNotify(string.Empty);
            CloseAllDropdowns();
            ApplyResponsiveLayout();
            RefreshFiltered();
        }

        private bool HasActiveFilters()
        {
            return _category != CargoCategory.All
                   || _detail != CargoDetail.Any
                   || !string.IsNullOrEmpty(_implantSlotFilter)
                   || !string.IsNullOrEmpty(_search?.text);
        }

        private void ClearSelection()
        {
            Selection.Clear();
            _selectionAnchorId = null;
            RefreshCards();
            RefreshLabels();
        }

        private void QueueSelectedForRecycle()
        {
            if (_cargo == null || _cargo.RecyclingInProgress
                || _progression?.HasStoreConstructorDepartment != true)
            {
                RefreshRecycleButton();
                return;
            }
            if (CountSelectedRecyclableUnits() == 0)
            {
                RefreshRecycleButton();
                return;
            }
            RequestRecycleConfirmation(CountSelectedRecyclableUnits());
        }

        /// <summary>
        /// Confirmation used to be "click the same button twice within N
        /// seconds", announced only by the caption changing -- easy to trigger
        /// by accident on a queue that can hold hundreds of thousands of units,
        /// and with nothing to undo it.  This is the dialog the game itself
        /// uses for destructive choices.
        /// </summary>
        private void RequestRecycleConfirmation(int units)
        {
            try
            {
                // Never cache this view: ConfirmDialogWindow is declared
                // [UIView(GameLoopGroup.None, false, false)] with singleton
                // false, so UI destroys and re-creates it across game loop
                // groups.  Resolve it through the chain every time.
                UI.Chain<ConfirmDialogWindow>().Invoke(v =>
                    v.Configure(OnRecycleConfirmed,
                        "qmcentral.recycle_dialog",
                        focusOnApply: false,
                        arg0: units.ToString("N0", CultureInfo.InvariantCulture),
                        applyCaption: "qmcentral.recycle_apply",
                        cancelCaption: "ui.dialog.no_option")).Show();
            }
            catch (Exception e)
            {
                Debug.LogError(Plugin.LogPrefix
                               + "could not open the recycle confirmation: " + e);
            }
        }

        private void OnRecycleConfirmed(ConfirmDialogWindow.Option option)
        {
            if (option != ConfirmDialogWindow.Option.Yes)
                return;
            // Re-check: the dialog is modal but the panel behind it is live,
            // and recycling may have started in the meantime.
            if (_cargo == null || _cargo.RecyclingInProgress
                || _progression?.HasStoreConstructorDepartment != true
                || CountSelectedRecyclableUnits() == 0)
            {
                RefreshRecycleButton();
                return;
            }
            ExecuteRecycle();
        }

        private void ExecuteRecycle()
        {

            var before = CountAllCargoUnits();
            var movedUnits = MoveSelectedQuantitiesToRecycle();
            var after = CountAllCargoUnits();
            if (before != after)
            {
                Debug.LogError(Plugin.LogPrefix + "cargo unit count changed "
                               + before + " -> " + after
                               + " during batch recycle queueing.");
            }
            else
            {
                Debug.Log(Plugin.LogPrefix + "queued " + movedUnits
                          + " unit(s) for recycling.");
            }
            _selectedForRecycle.Clear();
            _selectionAnchorId = null;
            SingletonMonoBehaviour<SoundController>.Instance.PlayUiSound(
                SingletonMonoBehaviour<SoundsStorage>.Instance.TakeItem,
                isUnique: true);
            RebuildAndRefresh();
        }

        private int MoveSelectedQuantitiesToRecycle()
        {
            var moved = 0;
            foreach (var selection in _selectedForRecycle.ToArray())
            {
                if (!Index.TryGetValue(selection.Key, out var entry))
                    continue;
                var remaining = Mathf.Clamp(selection.Value, 0,
                    CountRecyclableUnits(entry));
                foreach (var item in entry.Stacks.ToArray())
                {
                    if (remaining <= 0)
                        break;
                    if (!IsSafeToRecycle(item) || item.Storage == null)
                        continue;

                    var itemUnits = Mathf.Max(1, item.StackCount);
                    if (itemUnits <= remaining)
                    {
                        MagnumCargoSystem.PutItemWithFallback(_cargo,
                            _cargo.RecyclingStorage, item);
                        remaining -= itemUnits;
                        moved += itemUnits;
                        continue;
                    }

                    var splitCount = Mathf.Min(remaining, short.MaxValue);
                    var split = SplitItemStack(item, (short)splitCount);
                    if (split == null)
                    {
                        Debug.LogError(Plugin.LogPrefix + "could not split "
                                       + splitCount + " unit(s) from " + item.Id
                                       + " for recycling.");
                        break;
                    }
                    MagnumCargoSystem.PutItemWithFallback(_cargo,
                        _cargo.RecyclingStorage, split);
                    remaining -= splitCount;
                    moved += splitCount;
                }
            }
            return moved;
        }

        private static BasePickupItem SplitItemStack(BasePickupItem source,
            short splitCount)
        {
            if (source == null || splitCount <= 0
                || splitCount >= source.StackCount)
            {
                return null;
            }
            var split = SingletonMonoBehaviour<ItemFactory>.Instance
                .CreateForInventory(source.Id);
            if (split == null)
                return null;
            try
            {
                source.StackCount -= splitCount;
                split.StackCount = splitCount;
                split.ExaminedItem = false;
                split.CopyExpireTime(source);
                ItemInteractionSystem.TrySplitUsable(source, split);
                return split;
            }
            catch
            {
                source.StackCount += splitCount;
                throw;
            }
        }

        private int CountSelectedRecyclableUnits()
        {
            var count = 0;
            foreach (var selection in _selectedForRecycle)
            {
                if (!Index.TryGetValue(selection.Key, out var entry))
                    continue;
                count += Mathf.Clamp(selection.Value, 0,
                    CountRecyclableUnits(entry));
            }
            return count;
        }

        private int GetSelectedUnits(string itemId)
        {
            return !string.IsNullOrEmpty(itemId)
                   && _selectedForRecycle.TryGetValue(itemId, out var units)
                ? units
                : 0;
        }

        private Dictionary<string, int> Selection => _selectedForRecycle;

        private static int SelectableUnits(CentralInventoryEntry entry)
        {
            return CountRecyclableUnits(entry);
        }

        private static bool CanSelectEntry(CentralInventoryEntry entry)
        {
            return entry != null && entry.HasRecyclable;
        }

        private static int CountRecyclableUnits(CentralInventoryEntry entry)
        {
            if (entry == null)
                return 0;
            var count = 0;
            foreach (var item in entry.Stacks)
            {
                if (IsSafeToRecycle(item))
                    count += Mathf.Max(1, item.StackCount);
            }
            return count;
        }

        private static bool IsSafeToRecycle(BasePickupItem item)
        {
            var cargo = Plugin.GameState?.Get<MagnumCargo>();
            return item != null && item.Storage != null && !item.Locked
                   && !item.IsImplicit
                   && item.Storage != cargo?.RecyclingStorage
                   && !ItemInteractionSystem.IsQuestItem(item)
                   && ItemInteractionSystem.CanDisassemble(item);
        }

        private int CountAllCargoUnits()
        {
            var count = 0;
            foreach (var storage in _cargo.ShipCargo)
                count += CountStorageUnits(storage);
            count += CountStorageUnits(_cargo.FridgeStorage);
            count += CountStorageUnits(_cargo.RecyclingStorage);
            return count;
        }

        private static int CountStorageUnits(ItemStorage storage)
        {
            if (storage == null)
                return 0;
            var count = 0;
            foreach (var item in storage.Items)
            {
                if (item != null)
                    count += Mathf.Max(1, item.StackCount);
            }
            return count;
        }

        /// <summary>Total rows the filtered list needs.</summary>
        private int TotalRows => _cardColumns <= 0
            ? 0
            : Mathf.CeilToInt(_filtered.Count / (float)_cardColumns);

        /// <summary>Highest first-visible-row that still fills the grid.</summary>
        private int MaxScrollRow => Mathf.Max(0, TotalRows - _cardRows);

        private void ScrollRows(int rows)
        {
            if (rows == 0)
                return;
            var next = Mathf.Clamp(_scrollRow + rows, 0, MaxScrollRow);
            if (next == _scrollRow)
                return;
            _scrollRow = next;
            RefreshCards();
            RefreshLabels();
        }

        /// <summary>The arrow buttons still move a whole screen at a time.</summary>
        private void ChangePage(int direction)
        {
            ScrollRows(direction * Mathf.Max(1, _cardRows));
        }

        private bool MatchesCategory(CentralInventoryEntry entry)
        {
            return MatchesCategory(entry, _category);
        }

        /// <summary>
        /// Which tab an entry belongs under. Static and category-parameterised
        /// so SPECIAL can be defined as the complement of the others rather
        /// than as a second, hand-maintained list of ItemClasses that had to be
        /// kept in sync with every case below.
        /// </summary>
        private static bool MatchesCategory(CentralInventoryEntry entry,
            CargoCategory category)
        {
            switch (category)
            {
                case CargoCategory.All:
                    return true;
                case CargoCategory.Weapons:
                    // Installable limbs are excluded even though the game gives
                    // them a WeaponRecord: you fit them in the augmentation
                    // station, you never equip them from the weapon list.
                    return !IsAugmentation(entry)
                           && (entry.ItemClass == ItemClass.Weapon
                               || entry.ItemClass == ItemClass.ThrowableWeapon);
                case CargoCategory.Equipment:
                    return entry.ItemClass == ItemClass.Helmet
                           || entry.ItemClass == ItemClass.Armor
                           || entry.ItemClass == ItemClass.Leggings
                           || entry.ItemClass == ItemClass.Boots
                           || entry.ItemClass == ItemClass.Backpack
                           || entry.ItemClass == ItemClass.Vest;
                case CargoCategory.Ammo:
                    return entry.ItemClass == ItemClass.Ammo;
                case CargoCategory.Ordnance:
                    // Everything you put ON the map rather than equip:
                    // thrown, buried, dropped as a turret, or planted as
                    // cover (PlaceableObstacle is the armoured shield and the
                    // energy barrier). Special used to hold the last of those,
                    // one tab away from the mines they get used with.
                    return entry.ItemClass == ItemClass.Grenade
                           || entry.ItemClass == ItemClass.Mine
                           || entry.ItemClass == ItemClass.Turret
                           || entry.ItemClass == ItemClass.PlaceableObstacle;
                case CargoCategory.Supplies:
                    return entry.ItemClass == ItemClass.Food
                           || entry.ItemClass == ItemClass.Drink
                           || entry.ItemClass == ItemClass.Alcohol
                           || entry.ItemClass == ItemClass.Pills
                           || entry.ItemClass == ItemClass.Syringe
                           || entry.ItemClass == ItemClass.Medpack
                           || entry.ItemClass == ItemClass.Dressing
                           || entry.ItemClass == ItemClass.RepairKit;
                case CargoCategory.Augments:
                    // Membership is decided by the AugmentationRecord, not by
                    // ItemClass. 24 mechanical limbs -- cyborg_hand, cyborg_leg,
                    // cyborg_drill, spider_claw, the possessed and Mars/Venus
                    // arms -- are CompositeItemRecords carrying BOTH a
                    // WeaponRecord and an AugmentationRecord, and
                    // GetRecord<ItemRecord>() returns whichever comes first in
                    // the list, which for these is the weapon. Classifying by
                    // ItemClass therefore filed every replacement limb under
                    // Weapons and left it out of the tab you install from.
                    return IsAugmentation(entry)
                           || entry.ItemClass == ItemClass.BioAug
                           || entry.ItemClass == ItemClass.CyberneticAug
                           || entry.ItemClass == ItemClass.QuasiAug;
                case CargoCategory.Materials:
                    return entry.ItemClass == ItemClass.Parts
                           || entry.ItemClass == ItemClass.Data
                           || entry.ItemClass == ItemClass.Blueprint
                           || entry.ItemClass == ItemClass.Organ;
                case CargoCategory.Barter:
                    return entry.ItemClass == ItemClass.MilitaryBarter
                           || entry.ItemClass == ItemClass.ScienceBarter
                           || entry.ItemClass == ItemClass.IndustrialBarter
                           || entry.ItemClass == ItemClass.ValuableBarter;
                case CargoCategory.Special:
                    // Everything no other tab claims. Derived, never listed:
                    // a hand-written ItemClass list here drifted out of sync
                    // with the cases above, and the only symptom was an item
                    // quietly appearing in no tab at all.
                    return !IsClaimedByAnyCategory(entry);
                default:
                    return true;
            }
        }

        /// <summary>The tabs that claim entries by content, in tab order.</summary>
        private static readonly CargoCategory[] ClaimingCategories =
        {
            CargoCategory.Weapons, CargoCategory.Equipment,
            CargoCategory.Ammo, CargoCategory.Ordnance,
            CargoCategory.Supplies,
            CargoCategory.Augments, CargoCategory.Materials,
            CargoCategory.Barter,
        };

        private static bool IsClaimedByAnyCategory(CentralInventoryEntry entry)
        {
            foreach (var category in ClaimingCategories)
            {
                if (MatchesCategory(entry, category))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// True for anything the augmentation station can fit, including the
        /// mechanical limbs whose ItemClass reports Weapon because their
        /// CompositeItemRecord lists a WeaponRecord ahead of the
        /// AugmentationRecord.
        /// </summary>
        private static bool IsAugmentation(CentralInventoryEntry entry)
        {
            return entry?.Augmentation != null;
        }

        /// <summary>
        /// The Bio / Cybernetic / Quasi class as the augmentation station sees
        /// it. Falls through to the plain ItemClass for everything that is not
        /// an augmentation, so the shared detail tabs keep their old meaning in
        /// other categories.
        /// </summary>
        private static ItemClass EffectiveClass(CentralInventoryEntry entry)
        {
            return entry?.Augmentation?.ItemClass ?? entry?.ItemClass
                ?? ItemClass.None;
        }

        private bool MatchesDetail(CentralInventoryEntry entry)
        {
            switch (_detail)
            {
                case CargoDetail.Any:
                    return true;
                case CargoDetail.Ranged:
                    return entry.Weapon != null && !entry.Weapon.IsMelee;
                case CargoDetail.Melee:
                    return entry.Weapon?.IsMelee == true;
                case CargoDetail.Pistol:
                    return entry.Weapon?.WeaponClass == WeaponClass.Pistol;
                case CargoDetail.Shotgun:
                    return entry.Weapon?.WeaponClass == WeaponClass.Shotgun;
                case CargoDetail.Smg:
                    return entry.Weapon?.WeaponClass == WeaponClass.Smg;
                case CargoDetail.Rifle:
                    return entry.Weapon != null
                           && (entry.Weapon.WeaponClass == WeaponClass.AssaultRifle
                               || entry.Weapon.WeaponClass == WeaponClass.MarksmanRifle);
                case CargoDetail.Heavy:
                    return entry.Weapon != null
                           && (int)entry.Weapon.WeaponClass >= (int)WeaponClass.Machinegun;
                case CargoDetail.Head:
                    return entry.ItemClass == ItemClass.Helmet;
                case CargoDetail.Body:
                    return entry.ItemClass == ItemClass.Armor;
                case CargoDetail.Legs:
                    return entry.ItemClass == ItemClass.Leggings;
                case CargoDetail.Boots:
                    return entry.ItemClass == ItemClass.Boots;
                case CargoDetail.Backpack:
                    return entry.ItemClass == ItemClass.Backpack;
                case CargoDetail.Vest:
                    return entry.ItemClass == ItemClass.Vest;
                case CargoDetail.Grenades:
                    return entry.ItemClass == ItemClass.Grenade;
                case CargoDetail.Mines:
                    return entry.ItemClass == ItemClass.Mine;
                case CargoDetail.Turrets:
                    return entry.ItemClass == ItemClass.Turret;
                case CargoDetail.Food:
                    return entry.ItemClass == ItemClass.Food;
                case CargoDetail.Drinks:
                    return entry.ItemClass == ItemClass.Drink
                           || entry.ItemClass == ItemClass.Alcohol;
                case CargoDetail.Medicine:
                    return entry.ItemClass == ItemClass.Pills
                           || entry.ItemClass == ItemClass.Syringe
                           || entry.ItemClass == ItemClass.Medpack
                           || entry.ItemClass == ItemClass.Dressing;
                case CargoDetail.Repair:
                    return entry.ItemClass == ItemClass.RepairKit;
                case CargoDetail.AugLimb:
                    // Every #augmentations row replaces a body part, including
                    // the weapon-arms (cyborg_drill, cyborg_recreation_blade,
                    // venus_warrior_hand ...). Splitting these by
                    // AugmentationClass put those arms in a separate "armed"
                    // tab and left them out of the limb list they belong in.
                    return entry?.Augmentation != null;
                case CargoDetail.AugImplant:
                    return entry?.Implant != null;
                case CargoDetail.Bio:
                    return EffectiveClass(entry) == ItemClass.BioAug;
                case CargoDetail.Cybernetic:
                    return EffectiveClass(entry) == ItemClass.CyberneticAug;
                case CargoDetail.Quasi:
                    return EffectiveClass(entry) == ItemClass.QuasiAug
                           || entry.ItemClass == ItemClass.QuasiArtefact
                           || entry.ItemClass == ItemClass.QuasiPact
                           || entry.ItemClass == ItemClass.QuasiOrgan;
                case CargoDetail.Parts:
                    return entry.ItemClass == ItemClass.Parts;
                case CargoDetail.Data:
                    return entry.ItemClass == ItemClass.Data;
                case CargoDetail.Blueprints:
                    return entry.ItemClass == ItemClass.Blueprint;
                case CargoDetail.Organs:
                    return entry.ItemClass == ItemClass.Organ;
                case CargoDetail.Military:
                    return entry.ItemClass == ItemClass.MilitaryBarter;
                case CargoDetail.Science:
                    return entry.ItemClass == ItemClass.ScienceBarter;
                case CargoDetail.Industrial:
                    return entry.ItemClass == ItemClass.IndustrialBarter;
                case CargoDetail.Valuable:
                    return entry.ItemClass == ItemClass.ValuableBarter;
                case CargoDetail.Quest:
                    return entry.ItemClass == ItemClass.QuestItem;
                case CargoDetail.Keys:
                    return entry.ItemClass == ItemClass.Key;
                case CargoDetail.Deployables:
                    return entry.ItemClass == ItemClass.PlaceableObstacle;
                case CargoDetail.Cyborgs:
                    return entry.ItemClass == ItemClass.Cyborg;
                case CargoDetail.Other:
                    return entry.ItemClass == ItemClass.None;
                default:
                    return true;
            }
        }

        private bool MatchesImplantSlot(CentralInventoryEntry entry)
        {
            if (string.IsNullOrEmpty(_implantSlotFilter))
                return true;
            // Never let a filter whose control is hidden quietly empty the
            // list -- it would look like the catalogue had simply lost items.
            if (!ShouldShowImplantSlotFilter())
                return true;
            foreach (var slot in EnumerateImplantSlots(entry))
            {
                if (string.Equals(slot, _implantSlotFilter,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private bool ShouldShowImplantSlotFilter()
        {
            // Available for every Augments sub-filter: picking a body slot is
            // most useful precisely while browsing limbs, which the new role
            // tabs are for.
            return _category == CargoCategory.Augments;
        }

        private static CargoDetail[] GetDetails(CargoCategory category)
        {
            switch (category)
            {
                case CargoCategory.Weapons:
                    return new[] { CargoDetail.Any, CargoDetail.Ranged,
                        CargoDetail.Melee, CargoDetail.Pistol,
                        CargoDetail.Shotgun, CargoDetail.Smg,
                        CargoDetail.Rifle, CargoDetail.Heavy };
                case CargoCategory.Equipment:
                    return new[] { CargoDetail.Any, CargoDetail.Head,
                        CargoDetail.Body, CargoDetail.Legs,
                        CargoDetail.Boots, CargoDetail.Backpack,
                        CargoDetail.Vest };
                // Ammo holds one item class, so it needs no split of its own.
                case CargoCategory.Ordnance:
                    return new[] { CargoDetail.Any, CargoDetail.Grenades,
                        CargoDetail.Mines, CargoDetail.Turrets,
                        CargoDetail.Deployables };
                case CargoCategory.Supplies:
                    return new[] { CargoDetail.Any, CargoDetail.Food,
                        CargoDetail.Drinks, CargoDetail.Medicine,
                        CargoDetail.Repair };
                case CargoCategory.Augments:
                    return new[] { CargoDetail.Any,
                        CargoDetail.AugLimb, CargoDetail.AugImplant };
                case CargoCategory.Materials:
                    return new[] { CargoDetail.Any, CargoDetail.Parts,
                        CargoDetail.Data, CargoDetail.Blueprints,
                        CargoDetail.Organs };
                case CargoCategory.Barter:
                    return new[] { CargoDetail.Any, CargoDetail.Military,
                        CargoDetail.Science, CargoDetail.Industrial,
                        CargoDetail.Valuable };
                case CargoCategory.Special:
                    return new[] { CargoDetail.Any, CargoDetail.Quasi,
                        CargoDetail.Quest, CargoDetail.Keys,
                        CargoDetail.Cyborgs, CargoDetail.Other };
                default:
                    return new[] { CargoDetail.Any };
            }
        }


        private static string GetOperatorDisplayName(Mercenary mercenary)
        {
            if (mercenary == null)
                return "—";
            var profile = Data.MercenaryProfiles.GetRecord(mercenary.ProfileId);
            var profileId = profile == null ? mercenary.ProfileId : profile.Id;
            // Keep this identical to MercenaryPanel.Initialize(), which backs
            // the vanilla "Manage Operators" list. "monster.*" is the profile
            // title (Mother Wolf, Paladin, etc.); "spec.*" is the person's name.
            var key = "spec." + profileId + ".name";
            var localized = Localization.Get(key);
            if (!string.IsNullOrEmpty(localized) && localized != key)
                return localized;
            if (!string.IsNullOrEmpty(mercenary.ProfileId))
                return mercenary.ProfileId;
            return string.IsNullOrEmpty(mercenary.AgentName)
                ? "—"
                : mercenary.AgentName;
        }

        private void ReleaseItemSlots()
        {
            foreach (var card in _cards)
            {
                if (card?.ItemSlot == null)
                    continue;
                var slot = card.ItemSlot;
                card.ItemSlot = null;
                try
                {
                    slot.ConfigureEmptyHintTag(string.Empty);
                    slot.ConfigureDefaultSize(24f, 24f);
                    slot.CellPos = CellPosition.Zero;
                    var rect = (RectTransform)slot.transform;
                    // Exact root layout from the game's ItemSlot.prefab. This is
                    // deliberately restored before returning anything to UI.Pools.
                    rect.anchorMin = new Vector2(0f, 0.5f);
                    rect.anchorMax = new Vector2(0f, 0.5f);
                    rect.pivot = new Vector2(0.5f, 0.5f);
                    rect.anchoredPosition = Vector2.zero;
                    rect.sizeDelta = new Vector2(24f, 24f);
                    rect.localScale = Vector3.one;
                    rect.localRotation = Quaternion.identity;
                    slot.CachedLocalPosition = rect.localPosition;
                    slot.gameObject.name = "ItemSlot";
                    UI.Pools.ItemSlots.Put(slot.gameObject, setParent: true);
                }
                catch (NullReferenceException)
                {
                    // During application shutdown Unity can destroy UI.Pools
                    // before pooled screens. The slot reference is already
                    // detached, so no live vanilla UI state remains polluted.
                }
            }
        }

        // The old ToggleOperatorPanel lived here. It is SelectPane in
        // CentralManagementPanel.Shuttle.cs now: one control, four
        // destinations, each named before it is chosen.

        // --- measured layout ------------------------------------------------
        // MeasurePanelLayout writes these from the real design surface; every
        // Layout* method below reads them instead of recomputing. Splitting
        // measurement from placement is what lets a section be read on its own
        // -- this used to be one 226-line method.
        private float _layoutPanelH;
        private float _layoutGridTop;
        private float _layoutGridBottom;
        private float _layoutFooterY;

        // Header cluster, right to left from the header's own inner edge.
        private const float CloseW = 40f;
        // Wider than the old button: it is a dropdown now, so the caret well
        // eats 17px and the captions are pane names rather than verbs. Kept
        // as tight as the longest caption allows -- in the narrow layout the
        // panel title lives on whatever this cluster leaves behind.
        private const float GearW = 74f;
        private const float OperatorW = 79f;
        private const float ArrowW = 13f;
        private const float HeaderInset = 2f;
        private const float HeaderControlH = 15f;
        private const float TitleX = 19f;

        // Search row.
        private const float SearchRowY = -25f;
        private const float SortWideW = 96f;
        private const float SortNarrowW = 66f;
        private const float DropdownWideW = 120f;
        private const float DropdownNarrowW = 96f;
        private const float DropdownH = 20f;

        private const float CategoryRowY = -44f;
        private const float ClearFiltersW = 108f;

        // Footer action cluster.
        private const float SelectW = 62f;
        private const float ClearW = 40f;
        private const float RecycleW = 64f;
        // Verb only, no count. A count in the caption would widen the
        // button as the hold changes and shove the whole right-anchored
        // cluster around -- and at 282 wide the cluster already uses the
        // entire row, so it has nowhere to be shoved. The number lives in the
        // confirmation, which is where it has to be read anyway.
        private const float RepairW = 44f;
        private const float PageArrowWideW = 15f;
        private const float PageArrowNarrowW = 13f;
        private const float PageLabelWideW = 34f;
        private const float PageLabelNarrowW = 26f;
        // Below this the "{0} TYPES / {1} UNITS" sentence does not fit and the
        // readout drops to the bare "21 / 4,096" form.
        private const float CountCompactW = 96f;
        // Below this even "21 / 4,096" is clipped, so the readout is hidden
        // instead of shown as a fragment.
        private const float CountMinimumW = 34f;

        private void ApplyResponsiveLayout()
        {
            if (_root == null || _headerRoot == null)
                return;

            MeasurePanelLayout();
            LayoutHeader();
            LayoutFilterRow();
            LayoutFilterTabs();
            LayoutCardGrid();
            LayoutEmptyState();
            LayoutFooter();
            RefreshOperatorPanelButton();
            ApplyPresetLayout();
        }

        /// <summary>
        /// Measure, never assume: MGSC.UIScaleFixer swaps the CanvasScaler by
        /// aspect ratio, so the design space is 640x360 at 16:9, 640x480 at
        /// 4:3, ~853x360 at 21:9 and 1280x360 at 32:9. Height is what varies
        /// below 2.3:1 (that is the case this clamp is for); above it, width is
        /// what grows -- which is why this panel is centre-anchored and not
        /// pinned to a corner. See PanelUi.DesignSurfaceSize for the table.
        /// </summary>
        private void MeasurePanelLayout()
        {
            var canvasHeight = _root.transform.parent is RectTransform parentRect
                ? parentRect.rect.height
                : 360f;
            _layoutPanelH = Mathf.Floor(
                Mathf.Clamp(canvasHeight - 16f, 180f, 344f));
            _layoutFooterY = -(_layoutPanelH - PanelPad - RowH);

            var panelRect = (RectTransform)_root.transform;
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0f, 0.5f);
            panelRect.anchoredPosition = _operatorPanelVisible
                ? new Vector2(38f, 0f)
                : new Vector2(-312f, 0f);
            panelRect.sizeDelta = new Vector2(CurrentPanelWidth, _layoutPanelH);
        }

        /// <summary>
        /// The header cluster is laid out RIGHT TO LEFT from the header's own
        /// inner edge. It used to start at panelWidth - 189 while these
        /// controls are children of the header, whose width is only innerWidth
        /// (panelWidth - 8), so CLOSE ended 6px past the frame. Measuring back
        /// from the parent's edge cannot overflow.
        /// </summary>
        private void LayoutHeader()
        {
            var innerWidth = CurrentInnerWidth;
            PanelUi.SetTopLeft((RectTransform)_headerRoot.transform,
                PanelPad, -PanelPad, innerWidth, HeaderH);
            PanelUi.SetTopLeft((RectTransform)_headerIconRoot.transform,
                2f, -2f, 15f, 15f);

            _previousOperatorRoot.SetActive(false);
            _nextOperatorRoot.SetActive(false);

            var headerX = innerWidth - HeaderInset - CloseW;
            PanelUi.SetTopLeft((RectTransform)_closeRoot.transform,
                headerX, -2f, CloseW, HeaderControlH);
            headerX -= Gap + GearW;
            PanelUi.SetTopLeft((RectTransform)_operatorPanelRoot.transform,
                headerX, -2f, GearW, HeaderControlH);
            // The list follows the trigger, but lives on the PANEL root so
            // SetAsLastSibling can lift it above the card grid -- last child
            // of the header would still draw underneath it. Hence the header's
            // own offset folded into these panel-space coordinates.
            _paneDropdownX = PanelPad + headerX;
            _paneDropdownY = -PanelPad - 2f - HeaderControlH - 1f;
            headerX -= Gap + OperatorW;
            PanelUi.SetTopLeft((RectTransform)_operatorRoot.transform,
                headerX, -2f, OperatorW, HeaderControlH);
            PanelUi.SetTopLeft((RectTransform)_previousOperatorRoot.transform,
                headerX, -2f, ArrowW, HeaderControlH);
            PanelUi.SetTopLeft((RectTransform)_nextOperatorRoot.transform,
                headerX + OperatorW - ArrowW, -2f, ArrowW, HeaderControlH);

            // The title takes whatever is left, so it can never run under the
            // cluster no matter how the panel is sized.
            PanelUi.SetTopLeft(_title.rectTransform, TitleX, 0f,
                Mathf.Max(0f, headerX - Gap - TitleX), HeaderH);
        }

        /// <summary>
        /// Search field plus the SLOT and SORT triggers. Their widths are
        /// FIXED: the old layout sized them from the current label text, so the
        /// search field jumped under the cursor whenever the selection changed.
        /// </summary>
        private void LayoutFilterRow()
        {
            var side = _operatorPanelVisible;
            var innerWidth = CurrentInnerWidth;
            var showSlotFilter = ShouldShowImplantSlotFilter();
            var sortWidth = side ? SortNarrowW : SortWideW;
            var slotWidth = showSlotFilter ? sortWidth : 0f;
            var searchWidth = innerWidth - sortWidth - Gap
                              - (showSlotFilter ? slotWidth + Gap : 0f);

            PanelUi.SetTopLeft((RectTransform)_searchRoot.transform,
                PanelPad, SearchRowY, searchWidth, RowH);
            var auxiliaryX = PanelPad + searchWidth + Gap;
            _slotFilterRoot.SetActive(showSlotFilter);
            if (showSlotFilter)
            {
                PanelUi.SetTopLeft((RectTransform)_slotFilterRoot.transform,
                    auxiliaryX, SearchRowY, slotWidth, RowH);
                auxiliaryX += slotWidth + Gap;
            }
            else
            {
                // The control just vanished; its list must not stay open.
                CloseDropdown(_slotFilterDropdownRoot);
            }
            PanelUi.SetTopLeft((RectTransform)_sortRoot.transform,
                auxiliaryX, SearchRowY, sortWidth, RowH);

            var dropdownWidth = side ? DropdownNarrowW : DropdownWideW;
            var dropdownY = SearchRowY - RowH - Gap;
            PanelUi.SetTopLeft((RectTransform)_slotFilterDropdownRoot.transform,
                showSlotFilter ? PanelPad + searchWidth + Gap : auxiliaryX,
                dropdownY, dropdownWidth, DropdownH);
            PanelUi.SetTopLeft((RectTransform)_sortDropdownRoot.transform,
                CurrentPanelWidth - PanelPad - dropdownWidth,
                dropdownY, dropdownWidth, DropdownH);
        }

        /// <summary>
        /// Ten categories fit one flush row when the panel is wide; the narrow
        /// layout splits 5 + 5 and stretches BOTH rows edge to edge, so neither
        /// ends in the empty cell the old 5-column grid left behind.
        /// </summary>
        private void LayoutFilterTabs()
        {
            var innerWidth = CurrentInnerWidth;
            var categoryBottom = _operatorPanelVisible
                ? LayoutTabRows(_categoryTabs, CategoryRowY, new[] { 5, 5 },
                    innerWidth)
                : LayoutTabRows(_categoryTabs, CategoryRowY, new[] { 10 },
                    innerWidth);
            _detailRowY = categoryBottom - Gap;
            LayoutDetailTabs(GetDetails(_category));
        }

        private void LayoutCardGrid()
        {
            var innerWidth = CurrentInnerWidth;
            _layoutGridTop = _detailRowY - TabH - Gap;
            _layoutGridBottom = _layoutFooterY + Gap;

            _cardColumns = _operatorPanelVisible ? 2 : 4;
            _cardRows = Mathf.Clamp(
                Mathf.FloorToInt(
                    (_layoutGridTop - _layoutGridBottom + CardGap)
                    / (CardH + CardGap)),
                1, MaxCardsPerPage / _cardColumns);

            // Integer cell width, with the rounding remainder handed to the
            // last column so the grid still lands flush on both edges.
            // Fractional widths would put every other card on a half pixel.
            var cardWidth = Mathf.Floor(
                (innerWidth - CardGap * (_cardColumns - 1)) / _cardColumns);
            var lastCardWidth =
                innerWidth - (cardWidth + CardGap) * (_cardColumns - 1);

            if (_cardWellRoot != null)
            {
                PanelUi.SetTopLeft((RectTransform)_cardWellRoot.transform,
                    PanelPad - 2f, _layoutGridTop + 2f, innerWidth + 4f,
                    Mathf.Max(0f, _layoutGridTop - _layoutGridBottom + 4f));
            }

            for (var i = 0; i < _cards.Length; i++)
            {
                var card = _cards[i];
                var column = i % _cardColumns;
                var row = i / _cardColumns;
                var width = column == _cardColumns - 1
                    ? lastCardWidth
                    : cardWidth;

                PanelUi.SetTopLeft((RectTransform)card.Root.transform,
                    PanelPad + column * (cardWidth + CardGap),
                    _layoutGridTop - row * (CardH + CardGap), width, CardH);
                PanelUi.SetTopLeft((RectTransform)card.SlotHost,
                    3f, -4f, SlotColumnW, SlotModule);
                // The name gets the whole line minus the icon column; the count
                // and the chip share the line below it.
                PanelUi.SetTopLeft(card.Name.rectTransform,
                    NameX, -3f, width - NameX - 3f, 14f);
                PanelUi.SetTopLeft(card.Quantity.rectTransform, NameX, -17f,
                    Mathf.Max(20f, width - NameX - ChipW - 5f), 12f);
                PanelUi.SetTopLeft((RectTransform)card.SelectRoot.transform,
                    width - ChipW - 3f, -17f, ChipW, TabH);
                card.SelectLabel.fontSize = 7f;
            }
        }

        /// <summary>
        /// Centres the "nothing matches" message and its way out on the
        /// MEASURED grid, instead of at a magic offset that only lined up at
        /// one panel height.
        /// </summary>
        private void LayoutEmptyState()
        {
            var innerWidth = CurrentInnerWidth;
            var center = _layoutGridTop
                         - (_layoutGridTop - _layoutGridBottom) * 0.5f;
            if (_clearFiltersRoot != null)
            {
                PanelUi.SetTopLeft((RectTransform)_clearFiltersRoot.transform,
                    Mathf.Floor(PanelPad + (innerWidth - ClearFiltersW) * 0.5f),
                    center - 4f, ClearFiltersW, RowH);
            }
            PanelUi.SetTopLeft(_empty.rectTransform, PanelPad, center + 20f,
                innerWidth, 40f);
        }

        /// <summary>
        /// Right-aligned action cluster, sized from its own contents so it
        /// stays put when the panel width changes. The page controls hold short
        /// text, so tightening them in the narrow layout costs nothing and buys
        /// the status readout back the room it needs.
        /// </summary>
        private void LayoutFooter()
        {
            var side = _operatorPanelVisible;
            var pageArrowW = side ? PageArrowNarrowW : PageArrowWideW;
            var pageLabelW = side ? PageLabelNarrowW : PageLabelWideW;
            var clusterW = SelectW + Gap + ClearW + Gap + pageArrowW + Gap
                           + pageLabelW + Gap + pageArrowW + Gap + RepairW
                           + Gap + RecycleW;
            var footerX = Mathf.Floor(
                CurrentPanelWidth - PanelPad - clusterW);

            // No minimum width here. A Mathf.Max floor used to force the
            // readout wider than the space actually left, so in the narrow
            // layout it ran straight under SELECT FILTER. Whatever is left is
            // what it gets -- and if that is too little for the full sentence,
            // the text drops to a compact form rather than being clipped.
            var countWidth = Mathf.Max(0f, footerX - PanelPad - Gap);
            _countCompact = countWidth < CountCompactW;
            // Third rung of the same ladder: full sentence, then the bare
            // "types / units" pair, then nothing at all. In the side-by-side
            // layout the action cluster now uses the whole row, and an action
            // the player asked for outranks a readout they can get back by
            // collapsing the agent pane.
            _count.gameObject.SetActive(countWidth >= CountMinimumW);
            PanelUi.SetTopLeft(_count.rectTransform, PanelPad, _layoutFooterY,
                countWidth, RowH);

            var x = footerX;
            PanelUi.SetTopLeft((RectTransform)_selectFilteredRoot.transform,
                x, _layoutFooterY, SelectW, RowH);
            x += SelectW + Gap;
            PanelUi.SetTopLeft((RectTransform)_clearRoot.transform,
                x, _layoutFooterY, ClearW, RowH);
            x += ClearW + Gap;
            PanelUi.SetTopLeft((RectTransform)_previousPageRoot.transform,
                x, _layoutFooterY, pageArrowW, RowH);
            x += pageArrowW + Gap;
            PanelUi.SetTopLeft(_page.rectTransform,
                x, _layoutFooterY, pageLabelW, RowH);
            x += pageLabelW + Gap;
            PanelUi.SetTopLeft((RectTransform)_nextPageRoot.transform,
                x, _layoutFooterY, pageArrowW, RowH);
            x += pageArrowW + Gap;
            PanelUi.SetTopLeft((RectTransform)_repairRoot.transform,
                x, _layoutFooterY, RepairW, RowH);
            x += RepairW + Gap;
            PanelUi.SetTopLeft((RectTransform)_recycleRoot.transform,
                x, _layoutFooterY, RecycleW, RowH);
        }

        /// <summary>
        /// The pane selector's caption names what is on screen right now; the
        /// list it opens names where else you can go.
        /// </summary>
        private void RefreshOperatorPanelButton()
        {
            if (_operatorPanelLabel == null)
                return;
            _operatorPanelLabel.text = PaneLabel(CurrentPane);
            _operatorPanelLabel.font = Localization.GetActualFont();
            // Always usable: even with no operator the list still offers the
            // shuttle hold and hiding the pane.
            _operatorPanelButton?.SetInteractable(true);
        }

        private void ApplyFont()
        {
            if (_search == null)
                return;
            var font = Localization.GetActualFont();
            _search.SetGlobalFontAsset(font);
            _searchText.font = font;
            _searchPlaceholder.font = font;
            ApplyPresetFonts();
        }

        private void ClosePanel()
        {
            CloseItemMenu();
            ClosePresetPopup();
            CloseAllDropdowns();
            UI.Back(isBackButtonClicked: true);
        }

        private void Update()
        {
            if (!_centralMode)
                return;
            if (_refreshQueued && !UI.Drag.IsDragging)
            {
                _refreshQueued = false;
                RebuildAndRefresh();
            }
            if (_search != null && _search.isFocused)
                Input.imeCompositionMode = IMECompositionMode.On;
            if (_menuCleanupPending)
                DetachMenu();
            if (UI.IsShowing<CommonContextMenu>())
            {
                UI.Drag.Pause(0.08f);
                return;
            }
            if (IsRepairSummaryOpen)
            {
                // Report-only modal: it owns the pointer and any dismiss key
                // while it is up, and confirms nothing.
                UI.Drag.Pause(0.08f);
                _repairSummary.HandleKeys();
                return;
            }
            if (IsPresetPopupOpen)
            {
                UI.Drag.Pause(0.08f);
                if (Input.GetKeyDown(KeyCode.Escape))
                    ClosePresetPopup();
                else if (Input.GetKeyDown(KeyCode.Return)
                         || Input.GetKeyDown(KeyCode.KeypadEnter))
                    ConfirmPresetPopup();
                return;
            }
            if (HasOpenDropdown())
            {
                // Keep the raw drag controller paused for the entire lifetime
                // of the popup.  This is deliberately done before the Unity
                // EventSystem dispatches Button.onClick, so the release that
                // chooses a row is already blocked before the popup closes.
                UI.Drag.Pause(0.08f);
            }
            HandlePageWheel();
        }

        private void HandlePageWheel()
        {
            if (_root == null || UI.Drag.IsDragging
                || UI.IsShowing<CommonContextMenu>()
                || HasOpenDropdown())
            {
                return;
            }
            var scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) <= Mathf.Epsilon)
                return;
            if (!RectTransformUtility.RectangleContainsScreenPoint(
                    (RectTransform)_root.transform, Input.mousePosition,
                    null))
            {
                return;
            }
            // One notch moves a full screen, same as the arrow buttons. Fine
            // row-by-row scrolling just made the grid feel loose to read.
            var rows = Mathf.Max(1, _cardRows);
            ScrollRows(scroll < 0f ? rows : -rows);
        }

        private void OnDestroy()
        {
            DiscardPresetUi();
            ReleaseItemSlots();
            if (ReferenceEquals(_instance, this))
                _instance = null;
        }
    }
}
