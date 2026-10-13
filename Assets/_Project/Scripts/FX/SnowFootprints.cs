using SurvivorSouls.Gameplay.Player;
using UnityEngine;

namespace SurvivorSouls.FX
{
    /// <summary>
    /// Leaves boot prints in the snow behind the walking character: every half stride one print, left and right
    /// feet alternate. Prints are particles of one world-space system (one draw call, fade via colour over life).
    /// </summary>
    public sealed class SnowFootprints : MonoBehaviour
    {
        [SerializeField] private PlayerMover mover;
        [SerializeField] private ParticleSystem prints;
        [Tooltip("Distance between two prints (one step), metres.")]
        [SerializeField, Min(0.1f)] private float step = 0.75f;
        [SerializeField, Min(0f)] private float footSpacing = 0.13f;
        [SerializeField] private Vector2 size = new Vector2(0.26f, 0.4f);

        private Vector3 _last;
        private float _travelled;
        private bool _left;

        private void OnEnable() => _last = transform.position;

        private void LateUpdate()
        {
            Vector3 p = transform.position;
            Vector3 delta = p - _last;
            delta.y = 0f;
            _last = p;
            if (prints == null || (mover != null && mover.Velocity.sqrMagnitude < 0.25f)) return;

            _travelled += delta.magnitude;
            if (_travelled < step) return;
            _travelled = 0f;
            _left = !_left;

            Vector3 forward = delta.sqrMagnitude > 1e-6f ? delta.normalized : transform.forward;
            Vector3 side = Vector3.Cross(Vector3.up, forward) * (_left ? -footSpacing : footSpacing);
            var emit = new ParticleSystem.EmitParams
            {
                position = new Vector3(p.x, 0.02f, p.z) + side,
                rotation = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg,
                startSize3D = new Vector3(size.x, size.y, 1f),
                applyShapeToPosition = false,
            };
            prints.Emit(emit, 1);
        }
    }
}
