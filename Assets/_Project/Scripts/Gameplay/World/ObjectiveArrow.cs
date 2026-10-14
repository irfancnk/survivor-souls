using SurvivorSouls.Gameplay.Chamber;
using UnityEngine;

namespace SurvivorSouls.Gameplay.World
{
    /// <summary>
    /// Golden arrow on the ground that circles the player and points to the exit door.
    /// Hides when the player is close to the door (the door has its own floating marker).
    /// In the boss arena (KAN-59) it points to the arena portal instead, and only once that portal is open
    /// (<see cref="PointAt"/>): during the fight the way out is shut, so the arrow would only distract.
    /// </summary>
    public sealed class ObjectiveArrow : MonoBehaviour
    {
        [SerializeField] private WorldStreamer world;
        [SerializeField] private Transform arrow;
        [SerializeField, Min(0f)] private float radius = 1.9f;
        [SerializeField, Min(0f)] private float hideDistance = 7f;
        [SerializeField, Min(0f)] private float pulse = 0.08f;

        private Vector3 _baseScale;
        private PortalGate _gate;
        private bool _gated;

        /// <summary>Whether the arrow shows this frame.</summary>
        public bool Showing => arrow != null && arrow.gameObject.activeSelf;

        private void Awake() => _baseScale = arrow.localScale;

        /// <summary>Points at <paramref name="gate"/> (boss arena) and shows only while it is open. Null = back to the world exit.</summary>
        public void PointAt(PortalGate gate)
        {
            _gate = gate;
            _gated = gate != null;
        }

        /// <summary>Tests: wiring without the builder.</summary>
        public void Setup(WorldStreamer worldStreamer, Transform arrowMesh)
        {
            world = worldStreamer;
            arrow = arrowMesh;
            _baseScale = arrow.localScale;
        }

        private void LateUpdate()
        {
            bool open = !_gated || (_gate != null && _gate.State == PortalState.Open);
            Vector3 target = _gated ? (_gate != null ? _gate.TriggerPosition : transform.position) : world != null ? world.ExitPosition : transform.position;
            Vector3 to = target - transform.position;
            to.y = 0f;
            bool show = open && to.magnitude > hideDistance;
            if (arrow.gameObject.activeSelf != show) arrow.gameObject.SetActive(show);
            if (!show) return;

            Vector3 dir = to.normalized;
            arrow.SetPositionAndRotation(transform.position + dir * radius + Vector3.up * 0.06f, Quaternion.LookRotation(dir, Vector3.up));
            arrow.localScale = _baseScale * (1f + pulse * Mathf.Sin(Time.time * 5f));
        }
    }
}
