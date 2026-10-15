using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Boss;
using UnityEngine;

namespace SurvivorSouls.Audio
{
    /// <summary>
    /// Boss charge sounds (KAN-168, part of KAN-160). The warning starts a low growl loop and a heavy foot-scrape; the
    /// growl rises in pitch and volume with the lane fill (<see cref="BossFight.WindUpProgress"/>) and stops when the dash
    /// starts; the dash plays a whoosh + armour clank + grunt; the end plays a dull thud and skid, or a stone crash with
    /// debris when the dash slams into a pillar or the wall (<see cref="BossFight.DashBlocked"/>).
    /// Two voices per boss, never more: one looping growl source and one one-shot source. A re-dash restarts the same
    /// growl (no stacking). Both sources sit on the boss with a part 3D blend and a wide linear roll-off, so the sound
    /// leans to the boss's side but stays at full level anywhere on screen. Volumes follow <see cref="GameSettings.SfxVolume"/>.
    /// The growl pauses while the game is paused (time scale 0), so it never drones under the level-up cards.
    /// </summary>
    public sealed class ChargeSounds : MonoBehaviour
    {
        [SerializeField] private BossFight boss;

        [Header("Clips (SfxBuilder)")]
        [SerializeField] private AudioClip rumble;
        [SerializeField] private AudioClip[] scrapes;
        [SerializeField] private AudioClip[] dashes;
        [SerializeField] private AudioClip[] stops;
        [SerializeField] private AudioClip[] crashes;

        [Header("Mix")]
        [SerializeField, Range(0f, 1f)] private float rumbleVolume = 0.7f;
        [Tooltip("Growl volume at the start of the warning, as a share of the full volume (it swells to 1 at the dash).")]
        [SerializeField, Range(0f, 1f)] private float rumbleStartShare = 0.35f;
        [SerializeField, Range(0.3f, 2f)] private float rumblePitchStart = 0.7f;
        [SerializeField, Range(0.3f, 3f)] private float rumblePitchEnd = 1.45f;
        [SerializeField, Range(0f, 1f)] private float scrapeVolume = 0.65f;
        [SerializeField, Range(0f, 1f)] private float dashVolume = 0.9f;
        [SerializeField, Range(0f, 1f)] private float stopVolume = 0.85f;
        [SerializeField, Range(0f, 1f)] private float crashVolume = 1f;
        [SerializeField, Range(0f, 0.3f)] private float pitchJitter = 0.04f;
        [Tooltip("0 = flat 2D, 1 = full 3D. A part blend only leans the sound towards the boss.")]
        [SerializeField, Range(0f, 1f)] private float spatialBlend = 0.4f;
        [Tooltip("No roll-off inside this many metres (the whole visible arena).")]
        [SerializeField, Min(1f)] private float fullVolumeDistance = 30f;

        private AudioSource _rumble, _voice;
        private bool _paused;
        private int _lastClip = -1;

        /// <summary>The growl loop is on (a charge is winding up).</summary>
        public bool Rumbling { get; private set; }
        /// <summary>The growl's looping source (tests read its pitch and volume).</summary>
        public AudioSource Rumble => _rumble;
        /// <summary>The one-shot source (scrape, dash, stop, crash).</summary>
        public AudioSource Voice => _voice;
        /// <summary>One-shots played so far (tests).</summary>
        public int OneShots { get; private set; }
        /// <summary>The last one-shot clip played (tests).</summary>
        public AudioClip LastClip { get; private set; }

        /// <summary>Wires the boss and the clips (tests; the scene builder sets the serialized fields).</summary>
        public void Setup(BossFight fight, AudioClip rumbleLoop, AudioClip[] scrapeClips, AudioClip[] dashClips,
            AudioClip[] stopClips, AudioClip[] crashClips)
        {
            Unbind();
            boss = fight;
            rumble = rumbleLoop;
            scrapes = scrapeClips;
            dashes = dashClips;
            stops = stopClips;
            crashes = crashClips;
            if (_rumble != null) _rumble.clip = rumble;
            if (isActiveAndEnabled) Bind();
        }

