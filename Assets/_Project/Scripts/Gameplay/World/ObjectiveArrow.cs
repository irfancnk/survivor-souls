using UnityEngine;

namespace SurvivorSouls.Gameplay.World
{
    /// <summary>
    /// Golden arrow on the ground that circles the player and points to the exit door.
    /// Hides when the player is close to the door (the door has its own floating marker).
    /// </summary>
    public sealed class ObjectiveArrow : MonoBehaviour
    {
        [SerializeField] private WorldStreamer world;
        [SerializeField] private Transform arrow;
        [SerializeField, Min(0f)] private float radius = 1.9f;
        [SerializeField, Min(0f)] private float hideDistance = 7f;
        [SerializeField, Min(0f)] private float pulse = 0.08f;

        private Vector3 _baseScale;

        private void Awake() => _baseScale = arrow.localScale;

        private void LateUpdate()
        {
            Vector3 to = world.ExitPosition - transform.position;
            to.y = 0f;
            bool show = to.magnitude > hideDistance;
            if (arrow.gameObject.activeSelf != show) arrow.gameObject.SetActive(show);
            if (!show) return;

            Vector3 dir = to.normalized;
            arrow.SetPositionAndRotation(transform.position + dir * radius + Vector3.up * 0.06f, Quaternion.LookRotation(dir, Vector3.up));
            arrow.localScale = _baseScale * (1f + pulse * Mathf.Sin(Time.time * 5f));
        }
    }
}
