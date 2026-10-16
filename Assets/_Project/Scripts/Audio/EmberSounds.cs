using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.Gameplay.Skills.Effects;
using UnityEngine;

namespace SurvivorSouls.Audio
{
    /// <summary>
    /// Ember Bolts sounds (KAN-201, part 3 of KAN-134), three layers on their own small voice pools:
    /// <list type="bullet">
    /// <item>Ignite: a short gas "whoomp" with a sizzle tail when a bolt sets a foe on fire
    /// (<see cref="BurnOnHitEffect.Ignited"/>). Rate limited (<see cref="igniteInterval"/>) and voice capped
    /// (<see cref="igniteVoices"/>, own "busy until" clock): a horde catching fire gives a few whoomps, not noise.
    /// Hotter burns (higher Ember level) play a little lower and louder.</item>
    /// <item>Burning: a quiet fire crackle loop per burning foe, at most <see cref="crackleVoices"/> loops (the 2nd and 3rd
    /// are quieter and pitched apart). The burning count is scanned every <see cref="scanInterval"/> s; loops fade in / out,
    /// and fade out while the game is paused (level-up screen).</item>
    /// <item>Hot shot: a short fire "fwoosh" on top of the crossbow shot (<see cref="AutoShooter.Fired"/>) while Ember Bolts
    /// is owned (<see cref="StatId.BurnDps"/> &gt; 0), louder at higher levels.</item>
    /// </list>
    /// All 2D, volume from <see cref="GameSettings.SfxVolume"/>. No GC per hit, shot or frame.
    /// </summary>
    public sealed class EmberSounds : MonoBehaviour
    {
        [SerializeField] private AutoShooter shooter;
        [SerializeField] private PlayerStats stats;

        [Header("Ignite")]
        [SerializeField] private AudioClip[] ignites;
        [SerializeField, Range(0f, 1f)] private float igniteVolume = 0.5f;
        [SerializeField, Range(0f, 0.3f)] private float pitchJitter = 0.07f;
        [SerializeField, Range(1, 6)] private int igniteVoices = 3;
        [SerializeField, Min(0f)] private float igniteInterval = 0.08f;

        [Header("Burning crackle")]
        [Tooltip("Seamless crackle loops; voice k uses clip k % length.")]
        [SerializeField] private AudioClip[] crackles;
        [SerializeField, Range(0f, 1f)] private float crackleVolume = 0.16f;
        [Tooltip("Volume share lost per extra loop (the 2nd loop is quieter than the 1st).")]
        [SerializeField, Range(0f, 0.5f)] private float crackleStep = 0.25f;
        [SerializeField, Range(1, 4)] private int crackleVoices = 3;
        [Tooltip("Seconds between two counts of the burning foes.")]
        [SerializeField, Min(0.02f)] private float scanInterval = 0.2f;
        [Tooltip("Crackle volume change per second (fade in / out).")]
        [SerializeField, Min(0.01f)] private float crackleFade = 0.6f;

        [Header("Hot shot")]
        [SerializeField] private AudioClip[] hotShots;
        [SerializeField, Range(0f, 1f)] private float hotShotVolume = 0.3f;
        [Tooltip("Burn damage per second that sounds coolest (level 1) and hottest (level 4), like BurnOnHitEffect.heatDps.")]
        [SerializeField] private Vector2 heatDps = new Vector2(25f, 75f);
        [SerializeField, Range(1, 4)] private int hotShotVoices = 2;
        [SerializeField, Min(0f)] private float hotShotInterval = 0.05f;

        private static readonly float[] CracklePitch = { 1f, 0.9f, 1.12f, 0.82f };

        private AudioSource[] _ignitePool, _crackle, _hotPool;
        private float[] _igniteBusy, _hotBusy; // own voice clock (unscaled s): does not depend on the audio device
        private float[] _crackleLevel; // 0..1 per loop, before the master volume
        private bool[] _crackleOn;
        private float _lastIgnite = float.NegativeInfinity, _lastHot = float.NegativeInfinity;
        private int _lastIgniteClip = -1, _lastHotClip = -1;
        private float _scanLeft;

