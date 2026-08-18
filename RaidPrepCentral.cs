using System;
using HarmonyLib;
using MGSC;
using TMPro;
using UnityEngine;

namespace QM_CentralManagement
{
    /// <summary>
    /// Central management, reachable from the mission briefing.
    ///
    /// PrepareRaidScreen is where a run is actually planned -- read the
    /// briefing, pick the operator, then gear them up -- but the only door
    /// into central management used to be the spaceship screen. A player who
    /// wanted the full catalogue while preparing had to leave the briefing,
    /// walk back to the Magnum, and pick the mission again afterwards. This
    /// adds a second door that opens the panel on the operator the briefing
    /// has ALREADY selected, with that operator's equipment expanded beside
    /// the catalogue.
    ///
    /// The button is a mirror of the screen's own "start operation" button:
    /// same parent rect, same height, same drop below the window, anchored to
    /// the opposite corner. Every one of those numbers is measured off the
    /// vanilla button at runtime rather than hardcoded, because the design
    /// space is not a constant 640x360 -- MGSC.UIScaleFixer swaps the
    /// CanvasScaler by aspect ratio, so anything positioned in absolute screen
    /// coordinates drifts on ultrawide.
    ///
    /// It is built from PanelUi rather than cloned from the vanilla button on
    /// purpose. That button is a HotkeyButton whose KeyId is "Confirmation",
    /// and Navigation.ExecuteHotkey fires EVERY registered navigable whose
    /// KeyId matches the pressed key -- a clone would silently open central
    /// management every time the player pressed Enter to start the operation.
    /// PanelUi.CreateButtonRoot builds a plain CommonButton out of the same
    /// harvested vanilla sprites and calls VanillaSkin.SuppressNavigation on
    /// it, so it looks native and claims no key of its own.
    /// </summary>
    internal sealed class RaidPrepCentralButton : MonoBehaviour
    {
        /// <summary>
        /// Clearance kept from the start-operation button. The mod button
        /// takes whatever is left of the window's width after that.
        /// </summary>
        private const float SideGap = 6f;
        private const float MinWidth = 90f;
        private const float MaxWidth = 150f;
        private const float CaptionSize = 7f;
        // Only used if the window rect cannot be measured, which should not
        // happen: RefreshMercenary runs from OnEnable and Background is a
        // fixed-size rect, not a layout-driven one.
        private const float FallbackWidth = 150f;

        private PrepareRaidScreen _screen;
        private CommonButton _sourceButton;
        private GameObject _root;
        private TextMeshProUGUI _label;
        private bool _built;
        private bool _visible;

        internal static void RefreshFor(PrepareRaidScreen screen)
        {
            if (screen == null)
                return;
            var button = screen.GetComponent<RaidPrepCentralButton>();
            if (button == null)
            {
                // Nothing to hide and nothing to show: never attach the
                // component at all while the feature is switched off.
                if (!Plugin.RaidPrepCentralEnabled)
                    return;
                button = screen.gameObject.AddComponent<RaidPrepCentralButton>();
            }
            button.Refresh(screen);
        }

        private void Refresh(PrepareRaidScreen screen)
        {
            _screen = screen;
            _sourceButton = Plugin.RaidPrepStartButtonOf(screen);

            // Same visibility rule as the vanilla "select equipment" button --
            // it needs an operator to gear up -- plus this mod's own two
            // gates. Reusing the vanilla rule means the row of briefing
            // actions never appears half-populated.
            var usable = Plugin.RaidPrepCentralEnabled
                         && Plugin.IsTechnologyUnlocked()
                         && Plugin.RaidPrepMercenaryOf(screen) != null
                         && _sourceButton != null;
            if (!usable)
            {
                Hide();
                return;
            }

            try
            {
                if (!_built)
                    Build();
            }
            catch (Exception e)
            {
                Debug.LogError(Plugin.LogPrefix
                               + "briefing central button build failed: " + e);
                _built = false;
                Hide();
                return;
            }
            if (_root == null)
            {
                Hide();
                return;
            }

            Place();
            RefreshLabel();
            _root.SetActive(true);
            _visible = true;
        }

        private void Hide()
        {
            if (_root != null)
                _root.SetActive(false);
            _visible = false;
        }

