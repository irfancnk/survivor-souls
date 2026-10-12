using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Controls;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// Binds GameHud.uxml: draws the floating joystick where the finger is, shows a first-time hint,
    /// and handles the pause button (for now: back to main menu). Android back / Escape does the same.
    /// Element names are constants below; keep them in sync with the UXML.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class GameHudController : MonoBehaviour
    {
        private const string SafeRootName = "safe-root";
        private const string JoystickName = "joystick";
        private const string KnobName = "joystick-knob";
        private const string PauseName = "btn-pause";
        private const string HintName = "lbl-hint";
        private const string HiddenClass = "hud-hidden";
        private const string HintGoneClass = "hud-hint--gone";

        [SerializeField] private FloatingJoystick joystick;

        private UIDocument _document;
        private SafeAreaPadding _safeArea;
        private VisualElement _base;
        private VisualElement _knob;
        private Label _hint;
        private bool _leaving;

        public void SetJoystick(FloatingJoystick value) => joystick = value;

        private void Awake() => _document = GetComponent<UIDocument>();

        private void OnEnable()
        {
            var root = _document.rootVisualElement;
            _safeArea = new SafeAreaPadding(root.Q(SafeRootName));
            _base = root.Q(JoystickName);
            _knob = root.Q(KnobName);
            _hint = root.Q<Label>(HintName);
            root.Q<Button>(PauseName).clicked += LeaveToMenu;
        }

        private void Update()
        {
            _safeArea?.Refresh();
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) LeaveToMenu();
            DrawJoystick();
        }

        private void DrawJoystick()
        {
            if (_base?.panel == null) return; // document disabled or not laid out yet
            bool show = joystick != null && joystick.IsTouching;
            _base.EnableInClassList(HiddenClass, !show);
            if (!show) return;

            if (joystick.Value.sqrMagnitude > 0f) _hint.AddToClassList(HintGoneClass);

            var panel = _base.panel;
            float scale = panel.visualTree.layout.width / Mathf.Max(1, Screen.width); // px -> panel units
            float radius = joystick.RadiusPixels * scale;
            Vector2 centre = ToPanel(panel, joystick.Origin);
            Vector2 knob = ToPanel(panel, joystick.Knob) - centre;

            _base.style.width = radius * 2f;
            _base.style.height = radius * 2f;
            SetRadius(_base, radius);
            _base.style.left = centre.x - radius;
            _base.style.top = centre.y - radius;

            float knobHalf = _knob.resolvedStyle.width * 0.5f;
            _knob.style.left = radius + knob.x - knobHalf - _base.resolvedStyle.borderLeftWidth;
            _knob.style.top = radius + knob.y - knobHalf - _base.resolvedStyle.borderTopWidth;
        }

        private static Vector2 ToPanel(IPanel panel, Vector2 screen) =>
            RuntimePanelUtils.ScreenToPanel(panel, new Vector2(screen.x, Screen.height - screen.y));

        private static void SetRadius(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r;
            e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r;
            e.style.borderBottomRightRadius = r;
        }

        private void LeaveToMenu()
        {
            if (_leaving || !SceneLoader.IsInBuild(SceneNames.MainMenu)) return;
            _leaving = true;
            SceneLoader.LoadAsync(SceneNames.MainMenu);
        }
    }
}
