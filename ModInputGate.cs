using System;
using System.Collections.Generic;
using MGSC;
using UnityEngine;

namespace QM_CentralManagement
{
    /// <summary>
    /// Whether any mod-built UI currently owns the keyboard and the pointer.
    ///
    /// This used to be a hardcoded three-way OR inside CentralManagementPanel,
    /// so the central panel arbitrated input on behalf of panels it knows
    /// nothing about, and a fourth panel could not exist without editing that
    /// expression. Each feature now registers its own predicate from its own
    /// patch installer, and the three copies of the "swallow the release that
    /// closed a popup" frame counter collapse into one.
    /// </summary>
    internal static class ModInputGate
    {
        private static readonly List<Func<bool>> Sources =
            new List<Func<bool>>();

        private static int _blockedFrame = -1;

        internal static void Register(Func<bool> capturesInput)
        {
            if (capturesInput != null && !Sources.Contains(capturesInput))
                Sources.Add(capturesInput);
        }

        /// <summary>
        /// True for the remainder of the frame in which a popup was dismissed.
        /// Unity raises Button.onClick on mouse-UP, so without this the very
        /// release that closed a list would go on to reach the ItemSlot the
        /// list was covering.
        /// </summary>
        internal static bool IsFrameBlocked => Time.frameCount == _blockedFrame;

        internal static bool IsCaptured
        {
            get
            {
                if (IsFrameBlocked)
                    return true;
                for (var i = 0; i < Sources.Count; i++)
                {
                    try
                    {
                        if (Sources[i]())
                            return true;
                    }
                    catch (Exception e)
                    {
                        // A panel mid-teardown must never wedge the game's
                        // input: treat a throwing source as "not capturing".
                        Debug.LogError(Plugin.LogPrefix
                                       + "input gate source failed: " + e);
                    }
                }
                return false;
            }
        }

        /// <summary>
        /// Blocks the rest of this frame AND pauses the game's raw drag
        /// controller, which polls the mouse outside the EventSystem.
        /// </summary>
        internal static void ConsumePointerRelease()
        {
            UI.Drag.Pause(0.18f);
            _blockedFrame = Time.frameCount;
        }

        /// <summary>
        /// Blocks the rest of this frame only -- used when a text field loses
        /// focus, where there is no drag to pause.
        /// </summary>
        internal static void BlockThisFrame()
        {
            _blockedFrame = Time.frameCount;
        }
    }
}
