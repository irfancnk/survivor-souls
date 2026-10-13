using SurvivorSouls.Gameplay.Chamber;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// Chamber survival countdown on the HUD (KAN-39): "MM:SS" under the XP bar, a red pulse in the last seconds
    /// (<see cref="ChamberRules.FinalCountdown"/>), short toasts at the warning marks ("5 MINUTES REMAIN") and
    /// "THE PORTAL IS OPEN" when the clock ends. Hidden in Chambers without a timer (boss: <see cref="OpenCondition.KillBoss"/>).
    /// The clock itself runs on scaled time (<see cref="SurvivalTimer"/>); toasts and the pulse animate on unscaled time.
    /// Shown seconds round up (10:00 at start, 00:00 only when the portal opens). Strings cached: no allocation per frame.
    /// Element names are constants below; keep them in sync with GameHud.uxml. Looks: hud-timer*, chamber-toast* in GameHud.uss.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class TimerHud : MonoBehaviour
    {
        private const string TimerName = "timer";
        private const string LabelName = "timer-label";
        private const string ToastName = "chamber-toast";
        private const string HiddenClass = "hud-hidden";
        private const string FinalClass = "hud-timer--final";
        private const string DoneClass = "hud-timer--done";
        private const string ToastOpenClass = "chamber-toast--open";
        private const int MaxCachedSeconds = 60 * 100;

        [SerializeField] private SurvivalTimer timer;
        [SerializeField, Min(0.1f)] private float toastTime = 2.4f;
        [SerializeField, Min(0.1f)] private float openToastTime = 3.6f;
        [SerializeField, Range(0f, 0.5f)] private float pulseScale = 0.16f;

        private static string[] _texts;

        private UIDocument _document;
        private VisualElement _timer, _toast;
        private Label _label, _toastLabel;
        private int _drawnSeconds = -1;
        private bool _drawnFinal, _drawnDone, _drawnVisible = true;
        private float _toastAge = -1f, _toastLife;

        /// <summary>Text of the last toast (tests and dev tools).</summary>
        public string LastToast { get; private set; }

        private void Awake() => _document = GetComponent<UIDocument>();

        private void OnEnable()
        {
            var root = _document.rootVisualElement;
            _timer = root.Q(TimerName);
            _label = root.Q<Label>(LabelName);
            _toast = root.Q(ToastName);
            _toastLabel = _toast as Label;
            if (timer != null)
            {
                timer.WarningReached += OnWarning;
                timer.Finished += OnFinished;
            }
        }

        private void OnDisable()
        {
            if (timer == null) return;
            timer.WarningReached -= OnWarning;
            timer.Finished -= OnFinished;
        }

        private void OnWarning(int seconds) => ShowToast(WarningText(seconds), toastTime, open: false);

        private void OnFinished() => ShowToast("THE PORTAL IS OPEN", openToastTime, open: true);

        private void ShowToast(string text, float life, bool open)
        {
            LastToast = text;
            if (_toastLabel == null) return;
            _toastLabel.text = text;
            _toast.EnableInClassList(ToastOpenClass, open);
            _toast.RemoveFromClassList(HiddenClass);
            _toastAge = 0f;
            _toastLife = life;
        }

        private void LateUpdate()
        {
            if (timer == null || _label?.panel == null) return;
            bool visible = timer.UsesTimer;
            if (visible != _drawnVisible)
            {
                _drawnVisible = visible;
                _timer.EnableInClassList(HiddenClass, !visible);
            }
            if (visible) UpdateClock();
            UpdateToast(Time.unscaledDeltaTime);
        }

        private void UpdateClock()
        {
            int seconds = Mathf.CeilToInt(timer.Remaining);
            if (seconds != _drawnSeconds)
            {
                _drawnSeconds = seconds;
                _label.text = Format(seconds);
            }
            bool final = timer.InFinalCountdown, done = timer.IsFinished;
            if (final != _drawnFinal)
            {
                _drawnFinal = final;
                _timer.EnableInClassList(FinalClass, final);
                if (!final) _timer.style.scale = new Scale(Vector2.one);
            }
            if (done != _drawnDone)
            {
                _drawnDone = done;
                _timer.EnableInClassList(DoneClass, done);
            }
            if (final)
            {
                // Beat on every whole second: quick swell, slower settle.
                float frac = timer.Remaining - Mathf.Floor(timer.Remaining);
                float beat = Mathf.Pow(frac, 3f);
                float s = 1f + pulseScale * beat;
                _timer.style.scale = new Scale(new Vector2(s, s));
            }
        }

        private void UpdateToast(float dt)
        {
            if (_toastAge < 0f || _toast == null) return;
            _toastAge += dt;
            if (_toastAge >= _toastLife)
            {
                _toastAge = -1f;
                _toast.AddToClassList(HiddenClass);
                return;
            }
            float fadeIn = Mathf.Clamp01(_toastAge / 0.18f);
            float fadeOut = Mathf.Clamp01((_toastLife - _toastAge) / 0.45f);
            _toast.style.opacity = fadeIn * fadeOut;
            float pop = Mathf.Lerp(1.25f, 1f, Mathf.Clamp01(_toastAge / 0.22f));
            _toast.style.scale = new Scale(new Vector2(pop, pop));
        }

        /// <summary>"MM:SS" for whole seconds (cached; negative = 00:00).</summary>
        public static string Format(int seconds)
        {
            seconds = Mathf.Max(0, seconds);
            if (seconds >= MaxCachedSeconds) return $"{seconds / 60:00}:{seconds % 60:00}";
            _texts ??= new string[MaxCachedSeconds];
            return _texts[seconds] ??= $"{seconds / 60:00}:{seconds % 60:00}";
        }

        public static string WarningText(int seconds)
        {
            if (seconds >= 60 && seconds % 60 == 0)
            {
                int minutes = seconds / 60;
                return minutes == 1 ? "1 MINUTE REMAINS" : $"{minutes} MINUTES REMAIN";
            }
            return seconds == 1 ? "1 SECOND REMAINS" : $"{seconds} SECONDS REMAIN";
        }
    }
}
