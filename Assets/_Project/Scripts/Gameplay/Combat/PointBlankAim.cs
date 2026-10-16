using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Point-blank shot line (KAN-224). The muzzle sits ~0.6-0.8 m in front of the Knight, so an enemy that hugs the Knight
    /// has its aim point behind the muzzle: the plain muzzle-to-target line points back through the Knight, the bolt flies
    /// backwards and the upper-body aim twists to its yaw limit. A target a little in front of the muzzle is also a problem:
    /// the muzzle moves with the chest turn, so the aim solve cannot put the crossbow line on a point a few dm away.
    /// Rule: a target closer to the shooter root than <see cref="Range"/> (flat) is point-blank.
    /// - The bolt starts on the flat root-to-target line at the target aim height, level with the muzzle along that line or
    ///   at least <see cref="MinAhead"/> behind the target (<see cref="Start"/>; inside the body when the enemy hugs the
    ///   Knight, seen for one frame at most), and flies level from there at the target: forward, and it hits the enemy at its
    ///   aim height (short enemies too).
    /// - The crossbow aims at a far point (<see cref="AimReach"/>) beyond the target along that level forward line: the root
    ///   already faces the target, so the upper body barely turns and the solve is stable. The crossbow line is within a few
    ///   degrees of the bolt line (the muzzle height over <see cref="AimReach"/>). Not the muzzle-height pitch: the muzzle
    ///   before the turn is wherever the clip holds it, and over half a metre a few cm of height are many degrees.
    /// The decision uses the root, not the muzzle, because the muzzle before the aim turn is wherever the clip holds it.
    /// Every target at <see cref="Range"/> or further is unchanged (the KAN-215 3 deg rule).
    /// </summary>
    public static class PointBlankAim
    {
        /// <summary>Flat root-to-target distance (m) under which a shot is point-blank.</summary>
        public const float Range = 1.5f;

        /// <summary>Least distance (m) the target must lie in front of the bolt start along the root-to-target line.</summary>
        public const float MinAhead = 0.5f;

        /// <summary>How far (m) beyond a point-blank target the crossbow aims.</summary>
        public const float AimReach = 6f;

        /// <summary>True when the target is closer to the root than <see cref="Range"/> (flat).</summary>
        public static bool IsPointBlank(Vector3 root, Vector3 target)
        {
            float x = target.x - root.x, z = target.z - root.z;
            return x * x + z * z < Range * Range;
        }

        /// <summary>
        /// Point the crossbow line aims at: the target for a normal shot, else the point <see cref="AimReach"/> beyond it along
        /// the flat root-to-target line (at the target height).
        /// </summary>
        public static Vector3 AimPoint(Vector3 root, Vector3 target)
        {
            if (!IsPointBlank(root, target)) return target;
            Vector3 f = target - root;
            f.y = 0f;
            float len = f.magnitude;
            return len < 1e-4f ? target : target + f * (AimReach / len);
        }

        /// <summary>
        /// Bolt start: the muzzle for a normal shot. For a point-blank target the point on the flat root-to-target line at the
        /// target height (a level bolt), as far behind the target as the muzzle is, and at least <see cref="MinAhead"/>.
        /// </summary>
        public static Vector3 Start(Vector3 root, Vector3 muzzle, Vector3 target)
        {
            if (!IsPointBlank(root, target)) return muzzle;
            Vector3 f = target - root;
            f.y = 0f;
            float len = f.magnitude;
            if (len < 1e-4f) return muzzle; // target on the root axis: no facing to measure along
            f /= len;
            Vector3 m = target - muzzle;
            return target - f * Mathf.Max(MinAhead, m.x * f.x + m.z * f.z);
        }
    }
}
