using SurvivorSouls.Gameplay.Controls;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Player
{
    /// <summary>
    /// Moves the player on the XZ plane from the joystick. The camera has a fixed yaw of 0,
    /// so stick up = world +Z. The character turns to face the move direction.
    /// <see cref="maxSpeed"/> is the base; move speed skills raise it through <see cref="PlayerStats"/>.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMover : MonoBehaviour
    {
        [SerializeField] private FloatingJoystick joystick;
        [SerializeField, Min(0f)] private float maxSpeed = 5f;
        [Tooltip("Stick travel (0..1) that gives full speed. Archero-like: small drags already run.")]
        [SerializeField, Range(0.1f, 1f)] private float fullSpeedAt = 0.45f;
        [SerializeField, Min(0f)] private float acceleration = 40f;
        [SerializeField, Min(0f)] private float turnSpeed = 900f; // degrees per second

        private CharacterController _controller;
        private PlayerStats _stats;
        private Vector3 _velocity; // planar
        private float _verticalSpeed;
        private float _slow;

        public float MaxSpeed => Mathf.Min(PlayerStats.Eval(_stats, StatId.MoveSpeed, maxSpeed), maxSpeed * MaxSpeedShare) * (1f - Slow);
        /// <summary>KAN-246 ceiling: skills and meta bonuses never push the run speed past this share of the base (touch precision).</summary>
        public const float MaxSpeedShare = 1.6f;
        /// <summary>
        /// Share of the speed taken away (0 = none, at most 0.9): acid puddles (KAN-105). Set every frame by
        /// <see cref="Combat.GroundHazards"/>; 0 when the Knight stands in no slowing hazard.
        /// </summary>
        public float Slow
        {
            get => _slow;
            set => _slow = Mathf.Clamp(value, 0f, 0.9f);
        }
        /// <summary>Base speed before skills (KAN-75 power curve).</summary>
        public float BaseSpeed => maxSpeed;
        /// <summary>The stick is ignored (the Knight slows to a stop): boss intro (KAN-62). Gravity still runs.</summary>
        public bool InputLocked { get; set; }
        public Vector3 Velocity => _velocity;

        public void SetJoystick(FloatingJoystick value) => joystick = value;

        /// <summary>
        /// Moves the Knight by <paramref name="offset"/> through the CharacterController (walls and pillars stop it): the
        /// Butcher's chain hook pulls it (KAN-108).
        /// </summary>
        public void Shove(Vector3 offset)
        {
            if (_controller == null) _controller = GetComponent<CharacterController>();
            offset.y = 0f;
            if (_controller != null && _controller.enabled) _controller.Move(offset);
            else transform.position += offset;
        }

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _stats = GetComponent<PlayerStats>();
        }

        // Disabled (e.g. on death): report standing still, so footsteps, markers and aim stop at once.
        private void OnDisable() => _velocity = Vector3.zero;

        private void Update()
        {
            float dt = Time.deltaTime;
            Vector2 stick = joystick != null && !InputLocked ? joystick.Move : Vector2.zero;

            float magnitude = Mathf.Clamp01(stick.magnitude / fullSpeedAt);
            Vector3 desired = stick.sqrMagnitude > 0f
                ? new Vector3(stick.x, 0f, stick.y).normalized * (magnitude * MaxSpeed)
                : Vector3.zero;
            _velocity = Vector3.MoveTowards(_velocity, desired, acceleration * dt);

            // Simple gravity keeps the controller grounded on uneven floors.
            _verticalSpeed = _controller.isGrounded ? -1f : _verticalSpeed - 20f * dt;
            _controller.Move((_velocity + Vector3.up * _verticalSpeed) * dt);

            if (desired.sqrMagnitude > 0.01f)
            {
                var look = Quaternion.LookRotation(desired, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, look, turnSpeed * dt);
            }
        }
    }
}
