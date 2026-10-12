using UnityEngine;
using UnityEngine.InputSystem;

namespace SurvivorSouls.Gameplay
{
    /// <summary>
    /// Floating one-thumb joystick (Archero style): touch anywhere, drag to steer, lift to stop.
    /// Mouse drag and WASD/arrow keys work too, for the editor and desktop dev builds.
    /// Value is in [-1, 1] on each axis; x = right, y = up the screen.
    /// </summary>
    public sealed class VirtualJoystick : MonoBehaviour
    {
        [Tooltip("Drag distance for full speed, as a fraction of screen width.")]
        [SerializeField, Range(0.05f, 0.3f)] private float radiusScreenFraction = 0.12f;
        [SerializeField, Range(0f, 0.5f)] private float deadZone = 0.12f;

        public Vector2 Value { get; private set; }
        public bool IsHeld { get; private set; }
        /// <summary>Screen pixels, origin bottom-left (Input System convention).</summary>
        public Vector2 OriginScreen { get; private set; }
        public Vector2 KnobScreen { get; private set; }
        public float RadiusPixels => Screen.width * radiusScreenFraction;

        private void OnDisable()
        {
            Value = Vector2.zero;
            IsHeld = false;
        }

        private void Update()
        {
            Vector2 v = Vector2.zero;
            bool held = TryGetPointer(out Vector2 pos, out bool began);
            if (held)
            {
                if (began || !IsHeld) OriginScreen = pos;
                float r = RadiusPixels;
                Vector2 delta = pos - OriginScreen;
                // Pull the origin along past the rim, so reversing direction responds at once.
                if (delta.magnitude > r)
                {
                    OriginScreen = pos - delta.normalized * r;
                    delta = pos - OriginScreen;
                }
                KnobScreen = pos;
                v = delta / r;
            }
            IsHeld = held;

            var kb = Keyboard.current;
            if (kb != null)
            {
                var k = new Vector2(
                    (kb.dKey.isPressed || kb.rightArrowKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed || kb.leftArrowKey.isPressed ? 1f : 0f),
                    (kb.wKey.isPressed || kb.upArrowKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed || kb.downArrowKey.isPressed ? 1f : 0f));
                if (k != Vector2.zero) v = k.normalized;
            }

            Value = v.magnitude < deadZone ? Vector2.zero : Vector2.ClampMagnitude(v, 1f);
        }

        private static bool TryGetPointer(out Vector2 pos, out bool began)
        {
            var touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.isPressed)
            {
                pos = touch.primaryTouch.position.ReadValue();
                began = touch.primaryTouch.press.wasPressedThisFrame;
                return true;
            }

            var mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.isPressed)
            {
                pos = mouse.position.ReadValue();
                began = mouse.leftButton.wasPressedThisFrame;
                return true;
            }

            pos = default;
            began = false;
            return false;
        }
    }
}
