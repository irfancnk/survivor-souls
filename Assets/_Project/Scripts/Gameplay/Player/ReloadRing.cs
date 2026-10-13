using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Player
{
    /// <summary>
    /// Reload indicator on the floor ring under the Knight (KAN-42). Reads <see cref="AutoShooter.ReloadProgress"/> every
    /// frame (the shooter owns the timing, from the attack speed stat; this script never times anything itself), so the
    /// sweep ends in the frame the volley fires and follows attack speed changes at once without a jump.
    /// States: reloading (track + arc + head, clockwise from 12 o'clock), ready flash (<see cref="flashSeconds"/> when the
    /// reload completes), fire pulse (ring kicks outward on every volley), calm (full dim ring while the Knight moves or
    /// has no target). The ring quad is kept world-aligned so 12 o'clock stays at the top of the screen when the Knight
    /// turns. One MaterialPropertyBlock write per frame, no allocation. Flash and pulse run on scaled time: they freeze with
    /// <see cref="GamePause"/> like the reload itself. Hidden when the player turns the ring off in Settings.
    /// </summary>
    public sealed class ReloadRing : MonoBehaviour
    {
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int FlashId = Shader.PropertyToID("_Flash");
        private static readonly int PulseId = Shader.PropertyToID("_Pulse");
        private static readonly int CalmId = Shader.PropertyToID("_Calm");
        private static readonly int AlphaId = Shader.PropertyToID("_Alpha");

        [SerializeField] private AutoShooter shooter;
        [SerializeField] private PlayerMover mover;
        [Tooltip("Flat quad renderer with the SurvivorSouls/ReloadRing material.")]
        [SerializeField] private Renderer ring;
        [SerializeField, Min(0.01f)] private float flashSeconds = 0.1f;
        [SerializeField, Min(0.01f)] private float pulseSeconds = 0.16f;
        [Tooltip("How fast (per second) the arc eases between calm and engaged brightness.")]
        [SerializeField, Min(0.1f)] private float calmBlendSpeed = 6f;
        [Tooltip("Ring alpha while running at full speed (1 = no fade).")]
        [SerializeField, Range(0f, 1f)] private float runAlpha = 0.75f;
        [SerializeField, Min(0.1f)] private float runFadeSpeed = 5f;

        private MaterialPropertyBlock _block;
        private float _flash, _pulse, _calm = 1f, _alpha = 1f;
        private float _shownProgress = 1f;
        private bool _subscribed;

        /// <summary>The fraction the ring drew in its last frame (tests and debug).</summary>
        public float ShownProgress => _shownProgress;
        public float Flash => _flash;
        public float Pulse => _pulse;
        public float Calm => _calm;
        public bool Visible => ring != null && ring.enabled;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>Dev check: volleys that fired before the sweep was full (should stay 0).</summary>
        public int EarlyShots { get; private set; }
#endif

        /// <summary>Wiring for code-built objects and tests.</summary>
        public void Setup(AutoShooter playerShooter, PlayerMover playerMover, Renderer ringRenderer)
        {
            Unsubscribe();
            shooter = playerShooter;
            mover = playerMover;
            ring = ringRenderer;
            if (isActiveAndEnabled) Subscribe();
            ApplyVisibility();
        }

        private void Awake()
        {
            _block = new MaterialPropertyBlock();
            if (shooter == null) shooter = GetComponent<AutoShooter>();
            if (mover == null) mover = GetComponent<PlayerMover>();
        }

        private void OnEnable()
        {
            Subscribe();
            GameSettings.Changed += ApplyVisibility;
            ApplyVisibility();
        }

        private void OnDisable()
        {
            Unsubscribe();
            GameSettings.Changed -= ApplyVisibility;
        }

        private void Subscribe()
        {
            if (_subscribed || shooter == null) return;
            shooter.Fired += OnFired;
            shooter.ReloadCompleted += OnReloadCompleted;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || shooter == null) return;
            shooter.Fired -= OnFired;
            shooter.ReloadCompleted -= OnReloadCompleted;
            _subscribed = false;
        }

        private void ApplyVisibility()
        {
            if (ring != null) ring.enabled = GameSettings.ReloadRing;
        }

        private void OnReloadCompleted() => _flash = 1f;

        private void OnFired(Vector3 muzzle, Vector3 direction)
        {
            _pulse = 1f;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Fired is raised before the shooter resets its reload, so a volley on time always sees a full sweep here.
            // The ring draws that same value this frame, so fire and sweep end share the frame (error 0 frames).
            if (shooter.ReloadProgress < 1f)
            {
                EarlyShots++;
                Debug.LogWarning($"[ReloadRing] Volley fired at sweep {shooter.ReloadProgress:0.000}, before the sweep ended.");
            }
#endif
        }

        private void LateUpdate() => Tick(Time.deltaTime);

        /// <summary>One frame of ring state and drawing. Public so tests can step exact frame times.</summary>
        public void Tick(float dt)
        {
            if (shooter == null || ring == null) return;
            _shownProgress = shooter.ReloadProgress;
            _flash = Mathf.Max(0f, _flash - dt / flashSeconds);
            _pulse = Mathf.Max(0f, _pulse - dt / pulseSeconds);

            // Calm = full dim ring with nothing lined up. A reload in progress still sweeps (it is real) but at calm brightness.
            bool calm = !shooter.Engaged;
            _calm = Mathf.MoveTowards(_calm, calm ? 1f : 0f, calmBlendSpeed * dt);

            float run = mover != null && mover.MaxSpeed > 0f ? Mathf.Clamp01(mover.Velocity.magnitude / mover.MaxSpeed) : 0f;
            _alpha = Mathf.MoveTowards(_alpha, Mathf.Lerp(1f, runAlpha, run), runFadeSpeed * dt);

            if (!ring.enabled) return;
            ring.transform.rotation = Quaternion.Euler(90f, 0f, 0f); // flat, +V = world +Z = 12 o'clock on screen
            ring.GetPropertyBlock(_block);
            _block.SetFloat(ProgressId, _shownProgress);
            _block.SetFloat(FlashId, _flash);
            _block.SetFloat(PulseId, _pulse);
            _block.SetFloat(CalmId, _calm);
            _block.SetFloat(AlphaId, _alpha);
            ring.SetPropertyBlock(_block);
        }
    }
}
