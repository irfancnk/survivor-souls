using SurvivorSouls.Gameplay.Skills;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Player
{
    /// <summary>
    /// The player's soul pickup stats (KAN-21). <see cref="PickupRadius"/> is where wisps start flying to the Knight;
    /// pickup radius skills raise it through <see cref="PlayerStats"/> (KAN-19), other code through <see cref="RadiusMultiplier"/>.
    /// Disabled on death: wisps then stay where they are.
    /// </summary>
    public sealed class SoulCollector : MonoBehaviour
    {
        [Tooltip("Planar distance (m) at which a resting wisp starts flying to the player.")]
        [SerializeField, Min(0f)] private float baseRadius = 2.6f;
        [Tooltip("Height above the feet (m) where wisps are absorbed (chest).")]
        [SerializeField, Min(0f)] private float absorbHeight = 1.0f;

        private PlayerStats _stats;

        /// <summary>Extra multiplier on <see cref="BaseRadius"/> (non-skill sources). 1 = no bonus.</summary>
        public float RadiusMultiplier { get; set; } = 1f;
        public float BaseRadius => baseRadius;
        public float PickupRadius => Mathf.Max(0f, PlayerStats.Eval(_stats, StatId.PickupRadius, baseRadius * Mathf.Max(0f, RadiusMultiplier)));

        private void Awake() => _stats = GetComponent<PlayerStats>();
        public Vector3 AbsorbPoint => transform.position + Vector3.up * absorbHeight;

        /// <summary>Wiring for code-built objects and tests.</summary>
        public void Setup(float radius, float height = 1f)
        {
            baseRadius = Mathf.Max(0f, radius);
            absorbHeight = Mathf.Max(0f, height);
        }
    }
}
