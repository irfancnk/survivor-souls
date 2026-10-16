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
    /// step plays on the base layer.
    /// KAN-225: while the base layer plays the Hit reaction, the layer weight dips to <see cref="hitLayerWeight"/> (and
    /// <see cref="CrossbowAim"/> eases its spine turn by <see cref="AimShare"/>), so a hit while aiming shows on the chest,
    /// arms and head too, not only on the legs. The shot still lines up: <see cref="CrossbowAim.AlignNow"/> aligns fully.
    /// No allocation per frame.
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
        public const string HitState = "Hit";

        public static readonly int AimId = Animator.StringToHash(AimParam);
        public static readonly int TurnStepId = Animator.StringToHash(TurnStepParam);
        public static readonly int AimStateHash = Animator.StringToHash(AimState);
        public static readonly int LowerStateHash = Animator.StringToHash(LowerState);
        public static readonly int HitStateHash = Animator.StringToHash(HitState);

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
        [Header("Hit flinch (KAN-225)")]
        [Tooltip("Upper-body layer weight share while the base layer plays Hit (1 = the aim pose hides the flinch).")]
        [SerializeField, Range(0f, 1f)] private float hitLayerWeight = 0.3f;
        [Tooltip("CrossbowAim spine / head turn share while the base layer plays Hit.")]
        [SerializeField, Range(0f, 1f)] private float hitAimWeight = 0.5f;
        [Tooltip("Seconds to dip into the flinch when Hit starts.")]
        [SerializeField, Min(0.01f)] private float hitDipTime = 0.05f;
        [Tooltip("Seconds to come back to the full aim after Hit.")]
        [SerializeField, Min(0.01f)] private float hitRecoverTime = 0.2f;

        private AutoShooter _shooter;
        private int _layer = -1;
        private float _weight;
        private float _sinceTarget = float.MaxValue;
        private float _lastYaw;
        private Vector3 _lastPosition;
        private float _turn;
        private float _stepTimer;
        private float _hit; // 0 = no flinch, 1 = full Hit dip

        /// <summary>Upper-body layer weight 0..1 (1 = the layer pose fully replaces the base upper body).</summary>
        public float Weight => _weight;

        /// <summary>Hit flinch blend 0..1 (KAN-225): 1 while the base layer plays Hit, back to 0 after it.</summary>
        public float HitBlend => _hit;

        /// <summary>Upper-body layer weight given to the Animator: <see cref="Weight"/> dipped by the Hit flinch.</summary>
        public float LayerWeight => _weight * Mathf.Lerp(1f, hitLayerWeight, _hit);

        /// <summary>Share of the <see cref="CrossbowAim"/> turn to keep now (1, or <see cref="hitAimWeight"/> in a Hit).</summary>
        public float AimShare => Mathf.Lerp(1f, hitAimWeight, _hit);

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
                // Hold the weight until the Lower clip has brought the crossbow down, then fade (the base pose is the same
                // carry). The Animator reads the new "Aim" value after this Update, so the first frame still shows Aim: hold.
                var state = animator.GetCurrentAnimatorStateInfo(_layer);
                bool down = !animator.IsInTransition(_layer) && state.shortNameHash == LowerStateHash && state.normalizedTime >= 1f;
                goal = down ? 0f : _weight;
            }
            _weight = Mathf.MoveTowards(_weight, goal, dt / (goal > _weight ? raiseTime : fadeTime));
            float hitGoal = InHit() ? 1f : 0f;
            _hit = Mathf.MoveTowards(_hit, hitGoal, dt / (hitGoal > _hit ? hitDipTime : hitRecoverTime));
            animator.SetLayerWeight(_layer, LayerWeight);

            TurnStep(dt);
        }

        /// <summary>The base layer plays Hit, or is blending into it.</summary>
        private bool InHit()
        {
            if (animator.GetCurrentAnimatorStateInfo(0).shortNameHash == HitStateHash) return true;
            return animator.IsInTransition(0) && animator.GetNextAnimatorStateInfo(0).shortNameHash == HitStateHash;
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
