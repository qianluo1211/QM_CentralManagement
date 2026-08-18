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
    /// One line of a barter payout: what the station handed over and how many.
    ///
    /// Ids and counts, never the BasePickupItem: by the time the popup is
    /// shown the payout has already been merged into the cargo, so a held
    /// reference would be exactly the stale-item bug the card grid and the
    /// context menu both had to be null-guarded against.
    /// </summary>
    internal sealed class ExchangeItemLine
    {
        internal string ItemId;
        internal int Count;
        /// <summary>
        /// Localization key for the amount column, or null for the default
        /// "x{0}". It exists because not every list counts whole items: a
        /// repair receipt counts CHARGES off a multi-use kit, and rendering
        /// "x7" there reads as seven kits when it was seven uses out of one.
        /// </summary>
        internal string AmountKey;
    }

    /// <summary>
    /// The "the station handed these over" modal shown after a barter/quest
    /// exchange (the AnCom data chip delivery and anything like it).
    ///
    /// This used to be an AlertDialogWindow carrying one string per item,
    /// which is why the payout read as a wall of text: the game's alert has
    /// no item column, so the icons every other item list in this mod shows
    /// simply could not appear. It is a proper row list now -- vanilla icon,
    /// name, amount, and the same hover tooltip the trade rows carry.
    ///
    /// The overlay is parented to the trade PANEL, not the screen, so the
    /// panel's vanilla-hide sweep (which walks the panel's ancestors and
    /// switches off everything beside it) can never mistake it for a vanilla
    /// widget and re-enable it later.
    /// </summary>
    internal sealed class ExchangeSummaryPopup
    {
        private const float PopupWidth = 320f;
        private const float PadX = 8f;
        private const float RowW = PopupWidth - 2f * PadX;
        private const float TitleH = 18f;
        private const float NoteH = 12f;
        // First row's top edge, measured from the popup's top.
        private const float ListTop = -40f;
        private const float RowH = 22f;
        private const float IconX = 4f;
        private const float IconW = 26f;
        private const float IconH = 20f;
        private const float NameX = 36f;
        private const float CountW = 56f;
        private const float ButtonW = 96f;
        private const float ButtonH = 20f;
        private const float ButtonGap = 6f;
        private const float BottomPad = 8f;
        // Everything that is not list rows: see HeightFor.
        private const float FixedHeight = -ListTop + ButtonGap + ButtonH
                                          + BottomPad;
        // A payout is one or two item types in practice; ten rows is the
        // safety valve, and the overflow gets counted rather than dropped.
        private const int MaxRows = 10;

        private sealed class Row
        {
            internal GameObject Root;
            internal Image Icon;
            internal ItemTooltipHandler Tooltip;
            internal TextMeshProUGUI Name;
            internal TextMeshProUGUI Count;
        }

        private readonly Action _consumePointerRelease;
        private readonly Action _onBeforeOpen;

        private GameObject _overlayRoot;
        private GameObject _popupRoot;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _note;
        private GameObject _closeRoot;
        private TextMeshProUGUI _closeLabel;
        private readonly Row[] _rows = new Row[MaxRows];

        /// <param name="consumePointerRelease">Swallow the mouse release that
        /// closed the popup, so it cannot reach the panel underneath.</param>
        /// <param name="onBeforeOpen">Close anything of the host's that must
        /// not stay open behind the modal (its dropdowns).</param>
        internal ExchangeSummaryPopup(Action consumePointerRelease,
            Action onBeforeOpen)
        {
            _consumePointerRelease = consumePointerRelease;
            _onBeforeOpen = onBeforeOpen;
        }

        internal bool IsOpen => _overlayRoot != null
                                && _overlayRoot.activeInHierarchy;

        internal void Build(Transform parent)
        {
            if (_overlayRoot != null)
                return;

            _overlayRoot = PanelUi.CreateUiObject("ExchangeOverlay", parent);
            PanelUi.Stretch((RectTransform)_overlayRoot.transform);
            var scrim = _overlayRoot.AddComponent<Image>();
            scrim.color = VanillaSkin.Palette.Scrim;
            scrim.raycastTarget = true;
            var scrimButton = _overlayRoot.AddComponent<Button>();
            PanelUi.ConfigureButton(scrimButton, scrim);
            scrimButton.onClick.AddListener(Close);

            _popupRoot = PanelUi.CreateUiObject("ExchangePopup",
                _overlayRoot.transform);
            var popupRect = (RectTransform)_popupRoot.transform;
            PanelUi.SetCentered(popupRect, PopupWidth, HeightFor(1));
            VanillaSkin.Slice(_popupRoot, VanillaSkin.PanelFrame,
                PanelUi.PanelColor).raycastTarget = true;
            // The popup body is not a click target, but the scrim behind it
            // is: without a handler of its own, a click on dead space (or on
            // an item icon, whose tooltip handler takes hovers only) walks up
            // the hierarchy to the scrim's Button and dismisses the popup the
            // player was reading. EventTrigger implements the click interface
            // whether or not it has entries, so it stops there.
            _popupRoot.AddComponent<EventTrigger>().triggers =
                new List<EventTrigger.Entry>();

            _title = PanelUi.CreateText("Title", popupRect, PadX, -6f, RowW,
                TitleH, TextContext.WindowCaption, PanelUi.BrightColor,
                TextAlignmentOptions.MidlineLeft);
            _note = PanelUi.CreateText("Note", popupRect, PadX, -25f, RowW,
                NoteH, TextContext.IgnoreSize, 6f, PanelUi.OffColor,
                TextAlignmentOptions.MidlineLeft);

            for (var i = 0; i < MaxRows; i++)
                _rows[i] = BuildRow(popupRect, i);

            _closeRoot = PanelUi.CreateButtonRoot("Close", popupRect,
                (PopupWidth - ButtonW) * 0.5f, 0f, ButtonW, ButtonH,
                out _, out _closeLabel);
            PanelUi.BindClick(_closeRoot, Close);
            _overlayRoot.SetActive(false);
        }

        private static Row BuildRow(RectTransform popupRect, int index)
        {
            var row = new Row();
            row.Root = PanelUi.CreateUiObject("Row" + index, popupRect);
            PanelUi.SetTopLeft((RectTransform)row.Root.transform,
                PadX, ListTop - index * RowH, RowW, RowH);
            VanillaSkin.Slice(row.Root, VanillaSkin.ListBackground,
                PanelUi.CardColor).raycastTarget = false;

            var iconRoot = PanelUi.CreateUiObject("Icon", row.Root.transform);
            PanelUi.SetTopLeft((RectTransform)iconRoot.transform,
                IconX, -(RowH - IconH) * 0.5f, IconW, IconH);
            row.Icon = iconRoot.AddComponent<Image>();
            // Two-slot art (rifles and the like) is authored twice as wide as
            // it is tall, so the box is wider than square and the aspect is
            // preserved rather than the icon squashed into a 1x1 cell.
            row.Icon.preserveAspect = true;
            // The tooltip target is the icon alone, exactly as on the trade
            // rows: a small hover zone that cannot cover the row's text.
            row.Icon.raycastTarget = true;
            row.Tooltip = iconRoot.AddComponent<ItemTooltipHandler>();

            row.Name = PanelUi.CreateText("Name", row.Root.transform,
                NameX, 0f, RowW - NameX - IconX - CountW, RowH,
                TextContext.IgnoreSize, 7f, PanelUi.BrightColor,
                TextAlignmentOptions.MidlineLeft);
            row.Count = PanelUi.CreateText("Count", row.Root.transform,
                RowW - IconX - CountW, 0f, CountW, RowH,
                TextContext.IgnoreSize, 7f, PanelUi.AccentColor,
                TextAlignmentOptions.MidlineRight);

            row.Root.SetActive(false);
            return row;
        }

        private static float HeightFor(int rows)
        {
            return FixedHeight + Mathf.Max(1, rows) * RowH;
        }

        internal void Discard()
        {
            if (_overlayRoot != null)
                UnityEngine.Object.Destroy(_overlayRoot);
            _overlayRoot = null;
            _popupRoot = null;
            _title = null;
            _note = null;
            _closeRoot = null;
            _closeLabel = null;
            for (var i = 0; i < _rows.Length; i++)
                _rows[i] = null;
        }

        internal void Hide()
        {
            if (_overlayRoot != null)
                _overlayRoot.SetActive(false);
        }

        /// <summary>
        /// Escape and Enter both dismiss: the popup only reports, so there is
        /// nothing here to confirm or cancel between.
        /// </summary>
        internal void HandleKeys()
        {
            if (Input.GetKeyDown(KeyCode.Escape)
                || Input.GetKeyDown(KeyCode.Return)
                || Input.GetKeyDown(KeyCode.KeypadEnter)
                || Input.GetKeyDown(KeyCode.Space))
            {
                Close();
            }
        }

        /// <param name="noteKey">Localization key for the grey line under
        /// the title. It is the only thing that differs between the barter
        /// payout this list was built for and the batch-repair receipt, so it
        /// is a parameter rather than a second popup.</param>
        internal void Show(string title, IList<ExchangeItemLine> lines,
            string noteKey = "qmtrade.exchange_note")
        {
            if (_overlayRoot == null || lines == null || lines.Count == 0)
                return;
            _onBeforeOpen?.Invoke();

            _title.text = title;
            _note.text = Localization.Get(noteKey);
            _closeLabel.text = Localization.Get("qmtrade.close");

            // The row count is fitted to whatever the popup was parented
            // to -- the trade panel, or the arsenal screen for the repair
            // receipt -- rather than assumed, so a long list cannot hang out
            // of its host at the shortest supported aspect ratio.
            var visible = Mathf.Clamp(lines.Count, 1,
                Mathf.Min(MaxRows, MaxRowsFor(AvailableHeight())));
            // The last slot becomes a "+N more" line when the payout does not
            // fit, so an overflow is counted instead of silently dropped.
            var listed = visible < lines.Count ? visible - 1 : lines.Count;

            for (var i = 0; i < _rows.Length; i++)
            {
                var row = _rows[i];
                if (row == null)
                    continue;
                if (i < listed)
                    FillRow(row, lines[i]);
                else if (i == listed && listed < visible)
                    FillOverflowRow(row, lines.Count - listed);
                else
                    row.Root.SetActive(false);
            }

            PanelUi.SetCentered((RectTransform)_popupRoot.transform,
                PopupWidth, HeightFor(visible));
            PanelUi.SetTopLeft((RectTransform)_closeRoot.transform,
                (PopupWidth - ButtonW) * 0.5f,
                ListTop - visible * RowH - ButtonGap, ButtonW, ButtonH);

            _overlayRoot.SetActive(true);
            _overlayRoot.transform.SetAsLastSibling();
            UI.Drag.Pause(0.18f);
        }

        private static void FillRow(Row row, ExchangeItemLine line)
        {
            row.Root.SetActive(true);
            row.Name.text = Localization.Get("item." + line.ItemId + ".name");
            row.Count.text = string.Format(
                Localization.Get(line.AmountKey ?? "qmtrade.exchange_amount"),
                line.Count);
            row.Count.color = PanelUi.AccentColor;

            var record = Data.Items.GetRecord(line.ItemId);
            var icon = record?.ItemDesc == null
                ? null
                : SingletonMonoBehaviour<ItemFactory>.Instance
                    .ResolveIcon(record.ItemDesc, 1);
            row.Icon.sprite = icon;
            // An Image with no sprite paints a white block, so the icon well
            // is switched off entirely rather than left showing one.
            row.Icon.enabled = icon != null;
            row.Tooltip.enabled = true;
            row.Tooltip.Initialize(line.ItemId);
        }

        private static void FillOverflowRow(Row row, int remaining)
        {
            row.Root.SetActive(true);
            row.Name.text = string.Format(
                Localization.Get("qmtrade.exchange_more"), remaining);
            row.Count.text = string.Empty;
            row.Icon.sprite = null;
            row.Icon.enabled = false;
            // Nothing to hover, and an empty id would be handed straight to
            // Data.Items.GetRecord: take the handler off the row instead.
            row.Tooltip.enabled = false;
        }

        /// <summary>
        /// The height of the surface the popup is centred on. Read from the
        /// live parent rather than assumed, for the reasons on
        /// <see cref="PanelUi.DesignSurfaceSize"/>.
        /// </summary>
        private float AvailableHeight()
        {
            var host = _overlayRoot == null
                ? null
                : _overlayRoot.transform.parent as RectTransform;
            var height = host == null ? 0f : host.rect.height;
            return height > 1f ? height : 356f;
        }

        private static int MaxRowsFor(float availableHeight)
        {
            return Mathf.Max(1,
                Mathf.FloorToInt((availableHeight - FixedHeight) / RowH));
        }

        internal void Close()
        {
            if (_overlayRoot == null)
                return;
            var wasVisible = _overlayRoot.activeInHierarchy;
            _overlayRoot.SetActive(false);
            if (wasVisible)
                _consumePointerRelease?.Invoke();
        }
    }
}
