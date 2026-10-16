using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Skills.Effects;
using UnityEngine;

namespace SurvivorSouls.Audio
{
    /// <summary>
    /// Freeze Arrow sound (KAN-91): a sharp ice crack with a glassy crystal ring on every freeze
    /// (<see cref="FreezeArrowEffect.Frozen"/>). Bosses and big foes play a little lower. Voice cap: <see cref="voices"/>
    /// sources on an own "busy until" clock, and two freezes closer than <see cref="minInterval"/> give one sound, so a
    /// horde freezing in a row gives a few cracks, not noise. 2D, volume from <see cref="GameSettings.SfxVolume"/>. No GC per freeze.
    /// </summary>
    public sealed class FrostSounds : MonoBehaviour
    {
        [SerializeField] private AudioClip[] freezes;
        [SerializeField, Range(0f, 1f)] private float volume = 0.5f;
        [SerializeField, Range(0f, 0.3f)] private float pitchJitter = 0.06f;
        [SerializeField, Range(1, 6)] private int voices = 3;
        [SerializeField, Min(0f)] private float minInterval = 0.07f;

        private AudioSource[] _pool;
        private float[] _busyUntil; // own voice clock (unscaled s): does not depend on the audio device
        private float _last = float.NegativeInfinity;
        private int _lastClip = -1;

        /// <summary>Freezes that played a sound (tests).</summary>
        public int Played { get; private set; }
        /// <summary>Freezes dropped by the rate limit or the voice cap (tests).</summary>
        public int Dropped { get; private set; }
        /// <summary>Voice sources (tests).</summary>
        public int Voices => _pool?.Length ?? 0;

        /// <summary>Wires the clips (tests; the scene builder sets the serialized field).</summary>
        public void Setup(AudioClip[] clips) => freezes = clips;

        private void Awake()
        {
            _pool = new AudioSource[voices];
            _busyUntil = new float[voices];
            for (int i = 0; i < voices; i++)
            {
                _busyUntil[i] = float.NegativeInfinity;
                var go = new GameObject($"Frost Voice {i}");
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                _pool[i] = src;
            }
        }

        private void OnEnable() => FreezeArrowEffect.Frozen += OnFrozen;
        private void OnDisable() => FreezeArrowEffect.Frozen -= OnFrozen;

        // Dev builds: one summary line at quit, so a Mac / phone dev run shows the freezes sounded (KAN-91 check).
        private void OnDestroy()
        {
            if (Debug.isDebugBuild && !Application.isEditor && Played > 0)
                Debug.Log($"[FrostSounds] freezes played {Played}, dropped {Dropped}");
        }

        private void OnFrozen(Enemy enemy) => Play(enemy != null && (enemy.IsBoss || enemy.Scale > 1.2f), Time.unscaledTime);

        /// <summary>Plays one freeze crack at <paramref name="now"/> (unscaled s). Returns false when limited. Public for tests.</summary>
        public bool Play(bool big, float now)
        {
            if (freezes == null || freezes.Length == 0 || _pool == null) return false;
            if (now - _last < minInterval) { Dropped++; return false; }
            int slot = -1;
            for (int i = 0; i < _pool.Length; i++)
                if (now >= _busyUntil[i]) { slot = i; break; }
            if (slot < 0) { Dropped++; return false; }
            _last = now;
            int c = Random.Range(0, freezes.Length);
            if (freezes.Length > 1 && c == _lastClip) c = (c + 1) % freezes.Length;
            _lastClip = c;
            var src = _pool[slot];
            src.clip = freezes[c];
            src.pitch = (big ? 0.82f : 1f) + Random.Range(-pitchJitter, pitchJitter);
            src.volume = volume * (big ? 1f : Random.Range(0.85f, 1f)) * GameSettings.SfxVolume;
            src.Play();
            _busyUntil[slot] = now + freezes[c].length / Mathf.Max(0.1f, src.pitch);
            Played++;
            return true;
        }
    }
}