        /// <summary>Ignites that played a sound (tests).</summary>
        public int IgnitesPlayed { get; private set; }
        /// <summary>Ignites dropped by the rate limit or the voice cap (tests).</summary>
        public int IgnitesDropped { get; private set; }
        /// <summary>Hot shot layers played (tests).</summary>
        public int HotShotsPlayed { get; private set; }
        /// <summary>Burning foes at the last scan (tests).</summary>
        public int Burning { get; private set; }
        /// <summary>Crackle loops playing now (tests).</summary>
        public int CrackleLoops
        {
            get
            {
                int n = 0;
                if (_crackleOn != null)
                    for (int i = 0; i < _crackleOn.Length; i++) if (_crackleOn[i]) n++;
                return n;
            }
        }
        /// <summary>Ignite voice sources (tests).</summary>
        public int IgniteVoices => _ignitePool?.Length ?? 0;
        /// <summary>Crackle loop sources (tests).</summary>
        public int CrackleVoices => _crackle?.Length ?? 0;

        /// <summary>Wires refs and clips (tests; the scene builder sets the serialized fields).</summary>
        public void Setup(AutoShooter playerShooter, PlayerStats playerStats, AudioClip[] igniteClips, AudioClip[] crackleClips,
            AudioClip[] hotShotClips)
        {
            Unhook();
            shooter = playerShooter;
            stats = playerStats;
            ignites = igniteClips;
            crackles = crackleClips;
            hotShots = hotShotClips;
            if (isActiveAndEnabled) Hook();
            for (int i = 0; i < _crackle.Length; i++)
                _crackle[i].clip = crackles != null && crackles.Length > 0 ? crackles[i % crackles.Length] : null;
        }

        private void Awake()
        {
            _ignitePool = NewPool("Ember Ignite Voice", igniteVoices, out _igniteBusy);
            _hotPool = NewPool("Ember Hot Shot Voice", hotShotVoices, out _hotBusy);
            _crackle = NewPool("Ember Crackle Loop", crackleVoices, out _);
            _crackleLevel = new float[crackleVoices];
            _crackleOn = new bool[crackleVoices];
            for (int i = 0; i < _crackle.Length; i++)
            {
                _crackle[i].loop = true;
                _crackle[i].pitch = CracklePitch[i % CracklePitch.Length];
                _crackle[i].clip = crackles != null && crackles.Length > 0 ? crackles[i % crackles.Length] : null;
            }
        }

        private AudioSource[] NewPool(string label, int count, out float[] busy)
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

        private void OnEnable() => Hook();
        private void OnDisable()
        {
            Unhook();
            for (int i = 0; i < CrackleVoices; i++) StopLoop(i);
        }

        private void Hook()
        {
            BurnOnHitEffect.Ignited += OnIgnited;
            if (shooter != null) shooter.Fired += OnFired;
        }

        private void Unhook()
        {
            BurnOnHitEffect.Ignited -= OnIgnited;
            if (shooter != null) shooter.Fired -= OnFired;
        }

        private void OnIgnited(Enemy enemy) => PlayIgnite(Heat(enemy != null ? enemy.BurnDps : 0f), Time.unscaledTime);

        private void OnFired(Vector3 muzzle, Vector3 direction) => PlayHotShot(Time.unscaledTime);

        /// <summary>0 at the coolest burn (<see cref="heatDps"/>.x), 1 at the hottest (.y).</summary>
        public float Heat(float dps) => Mathf.InverseLerp(heatDps.x, heatDps.y, dps);

