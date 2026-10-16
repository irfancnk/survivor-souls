using System;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Audio
{
    /// <summary>
    /// Hexbound Shaman sounds (KAN-138): every cast is heard at its wind-up and at its release.
    /// <list type="bullet">
    /// <item>Wind-up (<see cref="HexShaman.CastStarted"/>): a low, rising hum. It stops at once when the cast is cut off
    /// (<see cref="HexShaman.Interrupted"/>: blink, kill, dead Knight), so a cancelled cast is silent.</item>
    /// <item>Orb ring (<see cref="HexShaman.OrbsFired"/>): a soft airy whoosh.</item>
    /// <item>Hex (<see cref="HexShaman.Hexed"/>, at least one zombie hexed): a glassy chime.</item>
    /// <item>Raise (<see cref="HexShaman.Raised"/>, at least one Laborer up): an earthy ground crack.</item>
    /// <item>Blink (<see cref="HexShaman.BlinkStarted"/> and <see cref="HexShaman.Blinked"/>): a small pop where it vanishes
    /// and again where it comes back.</item>
    /// </list>
    /// Same voice model as <see cref="SpecialMeleeSounds"/>: each sound has a small voice pool with an own "busy until" clock
    /// (no audio-device isPlaying, KAN-66) and a rate limit, so a group of Shamans gives a few voices, not noise. 2D with a
    /// distance fade to the Knight; the Shaman holds 8-11 m away, so the full-volume radius is wider than the melee one.
    /// Volume from <see cref="GameSettings.SfxVolume"/>. No GC per cast or frame.
    /// </summary>
    public sealed class ShamanSounds : MonoBehaviour
    {
        /// <summary>Sound ids (index into the layers).</summary>
        public enum Sound { Hum, Whoosh, Chime, Crack, Pop }

        private const int SoundCount = 5;
        private const int MaxVoices = 6;

        [Serializable]
        public struct Layer
        {
            public AudioClip[] clips;
            [Range(0f, 1f)] public float volume;
            [Range(0f, 0.3f)] public float pitchJitter;
            [Range(1, MaxVoices)] public int voices;
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

        [SerializeField] private Layer hum = new Layer(0.34f, 0.05f, 2, 0.25f);
        [SerializeField] private Layer whoosh = new Layer(0.36f, 0.08f, 2, 0.12f);
        [SerializeField] private Layer chime = new Layer(0.32f, 0.04f, 2, 0.2f);
        [SerializeField] private Layer crack = new Layer(0.5f, 0.06f, 2, 0.2f);
        [SerializeField] private Layer pop = new Layer(0.3f, 0.1f, 3, 0.06f);

        [Header("Distance to the Knight")]
        [SerializeField, Min(0f)] private float nearDistance = 11f;
        [SerializeField, Min(0.1f)] private float farDistance = 24f;

        private AudioSource[][] _pools;
        private float[][] _busy; // own voice clock (unscaled s): does not depend on the audio device
        private HexShaman[] _humOwner; // which Shaman each hum voice belongs to (stop on an interrupt)
        private readonly float[] _last = new float[SoundCount];
        private readonly int[] _lastClip = new int[SoundCount];
        private readonly int[] _played = new int[SoundCount];
        private readonly int[] _dropped = new int[SoundCount];
        private int _humsCut;

        /// <summary>Plays of <paramref name="sound"/> (tests, dev log).</summary>
        public int Played(Sound sound) => _played[(int)sound];
        /// <summary>Plays of <paramref name="sound"/> dropped by the rate limit, the voice cap or the distance (tests).</summary>
        public int Dropped(Sound sound) => _dropped[(int)sound];
        /// <summary>Voice sources of <paramref name="sound"/> (tests).</summary>
        public int Voices(Sound sound) => _pools != null ? _pools[(int)sound].Length : 0;
        /// <summary>Hums stopped early by an interrupted cast (tests).</summary>
        public int HumsCut => _humsCut;
        /// <summary>A hum voice of <paramref name="shaman"/> is busy at <paramref name="now"/> (tests).</summary>
        public bool Humming(HexShaman shaman, float now)
        {
            if (_pools == null) return false;
            var busy = _busy[(int)Sound.Hum];
            for (int i = 0; i < busy.Length; i++)
                if (_humOwner[i] == shaman && now < busy[i]) return true;
            return false;
        }

        /// <summary>Wires the clips (tests; the scene builder sets the serialized fields). Index = <see cref="Sound"/>.</summary>
        public void Setup(AudioClip[][] clips)
        {
            for (int i = 0; i < SoundCount && i < clips.Length; i++)
            {
                var layer = LayerOf((Sound)i);
                layer.clips = clips[i];
                SetLayer((Sound)i, layer);
            }
        }

        private void Awake()
        {
            _pools = new AudioSource[SoundCount][];
            _busy = new float[SoundCount][];
            for (int s = 0; s < SoundCount; s++)
            {
                var layer = LayerOf((Sound)s);
                int n = Mathf.Clamp(layer.voices, 1, MaxVoices);
                _pools[s] = new AudioSource[n];
                _busy[s] = new float[n];
                for (int i = 0; i < n; i++)
                {
                    _busy[s][i] = float.NegativeInfinity;
                    var go = new GameObject($"Shaman {(Sound)s} Voice {i}");
                    go.transform.SetParent(transform, false);
                    var src = go.AddComponent<AudioSource>();
                    src.playOnAwake = false;
                    src.spatialBlend = 0f;
                    _pools[s][i] = src;
                }
                _last[s] = float.NegativeInfinity;
                _lastClip[s] = -1;
            }
            _humOwner = new HexShaman[_pools[(int)Sound.Hum].Length];
        }

        private void OnEnable()
        {
            HexShaman.CastStarted += OnCastStarted;
            HexShaman.Interrupted += OnInterrupted;
            HexShaman.OrbsFired += OnOrbs;
            HexShaman.Hexed += OnHexed;
            HexShaman.Raised += OnRaised;
            HexShaman.BlinkStarted += OnBlinkStarted;
            HexShaman.Blinked += OnBlinked;
        }

        private void OnDisable()
        {
            HexShaman.CastStarted -= OnCastStarted;
            HexShaman.Interrupted -= OnInterrupted;
            HexShaman.OrbsFired -= OnOrbs;
            HexShaman.Hexed -= OnHexed;
            HexShaman.Raised -= OnRaised;
            HexShaman.BlinkStarted -= OnBlinkStarted;
            HexShaman.Blinked -= OnBlinked;
        }

        // Dev builds: one summary line at quit, so a Mac / phone dev run shows the sounds fired (KAN-138 check).
        private void OnDestroy()
        {
            if (!Debug.isDebugBuild || Application.isEditor) return;
            int total = 0;
            for (int i = 0; i < SoundCount; i++) total += _played[i];
            if (total > 0)
                Debug.Log($"[ShamanSounds] played hum {_played[0]} (cut {_humsCut}), whoosh {_played[1]}, chime {_played[2]}, crack {_played[3]}, pop {_played[4]}");
        }

        private void OnCastStarted(HexShaman shaman, HexShaman.Cast cast) => Play(Sound.Hum, shaman.transform.position, Time.unscaledTime, shaman);
        private void OnInterrupted(HexShaman shaman) => StopHum(shaman, Time.unscaledTime);
        private void OnOrbs(HexShaman shaman, int fired)
        {
            if (fired > 0) Play(Sound.Whoosh, shaman.transform.position, Time.unscaledTime);
        }
        private void OnHexed(HexShaman shaman, int hexed)
        {
            if (hexed > 0) Play(Sound.Chime, shaman.transform.position, Time.unscaledTime);
        }
        private void OnRaised(HexShaman shaman, int raised)
        {
            if (raised > 0) Play(Sound.Crack, shaman.transform.position, Time.unscaledTime);
        }
        private void OnBlinkStarted(HexShaman shaman, Vector3 from) => Play(Sound.Pop, from, Time.unscaledTime);
        private void OnBlinked(HexShaman shaman, Vector3 from, Vector3 to) => Play(Sound.Pop, to, Time.unscaledTime);

        /// <summary>
        /// Plays <paramref name="sound"/> for a Shaman at <paramref name="at"/> at <paramref name="now"/> (unscaled seconds).
        /// Returns false when limited (rate, voices) or too far from the Knight. <paramref name="owner"/> tags a hum voice so
        /// <see cref="StopHum"/> can cut it. Public for tests.
        /// </summary>
        public bool Play(Sound sound, Vector3 at, float now, HexShaman owner = null)
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
            if (sound == Sound.Hum) _humOwner[slot] = owner;
            _played[s]++;
            return true;
        }

        /// <summary>Stops the hum voice of <paramref name="shaman"/> (cast cut off). Public for tests.</summary>
        public void StopHum(HexShaman shaman, float now)
        {
            if (_pools == null || shaman == null) return;
            var pool = _pools[(int)Sound.Hum];
            var busy = _busy[(int)Sound.Hum];
            for (int i = 0; i < pool.Length; i++)
            {
                if (_humOwner[i] != shaman || now >= busy[i]) continue;
                pool[i].Stop();
                busy[i] = now;
                _humOwner[i] = null;
                _humsCut++;
            }
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

        private Layer LayerOf(Sound sound)
        {
            switch (sound)
            {
                case Sound.Hum: return hum;
                case Sound.Whoosh: return whoosh;
                case Sound.Chime: return chime;
                case Sound.Crack: return crack;
                default: return pop;
            }
        }

        private void SetLayer(Sound sound, Layer layer)
        {
            switch (sound)
            {
                case Sound.Hum: hum = layer; break;
                case Sound.Whoosh: whoosh = layer; break;
                case Sound.Chime: chime = layer; break;
                case Sound.Crack: crack = layer; break;
                default: pop = layer; break;
            }
        }
    }
}
