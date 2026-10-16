using SurvivorSouls.Core;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Shoot cycle of the Knight (KAN-130.3 / KAN-217) on the "UpperBody" Animator layer of <see cref="CrossbowStance"/>:
    /// each volley (<see cref="AutoShooter.Fired"/>) plays the Fire clip (sharp kick), then the Reload clip (string pulled
    /// back, bolt seated) at a speed that fits the time left before the next shot:
    /// <c>speed = reloadLength / ((CurrentInterval - fireLength) * fill)</c>, clamped. When even the fastest reload does not
    /// fit (Quick Draw max and similar), the snap variant plays: Fire straight back to Aim, the bolt pops back on.
    /// The animation never gates the shooter: shots still fire on the shooter's own timer, a new shot restarts Fire.
    /// The loaded bolt on the rail hides on the shot and shows again at <see cref="seatAt"/> of the reload (a quiet seat
    /// click plays there). Double Shot follow-ups keep the small additive <see cref="CrossbowRecoil"/> kick; the main kick
    /// is mostly the Fire clip now. No allocation per shot or frame.
    /// </summary>
    [DefaultExecutionOrder(111)] // after CrossbowStance (layer weight, Aim bool)
    public sealed class CrossbowCycle : MonoBehaviour
    {
        public const string FireState = "Fire";
        public const string ReloadState = "Reload";
        public const string SnapParam = "Snap";
        public const string ReloadSpeedParam = "ReloadSpeed";

        public static readonly int FireStateHash = Animator.StringToHash(FireState);
        public static readonly int ReloadStateHash = Animator.StringToHash(ReloadState);
        public static readonly int SnapId = Animator.StringToHash(SnapParam);
        public static readonly int ReloadSpeedId = Animator.StringToHash(ReloadSpeedParam);

        [SerializeField] private Animator animator;
        [Tooltip("Bolt resting on the rail (hidden from the shot until the reload seats a new one).")]
        [SerializeField] private GameObject loadedBolt;
        [Tooltip("Fire clip length (s) at speed 1 (the importer writes the real one).")]
        [SerializeField, Min(0.01f)] private float fireLength = 0.125f;
        [Tooltip("Reload clip length (s) at speed 1 (the importer writes the real one).")]
        [SerializeField, Min(0.01f)] private float reloadLength = 0.583f;
        [Tooltip("Share of the free time the reload fills, so the crossbow is back on aim a moment before the next shot.")]
        [SerializeField, Range(0.5f, 1f)] private float fill = 0.85f;
        [Tooltip("Slowest / fastest reload playback. Over the fastest the snap variant plays (no reload clip).")]
        [SerializeField] private Vector2 speedRange = new Vector2(0.5f, 3f);
        [Tooltip("Reload clip share where the new bolt sits on the rail (build_knight_crossbow.py RELOAD_SEAT).")]
        [SerializeField, Range(0f, 1f)] private float seatAt = 0.7f;
        [Tooltip("Main volley kick of CrossbowRecoil while this cycle runs (the Fire clip carries the rest).")]
        [SerializeField, Range(0f, 1f)] private float recoilScale = 0.4f;
        [SerializeField] private AudioClip[] seatClips;
        [SerializeField, Range(0f, 1f)] private float seatVolume = 0.22f;

        private AutoShooter _shooter;
        private AudioSource _audio;
        private int _layer = -1;
        private float _sinceShot = float.MaxValue;
        private bool _reloading; // a full reload is running: the bolt is seated at seatAt
        private bool _boltOn = true;

        /// <summary>Volleys this cycle has animated (tests, dev logs).</summary>
        public int Shots { get; private set; }
        /// <summary>True when the last volley used the snap variant (no reload clip).</summary>
        public bool Snap { get; private set; }
        /// <summary>Reload playback speed set for the last volley.</summary>
        public float ReloadSpeed { get; private set; } = 1f;
        /// <summary>True while a bolt rests on the rail.</summary>
        public bool BoltLoaded => _boltOn;
        public float FireLength => fireLength;
        public float ReloadLength => reloadLength;
        public Vector2 SpeedRange => speedRange;
        public float Fill => fill;
        public int Layer => _layer;

        /// <summary>Raised when the reload (or the snap) puts a new bolt on the rail.</summary>
        public event System.Action BoltSeated;

        /// <summary>Wiring for the builder and tests.</summary>
        public void Setup(Animator anim, GameObject bolt)
        {
            animator = anim;
            loadedBolt = bolt;
        }

        /// <summary>
        /// Reload speed for a fire interval, or 0 when the snap variant must play (the reload does not fit even at the
        /// fastest speed). Pure: tests and the cycle use the same rule.
        /// </summary>
        public static float SpeedFor(float interval, float fireLength, float reloadLength, float fill, Vector2 range)
        {
            float free = (interval - fireLength) * fill;
            if (free <= 0f) return 0f;
            float speed = reloadLength / free;
            if (speed > range.y) return 0f;
            return Mathf.Max(speed, range.x);
        }

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
        }

        private void Start()
        {
            // The scene builder adds the shooter and the recoil after the prefab: look them up here (CrossbowStance pattern).
            _shooter = GetComponent<AutoShooter>();
            _layer = animator != null && animator.runtimeAnimatorController != null ? animator.GetLayerIndex(CrossbowStance.UpperBodyLayer) : -1;
            if (_shooter != null)
            {
                _shooter.Fired -= OnFired;
                _shooter.Fired += OnFired;
            }
            var recoil = GetComponent<CrossbowRecoil>();
            if (recoil != null) recoil.SetVolleyScale(recoilScale);
            if (seatClips != null && seatClips.Length > 0)
            {
                _audio = gameObject.AddComponent<AudioSource>();
                _audio.playOnAwake = false;
                _audio.spatialBlend = 0f;
            }
        }

        private void OnDestroy()
        {
            if (_shooter != null) _shooter.Fired -= OnFired;
        }

        private void OnFired(Vector3 muzzle, Vector3 direction)
        {
            Shots++;
            _sinceShot = 0f;
            float speed = SpeedFor(_shooter.CurrentInterval, fireLength, reloadLength, fill, speedRange);
            Snap = speed <= 0f;
            ReloadSpeed = Snap ? speedRange.y : speed;
            _reloading = !Snap;
            SetBolt(false);
            if (_layer < 0) return;
            animator.SetBool(SnapId, Snap);
            animator.SetFloat(ReloadSpeedId, ReloadSpeed);
            animator.CrossFadeInFixedTime(FireStateHash, 0.02f, _layer, 0f);
        }

        private void Update()
        {
            if (_boltOn || _sinceShot == float.MaxValue) return;
            _sinceShot += Time.deltaTime;
            // Snap: the bolt pops on when the Fire clip ends. Reload: at seatAt of the reload clip (by time, so it also
            // works when the layer is culled or the Animator is off).
            float seat = Snap ? fireLength : fireLength + seatAt * reloadLength / Mathf.Max(0.01f, ReloadSpeed);
            if (_sinceShot < seat) return;
            SetBolt(true);
            if (_reloading && _audio != null && seatClips.Length > 0)
                _audio.PlayOneShot(seatClips[Random.Range(0, seatClips.Length)], seatVolume * GameSettings.SfxVolume);
            _reloading = false;
            BoltSeated?.Invoke();
        }

        private void SetBolt(bool on)
        {
            _boltOn = on;
            if (loadedBolt != null && loadedBolt.activeSelf != on) loadedBolt.SetActive(on);
        }
    }
}
