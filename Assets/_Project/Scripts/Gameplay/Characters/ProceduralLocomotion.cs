using UnityEngine;

namespace SurvivorSouls.Gameplay.Characters
{
    /// <summary>
    /// Procedural idle + run for a <see cref="HumanoidRig"/>. Reads speed from its own transform motion,
    /// so it works with any mover. Stride phase advances with distance, so feet do not slide at any speed.
    /// Angle convention (Unity, rest pose arms/legs down): +X swings a limb backward, -X forward.
    /// Replace with Animator clips when real animations arrive; keep the same rig fields.
    /// </summary>
    [DefaultExecutionOrder(100)] // after movement
    public sealed class ProceduralLocomotion : MonoBehaviour
    {
        [SerializeField] private HumanoidRig rig;
        [Tooltip("Speed (m/s) treated as full run.")]
        [SerializeField, Min(0.1f)] private float runSpeed = 5f;
        [Tooltip("Metres travelled per full stride cycle (two steps).")]
        [SerializeField, Min(0.1f)] private float strideLength = 2.1f;
        [SerializeField, Min(0.1f)] private float blendSharpness = 10f;

        [Header("Run amplitudes (degrees)")]
        [SerializeField] private float legSwing = 40f;
        [SerializeField] private float kneeBend = 60f;
        [SerializeField] private float armSwing = 38f;
        [SerializeField] private float bowArmSwing = 18f;
        [SerializeField] private float forwardLean = 11f;
        [SerializeField] private float bob = 0.05f;
        [Tooltip("Scales all arm motion. 0 keeps the arms in the rest pose (e.g. a model that holds its bow in front).")]
        [SerializeField, Range(0f, 1f)] private float armWeight = 1f;

        private Transform _mover;
        private Vector3 _lastPosition;
        private float _run;   // 0 idle .. 1 full run (smoothed)
        private float _phase; // radians
        private float _idleTime;

        private void Awake()
        {
            if (rig == null) rig = GetComponentInChildren<HumanoidRig>();
            _mover = transform;
            _lastPosition = _mover.position;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;

            Vector3 delta = _mover.position - _lastPosition;
            delta.y = 0f;
            _lastPosition = _mover.position;
            float speed = delta.magnitude / dt;

            float target = Mathf.Clamp01(speed / runSpeed);
            _run = Mathf.Lerp(_run, target, 1f - Mathf.Exp(-blendSharpness * dt));
            _phase = Mathf.Repeat(_phase + delta.magnitude / strideLength * Mathf.PI * 2f, Mathf.PI * 2f);
            _idleTime += dt;

            Apply(_run, _phase, _idleTime);
        }

        private Quaternion Weighted(float x, float y, float z) => Quaternion.Euler(x * armWeight, y * armWeight, z * armWeight);

        private void Apply(float run, float phase, float t)
        {
            float idle = 1f - run;
            float s = Mathf.Sin(phase), c = Mathf.Cos(phase);
            float breathe = Mathf.Sin(t * 1.7f);

            // Hips and torso.
            rig.hips.localPosition = rig.HipsRestPosition
                + Vector3.up * (run * (bob * Mathf.Abs(c) - bob * 0.8f) + idle * breathe * 0.004f);
            rig.SetRotation(rig.hips, Quaternion.Euler(0f, 9f * s * run, -2f * c * run));
            rig.SetRotation(rig.spine, Quaternion.Euler(forwardLean * run, -7f * s * run, 0f));
            rig.SetRotation(rig.chest, Quaternion.Euler(idle * (1.5f * breathe), -3f * s * run, 0f));
            rig.SetRotation(rig.neck, Quaternion.Euler(-forwardLean * 0.7f * run, 0f, 0f));
            rig.SetRotation(rig.head, Quaternion.Euler(idle * -2f, idle * 10f * Mathf.Sin(t * 0.35f), 0f));

            // Legs: knee bends most while the leg swings forward.
            rig.SetRotation(rig.thighL, Quaternion.Euler(-legSwing * s * run - idle * 2f, 0f, idle * -2f));
            rig.SetRotation(rig.thighR, Quaternion.Euler(legSwing * s * run - idle * 2f, 0f, idle * 2f));
            float kneeL = run * (12f + kneeBend * Mathf.Max(0f, c)) + idle * 4f;
            float kneeR = run * (12f + kneeBend * Mathf.Max(0f, -c)) + idle * 4f;
            rig.SetRotation(rig.shinL, Quaternion.Euler(kneeL, 0f, 0f));
            rig.SetRotation(rig.shinR, Quaternion.Euler(kneeR, 0f, 0f));
            rig.SetRotation(rig.footL, Quaternion.Euler(-kneeL * 0.45f + legSwing * s * run * 0.3f, 0f, 0f));
            rig.SetRotation(rig.footR, Quaternion.Euler(-kneeR * 0.45f - legSwing * s * run * 0.3f, 0f, 0f));

            // Arms: right arm swings against right leg; left arm holds the bow, so it swings less.
            float sway = idle * Mathf.Sin(t * 1.1f) * 1.5f;
            if (armWeight <= 0f)
            {
                // Arms stay in the rest pose; only a little bounce so they do not look frozen.
                float bounce = run * 3f * Mathf.Abs(c);
                rig.SetRotation(rig.upperArmR, Quaternion.Euler(bounce, 0f, sway));
                rig.SetRotation(rig.upperArmL, Quaternion.Euler(bounce, 0f, -sway));
                return;
            }
            rig.SetRotation(rig.upperArmR, Weighted(-armSwing * s * run, 0f, 8f + sway + 4f * run));
            rig.SetRotation(rig.foreArmR, Weighted(-(14f + 55f * run) - 12f * Mathf.Max(0f, -s) * run, 0f, 0f));
            rig.SetRotation(rig.handR, Quaternion.identity);

            rig.SetRotation(rig.upperArmL, Weighted(bowArmSwing * s * run - idle * 4f, 0f, -10f - sway - 6f * run));
            rig.SetRotation(rig.foreArmL, Weighted(-(22f + 30f * run), 0f, 0f));
            rig.SetRotation(rig.handL, Weighted(10f * run, 0f, 0f));
        }
    }
}
