using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Audio
{
    /// <summary>
    /// The Knight's shot channel (KAN-236, KAN-81.3). Takes the player's shots over from <see cref="GameAudio"/> (boss volleys
    /// keep theirs).
    /// <list type="bullet">
    /// <item>Variants: one of 8 crossbow takes (<c>SfxBuilder.BuildShotChannel</c>), never one of the last 2 (a hero set with
    /// fewer than 5 clips, the Ranger's 3 bow twangs: never the last 1). Pitch +-4 %, volume +-8 %.</item>
    /// <item>Attack speed: the pitch rises a little with the shooter's shots per second over its base rate
    /// (<see cref="risePerDouble"/> per +100 %), capped at <see cref="maxRise"/>.</item>
    /// <item>Limits: at most <see cref="voiceCap"/> shot voices (own "busy until" clock, KAN-66) and a minimum gap
    /// (<see cref="minGap"/>): a shot inside the gap is dropped; with all voices busy the oldest voice is cut (the newest
    /// shot is the one the player just saw).</item>
    /// <item>One sound per volley: <see cref="AutoShooter.Fired"/> is once per volley; a Spread Shot volley adds a quiet volley
    /// layer (staggered thrums + whooshes) on the SAME voice. Double Shot follow-ups are a quiet, higher layer on the channel
    /// (gap and cap apply).</item>
    /// </list>
    /// Volume from <see cref="GameSettings.SfxVolume"/>. No GC per shot.
    /// </summary>
    public sealed class ShotSounds : MonoBehaviour
    {
        private const int MaxVoices = 6;

        [Header("Clips (SfxBuilder.BuildShotChannel)")]
        [SerializeField] private AudioClip[] variants;
        [SerializeField] private AudioClip[] volleyLayers;
        [Tooltip("Reads the attack speed (pitch rise) and the volley (Spread Shot layer).")]
        [SerializeField] private AutoShooter shooter;

        [Header("Mix")]
        [Tooltip("Same level as the old GameAudio shot (0.8).")]
        [SerializeField, Range(0f, 1f)] private float shotVolume = 0.8f;
        [SerializeField, Range(0f, 0.2f)] private float pitchJitter = 0.04f;
        [SerializeField, Range(0f, 0.3f)] private float volumeJitter = 0.08f;
        [Tooltip("Double Shot follow-up (KAN-83): share of the shot volume, pitched up a little.")]
        [SerializeField, Range(0f, 1f)] private float followUpShare = 0.35f;
        [SerializeField, Range(0.5f, 2f)] private float followUpPitch = 1.1f;
        [Tooltip("Spread Shot volley layer: share of the shot volume with one side bolt pair, more per extra pair.")]
        [SerializeField, Range(0f, 1f)] private float volleyLayerShare = 0.32f;
        [SerializeField, Range(0f, 0.5f)] private float volleyLayerPerPair = 0.06f;
        [SerializeField, Range(0f, 1f)] private float volleyLayerMax = 0.5f;

        [Header("Attack speed")]
        [Tooltip("Pitch added per +100 % shots per second over the shooter's base rate.")]
        [SerializeField, Range(0f, 0.2f)] private float risePerDouble = 0.03f;
        [SerializeField, Range(0f, 0.2f)] private float maxRise = 0.06f;

        [Header("Limits")]
        [SerializeField, Range(1, MaxVoices)] private int voiceCap = 3;
        [Tooltip("Minimum seconds between two shot sounds.")]
        [SerializeField, Min(0f)] private float minGap = 0.04f;

        private AudioSource[] _voices;
        private float[] _busy;  // unscaled s
        private float[] _start; // unscaled s, oldest = cut first
        private AudioClip[] _hero;
        private float _last = float.NegativeInfinity;
        private int _last1 = -1, _last2 = -1;
        private int _played, _followUps, _dropped, _stolen, _layers;

        /// <summary>Shot sounds played (main shots + follow-ups).</summary>
        public int Played => _played;
        public int FollowUps => _followUps;
        /// <summary>Shots with no sound (inside the gap).</summary>
        public int Dropped => _dropped;
        /// <summary>Busy voices cut by a newer shot.</summary>
        public int Stolen => _stolen;
        /// <summary>Spread Shot volley layers played.</summary>
        public int Layers => _layers;
        /// <summary>Index into the current clip set of the last shot (-1 = none yet).</summary>
        public int LastClip => _last1;
        public int VoiceCap => _voices != null ? _voices.Length : voiceCap;
        public float MinGap => minGap;
        public float MaxRise => maxRise;
        /// <summary>The clips shots pick from: the hero's own (KAN-227) or the crossbow variants.</summary>
        public AudioClip[] Clips => _hero != null && _hero.Length > 0 ? _hero : variants;
        /// <summary>Last voice used (tests: pitch, volume).</summary>
        public AudioSource LastSource { get; private set; }

        /// <summary>Voices still sounding at <paramref name="now"/> (sound board, tests).</summary>
        public int ActiveVoices(float now)
        {
            int n = 0;
            if (_busy != null)
                for (int i = 0; i < _busy.Length; i++)
                    if (_busy[i] > now) n++;
            return n;
        }

        /// <summary>Wires clips and shooter (tests; the scene step sets the serialized fields). Call before Awake (inactive object).</summary>
        public void Setup(AudioClip[] shotClips, AudioClip[] layerClips, AutoShooter source)
        {
            variants = shotClips;
            volleyLayers = layerClips;
            shooter = source;
        }

        /// <summary>Hero shot clips (KAN-227, from <see cref="GameAudio.SetPlayerShots"/>); null / empty = the crossbow variants.</summary>
        public void SetHeroClips(AudioClip[] clips)
        {
            if (clips == _hero) return;
            _hero = clips;
            _last1 = _last2 = -1;
        }

        private void Awake()
        {
            int n = Mathf.Clamp(voiceCap, 1, MaxVoices);
            _voices = new AudioSource[n];
            _busy = new float[n];
            _start = new float[n];
            for (int i = 0; i < n; i++)
            {
                _busy[i] = _start[i] = float.NegativeInfinity;
                var go = new GameObject($"Shot Voice {i}");
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                _voices[i] = src;
            }
        }

        private void OnEnable()
        {
            if (shooter == null) return;
            shooter.Fired += OnFired;
            shooter.FollowUpFired += OnFollowUpFired;
        }

        private void OnDisable()
        {
            if (shooter == null) return;
            shooter.Fired -= OnFired;
            shooter.FollowUpFired -= OnFollowUpFired;
        }

        // Dev builds: one summary line at quit (KAN-236 check on a Mac / phone dev run).
        private void OnDestroy()
        {
            if (!Debug.isDebugBuild || Application.isEditor) return;
            if (_played + _dropped > 0)
                Debug.Log($"[ShotSounds] shots {_played} (follow-ups {_followUps}, volley layers {_layers}), dropped {_dropped}, stolen {_stolen}");
        }

        private void OnFired(Vector3 muzzle, Vector3 direction) =>
            Shot(Time.unscaledTime, false, shooter != null ? shooter.CurrentVolleySpec.Spread : 0);

        private void OnFollowUpFired(Vector3 muzzle, Vector3 direction) => Shot(Time.unscaledTime, true, 0);

        /// <summary>Pitch factor from the shooter's attack speed: 1 at the base rate, + <see cref="risePerDouble"/> per +100 %, capped.</summary>
        public float AttackSpeedPitch()
        {
            if (shooter == null) return 1f;
            float rate = 1f / Mathf.Max(0.01f, shooter.CurrentInterval);
            float extra = rate / Mathf.Max(0.01f, shooter.BaseShotsPerSecond) - 1f;
            return 1f + Mathf.Clamp(extra * risePerDouble, 0f, maxRise);
        }

        /// <summary>
        /// One shot sound at <paramref name="now"/> (unscaled s). <paramref name="spread"/> = side bolts of the volley (a
        /// volley layer when > 0). Returns true when it made a sound. Public for tests (the shooter events call it).
        /// </summary>
        public bool Shot(float now, bool followUp, int spread)
        {
            var clips = Clips;
            if (_voices == null || clips == null || clips.Length == 0) return false;
            if (now - _last < minGap) { _dropped++; return false; }

            int slot = FreeSlot(now);
            if (slot < 0)
            {
                slot = Oldest();
                _voices[slot].Stop();
                _stolen++;
            }
            int c = PickClip(clips.Length);
            float vol = shotVolume * (followUp ? followUpShare : 1f) * Random.Range(1f - volumeJitter, 1f + volumeJitter);
            float pitch = AttackSpeedPitch() * (followUp ? followUpPitch : 1f) * (1f + Random.Range(-pitchJitter, pitchJitter));
            var src = _voices[slot];
            src.clip = clips[c];
            src.pitch = pitch;
            src.volume = Mathf.Min(1f, vol) * GameSettings.SfxVolume;
            src.Play();
            float length = src.clip != null ? src.clip.length : 0.5f;

            if (!followUp && spread > 0 && volleyLayers != null && volleyLayers.Length > 0 && vol > 0f)
            {
                var layer = volleyLayers[(_played + spread) % volleyLayers.Length];
                float share = Mathf.Min(volleyLayerMax, volleyLayerShare + volleyLayerPerPair * (spread / 2 - 1));
                if (layer != null)
                {
                    src.PlayOneShot(layer, share * shotVolume / vol); // PlayOneShot scales by src.volume: net = share x shot volume
                    length = Mathf.Max(length, layer.length);
                    _layers++;
                }
            }

            _busy[slot] = now + length / Mathf.Max(0.1f, pitch);
            _start[slot] = now;
            _last = now;
            LastSource = src;
            _played++;
            if (followUp) _followUps++;
            return true;
        }

        private int FreeSlot(float now)
        {
            for (int i = 0; i < _busy.Length; i++)
                if (_busy[i] <= now) return i;
            return -1;
        }

        private int Oldest()
        {
            int best = 0;
            for (int i = 1; i < _start.Length; i++)
                if (_start[i] < _start[best]) best = i;
            return best;
        }

        /// <summary>A clip index that is not one of the last 2 (sets of 5+) or not the last 1 (smaller sets).</summary>
        private int PickClip(int count)
        {
            int c = Random.Range(0, count);
            if (count >= 5)
            {
                for (int guard = 0; guard < 4 && (c == _last1 || c == _last2); guard++) c = (c + 1) % count;
            }
            else if (count > 1 && c == _last1) c = (c + 1) % count;
            _last2 = _last1;
            _last1 = c;
            return c;
        }
    }
}
