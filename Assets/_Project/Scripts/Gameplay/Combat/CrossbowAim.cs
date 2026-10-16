using SurvivorSouls.Gameplay.Characters;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Upper-body aim (KAN-130.1 / KAN-215): the spine and chest turn so the crossbow line points at the
    /// <see cref="AutoShooter"/> target, and the head looks at it. The legs keep the Animator pose. Runs in LateUpdate on
    /// top of the Animator pose (like <see cref="CrossbowRecoil"/>), so it works with every clip.
    /// The crossbow hangs on the chest (Spine2), so a chest turn moves the whole hold. Yaw turns about world up, pitch about
    /// the horizontal axis across the aim line: no roll. The spine takes <see cref="spineShare"/> of the turn, the chest the
    /// rest, then a second pass removes the small error from the muzzle moving with the turn.
    /// <see cref="AutoShooter.Fire"/> calls <see cref="AlignNow"/> just before the bolts leave, so the root turn of that frame
    /// cannot leave the crossbow off the line: the muzzle line is within a fraction of a degree of the bolt line at each shot
    /// (inside the twist / pitch limits). No allocation per frame.
    /// </summary>
    [RequireComponent(typeof(AutoShooter))]
    public sealed class CrossbowAim : MonoBehaviour
    {
        [SerializeField] private HumanoidRig rig;
        [Tooltip("Crossbow_Muzzle socket. The crossbow line is muzzleAxis in its local space.")]
        [SerializeField] private Transform muzzle;
        [Tooltip("Crossbow line (muzzle forward) in the socket's local space. Knight GLB: the socket's local Y (+X of the crossbow, stock to muzzle).")]
        [SerializeField] private Vector3 muzzleAxis = Vector3.up;
        [Tooltip("Most upper-body yaw away from the animated pose (degrees). Matches the run-and-shoot twist limit of the shooter.")]
        [SerializeField, Range(0f, 180f)] private float maxYaw = 100f;
        [Tooltip("Most upper-body pitch up or down (degrees).")]
        [SerializeField, Range(0f, 60f)] private float maxPitch = 25f;
        [Tooltip("Share of the turn on the lower spine; the chest takes the rest.")]
        [SerializeField, Range(0f, 1f)] private float spineShare = 0.4f;
        [Tooltip("Most head yaw toward the target, on top of the chest (degrees).")]
        [SerializeField, Range(0f, 90f)] private float maxHeadYaw = 60f;
        [Tooltip("Share of the head turn on the neck; the head takes the rest.")]
        [SerializeField, Range(0f, 1f)] private float neckShare = 0.4f;
        [Tooltip("Seconds to blend the aim in when a target appears.")]
        [SerializeField, Min(0.01f)] private float blendIn = 0.08f;
        [Tooltip("Seconds to blend the aim out when there is no target or the Knight moves.")]
        [SerializeField, Min(0.01f)] private float blendOut = 0.25f;
        [Tooltip("KAN-231: the upper body swings to a new target this much faster than the root turn (same spring, shorter " +
                 "smoothing, higher cap), so the chest leads the turn instead of jumping up to maxYaw in one frame.")]
        [SerializeField, Min(1f)] private float leadFactor = 1.4f;

        private AutoShooter _shooter;
        private Vector3 _headForwardLocal; // the character forward in head space at rest
        private Vector3 _point;
        private float _weight;
        private bool _ready;
        private float _aimYaw, _aimYawVelocity; // smoothed world yaw of the aim (KAN-231)
        private bool _hasAim;

        /// <summary>Current aim blend 0..1 (1 = full upper-body aim on the target).</summary>
        public float Weight => _weight;

        /// <summary>The Crossbow_Muzzle socket (null on a model without one).</summary>
        public Transform Muzzle => muzzle;

        /// <summary>World direction of the crossbow line (muzzle forward) in the current pose.</summary>
        public Vector3 MuzzleLine => muzzle != null ? muzzle.TransformDirection(muzzleAxis).normalized : transform.forward;

        /// <summary>World forward of the head (rest forward carried by the current head pose).</summary>
        public Vector3 HeadForward => rig != null && rig.head != null ? rig.head.TransformDirection(_headForwardLocal) : transform.forward;

        /// <summary>Wiring for code-built objects and tests.</summary>
        public void Setup(HumanoidRig humanoid, Transform muzzleSocket)
        {
            rig = humanoid;
            muzzle = muzzleSocket;
            Init();
        }

        private void Awake()
        {
            _shooter = GetComponent<AutoShooter>();
            Init();
        }

        private void Init()
        {
            _ready = rig != null && muzzle != null && rig.chest != null;
            // At Awake the bones are in the prefab (bind) pose, where the head looks along the character forward.
            if (rig != null && rig.head != null) _headForwardLocal = rig.head.InverseTransformDirection(transform.forward);
        }

        private void LateUpdate()
        {
            if (!_ready) return;
            var target = _shooter.isActiveAndEnabled && _shooter.Engaged ? _shooter.Target : null;
            if (target != null) _point = SmoothedPoint(PointBlankAim.AimPoint(transform.position, target.AimPoint)); // KAN-224
            else _hasAim = false;
            float goal = target != null ? 1f : 0f;
            float time = goal > _weight ? blendIn : blendOut;
            _weight = Mathf.MoveTowards(_weight, goal, Time.deltaTime / time);
            if (_weight <= 0f) return;
            Solve(_point, _weight);
            Look(_point, _weight);
        }

        /// <summary>
        /// Turns the upper body fully onto <paramref name="point"/> now, from the current pose. The shooter calls it just
        /// before a volley, so the bolt line and the crossbow line match at the shot.
        /// </summary>
        public void AlignNow(Vector3 point)
        {
            if (!_ready) return;
            _point = point;
            Vector3 to = point - transform.position;
            if (to.x * to.x + to.z * to.z > 1e-6f)
            {
                _aimYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg; // the smoothed aim continues from the shot line
                _hasAim = true;
            }
            Solve(point, 1f);
        }

        /// <summary>
        /// KAN-231: the aim point swung round the root at a limited, eased rate (the shooter's spring turn, faster by
        /// <see cref="leadFactor"/>): the upper body no longer jumps up to <see cref="maxYaw"/> in one frame when the next
        /// target is behind the Knight. Starts from the body forward when a target appears; same distance and height.
        /// </summary>
        private Vector3 SmoothedPoint(Vector3 point)
        {
            Vector3 to = point - transform.position;
            float flat = Mathf.Sqrt(to.x * to.x + to.z * to.z);
            if (flat < 1e-3f) return point;
            float goal = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            if (!_hasAim)
            {
                _aimYaw = transform.eulerAngles.y;
                _aimYawVelocity = 0f;
                _hasAim = true;
            }
            float dt = Time.deltaTime, speed = _shooter.AimTurnSpeed * leadFactor;
            _aimYaw = _shooter.AimTurnSmoothing > 0f
                ? AutoShooter.TurnYaw(_aimYaw, goal, ref _aimYawVelocity, _shooter.AimTurnSmoothing / leadFactor, speed, dt)
                : Mathf.MoveTowardsAngle(_aimYaw, goal, speed * dt);
            float rad = _aimYaw * Mathf.Deg2Rad;
            return new Vector3(transform.position.x + Mathf.Sin(rad) * flat, point.y, transform.position.z + Mathf.Cos(rad) * flat);
        }

        /// <summary>Degrees between the crossbow line and the line from the muzzle to <paramref name="point"/>.</summary>
        public float AimError(Vector3 point) => muzzle == null ? 180f : Vector3.Angle(MuzzleLine, point - muzzle.position);

        private void Solve(Vector3 point, float weight)
        {
            // Pass 1 shares the turn between spine and chest; pass 2 (chest only) removes what the muzzle shift left over.
            if (rig.spine != null) Turn(rig.spine, point, spineShare * weight);
            Turn(rig.chest, point, weight);
            Turn(rig.chest, point, weight);
        }

        /// <summary>Turns <paramref name="bone"/> by <paramref name="share"/> of the yaw + pitch that puts the muzzle line on the point.</summary>
        private void Turn(Transform bone, Vector3 point, float share)
        {
            Vector3 line = MuzzleLine;
            Vector3 to = point - muzzle.position;
            Vector3 lineFlat = new Vector3(line.x, 0f, line.z);
            Vector3 toFlat = new Vector3(to.x, 0f, to.z);
            if (lineFlat.sqrMagnitude < 1e-6f || toFlat.sqrMagnitude < 1e-6f) return;

            float yaw = Mathf.Clamp(Vector3.SignedAngle(lineFlat, toFlat, Vector3.up), -maxYaw, maxYaw) * share;
            float pitch = Mathf.Clamp(Elevation(to) - Elevation(line), -maxPitch, maxPitch) * share;
            // Pitch about the horizontal axis across the target line (after the yaw the crossbow faces along toFlat).
            Vector3 across = Vector3.Cross(Vector3.up, toFlat).normalized;
            var turn = Quaternion.AngleAxis(-pitch, across) * Quaternion.AngleAxis(yaw, Vector3.up);
            bone.rotation = turn * bone.rotation;
        }

        /// <summary>Neck + head yaw so the head forward points at the target (on top of the chest turn).</summary>
        private void Look(Vector3 point, float weight)
        {
            if (rig.head == null) return;
            Vector3 head = HeadForward;
            Vector3 to = point - rig.head.position;
            head.y = 0f;
            to.y = 0f;
            if (head.sqrMagnitude < 1e-6f || to.sqrMagnitude < 1e-6f) return;
            float yaw = Mathf.Clamp(Vector3.SignedAngle(head, to, Vector3.up), -maxHeadYaw, maxHeadYaw) * weight;
            float neck = rig.neck != null ? neckShare : 0f;
            if (rig.neck != null) rig.neck.rotation = Quaternion.AngleAxis(yaw * neck, Vector3.up) * rig.neck.rotation;
            rig.head.rotation = Quaternion.AngleAxis(yaw * (1f - neck), Vector3.up) * rig.head.rotation;
        }

        /// <summary>Angle of a direction above the horizontal plane (degrees, + = up).</summary>
        private static float Elevation(Vector3 v)
        {
            float flat = Mathf.Sqrt(v.x * v.x + v.z * v.z);
            return Mathf.Atan2(v.y, flat) * Mathf.Rad2Deg;
        }
    }
}
