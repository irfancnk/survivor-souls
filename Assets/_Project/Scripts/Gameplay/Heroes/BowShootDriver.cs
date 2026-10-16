using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Heroes
{
    /// <summary>
    /// Bow shoot cycle of a bow hero (KAN-153 Shadow Ranger): while the shooter is engaged, bool "Shooting" puts the
    /// Animator in its Shoot state, and float "ShootTime" (the state's motion time) is driven from the shooter's clock:
    /// every shot (<see cref="AutoShooter.Fired"/>) lands on the clip's release frame (<see cref="releaseAt"/>), the
    /// follow-through, next arrow and draw then fill exactly one fire interval, so the next release is the next shot. No
    /// shot due (target just found) = the pose waits at full draw. The animation never gates a shot. On the hero's
    /// Model root (HeroBuilder); finds the shooter on the player. No GC per frame.
    /// </summary>
    [DefaultExecutionOrder(110)] // after the locomotion driver
    public sealed class BowShootDriver : MonoBehaviour
    {
        public static readonly int ShootingId = Animator.StringToHash("Shooting");
        public static readonly int ShootTimeId = Animator.StringToHash("ShootTime");

        [SerializeField] private Animator animator;
        [Tooltip("Normalized time of the release frame in the Shoot clip (frame 19 of 30 = 0.63).")]
        [SerializeField, Range(0f, 1f)] private float releaseAt = 0.63f;

        private AutoShooter _shooter;
        private float _since = float.PositiveInfinity;

        /// <summary>Normalized Shoot clip time written last frame (tests).</summary>
        public float Phase { get; private set; }
        public bool Shooting { get; private set; }
        public float ReleaseAt => releaseAt;
        public Animator Animator => animator;

        /// <summary>Tests and code-built heroes.</summary>
        public void Setup(Animator target, AutoShooter shooter)
        {
            animator = target;
            Bind(shooter);
        }

        private void Awake()
        {
            if (animator == null) animator = GetComponent<Animator>();
        }

        private void OnEnable() => Bind(_shooter != null ? _shooter : GetComponentInParent<AutoShooter>());

        private void OnDisable()
        {
            if (_shooter != null) _shooter.Fired -= OnFired;
        }

        private void Bind(AutoShooter shooter)
        {
            if (_shooter != null) _shooter.Fired -= OnFired;
            _shooter = shooter;
            if (_shooter != null && isActiveAndEnabled) _shooter.Fired += OnFired;
        }

        private void OnFired(Vector3 muzzle, Vector3 direction) => _since = 0f;

        private void Update()
        {
            if (animator == null) return;
            _since += Time.deltaTime;
            Shooting = _shooter != null && _shooter.isActiveAndEnabled && _shooter.Engaged;
            Phase = PhaseAt(_since, _shooter != null ? _shooter.CurrentInterval : 1f, releaseAt);
            animator.SetBool(ShootingId, Shooting);
            animator.SetFloat(ShootTimeId, Phase);
        }

        /// <summary>
        /// Clip time <paramref name="since"/> seconds after a shot with fire interval <paramref name="interval"/>: the
        /// release frame at 0, one full clip loop per interval, held just before the release once the interval is over.
        /// </summary>
        public static float PhaseAt(float since, float interval, float release)
        {
            float t = since / Mathf.Max(0.05f, interval);
            if (!(t < 0.999f)) t = 0.999f; // also catches infinity: wait at full draw
            return Mathf.Repeat(release + t, 1f);
        }
    }
}
