using UnityEngine;
using UnityEngine.InputSystem;

namespace SurvivorSouls.Gameplay.Controls
{
    /// <summary>
    /// Archero-style single-finger floating joystick. Touch (or mouse press) anywhere in the active area
    /// sets the stick centre; dragging gives a direction; release stops. If the finger travels past the
    /// radius, the centre follows it, so changing direction stays quick.
    /// Pure input: no visuals (see UI.Hud.JoystickView). Keyboard WASD/arrows work too, for editor tests.
    /// Pointer coordinates are screen pixels, origin bottom-left.
    /// </summary>
    [DefaultExecutionOrder(-100)] // before movement reads it
    public sealed class FloatingJoystick : MonoBehaviour
    {
        [Tooltip("Presses start only below this fraction of screen height (top strip is kept for HUD).")]
        [SerializeField, Range(0.3f, 1f)] private float activeAreaTop = 0.82f;
        [Tooltip("Stick radius as a fraction of screen width.")]
        [SerializeField, Range(0.05f, 0.3f)] private float radiusOfWidth = 0.13f;
        [Tooltip("Normalised travel below which the output is zero.")]
        [SerializeField, Range(0f, 0.5f)] private float deadZone = 0.12f;
        [SerializeField] private bool centreFollowsFinger = true;

        /// <summary>Dev/test hook: when set, overrides all device input (e.g. screenshot automation).</summary>
        public static Vector2? SimulatedValue;

        /// <summary>Direction with magnitude 0..1 (x = right, y = up/forward).</summary>
        public Vector2 Value { get; private set; }
        /// <summary>True while a finger/mouse drives the stick (not keyboard).</summary>
        public bool IsTouching { get; private set; }
        /// <summary>Stick centre in screen pixels (valid while touching).</summary>
        public Vector2 Origin { get; private set; }
        /// <summary>Knob position in screen pixels, clamped to the radius (valid while touching).</summary>
        public Vector2 Knob { get; private set; }
        public float RadiusPixels => Screen.width * radiusOfWidth;

        private void OnDisable() => Release();

        private void Update()
        {
            if (SimulatedValue.HasValue)
            {
                // Pretend a finger holds the stick at the lower centre, so the HUD draws it too.
                Value = Vector2.ClampMagnitude(SimulatedValue.Value, 1f);
                IsTouching = Value.sqrMagnitude > 0f;
                Origin = new Vector2(Screen.width * 0.5f, Screen.height * 0.22f);
                Knob = Origin + Value * RadiusPixels;
                return;
            }

            Vector2 stick = ReadPointer();
            if (!IsTouching) stick = ReadKeyboard();
            Value = ApplyDeadZone(stick);
        }

        private Vector2 ReadPointer()
        {
            var pointer = Pointer.current;
            if (pointer == null) { Release(); return Vector2.zero; }

            Vector2 pos = pointer.position.ReadValue();
            if (pointer.press.wasPressedThisFrame && pos.y <= Screen.height * activeAreaTop)
            {
                IsTouching = true;
                Origin = pos;
            }
            if (!pointer.press.isPressed) { Release(); return Vector2.zero; }
            if (!IsTouching) return Vector2.zero;

            float radius = Mathf.Max(1f, RadiusPixels);
            Vector2 offset = pos - Origin;
            if (offset.magnitude > radius)
            {
                if (centreFollowsFinger) Origin = pos - offset.normalized * radius;
                offset = Vector2.ClampMagnitude(offset, radius);
            }
            Knob = Origin + offset;
            return offset / radius;
        }

        private static Vector2 ReadKeyboard()
        {
            var kb = Keyboard.current;
            if (kb == null) return Vector2.zero;
            var v = Vector2.zero;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) v.y += 1f;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) v.y -= 1f;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) v.x += 1f;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) v.x -= 1f;
            return Vector2.ClampMagnitude(v, 1f);
        }

        private Vector2 ApplyDeadZone(Vector2 v)
        {
            float m = v.magnitude;
            if (m <= deadZone) return Vector2.zero;
            return v / m * Mathf.InverseLerp(deadZone, 1f, m);
        }

        private void Release()
        {
            IsTouching = false;
            Value = Vector2.zero;
        }
    }
}
