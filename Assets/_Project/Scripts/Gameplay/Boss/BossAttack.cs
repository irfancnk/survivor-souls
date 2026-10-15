using System;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Boss
{
    /// <summary>Boss phases an attack is used in (KAN-108).</summary>
    [Flags]
    public enum BossPhases
    {
        Phase1 = 1,
        Phase2 = 2,
        Both = Phase1 | Phase2,
    }

    /// <summary>
    /// One entry of a boss's attack list (KAN-108). <see cref="BossFight"/> picks the next attack from the entries that are
    /// on in the current phase and whose range holds the Knight: the highest <see cref="priority"/> band wins, and inside a
    /// band the score weight x (attacks since this one was last used) picks, so attacks of one band take turns and a high
    /// weight comes round more often. Every wind-up is at least <see cref="BossData.MinTelegraph"/> s. Damage is a share of
    /// the Knight's max health, capped by <see cref="BossData"/>'s max-hit share. Which numbers a type reads:
    /// <list type="bullet">
    /// <item><b>Slam</b>: circle of <see cref="radius"/> m, centre <see cref="reach"/> m in front of the boss.</item>
    /// <item><b>Fan</b>: <see cref="count"/> bolts over <see cref="spread"/> degrees, <see cref="speed"/> m/s, <see cref="length"/> m,
    /// lane <see cref="width"/>, bolt hit radius <see cref="radius"/>. One volley hits once.</item>
    /// <item><b>Charge</b>: dash <see cref="length"/> m at <see cref="speed"/> m/s, lane <see cref="width"/>; <see cref="count"/>
    /// dashes in a row (each warns again); a dash stopped by a pillar stuns the boss <see cref="stun"/> s and ends the combo.</item>
    /// <item><b>Lane</b>: rectangle <see cref="length"/> x <see cref="width"/> from the boss to the Knight (cleaver chop, chain
    /// hook). A pillar shortens it. A hit with <see cref="pull"/> above 0 drags the Knight that far towards the boss.</item>
    /// <item><b>Spin</b>: circle of <see cref="radius"/> m around the boss for <see cref="duration"/> s; the boss follows the Knight
    /// at <see cref="speed"/> m/s; a hit at most every <see cref="interval"/> s.</item>
    /// <item><b>Nova</b> (KAN-116): <see cref="count"/> bolts in a full ring from the body, <see cref="gaps"/> gaps of two
    /// missing bolts (evenly spaced, random turn per cast), <see cref="speed"/> m/s, <see cref="length"/> m, bolt hit radius
    /// <see cref="radius"/>. Warning: a circle of <see cref="reach"/> m around the body. One ring hits once.</item>
    /// <item><b>Spiral</b> (KAN-116): <see cref="count"/> arms; every <see cref="interval"/> s each arm fires a bolt, the arms
    /// turn <see cref="spread"/> degrees per second, for <see cref="duration"/> s. Bolt numbers and warning as Nova. The whole
    /// spiral hits once (walk through the gaps between the arms).</item>
    /// <item><b>Rain</b> (KAN-116): <see cref="count"/> ground circles of <see cref="radius"/> m, one every
    /// <see cref="interval"/> s, each within <see cref="reach"/> m of where the Knight stands then (the first right on it).
    /// Each warns <see cref="telegraph"/> s, then hits inside its circle. Circles come from the shared
    /// <see cref="SurvivorSouls.Gameplay.Combat.EnemyTelegraphs"/> pool.</item>
    /// </list>
    /// </summary>
    [Serializable]
    public struct BossAttack
    {
        [Tooltip("Name in logs, the debug menu and tests.")]
        public string label;
        public BossFight.Move type;
        public BossPhases phases;
        [Tooltip("Higher bands are picked first when their range holds the Knight (e.g. melee up close over ranged).")]
        public int priority;
        [Tooltip("Inside a band: weight x attacks since last use. Equal weights take turns.")]
        [Min(0.01f)] public float weight;
        [Tooltip("The attack starts only with the Knight between these distances (m).")]
        [Min(0f)] public float minRange;
        [Min(0f)] public float maxRange;
        [Tooltip("Seconds the warning fills before the hit. Never below 0.6 s.")]
        [Min(BossData.MinTelegraph)] public float telegraph;
        [Tooltip("Seconds the boss stands still after the hit.")]
        [Min(0f)] public float recover;
        [Tooltip("Seconds of chase after this attack before the next one.")]
        [Min(0f)] public float cooldown;
        [Tooltip("Damage as a share of the Knight's max health.")]
        [Range(0f, 1f)] public float damage;
        [Min(0f)] public float radius;
        [Min(0f)] public float reach;
        [Min(0f)] public float length;
        [Min(0f)] public float width;
        [Min(0f)] public float speed;
        [Min(0f)] public float spread;
        [Min(0)] public int count;
        [Min(0f)] public float duration;
        [Min(0f)] public float interval;
        [Min(0f)] public float pull;
        [Min(0f)] public float stun;
        [Tooltip("Nova: gaps in the ring (two bolts missing each).")]
        [Min(0)] public int gaps;
        [Tooltip("Animator state the body plays for this attack (wind-up and strike). Empty or missing = its Attack state.")]
        public string clip;

        public bool InPhase(int phase) => (phases & (phase >= 2 ? BossPhases.Phase2 : BossPhases.Phase1)) != 0;

        public bool InRange(float distance) => distance >= minRange && distance <= maxRange;

        /// <summary>Wind-up with the 0.6 s floor (the phase 2 factor comes on top, <see cref="BossData.TelegraphFor"/>).</summary>
        public float Telegraph => Mathf.Max(BossData.MinTelegraph, telegraph);

        public override string ToString() => string.IsNullOrEmpty(label) ? type.ToString() : label;

        // ------------------------------------------------------------ Factories (builder, legacy Warden, tests)

        public static BossAttack Slam(string label, BossPhases phases, float range, float radius, float reach, float telegraph,
            float damage, float recover, float cooldown, int priority = 1, string clip = null) =>
            new BossAttack
            {
                label = label, type = BossFight.Move.Slam, phases = phases, priority = priority, weight = 1f,
                minRange = 0f, maxRange = range, radius = radius, reach = reach, telegraph = telegraph, damage = damage,
                recover = recover, cooldown = cooldown, clip = clip,
            };

        public static BossAttack Fan(string label, BossPhases phases, float maxRange, int bolts, float spread, float telegraph,
            float speed, float distance, float laneWidth, float hitRadius, float damage, float recover, float cooldown,
            float weight = 1f, string clip = null) =>
            new BossAttack
            {
                label = label, type = BossFight.Move.Fan, phases = phases, weight = weight, minRange = 0f, maxRange = maxRange,
                count = bolts, spread = spread, telegraph = telegraph, speed = speed, length = distance, width = laneWidth,
                radius = hitRadius, damage = damage, recover = recover, cooldown = cooldown, clip = clip,
            };

        public static BossAttack Charge(string label, BossPhases phases, float minRange, float maxRange, float telegraph,
            float speed, float distance, float width, float damage, float recover, float cooldown, int dashes = 1,
            float stun = 0f, float weight = 1f, string clip = null) =>
            new BossAttack
            {
                label = label, type = BossFight.Move.Charge, phases = phases, weight = weight, minRange = minRange,
                maxRange = maxRange, telegraph = telegraph, speed = speed, length = distance, width = width, damage = damage,
                recover = recover, cooldown = cooldown, count = Mathf.Max(1, dashes), stun = stun, clip = clip,
            };

        public static BossAttack Lane(string label, BossPhases phases, float minRange, float maxRange, float length, float width,
            float telegraph, float damage, float recover, float cooldown, float pull = 0f, int priority = 0, float weight = 1f,
            string clip = null) =>
            new BossAttack
            {
                label = label, type = BossFight.Move.Lane, phases = phases, priority = priority, weight = weight,
                minRange = minRange, maxRange = maxRange, length = length, width = width, telegraph = telegraph,
                damage = damage, recover = recover, cooldown = cooldown, pull = pull, clip = clip,
            };

        public static BossAttack Spin(string label, BossPhases phases, float maxRange, float radius, float telegraph,
            float duration, float speed, float interval, float damage, float recover, float cooldown, int priority = 0,
            float weight = 1f, string clip = null) =>
            new BossAttack
            {
                label = label, type = BossFight.Move.Spin, phases = phases, priority = priority, weight = weight,
                minRange = 0f, maxRange = maxRange, radius = radius, telegraph = telegraph, duration = duration, speed = speed,
                interval = interval, damage = damage, recover = recover, cooldown = cooldown, clip = clip,
            };

        public static BossAttack Nova(string label, BossPhases phases, float maxRange, int bolts, int gaps, float warnRadius,
            float telegraph, float speed, float distance, float hitRadius, float damage, float recover, float cooldown,
            int priority = 0, float weight = 1f, string clip = null) =>
            new BossAttack
            {
                label = label, type = BossFight.Move.Nova, phases = phases, priority = priority, weight = weight,
                minRange = 0f, maxRange = maxRange, count = bolts, gaps = gaps, reach = warnRadius, telegraph = telegraph,
                speed = speed, length = distance, radius = hitRadius, damage = damage, recover = recover, cooldown = cooldown,
                clip = clip,
            };

        public static BossAttack Spiral(string label, BossPhases phases, float maxRange, int arms, float turnSpeed, float duration,
            float interval, float warnRadius, float telegraph, float speed, float distance, float hitRadius, float damage,
            float recover, float cooldown, int priority = 0, float weight = 1f, string clip = null) =>
            new BossAttack
            {
                label = label, type = BossFight.Move.Spiral, phases = phases, priority = priority, weight = weight,
                minRange = 0f, maxRange = maxRange, count = arms, spread = turnSpeed, duration = duration, interval = interval,
                reach = warnRadius, telegraph = telegraph, speed = speed, length = distance, radius = hitRadius, damage = damage,
                recover = recover, cooldown = cooldown, clip = clip,
            };

        public static BossAttack Rain(string label, BossPhases phases, float minRange, float maxRange, int circles, float radius,
            float scatter, float interval, float telegraph, float damage, float recover, float cooldown, int priority = 0,
            float weight = 1f, string clip = null) =>
            new BossAttack
            {
                label = label, type = BossFight.Move.Rain, phases = phases, priority = priority, weight = weight,
                minRange = minRange, maxRange = maxRange, count = circles, radius = radius, reach = scatter, interval = interval,
                telegraph = telegraph, damage = damage, recover = recover, cooldown = cooldown, clip = clip,
            };

        /// <summary>
        /// Pyre Lord style numbers (Docs/ENEMY_ROSTER.md 3.13) for a type: the debug menu and dev screenshots add it to a
        /// boss that lacks that type; KAN-109 starts its list from these. Other types: a plain slam.
        /// </summary>
        public static BossAttack Sample(BossFight.Move type)
        {
            switch (type)
            {
                case BossFight.Move.Nova:
                    return Nova("Fire Nova", BossPhases.Both, 9f, bolts: 16, gaps: 0, warnRadius: 3f, telegraph: 0.9f, speed: 7f,
                        distance: 14f, hitRadius: 0.6f, damage: 0.14f, recover: 0.8f, cooldown: 3f);
                case BossFight.Move.Spiral:
                    return Spiral("Fire Spiral", BossPhases.Phase2, 12f, arms: 2, turnSpeed: 110f, duration: 3f, interval: 0.14f,
                        warnRadius: 3f, telegraph: 1f, speed: 6.5f, distance: 14f, hitRadius: 0.55f, damage: 0.16f, recover: 1f,
                        cooldown: 3.5f);
                case BossFight.Move.Rain:
                    return Rain("Meteor Rain", BossPhases.Phase2, 3f, 16f, circles: 6, radius: 1.6f, scatter: 2.5f, interval: 0.35f,
                        telegraph: 1f, damage: 0.15f, recover: 0.8f, cooldown: 3.5f);
                default:
                    return Slam("Slam", BossPhases.Both, 4f, 3f, 1.6f, 1f, 0.22f, 0.7f, 3.2f);
            }
        }
    }
}
