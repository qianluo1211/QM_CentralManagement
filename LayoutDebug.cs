using System.Collections.Generic;
using MGSC;
using UnityEngine;
using UnityEngine.UI;

namespace QM_CentralManagement
{
    /// <summary>
    /// Layout diagnostics, gated behind the config switches.
    ///
    /// Three panels each grew their own copy of this: two hand-rolled
    /// world-corner formatters with their own "only log when it changed"
    /// string caches, plus a recursive hierarchy dump. All three described
    /// themselves as temporary. They are the only way to debug a rect problem
    /// from a user's Player.log, so they stay -- but as one implementation.
    /// </summary>
    internal static class LayoutDebug
    {
        /// <summary>Bottom-left and top-right in world space, rounded.</summary>
        internal static string World(RectTransform rect)
        {
            if (rect == null)
                return "null";
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            return string.Format("BL=({0:F0},{1:F0}) TR=({2:F0},{3:F0})",
                corners[0].x, corners[0].y, corners[2].x, corners[2].y);
        }

        internal static string Local(Rect rect)
        {
            return string.Format("(x={0:F0},y={1:F0},w={2:F0},h={3:F0})",
                rect.x, rect.y, rect.width, rect.height);
        }

        /// <summary>
        /// Logs only when the text differs from the last call through the same
        /// <paramref name="last"/> field. Placement runs on every layout pass,
        /// so an unconditional log would bury Player.log in identical lines.
        /// </summary>
        internal static void LogChanged(ref string last, string text)
        {
            if (text == last)
                return;
            last = text;
            Plugin.DebugLog(text);
        }

        /// <summary>
        /// Dumps a live hierarchy -- names, anchored positions, sizes, sprite
        /// names -- so a layout problem can be fixed against the actual data
        /// instead of guesses. Bounded, because a screen subtree can be deep.
        /// </summary>
        internal static void DumpHierarchy(string caption, Transform root,
            int maxLines = 400)
        {
            var lines = new List<string> { "== " + caption + " ==" };
            DumpNode(root, string.Empty, lines, maxLines);
            foreach (var line in lines)
                Debug.Log(Plugin.LogPrefix + line);
        }

        private static void DumpNode(Transform node, string indent,
            List<string> lines, int maxLines)
        {
            if (node == null || lines.Count > maxLines)
                return;
            var rect = node as RectTransform;
            var image = node.GetComponent<Image>();
            var sprite = image != null && image.sprite != null
                ? image.sprite.name
                : (image != null ? "<null>" : "-");
            var geometry = rect != null
                ? string.Format(" pos=({0:F0},{1:F0}) size=({2:F0}x{3:F0})",
                    rect.anchoredPosition.x, rect.anchoredPosition.y,
                    rect.sizeDelta.x, rect.sizeDelta.y)
                : string.Empty;
            lines.Add(indent + node.name + geometry + " sprite=" + sprite
                      + " active=" + node.gameObject.activeSelf);
            for (var i = 0; i < node.childCount; i++)
                DumpNode(node.GetChild(i), indent + "  ", lines, maxLines);
        }
    }
}
