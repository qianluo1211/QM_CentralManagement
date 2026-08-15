using HarmonyLib;
using MGSC;
using UnityEngine;

namespace QM_CentralManagement
{
    public static partial class Plugin
    {
        private static void PatchSearchInputIsolation(Harmony harmony)
        {
            // KeyCode queries get the narrow, key-aware guard.
            var keyArgs = new[] { typeof(KeyCode) };
            PatchRequired(harmony, typeof(InputHelper), nameof(InputHelper.GetKey),
                prefix: nameof(SuppressTypedKeyWhileSearching),
                argumentTypes: keyArgs);
            PatchRequired(harmony, typeof(InputHelper), nameof(InputHelper.GetKeyDown),
                prefix: nameof(SuppressTypedKeyWhileSearching),
                argumentTypes: keyArgs);
            PatchRequired(harmony, typeof(InputHelper), nameof(InputHelper.GetKeyUp),
                prefix: nameof(SuppressTypedKeyWhileSearching),
                argumentTypes: keyArgs);

            var actionArgs = new[]
            {
                typeof(string), typeof(string), typeof(bool)
            };
            PatchRequired(harmony, typeof(InputController),
                nameof(InputController.IsKey),
                prefix: nameof(SuppressBooleanInputWhileSearching),
                argumentTypes: actionArgs);
            PatchRequired(harmony, typeof(InputController),
                nameof(InputController.IsKeyDown),
                prefix: nameof(SuppressBooleanInputWhileSearching),
                argumentTypes: actionArgs);
            PatchRequired(harmony, typeof(InputController),
                nameof(InputController.IsKeyUp),
                prefix: nameof(SuppressBooleanInputWhileSearching),
                argumentTypes: actionArgs);
        }

        /// <summary>
        /// Action-name queries (InputController.IsKey and friends). The action
        /// cannot be resolved to a physical key cheaply, so these stay fully
        /// suppressed while the panel owns the keyboard.
        /// </summary>
        private static bool SuppressBooleanInputWhileSearching(ref bool __result)
        {
            if (!CentralManagementPanel.ShouldBlockGameInput)
                return true;
            __result = false;
            return false;
        }

        /// <summary>
        /// KeyCode queries (InputHelper.GetKey and friends).
        ///
        /// These prefixes answer for EVERY caller in the game, not just this
        /// mod, so a blanket "no" also blackholed function keys, modifiers and
        /// mouse buttons for the whole session -- including other mods. Only
        /// the keys a focused text field would actually swallow are suppressed
        /// now; everything else is answered by the game as usual.
        ///
        /// The patches cannot simply be dropped in favour of a Navigable that
        /// reports IsInputCaptured: Navigation.ExecuteHotkey only early-outs in
        /// InputController.InputMode.KeyboardAndMouse, and it gates nothing
        /// that polls InputHelper directly outside UI.Process -- which is
        /// exactly what these cover.
        /// </summary>
        private static bool SuppressTypedKeyWhileSearching(KeyCode keyCode,
            ref bool __result)
        {
            if (!CentralManagementPanel.ShouldBlockGameInput)
                return true;
            if (!IsConsumedByTextField(keyCode))
                return true;
            __result = false;
            return false;
        }

        private static bool IsConsumedByTextField(KeyCode key)
        {
            // Letters, digits and the keypad digits.
            if (key >= KeyCode.A && key <= KeyCode.Z)
                return true;
            if (key >= KeyCode.Alpha0 && key <= KeyCode.Alpha9)
                return true;
            if (key >= KeyCode.Keypad0 && key <= KeyCode.Keypad9)
                return true;

            switch (key)
            {
                // Editing and caret movement.
                case KeyCode.Space:
                case KeyCode.Backspace:
                case KeyCode.Delete:
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                case KeyCode.Tab:
                case KeyCode.LeftArrow:
                case KeyCode.RightArrow:
                case KeyCode.UpArrow:
                case KeyCode.DownArrow:
                case KeyCode.Home:
                case KeyCode.End:
                case KeyCode.PageUp:
                case KeyCode.PageDown:
                case KeyCode.Insert:
                // Punctuation that reaches the field as text.
                case KeyCode.Minus:
                case KeyCode.Equals:
                case KeyCode.Comma:
                case KeyCode.Period:
                case KeyCode.Slash:
                case KeyCode.Backslash:
                case KeyCode.Semicolon:
                case KeyCode.Quote:
                case KeyCode.LeftBracket:
                case KeyCode.RightBracket:
                case KeyCode.BackQuote:
                case KeyCode.KeypadPeriod:
                case KeyCode.KeypadDivide:
                case KeyCode.KeypadMultiply:
                case KeyCode.KeypadMinus:
                case KeyCode.KeypadPlus:
                // Kept suppressed so the field's own Escape handling can
                // unfocus it without the game also acting on the key.
                case KeyCode.Escape:
                    return true;
                default:
                    // Function keys, modifiers, mouse buttons, media keys and
                    // anything else reach the game untouched.
                    return false;
            }
        }
    }
}
