using System;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Audio
{
    /// <summary>
    /// Special melee zombie sounds (KAN-132): every attack is heard at its wind-up and at its strike.
    /// <list type="bullet">
    /// <item>Plague Bloater: a wet gurgle that rises in pitch through the 1 s swell (<see cref="SpecialAttack.WindUpStarted"/>),
    /// a pop + wet splash at the burst (<see cref="BloaterBurst.Burst"/>), a quiet hiss loop while a toxic cloud lives (one
    /// voice, faded in / out, scanned every <see cref="scanInterval"/> s).</item>
    /// <item>Frenzied Ghoul: a short screech at the crouch, a whoosh at the take-off (<see cref="SpecialAttack.Struck"/>), a
    /// thud on the landing (<see cref="GhoulLeap.Landed"/>).</item>
    /// <item>Grave Brute: a grunt at the wind-up, a heavy boom at the slam (<see cref="SpecialAttack.Struck"/>).</item>
    /// </list>
    /// Each sound has its own small voice pool with an own "busy until" clock (no audio-device isPlaying, KAN-66) and a rate
    /// limit, so a horde gives a few voices, not noise. 2D like the other combat sounds, with a distance fade to the Knight
    /// (<see cref="nearDistance"/> full, silent from <see cref="farDistance"/>). Volume from <see cref="GameSettings.SfxVolume"/>.
    /// No GC per attack or frame.
    /// </summary>
    public sealed class SpecialMeleeSounds : MonoBehaviour
    {
        /// <summary>Sound ids (index into the layers).</summary>
        public enum Sound { BloaterSwell, BloaterBurst, GhoulScreech, GhoulWhoosh, GhoulThud, BruteGrunt, BruteSlam }

        private const int SoundCount = 7;

        [Serializable]
        public struct Layer
        {
            public AudioClip[] clips;
            [Range(0f, 1f)] public float volume;
            [Range(0f, 0.3f)] public float pitchJitter;
            [Range(1, 6)] public int voices;
            [Min(0f)] public float minInterval;

            public Layer(float volume, float pitchJitter, int voices, float minInterval)
            {
                clips = null;
                this.volume = volume;
                this.pitchJitter = pitchJitter;
                this.voices = voices;
                this.minInterval = minInterval;
            }
        }

        [SerializeField] private Layer bloaterSwell = new Layer(0.42f, 0.05f, 2, 0.15f);
        [SerializeField] private Layer bloaterBurst = new Layer(0.6f, 0.07f, 3, 0.06f);
        [SerializeField] private Layer ghoulScreech = new Layer(0.36f, 0.08f, 2, 0.2f);
        [SerializeField] private Layer ghoulWhoosh = new Layer(0.3f, 0.08f, 2, 0.08f);
        [SerializeField] private Layer ghoulThud = new Layer(0.45f, 0.07f, 2, 0.08f);
        [SerializeField] private Layer bruteGrunt = new Layer(0.45f, 0.05f, 2, 0.2f);
        [SerializeField] private Layer bruteSlam = new Layer(0.72f, 0.05f, 2, 0.1f);

        [Header("Toxic cloud hiss")]
        [SerializeField] private AudioClip cloudHiss;
        [SerializeField, Range(0f, 1f)] private float hissVolume = 0.14f;
        [Tooltip("Hiss volume change per second (fade in / out).")]
        [SerializeField, Min(0.01f)] private float hissFade = 0.5f;
        [SerializeField, Min(0.02f)] private float scanInterval = 0.25f;

        [Header("Distance to the Knight")]
        [SerializeField, Min(0f)] private float nearDistance = 7f;
        [SerializeField, Min(0.1f)] private float farDistance = 20f;

        private AudioSource[][] _pools;
        private float[][] _busy; // own voice clock (unscaled s): does not depend on the audio device
        private readonly float[] _last = new float[SoundCount];
        private readonly int[] _lastClip = new int[SoundCount];
        private readonly int[] _played = new int[SoundCount];
        private readonly int[] _dropped = new int[SoundCount];
        private AudioSource _hiss;
        private float _hissLevel;
        private float _scanLeft;

        /// <summary>Plays of <paramref name="sound"/> (tests, dev log).</summary>
        public int Played(Sound sound) => _played[(int)sound];
        /// <summary>Plays of <paramref name="sound"/> dropped by the rate limit, the voice cap or the distance (tests).</summary>
        public int Dropped(Sound sound) => _dropped[(int)sound];
        /// <summary>Voice sources of <paramref name="sound"/> (tests).</summary>
        public int Voices(Sound sound) => _pools != null ? _pools[(int)sound].Length : 0;
        /// <summary>Toxic clouds alive at the last scan (tests).</summary>
        public int Clouds { get; private set; }
        /// <summary>The cloud hiss loop plays (tests).</summary>
        public bool Hissing => _hiss != null && _hiss.isPlaying;
        /// <summary>Hiss level 0..1 before the master volume (tests).</summary>
        public float HissLevel => _hissLevel;

        /// <summary>Wires the clips (tests; the scene builder sets the serialized fields). Index = <see cref="Sound"/>.</summary>
        public void Setup(AudioClip[][] clips, AudioClip hiss)
        {
            for (int i = 0; i < SoundCount && i < clips.Length; i++)
            {
                var layer = LayerOf((Sound)i);
                layer.clips = clips[i];
                SetLayer((Sound)i, layer);
            }
            cloudHiss = hiss;
            if (_hiss != null) _hiss.clip = hiss;
        }

        private void Awake()
        {
            _pools = new AudioSource[SoundCount][];
            _busy = new float[SoundCount][];
            for (int s = 0; s < SoundCount; s++)
            {
                var layer = LayerOf((Sound)s);
                int n = Mathf.Clamp(layer.voices, 1, 6);
                _pools[s] = new AudioSource[n];
                _busy[s] = new float[n];
                for (int i = 0; i < n; i++)
                {
                    _busy[s][i] = float.NegativeInfinity;
                    _pools[s][i] = NewSource($"Melee {(Sound)s} Voice {i}");
                }
                _last[s] = float.NegativeInfinity;
                _lastClip[s] = -1;
            }
            _hiss = NewSource("Melee Cloud Hiss Loop");
            _hiss.loop = true;
            _hiss.clip = cloudHiss;
        }

        private AudioSource NewSource(string label)
        {
            var go = new GameObject(label);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;
            return src;
        }

        private void OnEnable()
        {
            SpecialAttack.WindUpStarted += OnWindUp;
            SpecialAttack.Struck += OnStruck;
            BloaterBurst.Burst += OnBurst;
            GhoulLeap.Landed += OnLanded;
        }

        private void OnDisable()
        {
            SpecialAttack.WindUpStarted -= OnWindUp;
            SpecialAttack.Struck -= OnStruck;
            BloaterBurst.Burst -= OnBurst;
            GhoulLeap.Landed -= OnLanded;
            if (_hiss != null) _hiss.Stop();
            _hissLevel = 0f;
        }

        // Dev builds: one summary line at quit, so a Mac / phone dev run shows the sounds fired (KAN-132 check).
        private void OnDestroy()
        {
            if (!Debug.isDebugBuild || Application.isEditor) return;
            int total = 0;
            for (int i = 0; i < SoundCount; i++) total += _played[i];
            if (total > 0)
                Debug.Log($"[SpecialMeleeSounds] played swell {_played[0]}, burst {_played[1]}, screech {_played[2]}, whoosh {_played[3]}, thud {_played[4]}, grunt {_played[5]}, slam {_played[6]}");
        }

        private void OnWindUp(SpecialAttack attack)
        {
            if (attack is BloaterBurst) Play(Sound.BloaterSwell, attack.transform.position, Time.unscaledTime);
            else if (attack is GhoulLeap) Play(Sound.GhoulScreech, attack.transform.position, Time.unscaledTime);
            else if (attack is BruteSlam) Play(Sound.BruteGrunt, attack.transform.position, Time.unscaledTime);
        }

        private void OnStruck(SpecialAttack attack)
        {
            if (attack is GhoulLeap) Play(Sound.GhoulWhoosh, attack.transform.position, Time.unscaledTime);
            else if (attack is BruteSlam) Play(Sound.BruteSlam, attack.transform.position, Time.unscaledTime);
            // The Bloater's strike is its burst: OnBurst (it carries the centre of a corpse burst too).
        }

        private void OnBurst(BloaterBurst bloater, Vector3 centre, float dealt, bool byKill) =>
            Play(Sound.BloaterBurst, centre, Time.unscaledTime);

        private void OnLanded(GhoulLeap ghoul, Vector3 at, bool hit) => Play(Sound.GhoulThud, at, Time.unscaledTime);

        /// <summary>
        /// Plays <paramref name="sound"/> for an attack at <paramref name="at"/> at <paramref name="now"/> (unscaled seconds).
        /// Returns false when limited (rate, voices) or too far from the Knight. Public for tests.
        /// </summary>
        public bool Play(Sound sound, Vector3 at, float now)
        {
            int s = (int)sound;
            var layer = LayerOf(sound);
            if (layer.clips == null || layer.clips.Length == 0 || _pools == null) return false;
            float near = Proximity(at);
            if (near <= 0f || now - _last[s] < layer.minInterval) { _dropped[s]++; return false; }
            var pool = _pools[s];
            var busy = _busy[s];
            int slot = -1;
            for (int i = 0; i < pool.Length; i++)
                if (now >= busy[i]) { slot = i; break; }
            if (slot < 0) { _dropped[s]++; return false; }
            _last[s] = now;
            int c = UnityEngine.Random.Range(0, layer.clips.Length);
            if (layer.clips.Length > 1 && c == _lastClip[s]) c = (c + 1) % layer.clips.Length;
            _lastClip[s] = c;
            var src = pool[slot];
            src.clip = layer.clips[c];
            src.pitch = 1f + UnityEngine.Random.Range(-layer.pitchJitter, layer.pitchJitter);
            src.volume = layer.volume * near * UnityEngine.Random.Range(0.88f, 1f) * GameSettings.SfxVolume;
            src.Play();
            busy[slot] = now + (src.clip != null ? src.clip.length : 0.5f) / Mathf.Max(0.1f, src.pitch);
            _played[s]++;
            return true;
        }

        /// <summary>1 within <see cref="nearDistance"/> of the Knight, 0 from <see cref="farDistance"/>, eased between (1 with no Knight).</summary>
        public float Proximity(Vector3 at)
        {
            var shots = EnemyProjectiles.Current;
            var knight = shots != null ? shots.Player : null;
            if (knight == null) return 1f;
            float d = SpecialAttack.Planar(knight.transform.position, at);
            float k = Mathf.Clamp01((d - nearDistance) / Mathf.Max(0.1f, farDistance - nearDistance));
            return (1f - k) * (1f - k);
        }

        private void Update() => TickHiss(Time.deltaTime, Time.unscaledDeltaTime);

        /// <summary>Cloud scan + hiss fade. <paramref name="dt"/> scaled (0 while paused: the hiss fades out), <paramref name="unscaledDt"/> for the fade. Public for tests.</summary>
        public void TickHiss(float dt, float unscaledDt)
        {
            if (dt > 0f && (_scanLeft -= dt) <= 0f)
            {
                _scanLeft = scanInterval;
                Clouds = CountClouds();
            }
            float target = dt > 0f && Clouds > 0 ? 1f : 0f;
            _hissLevel = Mathf.MoveTowards(_hissLevel, target, hissFade * unscaledDt);
            if (_hiss == null || cloudHiss == null) return;
            if (_hissLevel > 0.001f)
            {
                if (!_hiss.isPlaying) _hiss.Play();
                _hiss.volume = hissVolume * _hissLevel * GameSettings.SfxVolume;
            }
            else if (_hiss.isPlaying) _hiss.Stop();
        }

        private static int CountClouds()
        {
            var hazards = GroundHazards.Current;
            if (hazards == null) return 0;
            int n = 0;
            for (int i = 0; i < hazards.Count; i++)
                if (hazards.SpecOf(i).kind == HazardKind.Toxic) n++;
            return n;
        }

        private Layer LayerOf(Sound sound)
        {
            switch (sound)
            {
                case Sound.BloaterSwell: return bloaterSwell;
                case Sound.BloaterBurst: return bloaterBurst;
                case Sound.GhoulScreech: return ghoulScreech;
                case Sound.GhoulWhoosh: return ghoulWhoosh;
                case Sound.GhoulThud: return ghoulThud;
                case Sound.BruteGrunt: return bruteGrunt;
                default: return bruteSlam;
            }
        }

        private void SetLayer(Sound sound, Layer layer)
        {
            switch (sound)
            {
                case Sound.BloaterSwell: bloaterSwell = layer; break;
                case Sound.BloaterBurst: bloaterBurst = layer; break;
                case Sound.GhoulScreech: ghoulScreech = layer; break;
                case Sound.GhoulWhoosh: ghoulWhoosh = layer; break;
                case Sound.GhoulThud: ghoulThud = layer; break;
                case Sound.BruteGrunt: bruteGrunt = layer; break;
                default: bruteSlam = layer; break;
            }
        }
    }
}
