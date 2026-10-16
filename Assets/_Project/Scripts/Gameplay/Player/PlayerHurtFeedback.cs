using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.CameraRig;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Player
{
    /// <summary>
    /// Knight-level hurt feel for discrete hits (KAN-187, follow-up of the KAN-82 health bar). Listens to
    /// <see cref="PlayerHealth.OnHit"/> only, so horde contact and burning ground never shake, freeze or buzz:
    /// <list type="bullet">
    /// <item>Camera shake scaled by the damage share (<see cref="ShakeFor"/>), capped.</item>
    /// <item>Hit-stop: a ~40 ms freeze (time scale 0, real-time clock) for big hits only (<see cref="WantsHitStop"/>).
    /// Never while <see cref="GamePause"/> holds or a slow motion runs (time scale not 1), never on the killing blow. It ends
    /// by setting time scale back to 1 only when nothing else changed it meanwhile and no pause hold is open, so it never
    /// fights the level-up pause or the boss slow motion.</item>
    /// <item>Light haptic, rate limited to one per <see cref="hapticInterval"/> s.</item>
    /// </list>
    /// "Reduce screen effects" (<see cref="GameSettings.ReduceScreenEffects"/>) turns off the shake and the hit-stop; the
    /// haptic follows the Vibration setting (<see cref="Haptics"/>). Allocation-free per hit.
    /// </summary>
    [RequireComponent(typeof(PlayerHealth))]
    public sealed class PlayerHurtFeedback : MonoBehaviour
    {
        [SerializeField] private TopDownFollowCamera followCamera;

        [Header("Camera shake")]
        [Tooltip("Shake (m) of the smallest discrete hit.")]
        [SerializeField, Min(0f)] private float baseShake = 0.05f;
        [Tooltip("Extra shake (m) per 1.0 share of max health lost in one hit.")]
        [SerializeField, Min(0f)] private float shakePerShare = 1.1f;
        [Tooltip("Shake cap (m).")]
        [SerializeField, Min(0f)] private float maxShake = 0.3f;
        [SerializeField, Min(0.02f)] private float shakeTime = 0.24f;

        [Header("Hit-stop")]
        [Tooltip("A single hit of at least this share of max health freezes the game for a moment.")]
        [SerializeField, Range(0f, 1f)] private float hitStopShare = 0.12f;
        [Tooltip("Freeze length (real seconds).")]
        [SerializeField, Range(0f, 0.2f)] private float hitStopTime = 0.04f;

        [Header("Haptic")]
        [SerializeField, Min(0f)] private float hapticInterval = 0.3f;

        private PlayerHealth _health;
        private bool _reduceEffects;
        private float _stopLeft;
        private bool _frozen; // this component set time scale 0 and still owes the undo
        private float _lastHaptic = float.NegativeInfinity;

        /// <summary>A hit-stop freeze is running.</summary>
        public bool HitStopping => _frozen;
        /// <summary>Hit-stops started (tests, dev overlays).</summary>
        public int HitStops { get; private set; }
        /// <summary>Camera shakes asked for (tests).</summary>
        public int Shakes { get; private set; }
        /// <summary>Amplitude (m) of the last shake asked for (tests).</summary>
        public float LastShake { get; private set; }
        /// <summary>Haptic pulses asked for (before the Vibration setting; tests).</summary>
        public int HapticRequests { get; private set; }

        /// <summary>Wires the camera (tests; the scene builder sets the serialized field).</summary>
        public void Setup(TopDownFollowCamera cam) => followCamera = cam;

        /// <summary>Shake amplitude (m) for a hit worth <paramref name="share"/> of max health: base + share x slope, capped.</summary>
        public static float ShakeFor(float share, float baseShake, float perShare, float max) =>
            Mathf.Min(max, baseShake + Mathf.Max(0f, share) * perShare);

        /// <summary>Whether a single hit worth <paramref name="share"/> of max health earns a hit-stop.</summary>
        public static bool WantsHitStop(float share, float threshold) => threshold > 0f && share >= threshold;

        private void Awake() => _health = GetComponent<PlayerHealth>();

        private void OnEnable()
        {
            _health.OnHit += OnHit;
            GameSettings.Changed += ReadSettings;
            ReadSettings();
        }

        private void OnDisable()
        {
            _health.OnHit -= OnHit;
            GameSettings.Changed -= ReadSettings;
            EndHitStop();
        }

        private void ReadSettings() => _reduceEffects = GameSettings.ReduceScreenEffects;

        private void OnHit(PlayerHit hit)
        {
            if (hit.Dealt <= 0f) return; // the shield took it: the arc shows the side, nothing shakes

            float now = Time.unscaledTime;
            if (now - _lastHaptic >= hapticInterval)
            {
                _lastHaptic = now;
                HapticRequests++;
                Haptics.Light();
            }

            if (_reduceEffects) return;
            LastShake = ShakeFor(hit.Share, baseShake, shakePerShare, maxShake);
            Shakes++;
            if (followCamera != null) followCamera.Shake(LastShake, shakeTime);

            if (!hit.Killed && !HitStopping && WantsHitStop(hit.Share, hitStopShare) && hitStopTime > 0f
                && !GamePause.IsPaused && Mathf.Approximately(Time.timeScale, 1f))
            {
                _stopLeft = hitStopTime;
                _frozen = true;
                HitStops++;
                Time.timeScale = 0f;
            }
        }

        private void Update() => Tick(Time.unscaledDeltaTime);

        /// <summary>Counts the hit-stop down by <paramref name="realDt"/> real seconds. Public for tests (Update calls it).</summary>
        public void Tick(float realDt)
        {
            if (!_frozen) return;
            _stopLeft -= realDt;
            if (_stopLeft <= 0f) EndHitStop();
        }

        private void EndHitStop()
        {
            if (!_frozen) return;
            _frozen = false;
            _stopLeft = 0f;
            // Only undo our own freeze: a pause hold or a slow motion that started meanwhile owns the time scale now.
            if (Time.timeScale == 0f && !GamePause.IsPaused) Time.timeScale = 1f;
        }
    }
}
