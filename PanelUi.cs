using System;
using MGSC;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace QM_CentralManagement
{
    /// <summary>
    /// Shared factory for mod-built panels. These helpers were originally
    /// private statics inside CentralManagementPanel; the station trade panel
    /// needs the same vanilla look (9-slice frames, CommonButton states, the
    /// game's TMP font presets), so they live here now. CentralManagementPanel
    /// keeps its own copies so this extraction cannot disturb it.
    /// </summary>
    internal static class PanelUi
    {
        // Surface fills. Only reached when a vanilla sprite cannot be resolved:
        // a skinned surface leaves Image.color at white so the 9-slice art
        // carries the colour, and any other value would multiply against it.
        internal static readonly Color PanelColor =
            new Color(0.006f, 0.018f, 0.015f, 0.99f);
        internal static readonly Color HeaderColor =
            new Color(0.018f, 0.055f, 0.044f, 1f);
        internal static readonly Color CardColor =
            new Color(0.012f, 0.038f, 0.031f, 1f);

        // Text and accents come from the game's own palette. These MUST stay
        // properties: Colors is a SingletonMonoBehaviour and every accessor
        // dereferences .Instance, so a static field initializer evaluated at
        // type load would NRE or silently capture default(Color).
        internal static Color SelectedColor => VanillaSkin.Palette.Recessed;
        internal static Color BrightColor => VanillaSkin.Palette.Bright;
        internal static Color ValueColor => VanillaSkin.Palette.Value;
        internal static Color AccentColor => VanillaSkin.Palette.Accent;
        internal static Color OffColor => VanillaSkin.Palette.Muted;
        internal static Color DangerColor => VanillaSkin.Palette.Danger;

        internal static GameObject CreateUiObject(string name, Transform parent)
        {
            var result = new GameObject(name, typeof(RectTransform),
                typeof(CanvasRenderer));
            result.transform.SetParent(parent, false);
            result.layer = parent.gameObject.layer;
            return result;
        }

        /// <summary>
        /// The design-space size of the surface a mod panel is parented to --
        /// in practice the screen root, which is stretched to the canvas.
        ///
        /// This is NOT a constant 640x360. MGSC.UIScaleFixer rewrites the
        /// CanvasScaler from the monitor's aspect ratio every time the
        /// resolution changes:
        ///   aspect &lt; 2.3   ScaleWithScreenSize, match WIDTH
        ///                   -> 640 x 640/aspect      (640x360 at 16:9)
        ///   2.3 .. 3.0      ScaleWithScreenSize, match 0.5
        ///                   -> sqrt(307200*aspect) wide, /aspect tall
        ///                      (853x360 at 21:9)
        ///   aspect &gt;= 3.0   ConstantPixelSize, scaleFactor 3
        ///                   -> screen/3             (1280x360 at 32:9)
        /// So an ultrawide screen makes the design space WIDER, not shorter,
        /// and any panel pinned to a corner ends up hugging that corner with
        /// a third of the screen empty beside it. Measure, then centre.
        /// </summary>
        internal static Vector2 DesignSurfaceSize(Transform parent,
            Vector2 fallback)
        {
            if (!(parent is RectTransform rect))
                return fallback;
            var size = rect.rect.size;
            // A rect that has never been laid out reads back as zero; the
            // 16:9 design size is the safe answer until it has.
            return size.x < 1f || size.y < 1f ? fallback : size;
        }

        /// <summary>
        /// Centres a panel on its parent surface. Anchoring to the centre
        /// (rather than a corner) is what makes the placement survive the
        /// scaler swaps described on <see cref="DesignSurfaceSize"/>.
        /// </summary>
        internal static void SetCentered(RectTransform rect, float width,
            float height)
        {
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = Vector2.zero;
            rect.sizeDelta = new Vector2(width, height);
        }

        internal static void SetTopLeft(RectTransform rect, float x, float y,
            float width, float height)
        {
            rect.anchorMin = new Vector2(0f, 1f);
            rect.anchorMax = new Vector2(0f, 1f);
            rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(width, height);
        }

        /// <summary>
        /// Edge-anchored placement: the element sticks to the given corner and
        /// tracks every later resize of its parent. Offsets are measured from
        /// the edge: AnchorTopRight with pos (-4,-2) sits 4px from the right
        /// edge and 2px from the top.
        /// </summary>
        internal static void AnchorTopRight(RectTransform rect,
            Vector2 pos, Vector2 size)
        {
            rect.anchorMin = new Vector2(1f, 1f);
            rect.anchorMax = new Vector2(1f, 1f);
            rect.pivot = new Vector2(1f, 1f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
        }

        internal static void AnchorBottomRight(RectTransform rect,
            Vector2 pos, Vector2 size)
        {
            rect.anchorMin = new Vector2(1f, 0f);
            rect.anchorMax = new Vector2(1f, 0f);
            rect.pivot = new Vector2(1f, 0f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
        }

        internal static void AnchorBottomLeft(RectTransform rect,
            Vector2 pos, Vector2 size)
        {
            rect.anchorMin = new Vector2(0f, 0f);
            rect.anchorMax = new Vector2(0f, 0f);
            rect.pivot = new Vector2(0f, 0f);
            rect.anchoredPosition = pos;
            rect.sizeDelta = size;
        }

        /// <summary>
        /// Stretches to fill the parent with the given edge insets, so the
        /// element follows the parent's real size (used by the panel, which
        /// is measured from the vanilla UI instead of hardcoded).
        /// </summary>
        internal static void AnchorStretch(RectTransform rect,
            Vector2 offsetMin, Vector2 offsetMax)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
        }

        /// <summary>
        /// Anchor a child to fill its parent, so it tracks every later resize
        /// of that parent.
        /// </summary>
        internal static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// Anchor a child to fill its parent with a horizontal inset.
        /// </summary>
        internal static void StretchInto(RectTransform rect, float inset)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = new Vector2(inset, 0f);
            rect.offsetMax = new Vector2(-inset, 0f);
        }

        internal static TextMeshProUGUI CreateText(string name, Transform parent,
            float x, float y, float width, float height, TextContext context,
            Color color, TextAlignmentOptions alignment)
        {
            return CreateText(name, parent, x, y, width, height, context, 0f,
                color, alignment);
        }

        /// <summary>
        /// <paramref name="sizeOverride"/> is honoured only for
        /// TextContext.IgnoreSize, which the game's font presets deliberately
        /// leave unstyled -- that is the vanilla-sanctioned way to take the
        /// font asset and material without taking the type scale.
        /// </summary>
        internal static TextMeshProUGUI CreateText(string name, Transform parent,
            float x, float y, float width, float height, TextContext context,
            float sizeOverride, Color color, TextAlignmentOptions alignment)
        {
            var root = CreateUiObject(name, parent);
            SetTopLeft((RectTransform)root.transform, x, y, width, height);
            var text = root.AddComponent<TextMeshProUGUI>();
            ConfigureText(text, context, sizeOverride, color, alignment);
            return text;
        }

        internal static void ConfigureText(TextMeshProUGUI text,
            TextContext context, float sizeOverride, Color color,
            TextAlignmentOptions alignment)
        {
            if (sizeOverride > 0f)
                text.fontSize = sizeOverride;
            text.color = color;
            text.alignment = alignment;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            // Assigns the font asset, restores the game's TMP material preset,
            // applies the context's size and weight, and honours the LargeText
            // accessibility setting -- none of which GetActualFont alone did.
            VanillaSkin.ApplyFont(text, context);
        }

        internal static GameObject CreateButtonRoot(string name,
            Transform parent, float x, float y, float width, float height,
            out Image background, out TextMeshProUGUI label)
        {
            return CreateButtonRoot(name, parent, x, y, width, height, null,
                out background, out label);
        }

        /// <summary>
        /// Builds a vanilla CommonButton: sprite hover/press/disabled states,
        /// the game's four caption colours and its ButtonClick sound, all of
        /// which UnityEngine.UI.Button's ColorTint could not provide.
        /// </summary>
        internal static GameObject CreateButtonRoot(string name,
            Transform parent, float x, float y, float width, float height,
            string tooltipTag, out Image background, out TextMeshProUGUI label)
        {
            var root = CreateUiObject(name, parent);
            SetTopLeft((RectTransform)root.transform, x, y, width, height);

            // CommonButton.OnEnable calls RefreshVisual, which dereferences
            // background.sprite -- and AddComponent on an already-active object
            // runs Awake and OnEnable immediately.  Assemble the whole control
            // while the object is inactive, then restore its previous state.
            var wasActive = root.activeSelf;
            root.SetActive(false);

            background = VanillaSkin.Slice(root, VanillaSkin.ButtonNormal,
                CardColor);
            var captionSize = height >= 17f ? 8f : (height >= 14f ? 7f : 6f);
            label = CreateText("Label", root.transform, 1f, 0f,
                width - 2f, height, TextContext.IgnoreSize, captionSize,
                ValueColor, TextAlignmentOptions.Center);
            StretchInto(label.rectTransform, 2f);

            // Must precede the CommonButton: its Awake caches the
            // GetComponent<ITooltipHandler>() it reuses for gamepad focus.
            VanillaSkin.AddHint(root, tooltipTag);

            var button = root.AddComponent<CommonButton>();
            VanillaSkin.SuppressNavigation(button);
            button.background = background;
            button.captionText = label;
            button.normalBgSprite = background.sprite;
            button.hoverBgSprite = VanillaSkin.S(VanillaSkin.ButtonHover);
            button.pressedBgSprite = VanillaSkin.S(VanillaSkin.ButtonPressed);
            button.disabledBgSprite = VanillaSkin.S(VanillaSkin.ButtonDisabled);
            button.normalCaptionColor = ValueColor;
            button.hoverCaptionColor = BrightColor;
            button.pressedCaptionColor = VanillaSkin.Palette.CaptionPressed;
            button.disabledCaptionColor = VanillaSkin.Palette.CaptionDisabled;

            root.SetActive(wasActive);
            return root;
        }

        /// <summary>
        /// A control that reads as "opens a list": the vanilla field frame plus
        /// the caret sitting in the well that frame reserves on its right.
        /// </summary>
        internal static GameObject CreateDropdownTrigger(string name,
            Transform parent, float x, float y, float width, float height,
            out TextMeshProUGUI label)
        {
            var root = CreateUiObject(name, parent);
            SetTopLeft((RectTransform)root.transform, x, y, width, height);

            var wasActive = root.activeSelf;
            root.SetActive(false);

            var background = VanillaSkin.Slice(root,
                VanillaSkin.FieldBackground, CardColor);

            label = CreateText("Label", root.transform, 0f, 0f, width, height,
                TextContext.IgnoreSize, 7f, ValueColor,
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
            VanillaSkin.Simple(caretObject, VanillaSkin.CaretArrow, ValueColor)
                .raycastTarget = false;

            var button = root.AddComponent<CommonButton>();
            VanillaSkin.SuppressNavigation(button);
            button.background = background;
            button.captionText = label;
            // dropdownBackground ships no hover or pressed variant, so the
            // frame holds still and the caption colour carries the feedback.
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
            button.disabledCaptionColor = VanillaSkin.Palette.CaptionDisabled;

            root.SetActive(wasActive);
            return root;
        }

        /// <summary>
        /// The popup body for dropdown lists: a plain list-background well.
        /// Its size and row list are owned by the caller.
        /// </summary>
        internal static GameObject CreateDropdownRoot(string name,
            Transform parent, float x, float y, float width, float height)
        {
            var root = CreateUiObject(name, parent);
            SetTopLeft((RectTransform)root.transform, x, y, width, height);
            var background = VanillaSkin.Slice(root, VanillaSkin.ListBackground,
                PanelColor);
            background.raycastTarget = true;
            root.SetActive(false);
            return root;
        }

        /// <summary>
        /// A clipped, wheel-and-drag scrollable list. No scrollbar: the game's
        /// own trade list has none either, and the wheel is the primary input.
        /// The returned content RectTransform is top-left anchored; the caller
        /// sizes it and stacks rows onto it.
        /// </summary>
        internal static GameObject CreateScrollArea(string name,
            Transform parent, float x, float y, float width, float height,
            out ScrollRect scroll, out RectTransform content)
        {
            var root = CreateUiObject(name, parent);
            SetTopLeft((RectTransform)root.transform, x, y, width, height);

            var viewport = CreateUiObject("Viewport", root.transform);
            var viewportRect = (RectTransform)viewport.transform;
            Stretch(viewportRect);
            viewportRect.offsetMin = new Vector2(1f, 1f);
            viewportRect.offsetMax = new Vector2(-1f, -1f);
            viewport.AddComponent<RectMask2D>();
            // A transparent catcher so the wheel works over empty list space.
            var catcher = viewport.AddComponent<Image>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;

            var contentObject = CreateUiObject("Content", viewport.transform);
            content = (RectTransform)contentObject.transform;
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = new Vector2(width - 2f, height - 2f);

            scroll = root.AddComponent<ScrollRect>();
            scroll.viewport = viewportRect;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 16f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;
            return root;
        }

        /// <summary>
        /// A single-line TMP input field restricted to non-negative integers,
        /// styled with the vanilla field frame. Used for row quantities.
        /// </summary>
        internal static GameObject CreateIntegerInput(string name,
            Transform parent, float x, float y, float width, float height,
            out TMP_InputField input, out TextMeshProUGUI text)
        {
            var root = CreateUiObject(name, parent);
            SetTopLeft((RectTransform)root.transform, x, y, width, height);

            var background = VanillaSkin.Slice(root,
                VanillaSkin.ListBackground, CardColor);
            background.raycastTarget = true;

            var viewport = CreateUiObject("Viewport", root.transform);
            var viewportRect = (RectTransform)viewport.transform;
            Stretch(viewportRect);
            viewportRect.offsetMin = new Vector2(3f, 1f);
            viewportRect.offsetMax = new Vector2(-3f, -1f);
            viewport.AddComponent<RectMask2D>();

            var textObject = CreateUiObject("Text", viewport.transform);
            var textRect = (RectTransform)textObject.transform;
            Stretch(textRect);
            text = textObject.AddComponent<TextMeshProUGUI>();
            ConfigureText(text, TextContext.IgnoreSize, 7f, ValueColor,
                TextAlignmentOptions.Center);

            input = root.AddComponent<TMP_InputField>();
            input.targetGraphic = background;
            input.textViewport = viewportRect;
            input.textComponent = text;
            input.lineType = TMP_InputField.LineType.SingleLine;
            input.contentType = TMP_InputField.ContentType.IntegerNumber;
            input.richText = false;
            input.customCaretColor = true;
            input.caretColor = AccentColor;
            input.selectionColor = new Color(0.25f, 0.58f, 0.49f, 0.55f);
            return root;
        }

        internal static CommonButton ButtonOf(GameObject root)
        {
            return root == null ? null : root.GetComponent<CommonButton>();
        }

        internal static void BindClick(GameObject root, Action action)
        {
            var button = ButtonOf(root);
            if (button != null)
                button.OnClick += (_, __) => action();
        }

        /// <summary>
        /// Latched / selected state. Never write Image.color once a sprite is
        /// assigned -- Image.color multiplies against the art and would tint
        /// the vanilla frame. Move the button's own normal state instead, so
        /// the latch survives the pointer (CommonButton.Select does not).
        /// </summary>
        internal static void SetSurfaceSelected(Image image, bool selected)
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

        /// <summary>
        /// Recolour a button caption durably. Writing TMP.color alone is not
        /// enough: CommonButton repaints captionText from its own four colour
        /// fields on every enter, exit, down and up.
        /// </summary>
        internal static void SetCaptionColor(TextMeshProUGUI label, Color color)
        {
            if (label == null)
                return;
            label.color = color;
            var button = label.GetComponentInParent<CommonButton>();
            if (button != null && ReferenceEquals(button.captionText, label))
                button.normalCaptionColor = color;
        }

        internal static void PlayVanillaButtonClick()
        {
            var controller = SingletonMonoBehaviour<SoundController>.Instance;
            var sounds = SingletonMonoBehaviour<SoundsStorage>.Instance;
            if (controller != null && sounds?.ButtonClick != null)
                controller.PlayUiSound(sounds.ButtonClick, isUnique: true);
        }
    }
}
