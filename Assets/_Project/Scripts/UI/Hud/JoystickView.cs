using System;
using SurvivorSouls.Gameplay.Controls;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// Draws the <see cref="FloatingJoystick"/> (KAN-16): a faint ring with four chevrons (up, right, down, left).
    /// Idle: rests at the bottom centre above the safe area, all four chevrons faint. Touch: jumps to the finger.
    /// Drag: each chevron's opacity follows <see cref="SideAlpha"/> (only the drag side stays; diagonals share two),
    /// smoothed over <c>fadeTime</c>. Release: glides back to the idle spot and look.
    /// Also tells the joystick not to start on HUD buttons. Moves with style.translate (no layout pass per frame)
    /// and writes a style only when its value changed. Element names are constants; keep them in sync with GameHud.uxml.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class JoystickView : MonoBehaviour
    {
        private const string RootName = "joystick";
        private const string RingName = "joystick-ring";
        private const string KnobName = "joystick-knob";
        public const string ModalClass = "hud-modal";
        private static readonly string[] SideNames = { "joystick-side-up", "joystick-side-right", "joystick-side-down", "joystick-side-left" };
        /// <summary>Side directions in joystick space (x = right, y = up), same order as the side elements.</summary>
        public static readonly Vector2[] SideDirections = { Vector2.up, Vector2.right, Vector2.down, Vector2.left };

        [SerializeField] private FloatingJoystick joystick;
        [Tooltip("Chevron opacity while nobody touches the stick.")]
        [SerializeField, Range(0f, 1f)] private float idleAlpha = 0.5f;
        [Tooltip("Chevron opacity while touching with (almost) no drag.")]
        [SerializeField, Range(0f, 1f)] private float faintAlpha = 0.2f;
        [SerializeField, Range(0f, 1f)] private float idleRingAlpha = 0.55f;
        [SerializeField, Range(0.01f, 0.5f)] private float fadeTime = 0.08f;
        [SerializeField, Range(0.01f, 0.5f)] private float returnTime = 0.12f;
        [Tooltip("Gap (panel px) between the safe-area bottom (home indicator) and the idle ring.")]
        [SerializeField] private float idleBottomMargin = 150f;

        private UIDocument _document;
        private VisualElement _root, _ring, _knob;
        private readonly VisualElement[] _sides = new VisualElement[4];
        private readonly float[] _alpha = new float[4];
        private readonly float[] _written = new float[6]; // 4 sides, ring, knob
        private float _ringAlpha, _knobAlpha, _radius;
        private Vector2 _centre, _knobOffset, _writtenCentre, _writtenKnob;
        private bool _placed;
        private Predicate<Vector2> _ignoreStart;

        public void SetJoystick(FloatingJoystick value) => joystick = value;

        /// <summary>
        /// Target opacity of the chevron facing <paramref name="side"/>: idle when not touching; while touching,
        /// faint at no drag and <c>clamp01(dot(dir, side))</c> at full drag, lerped by drag length.
        /// </summary>
        public static float SideAlpha(Vector2 move, Vector2 side, bool touching, float idle, float faint)
        {
            if (!touching) return idle;
            float m = Mathf.Clamp01(move.magnitude);
            if (m <= 0f) return faint;
            return Mathf.Lerp(faint, Mathf.Clamp01(Vector2.Dot(move / m, side)), m);
        }

        private void Awake() => _document = GetComponent<UIDocument>();

        private void OnEnable()
        {
            var root = _document.rootVisualElement;
            _root = root.Q(RootName);
            _ring = root.Q(RingName);
            _knob = root.Q(KnobName);
            for (int i = 0; i < _sides.Length; i++) { _sides[i] = root.Q(SideNames[i]); _alpha[i] = idleAlpha; }
            for (int i = 0; i < _written.Length; i++) _written[i] = -1f;
            _ringAlpha = idleRingAlpha;
            _knobAlpha = 0f;
            _radius = 0f;
            _placed = false;
            _ignoreStart ??= IsOverButton;
            if (joystick != null) joystick.IgnoreStartAt = _ignoreStart;
        }

        private void OnDisable()
        {
            if (joystick != null && joystick.IgnoreStartAt == _ignoreStart) joystick.IgnoreStartAt = null;
        }

        private void LateUpdate()
        {
            var panel = _root?.panel;
            if (panel == null || joystick == null || Screen.width <= 0) return;
            float scale = panel.visualTree.layout.width / Screen.width; // screen px -> panel units
            float radius = joystick.RadiusPixels * scale;
            if (radius <= 0f) return; // not laid out yet
            if (!Mathf.Approximately(radius, _radius)) Resize(radius);

            float dt = Time.unscaledDeltaTime;
            bool touching = joystick.IsTouching;
            Vector2 target = touching ? ToPanel(panel, joystick.Origin) : IdleCentre(panel, radius);
            _centre = touching || !_placed ? target : Vector2.Lerp(_centre, target, Ease(returnTime, dt));
            _placed = true;

            float k = Ease(fadeTime, dt);
            Vector2 move = joystick.Move;
            for (int i = 0; i < _sides.Length; i++)
            {
                _alpha[i] = Mathf.Lerp(_alpha[i], SideAlpha(move, SideDirections[i], touching, idleAlpha, faintAlpha), k);
                WriteOpacity(_sides[i], i, _alpha[i]);
            }
            _ringAlpha = Mathf.Lerp(_ringAlpha, touching ? 1f : idleRingAlpha, k);
            _knobAlpha = Mathf.Lerp(_knobAlpha, touching ? 1f : 0f, k);
            WriteOpacity(_ring, 4, _ringAlpha);
            WriteOpacity(_knob, 5, _knobAlpha);

            if (touching)
            {
                Vector2 offset = (joystick.Knob - joystick.Origin) * scale; // keeps the last offset while fading out
                _knobOffset = new Vector2(offset.x, -offset.y);
            }
            Vector2 corner = _centre - new Vector2(radius, radius);
            if (corner != _writtenCentre) { _root.style.translate = new Translate(corner.x, corner.y); _writtenCentre = corner; }
            if (_knob != null && _knobOffset != _writtenKnob) { _knob.style.translate = new Translate(_knobOffset.x, _knobOffset.y); _writtenKnob = _knobOffset; }
        }

        private void Resize(float radius)
        {
            _radius = radius;
            _root.style.width = radius * 2f;
            _root.style.height = radius * 2f;
            if (_ring == null) return;
            _ring.style.borderTopLeftRadius = radius;
            _ring.style.borderTopRightRadius = radius;
            _ring.style.borderBottomLeftRadius = radius;
            _ring.style.borderBottomRightRadius = radius;
        }

        private Vector2 IdleCentre(IPanel panel, float radius)
        {
            Rect safe = Screen.safeArea; // pixels, origin bottom-left
            Vector2 bottom = ToPanel(panel, new Vector2(safe.center.x, safe.yMin));
            return new Vector2(bottom.x, bottom.y - idleBottomMargin - radius);
        }

        private void WriteOpacity(VisualElement e, int slot, float value)
        {
            if (e == null || Mathf.Abs(_written[slot] - value) < 0.004f) return;
            e.style.opacity = value;
            _written[slot] = value;
        }

        /// <summary>
        /// True when a HUD button or a modal screen (class <see cref="ModalClass"/>, e.g. the skill pick) is under the
        /// screen point, so a press there stays a UI press.
        /// </summary>
        private bool IsOverButton(Vector2 screen)
        {
            var panel = _root?.panel;
            if (panel == null) return false;
            for (var e = panel.Pick(ToPanel(panel, screen)); e != null; e = e.parent)
                if (e is Button || e.ClassListContains(ModalClass)) return true;
            return false;
        }

        private static float Ease(float time, float dt) => 1f - Mathf.Exp(-dt / time);

        private static Vector2 ToPanel(IPanel panel, Vector2 screen) =>
            RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));
    }
}
