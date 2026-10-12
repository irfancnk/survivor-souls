using UnityEngine;

namespace SurvivorSouls.Gameplay.Characters
{
    /// <summary>
    /// Feeds an Animator from the character's own motion: float "Speed" (planar m/s, damped).
    /// The controller blends Idle / Walk / Run on Speed (see MeshyCharacterImporter). Works with any mover.
    /// Call <see cref="PlayHit"/> for the hit reaction (trigger "Hit").
    /// </summary>
    [DefaultExecutionOrder(100)] // after movement
    public sealed class LocomotionAnimatorDriver : MonoBehaviour
    {
        public static readonly int SpeedId = Animator.StringToHash("Speed");
        public static readonly int HitId = Animator.StringToHash("Hit");

        [SerializeField] private Animator animator;
        [SerializeField, Min(0f)] private float speedDampTime = 0.08f;

        private Vector3 _lastPosition;

        public void PlayHit() => animator.SetTrigger(HitId);

        private void Awake()
        {
            if (animator == null) animator = GetComponentInChildren<Animator>();
            _lastPosition = transform.position;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            if (dt <= 0f) return;
            Vector3 delta = transform.position - _lastPosition;
            delta.y = 0f;
            _lastPosition = transform.position;
            animator.SetFloat(SpeedId, delta.magnitude / dt, speedDampTime, dt);
        }
    }
}