        private void Awake()
        {
            _rumble = NewSource("Charge Growl");
            _rumble.loop = true;
            _rumble.clip = rumble;
            _voice = NewSource("Charge Voice");
        }

        private void OnEnable() => Bind();

        private void OnDisable()
        {
            Unbind();
            StopRumble();
        }

        private void Bind()
        {
            if (boss == null) return;
            boss.ChargeWarned += OnChargeWarned;
            boss.DashStarted += OnDashStarted;
            boss.Charged += OnCharged;
        }

        private void Unbind()
        {
            if (boss == null) return;
            boss.ChargeWarned -= OnChargeWarned;
            boss.DashStarted -= OnDashStarted;
            boss.Charged -= OnCharged;
        }

        private void Update() => Tick();

        /// <summary>Follows the wind-up: growl pitch and volume from the lane fill; stops when the charge is no longer warning.</summary>
        public void Tick()
        {
            if (!Rumbling) return;
            if (boss == null || boss.Current != BossFight.Stage.Fighting || boss.CurrentAttack != BossFight.Attack.WindUp
                || boss.CurrentMove != BossFight.Move.Charge)
            {
                StopRumble(); // the dash started without the event, the boss died, or the Knight fell mid-warning
                return;
            }
            bool paused = Time.timeScale <= 0f;
            if (paused != _paused)
            {
                _paused = paused;
                if (paused) _rumble.Pause();
                else _rumble.UnPause();
            }
            float p = boss.WindUpProgress;
            _rumble.pitch = Mathf.Lerp(rumblePitchStart, rumblePitchEnd, p);
            _rumble.volume = rumbleVolume * GameSettings.SfxVolume * Mathf.Lerp(rumbleStartShare, 1f, p * p);
            FollowBoss();
        }

        private void OnChargeWarned(Vector3 origin, Vector3 direction, float length)
        {
            FollowBoss(origin);
            Rumbling = true;
            _paused = false;
            _rumble.pitch = rumblePitchStart;
            _rumble.volume = rumbleVolume * GameSettings.SfxVolume * rumbleStartShare;
            if (_rumble.clip != null)
            {
                _rumble.time = 0f;
                _rumble.Play(); // a re-dash restarts the one growl, never a second one
            }
            PlayOneShot(scrapes, scrapeVolume);
        }

        private void OnDashStarted(Vector3 origin, Vector3 direction)
        {
            StopRumble();
            FollowBoss(origin);
            PlayOneShot(dashes, dashVolume);
        }

        private void OnCharged(Vector3 end, float dealt)
        {
            FollowBoss(end);
            bool crashed = boss != null && boss.DashBlocked;
            PlayOneShot(crashed ? crashes : stops, crashed ? crashVolume : stopVolume);
        }

        private void StopRumble()
        {
            Rumbling = false;
            _paused = false;
            if (_rumble != null) _rumble.Stop();
        }

        private void PlayOneShot(AudioClip[] clips, float volume)
        {
            if (clips == null || clips.Length == 0) return;
            int i = Random.Range(0, clips.Length);
            if (clips.Length > 1 && i == _lastClip) i = (i + 1) % clips.Length;
            _lastClip = i;
            if (clips[i] == null) return;
            _voice.pitch = 1f + Random.Range(-pitchJitter, pitchJitter);
            _voice.PlayOneShot(clips[i], volume * GameSettings.SfxVolume);
            OneShots++;
            LastClip = clips[i];
        }

        private void FollowBoss()
        {
            if (boss != null && boss.Boss != null) FollowBoss(boss.Boss.transform.position);
        }

        private void FollowBoss(Vector3 at)
        {
            _rumble.transform.position = at;
            _voice.transform.position = at;
        }

        private AudioSource NewSource(string sourceName)
        {
            var go = new GameObject(sourceName);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = spatialBlend;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = fullVolumeDistance;
            src.maxDistance = fullVolumeDistance * 3f;
            src.dopplerLevel = 0f;
            return src;
        }
    }
}
