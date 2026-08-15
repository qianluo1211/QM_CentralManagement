using System;
using HarmonyLib;
using MGSC;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace QM_CentralManagement
{
    internal sealed class CentralManagementActionHost : MonoBehaviour
    {
        private CommonButton _source;
        private CommonButton _mercenaries;
        private GameObject _root;
        private CommonButton _button;
        private TextMeshProUGUI _shortcutText;
        private HotkeyIcon _shortcutIcon;

        internal void Initialize(CommonButton arsenal, CommonButton mercenaries)
        {
            if (_root != null || arsenal == null)
                return;

            _source = arsenal;
            _mercenaries = mercenaries;
            var sourceRect = arsenal.transform as RectTransform;
            var otherRect = mercenaries == null
                ? null
                : mercenaries.transform as RectTransform;
            if (sourceRect == null || otherRect == null)
                return;

            // Use the actual M button prefab hierarchy as the visual and
            // geometric template. This preserves every original sprite,
            // padding, hover state and key-panel baseline.
            _root = Instantiate(mercenaries.gameObject,
                mercenaries.transform.parent, false);
            _root.name = "QM_CentralManagementAction";
            var rootRect = (RectTransform)_root.transform;
            rootRect.anchoredPosition = otherRect.anchoredPosition
                                        - (sourceRect.anchoredPosition
                                           - otherRect.anchoredPosition);

            _button = _root.GetComponent<CommonButton>();
            if (_button == null)
            {
                Destroy(_root);
                _root = null;
                return;
            }
            _button.OnClick += ButtonOnClick;

            ConfigureShortcutPanel(_root);
            ReplaceActionArtwork(_button, Plugin.ActionNormalSprite,
                Plugin.ActionHoverSprite, Plugin.ActionPressedSprite);

            var tooltips = _root.GetComponents<HintTooltipHandler>();
            if (tooltips.Length == 0)
                _root.AddComponent<HintTooltipHandler>()
                    .SetTag("qmcentral.title");
            else
                foreach (var tooltip in tooltips)
                    tooltip.SetTag("qmcentral.title");
            RefreshText();
            _root.SetActive(false);
        }

        private void RefreshText()
        {
            if (_shortcutIcon != null)
            {
                if (Plugin.CentralShortcutKey == KeyCode.None)
                    _shortcutIcon.InitializeAsNotSetBind();
                else
                    _shortcutIcon.Initialize(Plugin.CentralShortcutKey);
                return;
            }
            if (_shortcutText != null)
            {
                _shortcutText.text = Plugin.CentralShortcutKey == KeyCode.None
                    ? "—"
                    : Plugin.CentralShortcutKey.ToString()
                        .Replace("Alpha", string.Empty);
                _shortcutText.font = Localization.GetActualFont();
            }
        }

        private void ConfigureShortcutPanel(GameObject root)
        {
            var keyPanel = root.GetComponentInChildren<GameKeyPanel>(true);
            if (keyPanel == null)
                return;
            keyPanel.enabled = false;
            var iconsField = AccessTools.Field(typeof(GameKeyPanel),
                "_iconsRoot");
            var iconsRoot = iconsField?.GetValue(keyPanel) as Transform;
            if (iconsRoot == null)
                return;
            var keyIdField = AccessTools.Field(typeof(GameKeyPanel),
                "_keyId");
            keyIdField?.SetValue(keyPanel, string.Empty);
            var hotkeys = iconsRoot.GetComponentsInChildren<HotkeyIcon>(true);
            if (hotkeys.Length > 0)
            {
                _shortcutIcon = hotkeys[0];
                _shortcutIcon.gameObject.SetActive(true);
                for (var i = 1; i < hotkeys.Length; i++)
                    hotkeys[i].gameObject.SetActive(false);
                return;
            }
            var labels = iconsRoot.GetComponentsInChildren<TextMeshProUGUI>(
                true);
            if (labels.Length > 0)
            {
                _shortcutText = labels[0];
                for (var i = 1; i < labels.Length; i++)
                    labels[i].text = string.Empty;
                return;
            }
            var textRoot = new GameObject("CentralShortcut",
                typeof(RectTransform), typeof(CanvasRenderer),
                typeof(TextMeshProUGUI));
            textRoot.transform.SetParent(iconsRoot, false);
            textRoot.layer = root.layer;
            var rect = (RectTransform)textRoot.transform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            _shortcutText = textRoot.GetComponent<TextMeshProUGUI>();
            _shortcutText.fontSize = 7f;
            _shortcutText.color = new Color(0.43f, 0.74f, 0.61f, 1f);
            _shortcutText.alignment = TextAlignmentOptions.Center;
            _shortcutText.raycastTarget = false;
        }

        private static void ReplaceActionArtwork(CommonButton button,
            Sprite normal, Sprite hover, Sprite pressed)
        {
            if (button == null || button.background == null || normal == null)
                return;
            button.normalBgSprite = normal;
            button.hoverBgSprite = hover ?? normal;
            button.pressedBgSprite = pressed ?? normal;
            button.disabledBgSprite = pressed ?? normal;
            button.background.sprite = normal;
            button.background.type = Image.Type.Simple;
            button.background.preserveAspect = false;
            button.background.color = Color.white;
        }

        private static void ButtonOnClick(CommonButton button, int clickCount)
        {
            Plugin.OpenCentralManagement();
        }

        private void Update()
        {
            if (_root == null || _source == null)
                return;
            var sourceRect = _source.transform as RectTransform;
            var mercRect = _mercenaries?.transform as RectTransform;
            var rootRect = _root.transform as RectTransform;
            if (sourceRect != null && mercRect != null && rootRect != null)
            {
                rootRect.anchoredPosition = mercRect.anchoredPosition
                                            - (sourceRect.anchoredPosition
                                               - mercRect.anchoredPosition);
            }
            var visible = _source.gameObject.activeSelf
                          && Plugin.IsTechnologyUnlocked();
            if (_root.activeSelf != visible)
            {
                _root.SetActive(visible);
                if (visible)
                    RefreshText();
            }
            if (visible && Plugin.CentralShortcutKey != KeyCode.None
                        && InputHelper.GetKeyDown(Plugin.CentralShortcutKey))
            {
                Plugin.OpenCentralManagement();
            }
        }

        private void OnDestroy()
        {
            if (_button != null)
                _button.OnClick -= ButtonOnClick;
        }
    }

    public static partial class Plugin
    {
        private static AccessTools.FieldRef<SpaceshipScreen, CommonButton>
            _spaceshipArsenalButton;
        private static AccessTools.FieldRef<SpaceshipScreen, CommonButton>
            _spaceshipMercenariesButton;
        private static bool _centralOpenRequested;
        /// <summary>
        /// Which agent the central panel opens on, remembered for THIS
        /// session's save only (Plugin.DropSaveScopedState clears it on load).
        /// It used to live in PlayerPrefs, but a profile id is not save
        /// specific: after playing one campaign the next one would preselect
        /// whichever agent happened to share that profile. A session-scoped
        /// hint that falls back to "first available agent" is the honest
        /// scope until the mod has real per-save storage.
        /// </summary>
        private static string _lastDeployedMercenaryProfileId;

        private static void PatchSpaceshipButton(Harmony harmony)
        {
            _spaceshipArsenalButton = AccessTools.FieldRefAccess<SpaceshipScreen,
                CommonButton>("_arsenalButton");
            _spaceshipMercenariesButton = AccessTools.FieldRefAccess<SpaceshipScreen,
                CommonButton>("_mercenariesButton");
            PatchRequired(harmony, typeof(SpaceshipScreen), "Awake",
                postfix: nameof(SpaceshipScreenAwakePostfix),
                argumentTypes: Type.EmptyTypes);
            PatchRequired(harmony, typeof(GameModeStateMachine),
                "SpaceModeFinished",
                prefix: nameof(SpaceModeFinishedPrefix),
                argumentTypes: new[] { typeof(SpaceModeFinishedData) });
            PatchRequired(harmony, typeof(GameModeStateMachine),
                "DungeonFinished",
                prefix: nameof(DungeonFinishedPrefix),
                argumentTypes: new[] { typeof(DungeonFinishedData) });
        }

        private static void SpaceModeFinishedPrefix(SpaceModeFinishedData data)
        {
            try
            {
                RememberDeployedOperator(data?.mercProfileId);
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix
                               + "could not remember deployed operator: " + e);
            }
        }

        private static void DungeonFinishedPrefix()
        {
            try
            {
                RememberDeployedOperator(GameState?.Get<Mercenaries>()
                    ?.MercenaryInRaid?.ProfileId);
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix
                               + "could not remember returning operator: " + e);
            }
        }

        private static void RememberDeployedOperator(string profileId)
        {
            if (string.IsNullOrEmpty(profileId))
                return;
            _lastDeployedMercenaryProfileId = profileId;
        }

        private static Mercenary GetPreferredCentralOperator()
        {
            var mercenaries = GameState?.Get<Mercenaries>();
            var values = mercenaries?.Values;
            if (values == null)
                return null;

            if (!string.IsNullOrEmpty(_lastDeployedMercenaryProfileId))
            {
                var recent = mercenaries.Get(
                    _lastDeployedMercenaryProfileId);
                if (IsCentralOperatorAvailable(recent))
                    return recent;
            }

            foreach (var candidate in values)
            {
                if (IsCentralOperatorAvailable(candidate))
                    return candidate;
            }
            return null;
        }

        private static bool IsCentralOperatorAvailable(Mercenary mercenary)
        {
            return mercenary != null
                   && mercenary.State != MercenaryState.Cloning
                   && mercenary.State != MercenaryState.InRaid;
        }

        private static void SpaceshipScreenAwakePostfix(SpaceshipScreen __instance)
        {
            try
            {
                var host = __instance.GetComponent<CentralManagementActionHost>();
                if (host == null)
                    host = __instance.gameObject.AddComponent<CentralManagementActionHost>();
                host.Initialize(_spaceshipArsenalButton(__instance),
                    _spaceshipMercenariesButton(__instance));
            }
            catch (Exception e)
            {
                Debug.LogError(LogPrefix
                               + "could not create the spaceship action button: " + e);
            }
        }

        internal static void OpenCentralManagement()
        {
            if (!IsTechnologyUnlocked())
                return;
            try
            {
                var mercenary = GetPreferredCentralOperator();
                OpenCentralArsenal(mercenary, showOperatorPanel: false);
            }
            catch (Exception e)
            {
                _centralOpenRequested = false;
                Debug.LogError(LogPrefix + "could not open central management: " + e);
            }
        }
    }
}
