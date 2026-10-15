using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Controls;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// Binds GameHud.uxml: shows a first-time hint until the joystick first moves (JoystickView draws the stick),
    /// and handles the pause button (for now: back to main menu). Android back / Escape does the same. Leaving ends the run:
    /// its souls are banked into the wallet first (KAN-96). Shows the "TEST MODE" tag while Test Mode is on (KAN-144; it
    /// can only change in the menu, so it is read once).
    /// Element names are constants below; keep them in sync with the UXML.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class GameHudController : MonoBehaviour
    {
        private const string SafeRootName = "safe-root";
        private const string PauseName = "btn-pause";
        private const string HintName = "lbl-hint";
        private const string HintGoneClass = "hud-hint--gone";
        public const string TestModeTagName = "lbl-test-mode";

        [SerializeField] private FloatingJoystick joystick;
        [Tooltip("Leaving to the menu banks this run's souls (KAN-96).")]
        [SerializeField] private RunCounters counters;

        private UIDocument _document;
        private SafeAreaPadding _safeArea;
        private Label _hint;
        private bool _leaving;

        public void SetJoystick(FloatingJoystick value) => joystick = value;

        private void Awake() => _document = GetComponent<UIDocument>();

        private void OnEnable()
        {
            var root = _document.rootVisualElement;
            _safeArea = new SafeAreaPadding(root.Q(SafeRootName));
            _hint = root.Q<Label>(HintName);
            root.Q<Button>(PauseName).clicked += LeaveToMenu;
            root.Q<Label>(TestModeTagName)?.EnableInClassList("hud-hidden", !TestMode.IsOn);
        }

        private void Update()
        {
            _safeArea?.Refresh();
            if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) LeaveToMenu();
            if (_hint != null && joystick != null && joystick.Move.sqrMagnitude > 0f) _hint.AddToClassList(HintGoneClass);
        }

        private void LeaveToMenu()
        {
            if (_leaving || !SceneLoader.IsInBuild(SceneNames.MainMenu)) return;
            _leaving = true;
            if (counters != null) counters.BankToWallet();
            SceneLoader.LoadAsync(SceneNames.MainMenu);
        }
    }
}
