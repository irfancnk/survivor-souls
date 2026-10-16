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
    /// <item><b>Lobbed</b> (KAN-119): <see cref="count"/> boulders, one every <see cref="interval"/> s, each lobbed at where
    /// the Knight stands then; flight <see cref="duration"/> s (0.6 s floor), landing circle of <see cref="radius"/> m (shown
    /// from the throw; the first one already during the wind-up). A boulder that lands leaves rubble of <see cref="width"/> m
    /// radius for <see cref="obstacle"/> s (0 = none): it blocks the Knight, enemies, bolts and charges. Each boulder hits once.</item>
    /// <item><b>Summon</b> (KAN-119): <see cref="count"/> enemies of type <see cref="summon"/> (roster id, e.g. FrenziedGhoul;
    /// a spawner type) rise on a ring of <see cref="reach"/> m round the boss; circles warn where. Summons still alive count:
    /// only the missing ones come (never more than <see cref="count"/> alive).</item>
    /// <item><b>Teleport</b> (KAN-119): a circle of <see cref="radius"/> m warns at a walkable point <see cref="length"/> m from
    /// the Knight; the boss shrinks away, reappears there and grows back. <see cref="damage"/> above 0 hits inside the circle
    /// on arrival.</item>
    /// <item><b>Shockwave</b> (KAN-121): a circle of <see cref="radius"/> m warns, centre <see cref="reach"/> m in front of the
    /// boss; then <see cref="count"/> rings, one every <see cref="interval"/> s, grow from the centre at <see cref="speed"/> m/s
    /// until their outer edge reaches the circle. A ring band is <see cref="width"/> m wide and hits once as it passes the
    /// Knight; outside the circle is safe. Rings come from the shared circle pool.</item>
    /// <item><b>Homing</b> (KAN-121): <see cref="count"/> slow orbs over <see cref="spread"/> degrees at the Knight,
    /// <see cref="speed"/> m/s, turning at most <see cref="turn"/> degrees per second towards the Knight, <see cref="length"/> m,
    /// hit radius <see cref="radius"/>. Warning: a circle of <see cref="reach"/> m around the body. One volley hits once.</item>
    /// <item><b>Beams</b> (KAN-121): <see cref="count"/> lanes evenly round the body (<see cref="length"/> x
    /// <see cref="width"/>; the Knight starts between two), shown while it winds up, then live: they turn
    /// <see cref="spread"/> degrees per second (random way) for <see cref="duration"/> s. The whole cast hits once.</item>
    /// <item><b>Barrage</b> (KAN-174): a side cast, not part of the pick. While the boss fights (any attack state) and the
    /// Knight is in range, every <see cref="interval"/> s it lobs one shot (<see cref="SurvivorSouls.Gameplay.Combat.ShotKind.BlackFireball"/>)
    /// to a random point <see cref="length"/>..<see cref="reach"/> m from the Knight (never right on it); flight
    /// <see cref="duration"/> s (0.6 s floor), landing circle of <see cref="radius"/> m shown for the whole flight; each shot
    /// hits once inside its circle. No free circle = no throw (it waits). Circles: the shared
    /// <see cref="SurvivorSouls.Gameplay.Combat.EnemyTelegraphs"/> pool; shots: <see cref="SurvivorSouls.Gameplay.Combat.EnemyProjectiles"/>.</item>
    /// </list>
    /// KAN-109: a <b>Slam</b> or <b>Rain</b> entry with <see cref="burnRadius"/>, <see cref="burnLife"/> and
    /// <see cref="burnDps"/> above 0 leaves a burning ground patch (shared <see cref="SurvivorSouls.Gameplay.Combat.GroundHazards"/>
    /// pool, fire look) where each hit lands: the slam circle centre, every meteor circle. It hurts only while the Knight stands
    /// in it and goes out after its life (the Pyre Lord's flame circle and meteor rain).
    /// KAN-111: an entry with <see cref="every"/> above 0 is <b>timed</b>: it is never picked by score; it comes at the first
    /// free pick once <see cref="every"/> s of fight time have passed since it last started (or since its phase began), ahead
    /// of every other band, range permitting (the Bone Matriarch teleports every 8 s in phase 2).
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
        [Tooltip("Lobbed: seconds the rubble of a boulder stays (0 = no rubble).")]
        [Min(0f)] public float obstacle;
        [Tooltip("Homing: most degrees per second an orb turns towards the Knight.")]
        [Min(0f)] public float turn;
        [Tooltip("Summon: roster id of the summoned type (e.g. FrenziedGhoul). Must be a type of the scene's spawner.")]
        public string summon;
        [Tooltip("Slam / Rain (KAN-109): radius (m) of the burning patch left where a hit lands. 0 = none.")]
        [Min(0f)] public float burnRadius;
        [Tooltip("Slam / Rain (KAN-109): seconds the burning patch stays.")]
        [Min(0f)] public float burnLife;
        [Tooltip("Slam / Rain (KAN-109): damage per second inside the patch, as a share of the Knight's max health.")]
        [Range(0f, 0.5f)] public float burnDps;
        [Tooltip("Animator state the body plays for this attack (wind-up and strike). Empty or missing = its Attack state.")]
        public string clip;
        [Tooltip("KAN-111: > 0 = timed: it comes every this many seconds (at the next free pick, before other attacks), " +
                 "counted from its last start or the start of its phase. Never picked by score.")]
        [Min(0f)] public float every;

        public bool InPhase(int phase) => (phases & (phase >= 2 ? BossPhases.Phase2 : BossPhases.Phase1)) != 0;

        public bool InRange(float distance) => distance >= minRange && distance <= maxRange;

        /// <summary>KAN-111: comes on a timer (<see cref="every"/>), not by score.</summary>
        public bool Timed => every > 0f;

        /// <summary>KAN-111: a copy of this entry that comes every <paramref name="seconds"/> s (see <see cref="every"/>).</summary>
        public BossAttack Every(float seconds)
        {
            var a = this;
            a.every = Mathf.Max(0f, seconds);
            return a;
        }

        /// <summary>Wind-up with the 0.6 s floor (the phase 2 factor comes on top, <see cref="BossData.TelegraphFor"/>).</summary>
        public float Telegraph => Mathf.Max(BossData.MinTelegraph, telegraph);

        /// <summary>KAN-109: this entry leaves a burning patch where its hits land.</summary>
        public bool Burns => burnRadius > 0f && burnLife > 0f && burnDps > 0f;

        /// <summary>KAN-109: a copy of this entry that leaves burning patches (radius m, life s, dps as a share of max health).</summary>
        public BossAttack WithBurn(float radius, float life, float dpsShare)
        {
            var a = this;
            a.burnRadius = radius;
            a.burnLife = life;
            a.burnDps = dpsShare;
            return a;
        }

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

        public static BossAttack Lobbed(string label, BossPhases phases, float minRange, float maxRange, int boulders,
            float impactRadius, float flight, float interval, float rubbleRadius, float rubbleLife, float telegraph, float damage,
            float recover, float cooldown, int priority = 0, float weight = 1f, string clip = null) =>
            new BossAttack
            {
                label = label, type = BossFight.Move.Lobbed, phases = phases, priority = priority, weight = weight,
                minRange = minRange, maxRange = maxRange, count = boulders, radius = impactRadius, duration = flight,
                interval = interval, width = rubbleRadius, obstacle = rubbleLife, telegraph = telegraph, damage = damage,
                recover = recover, cooldown = cooldown, clip = clip,
            };

        public static BossAttack Summon(string label, BossPhases phases, float minRange, float maxRange, string type, int count,
            float ring, float telegraph, float recover, float cooldown, int priority = 0, float weight = 1f, string clip = null) =>
            new BossAttack
            {
                label = label, type = BossFight.Move.Summon, phases = phases, priority = priority, weight = weight,
                minRange = minRange, maxRange = maxRange, summon = type, count = count, reach = ring, telegraph = telegraph,
                recover = recover, cooldown = cooldown, clip = clip,
            };

        public static BossAttack Teleport(string label, BossPhases phases, float minRange, float maxRange, float distance,
            float warnRadius, float telegraph, float damage, float recover, float cooldown, int priority = 0, float weight = 1f,
            string clip = null) =>
            new BossAttack
            {
                label = label, type = BossFight.Move.Teleport, phases = phases, priority = priority, weight = weight,
                minRange = minRange, maxRange = maxRange, length = distance, radius = warnRadius, telegraph = telegraph,
                damage = damage, recover = recover, cooldown = cooldown, clip = clip,
            };

        public static BossAttack Shockwave(string label, BossPhases phases, float minRange, float maxRange, int rings,
            float radius, float reach, float speed, float band, float interval, float telegraph, float damage, float recover,
            float cooldown, int priority = 0, float weight = 1f, string clip = null) =>
            new BossAttack
            {
                label = label, type = BossFight.Move.Shockwave, phases = phases, priority = priority, weight = weight,
                minRange = minRange, maxRange = maxRange, count = rings, radius = radius, reach = reach, speed = speed,
                width = band, interval = interval, telegraph = telegraph, damage = damage, recover = recover,
                cooldown = cooldown, clip = clip,
            };

        public static BossAttack Homing(string label, BossPhases phases, float minRange, float maxRange, int orbs, float spread,
            float turn, float speed, float distance, float hitRadius, float warnRadius, float telegraph, float damage,
            float recover, float cooldown, int priority = 0, float weight = 1f, string clip = null) =>
            new BossAttack
            {
                label = label, type = BossFight.Move.Homing, phases = phases, priority = priority, weight = weight,
                minRange = minRange, maxRange = maxRange, count = orbs, spread = spread, turn = turn, speed = speed,
                length = distance, radius = hitRadius, reach = warnRadius, telegraph = telegraph, damage = damage,
                recover = recover, cooldown = cooldown, clip = clip,
            };

        public static BossAttack Beams(string label, BossPhases phases, float minRange, float maxRange, int lanes, float length,
            float width, float turnSpeed, float duration, float telegraph, float damage, float recover, float cooldown,
            int priority = 0, float weight = 1f, string clip = null) =>
            new BossAttack
            {
                label = label, type = BossFight.Move.Beams, phases = phases, priority = priority, weight = weight,
                minRange = minRange, maxRange = maxRange, count = lanes, length = length, width = width, spread = turnSpeed,
                duration = duration, telegraph = telegraph, damage = damage, recover = recover, cooldown = cooldown,
                clip = clip,
            };

        /// <summary>
        /// KAN-174: a side cast (see <see cref="BossFight.Move.Barrage"/>): every <paramref name="interval"/> s one lobbed shot
        /// to a random point <paramref name="minOffset"/>..<paramref name="maxOffset"/> m from the Knight, flight
        /// <paramref name="flight"/> s (= the warning), landing circle <paramref name="impactRadius"/> m.
        /// </summary>
        public static BossAttack Barrage(string label, BossPhases phases, float minRange, float maxRange, float interval,
            float minOffset, float maxOffset, float impactRadius, float flight, float damage) =>
            new BossAttack
            {
                label = label, type = BossFight.Move.Barrage, phases = phases, weight = 1f, minRange = minRange,
                maxRange = maxRange, interval = interval, length = minOffset, reach = maxOffset, radius = impactRadius,
                duration = flight, telegraph = Mathf.Max(BossData.MinTelegraph, flight), damage = damage,
            };

        /// <summary>
        /// KAN-174: the Butcher of Ashgrave's black fireballs: one every 1 s in both phases, 0.6-3 m from the Knight (a
        /// still Knight is clipped now and then; walking blind into the circles hurts), circle r 1 m, flight 0.9 s, 11 % each
        /// (KAN-163 share; KAN-174 shipped 6 %).
        /// </summary>
        public static BossAttack BlackFireballs() =>
            Barrage("Black Fireballs", BossPhases.Both, 0f, 30f, interval: 1f, minOffset: 0.6f, maxOffset: 3f,
                impactRadius: 1f, flight: 0.9f, damage: 0.11f);

        /// <summary>
        /// Pyre Lord style numbers (Docs/ENEMY_ROSTER.md 3.13) for a type: the debug menu and dev screenshots add it to a
        /// boss that lacks that type; KAN-109 starts its list from these. KAN-119: Lobbed and Summon are Rotting Colossus
        /// numbers (3.14), Teleport is the Bone Matriarch's (3.15); KAN-110 / KAN-111 start from these. KAN-121: Shockwave is the
        /// Colossus double slam (3.14a), Homing and Beams are the Matriarch's skulls and cross beams (3.15a, d). KAN-174: Barrage
        /// is the Butcher's black fireballs (<see cref="BlackFireballs"/>). Other types: a plain slam. KAN-190: damage = the
        /// KAN-163 shares (Docs/BALANCE.md; x1.8, Cross Beams x2.1), the same as the shipped boss assets.
        /// </summary>
        public static BossAttack Sample(BossFight.Move type)
        {
            switch (type)
            {
                case BossFight.Move.Nova:
                    return Nova("Fire Nova", BossPhases.Both, 9f, bolts: 16, gaps: 0, warnRadius: 3f, telegraph: 0.9f, speed: 7f,
                        distance: 14f, hitRadius: 0.6f, damage: 0.25f, recover: 0.8f, cooldown: 3f);
                case BossFight.Move.Spiral:
                    return Spiral("Fire Spiral", BossPhases.Phase2, 12f, arms: 2, turnSpeed: 110f, duration: 3f, interval: 0.14f,
                        warnRadius: 3f, telegraph: 1f, speed: 6.5f, distance: 14f, hitRadius: 0.55f, damage: 0.29f, recover: 1f,
                        cooldown: 3.5f);
                case BossFight.Move.Rain:
                    return Rain("Meteor Rain", BossPhases.Phase2, 3f, 16f, circles: 6, radius: 1.6f, scatter: 2.5f, interval: 0.35f,
                        telegraph: 1f, damage: 0.27f, recover: 0.8f, cooldown: 3.5f);
                case BossFight.Move.Lobbed:
                    return Lobbed("Corpse Boulder", BossPhases.Both, 3f, 14f, boulders: 1, impactRadius: 2.5f, flight: 1.1f,
                        interval: 0.6f, rubbleRadius: 0.9f, rubbleLife: 8f, telegraph: 0.8f, damage: 0.36f, recover: 0.9f, cooldown: 3f);
                case BossFight.Move.Summon:
                    return Summon("Summon Ghouls", BossPhases.Phase2, 0f, 30f, "FrenziedGhoul", count: 4, ring: 2.6f, telegraph: 1.2f,
                        recover: 0.8f, cooldown: 4f);
                case BossFight.Move.Teleport:
                    return Teleport("Teleport", BossPhases.Phase2, 0f, 30f, distance: 7f, warnRadius: 1.6f, telegraph: 0.8f, damage: 0f,
                        recover: 0.4f, cooldown: 2f);
                case BossFight.Move.Shockwave:
                    return Shockwave("Double Slam", BossPhases.Both, 0f, 4f, rings: 2, radius: 5.5f, reach: 1.2f, speed: 7f,
                        band: 0.8f, interval: 0.6f, telegraph: 1f, damage: 0.29f, recover: 1f, cooldown: 3f);
                case BossFight.Move.Homing:
                    return Homing("Homing Skulls", BossPhases.Both, 0f, 14f, orbs: 3, spread: 60f, turn: 55f, speed: 3.6f,
                        distance: 16f, hitRadius: 0.5f, warnRadius: 2.2f, telegraph: 0.8f, damage: 0.22f, recover: 0.6f,
                        cooldown: 2.5f);
                case BossFight.Move.Barrage:
                    return BlackFireballs();
                case BossFight.Move.Beams:
                    return Beams("Cross Beams", BossPhases.Phase2, 0f, 30f, lanes: 4, length: 9f, width: 0.9f, turnSpeed: 45f,
                        duration: 4f, telegraph: 1f, damage: 0.34f, recover: 0.8f, cooldown: 3f);
                default:
                    return Slam("Slam", BossPhases.Both, 4f, 3f, 1.6f, 1f, 0.22f, 0.7f, 3.2f);
            }
        }
    }
}
