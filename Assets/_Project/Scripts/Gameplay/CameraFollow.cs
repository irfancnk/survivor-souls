using UnityEngine;

namespace SurvivorSouls.Gameplay
{
    /// <summary>
    /// Archero-style high angle follow camera for portrait. Fixed rotation, smoothed position.
    /// The look point leads the target up-screen so the hero sits in the lower half, with room to see what comes.
    /// </summary>
    public sealed class CameraFollow : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 offset = new Vector3(0f, 13f, -7.5f);
        [SerializeField] private float lookAhead = 1.5f;
        [SerializeField, Min(0f)] private float smoothTime = 0.12f;

        private Vector3 _velocity;

        public void Configure(Transform followTarget)
        {
            target = followTarget;
            SnapToTarget();
        }

        public void SnapToTarget()
        {
            if (target == null) return;
            transform.position = target.position + offset;
            transform.LookAt(target.position + Vector3.forward * lookAhead);
        }

        private void LateUpdate()
        {
            if (target == null) return;
            Vector3 goal = target.position + offset;
            transform.position = Vector3.SmoothDamp(transform.position, goal, ref _velocity, smoothTime);
        }
    }
}
