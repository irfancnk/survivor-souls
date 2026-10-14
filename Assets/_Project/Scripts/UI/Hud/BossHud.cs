using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.Combat;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// Boss health bar (KAN-58) at the top of the HUD, in the timer's place (boss Chambers have no clock): boss name in
    /// gold, a wide crimson bar with a gold trail that drains behind it, and the title under it. Hidden until the boss
    /// rises, then slides down and fades in (unscaled time, so it also animates during a level-up pause). After the kill
    /// the bar empties and fades out. Fill width written only when it changes: no allocation per frame.
    /// Element names are constants below; keep them in sync with GameHud.uxml. Looks: hud-boss* in GameHud.uss.
    /// Phase mark (KAN-61): a gold tick at <see cref="BossData.Phase2At"/>; when phase 2 starts it lights and the bar turns
    /// a hotter red (classes below).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class BossHud : MonoBehaviour
    {
        private const string RootName = "boss";
        private const string NameLabel = "boss-name";
        private const string TitleLabel = "boss-title";
        private const string FillName = "boss-fill";
        private const string TrailName = "boss-trail";
        private const string PhaseName = "boss-phase";
        private const string HiddenClass = "hud-hidden";
        private const string PhaseLitClass = "hud-boss__phase--lit";
        private const string EnragedClass = "hud-boss--enraged";

        [SerializeField] private BossFight fight;
        [SerializeField, Min(0.05f)] private float slideTime = 0.45f;
        [SerializeField, Min(0f)] private float slideDistance = 60f;
        [Tooltip("Seconds the trail waits before it drains after a hit.")]
        [SerializeField, Min(0f)] private float trailDelay = 0.4f;
        [SerializeField, Min(0.01f)] private float trailSpeed = 0.6f; // share of the bar per second
        [Tooltip("Seconds after the kill before the bar fades out.")]
        [SerializeField, Min(0f)] private float hideAfter = 1.6f;

        private UIDocument _document;
        private VisualElement _root, _fill, _trailBar, _phase;
        private Label _name, _title;
        private float _shown = -1f;   // seconds since the bar appeared (-1 = hidden)
        private float _defeated = -1f;
        private float _drawnFill = -1f, _drawnTrail = -1f, _drawnAlpha = -1f;
        private float _trail = 1f, _sinceHit;

        /// <summary>The bar is on screen (tests and dev tools).</summary>
        public bool IsShown => _shown >= 0f;
        /// <summary>Fill 0..1 as drawn.</summary>
        public float DrawnFill => _drawnFill;
        /// <summary>Phase 2 has started: mark lit, bar enraged.</summary>
        public bool IsEnraged => _root != null && _root.ClassListContains(EnragedClass);
        /// <summary>The phase mark element (tests read its position).</summary>
        public VisualElement PhaseMark => _phase;

        /// <summary>Tests: wiring without the scene builder.</summary>
        public void Setup(BossFight bossFight)
        {
            Unsubscribe();
            fight = bossFight;
            if (isActiveAndEnabled) Subscribe();
        }

        private void Awake() => _document = GetComponent<UIDocument>();

        private void OnEnable()
        {
            var root = _document.rootVisualElement;
            _root = root.Q(RootName);
            _name = root.Q<Label>(NameLabel);
            _title = root.Q<Label>(TitleLabel);
            _fill = root.Q(FillName);
            _trailBar = root.Q(TrailName);
            _phase = root.Q(PhaseName);
            Subscribe();
        }

        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (fight == null) return;
            fight.Spawned += OnSpawned;
            fight.Defeated += OnDefeated;
            fight.PhaseChanged += OnPhaseChanged;
        }

        private void Unsubscribe()
        {
            if (fight == null) return;
            fight.Spawned -= OnSpawned;
            fight.Defeated -= OnDefeated;
            fight.PhaseChanged -= OnPhaseChanged;
        }

        private void OnSpawned(Enemy boss)
        {
            if (_name != null) _name.text = fight.Data != null ? fight.Data.DisplayName : boss.name;
            if (_title != null) _title.text = fight.Data != null ? fight.Data.Title : "";
            _shown = 0f;
            _defeated = -1f;
            _trail = 1f;
            _sinceHit = 0f;
            _drawnFill = _drawnTrail = _drawnAlpha = -1f;
            _root?.RemoveFromClassList(HiddenClass);
            _root?.RemoveFromClassList(EnragedClass);
            if (_phase != null)
            {
                float at = fight.Data != null ? fight.Data.Phase2At : 0f;
                _phase.style.display = at > 0f ? DisplayStyle.Flex : DisplayStyle.None;
                _phase.style.left = Length.Percent(at * 100f);
                _phase.RemoveFromClassList(PhaseLitClass);
            }
        }

        private void OnPhaseChanged(int phase)
        {
            if (phase < 2) return;
            _root?.AddToClassList(EnragedClass);
            _phase?.AddToClassList(PhaseLitClass);
        }

        private void OnDefeated() => _defeated = 0f;

        private void LateUpdate()
        {
            if (_shown < 0f || fight == null || _root == null) return;
            float dt = Time.unscaledDeltaTime;
            _shown += dt;

            float health = fight.HealthFraction;
            if (health < _trail)
            {
                _sinceHit += dt;
                if (_sinceHit >= trailDelay) _trail = Mathf.Max(health, _trail - trailSpeed * dt);
            }
            else
            {
                _trail = health;
                _sinceHit = 0f;
            }
            if (!Mathf.Approximately(health, _drawnFill))
            {
                if (health < _drawnFill) _sinceHit = 0f; // a new hit restarts the trail wait
                _drawnFill = health;
                _fill.style.width = Length.Percent(health * 100f);
            }
            if (!Mathf.Approximately(_trail, _drawnTrail))
            {
                _drawnTrail = _trail;
                _trailBar.style.width = Length.Percent(_trail * 100f);
            }

            float k = Mathf.Clamp01(_shown / slideTime);
            float eased = 1f - (1f - k) * (1f - k) * (1f - k);
            float fade = 1f;
            if (_defeated >= 0f)
            {
                _defeated += dt;
                fade = 1f - Mathf.Clamp01((_defeated - hideAfter) / 0.5f);
                if (fade <= 0f)
                {
                    _shown = -1f;
                    _root.AddToClassList(HiddenClass);
                    return;
                }
            }
            float alpha = eased * fade;
            if (Mathf.Approximately(alpha, _drawnAlpha) && k >= 1f) return; // settled: no style writes
            _drawnAlpha = alpha;
            _root.style.opacity = alpha;
            _root.style.translate = new Translate(0f, -slideDistance * (1f - eased));
        }
    }
}
