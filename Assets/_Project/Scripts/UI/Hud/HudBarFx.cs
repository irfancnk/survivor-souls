using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// Shared life of the medieval HUD bars (KAN-35): every <c>.ss-bar__shine</c> on the HUD document sweeps across its
    /// fill once per <see cref="shinePeriod"/> (bars staggered), and every <c>.ss-bar__runes</c> strip scrolls slowly to
    /// the right. One component for all bars (XP, boss, later health), so a new bar only needs the classes in GameHud.uxml.
    /// Unscaled time (bars stay alive in the level-up pause). A parked shine is written once, not every frame; no
    /// allocation per frame. Looks: <c>.ss-bar*</c> in GameHud.uss; art: Editor/Hud/HudBarArtBuilder.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class HudBarFx : MonoBehaviour
    {
        public const string ShineClass = "ss-bar__shine";
        public const string RunesClass = "ss-bar__runes";

        [Tooltip("Seconds between two shine sweeps on one bar.")]
        [SerializeField, Min(0.5f)] private float shinePeriod = 4.5f;
        [Tooltip("Seconds one sweep takes across the fill.")]
        [SerializeField, Min(0.1f)] private float shineTime = 0.85f;
        [Tooltip("Seconds between the sweeps of two bars, so they do not flash together.")]
        [SerializeField, Min(0f)] private float shineStagger = 1.7f;
        [Tooltip("Rune strip scroll speed (px per second).")]
        [SerializeField] private float runeSpeed = 14f;
        [Tooltip("Width of one rune tile on screen (px): the USS background-size width of .ss-bar__runes.")]
        [SerializeField, Min(1f)] private float runeTile = 192f;

        private readonly List<VisualElement> _shines = new List<VisualElement>();
        private readonly List<VisualElement> _runes = new List<VisualElement>();
        private float[] _drawnShine = System.Array.Empty<float>();
        private float _time;

        /// <summary>Shine elements found on the document (tests).</summary>
        public int ShineCount => _shines.Count;
        /// <summary>Rune strips found on the document (tests).</summary>
        public int RuneCount => _runes.Count;
        /// <summary>Last translate x written to shine <paramref name="i"/> (px; NaN before the first write).</summary>
        public float ShineX(int i) => i < _drawnShine.Length ? _drawnShine[i] : float.NaN;
        /// <summary>Rune scroll offset (px, -runeTile..0, grows: the strip drifts right) at the current time.</summary>
        public float RuneOffset => _time * runeSpeed % runeTile - runeTile;

        private void OnEnable()
        {
            var root = GetComponent<UIDocument>().rootVisualElement;
            _shines.Clear();
            _runes.Clear();
            root?.Query(className: ShineClass).ToList(_shines);
            root?.Query(className: RunesClass).ToList(_runes);
            _drawnShine = new float[_shines.Count];
            for (int i = 0; i < _drawnShine.Length; i++) _drawnShine[i] = float.NaN;
        }

        private void LateUpdate() => Tick(Time.unscaledDeltaTime);

        /// <summary>Advances the sweeps and the rune scroll by <paramref name="dt"/> seconds (LateUpdate; tests call it).</summary>
        public void Tick(float dt)
        {
            _time += dt;
            for (int i = 0; i < _shines.Count; i++)
            {
                var shine = _shines[i];
                float lane = shine.parent?.resolvedStyle.width ?? 0f;
                float width = shine.resolvedStyle.width;
                if (float.IsNaN(lane) || float.IsNaN(width) || lane <= 0f) continue; // not laid out / hidden
                float phase = (_time + i * shineStagger) % shinePeriod;
                float x = -width; // parked left of the fill (clipped)
                if (phase < shineTime)
                {
                    float k = phase / shineTime;
                    x = Mathf.Lerp(-width, lane, k * k * (3f - 2f * k));
                }
                if (x == _drawnShine[i]) continue;
                _drawnShine[i] = x;
                shine.style.translate = new Translate(x, 0f);
            }
            if (_runes.Count == 0) return;
            var position = new BackgroundPosition(BackgroundPositionKeyword.Left, RuneOffset);
            for (int i = 0; i < _runes.Count; i++) _runes[i].style.backgroundPositionX = position;
        }
    }
}
