using SurvivorSouls.Gameplay.Progression;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// XP on the HUD document (KAN-21): thin soul-blue bar at the top of the screen with the level number, and a
    /// "LEVEL UP" pop over the Knight when <see cref="LevelUpFlow.Started"/> fires. The bar eases toward the real value;
    /// a gain that crosses levels runs the fill to the end, wraps and continues, and the number ticks up as it wraps.
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

        [Header("Level up pop")]
        [SerializeField, Min(0f)] private float popHeight = 2.5f;
        [SerializeField, Min(0f)] private float popRise = 0.5f;
        [SerializeField, Min(0.1f)] private float popTime = 1.3f;

        private UIDocument _document;
        private VisualElement _fill, _pop;
        private Label _level;
        private string[] _levelText;
        private float _shown = -1f;
        private float _drawnFraction = -1f;
        private int _drawnLevel = -1;
        private float _flash;
        private float _popAge = -1f;

        /// <summary>Displayed progress in level units (level + fill). Tests and dev tools.</summary>
        public float Shown => _shown;

        private void Awake() => _document = GetComponent<UIDocument>();

        private void OnEnable()
        {
            var root = _document.rootVisualElement;
            _fill = root.Q(FillName);
            _level = root.Q<Label>(LevelName);
            _pop = root.Q(PopName);
            if (flow != null) flow.Started += OnLevelUpStarted;
        }

        private void OnDisable()
        {
            if (flow != null) flow.Started -= OnLevelUpStarted;
        }

        private void OnLevelUpStarted(int level)
        {
            _popAge = 0f;
            _pop?.RemoveFromClassList(HiddenClass);
        }

        private void LateUpdate()
        {
            if (xp == null || _fill?.panel == null) return;
            float dt = Time.unscaledDeltaTime;
            UpdateBar(dt);
            UpdatePop(dt);
        }

        private void UpdateBar(float dt)
        {
            float target = xp.Level + xp.Progress;
            if (_shown < 0f || target < _shown) _shown = target; // first frame or a new run: snap
            else if (_shown < target)
            {
                float step = Mathf.Max((target - _shown) * (1f - Mathf.Exp(-fillSharpness * dt)), minFillSpeed * dt);
                int before = (int)_shown;
                _shown = Mathf.Min(target, _shown + step);
                if ((int)_shown > before) _flash = flashTime;
            }

            int level = (int)_shown;
            float fraction = _shown - level;
            if (!Mathf.Approximately(fraction, _drawnFraction))
            {
                _drawnFraction = fraction;
                _fill.style.width = Length.Percent(fraction * 100f);
            }
            if (level != _drawnLevel)
            {
                _drawnLevel = level;
                _level.text = LevelText(level);
            }
            if (_flash > 0f)
            {
                _flash -= dt;
                _fill.parent.EnableInClassList(FlashClass, _flash > 0f);
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

        /// <summary>"LV 7" strings built once (no allocation while the bar animates).</summary>
        private string LevelText(int level)
        {
            _levelText ??= new string[100];
            if (level < 0 || level >= _levelText.Length) return "LV " + level;
            return _levelText[level] ??= "LV " + level;
        }
    }
}
