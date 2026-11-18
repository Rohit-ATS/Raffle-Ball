using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RiftRally.Core
{
    /// <summary>Keys the desktop simulator needs, named by what they do rather than where they are.</summary>
    public enum SimKey
    {
        Forward, Back, Left, Right,
        Up, Down,
        LeftHand, RightHand,
        HandForward, HandBack, HandLeft, HandRight,
        Reset, ToggleHelp,
        Confirm,

        /// <summary>Opens and closes the in-game options dock.</summary>
        ToggleOptions,

        /// <summary>Jumps straight to the next landscape, for looking through them quickly.</summary>
        NextLandscape
    }

    /// <summary>
    /// Keyboard and mouse, read through whichever input backend the project is set to.
    ///
    /// This exists for the same reason <see cref="PointerInput"/> does: the project runs the
    /// Input System package with the legacy manager switched off, and in that configuration
    /// every call to <c>UnityEngine.Input</c> throws at runtime. Keeping the reads in one
    /// place means the backend can be switched in Player Settings without hunting through
    /// gameplay code.
    /// </summary>
    public static class DesktopInput
    {
        public static bool Available
        {
#if ENABLE_INPUT_SYSTEM
            get => Keyboard.current != null || Mouse.current != null;
#elif ENABLE_LEGACY_INPUT_MANAGER
            get => true;
#else
            get => false;
#endif
        }

        public static bool Held(SimKey key)
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;

            return key switch
            {
                SimKey.Forward => keyboard.wKey.isPressed,
                SimKey.Back => keyboard.sKey.isPressed,
                SimKey.Left => keyboard.aKey.isPressed,
                SimKey.Right => keyboard.dKey.isPressed,
                SimKey.Up => keyboard.eKey.isPressed,
                SimKey.Down => keyboard.qKey.isPressed,
                SimKey.LeftHand => keyboard.leftShiftKey.isPressed,
                SimKey.RightHand => keyboard.spaceKey.isPressed,
                SimKey.HandForward => keyboard.upArrowKey.isPressed || keyboard.iKey.isPressed,
                SimKey.HandBack => keyboard.downArrowKey.isPressed || keyboard.kKey.isPressed,
                SimKey.HandLeft => keyboard.leftArrowKey.isPressed || keyboard.jKey.isPressed,
                SimKey.HandRight => keyboard.rightArrowKey.isPressed || keyboard.lKey.isPressed,
                _ => false,
            };
#elif ENABLE_LEGACY_INPUT_MANAGER
            return key switch
            {
                SimKey.Forward => Input.GetKey(KeyCode.W),
                SimKey.Back => Input.GetKey(KeyCode.S),
                SimKey.Left => Input.GetKey(KeyCode.A),
                SimKey.Right => Input.GetKey(KeyCode.D),
                SimKey.Up => Input.GetKey(KeyCode.E),
                SimKey.Down => Input.GetKey(KeyCode.Q),
                SimKey.LeftHand => Input.GetKey(KeyCode.LeftShift),
                SimKey.RightHand => Input.GetKey(KeyCode.Space),
                SimKey.HandForward => Input.GetKey(KeyCode.UpArrow) || Input.GetKey(KeyCode.I),
                SimKey.HandBack => Input.GetKey(KeyCode.DownArrow) || Input.GetKey(KeyCode.K),
                SimKey.HandLeft => Input.GetKey(KeyCode.LeftArrow) || Input.GetKey(KeyCode.J),
                SimKey.HandRight => Input.GetKey(KeyCode.RightArrow) || Input.GetKey(KeyCode.L),
                _ => false,
            };
#else
            return false;
#endif
        }

        public static bool PressedThisFrame(SimKey key)
        {
#if ENABLE_INPUT_SYSTEM
            var keyboard = Keyboard.current;
            if (keyboard == null) return false;

            return key switch
            {
                SimKey.Reset => keyboard.rKey.wasPressedThisFrame,
                SimKey.ToggleHelp => keyboard.f1Key.wasPressedThisFrame
                                     || keyboard.hKey.wasPressedThisFrame,
                SimKey.Confirm => keyboard.enterKey.wasPressedThisFrame,
                SimKey.ToggleOptions => keyboard.tabKey.wasPressedThisFrame,
                SimKey.NextLandscape => keyboard.nKey.wasPressedThisFrame,
                _ => false,
            };
#elif ENABLE_LEGACY_INPUT_MANAGER
            return key switch
            {
                SimKey.Reset => Input.GetKeyDown(KeyCode.R),
                SimKey.ToggleHelp => Input.GetKeyDown(KeyCode.F1) || Input.GetKeyDown(KeyCode.H),
                SimKey.Confirm => Input.GetKeyDown(KeyCode.Return),
                SimKey.ToggleOptions => Input.GetKeyDown(KeyCode.Tab),
                SimKey.NextLandscape => Input.GetKeyDown(KeyCode.N),
                _ => false,
            };
#else
            return false;
#endif
        }

        /// <summary>Mouse movement since the last frame, in pixels.</summary>
        public static Vector2 MouseDelta
        {
#if ENABLE_INPUT_SYSTEM
            get => Mouse.current != null ? Mouse.current.delta.ReadValue() : Vector2.zero;
#elif ENABLE_LEGACY_INPUT_MANAGER
            get => new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y")) * 10f;
#else
            get => Vector2.zero;
#endif
        }

        /// <summary>Scroll wheel movement, normalised so one notch is roughly 1.</summary>
        public static float Scroll
        {
#if ENABLE_INPUT_SYSTEM
            get => Mouse.current != null ? Mouse.current.scroll.ReadValue().y / 120f : 0f;
#elif ENABLE_LEGACY_INPUT_MANAGER
            get => Input.mouseScrollDelta.y;
#else
            get => 0f;
#endif
        }

        /// <summary>Right mouse button, used to look around.</summary>
        public static bool LookHeld
        {
#if ENABLE_INPUT_SYSTEM
            get => Mouse.current != null && Mouse.current.rightButton.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
            get => Input.GetMouseButton(1);
#else
            get => false;
#endif
        }
    }
}