        /// <summary>One ignite whoomp at <paramref name="now"/> (unscaled s). Returns false when limited. Public for tests.</summary>
        public bool PlayIgnite(float heat, float now)
        {
            if (ignites == null || ignites.Length == 0 || _ignitePool == null) return false;
            if (now - _lastIgnite < igniteInterval) { IgnitesDropped++; return false; }
            int slot = FreeVoice(_igniteBusy, now);
            if (slot < 0) { IgnitesDropped++; return false; }
            _lastIgnite = now;
            var src = _ignitePool[slot];
            var clip = Pick(ignites, ref _lastIgniteClip);
            src.clip = clip;
            src.pitch = Mathf.Lerp(1.06f, 0.94f, heat) + Random.Range(-pitchJitter, pitchJitter);
            src.volume = igniteVolume * Mathf.Lerp(0.85f, 1f, heat) * Random.Range(0.9f, 1f) * GameSettings.SfxVolume;
            src.Play();
            _igniteBusy[slot] = now + clip.length / Mathf.Max(0.1f, src.pitch);
            IgnitesPlayed++;
            return true;
        }

        /// <summary>The hot shot layer at <paramref name="now"/> when Ember Bolts is owned. Returns false otherwise. Public for tests.</summary>
        public bool PlayHotShot(float now)
        {
            if (hotShots == null || hotShots.Length == 0 || _hotPool == null) return false;
            float dps = PlayerStats.Eval(stats, StatId.BurnDps, 0f);
            if (dps <= 0f) return false;
            if (now - _lastHot < hotShotInterval) return false;
            int slot = FreeVoice(_hotBusy, now);
            if (slot < 0) return false;
            _lastHot = now;
            float heat = Heat(dps);
            var src = _hotPool[slot];
            var clip = Pick(hotShots, ref _lastHotClip);
            src.clip = clip;
            src.pitch = Mathf.Lerp(1.04f, 0.95f, heat) + Random.Range(-0.04f, 0.04f);
            src.volume = hotShotVolume * Mathf.Lerp(0.7f, 1f, heat) * GameSettings.SfxVolume;
            src.Play();
            _hotBusy[slot] = now + clip.length / Mathf.Max(0.1f, src.pitch);
            HotShotsPlayed++;
            return true;
        }

        private static int FreeVoice(float[] busy, float now)
        {
            for (int i = 0; i < busy.Length; i++)
                if (now >= busy[i]) return i;
            return -1;
        }

        private static AudioClip Pick(AudioClip[] clips, ref int last)
        {
            int c = Random.Range(0, clips.Length);
            if (clips.Length > 1 && c == last) c = (c + 1) % clips.Length;
            last = c;
            return clips[c];
        }

        private void Update()
        {
            _scanLeft -= Time.unscaledDeltaTime;
            if (_scanLeft <= 0f)
            {
                _scanLeft = scanInterval;
                Burning = CountBurning();
            }
            Tick(Burning, Time.timeScale <= 0f, Time.unscaledDeltaTime);
        }

        private static int CountBurning()
        {
            int n = 0;
            var enemies = Enemy.Active;
            for (int i = 0; i < enemies.Count; i++)
                if (enemies[i].IsBurning) n++;
            return n;
        }

        /// <summary>
        /// Fades the crackle loops toward <paramref name="burning"/> foes (loop k plays while more than k foes burn), all
        /// down while <paramref name="paused"/>. Public for tests (Update calls it with the scanned count).
        /// </summary>
        public void Tick(int burning, bool paused, float dt)
        {
            if (_crackle == null) return;
            float master = crackleVolume * GameSettings.SfxVolume;
            for (int i = 0; i < _crackle.Length; i++)
            {
                float target = !paused && burning > i ? 1f - crackleStep * i : 0f;
                _crackleLevel[i] = Mathf.MoveTowards(_crackleLevel[i], target, crackleFade * dt);
                if (_crackleLevel[i] > 0f)
                {
                    if (!_crackleOn[i] && _crackle[i].clip != null)
                    {
                        _crackleOn[i] = true;
                        // Start each loop at its own place, so two loops of one clip never phase.
                        _crackle[i].time = _crackle[i].clip.length * ((i * 0.37f + Random.value * 0.2f) % 1f);
                        _crackle[i].Play();
                    }
                    _crackle[i].volume = _crackleLevel[i] * master;
                }
                else if (_crackleOn[i]) StopLoop(i);
            }
        }

        private void StopLoop(int i)
        {
            _crackleOn[i] = false;
            _crackleLevel[i] = 0f;
            _crackle[i].Stop();
        }
    }
}
