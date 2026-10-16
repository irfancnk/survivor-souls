using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Skills.Effects;
using UnityEngine;

namespace SurvivorSouls.Audio
{
    /// <summary>
    /// Soul Burst sound (KAN-66): a dull low whump with a short dark chime tail on every visual burst
    /// (<see cref="KillBurstEffect.Burst"/>, already merged per frame). Pitch and volume vary per play; deeper chain links
    /// play a little quieter and higher, so a chain reads as a ripple. Voice cap: <see cref="voices"/> sources, a new burst
    /// takes a free one or is dropped while all ring (no stacking into noise), and two bursts closer than
    /// <see cref="minInterval"/> give one sound. 2D, volume from <see cref="GameSettings.SfxVolume"/>. No GC per burst.
    /// </summary>
    public sealed class SoulBurstSounds : MonoBehaviour
    {
        [SerializeField] private AudioClip[] bursts;
        [SerializeField, Range(0f, 1f)] private float volume = 0.55f;
        [SerializeField, Range(0f, 0.3f)] private float pitchJitter = 0.06f;
        [Tooltip("Volume lost per chain link (share), down to minChainVolume.")]
        [SerializeField, Range(0f, 0.5f)] private float chainVolumeStep = 0.12f;
        [SerializeField, Range(0f, 1f)] private float minChainVolume = 0.4f;
        [Tooltip("Pitch gained per chain link (the ripple rises a little).")]
        [SerializeField, Range(0f, 0.2f)] private float chainPitchStep = 0.03f;
        [SerializeField, Range(1, 6)] private int voices = 3;
        [SerializeField, Min(0f)] private float minInterval = 0.06f;

        private AudioSource[] _pool;
        private float[] _busyUntil; // own voice clock (unscaled s): does not depend on the audio device
        private float _last = float.NegativeInfinity;
        private int _lastClip = -1;

        /// <summary>Bursts that played a sound (tests).</summary>
        public int Played { get; private set; }
        /// <summary>Bursts dropped by the rate limit or the voice cap (tests).</summary>
        public int Dropped { get; private set; }
        /// <summary>Voice sources (tests).</summary>
        public int Voices => _pool?.Length ?? 0;

        /// <summary>Wires the clips (tests; the scene builder sets the serialized field).</summary>
        public void Setup(AudioClip[] clips) => bursts = clips;

        private void Awake()
        {
            _pool = new AudioSource[voices];
            _busyUntil = new float[voices];
            for (int i = 0; i < voices; i++) _busyUntil[i] = float.NegativeInfinity;
            for (int i = 0; i < voices; i++)
            {
                var go = new GameObject($"Soul Burst Voice {i}");
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                _pool[i] = src;
            }
        }

        private void OnEnable() => KillBurstEffect.Burst += OnBurst;
        private void OnDisable() => KillBurstEffect.Burst -= OnBurst;

        private void OnBurst(Vector3 centre, int depth) => Play(depth, Time.unscaledTime);

        /// <summary>Plays one burst at <paramref name="now"/> (unscaled seconds). Returns false when limited. Public for tests.</summary>
        public bool Play(int depth, float now)
        {
            if (bursts == null || bursts.Length == 0 || _pool == null) return false;
            if (now - _last < minInterval) { Dropped++; return false; }
            int slot = -1;
            for (int i = 0; i < _pool.Length; i++)
                if (now >= _busyUntil[i]) { slot = i; break; }
            if (slot < 0) { Dropped++; return false; }
            var free = _pool[slot];
            _last = now;
            int c = Random.Range(0, bursts.Length);
            if (bursts.Length > 1 && c == _lastClip) c = (c + 1) % bursts.Length;
            _lastClip = c;
            int d = Mathf.Max(0, depth);
            float v = volume * Mathf.Max(minChainVolume, 1f - chainVolumeStep * d) * Random.Range(0.88f, 1f);
            free.clip = bursts[c];
            free.pitch = 1f + chainPitchStep * Mathf.Min(d, 6) + Random.Range(-pitchJitter, pitchJitter);
            free.volume = v * GameSettings.SfxVolume;
            free.Play();
            _busyUntil[slot] = now + bursts[c].length / Mathf.Max(0.1f, free.pitch);
            Played++;
            return true;
        }
    }
}
