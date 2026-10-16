using SurvivorSouls.Gameplay.Progression;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// XP on the HUD document (KAN-21): bar at the top of the screen with the level number, and a "LEVEL UP" pop over the
    /// Knight when <see cref="LevelUpFlow.Started"/> fires. The bar eases toward the real value; a gain that crosses
    /// levels runs the fill to the end, wraps and continues, and the number ticks up as it wraps.
    /// Medieval look (KAN-35): forged iron rim + molten-gold fill with a scrolling rune strip and a shine sweep (shared
    /// <c>.ss-bar</c> style, animated by <see cref="HudBarFx"/>); the level number sits on a heraldic shield. Every XP gain
    /// lights a gold spark on the fill tip; a level wrap flashes the bar, rings the shield like a bell (scale pop + wobble)
    /// and bursts gold motes out of it (the level-up chime is GameAudio's). Effects write styles only while they run.
    /// Runs on unscaled time (the bar keeps filling while the level-up pause is on). Never blocks touches.
    /// Element names are constants below; keep them in sync with GameHud.uxml. Looks: hud-xp*, levelup-pop in GameHud.uss.
    /// </summary>
    [DefaultExecutionOrder(500)] // after TopDownFollowCamera.LateUpdate (pop follows the final camera)
    [RequireComponent(typeof(UIDocument))]
    public sealed class XpHud : MonoBehaviour
    {
        private const string FillName = "xp-fill";
        private const string LevelName = "xp-level";
        private const string PopName = "levelup-pop";
        private const string TrackName = "xp-track";
        private const string BadgeName = "xp-badge";
        private const string SparkName = "xp-spark";
        private const string FlashName = "xp-flash";
        private const string MotesName = "xp-motes";
        private const string MoteClass = "hud-xp__mote";
        private const string HiddenClass = "hud-hidden";
        private const string FlashClass = "hud-xp--flash";

        [SerializeField] private XpManager xp;
        [SerializeField] private LevelUpFlow flow;
        [SerializeField] private Transform player;
        [SerializeField] private Camera worldCamera;

        [Header("Bar")]
        [Tooltip("Exponential ease toward the real value (1/s).")]
        [SerializeField, Min(1f)] private float fillSharpness = 10f;
        [Tooltip("Minimum fill speed (levels per second), so a multi-level gain does not crawl.")]
        [SerializeField, Min(0.05f)] private float minFillSpeed = 0.8f;
        [Tooltip("Seconds the bar glows after a level wraps.")]
        [SerializeField, Min(0f)] private float flashTime = 0.35f;

        [Header("Medieval bar (KAN-35)")]
        [Tooltip("Seconds the gold spark shows on the fill tip after an XP gain.")]
        [SerializeField, Min(0.05f)] private float sparkTime = 0.4f;
        [Tooltip("Seconds the shield rings (scale pop + wobble) after a level wrap.")]
        [SerializeField, Min(0.05f)] private float ringTime = 0.75f;
        [Tooltip("Shield scale at the start of the ring.")]
        [SerializeField, Min(1f)] private float ringScale = 1.38f;
        [Tooltip("Shield wobble at the start of the ring (degrees).")]
        [SerializeField, Min(0f)] private float ringWobble = 10f;
        [SerializeField, Range(0, 16)] private int moteCount = 10;
        [SerializeField, Min(0.05f)] private float moteTime = 0.8f;
        [Tooltip("How far the motes fly from the shield (px).")]
        [SerializeField, Min(0f)] private float moteDistance = 130f;

        [Header("Level up pop")]
        [SerializeField, Min(0f)] private float popHeight = 2.5f;
        [SerializeField, Min(0f)] private float popRise = 0.5f;
        [SerializeField, Min(0.1f)] private float popTime = 1.3f;

        private UIDocument _document;
        private VisualElement _fill, _pop, _track, _badge, _spark, _flashOverlay, _motes;
        private VisualElement[] _mote = System.Array.Empty<VisualElement>();
        private Vector2[] _moteDir = System.Array.Empty<Vector2>();
        private Label _level;
        private string[] _levelText;
        private float _shown = -1f;
        private float _lastTarget = -1f;
        private float _drawnFraction = -1f;
        private int _drawnLevel = -1;
        private float _flash;
        private float _popAge = -1f;
        private float _sparkAge = -1f, _ringAge = -1f, _moteAge = -1f;

        /// <summary>Displayed progress in level units (level + fill). Tests and dev tools.</summary>
        public float Shown => _shown;
        /// <summary>The fill-tip spark is showing (an XP gain less than <see cref="sparkTime"/> ago).</summary>
        public bool SparkActive => _sparkAge >= 0f;
        /// <summary>The level shield is ringing (a level wrap less than <see cref="ringTime"/> ago).</summary>
        public bool Ringing => _ringAge >= 0f;
        /// <summary>The level-up motes are flying.</summary>
        public bool MotesActive => _moteAge >= 0f;
        /// <summary>Mote elements built under the shield (tests).</summary>
        public int MoteCount => _mote.Length;
        /// <summary>Shield scale as drawn (1 at rest).</summary>
        public float BadgeScale
        {
            get
            {
                if (_badge == null) return 1f;
                var scale = _badge.style.scale;
                return scale.keyword == StyleKeyword.Undefined ? scale.value.value.x : 1f;
            }
        }

        /// <summary>Tests: wiring without the scene builder (call before the component is enabled, or re-enable it).</summary>
        public void Setup(XpManager xpManager, LevelUpFlow levelUpFlow = null)
        {
            if (flow != null && isActiveAndEnabled) flow.Started -= OnLevelUpStarted;
            xp = xpManager;
            flow = levelUpFlow;
            if (flow != null && isActiveAndEnabled) flow.Started += OnLevelUpStarted;
        }

        private void Awake() => _document = GetComponent<UIDocument>();

        private void OnEnable()
        {
            var root = _document.rootVisualElement;
            _fill = root.Q(FillName);
            _level = root.Q<Label>(LevelName);
            _pop = root.Q(PopName);
            _track = root.Q(TrackName);
            _badge = root.Q(BadgeName);
            _spark = root.Q(SparkName);
            _flashOverlay = root.Q(FlashName);
            _motes = root.Q(MotesName);
            BuildMotes();
            if (flow != null) flow.Started += OnLevelUpStarted;
        }

        private void OnDisable()
        {
            if (flow != null) flow.Started -= OnLevelUpStarted;
        }

        /// <summary>Pooled motes: built once, evenly spread around the shield with a fixed jitter.</summary>
        private void BuildMotes()
        {
            if (_motes == null) return;
            _motes.Clear();
            _mote = new VisualElement[moteCount];
            _moteDir = new Vector2[moteCount];
            for (int i = 0; i < moteCount; i++)
            {
                var m = new VisualElement { pickingMode = PickingMode.Ignore };
                m.AddToClassList(MoteClass);
                _motes.Add(m);
                _mote[i] = m;
                float angle = (i + 0.37f * Mathf.Sin(i * 2.3f)) / Mathf.Max(1, moteCount) * Mathf.PI * 2f;
                float reach = 0.7f + 0.3f * Mathf.Abs(Mathf.Sin(i * 1.7f));
                _moteDir[i] = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * reach;
            }
            _motes.AddToClassList(HiddenClass);
        }

        private void OnLevelUpStarted(int level)
        {
            _popAge = 0f;
            _pop?.RemoveFromClassList(HiddenClass);
        }

        private void LateUpdate() => Tick(Time.unscaledDeltaTime);

        /// <summary>Advances the bar, the effects and the pop by <paramref name="dt"/> seconds (LateUpdate; tests call it).</summary>
        public void Tick(float dt)
        {
            if (xp == null || _fill?.panel == null) return;
            UpdateBar(dt);
            UpdateSpark(dt);
            UpdateRing(dt);
            UpdateMotes(dt);
            UpdatePop(dt);
        }

        private void UpdateBar(float dt)
        {
            float target = xp.Level + xp.Progress;
            if (_lastTarget >= 0f && target > _lastTarget) StartSpark();
            _lastTarget = target;
            if (_shown < 0f || target < _shown) _shown = target; // first frame or a new run: snap
            else if (_shown < target)
            {
                float step = Mathf.Max((target - _shown) * (1f - Mathf.Exp(-fillSharpness * dt)), minFillSpeed * dt);
                int before = (int)_shown;
                _shown = Mathf.Min(target, _shown + step);
                if ((int)_shown > before) StartLevelWrap();
            }

            int level = (int)_shown;
            float fraction = _shown - level;
            if (!Mathf.Approximately(fraction, _drawnFraction))
            {
                _drawnFraction = fraction;
                _fill.style.width = Length.Percent(fraction * 100f);
                if (_spark != null && _sparkAge >= 0f) _spark.style.left = Length.Percent(fraction * 100f);
            }
            if (level != _drawnLevel)
            {
                _drawnLevel = level;
                if (_level != null) _level.text = LevelText(level);
            }
            if (_flash > 0f)
            {
                _flash -= dt;
                _track?.EnableInClassList(FlashClass, _flash > 0f);
                if (_flashOverlay != null) _flashOverlay.style.opacity = Mathf.Clamp01(_flash / Mathf.Max(0.01f, flashTime));
            }
        }

        private void StartSpark()
        {
            if (_spark == null) return;
            _sparkAge = 0f;
            _spark.style.left = Length.Percent(Mathf.Max(0f, _drawnFraction) * 100f);
        }

        private void StartLevelWrap()
        {
            _flash = flashTime;
            if (_badge != null) _ringAge = 0f;
            if (_mote.Length > 0)
            {
                _moteAge = 0f;
                _motes.RemoveFromClassList(HiddenClass);
            }
        }

        private void UpdateSpark(float dt)
        {
            if (_sparkAge < 0f) return;
            _sparkAge += dt;
            float k = _sparkAge / sparkTime;
            if (k >= 1f)
            {
                _sparkAge = -1f;
                _spark.style.opacity = 0f;
                return;
            }
            float size = Mathf.Lerp(1.25f, 0.7f, k);
            _spark.style.opacity = 1f - k * k;
            _spark.style.scale = new Scale(new Vector2(size, size));
            _spark.style.rotate = new Rotate(k * 90f);
        }

        /// <summary>Bell ring: a scale pop that settles with a damped wobble.</summary>
        private void UpdateRing(float dt)
        {
            if (_ringAge < 0f) return;
            _ringAge += dt;
            if (_ringAge >= ringTime)
            {
                _ringAge = -1f;
                _badge.style.scale = new Scale(Vector2.one);
                _badge.style.rotate = new Rotate(0f);
                return;
            }
            float a = _ringAge;
            float s = 1f + (ringScale - 1f) * Mathf.Exp(-6f * a) * Mathf.Cos(14f * a);
            _badge.style.scale = new Scale(new Vector2(s, s));
            _badge.style.rotate = new Rotate(ringWobble * Mathf.Exp(-4.5f * a) * Mathf.Sin(24f * a));
        }

        private void UpdateMotes(float dt)
        {
            if (_moteAge < 0f) return;
            _moteAge += dt;
            float k = _moteAge / moteTime;
            if (k >= 1f)
            {
                _moteAge = -1f;
                _motes.AddToClassList(HiddenClass);
                return;
            }
            float reach = moteDistance * (1f - (1f - k) * (1f - k) * (1f - k));
            float alpha = 1f - k * k;
            float size = Mathf.Lerp(1.1f, 0.45f, k);
            for (int i = 0; i < _mote.Length; i++)
            {
                var m = _mote[i];
                Vector2 p = _moteDir[i] * reach;
                m.style.translate = new Translate(p.x, p.y + 30f * k * k); // a little drop as they fade
                m.style.opacity = alpha;
                m.style.scale = new Scale(new Vector2(size, size));
            }
        }

        private void UpdatePop(float dt)
        {
            if (_popAge < 0f || _pop == null) return;
            _popAge += dt;
            float k = _popAge / popTime;
            if (k >= 1f || player == null)
            {
                _popAge = -1f;
                _pop.AddToClassList(HiddenClass);
                return;
            }
            var cam = worldCamera != null ? worldCamera : Camera.main;
            if (cam == null) return;
            Vector3 world = player.position + Vector3.up * (popHeight + popRise * (1f - (1f - k) * (1f - k)));
            Vector2 p = RuntimePanelUtils.CameraTransformWorldToPanel(_pop.panel, world, cam);
            float pop = Mathf.Lerp(1.45f, 1f, Mathf.Clamp01(_popAge / 0.18f));
            _pop.style.translate = new Translate(p.x, p.y);
            _pop.style.scale = new Scale(new Vector2(pop, pop));
            _pop.style.opacity = Mathf.Clamp01(_popAge / 0.08f) * (1f - Mathf.Clamp01((k - 0.7f) / 0.3f));
        }

        /// <summary>Level number strings built once (no allocation while the bar animates). The shield shows "LV" above.</summary>
        private string LevelText(int level)
        {
            _levelText ??= new string[100];
            if (level < 0 || level >= _levelText.Length) return level.ToString();
            return _levelText[level] ??= level.ToString();
        }
    }
}
