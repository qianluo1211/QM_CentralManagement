using HarmonyLib;
using MGSC;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace QM_CentralManagement
{
    internal sealed class SplitStackWheelHandler : MonoBehaviour,
        IScrollHandler
    {
        internal ContextMenuSplitStacksButton Owner;
        internal Slider Slider;

        public void OnScroll(PointerEventData eventData)
        {
            if (Owner == null || Slider == null
                || !Owner.gameObject.activeInHierarchy
                || Mathf.Abs(eventData.scrollDelta.y) <= Mathf.Epsilon)
            {
                return;
            }
            var total = Owner.LeftVal + Owner.RightVal;
            if (total <= 1)
                return;
            var delta = eventData.scrollDelta.y > 0f ? 1 : -1;
            var value = Mathf.Clamp(Owner.LeftVal + delta, 1, total - 1);
            Slider.value = value / (float)total;
            eventData.Use();
        }
    }

    public static partial class Plugin
    {
        private static AccessTools.FieldRef<ContextMenuSplitStacksButton,
            Slider> _splitStackSlider;

        private static void PatchSplitStackWheel(Harmony harmony)
        {
            _splitStackSlider = AccessTools.FieldRefAccess<
                ContextMenuSplitStacksButton, Slider>("_slider");
            PatchRequired(harmony, typeof(ContextMenuSplitStacksButton),
                nameof(ContextMenuSplitStacksButton.Initialize),
                postfix: nameof(SplitStackInitializePostfix),
                argumentTypes: new[] { typeof(BasePickupItem) });
        }

        private static void SplitStackInitializePostfix(
            ContextMenuSplitStacksButton __instance)
        {
            try
            {
                var menu = __instance.GetComponentInParent<CommonContextMenu>(
                    true);
                var host = menu == null ? __instance.gameObject : menu.gameObject;
                var handler = host.GetComponent<SplitStackWheelHandler>()
                              ?? host.AddComponent<SplitStackWheelHandler>();
                handler.Owner = __instance;
                handler.Slider = _splitStackSlider(__instance);
            }
            catch (System.Exception e)
            {
                Debug.LogError(LogPrefix
                               + "split-stack wheel setup failed: " + e);
            }
        }
    }
}
