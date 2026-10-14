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

        public float MaxSpeed => PlayerStats.Eval(_stats, StatId.MoveSpeed, maxSpeed);
        /// <summary>The stick is ignored (the Knight slows to a stop): boss intro (KAN-62). Gravity still runs.</summary>
        public bool InputLocked { get; set; }
        public Vector3 Velocity => _velocity;

        public void SetJoystick(FloatingJoystick value) => joystick = value;

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
