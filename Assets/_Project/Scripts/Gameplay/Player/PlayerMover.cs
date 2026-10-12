using SurvivorSouls.Gameplay.Controls;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Player
{
    /// <summary>
    /// Moves the player on the XZ plane from the joystick. The camera has a fixed yaw of 0,
    /// so stick up = world +Z. The character turns to face the move direction.
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
        private Vector3 _velocity; // planar
        private float _verticalSpeed;

        public float MaxSpeed => maxSpeed;
        public Vector3 Velocity => _velocity;

        public void SetJoystick(FloatingJoystick value) => joystick = value;

        private void Awake() => _controller = GetComponent<CharacterController>();

        private void Update()
        {
            float dt = Time.deltaTime;
            Vector2 stick = joystick != null ? joystick.Value : Vector2.zero;

            float magnitude = Mathf.Clamp01(stick.magnitude / fullSpeedAt);
            Vector3 desired = stick.sqrMagnitude > 0f
                ? new Vector3(stick.x, 0f, stick.y).normalized * (magnitude * maxSpeed)
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
