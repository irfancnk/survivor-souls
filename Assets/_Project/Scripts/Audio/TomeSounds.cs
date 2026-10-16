using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Skills.Effects;
using UnityEngine;

namespace SurvivorSouls.Audio
{
    /// <summary>
    /// Holy Tomes sound (KAN-123): a dull leather "thump" with a faint holy chime on a tome hit
    /// (<see cref="OrbitingTomesEffect.TomeHit"/>), at most one per <see cref="minInterval"/> (8 per second) on
    /// <see cref="voices"/> sources with an own "busy until" clock; and a very quiet page whoosh loop while books are on
    /// show (<see cref="OrbitingTomesEffect.VisibleTomes"/>), a little louder with more books, faded out on pause and while
    /// hidden. 2D, volume from <see cref="GameSettings.SfxVolume"/>. No GC per hit.
    /// </summary>
    public sealed class TomeSounds : MonoBehaviour
    {
        [SerializeField] private AudioClip[] thumps;
        [SerializeField] private AudioClip whoosh;
        [SerializeField, Range(0f, 1f)] private float volume = 0.42f;
        [SerializeField, Range(0f, 0.3f)] private float pitchJitter = 0.08f;
        [SerializeField, Range(1, 6)] private int voices = 3;
        [Tooltip("Seconds between two thumps (0.125 = at most 8 per second).")]
        [SerializeField, Min(0f)] private float minInterval = 0.125f;
        [SerializeField, Range(0f, 1f)] private float whooshVolume = 0.08f;
        [SerializeField, Range(0f, 0.2f)] private float whooshPerTome = 0.015f;
        [SerializeField, Min(0.01f)] private float whooshFade = 0.4f;

        private AudioSource[] _pool;
        private float[] _busyUntil; // own voice clock (unscaled s): does not depend on the audio device
        private AudioSource _loop;
        private float _loopLevel;
        private float _last = float.NegativeInfinity;
        private int _lastClip = -1;

        /// <summary>Hits that played a thump (tests).</summary>
        public int Played { get; private set; }
        /// <summary>Hits dropped by the rate limit or the voice cap (tests).</summary>
        public int Dropped { get; private set; }
        /// <summary>Voice sources (tests).</summary>
        public int Voices => _pool?.Length ?? 0;
        /// <summary>Current whoosh loop level 0..1 before the volume settings (tests).</summary>
        public float LoopLevel => _loopLevel;

        /// <summary>Wires the clips (tests; the scene builder sets the serialized fields).</summary>
        public void Setup(AudioClip[] hits, AudioClip loop)
        {
            thumps = hits;
            whoosh = loop;
        }

        private void Awake()
        {
            _pool = new AudioSource[voices];
            _busyUntil = new float[voices];
            for (int i = 0; i < voices; i++)
            {
                _busyUntil[i] = float.NegativeInfinity;
                var go = new GameObject($"Tome Voice {i}");
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                _pool[i] = src;
            }
            var loopGo = new GameObject("Tome Whoosh");
            loopGo.transform.SetParent(transform, false);
            _loop = loopGo.AddComponent<AudioSource>();
            _loop.playOnAwake = false;
            _loop.loop = true;
            _loop.spatialBlend = 0f;
            _loop.volume = 0f;
        }

        private void OnEnable() => OrbitingTomesEffect.TomeHit += OnHit;
        private void OnDisable()
        {
            OrbitingTomesEffect.TomeHit -= OnHit;
            if (_loop != null) _loop.Stop();
            _loopLevel = 0f;
        }

        // Dev builds: one summary line at quit, so a Mac / phone dev run shows the tome hits sounded (KAN-123 check).
        private void OnDestroy()
        {
            if (Debug.isDebugBuild && !Application.isEditor && Played > 0)
                Debug.Log($"[TomeSounds] thumps played {Played}, dropped {Dropped}");
        }

        private void Update() => TickLoop(OrbitingTomesEffect.VisibleTomes, GamePause.IsPaused, Time.unscaledDeltaTime);

        /// <summary>Eases the whoosh loop toward its level for <paramref name="tomes"/> books on show. Public for tests.</summary>
        public void TickLoop(int tomes, bool paused, float dt)
        {
            if (_loop == null) return;
            float target = tomes > 0 && !paused && whoosh != null ? 1f : 0f;
            _loopLevel = Mathf.MoveTowards(_loopLevel, target, dt / whooshFade);
            if (_loopLevel <= 0f)
            {
                if (_loop.isPlaying) _loop.Stop();
                return;
            }
            if (_loop.clip != whoosh) _loop.clip = whoosh;
            if (!_loop.isPlaying) _loop.Play();
            _loop.volume = _loopLevel * (whooshVolume + whooshPerTome * Mathf.Max(0, tomes - 1)) * GameSettings.SfxVolume;
        }

        private void OnHit(Enemy enemy, Vector3 point) => Play(enemy != null && (enemy.IsBoss || enemy.Scale > 1.2f), Time.unscaledTime);

        /// <summary>Plays one thump at <paramref name="now"/> (unscaled s). Returns false when limited. Public for tests.</summary>
        public bool Play(bool big, float now)
        {
            if (thumps == null || thumps.Length == 0 || _pool == null) return false;
            if (now - _last < minInterval) { Dropped++; return false; }
            int slot = -1;
            for (int i = 0; i < _pool.Length; i++)
                if (now >= _busyUntil[i]) { slot = i; break; }
            if (slot < 0) { Dropped++; return false; }
            _last = now;
            int c = Random.Range(0, thumps.Length);
            if (thumps.Length > 1 && c == _lastClip) c = (c + 1) % thumps.Length;
            _lastClip = c;
            var src = _pool[slot];
            src.clip = thumps[c];
            src.pitch = (big ? 0.85f : 1f) + Random.Range(-pitchJitter, pitchJitter);
            src.volume = volume * Random.Range(0.85f, 1f) * GameSettings.SfxVolume;
            src.Play();
            _busyUntil[slot] = now + thumps[c].length / Mathf.Max(0.1f, src.pitch);
            Played++;
            return true;
        }
    }
}
