using SurvivorSouls.Core;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.MainMenu
{
    /// <summary>
    /// Binds MainMenu.uxml. Element names are constants below; keep them in sync with the UXML.
    /// No game logic here: Play delegates to SceneLoader, settings delegate to GameSettings.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MainMenuController : MonoBehaviour
    {
        private const string SafeRootName = "safe-root";
        private const string PlayName = "btn-play";
        private const string SettingsName = "btn-settings";
        private const string QuitName = "btn-quit";
        private const string VersionName = "lbl-version";
        private const string ToastName = "lbl-toast";
        private const string OverlayName = "settings-overlay";
        private const string MusicName = "sld-music";
        private const string SfxName = "sld-sfx";
        private const string VibrationName = "tgl-vibration";
        private const string FpsName = "tgl-fps";
        private const string ReloadRingName = "tgl-reload-ring";
        private const string CloseName = "btn-settings-close";
        private const string HiddenClass = "ss-hidden";

        [SerializeField, Min(0.5f)] private float toastSeconds = 2f;

        private UIDocument _document;
        private SafeAreaPadding _safeArea;
        private VisualElement _overlay;
        private Label _toast;
        private Button _play;
        private IVisualElementScheduledItem _toastHide;
        private bool _loading;

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
            GameSettings.Apply();
        }

        private void OnEnable()
        {
            var root = _document.rootVisualElement;

            _safeArea = new SafeAreaPadding(root.Q(SafeRootName));
            _overlay = root.Q(OverlayName);
            _toast = root.Q<Label>(ToastName);
            root.Q<Label>(VersionName).text = $"v{Application.version}";

            _play = root.Q<Button>(PlayName);
            _play.clicked += OnPlay;
            root.Q<Button>(SettingsName).clicked += OpenSettings;
            root.Q<Button>(CloseName).clicked += CloseSettings;

            var quit = root.Q<Button>(QuitName);
            // Apple rejects apps that quit themselves; hide the button on iOS.
            if (Application.platform == RuntimePlatform.IPhonePlayer)
                quit.AddToClassList(HiddenClass);
            else
                quit.clicked += OnQuit;

            BindSettings(root);
            _play.Focus();
        }

        private void Update() => _safeArea?.Refresh();

        private void OnApplicationPause(bool paused)
        {
            if (paused) GameSettings.Save();
        }

        private void OnPlay()
        {
            if (_loading) return;
            if (!SceneLoader.IsInBuild(SceneNames.Game))
            {
                ShowToast("The path ahead is not yet forged.");
                return;
            }

            _loading = true;
            _play.SetEnabled(false);
            SceneLoader.LoadAsync(SceneNames.Game);
        }

        private void OnQuit()
        {
            GameSettings.Save();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void OpenSettings() => _overlay.RemoveFromClassList(HiddenClass);

        private void CloseSettings()
        {
            _overlay.AddToClassList(HiddenClass);
            GameSettings.Save();
        }

        private static void BindSettings(VisualElement root)
        {
            var music = root.Q<Slider>(MusicName);
            music.SetValueWithoutNotify(GameSettings.MusicVolume);
            music.RegisterValueChangedCallback(e => GameSettings.MusicVolume = e.newValue);

            var sfx = root.Q<Slider>(SfxName);
            sfx.SetValueWithoutNotify(GameSettings.SfxVolume);
            sfx.RegisterValueChangedCallback(e => GameSettings.SfxVolume = e.newValue);

            var vibration = root.Q<Toggle>(VibrationName);
            vibration.SetValueWithoutNotify(GameSettings.Vibration);
            vibration.RegisterValueChangedCallback(e =>
            {
                GameSettings.Vibration = e.newValue;
                if (e.newValue) Haptics.Light(); // preview tap: the player feels what they turned on
            });

            var fps = root.Q<Toggle>(FpsName);
            fps.SetValueWithoutNotify(GameSettings.HighFrameRate);
            fps.RegisterValueChangedCallback(e => GameSettings.HighFrameRate = e.newValue);

            var reloadRing = root.Q<Toggle>(ReloadRingName);
            reloadRing.SetValueWithoutNotify(GameSettings.ReloadRing);
            reloadRing.RegisterValueChangedCallback(e => GameSettings.ReloadRing = e.newValue);
        }

        private void ShowToast(string message)
        {
            _toast.text = message;
            _toast.RemoveFromClassList(HiddenClass);
            _toastHide?.Pause();
            _toastHide = _toast.schedule
                .Execute(() => _toast.AddToClassList(HiddenClass))
                .StartingIn((long)(toastSeconds * 1000f));
        }
    }
}
