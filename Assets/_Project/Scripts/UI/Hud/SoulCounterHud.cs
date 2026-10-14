using SurvivorSouls.Gameplay.Chamber;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// Souls collected in this run (KAN-96) on the HUD: soul-blue gem + number on the right of the health bar. The number
    /// comes from <see cref="RunCounters.Souls"/> (carried through portals) and pops (short scale-up, unscaled time) when it
    /// rises. Strings are cached up to <see cref="MaxCached"/>: no allocation per frame or per pickup below that.
    /// Element names are constants below; keep them in sync with GameHud.uxml. Looks: hud-souls* in GameHud.uss.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class SoulCounterHud : MonoBehaviour
    {
        public const string CounterName = "run-souls";
        public const string ValueName = "run-souls-value";
        private const int MaxCached = 20000;

        [SerializeField] private RunCounters counters;
        [SerializeField, Range(0f, 0.6f)] private float popScale = 0.22f;
        [SerializeField, Min(0.05f)] private float popTime = 0.22f;

        private static string[] _texts;

        private UIDocument _document;
        private VisualElement _counter;
        private Label _value;
        private int _drawn = -1;
        private float _pop;

        /// <summary>Text as last drawn (tests and dev tools).</summary>
        public string Text => _value != null ? _value.text : null;

        /// <summary>Tests: wiring without the scene builder.</summary>
        public void Setup(RunCounters runCounters) => counters = runCounters;

        private void Awake() => _document = GetComponent<UIDocument>();

        private void OnEnable()
        {
            var root = _document.rootVisualElement;
            _counter = root.Q(CounterName);
            _value = root.Q<Label>(ValueName);
            _drawn = -1;
        }

        private void LateUpdate()
        {
            if (_value == null) return;
            int souls = counters != null ? counters.Souls : 0;
            if (souls != _drawn)
            {
                if (_drawn >= 0 && souls > _drawn) _pop = 1f; // not on the first draw (carried souls after a portal)
                _drawn = souls;
                _value.text = TextFor(souls);
            }
            if (_pop <= 0f || _counter == null) return;
            _pop = Mathf.Max(0f, _pop - Mathf.Min(Time.unscaledDeltaTime, 0.1f) / popTime);
            float s = 1f + popScale * _pop * _pop;
            _counter.style.scale = new Scale(new Vector3(s, s, 1f));
        }

        /// <summary>"1,240": thousands separators, cached for 0..<see cref="MaxCached"/>.</summary>
        public static string TextFor(int souls)
        {
            if (souls < 0) souls = 0;
            if (souls > MaxCached) return PlayerHealthHud.FormatCount(souls);
            _texts ??= new string[MaxCached + 1];
            return _texts[souls] ??= PlayerHealthHud.FormatCount(souls);
        }
    }
}
