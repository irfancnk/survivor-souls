using UnityEngine;

namespace SurvivorSouls.Gameplay
{
    /// <summary>
    /// Moves the hero on the arena floor from the joystick. Move = dodge; standing still lets
    /// PlayerAutoAttack fire. No physics: the arena is a flat disc, so a radius clamp is enough.
    /// </summary>
    [RequireComponent(typeof(Health))]
    public sealed class PlayerController : MonoBehaviour
    {
        [SerializeField] private VirtualJoystick joystick;
        [SerializeField, Min(0f)] private float moveSpeed = 5f;
        [SerializeField, Min(0f)] private float turnSpeed = 900f;
        [SerializeField, Min(1f)] private float arenaRadius = 14f;

        private Health _health;

        /// <summary>Resolved lazily: other objects' OnEnable can run before this Awake.</summary>
        public Health Health => _health != null ? _health : _health = GetComponent<Health>();
        public bool IsMoving { get; private set; }

        public void Configure(VirtualJoystick input, float radius)
        {
            joystick = input;
            arenaRadius = radius;
        }

        private void Update()
        {
            Vector2 input = Health.IsDead || joystick == null ? Vector2.zero : joystick.Value;
            IsMoving = input != Vector2.zero;
            if (!IsMoving) return;

            // Camera looks along +Z, so screen up = world +Z.
            var dir = new Vector3(input.x, 0f, input.y);
            Vector3 pos = transform.position + dir * (moveSpeed * Time.deltaTime);
            pos.y = 0f;
            Vector3 flat = Vector3.ClampMagnitude(pos, arenaRadius);
            transform.position = flat;
            TurnTowards(dir);
        }

        public void FaceTowards(Vector3 worldPoint)
        {
            Vector3 dir = worldPoint - transform.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.0001f) TurnTowards(dir);
        }

        private void TurnTowards(Vector3 dir)
        {
            var target = Quaternion.LookRotation(dir.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, turnSpeed * Time.deltaTime);
        }
    }
}
