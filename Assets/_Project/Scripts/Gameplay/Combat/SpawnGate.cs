using System.Collections.Generic;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Designated entry point for enemies (KAN-29): a gap in a wall, a ship ramp, a ruin archway, a cave mouth.
    /// Chambers with hard edges (shore, boss arena) place these; <see cref="EnemySpawner"/> treats every enabled gate as a
    /// spawn candidate next to its ring points, and still applies the off-screen, distance and walkability tests.
    /// An enemy appears on the gate and walks in along its forward (blue) axis.
    /// </summary>
    public sealed class SpawnGate : MonoBehaviour
    {
        /// <summary>Enabled gates. Read-only for other systems.</summary>
        public static readonly List<SpawnGate> Active = new List<SpawnGate>();

        [Tooltip("Random offset (m) around the gate, so a group does not stack on one point.")]
        [SerializeField, Min(0f)] private float spread = 1f;
        [Tooltip("Relative chance against other valid candidates.")]
        [SerializeField, Min(0f)] private float weight = 1.5f;

        public float Spread => spread;
        public float Weight => weight;

        private void OnEnable() => Active.Add(this);
        private void OnDisable() => Active.Remove(this);

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, Mathf.Max(0.3f, spread));
            Gizmos.DrawLine(transform.position, transform.position + transform.forward * 2f);
        }
    }
}
