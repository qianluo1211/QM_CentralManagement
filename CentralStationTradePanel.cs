using System;
using System.Collections.Generic;
using MGSC;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace QM_CentralManagement
{
    /// <summary>
    /// The wheel's only job is paging the list. Attached to every leaf
    /// interactive object, because EventTrigger / TMP_InputField stop the
    /// scroll event chain and the page handler must sit on the same object
    /// to receive it.
    /// </summary>
    internal sealed class TradePageWheel : MonoBehaviour, IScrollHandler
    {
        internal CentralStationTradePanel Panel;

        public void OnScroll(PointerEventData eventData)
        {
            if (Panel == null
                || Mathf.Abs(eventData.scrollDelta.y) <= Mathf.Epsilon)
            {
                return;
            }
            Panel.ScrollRows(eventData.scrollDelta.y > 0f ? -1 : 1);
            eventData.Use();
        }
    }

    /// <summary>
    /// The mod's station trade panel: a full-screen workbench replacing the
    /// vanilla station pages inside FastTradeScreen.
    ///
    /// The list is a FIXED set of row slots that pages through the index,
    /// exactly like CentralManagementPanel's card grid -- every search
    /// keystroke only updates 13 labels instead of rebuilding hundreds of
    /// GameObjects, which is what makes the panel open and filter instantly.
    ///
    /// Geometry is measured, not hardcoded. The panel is CENTRED on the
    /// design surface and fitted to it by MeasureLayout / ApplyLayout: it
    /// takes the available width up to MaxPanelW and fits a whole number of
    /// list rows into the available height. This matters because the game
    /// changes the canvas scaler with the aspect ratio, so the design space
    /// is 640x360 at 16:9 but ~853x360 at 21:9 and 640x480 at 4:3 (see
    /// PanelUi.DesignSurfaceSize). A 640x360 surface reproduces the original
    /// fixed 636x356 layout down to the pixel:
    ///   content area   2..634
    ///   list well      4..632, slots 6..630 (rows 624 wide, 4px inner pad)
    ///   footer         4..632, buttons right-aligned to the footer edge
    ///   filter row     search 4..534, sort 538..632
    ///   header         title 6..156, station 158.., points and close
    ///                  right-aligned to the header edge
    /// Widgets that must reach the right edge are placed by their distance
    /// FROM that edge, so widening the panel moves them as one cluster and
    /// the slack lands in the name column and the running-total text.
    /// </summary>
    internal sealed class CentralStationTradePanel : MonoBehaviour,
        IScreenPanel
    {
        bool IScreenPanel.OwnsScreen => IsPanelActive;

        void IScreenPanel.OnScreenEnabled()
        {
            // Vanilla's OnEnable brings its own chrome back; this panel
            // stands in front of all of it.
            HideVanillaUi();
            ShowPanel();
        }

        void IScreenPanel.OnScreenDisabled()
        {
            OnScreenClosed();
        }

        private const float RowH = 20f;
        private const float HeaderRowH = 14f;
        private const float SlotsTop = -18f;
        private const float ListY = -70f;

        // --- responsive envelope -------------------------------------------
        // The design space is NOT a fixed 640x360: UIScaleFixer swaps the
        // canvas scaler by aspect ratio, so an ultrawide screen makes it
        // ~853x360 (21:9) or 1280x360 (32:9).  See PanelUi.DesignSurfaceSize.
        // Everything here is a design target that ApplyResponsiveLayout fits
        // to whatever the surface actually measures.
        private const float DesignPanelW = 636f;
        private const float DesignPanelH = 356f;
        // A panel wider than a 21:9 canvas gives it does not read as a list
        // any more -- the +/- buttons drift too far from the item name.  At
        // 32:9 the surplus becomes margin instead.
        private const float MaxPanelW = 860f;
        private const float PanelMargin = 2f;
        // Panel height that is not list rows: 70 above the well, the well's
        // column-header strip (18) and bottom pad (2), the footer gap (2)
        // and footer (17), and the panel's own bottom pad (3).
        private const float FixedPanelH = 112f;
        private const float FooterH = 17f;
        private const float FooterGap = 2f;
        private const float WellPadTop = 18f;
        private const float WellPadBottom = 2f;
        private const int MinRowSlots = 3;
        // Row slots are built once and shown or hidden by the layout pass,
        // so a resolution change never has to rebuild the panel.  20 covers
        // the tallest design space any supported aspect produces (5:4 -> 19).
        private const int MaxRowSlots = 20;

        private enum Pane
        {
            Buy,
            Sell
        }

        private enum TradeCategory
        {
            All,
            Weapons,
            Armor,
            Ammo,
            Medical,
            Food,
            Materials,
            Other
        }

        private enum TradeSort
        {
            Name,
            PriceAscending,
            PriceDescending,
            QuantityDescending
        }

        private sealed class TradeEntry
        {
            public string ItemId;
            public int Available;
            public int UnitPrice;
            public bool CanTrade;
            public int MaxStack = 1;
        }

        private sealed class TradeRow
        {
            public GameObject Root;
            public Image Icon;
            public TextMeshProUGUI Name;
            public TextMeshProUGUI Stock;
            public TextMeshProUGUI Price;
            public TextMeshProUGUI Subtotal;
            public TextMeshProUGUI Badge;
            public GameObject MinusRoot;
            public GameObject PlusRoot;
            public GameObject MaxRoot;
            public GameObject Catcher;
            public GameObject QtyRoot;
            public GameObject InputRoot;
            public TMP_InputField Input;
            public TextMeshProUGUI InputText;
            public CommonButton Minus;
            public CommonButton Plus;
            public CommonButton Max;
            public string ItemId;
            public TradeEntry Entry;
        }

        private sealed class FilterTab
        {
            public GameObject Root;
            public Image Background;
            public TextMeshProUGUI Label;
            public TradeCategory Category;
        }

        private static CentralStationTradePanel _instance;

        /// <summary>
        /// Tells SearchInputIsolation whether a text field of this panel owns
        /// the keyboard right now. Mirrors CentralManagementPanel's gate so
        /// typing in a quantity box never triggers game hotkeys.
        /// </summary>
        internal static bool CapturesInput =>
            _instance != null
            && _instance.IsPanelActive
            && _instance._root != null
            && _instance._root.activeInHierarchy
            && (_instance.AnyInputFocused
                || _instance.HasOpenDropdown()
                || UI.IsShowing<ConfirmDialogWindow>()
                );

        internal bool IsPanelActive { get; private set; }

        // Services, passed in by the hooks.
        private FastTradeScreen _screen;
        private Station _station;
        private Factions _factions;
        private ItemsPrices _itemsPrices;
        private Statistics _statistics;
        private Difficulty _difficulty;
        private MagnumCargo _cargo;
        private SpaceTime _spaceTime;
        private MagnumProgression _progression;
        private StoryTriggers _storyTriggers;
        private Faction _faction;
        private ItemStorage _activeCargo;
        private readonly List<ItemStorage> _sellStorages = new List<ItemStorage>();

        // Row column layout. The number columns are fixed width and packed
        // against the row's right edge, stopping 4px short of it so nothing
        // ever touches the row border or the well; the name column takes
        // whatever is left, which is how a wider panel pays off.
        private const float NameX = 24f;
        private const float StockW = 68f;
        private const float PriceW = 78f;
        private const float QuantityW = 148f;
        private const float SubtotalW = 118f;

        // --- measured layout ------------------------------------------------
        // MeasureLayout writes these from the real design-space size; the
        // Build* methods read them, and ApplyLayout re-writes every rect
        // when the surface changes.  Defaults describe the 636x356 design.
        private float _panelW = DesignPanelW;
        private float _panelH = DesignPanelH;
        private float _rowW = DesignPanelW - 12f;
        private float _listH = 260f;
        private float _footerY = -332f;
        private int _visibleRows = 12;
        private float _subtotalX = DesignPanelW - 12f - 4f - SubtotalW;
        private float _quantityX =
            DesignPanelW - 12f - 8f - SubtotalW - QuantityW;
        private float _priceX =
            DesignPanelW - 12f - 12f - SubtotalW - QuantityW - PriceW;
        private float _stockX = DesignPanelW - 12f - 16f - SubtotalW
                                - QuantityW - PriceW - StockW;
        private float _nameW = DesignPanelW - 12f - 16f - SubtotalW
                               - QuantityW - PriceW - StockW - NameX - 2f;

        // Built UI.
        private GameObject _root;
        private GameObject _headerRoot;
        private GameObject _closeRoot;
        private GameObject _searchRoot;
        private GameObject _columnHeaderRoot;
        private TextMeshProUGUI _headerItem;
        private TextMeshProUGUI _headerPrice;
        private TextMeshProUGUI _headerAmount;
        private TextMeshProUGUI _headerTotal;
        private GameObject _footerRoot;
        private TextMeshProUGUI _stationName;
        private TextMeshProUGUI _pointsLabel;
        private ExchangeView _exchangeView;
        private ExtraChargeView _extraChargeView;
        private GameObject _extraChargeRoot;
        private TextMeshProUGUI _extraChargeLabel;
        private Image _tabBuyBg;
        private Image _tabSellBg;
        private GameObject _sortRoot;
        private TextMeshProUGUI _sortLabel;
        private GameObject _sortDropdownRoot;
        private TMP_InputField _search;
        private readonly List<FilterTab> _categoryTabs = new List<FilterTab>();
        private RectTransform _listWell;
        private TextMeshProUGUI _headerStock;
        private readonly TradeRow[] _slots = new TradeRow[MaxRowSlots];
        private TextMeshProUGUI _emptyLabel;
        private GameObject _clearFiltersRoot;
        private TextMeshProUGUI _buySummaryLabel;
        private GameObject _buyClearRoot;
        private GameObject _buyExecuteRoot;
        private CommonButton _buyExecuteButton;
        private TextMeshProUGUI _lowReputationLabel;
        private TextMeshProUGUI _sellSummaryLabel;
        private TextMeshProUGUI _sellNoticeLabel;
        private GameObject _sellSelectAllRoot;
        private GameObject _sellClearRoot;
        private GameObject _sellExecuteRoot;
        private CommonButton _sellExecuteButton;
        private GameObject _previousPageRoot;
        private CommonButton _previousPageButton;
        private TextMeshProUGUI _pageLabel;
        private GameObject _nextPageRoot;
        private CommonButton _nextPageButton;
        private readonly List<GameObject> _sortDropdownRows = new List<GameObject>();
        // Vanilla widgets this panel switched off, so the restore puts back
        // exactly that set. See HideVanillaUi.
        private readonly List<GameObject> _hiddenVanillaObjects =
            new List<GameObject>();

        // State.
        private Pane _pane = Pane.Buy;
        private TradeCategory _category = TradeCategory.All;
        private TradeSort _sort = TradeSort.Name;
        private string _searchText = string.Empty;
        private int _scrollRow;
        private readonly Dictionary<string, int> _buyWallet = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _sellWallet = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _buyStockMap = new Dictionary<string, int>();
        private readonly Dictionary<string, int> _sellOwnedMap = new Dictionary<string, int>();
        private readonly List<TradeEntry> _index = new List<TradeEntry>();
        /// <summary>
        /// Item id -> maximum stack size, for the sell preview's per-stack
        /// rounding. Cleared on every save load: ItemFactory.GetMaxStackSize
        /// multiplies the record's MaxStack by the SAVE'S OWN
        /// Difficulty.Preset.ItemsStackSize (X2/X3/X4), so a value cached
        /// under one save is wrong under another.
        /// </summary>
        private static readonly Dictionary<string, int> StaticMaxStack =
            new Dictionary<string, int>(StringComparer.Ordinal);

        internal static void InvalidateSaveScopedCaches()
        {
            StaticMaxStack.Clear();
        }
        private bool _canTrade;
        private bool _canSell;
        private bool _listDirty;
        // Memoised footer readouts. RefreshTradeFooter runs every frame, so
        // the formatted strings are cached against the numbers that produced
        // them; invalidated whenever the labels or the localised format
        // strings are replaced.
        // Recomputed only when the cart, the pane or the save state moves.
        private bool _totalsDirty = true;
        private int _cachedBuyTotal;
        private int _cachedSellEstimate;
        private bool _summaryValid;
        private int _summarySell;
        private int _summaryBuy;
        private int _summaryAfter;
        private bool _pointsValid;
        private int _pointsShown;
        // Items received through a barter/quest exchange (e.g. handing the
        // AnCom data chip over) are moved straight to cargo, but the player
        // still needs to see what the station gave back: the summary lines
        // are shown in a dialog after the trade finishes.
        private List<string> _pendingExchangeSummary;
        // The language the panel was last built with: the pooled panel
        // survives screen closes, so a stale build must be discarded when
        // the game language changed meanwhile.
        private bool _langKnown;
        private Localization.Lang _builtLang;
        // Left-button sweep over rows: press anywhere on a row (a + button
        // included), slide over rows and every row touched gets the current
        // modifier step (plain sweep = MAX). Driven from Update via raw
        // mouse state, because the EventSystem's drag pipeline is unreliable
        // over buttons.
        private bool _sweepActive;
        private bool _pressTracked;
        private Vector2 _pressScreenPos;
        private bool _pressEligible;
        // Sweep direction, set by the press origin: -1 from the minus
        // button, +1 from the plus button (or anywhere else), 0 = MAX from
        // the MAX button.
        private int _sweepSign = 1;
        private int _sweepReleaseFrame = -1;
        private string _sweptItemId;
        private float _sweepHoldAccumulator;
        private const float SweepThresholdPx = 6f;
        private const float SweepHoldInterval = 0.22f;

        /// <summary>
        /// A click that arrives on the same frame a sweep ended is the
        /// release of the sweep press, not a real click -- callers skip it.
        /// </summary>
        internal bool SweepClickSuppressed =>
            Time.frameCount == _sweepReleaseFrame;
        // The click that opens a dropdown also bubbles up to the panel-wide
        // click catcher; the catcher ignores clicks on the very frame a
        // dropdown was opened so it does not close it again.
        private static int _blockedDropdownCloseFrame;

        private int PageCount =>
            Mathf.Max(1, (_index.Count + _visibleRows - 1) / _visibleRows);

        private int CurrentPage => _scrollRow / _visibleRows + 1;

        internal void Configure(FastTradeScreen screen, Station station,
            Factions factions, ItemsPrices itemsPrices, Statistics statistics,
            Difficulty difficulty, MagnumCargo cargo, SpaceTime spaceTime,
            MagnumProgression progression, StoryTriggers storyTriggers,
            ItemStorage activeCargo)
        {
            _screen = screen;
            _station = station;
            _factions = factions;
            _itemsPrices = itemsPrices;
            _statistics = statistics;
            _difficulty = difficulty;
            _cargo = cargo;
            _spaceTime = spaceTime;
            _progression = progression;
            _storyTriggers = storyTriggers;
            _faction = _factions.Get(station.OwnerFactionId);
            _canTrade = _faction.PlayerReputation
                        >= (float)Data.Global.TradeMinReputationToBuy;
            _canSell = _faction.PlayerReputation
                       >= (float)Data.Global.TradeMinReputationToSell;
            _activeCargo = activeCargo ?? cargo.ShipCargo[0];

            // Any items left in the vanilla station stash (staged through
            // the vanilla exchange UI before this mod was in charge) come
            // home automatically, so the staging area is always clean.
            TakeBackStashItems();

            // The pooled panel may survive from an earlier session in a
            // different language; throw it away before rebuilding.
            var localization = Singleton<Localization>.Instance;
            if (_root != null && _langKnown && localization != null
                && _builtLang != localization.CurrentLang)
            {
                DiscardBuiltUi();
            }
            EnsureBuilt();
            if (localization != null)
            {
                _builtLang = localization.CurrentLang;
                _langKnown = true;
            }
            _pane = Pane.Buy;
            _category = TradeCategory.All;
            _sort = TradeSort.Name;
            _searchText = string.Empty;
            _scrollRow = 0;
            if (_search != null)
                _search.SetTextWithoutNotify(string.Empty);
            _buyWallet.Clear();
            _sellWallet.Clear();

            _instance = this;
            IsPanelActive = true;
            if (localization != null)
            {
                localization.OnLangChanged -= OnLangChanged;
                localization.OnLangChanged += OnLangChanged;
            }
            _stationName.text =
                Localization.Get("station." + _station.Id + ".shortname");
            ApplyExtraCharge();
            if (Plugin.DebugTradeLayout)
                DumpVanillaLayout();
            RefreshAll();
            ShowPanel();
        }

        internal void ShowPanel()
        {
            if (_root == null)
                return;
            // The panel is pooled across screen openings and the design space
            // follows the screen resolution, so re-fit before showing rather
            // than trusting whatever size it was first built at.
            RefitToSurface();
            _root.SetActive(true);
            _root.transform.SetAsLastSibling();
            // The vanilla trade page may not have been injected when the
            // panel was first built; once the screen is live, retry.
            if (_exchangeView == null)
                TryAttachExchangeView();
            if (_extraChargeView == null)
            {
                TryAttachExtraChargeView();
                ApplyExtraCharge();
            }
        }

        /// <summary>
        /// Called when the pooled screen is closed: keep the component for the
        /// next opening but take the panel off the input paths.
        /// </summary>
        internal void OnScreenClosed()
        {
            // Symmetric with HideVanillaUi: the pooled screen goes back to
            // vanilla's own idea of what is visible, whoever opens it next.
            ShowHiddenVanillaUi();
            IsPanelActive = false;
            _sweepActive = false;
            _pressTracked = false;
            if (_root != null)
                _root.SetActive(false);
            _buyWallet.Clear();
            _sellWallet.Clear();
            _instance = null;
        }

        /// <summary>
        /// A vanilla opening of FastTradeScreen (mod disabled or not
        /// requested) must not leave our UI visible on the pooled screen --
        /// nor leave the vanilla screen's own widgets switched off.
        /// </summary>
        internal void RestoreVanillaUi()
        {
            IsPanelActive = false;
            if (_root != null)
                _root.SetActive(false);
            ShowHiddenVanillaUi();
            _instance = null;
        }

        /// <summary>
        /// This panel is a full-screen takeover, so everything the vanilla
        /// trade screen draws has to go -- not just the handful of fields the
        /// mod happens to hold FieldRefs for. Naming them one at a time missed
        /// FastTradeScreen's own caption and back button, which kept bleeding
        /// through behind the panel, and gave nothing to undo afterwards.
        ///
        /// Walks from this panel's parent up to the screen, switching off every
        /// child that is not on the path down to the panel, and REMEMBERS each
        /// one so the restore puts back exactly that set and nothing else.
        /// </summary>
        internal void HideVanillaUi()
        {
            if (_root == null || _screen == null)
                return;

            // Refuse to walk if the panel somehow ended up outside the screen:
            // the loop would climb past it and start switching off unrelated
            // UI views at the canvas root.
            if (!_root.transform.IsChildOf(_screen.transform))
            {
                Plugin.DebugLog("trade panel is not under its screen; "
                                + "skipping the vanilla hide sweep.");
                return;
            }

            ShowHiddenVanillaUi();
            var panelPath = new HashSet<Transform>();
            for (var node = _root.transform; node != null; node = node.parent)
                panelPath.Add(node);

            for (var level = _root.transform.parent; level != null;
                 level = level.parent)
            {
                foreach (Transform child in level)
                {
                    // Already-inactive children are left alone, so the restore
                    // cannot switch ON something vanilla wanted hidden.
                    if (panelPath.Contains(child)
                        || !child.gameObject.activeSelf)
                    {
                        continue;
                    }
                    child.gameObject.SetActive(false);
                    _hiddenVanillaObjects.Add(child.gameObject);
                }
                if (level == _screen.transform)
                    break;
            }
        }

        private void ShowHiddenVanillaUi()
        {
            foreach (var hidden in _hiddenVanillaObjects)
            {
                if (hidden != null)
                    hidden.SetActive(true);
            }
            _hiddenVanillaObjects.Clear();
        }

        internal void AdjustQuantity(string itemId, bool sell, int delta)
        {
            // Guard every entry point (buttons, wheel, sweep, right-click):
            // disabled-looking rows must not trade. EventTriggers fire even
            // when the CommonButton below them is not interactable.
            if (sell
                ? !TradeSystem.IsValidItem(_faction, _station, itemId)
                : !_canTrade)
            {
                return;
            }
            var wallet = WalletOf(sell);
            var full = sell ? _sellOwnedMap : _buyStockMap;
            var available = full.TryGetValue(itemId, out var value)
                ? value
                : 0;
            if (available <= 0)
                return;
            var current = wallet.TryGetValue(itemId, out var qty) ? qty : 0;
            var next = delta == int.MaxValue
                ? available
                : Mathf.Clamp(current + delta, 0, available);
            SetQuantity(itemId, sell, next);
        }

        private void SetQuantity(string itemId, bool sell, int qty)
        {
            var wallet = WalletOf(sell);
            if (qty <= 0)
                wallet.Remove(itemId);
            else
                wallet[itemId] = qty;
            var row = FindRow(itemId);
            if (row != null)
            {
                row.Input.SetTextWithoutNotify(qty.ToString());
                RefreshRowLabel(row);
            }
            InvalidateTotals();
            RefreshTotals();
        }

        private Dictionary<string, int> WalletOf(bool sell)
        {
            return sell ? _sellWallet : _buyWallet;
        }

        private TradeRow FindRow(string itemId)
        {
            for (var i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] != null && _slots[i].ItemId == itemId)
                    return _slots[i];
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Paging and drag sweep.
        // ------------------------------------------------------------------

        internal void ScrollRows(int direction)
        {
            if (_index.Count == 0)
                return;
            var page = Mathf.Clamp(CurrentPage + direction, 1, PageCount);
            if (page == CurrentPage)
                return;
            _scrollRow = (page - 1) * _visibleRows;
            PanelUi.PlayVanillaButtonClick();
            RefreshSlots();
        }

        internal void BeginSweep()
        {
            _sweepActive = true;
            _sweptItemId = null;
            _sweepHoldAccumulator = 0f;
        }

        internal void EndSweep()
        {
            _sweepActive = false;
            _sweepReleaseFrame = Time.frameCount;
            _sweptItemId = null;
        }

        private TradeRow RowUnderScreenPoint(Vector2 screenPosition)
        {
            if (_listWell == null || _index.Count == 0)
                return null;
            var canvas = UI.Canvas;
            var camera = canvas != null
                         && canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas == null ? null : canvas.worldCamera;
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    _listWell, screenPosition, camera, out var local))
            {
                return null;
            }
            if (local.x < 2f || local.x > 2f + _rowW)
                return null;
            // The header strip at the top of the well is not a row.
            var slot = Mathf.FloorToInt(-(local.y - SlotsTop) / RowH);
            if (slot < 0 || slot >= _visibleRows)
                return null;
            var index = _scrollRow + slot;
            if (index < 0 || index >= _index.Count)
                return null;
            var row = _slots[slot];
            return row != null && row.ItemId == _index[index].ItemId
                ? row
                : null;
        }

        // ------------------------------------------------------------------
        // Raw-input sweep: pressed + moved beyond a few pixels = sweep.
        // ------------------------------------------------------------------

        private void UpdateSweepInput()
        {
            var pressed = Input.GetMouseButton(0);
            if (!_pressTracked)
            {
                if (pressed)
                {
                    _pressTracked = true;
                    _pressScreenPos = Input.mousePosition;
                    _pressEligible = IsSweepEligiblePress(_pressScreenPos);
                    if (_pressEligible)
                    {
                        var origin = RowUnderScreenPoint(_pressScreenPos);
                        _sweepSign = origin == null
                            ? 1
                            : SweepSignOf(origin, _pressScreenPos);
                    }
                }
                return;
            }
            if (pressed)
            {
                if (_pressEligible && !_sweepActive
                    && (Input.mousePosition - (Vector3)_pressScreenPos)
                    .magnitude > SweepThresholdPx)
                {
                    BeginSweep();
                }
                if (!_sweepActive)
                    return;
                var row = RowUnderScreenPoint(Input.mousePosition);
                if (row == null || string.IsNullOrEmpty(row.ItemId))
                    return;
                var delta = _sweepSign == 0
                    ? int.MaxValue
                    : _sweepSign * ModifierStep();
                if (row.ItemId != _sweptItemId)
                {
                    // First touch of a row during this press: exactly ONE
                    // change of the sweep amount.
                    _sweptItemId = row.ItemId;
                    _sweepHoldAccumulator = 0f;
                    AdjustQuantity(row.ItemId, _pane == Pane.Sell, delta);
                }
                else
                {
                    // Holding still on one row: a slow repeat.
                    _sweepHoldAccumulator += Time.unscaledDeltaTime;
                    if (_sweepHoldAccumulator >= SweepHoldInterval)
                    {
                        _sweepHoldAccumulator = 0f;
                        AdjustQuantity(row.ItemId, _pane == Pane.Sell, delta);
                    }
                }
            }
            else
            {
                if (_sweepActive)
                    EndSweep();
                _pressTracked = false;
                _sweptItemId = null;
            }
        }

        /// <summary>
        /// The press origin decides what the sweep does: starting on the
        /// minus button sweeps downward, on MAX sweeps to the maximum, on
        /// the plus button (or anywhere else on the row) sweeps upward.
        /// </summary>
        private int SweepSignOf(TradeRow row, Vector2 screenPosition)
        {
            if (row.MinusRoot != null
                && ContainsScreenPoint(row.MinusRoot, screenPosition))
            {
                return -1;
            }
            if (row.MaxRoot != null
                && ContainsScreenPoint(row.MaxRoot, screenPosition))
            {
                return 0;
            }
            return 1;
        }

        private bool ContainsScreenPoint(GameObject target,
            Vector2 screenPosition)
        {
            var rect = target.transform as RectTransform;
            if (rect == null)
                return false;
            var canvas = UI.Canvas;
            var camera = canvas != null
                         && canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas == null ? null : canvas.worldCamera;
            return RectTransformUtility.ScreenPointToLocalPointInRectangle(
                       rect, screenPosition, camera, out var local)
                   && rect.rect.Contains(local);
        }

        /// <summary>
        /// The sweep press must start on a visible row and not on the
        /// quantity input (its text selection owns that drag).
        /// </summary>
        private bool IsSweepEligiblePress(Vector2 screenPosition)
        {
            var row = RowUnderScreenPoint(screenPosition);
            if (row == null || row.InputRoot == null)
                return false;
            var inputRect = row.InputRoot.transform as RectTransform;
            var canvas = UI.Canvas;
            var camera = canvas != null
                         && canvas.renderMode == RenderMode.ScreenSpaceOverlay
                ? null
                : canvas == null ? null : canvas.worldCamera;
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    inputRect, screenPosition, camera, out var local)
                && inputRect.rect.Contains(local))
            {
                return false;
            }
            return true;
        }

        private bool AnyInputFocused
        {
            get
            {
                if (_root == null || EventSystem.current == null)
                    return false;
                var current = EventSystem.current.currentSelectedGameObject;
                return current != null
                       && current.transform.IsChildOf(_root.transform)
                       && current.GetComponent<TMP_InputField>() != null;
            }
        }

        private bool HasOpenDropdown()
        {
            return _sortDropdownRoot != null
                   && _sortDropdownRoot.activeInHierarchy;
        }

        /// <summary>
        /// debugTradeLayout=true dumps the vanilla screen's real hierarchy so
        /// layout problems can be fixed against the actual data.
        /// </summary>
        private void DumpVanillaLayout()
        {
            try
            {
                LayoutDebug.DumpHierarchy("station trade layout dump",
                    _screen.transform);
            }
            catch (Exception e)
            {
                Debug.LogWarning(Plugin.LogPrefix
                                 + "layout dump failed: " + e);
            }
        }

        // ------------------------------------------------------------------
        // UI construction.
        // ------------------------------------------------------------------

        /// <summary>
        /// Works out the panel envelope for the surface it is parented to.
        ///
        /// The design space is not a constant: MGSC.UIScaleFixer picks a
        /// different CanvasScaler mode per aspect ratio, so a 21:9 monitor
        /// gets roughly 853x360 design units and a 32:9 one 1280x360 (see
        /// PanelUi.DesignSurfaceSize).  The panel therefore takes the width
        /// it is given, up to a readable maximum, and fits a whole number of
        /// list rows into the height.  A 640x360 surface reproduces the
        /// original fixed 636x356 layout exactly, down to the pixel.
        /// </summary>
        private void MeasureLayout(Transform surface)
        {
            var size = PanelUi.DesignSurfaceSize(surface,
                new Vector2(640f, 360f));

            _panelW = Mathf.Floor(Mathf.Clamp(size.x - PanelMargin * 2f,
                DesignPanelW, MaxPanelW));

            var availableH = size.y - PanelMargin * 2f;
            _visibleRows = Mathf.Clamp(
                Mathf.FloorToInt((availableH - FixedPanelH) / RowH),
                MinRowSlots, MaxRowSlots);
            // Whatever is left after the last whole row becomes bottom
            // padding, up to the 7px the 16:9 design uses.
            var contentH = FixedPanelH + _visibleRows * RowH;
            _panelH = Mathf.Floor(contentH
                                  + Mathf.Clamp(availableH - contentH, 0f, 4f));

            _rowW = _panelW - 12f;
            _listH = WellPadTop + _visibleRows * RowH + WellPadBottom;
            _footerY = ListY - _listH - FooterGap;

            // Number columns pack against the row's right edge; the name
            // column takes the rest, so extra width goes to item names.
            _subtotalX = _rowW - 4f - SubtotalW;
            _quantityX = _subtotalX - 4f - QuantityW;
            _priceX = _quantityX - 4f - PriceW;
            _stockX = _priceX - 4f - StockW;
            _nameW = _stockX - NameX - 2f;
        }

        /// <summary>
        /// Re-stamps every rect from the current measurements. Split from the
        /// Build* methods so a resolution change only has to re-position the
        /// panel instead of rebuilding it.
        /// </summary>
        private void ApplyLayout()
        {
            if (_root == null)
                return;

            // Centred, never corner-anchored: this is the whole reason the
            // panel used to sit in the top-left third of an ultrawide screen.
            PanelUi.SetCentered((RectTransform)_root.transform,
                _panelW, _panelH);

            if (_headerRoot != null)
            {
                PanelUi.SetTopLeft((RectTransform)_headerRoot.transform,
                    0f, 0f, _panelW, 17f);
            }
            if (_closeRoot != null)
            {
                PanelUi.SetTopLeft((RectTransform)_closeRoot.transform,
                    _panelW - 54f, -2f, 52f, 13f);
            }
            LayoutHeaderWidgets();

            if (_searchRoot != null)
            {
                PanelUi.SetTopLeft((RectTransform)_searchRoot.transform,
                    4f, -37f, Mathf.Max(80f, _panelW - 106f), 14f);
            }
            if (_sortRoot != null)
            {
                PanelUi.SetTopLeft((RectTransform)_sortRoot.transform,
                    _panelW - 98f, -37f, 94f, 14f);
            }
            if (_sortDropdownRoot != null)
            {
                // Height is owned by RebuildSortDropdown (it grows with the
                // option count), so only the placement is touched here.
                var dropdownRect = (RectTransform)_sortDropdownRoot.transform;
                PanelUi.SetTopLeft(dropdownRect, _panelW - 98f, -53f, 94f,
                    dropdownRect.sizeDelta.y);
            }
            LayoutCategoryChips();

            if (_listWell != null)
                PanelUi.SetTopLeft(_listWell, 4f, ListY, _panelW - 8f, _listH);
            if (_columnHeaderRoot != null)
            {
                PanelUi.SetTopLeft(
                    (RectTransform)_columnHeaderRoot.transform,
                    2f, -2f, _rowW, HeaderRowH);
            }
            SetColumnRect(_headerItem, NameX, _nameW, HeaderRowH);
            SetColumnRect(_headerStock, _stockX, StockW, HeaderRowH);
            SetColumnRect(_headerPrice, _priceX, PriceW, HeaderRowH);
            SetColumnRect(_headerAmount, _quantityX, QuantityW, HeaderRowH);
            SetColumnRect(_headerTotal, _subtotalX, SubtotalW, HeaderRowH);

            LayoutRows();
            LayoutEmptyState();
            LayoutFooter();
        }

        private void LayoutRows()
        {
            for (var i = 0; i < _slots.Length; i++)
            {
                var row = _slots[i];
                if (row == null || row.Root == null)
                    continue;
                // Slots past the fitted row count are parked, not destroyed:
                // a taller screen brings them straight back.
                if (i >= _visibleRows)
                {
                    row.ItemId = null;
                    row.Entry = null;
                    row.Root.SetActive(false);
                    continue;
                }
                PanelUi.SetTopLeft((RectTransform)row.Root.transform,
                    2f, SlotsTop - i * RowH, _rowW, RowH);
                SetColumnRect(row.Name, NameX, _nameW, RowH);
                SetColumnRect(row.Stock, _stockX, StockW, RowH);
                SetColumnRect(row.Price, _priceX, PriceW, RowH);
                SetColumnRect(row.Subtotal, _subtotalX, SubtotalW, RowH);
                SetColumnRect(row.Badge, _subtotalX, SubtotalW, RowH);
                if (row.Catcher != null)
                {
                    PanelUi.SetTopLeft((RectTransform)row.Catcher.transform,
                        NameX, 0f, Mathf.Max(20f, _stockX - NameX - 4f), RowH);
                }
                if (row.QtyRoot != null)
                {
                    PanelUi.SetTopLeft((RectTransform)row.QtyRoot.transform,
                        _quantityX, -2f, QuantityW, 16f);
                }
            }
        }

        private static void SetColumnRect(TextMeshProUGUI label, float x,
            float width, float height)
        {
            if (label != null)
                PanelUi.SetTopLeft(label.rectTransform, x, 0f, width, height);
        }

        /// <summary>
        /// Re-fits a pooled panel to the surface it is about to be shown on.
        /// The player can change resolution -- and with it the scaler mode --
        /// between two openings of the trade screen.
        /// </summary>
        private void RefitToSurface()
        {
            if (_root == null)
                return;
            var previousWidth = _panelW;
            var previousHeight = _panelH;
            var previousRows = _visibleRows;
            MeasureLayout(_root.transform.parent);
            if (previousRows == _visibleRows
                && Mathf.Approximately(previousWidth, _panelW)
                && Mathf.Approximately(previousHeight, _panelH))
            {
                return;
            }
            ApplyLayout();
            RefreshSlots();
        }

        private void EnsureBuilt()
        {
            if (_root != null)
                return;
            try
            {
                var cargoWindow = Plugin.CargoWindowOf(_screen);
                var parent = cargoWindow == null
                    ? _screen.transform
                    : cargoWindow.transform.parent;

                // Collect the game's own UI art before anything is built.
                VanillaSkin.Seed(parent);

                // Measure first: the Build* methods place their widgets from
                // these numbers, so nothing is ever built at the wrong size.
                MeasureLayout(parent);

                _root = PanelUi.CreateUiObject("QM_CentralStationTrade", parent);
                _root.SetActive(false);
                var rect = (RectTransform)_root.transform;
                PanelUi.SetCentered(rect, _panelW, _panelH);
                VanillaSkin.Slice(_root, VanillaSkin.PanelFrame,
                    PanelUi.PanelColor).raycastTarget = true;

                BuildHeader(_root.transform);
                BuildTabs(_root.transform);
                BuildFilterRow(_root.transform);
                BuildCategoryChips(_root.transform);
                BuildList(_root.transform);
                BuildFooter(_root.transform);
                ApplyLayout();

                // Any click anywhere in the panel closes an open dropdown;
                // option rows handle their own click first, then this bubbles.
                var trigger = _root.AddComponent<EventTrigger>();
                var entry = new EventTrigger.Entry
                {
                    eventID = EventTriggerType.PointerClick,
                    callback = new EventTrigger.TriggerEvent(),
                };
                entry.callback.AddListener(_ =>
                {
                    if (Time.frameCount != _blockedDropdownCloseFrame)
                        CloseDropdowns();
                });
                trigger.triggers = new List<EventTrigger.Entry> { entry };
            }
            catch
            {
                DiscardBuiltUi();
                throw;
            }
        }

        private void DiscardBuiltUi()
        {
            for (var i = 0; i < _slots.Length; i++)
            {
                if (_slots[i] != null && _slots[i].Root != null)
                    Destroy(_slots[i].Root);
                _slots[i] = null;
            }
            _categoryTabs.Clear();
            _sortDropdownRows.Clear();
            // The cached readouts belong to labels that are about to die --
            // and a language switch replaces the format strings too.
            _summaryValid = false;
            _pointsValid = false;
            _totalsDirty = true;
            // Destroy is deferred to the end of the frame, so every field
            // must be nulled NOW: otherwise the guards in the attach
            // methods see the dying clones as "already present", skip the
            // re-clone, and the widgets vanish permanently once the old
            // objects are really destroyed.
            _headerRoot = null;
            _closeRoot = null;
            _searchRoot = null;
            _columnHeaderRoot = null;
            _headerItem = null;
            _headerPrice = null;
            _headerAmount = null;
            _headerTotal = null;
            _footerRoot = null;
            _stationName = null;
            _pointsLabel = null;
            _exchangeView = null;
            _extraChargeView = null;
            _extraChargeRoot = null;
            _extraChargeLabel = null;
            _tabBuyBg = null;
            _tabSellBg = null;
            _sortRoot = null;
            _sortLabel = null;
            _sortDropdownRoot = null;
            _search = null;
            _listWell = null;
            _headerStock = null;
            _emptyLabel = null;
            _clearFiltersRoot = null;
            _buySummaryLabel = null;
            _buyClearRoot = null;
            _buyExecuteRoot = null;
            _buyExecuteButton = null;
            _lowReputationLabel = null;
            _sellSummaryLabel = null;
            _sellNoticeLabel = null;
            _sellSelectAllRoot = null;
            _sellClearRoot = null;
            _sellExecuteRoot = null;
            _sellExecuteButton = null;
            _previousPageRoot = null;
            _previousPageButton = null;
            _pageLabel = null;
            _nextPageRoot = null;
            _nextPageButton = null;
            if (_root != null)
            {
                _root.SetActive(false);
                Destroy(_root);
                _root = null;
            }
        }

        private void BuildHeader(Transform parent)
        {
            var header = PanelUi.CreateUiObject("Header", parent);
            _headerRoot = header;
            PanelUi.SetTopLeft((RectTransform)header.transform,
                0f, 0f, _panelW, 17f);
            VanillaSkin.Slice(header, VanillaSkin.HeaderFrame,
                PanelUi.HeaderColor);

            var title = PanelUi.CreateText("Title", header.transform,
                6f, 0f, 150f, 17f, TextContext.WindowCaption,
                PanelUi.BrightColor, TextAlignmentOptions.MidlineLeft);
            title.text = Localization.Get("qmtrade.title");

            _stationName = PanelUi.CreateText("Station", header.transform,
                158f, 0f, 190f, 17f, TextContext.IgnoreSize, 7f,
                PanelUi.ValueColor, TextAlignmentOptions.MidlineLeft);

            // The game's own trade-points widget: value plus the current
            // cart's delta, e.g. "1234 (+567)" on the sell pane. The clone
            // is attempted again from ShowPanel when the vanilla page was
            // not injected yet at build time.
            TryAttachExchangeView();

            // Fallback readout when the vanilla widget is unavailable.
            _pointsLabel = PanelUi.CreateText("Points", header.transform,
                358f, 0f, 200f, 17f, TextContext.IgnoreSize, 8f,
                PanelUi.AccentColor, TextAlignmentOptions.MidlineLeft);
            _pointsLabel.gameObject.SetActive(_exchangeView == null);
            LayoutHeaderWidgets();

            _closeRoot = PanelUi.CreateButtonRoot("Close", header.transform,
                _panelW - 54f, -2f, 52f, 13f, "qmtrade.tip.close",
                out _, out var closeLabel);
            closeLabel.text = Localization.Get("qmtrade.close");
            PanelUi.BindClick(_closeRoot, ClosePanel);
        }

        /// <summary>
        /// Clones the vanilla ExchangeView (trade points widget) into the
        /// header. Called at build time and again from ShowPanel, because the
        /// vanilla trade page the widget lives on is sub-injected and may not
        /// exist yet when the panel is first assembled.
        /// </summary>
        private void TryAttachExchangeView()
        {
            if (_exchangeView != null || _headerRoot == null)
                return;
            var vanillaExchange = Plugin.ExchangeViewOf(_screen);
            if (vanillaExchange == null)
                return;
            var clone = Instantiate(vanillaExchange.gameObject,
                _headerRoot.transform, false);
            _exchangeView = clone.GetComponent<ExchangeView>();
            if (_exchangeView == null)
            {
                Destroy(clone);
                Debug.LogWarning(Plugin.LogPrefix
                                 + "could not clone the vanilla exchange view.");
                return;
            }
            _exchangeView.gameObject.SetActive(true);
            if (_pointsLabel != null)
                _pointsLabel.gameObject.SetActive(false);
            LayoutHeaderWidgets();
        }

        /// <summary>
        /// Positions the trade-points widget by its measured width, right
        /// before the close button, so it never collides with anything:
        ///   close(right..-2) | points(..-56) | station name
        /// The offset is measured from the header's RIGHT edge, so the widget
        /// keeps its place beside CLOSE at any panel width.
        /// </summary>
        private void LayoutHeaderWidgets()
        {
            if (_headerRoot == null)
                return;
            const float pointsRightInset = -56f;
            const float pointsMaxW = 110f;

            var pointsRect = _exchangeView != null
                ? (RectTransform)_exchangeView.transform
                : (_pointsLabel != null ? _pointsLabel.rectTransform : null);

            var pointsW = pointsMaxW;
            if (pointsRect != null)
            {
                pointsW = Mathf.Min(pointsRect.sizeDelta.x, pointsMaxW);
                PanelUi.AnchorTopRight(pointsRect,
                    new Vector2(pointsRightInset, 0f),
                    new Vector2(pointsW,
                        Mathf.Min(pointsRect.sizeDelta.y, 17f)));
            }

            // The station name fills everything between the title and the
            // points widget instead of a fixed 190px box, so a wider panel
            // stops truncating long station names.
            if (_stationName != null)
            {
                PanelUi.SetTopLeft(_stationName.rectTransform, 158f, 0f,
                    Mathf.Max(60f,
                        _panelW + pointsRightInset - pointsW - 4f - 158f),
                    17f);
            }
        }

        /// <summary>
        /// Clones the vanilla ExtraChargeView (the game's discount widget)
        /// into the buy pane's footer, next to the running total. Its full
        /// height is kept so the widget's decorative bars stay visible and
        /// no frame covers them.
        /// </summary>
        private void TryAttachExtraChargeView()
        {
            if (_extraChargeView != null || _footerRoot == null)
                return;
            var vanillaExtraCharge = Plugin.ExtraChargeViewOf(_screen);
            if (vanillaExtraCharge == null)
                return;
            var clone = Instantiate(vanillaExtraCharge.gameObject,
                _footerRoot.transform, false);
            _extraChargeView = clone.GetComponent<ExtraChargeView>();
            if (_extraChargeView == null)
            {
                Destroy(clone);
                Debug.LogWarning(Plugin.LogPrefix
                                 + "could not clone the vanilla extra charge view.");
                return;
            }
            _extraChargeRoot = clone;
            _extraChargeView.gameObject.SetActive(true);
            var viewRect = (RectTransform)_extraChargeView.transform;
            // Bottom-left of the footer, vertically CENTERED on the footer's
            // own centre line (footer height 17 -> centre at y = -8.5), so
            // the widget aligns with every other footer element regardless
            // of its natural height.
            viewRect.anchorMin = new Vector2(0f, 0f);
            viewRect.anchorMax = new Vector2(0f, 0f);
            viewRect.pivot = new Vector2(0f, 0f);
            viewRect.sizeDelta = new Vector2(
                Mathf.Min(viewRect.sizeDelta.x, 120f), viewRect.sizeDelta.y);
            viewRect.anchoredPosition = new Vector2(
                _panelW - 8f - ExtraChargeInset,
                FooterH * 0.5f - viewRect.sizeDelta.y * 0.5f);
            viewRect.localScale = Vector3.one;
            if (_extraChargeLabel != null)
                _extraChargeLabel.gameObject.SetActive(false);
            ApplyExtraCharge();
        }

        /// <summary>
        /// Feeds the current station's price situation into the widget:
        /// negative = discount (the higher the reputation above the vanilla
        /// threshold, the bigger the discount), positive = markup, and the
        /// player's own proxy faction trades at -100% (everything free).
        /// </summary>
        private void ApplyExtraCharge()
        {
            var charge = CurrentExtraCharge();
            if (_extraChargeView != null)
            {
                _extraChargeView.Initialize(charge);
                return;
            }
            if (_extraChargeLabel == null)
                return;
            _extraChargeLabel.text = Localization.Get(charge < 0f
                    ? "qmtrade.discount"
                    : "qmtrade.extra_charge")
                .SafeFormat(FormatHelper.To100Percent(charge, showPlus: true));
            _extraChargeLabel.color = charge < 0f
                ? PanelUi.AccentColor
                : PanelUi.DangerColor;
        }

        private float CurrentExtraCharge()
        {
            var department = _progression.GetDepartment<ProxyCorpDepartment>();
            if (department != null
                && department.ProxyFactionId == _faction.Id)
            {
                return -1f;
            }
            return TradeSystem.GetExtraCharge(_faction.PlayerReputation);
        }

        private void BuildTabs(Transform parent)
        {
            // Tabs keep their natural width against the panel's left edge;
            // only the widgets that must reach the right edge are re-laid
            // out when the panel grows.
            var buyRoot = PanelUi.CreateButtonRoot("BuyTab", parent,
                4f, -21f, 90f, 14f, "qmtrade.tip.buy_tab",
                out _tabBuyBg, out var buyLabel);
            buyLabel.text = Localization.Get("qmtrade.tab_buy")
                            + ShortcutSuffix(Plugin.ShortcutTogglePane);
            PanelUi.BindClick(buyRoot, () => SelectPane(Pane.Buy));

            var sellRoot = PanelUi.CreateButtonRoot("SellTab", parent,
                96f, -21f, 90f, 14f, "qmtrade.tip.sell_tab",
                out _tabSellBg, out var sellLabel);
            sellLabel.text = Localization.Get("qmtrade.tab_sell")
                             + ShortcutSuffix(Plugin.ShortcutTogglePane);
            PanelUi.BindClick(sellRoot, () => SelectPane(Pane.Sell));
        }

        private void BuildFilterRow(Transform parent)
        {
            var searchRoot = PanelUi.CreateUiObject("Search", parent);
            _searchRoot = searchRoot;
            PanelUi.SetTopLeft((RectTransform)searchRoot.transform,
                4f, -37f, _panelW - 106f, 14f);
            var background = VanillaSkin.Slice(searchRoot,
                VanillaSkin.ListBackground, PanelUi.CardColor);
            VanillaSkin.AddHint(searchRoot, "qmtrade.tip.search");

            var viewport = PanelUi.CreateUiObject("Viewport",
                searchRoot.transform);
            var viewportRect = (RectTransform)viewport.transform;
            PanelUi.Stretch(viewportRect);
            viewportRect.offsetMin = new Vector2(3f, 1f);
            viewportRect.offsetMax = new Vector2(-3f, -1f);
            viewport.AddComponent<RectMask2D>();

            var placeholderObject = PanelUi.CreateUiObject("Placeholder",
                viewport.transform);
            var placeholderRect = (RectTransform)placeholderObject.transform;
            PanelUi.Stretch(placeholderRect);
            var placeholder = placeholderObject.AddComponent<TextMeshProUGUI>();
            PanelUi.ConfigureText(placeholder, TextContext.IgnoreSize, 7f,
                PanelUi.OffColor, TextAlignmentOptions.MidlineLeft);
            placeholder.fontStyle = FontStyles.Italic;
            placeholder.text = Localization.Get("qmtrade.search");

            var textObject = PanelUi.CreateUiObject("Text", viewport.transform);
            var textRect = (RectTransform)textObject.transform;
            PanelUi.Stretch(textRect);
            var text = textObject.AddComponent<TextMeshProUGUI>();
            PanelUi.ConfigureText(text, TextContext.IgnoreSize, 7f,
                PanelUi.ValueColor, TextAlignmentOptions.MidlineLeft);

            _search = searchRoot.AddComponent<TMP_InputField>();
            _search.targetGraphic = background;
            _search.textViewport = viewportRect;
            _search.textComponent = text;
            _search.placeholder = placeholder;
            _search.lineType = TMP_InputField.LineType.SingleLine;
            _search.contentType = TMP_InputField.ContentType.Standard;
            _search.richText = false;
            _search.customCaretColor = true;
            _search.caretColor = PanelUi.AccentColor;
            _search.selectionColor = new Color(0.25f, 0.58f, 0.49f, 0.55f);
            _search.onValueChanged.AddListener(_ =>
            {
                _searchText = _search.text;
                _scrollRow = 0;
                _listDirty = true;
            });
            _search.onSelect.AddListener(_ =>
            {
                CloseDropdowns();
                Input.imeCompositionMode = IMECompositionMode.On;
            });
            _search.onDeselect.AddListener(_ =>
                ModInputGate.BlockThisFrame());
            _search.onEndEdit.AddListener(_ =>
                ModInputGate.BlockThisFrame());

            _sortRoot = PanelUi.CreateDropdownTrigger("Sort", parent,
                _panelW - 98f, -37f, 94f, 14f, out _sortLabel);
            VanillaSkin.AddHint(_sortRoot, "qmtrade.tip.sort");
            PanelUi.BindClick(_sortRoot, ToggleSortDropdown);
            _sortDropdownRoot = PanelUi.CreateDropdownRoot("SortDropdown",
                parent, _panelW - 98f, -53f, 94f, 20f);
        }

        private void BuildCategoryChips(Transform parent)
        {
            var categories = (TradeCategory[])Enum.GetValues(
                typeof(TradeCategory));
            for (var i = 0; i < categories.Length; i++)
            {
                var category = categories[i];
                var root = PanelUi.CreateButtonRoot("Category" + category,
                    parent, 4f + i * 79f, -53f, 77f, 13f,
                    out var background, out var label);
                label.text = CategoryLabelOf(category);
                PanelUi.BindClick(root, () =>
                {
                    _scrollRow = 0;
                    SelectCategory(category);
                });
                _categoryTabs.Add(new FilterTab
                {
                    Root = root,
                    Background = background,
                    Label = label,
                    Category = category,
                });
            }
            LayoutCategoryChips();
        }

        /// <summary>
        /// One flush row of chips across the panel's inner width. The width
        /// is integer and the division remainder is spread one pixel at a
        /// time over the leading chips, so the row lands exactly on both
        /// edges without any chip sitting on a half pixel.
        /// </summary>
        private void LayoutCategoryChips()
        {
            var count = _categoryTabs.Count;
            if (count == 0)
                return;
            const float gap = 2f;
            var innerW = _panelW - 8f;
            var total = Mathf.FloorToInt(innerW - gap * (count - 1));
            var chipW = total / count;
            var remainder = total - chipW * count;

            var x = 4f;
            for (var i = 0; i < count; i++)
            {
                var width = chipW + (i < remainder ? 1 : 0);
                PanelUi.SetTopLeft(
                    (RectTransform)_categoryTabs[i].Root.transform,
                    x, -53f, width, 13f);
                x += width + gap;
            }
        }

        private void BuildList(Transform parent)
        {
            // A recessed well behind the slots, like the central panel's
            // card well: no ScrollRect, fixed slots that page instead.
            var well = PanelUi.CreateUiObject("ListWell", parent);
            _listWell = (RectTransform)well.transform;
            PanelUi.SetTopLeft(_listWell, 4f, ListY, _panelW - 8f, _listH);
            VanillaSkin.Slice(well, VanillaSkin.ListBackground,
                PanelUi.CardColor).raycastTarget = true;
            var pageWheel = well.AddComponent<TradePageWheel>();
            pageWheel.Panel = this;

            // Column headers sit at the top of the well, right below the
            // category row, explaining what each number column means.
            _columnHeaderRoot = PanelUi.CreateUiObject("ColumnHeaders",
                well.transform);
            var header = _columnHeaderRoot;
            PanelUi.SetTopLeft((RectTransform)header.transform,
                2f, -2f, _rowW, HeaderRowH);
            _headerItem = PanelUi.CreateText("HeaderItem", header.transform,
                NameX, 0f, _nameW, HeaderRowH, TextContext.IgnoreSize, 7f,
                PanelUi.OffColor, TextAlignmentOptions.MidlineLeft);
            _headerItem.text = Localization.Get("qmtrade.header.item");
            _headerStock = PanelUi.CreateText("HeaderStock", header.transform,
                _stockX, 0f, StockW, HeaderRowH, TextContext.IgnoreSize, 7f,
                PanelUi.OffColor, TextAlignmentOptions.MidlineRight);
            _headerPrice = PanelUi.CreateText("HeaderPrice",
                header.transform, _priceX, 0f, PriceW, HeaderRowH,
                TextContext.IgnoreSize, 7f, PanelUi.OffColor,
                TextAlignmentOptions.MidlineRight);
            _headerPrice.text = Localization.Get("qmtrade.header.price");
            _headerAmount = PanelUi.CreateText("HeaderAmount",
                header.transform, _quantityX, 0f, QuantityW, HeaderRowH,
                TextContext.IgnoreSize, 7f, PanelUi.OffColor,
                TextAlignmentOptions.Center);
            _headerAmount.text = Localization.Get("qmtrade.header.amount");
            _headerTotal = PanelUi.CreateText("HeaderTotal",
                header.transform, _subtotalX, 0f, SubtotalW, HeaderRowH,
                TextContext.IgnoreSize, 7f, PanelUi.OffColor,
                TextAlignmentOptions.MidlineRight);
            _headerTotal.text = Localization.Get("qmtrade.header.total");

            for (var i = 0; i < MaxRowSlots; i++)
            {
                var row = BuildSlot(_listWell, i);
                _slots[i] = row;
            }

            _emptyLabel = PanelUi.CreateText("Empty", parent, 4f, -200f,
                _panelW - 8f, 16f, TextContext.IgnoreSize, 9f,
                PanelUi.OffColor, TextAlignmentOptions.Center);
            _emptyLabel.gameObject.SetActive(false);

            _clearFiltersRoot = PanelUi.CreateButtonRoot("ClearFilters", parent,
                268f, -182f, 96f, 14f, "qmtrade.tip.clear_filters",
                out _, out var clearFiltersLabel);
            clearFiltersLabel.text = Localization.Get("qmtrade.clear_filters");
            PanelUi.BindClick(_clearFiltersRoot, ClearAllFilters);
            _clearFiltersRoot.SetActive(false);
            LayoutEmptyState();
        }

        /// <summary>
        /// The "nothing matches" message and its CLEAR FILTERS button, both
        /// centred on the list well's real centre line rather than the magic
        /// offsets that only lined up at one panel size.
        /// </summary>
        private void LayoutEmptyState()
        {
            var center = ListY - _listH * 0.5f;
            if (_emptyLabel != null)
            {
                PanelUi.SetTopLeft(_emptyLabel.rectTransform, 4f,
                    Mathf.Floor(center + 16f), _panelW - 8f, 16f);
            }
            if (_clearFiltersRoot != null)
            {
                const float clearFiltersW = 96f;
                PanelUi.SetTopLeft(
                    (RectTransform)_clearFiltersRoot.transform,
                    Mathf.Floor((_panelW - clearFiltersW) * 0.5f),
                    Mathf.Floor(center - 4f), clearFiltersW, 14f);
            }
        }

        private void BuildFooter(Transform parent)
        {
            var footer = PanelUi.CreateUiObject("Footer", parent);
            _footerRoot = footer;
            PanelUi.SetTopLeft((RectTransform)footer.transform,
                4f, _footerY, _panelW - 8f, FooterH);

            _buySummaryLabel = PanelUi.CreateText("BuySummary",
                footer.transform, 2f, 0f, SummaryMinW, FooterH, TextContext.IgnoreSize,
                8f, PanelUi.ValueColor, TextAlignmentOptions.MidlineLeft);
            _lowReputationLabel = PanelUi.CreateText("LowReputation",
                footer.transform, 2f, 0f, SummaryMinW, FooterH, TextContext.IgnoreSize,
                8f, PanelUi.DangerColor, TextAlignmentOptions.MidlineLeft);
            _lowReputationLabel.gameObject.SetActive(false);

            _sellSummaryLabel = PanelUi.CreateText("SellSummary",
                footer.transform, 2f, 0f, SummaryMinW, FooterH, TextContext.IgnoreSize,
                8f, PanelUi.ValueColor, TextAlignmentOptions.MidlineLeft);
            _sellNoticeLabel = PanelUi.CreateText("SellNotice",
                footer.transform, 2f, 0f, SummaryMinW, FooterH, TextContext.IgnoreSize,
                8f, PanelUi.DangerColor, TextAlignmentOptions.MidlineLeft);
            _sellNoticeLabel.gameObject.SetActive(false);

            // Pager, between the summary and the action buttons.
            _previousPageRoot = PanelUi.CreateButtonRoot("PreviousPage",
                footer.transform, 0f, 0f, PageArrowW, FooterH,
                "qmtrade.tip.previous_page", out _, out var previousLabel);
            previousLabel.text = "[";
            _previousPageButton = PanelUi.ButtonOf(_previousPageRoot);
            PanelUi.BindClick(_previousPageRoot, () => ScrollRows(-1));
            _pageLabel = PanelUi.CreateText("Page", footer.transform,
                0f, 0f, PageLabelW, FooterH, TextContext.SmallNumbers,
                PanelUi.ValueColor, TextAlignmentOptions.Center);
            _nextPageRoot = PanelUi.CreateButtonRoot("NextPage",
                footer.transform, 0f, 0f, PageArrowW, FooterH,
                "qmtrade.tip.next_page", out _, out var nextLabel);
            nextLabel.text = "]";
            _nextPageButton = PanelUi.ButtonOf(_nextPageRoot);
            PanelUi.BindClick(_nextPageRoot, () => ScrollRows(1));

            // Buy pane controls.
            _buyClearRoot = PanelUi.CreateButtonRoot("BuyClear",
                footer.transform, 0f, 0f, ClearButtonW, FooterH, "qmtrade.tip.clear",
                out _, out var buyClearLabel);
            buyClearLabel.text = Localization.Get("qmtrade.clear")
                                 + ShortcutSuffix(Plugin.ShortcutClearCart);
            PanelUi.BindClick(_buyClearRoot, () =>
            {
                _buyWallet.Clear();
                RefreshSlots();
                InvalidateTotals();
                RefreshTotals();
            });

            _buyExecuteRoot = PanelUi.CreateButtonRoot("Trade",
                footer.transform, 0f, 0f, BuyExecuteW, FooterH, "qmtrade.tip.trade",
                out _, out var buyLabel);
            buyLabel.text = Localization.Get("qmtrade.trade")
                            + ShortcutSuffix(Plugin.ShortcutTrade);
            _buyExecuteButton = PanelUi.ButtonOf(_buyExecuteRoot);
            PanelUi.BindClick(_buyExecuteRoot, ExecuteTrade);

            // Sell pane controls. Every footer widget is positioned by
            // LayoutFooter, from its distance to the footer's right edge.
            _sellSelectAllRoot = PanelUi.CreateButtonRoot("SelectAll",
                footer.transform, 0f, 0f, SellSelectAllW, FooterH, "qmtrade.tip.select_all",
                out _, out var selectAllLabel);
            selectAllLabel.text = Localization.Get("qmtrade.select_all")
                                  + ShortcutSuffix(Plugin.ShortcutSelectAll);
            PanelUi.BindClick(_sellSelectAllRoot, SelectAllVisible);

            _sellClearRoot = PanelUi.CreateButtonRoot("SellClear",
                footer.transform, 0f, 0f, ClearButtonW, FooterH, "qmtrade.tip.clear",
                out _, out var sellClearLabel);
            sellClearLabel.text = Localization.Get("qmtrade.clear")
                                  + ShortcutSuffix(Plugin.ShortcutClearCart);
            PanelUi.BindClick(_sellClearRoot, () =>
            {
                _sellWallet.Clear();
                RefreshSlots();
                InvalidateTotals();
                RefreshTotals();
            });

            _sellExecuteRoot = PanelUi.CreateButtonRoot("Trade",
                footer.transform, 0f, 0f, SellExecuteW, FooterH, "qmtrade.tip.trade",
                out _, out var sellLabel);
            sellLabel.text = Localization.Get("qmtrade.trade")
                             + ShortcutSuffix(Plugin.ShortcutTrade);
            _sellExecuteButton = PanelUi.ButtonOf(_sellExecuteRoot);
            PanelUi.BindClick(_sellExecuteRoot, ExecuteTrade);

            // The station's discount widget sits in the buy pane footer,
            // between the pager and the action buttons; its full height is
            // kept so the decorative bars stay visible.
            _extraChargeLabel = PanelUi.CreateText("ExtraCharge",
                footer.transform, 0f, 0f, ExtraChargeW, FooterH,
                TextContext.IgnoreSize, 8f, PanelUi.AccentColor,
                TextAlignmentOptions.MidlineLeft);
            _extraChargeLabel.gameObject.SetActive(false);
            TryAttachExtraChargeView();
            LayoutFooter();
        }

        // Footer geometry, expressed as distances from the footer's RIGHT
        // edge. At the 628px design width these reproduce the original fixed
        // positions exactly; on a wider panel the whole cluster travels with
        // the edge and the running-total text absorbs the difference.
        // Control widths, shared by BuildFooter and LayoutFooter. They used
        // to be written twice -- literals at build time, literals again in the
        // layout pass -- so the two could disagree and only the layout pass
        // would win.
        private const float PageArrowW = 14f;
        private const float PageLabelW = 34f;
        private const float ClearButtonW = 54f;
        private const float BuyExecuteW = 76f;
        private const float SellSelectAllW = 60f;
        private const float SellExecuteW = 80f;
        private const float ExtraChargeW = 110f;
        private const float SummaryMinW = 60f;

        private const float PagePreviousInset = 326f;
        private const float PageLabelInset = 310f;
        private const float PageNextInset = 274f;
        private const float ExtraChargeInset = 254f;
        private const float BuyClearInset = 132f;
        private const float BuyExecuteInset = 76f;
        private const float SellSelectAllInset = 198f;
        private const float SellClearInset = 136f;
        private const float SellExecuteInset = 80f;

        private void LayoutFooter()
        {
            if (_footerRoot == null)
                return;
            var footerW = _panelW - 8f;
            PanelUi.SetTopLeft((RectTransform)_footerRoot.transform,
                4f, _footerY, footerW, FooterH);

            // Everything left of the pager is running text; it gets whatever
            // the cluster does not need.
            var summaryW = Mathf.Max(SummaryMinW,
                footerW - PagePreviousInset - 4f);
            SetFooterRect(_buySummaryLabel, 2f, summaryW);
            SetFooterRect(_lowReputationLabel, 2f, summaryW);
            SetFooterRect(_sellSummaryLabel, 2f, summaryW);
            SetFooterRect(_sellNoticeLabel, 2f, summaryW);

            SetFooterRect(_previousPageRoot, footerW - PagePreviousInset,
                PageArrowW);
            SetFooterRect(_pageLabel, footerW - PageLabelInset, PageLabelW);
            SetFooterRect(_nextPageRoot, footerW - PageNextInset, PageArrowW);
            SetFooterRect(_extraChargeLabel, footerW - ExtraChargeInset,
                ExtraChargeW);
            SetFooterRect(_buyClearRoot, footerW - BuyClearInset, ClearButtonW);
            SetFooterRect(_buyExecuteRoot, footerW - BuyExecuteInset, BuyExecuteW);
            SetFooterRect(_sellSelectAllRoot,
                footerW - SellSelectAllInset, SellSelectAllW);
            SetFooterRect(_sellClearRoot, footerW - SellClearInset, ClearButtonW);
            SetFooterRect(_sellExecuteRoot, footerW - SellExecuteInset, SellExecuteW);

            // The cloned vanilla discount widget keeps its own height and is
            // bottom-anchored, so it is placed by its measured size rather
            // than stamped into a 17px box like the mod's own labels.
            if (_extraChargeView != null)
            {
                var viewRect = (RectTransform)_extraChargeView.transform;
                viewRect.anchoredPosition = new Vector2(
                    footerW - ExtraChargeInset,
                    FooterH * 0.5f - viewRect.sizeDelta.y * 0.5f);
            }
        }

        private void SetFooterRect(TextMeshProUGUI label, float x, float width)
        {
            if (label != null)
                PanelUi.SetTopLeft(label.rectTransform, x, 0f, width, FooterH);
        }

        private void SetFooterRect(GameObject root, float x, float width)
        {
            if (root != null)
            {
                PanelUi.SetTopLeft((RectTransform)root.transform, x, 0f,
                    width, FooterH);
            }
        }

        private TradeRow BuildSlot(RectTransform well, int index)
        {
            var row = new TradeRow();
            var root = PanelUi.CreateUiObject("Row" + index, well);
            var rect = (RectTransform)root.transform;
            PanelUi.SetTopLeft(rect, 2f, SlotsTop - index * RowH, _rowW, RowH);
            row.Root = root;
            VanillaSkin.Slice(root, VanillaSkin.ListBackground,
                PanelUi.CardColor).raycastTarget = true;

            var iconRoot = PanelUi.CreateUiObject("Icon", root.transform);
            PanelUi.SetTopLeft((RectTransform)iconRoot.transform,
                3f, -2f, 18f, 18f);
            row.Icon = iconRoot.AddComponent<Image>();
            row.Icon.preserveAspect = true;
            row.Icon.raycastTarget = true;
            // The item tooltip lives on the icon alone: a small hover target
            // that does not cover the click/sweep strip and never gets in
            // the way of the page wheel.
            iconRoot.AddComponent<ItemTooltipHandler>();

            row.Name = PanelUi.CreateText("Name", root.transform,
                NameX, 0f, _nameW, RowH, TextContext.IgnoreSize, 7f,
                PanelUi.BrightColor, TextAlignmentOptions.MidlineLeft);
            row.Stock = PanelUi.CreateText("Stock", root.transform,
                _stockX, 0f, StockW, RowH, TextContext.IgnoreSize, 7f,
                PanelUi.ValueColor, TextAlignmentOptions.MidlineRight);
            row.Price = PanelUi.CreateText("Price", root.transform,
                _priceX, 0f, PriceW, RowH, TextContext.IgnoreSize, 7f,
                PanelUi.AccentColor, TextAlignmentOptions.MidlineRight);
            row.Subtotal = PanelUi.CreateText("Subtotal", root.transform,
                _subtotalX, 0f, SubtotalW, RowH, TextContext.IgnoreSize, 7f,
                PanelUi.ValueColor, TextAlignmentOptions.MidlineRight);
            row.Badge = PanelUi.CreateText("Badge", root.transform,
                _subtotalX, 0f, SubtotalW, RowH, TextContext.IgnoreSize, 7f,
                PanelUi.OffColor, TextAlignmentOptions.MidlineRight);
            row.Badge.gameObject.SetActive(false);

            // The name strip is the row's interaction zone (click to type,
            // right-click / drag-sweep to MAX); the item tooltip stays on
            // the icon only, so hovering here never blocks wheel paging.
            var catcher = PanelUi.CreateUiObject("Catcher", root.transform);
            row.Catcher = catcher;
            PanelUi.SetTopLeft((RectTransform)catcher.transform,
                NameX, 0f, _stockX - NameX - 4f, RowH);
            VanillaSkin.AddHint(catcher, "qmtrade.tip.row");
            var catcherImage = catcher.AddComponent<Image>();
            catcherImage.color = new Color(0f, 0f, 0f, 0f);
            catcherImage.raycastTarget = true;
            // EventTrigger stops scroll bubbling, so the page wheel must
            // live on the same object to keep the wheel working here.
            var catcherWheel = catcher.AddComponent<TradePageWheel>();
            catcherWheel.Panel = this;

            var trigger = catcher.AddComponent<EventTrigger>();
            var clickEntry = new EventTrigger.Entry
            {
                eventID = EventTriggerType.PointerClick,
                callback = new EventTrigger.TriggerEvent(),
            };
            clickEntry.callback.AddListener(data =>
            {
                CloseDropdowns();
                if (SweepClickSuppressed)
                    return;
                if (!(data is PointerEventData pointer)
                    || string.IsNullOrEmpty(row.ItemId))
                {
                    return;
                }
                if (pointer.button == PointerEventData.InputButton.Right)
                {
                    AdjustQuantity(row.ItemId, _pane == Pane.Sell,
                        int.MaxValue);
                    PanelUi.PlayVanillaButtonClick();
                    return;
                }
                // A plain click focuses the quantity box for direct typing.
                if (pointer.button == PointerEventData.InputButton.Left
                    && row.Input.interactable)
                {
                    row.Input.ActivateInputField();
                }
            });
            trigger.triggers = new List<EventTrigger.Entry>
            {
                clickEntry,
            };

            var qtyRoot = PanelUi.CreateUiObject("Quantity", root.transform);
            row.QtyRoot = qtyRoot;
            PanelUi.SetTopLeft((RectTransform)qtyRoot.transform,
                _quantityX, -2f, QuantityW, 16f);
            // Hold left on any editor button (+ included) and slide down a
            // column: every row swept gets the modifier step (or MAX).
            AddSweepHandlers(qtyRoot);
            // Same scroll-bubbling note as the catcher: the page wheel must
            // sit on the EventTrigger object itself.
            var qtyPageWheel = qtyRoot.AddComponent<TradePageWheel>();
            qtyPageWheel.Panel = this;

            row.MinusRoot = BuildStepButton("Minus", qtyRoot.transform,
                0f, 0f, 18f, 16f, "qmtrade.tip.minus", "-",
                () => AdjustQuantity(row.ItemId, _pane == Pane.Sell,
                    -ModifierStep()));
            row.Minus = PanelUi.ButtonOf(row.MinusRoot);
            var minusWheel = row.MinusRoot.AddComponent<TradePageWheel>();
            minusWheel.Panel = this;

            row.InputRoot = PanelUi.CreateIntegerInput("Input",
                qtyRoot.transform, 19f, 0f, 46f, 16f,
                out row.Input, out row.InputText);
            // The input stops the scroll chain (TMP_InputField implements
            // IScrollHandler), so it carries its own page wheel.
            var wheel = row.InputRoot.AddComponent<TradePageWheel>();
            wheel.Panel = this;
            row.Input.onEndEdit.AddListener(value =>
            {
                ModInputGate.BlockThisFrame();
                if (string.IsNullOrEmpty(row.ItemId))
                    return;
                var qty = 0;
                if (int.TryParse(value, out var parsed) && parsed > 0)
                    qty = parsed;
                var full = _pane == Pane.Sell ? _sellOwnedMap : _buyStockMap;
                var available = full.TryGetValue(row.ItemId, out var count)
                    ? count
                    : 0;
                SetQuantity(row.ItemId, _pane == Pane.Sell,
                    Mathf.Clamp(qty, 0, available));
            });
            row.Input.onDeselect.AddListener(_ =>
                ModInputGate.BlockThisFrame());

            row.PlusRoot = BuildStepButton("Plus", qtyRoot.transform,
                66f, 0f, 18f, 16f, "qmtrade.tip.plus", "+",
                () => AdjustQuantity(row.ItemId, _pane == Pane.Sell,
                    ModifierStep()));
            row.Plus = PanelUi.ButtonOf(row.PlusRoot);
            var plusWheel = row.PlusRoot.AddComponent<TradePageWheel>();
            plusWheel.Panel = this;

            row.MaxRoot = PanelUi.CreateButtonRoot("Max", qtyRoot.transform,
                85f, 0f, 44f, 16f, "qmtrade.tip.max",
                out _, out var maxLabel);
            maxLabel.text = "MAX";
            row.Max = PanelUi.ButtonOf(row.MaxRoot);
            PanelUi.BindClick(row.MaxRoot, () =>
            {
                CloseDropdowns();
                // The release of a sweep press must not also click.
                if (SweepClickSuppressed)
                    return;
                AdjustQuantity(row.ItemId, _pane == Pane.Sell, int.MaxValue);
            });

            // Every row pages on its own wheel, so the wheel works over the
            // name strip and buttons too -- the quantity wheel on the input
            // box is deeper in the hierarchy and consumes the event first.
            var pageWheel = root.AddComponent<TradePageWheel>();
            pageWheel.Panel = this;

            // The stock / price / subtotal columns also start the sweep, so
            // sliding down a column works from anywhere on a row.
            AddSweepHandlers(root);

            row.Root.SetActive(false);
            return row;
        }

        /// <summary>
        /// Builds a +/- button whose step depends on held modifiers:
        /// plain click = 1, Ctrl = QuantityCtrlStep, Ctrl+Shift =
        /// QuantityCtrlShiftStep. The click is handled through an EventTrigger
        /// so the modifiers can be read at click time; CommonButton still
        /// owns the visuals and the click sound.
        /// </summary>
        private GameObject BuildStepButton(string name, Transform parent,
            float x, float y, float width, float height, string tooltipTag,
            string caption, Action onClick)
        {
            var root = PanelUi.CreateButtonRoot(name, parent, x, y, width,
                height, tooltipTag, out _, out var label);
            label.text = caption;
            var trigger = root.GetComponent<EventTrigger>()
                          ?? root.AddComponent<EventTrigger>();
            var entry = new EventTrigger.Entry
            {
                eventID = EventTriggerType.PointerClick,
                callback = new EventTrigger.TriggerEvent(),
            };
            entry.callback.AddListener(data =>
            {
                if (data is PointerEventData pointer
                    && pointer.button == PointerEventData.InputButton.Left)
                {
                    CloseDropdowns();
                    // The release of a sweep press must not also click.
                    if (SweepClickSuppressed)
                        return;
                    onClick();
                }
            });
            if (trigger.triggers == null)
                trigger.triggers = new List<EventTrigger.Entry>();
            trigger.triggers.Add(entry);
            return root;
        }

        private static int ModifierStep()
        {
            var ctrl = Input.GetKey(KeyCode.LeftControl)
                       || Input.GetKey(KeyCode.RightControl);
            var shift = Input.GetKey(KeyCode.LeftShift)
                        || Input.GetKey(KeyCode.RightShift);
            if (ctrl && shift)
                return Plugin.QuantityCtrlShiftStep;
            if (ctrl)
                return Plugin.QuantityCtrlStep;
            if (shift)
                return Plugin.QuantityShiftStep;
            return 1;
        }

        /// <summary>
        /// The sweep itself is driven from Update (raw mouse state), so the
        /// EventTriggers only carry a click entry that closes open dropdowns
        /// -- an EventTrigger stops event bubbling, and the panel-wide close
        /// handler would never see these clicks otherwise.
        /// </summary>
        private void AddSweepHandlers(GameObject target)
        {
            var trigger = target.GetComponent<EventTrigger>()
                          ?? target.AddComponent<EventTrigger>();
            var click = new EventTrigger.Entry
            {
                eventID = EventTriggerType.PointerClick,
                callback = new EventTrigger.TriggerEvent(),
            };
            click.callback.AddListener(_ => CloseDropdowns());
            if (trigger.triggers == null)
                trigger.triggers = new List<EventTrigger.Entry>();
            trigger.triggers.Add(click);
        }

        // ------------------------------------------------------------------
        // Index, filtering and slots.
        // ------------------------------------------------------------------

        private void RefreshAll()
        {
            BuildIndex();
            ClampWallets();
            RefreshSlots();
            RefreshChrome();
            InvalidateTotals();
            RefreshTotals();
        }

        private void RefreshList()
        {
            BuildIndex();
            ClampWallets();
            RefreshSlots();
            RefreshChrome();
            InvalidateTotals();
            RefreshTotals();
        }

        /// <summary>
        /// Only the ACTIVE pane's cart is clamped: its availability map was
        /// just rebuilt, and the other pane's cart must survive browsing
        /// (it is clamped again the next time that pane opens). Entries are
        /// clamped against the FULL availability maps, not the filtered
        /// index: hiding a row with search or category must not silently
        /// reset or silently trade hidden quantities.
        /// </summary>
        private void ClampWallets()
        {
            if (_pane == Pane.Buy)
                ClampWallet(_buyWallet, _buyStockMap);
            else
                ClampWallet(_sellWallet, _sellOwnedMap);
        }

        private static void ClampWallet(Dictionary<string, int> wallet,
            Dictionary<string, int> full)
        {
            foreach (var id in new List<string>(wallet.Keys))
            {
                var qty = wallet[id];
                if (qty <= 0
                    || !full.TryGetValue(id, out var available)
                    || available <= 0)
                {
                    wallet.Remove(id);
                    continue;
                }
                if (qty > available)
                    wallet[id] = available;
            }
        }

        private void BuildIndex()
        {
            _index.Clear();
            _buyStockMap.Clear();
            _sellOwnedMap.Clear();

            if (_pane == Pane.Buy)
            {
                // Vanilla hides the station's fixed-supply consumables
                // (ammo, meds, food...) from the shop -- which also makes
                // items you just sold disappear. Everything the station
                // actually holds is listed instead, so sold goods can always
                // be bought back; consumables use the station's own price.
                foreach (var item in _station.InternalStorage.Items)
                {
                    if (_buyStockMap.TryGetValue(item.Id, out var count))
                        _buyStockMap[item.Id] = count + item.StackCount;
                    else
                        _buyStockMap[item.Id] = item.StackCount;
                }
                foreach (var pair in _buyStockMap)
                {
                    _index.Add(new TradeEntry
                    {
                        ItemId = pair.Key,
                        Available = pair.Value,
                        UnitPrice = TradeSystem.GetItemBuyPrice(_progression,
                            _faction, _station, _itemsPrices, pair.Key),
                        CanTrade = _canTrade,
                        MaxStack = MaxStackOf(pair.Key),
                    });
                }
            }
            else
            {
                // Everything the ship holds is on the table at once; the
                // category chips do the separating.
                _sellStorages.Clear();
                _sellStorages.AddRange(BuildSellStorages());
                foreach (var storage in _sellStorages)
                {
                    foreach (var item in storage.Items)
                    {
                        if (_sellOwnedMap.TryGetValue(item.Id, out var count))
                            _sellOwnedMap[item.Id] = count + item.StackCount;
                        else
                            _sellOwnedMap[item.Id] = item.StackCount;
                    }
                }
                foreach (var pair in _sellOwnedMap)
                {
                    // Only items the station actually accepts are listed --
                    // everything on the sell pane is sellable.
                    if (!TradeSystem.IsValidItem(_faction, _station, pair.Key))
                        continue;
                    // The AnCom data chip pays no trade points; vanilla only
                    // rewards reputation and story progress for it.
                    var questItem = pair.Key.Equals(
                        Data.StoryVars.TutorialAncomQuestItemId);
                    _index.Add(new TradeEntry
                    {
                        ItemId = pair.Key,
                        Available = pair.Value,
                        UnitPrice = questItem
                            ? 0
                            : Mathf.RoundToInt(
                                TradeSystem.GetItemSellPrice(_progression,
                                    _faction, _station, _itemsPrices, pair.Key)
                                * _difficulty.Preset.BarterValue),
                        CanTrade = true,
                        MaxStack = MaxStackOf(pair.Key),
                    });
                }
            }

            ApplyFilterAndSort();
        }

        private void ApplyFilterAndSort()
        {
            if (_category != TradeCategory.All)
            {
                _index.RemoveAll(entry =>
                    CategoryOf(entry.ItemId) != _category);
            }
            if (!string.IsNullOrEmpty(_searchText))
            {
                var query = _searchText.Trim();
                _index.RemoveAll(entry =>
                    !MatchesSearch(entry.ItemId, query));
            }
            switch (_sort)
            {
                case TradeSort.Name:
                    _index.Sort((a, b) => string.Compare(
                        ItemNameOf(a.ItemId), ItemNameOf(b.ItemId),
                        StringComparison.OrdinalIgnoreCase));
                    break;
                case TradeSort.PriceAscending:
                    _index.Sort((a, b) =>
                        a.UnitPrice.CompareTo(b.UnitPrice));
                    break;
                case TradeSort.PriceDescending:
                    _index.Sort((a, b) =>
                        b.UnitPrice.CompareTo(a.UnitPrice));
                    break;
                case TradeSort.QuantityDescending:
                    _index.Sort((a, b) =>
                        b.Available.CompareTo(a.Available));
                    break;
            }
        }

        private static bool MatchesSearch(string itemId, string query)
        {
            return ItemNameOf(itemId).IndexOf(query,
                       StringComparison.OrdinalIgnoreCase) >= 0
                   || itemId.IndexOf(query,
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string ItemNameOf(string itemId)
        {
            return Localization.Get("item." + itemId + ".name");
        }

        private static int MaxStackOf(string itemId)
        {
            if (StaticMaxStack.TryGetValue(itemId, out var cached))
                return cached;
            var sample = SingletonMonoBehaviour<ItemFactory>.Instance
                .CreateForInventory(itemId);
            var maxStack = 1;
            if (sample != null && sample.IsStackable)
                maxStack = sample.MaxStack;
            StaticMaxStack[itemId] = maxStack;
            return maxStack;
        }

        private static TradeCategory CategoryOf(string itemId)
        {
            var record = Data.Items.GetSimpleRecord<ItemRecord>(itemId);
            var cls = record?.ItemClass ?? ItemClass.None;
            switch (cls)
            {
                case ItemClass.Weapon:
                case ItemClass.ThrowableWeapon:
                case ItemClass.Grenade:
                case ItemClass.Mine:
                case ItemClass.Turret:
                    return TradeCategory.Weapons;
                case ItemClass.Helmet:
                case ItemClass.Armor:
                case ItemClass.Leggings:
                case ItemClass.Boots:
                case ItemClass.Backpack:
                case ItemClass.Vest:
                    return TradeCategory.Armor;
                case ItemClass.Ammo:
                    return TradeCategory.Ammo;
                case ItemClass.Pills:
                case ItemClass.Syringe:
                case ItemClass.Medpack:
                case ItemClass.Dressing:
                    return TradeCategory.Medical;
                case ItemClass.Food:
                case ItemClass.Drink:
                case ItemClass.Alcohol:
                    return TradeCategory.Food;
                case ItemClass.Parts:
                case ItemClass.RepairKit:
                case ItemClass.MilitaryBarter:
                case ItemClass.ScienceBarter:
                case ItemClass.IndustrialBarter:
                case ItemClass.ValuableBarter:
                case ItemClass.Cyborg:
                case ItemClass.QuasiArtefact:
                case ItemClass.QuasiPact:
                case ItemClass.QuasiOrgan:
                case ItemClass.Data:
                case ItemClass.Blueprint:
                case ItemClass.Organ:
                case ItemClass.BioAug:
                case ItemClass.CyberneticAug:
                case ItemClass.QuasiAug:
                case ItemClass.PlaceableObstacle:
                case ItemClass.Key:
                    return TradeCategory.Materials;
                default:
                    return TradeCategory.Other;
            }
        }

        private static string CategoryLabelOf(TradeCategory category)
        {
            return Localization.Get(
                "qmtrade.cat." + category.ToString().ToLowerInvariant());
        }

        private static string SortLabelOf(TradeSort sort)
        {
            return Localization.Get(
                "qmtrade.sort." + sort.ToString().ToLowerInvariant());
        }

        /// <summary>
        /// Fills the fixed slots from the current page. The expensive part of
        /// the panel (creating rows) happens once; every refresh only writes
        /// text into 13 prebuilt slots.
        /// </summary>
        private void RefreshSlots()
        {
            // The page size changes with the design space, so a scroll
            // position carried over from a taller layout has to be re-seated
            // on a page boundary before anything is filled.
            _scrollRow = Mathf.Clamp(
                _scrollRow / _visibleRows * _visibleRows, 0,
                Mathf.Max(0,
                    (Mathf.Max(1, _index.Count) - 1) / _visibleRows
                    * _visibleRows));
            for (var i = 0; i < _slots.Length; i++)
            {
                var slot = _slots[i];
                if (slot == null)
                    continue;
                if (i >= _visibleRows)
                {
                    slot.ItemId = null;
                    slot.Entry = null;
                    slot.Root.SetActive(false);
                    continue;
                }
                var index = _scrollRow + i;
                if (index < _index.Count)
                {
                    var entry = _index[index];
                    slot.ItemId = entry.ItemId;
                    slot.Entry = entry;
                    slot.Root.SetActive(true);
                    FillRow(slot);
                }
                else
                {
                    slot.ItemId = null;
                    slot.Entry = null;
                    slot.Root.SetActive(false);
                }
            }
            UpdatePageLabel();
        }

        private void UpdatePageLabel()
        {
            if (_pageLabel == null)
                return;
            _pageLabel.text = CurrentPage + "/" + PageCount;
            _previousPageButton?.SetInteractable(CurrentPage > 1);
            _nextPageButton?.SetInteractable(CurrentPage < PageCount);
        }

        private void FillRow(TradeRow row)
        {
            var entry = row.Entry;
            var tooltip = row.Root.GetComponentInChildren<ItemTooltipHandler>(
                true);
            if (tooltip != null)
                tooltip.Initialize(entry.ItemId);

            row.Name.text = ItemNameOf(entry.ItemId);
            // The column header explains what this number is (the station's
            // stock on the buy pane, ours on the sell pane).
            row.Stock.text = entry.Available.ToString();
            row.Price.text = entry.UnitPrice.ToString();

            var record = Data.Items.GetRecord(entry.ItemId);
            if (record != null && record.ItemDesc != null)
            {
                row.Icon.sprite = SingletonMonoBehaviour<ItemFactory>.Instance
                    .ResolveIcon(record.ItemDesc, 1);
            }

            row.Name.color = PanelUi.BrightColor;
            row.Stock.color = PanelUi.ValueColor;
            row.Price.color = PanelUi.AccentColor;
            RefreshRowLabel(row);
        }

        private void RefreshRowLabels()
        {
            for (var i = 0; i < _slots.Length; i++)
            {
                var row = _slots[i];
                if (row != null && !string.IsNullOrEmpty(row.ItemId))
                    RefreshRowLabel(row);
            }
        }

        private void RefreshRowLabel(TradeRow row)
        {
            if (row == null || row.Entry == null)
                return;
            var sell = _pane == Pane.Sell;
            var wallet = WalletOf(sell);
            var qty = wallet.TryGetValue(row.ItemId, out var value) ? value : 0;
            row.Input.SetTextWithoutNotify(qty.ToString());
            row.InputText.color =
                qty == 0 ? Colors.Green : PanelUi.ValueColor;

            var editable = sell ? row.Entry.CanTrade : _canTrade;
            row.Input.interactable = editable;
            row.Minus.SetInteractable(editable && qty > 0);
            row.Plus.SetInteractable(editable && qty < row.Entry.Available);
            row.Max.SetInteractable(editable && qty < row.Entry.Available);

            if (sell
                && row.ItemId.Equals(Data.StoryVars.TutorialAncomQuestItemId))
            {
                row.Badge.text = Localization.Get("qmtrade.quest");
                row.Badge.gameObject.SetActive(true);
                row.Subtotal.gameObject.SetActive(false);
                return;
            }
            row.Badge.gameObject.SetActive(false);
            row.Subtotal.gameObject.SetActive(true);
            row.Subtotal.text =
                (qty * row.Entry.UnitPrice).ToString();
        }

        // ------------------------------------------------------------------
        // Chrome and totals.
        // ------------------------------------------------------------------

        private void RefreshChrome()
        {
            if (_root == null)
                return;

            PanelUi.SetSurfaceSelected(_tabBuyBg, _pane == Pane.Buy);
            PanelUi.SetSurfaceSelected(_tabSellBg, _pane == Pane.Sell);
            _sortLabel.text = SortLabelOf(_sort);
            if (_headerStock != null)
            {
                _headerStock.text = Localization.Get(_pane == Pane.Sell
                    ? "qmtrade.header.stock_sell"
                    : "qmtrade.header.stock_buy");
            }

            foreach (var tab in _categoryTabs)
            {
                PanelUi.SetSurfaceSelected(tab.Background,
                    tab.Category == _category);
            }

            var filtered = HasActiveFilter();
            var empty = _index.Count == 0;
            _emptyLabel.gameObject.SetActive(empty);
            if (empty)
            {
                _emptyLabel.text = filtered
                    ? Localization.Get("qmtrade.empty_filtered")
                    : (_pane == Pane.Buy
                        ? Localization.Get("qmtrade.empty_buy")
                        : Localization.Get("qmtrade.empty_sell"));
            }
            _clearFiltersRoot.SetActive(empty && filtered);

            var buyLowRep = _pane == Pane.Buy && !_canTrade;
            _buySummaryLabel.gameObject.SetActive(_pane == Pane.Buy
                                                  && !buyLowRep);
            _buyClearRoot.SetActive(_pane == Pane.Buy && !buyLowRep);
            _buyExecuteRoot.SetActive(_pane == Pane.Buy && !buyLowRep);
            // The discount belongs to buying: it lives in the buy footer.
            var chargeVisible = _pane == Pane.Buy && !buyLowRep;
            if (_extraChargeRoot != null)
                _extraChargeRoot.SetActive(chargeVisible);
            if (_extraChargeView == null && _extraChargeLabel != null)
                _extraChargeLabel.gameObject.SetActive(chargeVisible);
            _lowReputationLabel.gameObject.SetActive(buyLowRep);
            if (buyLowRep)
                _lowReputationLabel.text =
                    Localization.Get("qmtrade.low_reputation_buy");

            var sellLowRep = _pane == Pane.Sell && !_canSell;
            _sellSelectAllRoot.SetActive(_pane == Pane.Sell && !sellLowRep);
            _sellClearRoot.SetActive(_pane == Pane.Sell && !sellLowRep);
            _sellExecuteRoot.SetActive(_pane == Pane.Sell && !sellLowRep);
            _sellSummaryLabel.gameObject.SetActive(_pane == Pane.Sell
                                                  && !sellLowRep);
            _sellNoticeLabel.gameObject.SetActive(sellLowRep);
            if (sellLowRep)
                _sellNoticeLabel.text =
                    Localization.Get("qmtrade.low_reputation_sell");
        }

        private bool HasActiveFilter()
        {
            return _category != TradeCategory.All
                   || !string.IsNullOrEmpty(_searchText);
        }

        /// <summary>
        /// The cart changed. The money is recomputed on the next frame rather
        /// than here, so a sweep across twenty rows costs one recompute, not
        /// twenty.
        /// </summary>
        private void InvalidateTotals()
        {
            _totalsDirty = true;
        }

        private void RefreshTotals()
        {
            if (!IsPanelActive || _root == null || !_root.activeInHierarchy)
                return;
            RefreshTradeFooter();
        }

        /// <summary>
        /// The header trade-points readout: the game's own ExchangeView shows
        /// "points (+delta)", where the delta is the combined cart -- sell
        /// earnings minus purchase costs, on both panes.
        /// </summary>
        private void RefreshPointsLabel(int delta)
        {
            if (_faction == null)
                return;
            var points = _faction.PlayerTradePoints;
            if (_exchangeView != null)
            {
                _exchangeView.RefreshValue(points, delta);
                return;
            }
            if (_pointsLabel == null)
                return;
            // Same per-frame caller as the summary above: only reformat when
            // the value moved.
            if (!_pointsValid || _pointsShown != points)
            {
                _pointsValid = true;
                _pointsShown = points;
                _pointsLabel.text = Localization.Get("qmtrade.points")
                    .SafeFormat(points);
            }
            _pointsLabel.color = PanelUi.AccentColor;
        }

        /// <summary>
        /// One footer for both panes: the sell cart and the buy cart are
        /// pending together, the summary shows the combined deal, and the
        /// single TRADE button completes everything at once.
        /// </summary>
        private void RefreshTradeFooter()
        {
            if (_root == null)
                return;

            // The prices are the expensive half: GetBuyPrice walks the cart and
            // EstimateSellGain calls GetItemSellPrice once per line, then splits
            // each line into whole stacks to match vanilla's per-stack rounding.
            // None of that can change without the cart, the pane or a completed
            // trade changing, so it runs on demand -- it used to run sixty times
            // a second because the TRADE button also tracks the drag state,
            // which has no change event. Only that last part is per-frame now.
            if (_totalsDirty)
            {
                _totalsDirty = false;
                _cachedBuyTotal = _canTrade
                    ? TradeSystem.GetBuyPrice(_progression, _factions,
                        _itemsPrices, _station, _buyWallet)
                    : 0;
                _cachedSellEstimate = _canSell ? EstimateSellGain() : 0;
            }
            var buyTotal = _cachedBuyTotal;
            var sellEstimate = _cachedSellEstimate;

            var after = _faction.PlayerTradePoints + sellEstimate - buyTotal;
            var hasSell = _canSell && _sellWallet.Count > 0;
            var hasBuy = _canTrade && _buyWallet.Count > 0;
            var cartEmpty = !hasSell && !hasBuy;
            var affordable = _faction.PlayerTradePoints + sellEstimate
                             >= buyTotal;

            var color = cartEmpty
                ? PanelUi.OffColor
                : (affordable ? PanelUi.ValueColor : PanelUi.DangerColor);
            // The formatted string is memoised separately: the numbers above
            // can be recomputed to the same values (a clamp, a refused trade)
            // without the sentence needing to be rebuilt.
            if (!_summaryValid
                || _summarySell != sellEstimate
                || _summaryBuy != buyTotal
                || _summaryAfter != after)
            {
                _summaryValid = true;
                _summarySell = sellEstimate;
                _summaryBuy = buyTotal;
                _summaryAfter = after;
                var summary = string.Format(
                    Localization.Get("qmtrade.trade_summary"),
                    sellEstimate, buyTotal, after);
                if (_buySummaryLabel != null)
                    _buySummaryLabel.text = summary;
                if (_sellSummaryLabel != null)
                    _sellSummaryLabel.text = summary;
            }
            // Colour is a plain field compare inside TMP, so it stays
            // unconditional and cannot go stale.
            if (_buySummaryLabel != null)
                _buySummaryLabel.color = color;
            if (_sellSummaryLabel != null)
                _sellSummaryLabel.color = color;

            var ready = !cartEmpty && affordable && !UI.Drag.IsDragging;
            if (_buyExecuteButton != null)
                _buyExecuteButton.SetInteractable(ready);
            if (_sellExecuteButton != null)
                _sellExecuteButton.SetInteractable(ready);
            RefreshPointsLabel(sellEstimate - buyTotal);
        }

        /// <summary>
        /// Vanilla pays per physical stack, rounding after multiplying by the
        /// stack count, so the preview splits the requested amount into full
        /// stacks plus a remainder and rounds each piece the same way.
        /// </summary>
        private int EstimateSellGain()
        {
            var gain = 0;
            foreach (var pair in _sellWallet)
            {
                if (pair.Value <= 0
                    || pair.Key.Equals(Data.StoryVars.TutorialAncomQuestItemId))
                {
                    continue;
                }
                var unit = TradeSystem.GetItemSellPrice(_progression, _faction,
                    _station, _itemsPrices, pair.Key);
                var maxStack = Mathf.Max(1, MaxStackOf(pair.Key));
                var full = pair.Value / maxStack;
                var rem = pair.Value % maxStack;
                gain += full * Mathf.RoundToInt(
                    unit * maxStack * _difficulty.Preset.BarterValue);
                if (rem > 0)
                {
                    gain += Mathf.RoundToInt(
                        unit * rem * _difficulty.Preset.BarterValue);
                }
            }
            return gain;
        }

        // ------------------------------------------------------------------
        // Selection and navigation.
        // ------------------------------------------------------------------

        private void SelectPane(Pane pane)
        {
            if (_pane == pane)
                return;
            _pane = pane;
            _scrollRow = 0;
            CloseDropdowns();
            RefreshList();
        }

        private void SelectCategory(TradeCategory category)
        {
            if (_category == category)
                return;
            _category = category;
            RefreshList();
        }

        private void SelectSort(TradeSort sort)
        {
            if (_sort == sort)
                return;
            _sort = sort;
            RefreshList();
        }

        private void ClearAllFilters()
        {
            _category = TradeCategory.All;
            _searchText = string.Empty;
            _scrollRow = 0;
            if (_search != null)
                _search.SetTextWithoutNotify(string.Empty);
            RefreshList();
        }

        private void SelectAllVisible()
        {
            foreach (var entry in _index)
            {
                if (entry.CanTrade && entry.Available > 0)
                    _sellWallet[entry.ItemId] = entry.Available;
            }
            RefreshRowLabels();
            InvalidateTotals();
            RefreshTotals();
        }

        private void ClosePanel()
        {
            UI.BackToDefault();
        }

        // ------------------------------------------------------------------
        // Sort dropdown.
        // ------------------------------------------------------------------

        private void ToggleSortDropdown()
        {
            if (_sortDropdownRoot != null
                && _sortDropdownRoot.activeSelf)
            {
                CloseDropdowns();
                return;
            }
            CloseDropdowns();
            RebuildSortDropdown();
            _blockedDropdownCloseFrame = Time.frameCount;
            // The dropdown must draw above the list built after it.
            _sortDropdownRoot.transform.SetAsLastSibling();
            _sortDropdownRoot.SetActive(true);
        }

        private void RebuildSortDropdown()
        {
            ClearDropdownRows(_sortDropdownRows);
            var values = (TradeSort[])Enum.GetValues(typeof(TradeSort));
            var height = values.Length * RowH + 4f;
            var rect = (RectTransform)_sortDropdownRoot.transform;
            rect.sizeDelta = new Vector2(rect.sizeDelta.x, height);
            for (var i = 0; i < values.Length; i++)
            {
                var sort = values[i];
                var root = PanelUi.CreateButtonRoot("Option" + sort,
                    _sortDropdownRoot.transform, 2f, -2f - i * RowH,
                    90f, RowH, out _, out var label);
                label.text = SortLabelOf(sort);
                PanelUi.BindClick(root, () =>
                {
                    SelectSort(sort);
                    CloseDropdowns();
                });
                _sortDropdownRows.Add(root);
            }
        }

        private void CloseDropdowns()
        {
            if (_sortDropdownRoot != null)
                _sortDropdownRoot.SetActive(false);
        }

        private static void ClearDropdownRows(List<GameObject> rows)
        {
            foreach (var row in rows)
            {
                if (row != null)
                {
                    row.SetActive(false);
                    Destroy(row);
                }
            }
            rows.Clear();
        }

        private List<ItemStorage> BuildSellStorages()
        {
            var list = new List<ItemStorage>();
            for (var i = 0; i < _cargo.ShipCargo.Count; i++)
                list.Add(_cargo.ShipCargo[i]);
            if (_progression.HasStoreFridge)
                list.Add(_cargo.FridgeStorage);
            // Vanilla cannot trade from the recycling storage while a batch
            // is in progress, so those items stay out of the sell list.
            if (_progression.HasStoreConstructorDepartment
                && !_cargo.RecyclingInProgress)
            {
                list.Add(_cargo.RecyclingStorage);
            }
            return list;
        }

        // ------------------------------------------------------------------
        // Trade execution. All game logic is delegated to TradeSystem /
        // MagnumCargoSystem, mirroring what the vanilla pages do.
        // ------------------------------------------------------------------

        private ItemStorage ActiveCargo()
        {
            return Plugin.ActiveCargoOf(_screen) ?? _activeCargo;
        }

        private void ExecuteTrade()
        {
            if (!IsPanelActive || UI.Drag.IsDragging)
                return;
            ClampWallets();
            var buyTotal = _canTrade
                ? TradeSystem.GetBuyPrice(_progression, _factions,
                    _itemsPrices, _station, _buyWallet)
                : 0;
            var sellEstimate = _canSell ? EstimateSellGain() : 0;
            var hasSell = _canSell && _sellWallet.Count > 0;
            var hasBuy = _canTrade && _buyWallet.Count > 0;
            if (!hasSell && !hasBuy)
            {
                PlayEmptyAttack();
                return;
            }
            // Sells are completed first, so their earnings can pay for the
            // purchases in the same trade.
            if (hasBuy
                && _faction.PlayerTradePoints + sellEstimate < buyTotal)
            {
                PlayEmptyAttack();
                return;
            }
            if (Plugin.TradeConfirm)
            {
                var details = string.Format(
                    Localization.Get("qmtrade.dialog_trade_details"),
                    sellEstimate, buyTotal);
                UI.Chain<ConfirmDialogWindow>().Invoke(v =>
                    v.Configure(OnTradeConfirm, "qmtrade.dialog_trade",
                        focusOnApply: true, details, "qmtrade.trade",
                        "qmtrade.cancel")).Show();
                return;
            }
            PerformTrade();
        }

        private void OnTradeConfirm(ConfirmDialogWindow.Option option)
        {
            if (option != ConfirmDialogWindow.Option.Yes)
                return;
            PerformTrade();
        }

        /// <summary>
        /// Completes the whole pending deal: every sell first, then every
        /// buy, then one refresh.
        /// </summary>
        private void PerformTrade()
        {
            if (_canSell && _sellWallet.Count > 0)
                SellNowCore();
            if (_canTrade && _buyWallet.Count > 0)
                BuyNowCore();
            RefreshAll();
            ShowPendingExchangeSummary();
        }

        /// <summary>
        /// After a trade in which the station handed items back (AnCom data
        /// chip delivery and similar exchanges), list them for the player.
        /// The items are already in the cargo; this dialog is informational.
        /// </summary>
        private void ShowPendingExchangeSummary()
        {
            if (_pendingExchangeSummary == null)
                return;
            var lines = _pendingExchangeSummary;
            _pendingExchangeSummary = null;
            var text = Localization.Get("qmtrade.exchange_title") + "\n"
                       + string.Join("\n", lines);
            UI.Chain<AlertDialogWindow>().Invoke(v =>
                v.Configure(text)).Show();
        }

        private void BuyNowCore()
        {
            try
            {
                var pointsBefore = _faction.PlayerTradePoints;
                var items = TradeSystem.BuyStationItems(_progression, _factions,
                    _itemsPrices, _statistics, _station, _buyWallet);
                foreach (var item in items)
                {
                    MagnumCargoSystem.AddCargo(_cargo, _spaceTime, item,
                        ActiveCargo());
                }
                if (items.Count > 0)
                {
                    PlayTakeItemSound();
                    Plugin.DebugLog("station trade bought "
                                    + items.Count + " stacks for "
                                    + (pointsBefore - _faction.PlayerTradePoints)
                                    + " points.");
                }
            }
            catch (Exception e)
            {
                Debug.LogError(Plugin.LogPrefix
                               + "station trade buy failed: " + e);
            }
            _buyWallet.Clear();
        }

        private void SellNowCore()
        {
            try
            {
                var totalBefore = CountAllItems();
                // 1. Return whatever was staged earlier so only this cart is
                //    sold -- nothing the player left in the stash is swept up.
                TakeBackStashItems();
                // 2. Stage the cart: remove from the storages (in cargo,
                //    fridge, recycle order), recreate as pickup stacks (the
                //    same factory BuyStationItems uses), then put them into
                //    the station stash that SellItems consumes.
                var staged = 0;
                var requested = 0;
                foreach (var pair in _sellWallet)
                {
                    if (pair.Value <= 0)
                        continue;
                    var available = _sellOwnedMap.TryGetValue(pair.Key,
                        out var count) ? count : 0;
                    var qty = Mathf.Clamp(pair.Value, 0, available);
                    if (qty <= 0)
                        continue;
                    var remaining = qty;
                    foreach (var storage in _sellStorages)
                    {
                        if (remaining <= 0)
                            break;
                        if (storage == _cargo.RecyclingStorage
                            && _cargo.RecyclingInProgress)
                        {
                            continue;
                        }
                        var inStorage = CountOf(storage, pair.Key);
                        if (inStorage <= 0)
                            continue;
                        var take = Mathf.Min(remaining, inStorage);
                        var batch = new List<BasePickupItem>();
                        ItemInteractionSystem.CreateItem(batch, pair.Key, take);
                        try
                        {
                            storage.RemoveSpecificItem(pair.Key, (short)take);
                        }
                        catch
                        {
                            // The batch was already created: keep it in the
                            // player's hands, then let the outer handler
                            // recover.
                            foreach (var item in batch)
                            {
                                MagnumCargoSystem.AddCargo(_cargo, _spaceTime,
                                    item, ActiveCargo());
                            }
                            throw;
                        }
                        foreach (var item in batch)
                        {
                            item.ExaminedItem = false;
                            // If a single put fails, that item must still end
                            // up in the player's hands: straight to cargo.
                            try
                            {
                                _station.Stash.ExpandHeightAndPutItem(item);
                                staged += item.StackCount;
                            }
                            catch
                            {
                                MagnumCargoSystem.AddCargo(_cargo, _spaceTime,
                                    item, ActiveCargo());
                            }
                        }
                        remaining -= take;
                    }
                    requested += qty;
                }
                if (staged != requested)
                {
                    // Never risk losing items: pull the staging area back and
                    // abort before the game touches anything.
                    Debug.LogError(Plugin.LogPrefix
                                   + "station trade sell staging mismatch: "
                                   + "requested " + requested + ", staged "
                                   + staged + ". Items returned to cargo.");
                    TakeBackStashItems();
                    RefreshAll();
                    return;
                }
                // 3. Sell everything now in the stash (exactly the cart).
                var result = new List<BasePickupItem>();
                TradeSystem.SellItems(_progression, _factions, _itemsPrices,
                    _statistics, _station, result, _station.Stash, _difficulty,
                    exchangeItems: false, 0, out var exchangeQuestItem);
                if (exchangeQuestItem)
                {
                    _faction.PlayerReputation +=
                        Data.StoryVars.TutorialAncomDataReputationReward;
                    AchievementProgress.ProcessAnComDataDelivered();
                    _storyTriggers.Pass("AnComData_Received");
                    _storyTriggers.Pass("quest_ancom_data_"
                                        + _station.OwnerFactionId);
                }
                // Reward items (the quest delivery reward) come back in the
                // result list; vanilla stages them in the station stash for a
                // manual transfer, we hand them straight to the cargo -- but
                // remember what was received so the player can see the
                // exchange after the trade.
                var receivedLines = new List<string>();
                foreach (var item in result)
                {
                    MagnumCargoSystem.AddCargo(_cargo, _spaceTime, item,
                        ActiveCargo());
                    receivedLines.Add(string.Format(
                        Localization.Get("qmtrade.exchange_item"),
                        ItemNameOf(item.Id), item.StackCount));
                }
                if (receivedLines.Count > 0)
                    _pendingExchangeSummary = receivedLines;
                // 4. Return anything the station would not take.
                TakeBackStashItems();
                var totalAfter = CountAllItems();
                // Quest delivery adds reward items, so the strict equality
                // only applies to ordinary sales.
                if (!exchangeQuestItem && totalBefore != totalAfter)
                {
                    Debug.LogError(Plugin.LogPrefix
                                   + "station trade sell item count changed: "
                                   + totalBefore + " -> " + totalAfter);
                }
                PlayTakeItemSound();
            }
            catch (Exception e)
            {
                Debug.LogError(Plugin.LogPrefix
                               + "station trade sell failed: " + e);
                // Never leave the cart stranded in the staging area.
                TakeBackStashItems();
            }
            _sellWallet.Clear();
        }

        private void TakeBackStashItems()
        {
            var items = new List<BasePickupItem>(_station.Stash.Items);
            foreach (var item in items)
            {
                MagnumCargoSystem.AddCargo(_cargo, _spaceTime, item,
                    ActiveCargo());
            }
        }

        /// <summary>
        /// Total item units across every storage this panel can touch. Used to
        /// prove sell staging neither creates nor destroys items. The AnCom
        /// quest item is excluded: selling it legitimately removes it from
        /// the world and replaces it with reward items.
        /// </summary>
        private int CountAllItems()
        {
            var total = 0;
            foreach (var storage in _cargo.ShipCargo)
                total += CountStorage(storage);
            total += CountStorage(_cargo.FridgeStorage);
            total += CountStorage(_cargo.RecyclingStorage);
            total += CountStorage(_station.Stash);
            total += CountStorage(_station.InternalStorage);
            return total;
        }

        private static int CountStorage(ItemStorage storage)
        {
            if (storage == null)
                return 0;
            var total = 0;
            foreach (var item in storage.Items)
            {
                if (!item.Id.Equals(Data.StoryVars.TutorialAncomQuestItemId))
                    total += item.StackCount;
            }
            return total;
        }

        private static int CountOf(ItemStorage storage, string itemId)
        {
            if (storage == null)
                return 0;
            var total = 0;
            foreach (var item in storage.Items)
            {
                if (item.Id.Equals(itemId))
                    total += item.StackCount;
            }
            return total;
        }

        private static void PlayTakeItemSound()
        {
            var controller = SingletonMonoBehaviour<SoundController>.Instance;
            var sounds = SingletonMonoBehaviour<SoundsStorage>.Instance;
            if (controller != null && sounds?.TakeItem != null)
                controller.PlayUiSound(sounds.TakeItem);
        }

        private static void PlayEmptyAttack()
        {
            var controller = SingletonMonoBehaviour<SoundController>.Instance;
            var sounds = SingletonMonoBehaviour<SoundsStorage>.Instance;
            if (controller != null && sounds?.EmptyAttack != null)
                controller.PlayUiSound(sounds.EmptyAttack);
        }

        // ------------------------------------------------------------------

        private void Update()
        {
            if (!IsPanelActive || _root == null || !_root.activeInHierarchy)
                return;
            // UIScaleFixer reacts to a resolution change from its own Update,
            // which can swap the scaler mode -- and therefore the design
            // space -- while this panel is open. Re-fitting from here also
            // covers the case where the parent rect was not yet driven when
            // the panel was first built.
            RefitToSurface();
            UpdateSweepInput();
            UpdateShortcuts();
            if (_listDirty)
            {
                _listDirty = false;
                RefreshList();
            }
            // Deliberately NOT InvalidateTotals: the per-frame job is only the
            // TRADE button's drag-state gate. The prices behind it are
            // recomputed when something actually moves the cart.
            RefreshTotals();
        }

        /// <summary>
        /// Keyboard shortcuts. Keys are read through InputHelper so the
        /// shared input isolation keeps them dead while a text field owns
        /// the keyboard; none of them collide with a vanilla binding.
        /// </summary>
        private void UpdateShortcuts()
        {
            if (AnyInputFocused || _sweepActive)
                return;
            if (InputHelper.GetKeyDown(Plugin.ShortcutTogglePane))
            {
                SelectPane(_pane == Pane.Buy ? Pane.Sell : Pane.Buy);
                return;
            }
            if (InputHelper.GetKeyDown(Plugin.ShortcutPrevPage))
            {
                ScrollRows(-1);
                return;
            }
            if (InputHelper.GetKeyDown(Plugin.ShortcutNextPage))
            {
                ScrollRows(1);
                return;
            }
            if (InputHelper.GetKeyDown(Plugin.ShortcutTrade))
            {
                ExecuteTrade();
                return;
            }
            if (InputHelper.GetKeyDown(Plugin.ShortcutClearCart))
            {
                WalletOf(_pane == Pane.Sell).Clear();
                RefreshSlots();
                InvalidateTotals();
                RefreshTotals();
                return;
            }
            if (_pane == Pane.Sell
                && InputHelper.GetKeyDown(Plugin.ShortcutSelectAll))
            {
                SelectAllVisible();
            }
        }

        /// <summary>
        /// Short, readable key captions for the inline hints: brackets stay
        /// brackets, Delete becomes DEL, letter keys lose the Alpha prefix.
        /// </summary>
        private static string ShortcutLabel(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.LeftBracket:
                    return "[";
                case KeyCode.RightBracket:
                    return "]";
                case KeyCode.Delete:
                    return "DEL";
                case KeyCode.Return:
                    return "ENTER";
                case KeyCode.Space:
                    return "SPACE";
                default:
                    return key.ToString().Replace("Alpha", string.Empty);
            }
        }

        private static string ShortcutSuffix(KeyCode key)
        {
            return " (" + ShortcutLabel(key) + ")";
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
            var localization = Singleton<Localization>.Instance;
            if (localization != null)
                localization.OnLangChanged -= OnLangChanged;
        }

        /// <summary>
        /// Every label was resolved when the panel was built, so a live
        /// language change rebuilds the whole panel to re-resolve them --
        /// the same way the vanilla screens refresh on OnLangChanged.
        /// </summary>
        private void OnLangChanged()
        {
            if (!IsPanelActive)
                return;
            try
            {
                DiscardBuiltUi();
                EnsureBuilt();
                var localization = Singleton<Localization>.Instance;
                if (localization != null)
                {
                    _builtLang = localization.CurrentLang;
                    _langKnown = true;
                }
                RefreshAll();
                ShowPanel();
            }
            catch (Exception e)
            {
                Debug.LogError(Plugin.LogPrefix
                               + "language change rebuild failed: " + e);
            }
        }
    }
}