        private void Build()
        {
            var sourceRect = _sourceButton.transform as RectTransform;
            var parent = sourceRect == null ? null : sourceRect.parent;
            if (parent == null)
                throw new InvalidOperationException(
                    "the start operation button has no parent rect");

            // Collect the game's own button art before building: PanelUi asks
            // VanillaSkin for every sprite it uses.
            VanillaSkin.Seed(parent);

            _root = PanelUi.CreateButtonRoot("QM_RaidPrepCentralManagement",
                parent, 0f, 0f, MaxWidth, sourceRect.rect.height,
                "qmcentral.tip.raidprep", out _, out _label);
            // One notch above PanelUi's default for a 13-unit control: this
            // button shares a line with vanilla captions, not with the dense
            // controls inside a mod panel.
            _label.fontSize = CaptionSize;
            PanelUi.BindClick(_root, Open);
            _root.SetActive(false);
            _built = true;
        }

        /// <summary>
        /// Mirrors the start-operation button into the window's other bottom
        /// corner, reading its inset and drop from the button itself so the
        /// two stay on one line whatever the vanilla prefab does.
        /// </summary>
        private void Place()
        {
            var sourceRect = _sourceButton.transform as RectTransform;
            var rect = _root.transform as RectTransform;
            if (sourceRect == null || rect == null)
                return;

            // The vanilla button is right-anchored with a right pivot, so its
            // x is negative and its magnitude IS the inset from the edge.
            var inset = Mathf.Abs(sourceRect.anchoredPosition.x);
            var drop = sourceRect.anchoredPosition.y;
            var height = sourceRect.rect.height;

            var width = FallbackWidth;
            var parentRect = sourceRect.parent as RectTransform;
            if (parentRect != null && parentRect.rect.width > 0f)
            {
                width = Mathf.Clamp(
                    parentRect.rect.width - sourceRect.rect.width
                    - inset * 2f - SideGap,
                    MinWidth, MaxWidth);
            }

            PanelUi.AnchorBottomLeft(rect, new Vector2(inset, drop),
                new Vector2(width, height));
            rect.localScale = Vector3.one;
            rect.localRotation = Quaternion.identity;
        }

        private void RefreshLabel()
        {
            if (_label == null)
                return;
            var caption = Localization.Get("qmcentral.caption.raidprep");
            // The shortcut belongs on the face of the button, not in a
            // tooltip: VanillaSkin.HintTooltipsEnabled is false, so the hint
            // tag this control passes is inert, and the key is configurable
            // enough that it cannot be baked into the localized string.
            var shortcut = Plugin.CentralShortcutLabel;
            if (shortcut != null)
                caption = "[" + shortcut + "] " + caption;
            _label.text = caption;
            _label.font = Localization.GetActualFont();
        }

        private void Update()
        {
            if (!_visible || _screen == null)
                return;
            if (Plugin.CentralShortcutKey == KeyCode.None)
                return;
            // The component keeps running while a dialog sits on top of the
            // briefing (the "no items" confirmation opens without hiding it),
            // so only answer the key while the briefing is the front screen.
            if (UI.IsAnyShowing(typeof(PrepareRaidScreen)))
                return;
            if (!InputHelper.GetKeyDown(Plugin.CentralShortcutKey))
                return;
            Open();
        }

        private void Open()
        {
            Plugin.OpenCentralManagementFromRaidPreparation(_screen);
        }
    }

    public static partial class Plugin
    {
        // config.txt switch, parsed in LoadConfig.
        internal static bool RaidPrepCentralEnabled { get; set; } = true;

        private static AccessTools.FieldRef<PrepareRaidScreen, Mercenary>
            _raidPrepMercenary;
        private static AccessTools.FieldRef<PrepareRaidScreen, CommonButton>
            _raidPrepStartButton;

