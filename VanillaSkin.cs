using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using MGSC;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace QM_CentralManagement
{
    /// <summary>
    /// Single entry point for vanilla art, palette and typography.
    ///
    /// Sprites are harvested from the LIVE hierarchy rather than looked up by
    /// path: Resources.FindObjectsOfTypeAll is used nowhere in the game's own
    /// code, returns every loaded Sprite, and sprite names are not unique
    /// across atlases. Walking the screen we already parent into plus
    /// UI.ScreenRoot (the canvas root vanilla itself uses in
    /// CommonContextMenu.Show) reaches everything this panel needs.
    /// </summary>
    internal static class VanillaSkin
    {
        // Frames. All 32x32 with a real 9-slice border.
        internal const string PanelFrame = "tooltypeGreenBorder";        // border 4,4,4,4
        internal const string HeaderFrame = "tooltypeGreenHeaderBorder"; // border 4,3,3,3
        internal const string DangerFrame = "tooltypeRedBorder";         // border 4,4,4,4

        // Dropdown / input chrome.
        internal const string ListBackground = "dropdownListBackground"; // 5x5, border 2
        // 20x5 with border {2,2,17,2}: the right 17px is a fixed well the
        // caret sits in, so only the left edge stretches.
        internal const string FieldBackground = "dropdownBackground";
        internal const string FieldBackgroundBlocked = "dropdownBackground_blocked";
        internal const string CaretArrow = "dropdownArrow";              // 10x6, border 0
        internal const string CaretArrowBlocked = "dropdownArrow_blocked";
        internal const float CaretWellW = 17f;
        internal const float CaretW = 10f;
        internal const float CaretH = 6f;

        // Text buttons. 7x7, border 2,2,2,2. Pressed doubles as the latched state.
        internal const string ButtonNormal = "RegularButton_Normal";
        internal const string ButtonHover = "RegularButton_Hover";
        internal const string ButtonPressed = "RegularButton_Pressed";
        internal const string ButtonDisabled = "RegularButton_Disabled";

        // NOTE: there is deliberately no red button sprite here.
        // ButtonRed_normal / _mouseOver / _pressed exist on disk but are only
        // referenced by game-editor prefabs (gameeditor/CreatePresetWindow,
        // LightCompInspector, PresetRulePanel), which never load in a normal
        // session -- a runtime harvest can never find them.  Vanilla's own
        // destructive control, the ArsenalScreen Recycle button, is a plain
        // RegularButton_* and signals intent through its caption, so mod
        // destructive buttons do the same with a red caption.

        // Item cells. 24x24, border 3,3,3,3.
        internal const string SlotNormal = "slothItem";
        internal const string SlotSelected = "slothHovered";

        /// <summary>
        /// Everything the panel needs. Used to decide whether a harvest was
        /// complete, so a Seed that ran before the relevant views existed can
        /// be retried on the next build instead of leaving the panel unskinned.
        /// </summary>
        private static readonly string[] RequiredSprites =
        {
            PanelFrame, HeaderFrame, ListBackground,
            ButtonNormal, ButtonHover, ButtonPressed, ButtonDisabled,
            SlotNormal, SlotSelected,
            FieldBackground, CaretArrow,
        };

        private static FieldInfo[] _itemSlotSpriteFields;

        private static Dictionary<string, Sprite> _sprites;
        private static bool _complete;
        private static bool _warnedMissing;

        /// <summary>
        /// Collect every Sprite reachable from the live UI. Cheap to call on
        /// every panel build; it returns immediately once the full set resolved.
        /// </summary>
        internal static void Seed(Transform screenRoot)
        {
            if (_complete)
                return;
            if (_sprites == null)
                _sprites = new Dictionary<string, Sprite>(256, StringComparer.Ordinal);

            try
            {
                Harvest(screenRoot);
                var canvasRoot = UI.ScreenRoot;
                if (canvasRoot != null)
                    Harvest(canvasRoot.root);
            }
            catch (Exception e)
            {
                Debug.LogWarning(Plugin.LogPrefix + "sprite harvest failed: " + e);
                return;
            }

            var missing = new List<string>();
            foreach (var name in RequiredSprites)
            {
                if (!_sprites.ContainsKey(name))
                    missing.Add(name);
            }

            _complete = missing.Count == 0;
            if (_complete)
            {
                Plugin.DebugLog("VanillaSkin resolved every required sprite ("
                                + _sprites.Count + " harvested).");
            }
            else
            {
                Debug.LogWarning(Plugin.LogPrefix + "VanillaSkin harvested "
                                 + _sprites.Count + " sprites but is still missing: "
                                 + string.Join(", ", missing.ToArray())
                                 + ". Those surfaces stay on flat colours; the "
                                 + "harvest retries on the next panel build.");
            }
        }

        private static void Harvest(Transform root)
        {
            if (root == null)
                return;

            foreach (var image in root.GetComponentsInChildren<Image>(true))
                Remember(image.sprite);

            // CommonButton holds the hover/pressed/disabled art that is never
            // assigned to an Image until the pointer actually enters it, so the
            // Image sweep above cannot see those states.
            foreach (var button in root.GetComponentsInChildren<CommonButton>(true))
            {
                Remember(button.normalBgSprite);
                Remember(button.hoverBgSprite);
                Remember(button.pressedBgSprite);
                Remember(button.disabledBgSprite);
            }

            // ItemSlot keeps all nine of its state sprites in private
            // [SerializeField] fields and only ever pushes one into its Image,
            // so slothHovered / slothEmpty are invisible to both sweeps above.
            HarvestItemSlots(root);
        }

        private static void HarvestItemSlots(Transform root)
        {
            var slots = root.GetComponentsInChildren<ItemSlot>(true);
            if (slots.Length == 0)
                return;

            if (_itemSlotSpriteFields == null)
            {
                var fields = new List<FieldInfo>();
                foreach (var field in AccessTools.GetDeclaredFields(typeof(ItemSlot)))
                {
                    if (field.FieldType == typeof(Sprite))
                        fields.Add(field);
                }
                _itemSlotSpriteFields = fields.ToArray();
            }

            foreach (var slot in slots)
            {
                foreach (var field in _itemSlotSpriteFields)
                    Remember(field.GetValue(slot) as Sprite);
            }
        }

        private static void Remember(Sprite sprite)
        {
            if (sprite == null || string.IsNullOrEmpty(sprite.name))
                return;
            if (!_sprites.ContainsKey(sprite.name))
                _sprites[sprite.name] = sprite;
        }

        internal static Sprite S(string name)
        {
            if (_sprites != null && _sprites.TryGetValue(name, out var sprite))
                return sprite;

            if (!_warnedMissing)
            {
                _warnedMissing = true;
                Debug.LogWarning(Plugin.LogPrefix + "vanilla sprite '" + name
                                 + "' not found; falling back to flat colours.");
            }
            return null;
        }

        /// <summary>
        /// 9-slice a surface with vanilla art.
        ///
        /// pixelsPerUnitMultiplier is load-bearing: the game's CanvasScaler has
        /// referencePixelsPerUnit = 1 while every sprite is authored at 100
        /// pixels-per-unit, so Image.pixelsPerUnit resolves to 100 and only
        /// 0.01 puts one sprite pixel back on one canvas unit. This constant is
        /// specific to this game's canvas -- on a default Unity canvas it is 1.
        /// </summary>
        internal static Image Slice(GameObject target, string spriteName, Color fallback)
        {
            var image = target.GetComponent<Image>() ?? target.AddComponent<Image>();
            var sprite = S(spriteName);
            if (sprite == null)
            {
                // Keep the mod usable if a future patch renames the art.
                image.sprite = null;
                image.color = fallback;
                return image;
            }

            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.pixelsPerUnitMultiplier = 0.01f;
            image.color = Color.white; // the sprite carries the colour now
            return image;
        }

        /// <summary>
        /// For border-less art. SortingButton_* / filtersButton_* / dropdownArrow
        /// all have a zero 9-slice border, so slicing them would be wrong.
        /// </summary>
        internal static Image Simple(GameObject target, string spriteName, Color fallback)
        {
            var image = target.GetComponent<Image>() ?? target.AddComponent<Image>();
            var sprite = S(spriteName);
            if (sprite == null)
            {
                image.sprite = null;
                image.color = fallback;
                return image;
            }

            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.color = Color.white;
            return image;
        }

        /// <summary>
        /// Swap a sliced Image's sprite without disturbing the slice settings.
        /// Used for latched/selected states -- writing Image.color instead would
        /// multiply against the vanilla art and tint the frame.
        /// </summary>
        internal static void SetSprite(Image image, string spriteName, Color fallback)
        {
            if (image == null)
                return;
            var sprite = S(spriteName);
            if (sprite == null)
            {
                // Only fall back to a flat colour if there is no art to
                // preserve.  Tinting an Image that already carries a vanilla
                // sprite multiplies against it and discolours the frame.
                if (image.sprite == null)
                    image.color = fallback;
                return;
            }
            image.sprite = sprite;
            image.color = Color.white;
        }

        /// <summary>
        /// Apply the game's font, size and weight for a text context.
        ///
        /// Localization.ActualizeFontAndSize early-returns on empty text, and
        /// this panel builds every label empty and fills it later, so a naive
        /// call would silently leave TMP's default size in place. Stand a
        /// throwaway glyph in so the vanilla path runs, then restore.
        /// </summary>
        internal static void ApplyFont(TextMeshProUGUI text, TextContext context)
        {
            if (text == null)
                return;

            try
            {
                var original = text.text;
                var empty = string.IsNullOrEmpty(original);
                if (empty)
                    text.text = "0";

                Localization.ActualizeFontAndSize(text, context);

                if (empty)
                    text.text = original ?? string.Empty;
            }
            catch (Exception e)
            {
                Debug.LogWarning(Plugin.LogPrefix + "ApplyFont failed: " + e);
            }
        }

        private static AccessTools.FieldRef<Navigable, bool> _registerNavigableRef;
        private static bool _navigationRefResolved;

        /// <summary>
        /// Keep mod controls out of the game's gamepad navigation ring.
        ///
        /// CommonButton derives from Navigable, whose OnEnable registers with
        /// UI.Navigation. The panel adds ~30 of them on top of a screen that
        /// already has its own navigation order, and this pass is about visuals
        /// and audio -- not about changing controller behaviour. Opting out
        /// keeps that surface exactly as it was before the re-skin.
        /// </summary>
        internal static void SuppressNavigation(Navigable navigable)
        {
            if (navigable == null)
                return;

            if (!_navigationRefResolved)
            {
                _navigationRefResolved = true;
                try
                {
                    _registerNavigableRef =
                        AccessTools.FieldRefAccess<Navigable, bool>("_registerNavigable");
                }
                catch (Exception e)
                {
                    Debug.LogWarning(Plugin.LogPrefix
                                     + "could not bind Navigable._registerNavigable; "
                                     + "mod buttons will join gamepad navigation: " + e);
                }
            }

            if (_registerNavigableRef == null)
                return;

            try
            {
                _registerNavigableRef(navigable) = false;
            }
            catch (Exception e)
            {
                Debug.LogWarning(Plugin.LogPrefix + "SuppressNavigation failed: " + e);
            }
        }

        /// <summary>
        /// Attaches the game's own hover-hint tooltip to a mod control.
        ///
        /// This was switched off for a while on the belief that
        /// TooltipFactory.ShowSimpleTextTooltip carries no positioning -- its
        /// body really is just SetActive(true) plus Initialize. But the
        /// positioning is one level down: SimpleTextTooltip.Initialize ends
        /// with RecalculatePosition(Input.mousePosition), and
        /// BaseTooltip.RecalculatePosition places the rect at the cursor and
        /// clamps it against the right and bottom screen edges. Vanilla drives
        /// the identical path from HintTooltipHandler, which StarmapScreen even
        /// adds at runtime exactly the way this method does.
        ///
        /// So the tooltip is not broken -- but it was still switched back off
        /// after testing, for a plainer reason: it appears BELOW-RIGHT of the
        /// cursor, and these panels are dense grids of tabs and rows. Hovering
        /// a filter tab covers the two tabs under it; the hint costs more than
        /// the one line of text is worth. Vanilla only puts hints on isolated
        /// controls, which is the shape this pays off in.
        ///
        /// The call sites and localisation tags are kept: turning this back on
        /// is one bool, and the widgets that WOULD earn a hint (the trade row's
        /// sweep interaction, which nothing else explains) are already tagged.
        ///
        /// One further caveat if it is re-enabled: in controller / keyboard-only
        /// input modes RecalculatePosition snaps to UI.Navigation.Selected
        /// rather than the cursor, and mod controls are deliberately kept out of
        /// that ring by SuppressNavigation.
        /// </summary>
        internal static void AddHint(GameObject target, string localizationTag)
        {
            if (!HintTooltipsEnabled)
                return;
            if (target == null || string.IsNullOrEmpty(localizationTag))
                return;
            if (target.GetComponent<HintTooltipHandler>() != null)
                return;
            target.AddComponent<HintTooltipHandler>().SetTag(localizationTag);
        }

        // static readonly, not const: a const would let the compiler fold the
        // guard away and warn that the body is unreachable.
        internal static readonly bool HintTooltipsEnabled = false;

        /// <summary>
        /// Vanilla palette. Colors is a SingletonMonoBehaviour whose accessors
        /// all dereference .Instance, so these MUST stay lazy -- reading one from
        /// a static field initializer would NRE or capture default(Color)
        /// depending on load order. Every entry falls back to the value the mod
        /// shipped with so a missing singleton degrades instead of crashing.
        /// </summary>
        internal static class Palette
        {
            private static bool Ready => SingletonMonoBehaviour<Colors>.Instance != null;

            /// <summary>Cream. Vanilla's "bright" is not a light green.</summary>
            internal static Color Bright =>
                Ready ? Colors.White : new Color(0.72f, 0.89f, 0.61f, 1f);

            internal static Color Value =>
                Ready ? Colors.Green : new Color(0.43f, 0.74f, 0.61f, 1f);

            internal static Color Accent =>
                Ready ? Colors.Yellow : new Color(0.93f, 0.83f, 0.26f, 1f);

            internal static Color Muted =>
                Ready ? Colors.DarkYellow : new Color(0.24f, 0.42f, 0.35f, 1f);

            internal static Color Border =>
                Ready ? Colors.AltGreen : new Color(0.31f, 0.61f, 0.48f, 0.97f);

            internal static Color Recessed =>
                Ready ? Colors.DarkGreen : new Color(0.035f, 0.12f, 0.09f, 1f);

            internal static Color Danger =>
                Ready ? Colors.LightRed : new Color(0.73f, 0.10f, 0.24f, 1f);

            /// <summary>The one scrim value vanilla uses for its own modals.</summary>
            internal static Color Scrim =>
                Ready ? Colors.ViewBackground : new Color(0f, 0f, 0f, 0.72f);

            // Caption states, matching the CommonButtons already in the screen.
            internal static Color CaptionPressed =>
                new Color(0f, 0.003921569f, 0.007843138f, 1f);

            internal static Color CaptionDisabled =>
                new Color(0.2901961f, 0.078431375f, 0.08627451f, 1f);
        }
    }
}
