using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Audio
{
    /// <summary>
    /// Zombie death sounds (KAN-234, KAN-81.1). Replaces the one shared groan of <see cref="GameAudio"/> for enemy kills
    /// (the player death and the boss groans keep theirs).
    /// <list type="bullet">
    /// <item>Voice: one of 8+ death clips, never one of the last 2, pitch +-8 %, volume +-10 %. Pitch by type
    /// (<see cref="Kind"/>: runner higher, brute deeper) and by body scale (bigger = lower).</item>
    /// <item>Body fall: a low thump a short moment after the voice (later and heavier for a brute), on its own 2 voices.</item>
    /// <item>Horde limits: at most <see cref="voiceCap"/> death voices at once (own "busy until" clock, no audio-device
    /// isPlaying, KAN-66), a minimum gap, a distance fade to the Knight and a lower level off screen. When all voices are
    /// busy, a much louder (closer, on screen) death takes the quietest voice.</item>
    /// <item>Mass death: <see cref="massThreshold"/> kills inside <see cref="massWindow"/> (Soul Burst chain, a Holy Tome
    /// sweep) play ONE louder layered clip; single groans in the next <see cref="massHold"/> s fold into it.</item>
    /// <item>Stereo pan from the screen position (no 3D audio).</item>
    /// </list>
    /// Bosses are skipped (<see cref="GameAudio"/> plays their fall). Volume from <see cref="GameSettings.SfxVolume"/>.
    /// No GC per kill or frame.
    /// </summary>
    public sealed class ZombieDeathSounds : MonoBehaviour
    {
        public enum Kind { Normal, Runner, Brute }

        private const int MaxVoices = 8;
        private const int MaxFallVoices = 4;
        private const int MaxPending = 16;

        [Header("Clips (SfxBuilder.BuildZombieDeaths)")]
        [SerializeField] private AudioClip[] voices;
        [SerializeField] private AudioClip[] falls;
        [SerializeField] private AudioClip[] masses;

        [Header("Mix")]
        [Tooltip("Below the shot (0.8), above footsteps; the old shared groan was 0.7.")]
        [SerializeField, Range(0f, 1f)] private float voiceVolume = 0.6f;
        [SerializeField, Range(0f, 1f)] private float fallVolume = 0.32f;
        [SerializeField, Range(0f, 1f)] private float massVolume = 0.85f;
        [SerializeField, Range(0f, 0.3f)] private float pitchJitter = 0.08f;
        [SerializeField, Range(0f, 0.3f)] private float volumeJitter = 0.1f;

        [Header("Horde limits")]
        [SerializeField, Range(1, MaxVoices)] private int voiceCap = 4;
        [SerializeField, Range(1, MaxFallVoices)] private int fallVoices = 2;
        [Tooltip("Minimum seconds between two death voices.")]
        [SerializeField, Min(0f)] private float minInterval = 0.06f;
        [Tooltip("All voices busy: a new death takes the quietest voice when it is this many times louder.")]
        [SerializeField, Min(1f)] private float stealRatio = 1.5f;
        [Tooltip("Kills inside this many seconds count as one burst.")]
        [SerializeField, Min(0.01f)] private float massWindow = 0.15f;
        [Tooltip("Kills in one burst that turn it into one mass death sound.")]
        [SerializeField, Min(2)] private int massThreshold = 5;
        [Tooltip("Seconds after a mass death sound in which single groans fold into it (and no second mass sound starts).")]
        [SerializeField, Min(0f)] private float massHold = 0.45f;

        [Header("Type (pitch, fall)")]
        [SerializeField, Range(0.5f, 1.5f)] private float runnerPitch = 1.1f;
        [SerializeField, Range(0.5f, 1.5f)] private float brutePitch = 0.88f;
        [Tooltip("Type walk speed (m/s, before slows and boosts) from which a kill counts as a runner (Runner 2.6, Ghoul 4.2).")]
        [SerializeField, Min(0f)] private float runnerSpeed = 2.3f;
        [Tooltip("Body scale from which a kill counts as a brute (old Brute 1.25).")]
        [SerializeField, Min(0.1f)] private float bruteScale = 1.15f;
        [Tooltip("Type max health from which a kill counts as a brute (roster brutes keep scale 1: GraveBrute 520).")]
        [SerializeField, Min(1f)] private float bruteHealth = 300f;
        [SerializeField, Min(0f)] private float fallDelay = 0.16f;
        [SerializeField, Min(0f)] private float runnerFallDelay = 0.11f;
        [SerializeField, Min(0f)] private float bruteFallDelay = 0.24f;
        [SerializeField, Min(0f)] private float bruteFallBoost = 1.6f;
        [SerializeField, Range(0f, 1f)] private float runnerFallShare = 0.7f;

        [Header("Space")]
        [Tooltip("The Knight: deaths fade with distance to it. Empty = no fade.")]
        [SerializeField] private Transform listener;
        [Tooltip("Game camera: pan and the on-screen check. Empty = centre pan, no off-screen check.")]
        [SerializeField] private Camera view;
        [SerializeField, Min(0f)] private float nearDistance = 9f;
        [SerializeField, Min(0.1f)] private float farDistance = 22f;
        [SerializeField, Range(0f, 1f)] private float panWidth = 0.55f;
        [SerializeField, Range(0f, 1f)] private float offscreenGain = 0.55f;

        private struct PendingFall
        {
            public float At, Pitch, Volume, Pan;
        }

        private AudioSource[] _voices;
        private float[] _busy;   // unscaled s
        private float[] _level;  // loudness of what each voice plays (steal the quietest)
        private AudioSource[] _fallPool;
        private float[] _fallBusy;
        private readonly PendingFall[] _pending = new PendingFall[MaxPending];
        private int _pendingCount;
        private float _lastVoice = float.NegativeInfinity;
        private float _lastMass = float.NegativeInfinity;
        private float _burstStart = float.NegativeInfinity;
        private int _burstKills;
        private int _last1 = -1, _last2 = -1;
        private int _played, _dropped, _merged, _masses, _falls, _stolen;

        /// <summary>Death voices played (tests, dev log).</summary>
        public int Played => _played;
        /// <summary>Kills with no sound: gap, cap, too far.</summary>
        public int Dropped => _dropped;
        /// <summary>Kills folded into a mass death sound.</summary>
        public int Merged => _merged;
        public int Masses => _masses;
        public int Falls => _falls;
        /// <summary>Busy voices taken over by a louder death.</summary>
        public int Stolen => _stolen;
        /// <summary>Index into the voice clips of the last voice played (-1 = none yet).</summary>
        public int LastClip => _last1;
        public int VoiceCap => _voices != null ? _voices.Length : voiceCap;
        public int VoiceClips => voices != null ? voices.Length : 0;
        public int PendingFalls => _pendingCount;
        /// <summary>Last voice source used (tests: pitch, pan, volume).</summary>
        public AudioSource LastSource { get; private set; }

        /// <summary>Wires clips and space (tests; the scene step sets the serialized fields). Call before Awake (inactive object).</summary>
        public void Setup(AudioClip[] voiceClips, AudioClip[] fallClips, AudioClip[] massClips, Transform knight = null, Camera cam = null)
        {
            voices = voiceClips;
            falls = fallClips;
            masses = massClips;
            listener = knight;
            view = cam;
        }

        private void Awake()
        {
            _voices = MakePool("Zombie Death Voice", Mathf.Clamp(voiceCap, 1, MaxVoices), out _busy);
            _level = new float[_voices.Length];
            _fallPool = MakePool("Zombie Fall Voice", Mathf.Clamp(fallVoices, 1, MaxFallVoices), out _fallBusy);
        }

        private void OnEnable() => Enemy.Damaged += OnDamaged;
        private void OnDisable() => Enemy.Damaged -= OnDamaged;

        private void Update() => Tick(Time.unscaledTime);

        // Dev builds: one summary line at quit (KAN-234 check on a Mac / phone dev run).
        private void OnDestroy()
        {
            if (!Debug.isDebugBuild || Application.isEditor) return;
            if (_played + _dropped + _merged > 0)
                Debug.Log($"[ZombieDeathSounds] voices {_played} (stolen {_stolen}), falls {_falls}, mass {_masses} (merged {_merged}), dropped {_dropped}");
        }

        private void OnDamaged(Enemy enemy, float amount, bool killed)
        {
            if (!killed || enemy == null || enemy.IsBoss) return;
            Kill(enemy.transform.position, enemy.Scale, KindOf(enemy), Time.unscaledTime);
        }

        /// <summary>Type of a dying enemy from its walk speed, body scale and max health; a mini boss is a brute.</summary>
        public Kind KindOf(Enemy enemy)
        {
            float speed = enemy.MoveSpeed / Mathf.Max(0.05f, enemy.SpeedBoost * (1f - enemy.Slow));
            return Classify(enemy.Scale, speed, enemy.BaseMaxHealth, enemy.IsMiniBoss);
        }

        public Kind Classify(float scale, float typeSpeed, float typeHealth, bool miniBoss)
        {
            if (miniBoss || scale >= bruteScale || typeHealth >= bruteHealth) return Kind.Brute;
            return typeSpeed >= runnerSpeed ? Kind.Runner : Kind.Normal;
        }

        /// <summary>Base pitch of a death (before jitter): type pitch x 1/sqrt(scale), clamped 0.7..1.15.</summary>
        public float PitchOf(Kind kind, float scale)
        {
            float type = kind == Kind.Runner ? runnerPitch : kind == Kind.Brute ? brutePitch : 1f;
            return type * Mathf.Clamp(1f / Mathf.Sqrt(Mathf.Max(0.1f, scale)), 0.7f, 1.15f);
        }

        /// <summary>
        /// A kill at <paramref name="at"/> at <paramref name="now"/> (unscaled s). Returns true when it made a sound (a voice or
        /// the mass death). Public for tests.
        /// </summary>
        public bool Kill(Vector3 at, float scale, Kind kind, float now)
        {
            if (_voices == null || voices == null || voices.Length == 0) return false;
            float gain = Gain(at, out float pan);
            if (gain <= 0f) { _dropped++; return false; }

            if (now - _burstStart > massWindow) { _burstStart = now; _burstKills = 0; }
            _burstKills++;
            if (_burstKills >= massThreshold && now - _lastMass >= massHold && masses != null && masses.Length > 0)
            {
                _burstKills = 0;
                _burstStart = now;
                PlayMass(pan * 0.5f, now);
                return true;
            }
            if (now - _lastMass < massHold) { _merged++; return false; }
            if (now - _lastVoice < minInterval) { _dropped++; return false; }

            float vol = voiceVolume * gain * Random.Range(1f - volumeJitter, 1f + volumeJitter);
            int slot = FreeSlot(now);
            if (slot < 0)
            {
                slot = Quietest();
                if (vol < _level[slot] * stealRatio) { _dropped++; return false; }
                _voices[slot].Stop();
                _stolen++;
            }
            int c = PickClip();
            float pitch = PitchOf(kind, scale);
            var src = _voices[slot];
            src.clip = voices[c];
            src.pitch = pitch * (1f + Random.Range(-pitchJitter, pitchJitter));
            src.panStereo = pan;
            src.volume = Mathf.Min(1f, vol) * GameSettings.SfxVolume;
            src.Play();
            _busy[slot] = now + (src.clip != null ? src.clip.length : 0.5f) / Mathf.Max(0.1f, src.pitch);
            _level[slot] = vol;
            _lastVoice = now;
            LastSource = src;
            _played++;

            float delay = kind == Kind.Runner ? runnerFallDelay : kind == Kind.Brute ? bruteFallDelay : fallDelay;
            float share = kind == Kind.Runner ? runnerFallShare : kind == Kind.Brute ? bruteFallBoost : 1f;
            QueueFall(now + delay, pitch * 0.97f, fallVolume * gain * share, pan);
            return true;
        }

        /// <summary>Plays the body falls that are due at <paramref name="now"/>. Public for tests (Update calls it).</summary>
        public void Tick(float now)
        {
            for (int i = _pendingCount - 1; i >= 0; i--)
            {
                if (now < _pending[i].At) continue;
                PlayFall(_pending[i], now);
                _pending[i] = _pending[--_pendingCount];
            }
        }

        /// <summary>
        /// Loudness share of a death at <paramref name="at"/>: distance fade to the Knight (1 inside <see cref="nearDistance"/>,
        /// 0 from <see cref="farDistance"/>, eased) x <see cref="offscreenGain"/> off screen. <paramref name="pan"/> = stereo
        /// pan from the screen x. Public for tests.
        /// </summary>
        public float Gain(Vector3 at, out float pan)
        {
            pan = 0f;
            float gain = 1f;
            if (listener != null)
            {
                Vector3 d = at - listener.position;
                d.y = 0f;
                float k = Mathf.Clamp01((d.magnitude - nearDistance) / Mathf.Max(0.1f, farDistance - nearDistance));
                gain = (1f - k) * (1f - k);
                if (gain <= 0f) return 0f;
            }
            if (view != null)
            {
                Vector3 vp = view.WorldToViewportPoint(at);
                bool onScreen = vp.z > 0f && vp.x >= -0.05f && vp.x <= 1.05f && vp.y >= -0.05f && vp.y <= 1.05f;
                if (!onScreen) gain *= offscreenGain;
                float x = vp.z > 0f ? vp.x : 1f - vp.x; // behind the camera the viewport x is mirrored
                pan = Mathf.Clamp((x - 0.5f) * 2f, -1f, 1f) * panWidth;
            }
            return gain;
        }

        private void PlayMass(float pan, float now)
        {
            int slot = FreeSlot(now);
            if (slot < 0) { slot = Quietest(); _voices[slot].Stop(); _stolen++; }
            var src = _voices[slot];
            src.clip = masses[Random.Range(0, masses.Length)];
            src.pitch = 1f + Random.Range(-0.04f, 0.04f);
            src.panStereo = pan;
            src.volume = massVolume * GameSettings.SfxVolume;
            src.Play();
            _busy[slot] = now + (src.clip != null ? src.clip.length : 1f) / src.pitch;
            _level[slot] = massVolume;
            _lastMass = now;
            _lastVoice = now;
            LastSource = src;
            _masses++;
            _pendingCount = 0; // the mass clip has its own falls
        }

        private void QueueFall(float at, float pitch, float volume, float pan)
        {
            if (falls == null || falls.Length == 0 || _pendingCount >= MaxPending) return;
            _pending[_pendingCount++] = new PendingFall { At = at, Pitch = pitch, Volume = volume, Pan = pan };
        }

        private void PlayFall(PendingFall fall, float now)
        {
            int slot = -1;
            for (int i = 0; i < _fallPool.Length; i++)
                if (now >= _fallBusy[i]) { slot = i; break; }
            if (slot < 0) return; // a horde: the voices carry it
            var src = _fallPool[slot];
            src.clip = falls[Random.Range(0, falls.Length)];
            src.pitch = fall.Pitch * (1f + Random.Range(-0.05f, 0.05f));
            src.panStereo = fall.Pan;
            src.volume = Mathf.Min(1f, fall.Volume) * GameSettings.SfxVolume;
            src.Play();
            _fallBusy[slot] = now + (src.clip != null ? src.clip.length : 0.4f) / Mathf.Max(0.1f, src.pitch);
            _falls++;
        }

        private int FreeSlot(float now)
        {
            for (int i = 0; i < _voices.Length; i++)
                if (now >= _busy[i]) return i;
            return -1;
        }

        private int Quietest()
        {
            int q = 0;
            for (int i = 1; i < _level.Length; i++)
                if (_level[i] < _level[q]) q = i;
            return q;
        }

        /// <summary>Random clip that is not one of the last two (fewer clips: as many as can be avoided).</summary>
        private int PickClip()
        {
            int n = voices.Length;
            int a = n > 1 ? _last1 : -1;
            int b = n > 2 ? _last2 : -1;
            if (b == a) b = -1;
            int skip = (a >= 0 ? 1 : 0) + (b >= 0 ? 1 : 0);
            int r = Random.Range(0, n - skip);
            int lo = a >= 0 && b >= 0 ? Mathf.Min(a, b) : Mathf.Max(a, b);
            int hi = a >= 0 && b >= 0 ? Mathf.Max(a, b) : -1;
            if (lo >= 0 && r >= lo) r++;
            if (hi >= 0 && r >= hi) r++;
            _last2 = _last1;
            _last1 = r;
            return r;
        }

        private AudioSource[] MakePool(string label, int count, out float[] busy)
        {
            var pool = new AudioSource[count];
            busy = new float[count];
            for (int i = 0; i < count; i++)
            {
                busy[i] = float.NegativeInfinity;
                var go = new GameObject($"{label} {i}");
                go.transform.SetParent(transform, false);
                var src = go.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.spatialBlend = 0f;
                pool[i] = src;
            }
            return pool;
        }
    }
}
