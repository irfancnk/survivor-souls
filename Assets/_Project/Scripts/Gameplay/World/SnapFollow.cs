using UnityEngine;

namespace SurvivorSouls.Gameplay.World
{
    /// <summary>
    /// Keeps a large ground quad (or an FX emitter) under the target, snapped to a grid in X/Z.
    /// With grid = texture tile size, a world-tiled ground texture never swims: one draw call, endless ground.
    /// </summary>
    public sealed class SnapFollow : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField, Min(0.01f)] private float grid = 4f;
        [SerializeField] private float y;

        public Transform Target
        {
            get => target;
            set => target = value;
        }

        private void LateUpdate()
        {
            if (target == null) return;
            var p = target.position;
            transform.position = new Vector3(Mathf.Round(p.x / grid) * grid, y, Mathf.Round(p.z / grid) * grid);
        }
    }
}
