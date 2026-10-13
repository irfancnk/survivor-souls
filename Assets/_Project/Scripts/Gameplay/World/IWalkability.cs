using UnityEngine;

namespace SurvivorSouls.Gameplay.World
{
    /// <summary>
    /// Answers "can a body of this radius stand here?" for the play area, without physics queries.
    /// The enemy spawner uses it to reject spawn points in walls, props or outside the bounds (KAN-29), and
    /// enemies use it for cheap wall-slide steering. Implemented by <see cref="WorldStreamer"/> (open world) and later
    /// by Chamber layouts (shore, boss arena).
    /// </summary>
    public interface IWalkability
    {
        /// <summary>True when a circle of <paramref name="radius"/> m on the ground at <paramref name="position"/> touches an obstacle or leaves the play area.</summary>
        bool IsBlocked(Vector3 position, float radius);
    }
}
