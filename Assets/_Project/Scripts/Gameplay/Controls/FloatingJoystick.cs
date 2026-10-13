using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace SurvivorSouls.Gameplay.Controls
{
    /// <summary>
    /// Archero-style floating joystick (KAN-16). A finger that touches down in the movement zone becomes the stick
    /// centre; dragging gives <see cref="Move"/>; release gives zero. Past the radius the centre follows the finger,
    /// so a direction change answers at once. One finger owns the stick (tracked by touch id): other fingers never
    /// move, steal or release it. Presses that start where <see cref="IgnoreStartAt"/> says (HUD buttons) are skipped.
    /// Pure input: no visuals (UI.Hud.JoystickView draws it). Mouse and WASD/arrows work in the editor.
    /// Pointer coordinates are screen pixels, origin bottom-left.
    /// </summary>
    [DefaultExecutionOrder(-100)] // before movement reads it
    public sealed class FloatingJoystick : MonoBehaviour
    {
        /// <summary>HUD reference width (GameHudPanelSettings, match width): radius is given in these pixels.</summary>
        public const float ReferenceWidth = 1080f;
        private const int NoFinger = int.MinValue;
        private const int MouseFinger = -1; // touch ids are positive

        [Tooltip("Presses start only below this fraction of screen height (top strip is kept for the HUD bar).")]
        [SerializeField, Range(0.3f, 1f)] private float activeAreaTop = 0.82f;
        [Tooltip("Stick radius in reference pixels (1080 wide HUD).")]
        [SerializeField, Range(40f, 300f)] private float radiusReference = 120f;
        [Tooltip("Normalised travel below which the output is zero.")]
        [SerializeField, Range(0f, 0.5f)] private float deadZone = 0.1f;
        [SerializeField] private bool centreFollowsFinger = true;

        /// <summary>Dev/test hook: when set, overrides all device input (e.g. screenshot automation).</summary>
        public static Vector2? SimulatedValue;

        /// <summary>Optional filter (screen position -> true = do not start the stick there), e.g. over HUD buttons.</summary>
        public Predicate<Vector2> IgnoreStartAt { get; set; }

        /// <summary>Direction with magnitude 0..1 (x = right, y = up/forward). Zero when released.</summary>
        public Vector2 Move { get; private set; }
        /// <summary>True while a finger/mouse drives the stick (not keyboard).</summary>
        public bool IsTouching => _finger != NoFinger || _simulated;
        /// <summary>Stick centre in screen pixels (valid while touching).</summary>
        public Vector2 Origin { get; private set; }
        /// <summary>Knob position in screen pixels, clamped to the radius (valid while touching).</summary>
        public Vector2 Knob { get; private set; }
        public float RadiusPixels => radiusReference * Screen.width / ReferenceWidth;

        private int _finger = NoFinger;
        private bool _simulated;

        private void OnDisable() => Release();

        private void Update() => Tick();

        /// <summary>Reads devices and updates <see cref="Move"/>. Called by Update; public for tests.</summary>
        public void Tick()
        {
            _simulated = SimulatedValue.HasValue;
            if (_simulated)
            {
                // Pretend a finger holds the stick at the lower centre, so the HUD draws it too.
                Move = Vector2.ClampMagnitude(SimulatedValue.Value, 1f);
                Origin = new Vector2(Screen.width * 0.5f, Screen.height * 0.22f);
                Knob = Origin + Move * RadiusPixels;
                return;
            }

            Vector2 pos = default;
            bool held = _finger != NoFinger && TryReadFinger(_finger, out pos);
            if (!held)
            {
                Release(); // owner finger lifted (other fingers never count)
                held = TryStart(out pos);
            }
            Move = ApplyDeadZone(held ? Drag(pos) : ReadKeyboard());
        }

        private bool TryReadFinger(int finger, out Vector2 pos)
        {
            pos = default;
            if (finger == MouseFinger)
            {
                var mouse = Mouse.current;
                if (mouse == null || !mouse.leftButton.isPressed) return false;
                pos = mouse.position.ReadValue();
                return true;
            }
            var screen = Touchscreen.current;
            if (screen == null) return false;
            var touches = screen.touches;
            for (int i = 0; i < touches.Count; i++)
            {
                var t = touches[i];
                if (t.touchId.ReadValue() != finger || !t.press.isPressed) continue;
                pos = t.position.ReadValue();
                return true;
            }
            return false;
        }

        private bool TryStart(out Vector2 pos)
        {
            var screen = Touchscreen.current;
            if (screen != null)
            {
                var touches = screen.touches;
                for (int i = 0; i < touches.Count; i++)
                {
                    var t = touches[i];
                    pos = t.position.ReadValue();
                    if (t.press.wasPressedThisFrame && Begin(t.touchId.ReadValue(), pos)) return true;
                }
            }
            var mouse = Mouse.current;
            pos = mouse != null ? mouse.position.ReadValue() : default;
            return mouse != null && mouse.leftButton.wasPressedThisFrame && Begin(MouseFinger, pos);
        }

        private bool Begin(int finger, Vector2 pos)
        {
            if (pos.y > Screen.height * activeAreaTop) return false;
            if (IgnoreStartAt != null && IgnoreStartAt(pos)) return false;
            _finger = finger;
            Origin = pos;
            Knob = pos;
            return true;
        }

        private Vector2 Drag(Vector2 pos)
        {
            float radius = Mathf.Max(1f, RadiusPixels);
            Vector2 offset = pos - Origin;
            if (offset.sqrMagnitude > radius * radius)
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
            _finger = NoFinger;
            Move = Vector2.zero;
        }
    }
}
