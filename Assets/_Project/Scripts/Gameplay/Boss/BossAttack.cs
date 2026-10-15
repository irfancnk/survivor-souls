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
    }
}