        /// <summary>
        /// True while the central session on screen was opened from the
        /// mission briefing. It decides exactly one thing: where UI.Back goes.
        ///
        /// A briefing-born session MUST return to PrepareRaidScreen. That
        /// screen holds the mission, the chosen side and the selected
        /// operator; dropping the player on SpaceshipScreen instead would
        /// discard all three and make them pick the mission again.
        ///
        /// It is set by the briefing entry point and cleared at the only two
        /// places another destination becomes possible -- the spaceship entry
        /// point, and the moment a raid actually launches. Deliberately NOT
        /// cleared when the panel closes: the arsenal/augmentation hop inside
        /// a live session tears the screen down and rebuilds it, so a reset
        /// there would lose the way home halfway through.
        /// </summary>
        private static bool _centralOpenedFromRaidPrep;

        /// <summary>
        /// The shortcut key as it should read on a button face, or null when
        /// the player has disabled it. Alpha1..Alpha9 print as their digits.
        /// </summary>
        internal static string CentralShortcutLabel
        {
            get
            {
                if (_centralShortcutKey == KeyCode.None)
                    return null;
                return _centralShortcutKey.ToString()
                    .Replace("Alpha", string.Empty);
            }
        }

        private static void PatchRaidPrepCentral(Harmony harmony)
        {
            _raidPrepMercenary = AccessTools.FieldRefAccess<PrepareRaidScreen,
                Mercenary>("_mercenary");
            _raidPrepStartButton = AccessTools.FieldRefAccess<PrepareRaidScreen,
                CommonButton>("_startOperationButton");

            // RefreshMercenary is the screen's own "who is going has changed"
            // callback: OnEnable calls it, and so does every path that picks
            // or swaps an operator. Hooking it means the mod button appears
            // and disappears in lockstep with the vanilla equipment button
            // instead of needing a refresh rule of its own.
            PatchRequired(harmony, typeof(PrepareRaidScreen),
                nameof(PrepareRaidScreen.RefreshMercenary),
                postfix: nameof(RaidPrepRefreshMercenaryPostfix),
                argumentTypes: Type.EmptyTypes);
        }

        private static void RaidPrepRefreshMercenaryPostfix(
            PrepareRaidScreen __instance)
        {
            try
            {
                RaidPrepCentralButton.RefreshFor(__instance);
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix
                               + "briefing central button refresh failed: " + e);
            }
        }

        internal static Mercenary RaidPrepMercenaryOf(PrepareRaidScreen screen)
            => screen == null || _raidPrepMercenary == null
                ? null
                : _raidPrepMercenary(screen);

        internal static CommonButton RaidPrepStartButtonOf(
            PrepareRaidScreen screen)
            => screen == null || _raidPrepStartButton == null
                ? null
                : _raidPrepStartButton(screen);

        internal static bool OpenCentralManagementFromRaidPreparation(
            PrepareRaidScreen screen)
        {
            if (screen == null || !RaidPrepCentralEnabled
                               || !IsTechnologyUnlocked())
            {
                return false;
            }
            try
            {
                var mercenary = RaidPrepMercenaryOf(screen);
                if (mercenary == null)
                    return false;
                _centralOpenedFromRaidPrep = true;
                // showOperatorPanel: the briefing has already decided who is
                // going, so the panel opens with that agent's equipment
                // expanded beside the catalogue -- gearing them up is the
                // whole reason for being here. The spaceship entry, which has
                // no such context, still opens on the bare catalogue.
                if (OpenCentralArsenal(mercenary, showOperatorPanel: true))
                    return true;
                _centralOpenedFromRaidPrep = false;
                return false;
            }
            catch (Exception e)
            {
                _centralOpenedFromRaidPrep = false;
                Debug.LogError(LogPrefix
                               + "could not open central management from the "
                               + "mission briefing: " + e);
                return false;
            }
        }

        /// <summary>
        /// Finishes a central-mode screen chain with the right way home.
        ///
        /// The briefing case also hides what is underneath, exactly as
        /// PrepareRaidScreen's own "select equipment" button does: without it
        /// the briefing stays in the showing list and shows through wherever
        /// the mod panel does not reach. The spaceship case deliberately does
        /// not, matching vanilla's arsenal opening.
        /// </summary>
        private static void ShowCentralScreen<T>(UI.CmdChain<T> chain)
            where T : MonoBehaviour
        {
            if (_centralOpenedFromRaidPrep)
                chain.HideAll().Show().Fallback<PrepareRaidScreen>();
            else
                chain.Show().Fallback<SpaceshipScreen>();
        }
    }
}
