using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills.Effects
{
    /// <summary>
    /// "Sentinel Halo" (KAN-246, Epic): <see cref="StatId.HaloCount"/> shield fragments circle close to the Knight and stop
    /// enemy projectiles that come within <see cref="blockRadius"/> + <see cref="StatId.HaloReach"/> m of a fragment
    /// (<see cref="EnemyProjectiles.Intercept"/>, <see cref="BossProjectiles.Intercept"/>). Projectiles only: no contact
    /// damage, ground hazards, contact bites or boss beams. The fragments deal no damage. Look: gold-white plates with a soft
    /// glow and a bright chip burst on each block.
    /// </summary>
    public sealed class SentinelHaloEffect : OrbitRing
    {
        [SerializeField, Min(0.5f)] private float radius = 1.35f;
        [SerializeField, Min(0.3f)] private float spin = 2.2f;
        [SerializeField, Min(0.1f)] private float blockRadius = 0.55f;

        private static readonly Color Chip = new Color(1f, 0.92f, 0.62f, 1f);

        protected override int BodyCount => Mathf.RoundToInt(PlayerStats.Eval(Context?.Stats, StatId.HaloCount, 0f));
        public override float Radius => radius;
        public override float SecondsPerTurn => spin;
        protected override bool HitsOnContact => false;
        protected override int FormationSlot => 4;
        /// <summary>Block reach of one fragment (m).</summary>
        public float BlockReach => blockRadius + Mathf.Max(0f, PlayerStats.Eval(Context?.Stats, StatId.HaloReach, 0f));
        /// <summary>Projectiles stopped this run (tests).</summary>
        public int Blocked { get; private set; }

        protected override void AfterMove(float dt, int count)
        {
            var shots = EnemyProjectiles.Current;
            var boss = BossProjectiles.Live;
            if (shots == null && boss.Count == 0) return;
            float reach = BlockReach;
            for (int i = 0; i < count; i++)
            {
                if (bodies[i] == null) continue;
                Vector3 at = bodies[i].position;
                int stopped = shots != null ? shots.Intercept(at, reach) : 0;
                for (int k = 0; k < boss.Count; k++) stopped += boss[k].Intercept(at, reach);
                if (stopped <= 0) continue;
                Blocked += stopped;
                SkillFx.Flash(fx, at, 1.1f, Chip, 0.12f);
                SkillFx.Sparks(fx, at, 8, Chip, 3.5f, 0.09f, 0.25f);
            }
        }

        protected override Quaternion BodyRotation(int index, float angle, Vector3 tangent)
        {
            // Plate faces outward (its normal along the radius), like a shield held toward the crowd.
            Vector3 outward = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
            return Quaternion.LookRotation(outward, Vector3.up);
        }
    }
}
