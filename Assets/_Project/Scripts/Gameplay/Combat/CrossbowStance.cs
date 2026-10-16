using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Two-hand shoulder aim stance of the Knight (KAN-130.2 / KAN-216). Drives the "UpperBody" Animator layer built by
    /// MeshyCharacterImporter: while the <see cref="AutoShooter"/> has a target the layer plays the Aim idle (crossbow at
    /// the right shoulder, cheek on the stock); <see cref="lowerDelay"/> seconds after the last target the Lower clip takes
    /// the crossbow back to the hip carry and the layer fades out. <see cref="CrossbowAim"/> still turns the spine onto the
    /// target in LateUpdate, on top of this pose.
    /// While the Knight stands still and the root turns more than <see cref="stepAngle"/> degrees, a short turn-in-place
    /// step plays on the base layer. No allocation per frame.
    /// </summary>
    [DefaultExecutionOrder(110)] // after movement, the shooter and the locomotion driver
    public sealed class CrossbowStance : MonoBehaviour
    {
        public const string UpperBodyLayer = "UpperBody";
        public const string AimParam = "Aim";
        public const string TurnStepParam = "TurnStep";
        public const string AimState = "Aim";
        public const string LowerState = "Lower";
        public const string TurnStepState = "TurnStep";

        public static readonly int AimId = Animator.StringToHash(AimParam);
        public static readonly int TurnStepId = Animator.StringToHash(TurnStepParam);
        public static readonly int AimStateHash = Animator.StringToHash(AimState);
        public static readonly int LowerStateHash = Animator.StringToHash(LowerState);

        [SerializeField] private Animator animator;
        [Tooltip("Seconds without a target before the crossbow comes down (Lower clip).")]
        [SerializeField, Min(0f)] private float lowerDelay = 1f;
        [Tooltip("Seconds to raise the upper-body layer to full weight when a target appears.")]
        [SerializeField, Min(0.01f)] private float raiseTime = 0.12f;
        [Tooltip("Seconds to fade the upper-body layer out after the Lower clip.")]
        [SerializeField, Min(0.01f)] private float fadeTime = 0.15f;
        [Tooltip("Root turn (degrees) while standing that plays one turn-in-place step.")]
        [SerializeField, Range(10f, 180f)] private float stepAngle = 45f;
        [Tooltip("Planar speed (m/s) under which the Knight counts as standing.")]
        [SerializeField, Min(0f)] private float standSpeed = 0.25f;
        [Tooltip("Seconds between two steps.")]
        [SerializeField, Min(0f)] private float stepCooldown = 0.45f;
        [Tooltip("Slow turning is forgotten at this rate (degrees per second), so drift never adds up to a step.")]
        [SerializeField, Min(0f)] private float turnForget = 40f;

        private AutoShooter _shooter;
        private int _layer = -1;
        private float _weight;
        private float _sinceTarget = float.MaxValue;
        private float _lastYaw;
        private Vector3 _lastPosition;
        private float _turn;
        private float _stepTimer;

        /// <summary>Upper-body layer weight 0..1 (1 = the layer pose fully replaces the base upper body).</summary>
        public float Weight => _weight;

        /// <summary>True while the stance holds the crossbow up (target now, or within <see cref="lowerDelay"/>).</summary>
        public bool Aiming => _sinceTarget < lowerDelay;

        /// <summary>Turn-in-place steps played so far (for tests and dev logs).</summary>
        public int Steps { get; private set; }

        public Animator Animator => animator;
        public int Layer => _layer;

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            _lastYaw = transform.eulerAngles.y;
            _lastPosition = transform.position;
        }

        private void Start()
        {
            // The scene builder adds the shooter after the prefab (KAN-215 pattern): look it up once here.
            _shooter = GetComponent<AutoShooter>();
            _layer = animator != null && animator.runtimeAnimatorController != null ? animator.GetLayerIndex(UpperBodyLayer) : -1;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (_layer < 0 || dt <= 0f) return;

            bool target = _shooter != null && _shooter.isActiveAndEnabled && _shooter.Engaged && _shooter.Target != null;
            _sinceTarget = target ? 0f : _sinceTarget + dt; // float.MaxValue + dt stays MaxValue (never aimed)
            bool aiming = Aiming;
            animator.SetBool(AimId, aiming);

            float goal;
            if (aiming) goal = 1f;
            else
            {
                // Hold the weight while the Lower clip brings the crossbow down, then fade (the base pose is the same carry).
                var state = animator.GetCurrentAnimatorStateInfo(_layer);
                bool lowering = animator.IsInTransition(_layer) || (state.shortNameHash == LowerStateHash && state.normalizedTime < 1f);
                goal = lowering ? _weight : 0f;
            }
            _weight = Mathf.MoveTowards(_weight, goal, dt / (goal > _weight ? raiseTime : fadeTime));
            animator.SetLayerWeight(_layer, _weight);

            TurnStep(dt);
        }

        private void TurnStep(float dt)
        {
            float yaw = transform.eulerAngles.y;
            float turned = Mathf.Abs(Mathf.DeltaAngle(_lastYaw, yaw));
            _lastYaw = yaw;
            Vector3 delta = transform.position - _lastPosition;
            _lastPosition = transform.position;
            delta.y = 0f;
            _stepTimer -= dt;

            if (delta.magnitude / dt > standSpeed) { _turn = 0f; return; }
            _turn = Mathf.Max(0f, _turn + turned - turnForget * dt);
            if (_turn < stepAngle || _stepTimer > 0f) return;
            animator.SetTrigger(TurnStepId);
            Steps++;
            _turn = 0f;
            _stepTimer = stepCooldown;
        }
    }
}
