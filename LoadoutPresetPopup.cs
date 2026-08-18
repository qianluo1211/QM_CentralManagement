using System;
using System.Collections.Generic;
using System.Linq;
using MGSC;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace QM_CentralManagement
{
    /// <summary>
    /// The services a preset flow needs, read fresh on every use. Both hosts
    /// live on pooled screens whose mercenary and cargo are re-assigned behind
    /// their backs, so the popup must never cache them.
    /// </summary>
    internal sealed class LoadoutPresetContext
    {
        internal Mercenary Mercenary;
        internal MagnumCargo Cargo;
        internal MagnumProgression Progression;
        internal SpaceTime SpaceTime;
        internal PerkFactory PerkFactory;

        internal bool IsUsable => Mercenary != null && Cargo != null
                                  && Cargo.ShipCargo.Count > 0;
    }

    /// <summary>
    /// The save / apply / delete modal shared by the central panel's preset bar
    /// and the pre-departure ship loadout strip.
    ///
    /// Both hosts used to carry their own copy of this -- the same six popup
    /// modes, the same issue-list truncation, the same force-apply branch --
    /// and they had already drifted: only one of them played the augmentation
    /// sounds, and only one used the ship-specific wording that has been
    /// sitting unused in the localisation table since it was written.
    ///
    /// What is deliberately NOT here: the bar itself. The central bar is a
    /// two-row 314x46 block inside the inventory window and the ship strip is a
    /// one-row band pinned above it; that geometry is genuinely per-host and
    /// pretending otherwise would need more configuration than duplication.
    /// </summary>
    internal sealed class LoadoutPresetPopup
    {
        internal enum Mode
        {
            None,
            Save,
            Apply,
            ForceApply,
            Delete,
            Message,
            /// <summary>
            /// A flow this class knows nothing about: the host supplies the
            /// wording and the confirm action. Used by the shuttle manifest
            /// controls, which share this popup rather than standing up a
            /// second identical modal.
            /// </summary>
            Custom
        }

        private const float PopupWidth = 260f;
        private const float PopupHeight = 136f;

        // Issue lists are truncated so the decision the player is making stays
        // inside the fixed-height vanilla-style popup. Force-apply spends a
        // line on its own explanation, so it shows fewer diagnostics.
        private const int ForceIssueLines = 4;
        private const int BlockingIssueLines = 7;

        private readonly LoadoutPresetTextSet _texts;
        private readonly Func<LoadoutPresetContext> _context;
        private readonly Action _onPresetsChanged;
        private readonly Action _onPresetApplied;
        private readonly Action _consumePointerRelease;
        private readonly Action _onBeforeOpen;

        private GameObject _overlayRoot;
        private GameObject _popupRoot;
        private TextMeshProUGUI _title;
        private TextMeshProUGUI _body;
        private TextMeshProUGUI _confirmLabel;
        private TextMeshProUGUI _cancelLabel;
        private TextMeshProUGUI _nameText;
        private TMP_Text _namePlaceholder;
        private TMP_InputField _nameInput;
        private Mode _mode;
        private string _pendingId;
        private Action<string> _customConfirm;

        /// <param name="texts">Which wording variant this host uses.</param>
        /// <param name="context">Reads the host's current services.</param>
        /// <param name="onPresetsChanged">The preset list or selection moved:
        /// refresh the host's bar.</param>
        /// <param name="onPresetApplied">A preset was applied: the host also
        /// has to refresh whatever it shows of the agent and the cargo.</param>
        /// <param name="consumePointerRelease">Swallow the mouse release that
        /// closed the popup, so it cannot reach an ItemSlot underneath.</param>
        /// <param name="onBeforeOpen">Close anything of the host's that must
        /// not stay open behind the modal (its dropdowns).</param>
        internal LoadoutPresetPopup(LoadoutPresetTextSet texts,
            Func<LoadoutPresetContext> context,
            Action onPresetsChanged,
            Action onPresetApplied,
            Action consumePointerRelease,
            Action onBeforeOpen)
        {
            _texts = texts ?? LoadoutPresetTextSet.Central;
            _context = context;
            _onPresetsChanged = onPresetsChanged;
            _onPresetApplied = onPresetApplied;
            _consumePointerRelease = consumePointerRelease;
            _onBeforeOpen = onBeforeOpen;
        }

        internal bool IsOpen => _overlayRoot != null
                                && _overlayRoot.activeInHierarchy;

        internal bool IsInputFocused => _nameInput != null
                                        && _nameInput.isFocused;

        internal IEnumerable<TMP_Text> Texts
        {
            get
            {
                yield return _title;
                yield return _body;
                yield return _confirmLabel;
                yield return _cancelLabel;
                yield return _nameText;
                yield return _namePlaceholder;
            }
        }

        internal void Build(Transform parent)
        {
            if (_overlayRoot != null)
                return;

            _overlayRoot = PanelUi.CreateUiObject("PresetOverlay", parent);
            PanelUi.Stretch((RectTransform)_overlayRoot.transform);
            var overlay = _overlayRoot.AddComponent<Image>();
            overlay.color = VanillaSkin.Palette.Scrim;
            overlay.raycastTarget = true;
            var overlayButton = _overlayRoot.AddComponent<Button>();
            PanelUi.ConfigureButton(overlayButton, overlay);
            overlayButton.onClick.AddListener(Close);

            _popupRoot = PanelUi.CreateUiObject("PresetPopup",
                _overlayRoot.transform);
            PanelUi.SetCentered((RectTransform)_popupRoot.transform,
                PopupWidth, PopupHeight);
            var popupRect = (RectTransform)_popupRoot.transform;
            VanillaSkin.Slice(_popupRoot, VanillaSkin.PanelFrame,
                PanelUi.PanelColor).raycastTarget = true;
            // The body needs a click handler of its own even though nothing
            // in it is clickable. The scrim behind it closes on click, and a
            // click that lands on dead space has no handler until the search
            // walks up to that scrim -- so clicking the popup's own margin
            // dismissed it, and while naming a preset that threw away
            // whatever had been typed. EventTrigger implements the click
            // interface with or without entries, so the search stops here.
            // The (empty) list is assigned rather than left to the lazy
            // getter, matching every other EventTrigger in this mod.
            _popupRoot.AddComponent<EventTrigger>().triggers =
                new List<EventTrigger.Entry>();

            _title = PanelUi.CreateText("Title", popupRect, 8f, -6f, 244f, 18f,
                TextContext.WindowCaption, PanelUi.BrightColor,
                TextAlignmentOptions.MidlineLeft);
            _body = PanelUi.CreateText("Body", popupRect, 8f, -27f, 244f, 52f,
                TextContext.IgnoreSize, 7f, PanelUi.ValueColor,
                TextAlignmentOptions.TopLeft);
            _body.enableWordWrapping = true;
            _body.overflowMode = TextOverflowModes.Ellipsis;

            BuildNameInput(popupRect);

            var cancelRoot = PanelUi.CreateButtonRoot("Cancel", popupRect,
                8f, -108f, 118f, 20f, out _, out _cancelLabel);
            PanelUi.BindClick(cancelRoot, Close);
            var confirmRoot = PanelUi.CreateButtonRoot("Confirm", popupRect,
                134f, -108f, 118f, 20f, out _, out _confirmLabel);
            PanelUi.BindClick(confirmRoot, Confirm);
            _overlayRoot.SetActive(false);
        }

        private void BuildNameInput(RectTransform popupRect)
        {
            var inputRoot = PanelUi.CreateUiObject("Name", popupRect);
            PanelUi.SetTopLeft((RectTransform)inputRoot.transform,
                8f, -82f, 244f, 20f);
            var background = VanillaSkin.Slice(inputRoot,
                VanillaSkin.ListBackground, PanelUi.CardColor);

            var viewport = PanelUi.CreateUiObject("Viewport",
                inputRoot.transform);
            var viewportRect = (RectTransform)viewport.transform;
            PanelUi.Stretch(viewportRect);
            viewportRect.offsetMin = new Vector2(4f, 1f);
            viewportRect.offsetMax = new Vector2(-4f, -1f);
            viewport.AddComponent<RectMask2D>();

            _nameText = PanelUi.CreateText("Text", viewport.transform,
                0f, 0f, 236f, 18f, TextContext.IgnoreSize, 7f,
                PanelUi.BrightColor, TextAlignmentOptions.MidlineLeft);
            var placeholder = PanelUi.CreateText("Placeholder",
                viewport.transform, 0f, 0f, 236f, 18f, TextContext.IgnoreSize,
                7f, PanelUi.OffColor, TextAlignmentOptions.MidlineLeft);
            placeholder.fontStyle = FontStyles.Italic;
            _namePlaceholder = placeholder;

            _nameInput = inputRoot.AddComponent<TMP_InputField>();
            _nameInput.targetGraphic = background;
            _nameInput.textViewport = viewportRect;
            _nameInput.textComponent = _nameText;
            _nameInput.placeholder = _namePlaceholder;
            _nameInput.lineType = TMP_InputField.LineType.SingleLine;
            _nameInput.characterLimit = 28;
            _nameInput.richText = false;
            _nameInput.onSelect.AddListener(_ =>
                Input.imeCompositionMode = IMECompositionMode.On);
            _nameInput.onDeselect.AddListener(_ => _consumePointerRelease());
        }

        internal void Discard()
        {
            if (_overlayRoot != null)
                UnityEngine.Object.Destroy(_overlayRoot);
            _overlayRoot = null;
            _popupRoot = null;
            _title = null;
            _body = null;
            _confirmLabel = null;
            _cancelLabel = null;
            _nameText = null;
            _namePlaceholder = null;
            _nameInput = null;
            _mode = Mode.None;
            _pendingId = null;
            _customConfirm = null;
        }

        internal void Hide()
        {
            if (_overlayRoot != null)
                _overlayRoot.SetActive(false);
            _mode = Mode.None;
            _pendingId = null;
            _customConfirm = null;
        }

        internal void ApplyFont(TMP_FontAsset font)
        {
            foreach (var text in Texts)
            {
                if (text != null)
                    text.font = font;
            }
        }

        /// <summary>
        /// Escape closes, Enter confirms. The host calls this from its Update
        /// while the modal owns the screen.
        /// </summary>
        internal void HandleKeys()
        {
            if (Input.GetKeyDown(KeyCode.Escape))
                Close();
            else if (Input.GetKeyDown(KeyCode.Return)
                     || Input.GetKeyDown(KeyCode.KeypadEnter))
                Confirm();
        }

        // ------------------------------------------------------------------
        // Flows.
        // ------------------------------------------------------------------

        internal void OpenSave()
        {
            var context = _context();
            if (context?.Mercenary == null)
                return;
            Open(Mode.Save,
                Localization.Get("qmcentral.preset_save_title"),
                Localization.Get(_texts.SaveBodyKey),
                Localization.Get("qmcentral.preset_save_confirm"),
                LoadoutPresetRepository.Selected?.Name
                ?? LoadoutPresetRepository.SuggestName());
        }

        internal void OpenApply()
        {
            var preset = LoadoutPresetRepository.Selected;
            var context = _context();
            if (preset == null || context?.Mercenary == null)
                return;

            var validation = LoadoutPresetService.Check(preset,
                context.Mercenary, context.Cargo, context.Progression);
            if (validation.Success)
            {
                _pendingId = preset.Id;
                Open(Mode.Apply,
                    Localization.Get("qmcentral.preset_apply_title"),
                    string.Format(Localization.Get(_texts.ApplyBodyKey),
                        preset.Name, LoadoutPresetService.GetSummary(preset)),
                    Localization.Get("qmcentral.preset_apply_confirm"));
                return;
            }

            var issues = validation.AllIssues.ToList();
            var lines = issues.Take(validation.CanForce
                ? ForceIssueLines
                : BlockingIssueLines).ToList();
            if (issues.Count > lines.Count)
                lines.Add("…");

            if (!validation.CanForce)
            {
                Open(Mode.Message,
                    Localization.Get("qmcentral.preset_missing_title"),
                    string.Join("\n", lines),
                    Localization.Get("qmcentral.preset_close"));
                return;
            }
            _pendingId = preset.Id;
            lines.Add(Localization.Get(_texts.ForceExplanationKey));
            Open(Mode.ForceApply,
                Localization.Get("qmcentral.preset_force_title"),
                string.Join("\n", lines),
                Localization.Get("qmcentral.preset_force_confirm"));
        }

        internal void OpenDelete()
        {
            var preset = LoadoutPresetRepository.Selected;
            if (preset == null)
                return;
            _pendingId = preset.Id;
            Open(Mode.Delete,
                Localization.Get("qmcentral.preset_delete_title"),
                string.Format(
                    Localization.Get("qmcentral.preset_delete_body"),
                    preset.Name),
                Localization.Get("qmcentral.preset_delete_confirm"));
        }

        /// <summary>
        /// A modal this class has no opinion about: the caller owns the
        /// wording and what Confirm does. Passing a non-null
        /// <paramref name="inputInitial"/> shows the name field; passing a
        /// null <paramref name="onConfirm"/> makes it a plain message with a
        /// single dismiss button.
        /// </summary>
        internal void OpenCustom(string title, string body, string confirm,
            string inputInitial, Action<string> onConfirm)
        {
            Open(onConfirm == null ? Mode.Message : Mode.Custom, title, body,
                confirm, inputInitial);
            // After Open, which clears the previous flow's action.
            _customConfirm = onConfirm;
        }

        private void Open(Mode mode, string title, string body, string confirm,
            string input = null)
        {
            if (_overlayRoot == null)
                return;
            _onBeforeOpen?.Invoke();
            _customConfirm = null;
            _mode = mode;
            _title.text = title;
            _body.text = body;
            _confirmLabel.text = confirm;
            _cancelLabel.text = Localization.Get("qmcentral.preset_cancel");

            var showInput = mode == Mode.Save
                            || (mode == Mode.Custom && input != null);
            _nameInput.gameObject.SetActive(showInput);
            if (showInput)
            {
                _namePlaceholder.text = Localization.Get(
                    "qmcentral.preset_name_placeholder");
                _nameInput.SetTextWithoutNotify(input ?? string.Empty);
            }
            // A message popup has nothing to cancel -- its only button closes.
            _cancelLabel.transform.parent.gameObject.SetActive(
                mode != Mode.Message);

            _overlayRoot.SetActive(true);
            _overlayRoot.transform.SetAsLastSibling();
            ApplyFont(Localization.GetActualFont());
            UI.Drag.Pause(0.18f);
            if (showInput)
            {
                _nameInput.ActivateInputField();
                _nameInput.Select();
            }
        }

        internal void Confirm()
        {
            var mode = _mode;
            if (mode == Mode.None)
                return;
            if (mode == Mode.Message)
            {
                Close();
                return;
            }
            if (mode == Mode.Custom)
            {
                var action = _customConfirm;
                var text = _nameInput.gameObject.activeSelf
                    ? _nameInput.text
                    : null;
                // Closed first: the action is free to open another modal on
                // top (the manifest save reports its own result that way).
                Close();
                action?.Invoke(text);
                return;
            }
            if (mode == Mode.Save)
            {
                ConfirmSave();
                return;
            }
            if (mode == Mode.Delete)
            {
                LoadoutPresetRepository.Delete(_pendingId);
                Close();
                _onPresetsChanged?.Invoke();
                return;
            }
            if (mode == Mode.Apply || mode == Mode.ForceApply)
                ConfirmApply(mode == Mode.ForceApply);
        }

        private void ConfirmSave()
        {
            var context = _context();
            var snapshot = LoadoutPresetService.Capture(context?.Mercenary);
            if (snapshot == null)
                return;
            LoadoutPresetRepository.Save(_nameInput.text, snapshot);
            Close();
            _onPresetsChanged?.Invoke();
        }

        private void ConfirmApply(bool force)
        {
            var preset = LoadoutPresetRepository.Data.Presets.FirstOrDefault(
                p => p != null && p.Id == _pendingId);
            var context = _context();
            if (preset == null || context == null || !context.IsUsable)
            {
                Close();
                return;
            }

            var result = LoadoutPresetService.Apply(preset, context.Mercenary,
                context.Cargo, context.Progression, context.SpaceTime,
                context.PerkFactory, force, _texts);
            Close();
            _onPresetApplied?.Invoke();
            _onPresetsChanged?.Invoke();
            if (result.Success)
            {
                PlayAppliedSounds(preset);
                return;
            }
            Open(Mode.Message,
                Localization.Get("qmcentral.preset_error_title"),
                result.Message,
                Localization.Get("qmcentral.preset_close"));
        }

        internal void Close()
        {
            if (_overlayRoot == null)
                return;
            var wasVisible = _overlayRoot.activeInHierarchy;
            _nameInput?.DeactivateInputField();
            _overlayRoot.SetActive(false);
            _mode = Mode.None;
            _pendingId = null;
            _customConfirm = null;
            if (wasVisible)
                _consumePointerRelease?.Invoke();
        }

        /// <summary>
        /// One weapon sound and one general "gear moved" sound at most, so a
        /// full loadout does not fire a dozen overlapping cues.
        /// </summary>
        private static void PlayAppliedSounds(LoadoutPreset preset)
        {
            var controller = SingletonMonoBehaviour<SoundController>.Instance;
            var sounds = SingletonMonoBehaviour<SoundsStorage>.Instance;
            if (controller == null || sounds == null || preset == null)
                return;

            var hasWeapon = false;
            var hasOtherGear = false;
            foreach (var entry in preset.Equipment
                                  ?? new List<LoadoutEquipmentEntry>())
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.ItemId))
                    continue;
                var composite = Data.Items.GetRecord(entry.ItemId)
                    as CompositeItemRecord;
                if (composite?.GetRecord<WeaponRecord>() != null)
                    hasWeapon = true;
                else
                    hasOtherGear = true;
            }
            var hasBodyWork = (preset.Augmentations?.Count ?? 0) > 0
                              || (preset.Implants?.Count ?? 0) > 0;

            if (hasWeapon)
                controller.PlayUiSound(sounds.EquipWeapon, isUnique: true);
            if (hasOtherGear || hasBodyWork || preset.IncludesCarriedItems)
                controller.PlayUiSound(sounds.TakeItem, isUnique: true);
        }
    }
}
