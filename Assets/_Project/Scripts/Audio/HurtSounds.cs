using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Player;
using UnityEngine;

namespace SurvivorSouls.Audio
{
    /// <summary>
    /// Knight hurt sounds (KAN-187). A discrete hit (<see cref="PlayerHealth.OnHit"/>: ranged shot, boss attack, telegraphed
    /// special) plays a dull body thud + short grunt (SfxBuilder <c>sfx_knight_grunt_*</c>), louder for bigger hits; the
    /// plain contact pulse keeps <see cref="GameAudio"/>'s impact, so a horde does not grunt. Below
    /// <see cref="lowShare"/> health a quiet slow heartbeat (lub-dub, <c>sfx_heartbeat_01</c>) beats, faster below
    /// <see cref="criticalShare"/> (same thresholds as the health bar, <see cref="SurvivorSouls.UI.Hud.HealthBarFeel"/>).
    /// The heartbeat has its own setting (<see cref="GameSettings.HeartbeatSound"/>), runs on scaled time (silent under a
    /// pause) and stops on death. Two voices, never more. Volumes follow <see cref="GameSettings.SfxVolume"/>.
    /// </summary>
    public sealed class HurtSounds : MonoBehaviour
    {
        [SerializeField] private PlayerHealth health;

        [Header("Clips (SfxBuilder)")]
        [SerializeField] private AudioClip[] grunts;
        [SerializeField] private AudioClip heartbeat;

        [Header("Hit")]
        [SerializeField, Range(0f, 1f)] private float gruntVolume = 0.55f;
        [Tooltip("Extra grunt volume for a hit worth this share of max health or more (linear up to it).")]
        [SerializeField, Range(0.01f, 1f)] private float loudShare = 0.3f;
        [SerializeField, Range(0f, 1f)] private float loudGruntVolume = 0.85f;
        [Tooltip("Grunts closer than this (s, real time) collapse into one.")]
        [SerializeField, Min(0f)] private float gruntInterval = 0.25f;
        [SerializeField, Range(0f, 0.3f)] private float pitchJitter = 0.05f;

        [Header("Heartbeat")]
        [Tooltip("Below this health share: slow heartbeat. Keep equal to HealthBarFeel.lowShare.")]
        [SerializeField, Range(0f, 1f)] private float lowShare = 0.3f;
        [Tooltip("Below this health share: faster, a bit louder. Keep equal to HealthBarFeel.criticalShare.")]
        [SerializeField, Range(0f, 1f)] private float criticalShare = 0.15f;
        [SerializeField, Min(0.1f)] private float beatsPerSecond = 1.1f;
        [SerializeField, Min(0.1f)] private float criticalBeatsPerSecond = 1.9f;
        [SerializeField, Range(0f, 1f)] private float heartbeatVolume = 0.32f;
        [SerializeField, Range(0f, 1f)] private float criticalHeartbeatVolume = 0.45f;

        private AudioSource _voice, _heart;
        private float _lastGrunt = float.NegativeInfinity;
        private float _beatIn;
        private bool _heartbeatOn;
        private int _lastClip = -1;

        /// <summary>Grunts played (tests).</summary>
        public int Grunts { get; private set; }
        /// <summary>Heartbeats played (tests).</summary>
        public int Beats { get; private set; }
        /// <summary>Volume of the last grunt before the SFX volume (tests).</summary>
        public float LastGruntVolume { get; private set; }

        /// <summary>Heartbeat state for a health share: 0 none, 1 low, 2 critical.</summary>
        public static int HeartState(float healthShare, float low, float critical) =>
            healthShare < critical ? 2 : healthShare < low ? 1 : 0;

        /// <summary>Grunt volume for a hit worth <paramref name="share"/> of max health (before the SFX volume).</summary>
        public static float GruntVolumeFor(float share, float baseVolume, float loudVolume, float loudShare) =>
            Mathf.Lerp(baseVolume, loudVolume, Mathf.Clamp01(share / Mathf.Max(0.0001f, loudShare)));

        /// <summary>Wires the Knight and the clips (tests; the scene builder sets the serialized fields).</summary>
        public void Setup(PlayerHealth knight, AudioClip[] gruntClips, AudioClip heartbeatClip)
        {
            if (isActiveAndEnabled && health != null) health.OnHit -= OnHit;
            health = knight;
            grunts = gruntClips;
            heartbeat = heartbeatClip;
            if (isActiveAndEnabled && health != null) health.OnHit += OnHit;
        }

        private void Awake()
        {
            _voice = NewSource("Hurt Voice");
            _heart = NewSource("Heartbeat");
        }

        private void OnEnable()
        {
            if (health != null) health.OnHit += OnHit;
            GameSettings.Changed += ReadSettings;
            ReadSettings();
        }

        private void OnDisable()
        {
            if (health != null) health.OnHit -= OnHit;
            GameSettings.Changed -= ReadSettings;
        }

        private void ReadSettings() => _heartbeatOn = GameSettings.HeartbeatSound;

        private void OnHit(PlayerHit hit)
        {
            if (hit.Dealt <= 0f || hit.Killed) return; // shield took it, or the death groan plays instead
            float now = Time.unscaledTime;
            if (now - _lastGrunt < gruntInterval || grunts == null || grunts.Length == 0) return;
            _lastGrunt = now;
            int i = Random.Range(0, grunts.Length);
            if (grunts.Length > 1 && i == _lastClip) i = (i + 1) % grunts.Length;
            _lastClip = i;
            if (grunts[i] == null) return;
            LastGruntVolume = GruntVolumeFor(hit.Share, gruntVolume, loudGruntVolume, loudShare);
            _voice.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            _voice.PlayOneShot(grunts[i], LastGruntVolume * GameSettings.SfxVolume);
            Grunts++;
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Steps the heartbeat by <paramref name="dt"/> scaled seconds. Public for tests (Update calls it).</summary>
        public void Tick(float dt)
        {
            if (health == null || health.IsDead || !_heartbeatOn || dt <= 0f)
            {
                if (health == null || health.IsDead) _beatIn = 0f;
                return;
            }
            int state = HeartState(health.Normalized, lowShare, criticalShare);
            if (state == 0)
            {
                _beatIn = 0f; // the first beat comes at once when health drops again
                return;
            }
            _beatIn -= dt;
            if (_beatIn > 0f) return;
            _beatIn = 1f / (state == 2 ? criticalBeatsPerSecond : beatsPerSecond);
            Beats++;
            if (heartbeat != null)
                _heart.PlayOneShot(heartbeat, (state == 2 ? criticalHeartbeatVolume : heartbeatVolume) * GameSettings.SfxVolume);
        }

        private AudioSource NewSource(string sourceName)
        {
            var go = new GameObject(sourceName);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            return src;
        }
    }
}
