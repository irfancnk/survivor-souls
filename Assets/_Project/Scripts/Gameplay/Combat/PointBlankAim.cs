using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Point-blank shot line (KAN-224). The muzzle sits ~0.5 m in front of the Knight, so an enemy that hugs the Knight has
    /// its aim point behind the muzzle: the plain muzzle-to-target line points back through the Knight, the bolt flies
    /// backwards and the upper-body aim twists to its yaw limit.
    /// Rule: measured along the flat line from the shooter root to the target, the aim point must be at least
    /// <see cref="MinAhead"/> in front of the muzzle. When it is not, the bolt starts that much further back (inside the
    /// body, along the same flat line) and the crossbow aims at a point pushed forward by the same shift. Both lines are then
    /// the same vector (<c>target - (muzzle - shift) == (target + shift) - muzzle</c>), so the crossbow line stays on the bolt
    /// line, the bolt points forward and still crosses the hugging enemy on its first step.
    /// Every shot with the target further than <see cref="MinAhead"/> past the muzzle is unchanged (shift 0, the KAN-215 rule).
    /// </summary>
    public static class PointBlankAim
    {
        /// <summary>Least distance (m) the aim point must lie in front of the muzzle along the root-to-target line.</summary>
        public const float MinAhead = 0.3f;

        /// <summary>
        /// The shift (world, flat) to pull the bolt start back and push the crossbow aim point forward. Zero for a normal shot.
        /// </summary>
        public static Vector3 Shift(Vector3 root, Vector3 muzzle, Vector3 target)
        {
            Vector3 f = target - root;
            f.y = 0f;
            float len = f.magnitude;
            if (len < 1e-4f) return Vector3.zero; // target on the root axis: no facing to measure along
            f /= len;
            Vector3 m = target - muzzle;
            float ahead = m.x * f.x + m.z * f.z;
            return ahead >= MinAhead ? Vector3.zero : f * (MinAhead - ahead);
        }

        /// <summary>Point the crossbow line should aim at: the target, or the target pushed forward by the point-blank shift.</summary>
        public static Vector3 AimPoint(Vector3 root, Vector3 muzzle, Vector3 target) => target + Shift(root, muzzle, target);
    }
}
