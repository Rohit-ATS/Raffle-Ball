using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace RiftRally.Core
{
    /// <summary>
    /// The mouse and keyboard, read through whichever input backend the project is set to.
    ///
    /// This exists because the project runs the Input System package with the legacy manager
    /// switched off, and in that configuration every call to <c>UnityEngine.Input</c> throws
    /// at runtime. Routing the handful of desktop-only reads through one place means the
    /// backend can be switched in Player Settings without breaking the game, and the desktop
    /// fallback that makes the game testable without a headset keeps working either way.
    /// </summary>
    public static class PointerInput
    {
        /// <summary>True when a mouse (or equivalent pointer) is available.</summary>
        public static bool HasPointer
        {
#if ENABLE_INPUT_SYSTEM
            get => Mouse.current != null;
#elif ENABLE_LEGACY_INPUT_MANAGER
            get => Input.mousePresent;
#else
            get => false;
#endif
        }

        /// <summary>Pointer position in screen pixels.</summary>
        public static Vector2 Position
        {
#if ENABLE_INPUT_SYSTEM
            get => Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero;
#elif ENABLE_LEGACY_INPUT_MANAGER
            get => Input.mousePosition;
#else
            get => Vector2.zero;
#endif
        }

        /// <summary>True while the primary button is down.</summary>
        public static bool PrimaryHeld
        {
#if ENABLE_INPUT_SYSTEM
            get => Mouse.current != null && Mouse.current.leftButton.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
            get => Input.GetMouseButton(0);
#else
            get => false;
#endif
        }

        /// <summary>True on the frame the primary button goes down.</summary>
        public static bool PrimaryPressedThisFrame
        {
#if ENABLE_INPUT_SYSTEM
            get => Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            get => Input.GetMouseButtonDown(0);
#else
            get => false;
#endif
        }

        /// <summary>True on the frame the space bar goes down.</summary>
        public static bool SpacePressedThisFrame
        {
#if ENABLE_INPUT_SYSTEM
            get => Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER
            get => Input.GetKeyDown(KeyCode.Space);
#else
            get => false;
#endif
        }
    }
}
