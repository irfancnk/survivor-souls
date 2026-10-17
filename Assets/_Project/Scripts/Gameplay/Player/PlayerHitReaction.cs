using SurvivorSouls.Gameplay.Characters;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Player
{
    /// <summary>
    /// Knight hit reaction (KAN-225): a discrete hit (<see cref="PlayerHealth.OnHit"/>: telegraphed attacks, bolts, boss
    /// hits; never horde contact or burning ground) plays the Hit clip (<see cref="LocomotionAnimatorDriver.PlayHit"/>)
    /// while the Knight stands, at most once per <see cref="cooldown"/>. Before this nothing played it, so the Knight only
    /// flashed. While aiming, <see cref="Combat.CrossbowStance"/> dips the upper-body layer during the clip so the flinch
    /// shows on the chest and arms.
    /// The clip plays on the base layer, so a running Knight gets no clip (no foot slide); if the Knight starts to run
    /// during the clip, it blends back to locomotion at once. The Knight's Hit_Reaction clip is 1.67 s: it is cut after
    /// <see cref="maxHitTime"/> so the flinch is a short beat, not a long stagger. No clip on the killing blow (<see cref="PlayerDeath"/> owns
    /// that). No allocation per hit or per frame.
    /// </summary>
    [RequireComponent(typeof(PlayerHealth))]
    [DefaultExecutionOrder(105)] // after movement and the locomotion driver, before CrossbowStance (110)
    public sealed class PlayerHitReaction : MonoBehaviour
    {
        public const string HitState = "Hit";
        public const string LocomotionState = "Locomotion";
        public static readonly int HitStateHash = Animator.StringToHash(HitState);
        public static readonly int LocomotionStateHash = Animator.StringToHash(LocomotionState);

        [SerializeField] private LocomotionAnimatorDriver driver;
        [Tooltip("Seconds between two hit clips (a fast hit chain plays one flinch, not a stutter).")]
        [SerializeField, Min(0f)] private float cooldown = 0.6f;
        [Tooltip("Planar speed (m/s) under which the Knight counts as standing and plays the clip.")]
        [SerializeField, Min(0f)] private float standSpeed = 0.5f;
        [Tooltip("Planar speed (m/s) that cuts a running clip short (blend back to locomotion).")]
        [SerializeField, Min(0f)] private float cancelSpeed = 1f;
        [Tooltip("Blend (s) from the Hit clip back to locomotion when the Knight starts to run.")]
        [SerializeField, Min(0f)] private float cancelBlend = 0.12f;
        [Tooltip("Seconds of the Hit clip to play before blending back to locomotion (the full clip is a long stagger).")]
        [SerializeField, Min(0.1f)] private float maxHitTime = 0.7f;
        [Tooltip("Blend (s) from the Hit clip back to locomotion at maxHitTime.")]
        [SerializeField, Min(0f)] private float endBlend = 0.2f;

        private PlayerHealth _health;
        private Vector3 _lastPosition;
        private float _speed;
        private float _lastReaction = float.NegativeInfinity;

        /// <summary>Hit clips started (tests, dev logs).</summary>
        public int Reactions { get; private set; }

        /// <summary>Hit clips cut short because the Knight ran (tests).</summary>
        public int Cancels { get; private set; }

        /// <summary>Wiring for code-built objects and tests (the scene step sets the serialized field).</summary>
        public void Setup(LocomotionAnimatorDriver locomotion) => driver = locomotion;

        private void Awake()
        {
            _health = GetComponent<PlayerHealth>();
            if (driver == null) driver = GetComponent<LocomotionAnimatorDriver>();
            _lastPosition = transform.position;
        }

        private void OnEnable() => _health.OnHit += OnHit;
        private void OnDisable() => _health.OnHit -= OnHit;

        private void OnHit(PlayerHit hit)
        {
            if (hit.Killed || _health.IsDead || driver == null || !driver.isActiveAndEnabled || driver.Animator == null) return;
            if (_speed > standSpeed || Time.time - _lastReaction < cooldown) return;
            _lastReaction = Time.time;
            driver.PlayHit();
            Reactions++;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Vector3 delta = transform.position - _lastPosition;
            _lastPosition = transform.position;
            delta.y = 0f;
            _speed = delta.magnitude / dt;

            if (_health.IsDead || driver == null || !driver.isActiveAndEnabled) return;
            var animator = driver.Animator;
            if (animator == null || !animator.isActiveAndEnabled) return;
            bool running = _speed > cancelSpeed;
            if (animator.IsInTransition(0))
            {
                // Blending into Hit while running: cut it. Blending out of Hit (or our own blend back): let it finish.
                if (!running || animator.GetNextAnimatorStateInfo(0).shortNameHash != HitStateHash) return;
            }
            else
            {
                var state = animator.GetCurrentAnimatorStateInfo(0);
                if (state.shortNameHash != HitStateHash) return;
                if (!running)
                {
                    if (state.normalizedTime * state.length < maxHitTime) return;
                    animator.CrossFadeInFixedTime(LocomotionStateHash, endBlend, 0); // short beat, not the full stagger
                    return;
                }
            }
            animator.ResetTrigger(LocomotionAnimatorDriver.HitId);
            animator.CrossFadeInFixedTime(LocomotionStateHash, cancelBlend, 0);
            Cancels++;
        }
    }
}
