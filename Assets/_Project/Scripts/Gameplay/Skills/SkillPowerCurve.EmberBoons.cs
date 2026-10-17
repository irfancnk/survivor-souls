using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills
{
    /// <summary>
    /// KAN-246 Ember Boons in the power index (KAN-75): the damage the 19 new skills add, per stat sheet (averaged over runs
    /// like <see cref="TomeDps"/>), so the balance and Chamber pacing sims see a pool of 36 skills instead of 17 + 19 blanks.
    /// Rough horde assumptions, one constant each, documented below; an index for comparing builds, not a fight simulation.
    /// Survival skills (Ashen Heart, Sentinel Halo, Second Dawn, Repelling Bolts) and Last Oath's short low-health surge add
    /// no damage here.
    /// </summary>
    public partial struct PowerSnapshot
    {
        /// <summary>DPS of the Ember Boons on one target (a boss) / in a dense horde (KAN-246).</summary>
        public float BoonSingleDps, BoonCrowdDps;

        /// <summary>Share of rear bolts that find a target (the horde surrounds the Knight, a boss is in front).</summary>
        private const float RearFind = 0.6f;
        /// <summary>Share of bolts that reach a wall and come back through the horde.</summary>
        private const float WallReturn = 0.25f;
        /// <summary>Foes an orbital pass, a pulse or a sword impact meets in a horde.</summary>
        private const float PassFoes = 2f, PulseFoes = 4f, ImpactFoes = 3f;
        /// <summary>Share of orbital passes that meet a single target (Holy Tomes rule: 0.5); wide scythes meet it less.</summary>
        private const float NearContact = 0.5f, WideContact = 0.3f;

        internal void MeasureBoons(PlayerStats stats, PowerBase b, bool permanentOnly)
        {
            float E(StatId id, float baseValue = 0f) => PlayerStats.Eval(stats, id, baseValue, permanentOnly);
            int I(StatId id) => stats != null ? stats.ModifyInt(id, 0, permanentOnly) : 0;

            float d = BoltDamage;
            float laneDps = d * ShotsPerSecond * CritFactor * ChainFactor;            // main-lane weapon DPS on one target
            float crowdBolts = d * ShotsPerSecond * CritFactor * VolleyFactor * HitFactor;
            float hitsSingle = ShotsPerSecond * ChainFactor;                           // weapon hits / s on one target
            float hitsCrowd = ShotsPerSecond * VolleyBolts * HitFactor;                // weapon hits / s in a horde
            float single = 0f, crowd = 0f;

            int front = I(StatId.FrontLanes); // Front Arrow +1: every forward lane at the trade-off share
            if (front > 0)
            {
                float k = (1f + front) * Mathf.Clamp(E(StatId.FrontDamage, 1f), 0f, 1.5f) - 1f;
                single += laneDps * k;
                crowd += laneDps * HitFactor * k;
            }
            int rear = I(StatId.RearArrows); // Rear Arrow +1
            if (rear > 0) crowd += laneDps * HitFactor * rear * E(StatId.RearDamage) * RearFind;
            int walls = I(StatId.WallBounces); // Bouncy Wall
            if (walls > 0) crowd += crowdBolts * walls * Mathf.Clamp01(E(StatId.WallDamage, 0.6f)) * WallReturn;

            float poison = E(StatId.PoisonDps); // Venom Bolts: stacks on a boss, ~1.3 on a horde zombie
            if (poison > 0f)
            {
                float stacks = Mathf.Max(1f, E(StatId.PoisonStacks, 1f));
                single += poison * stacks;
                crowd += poison * Mathf.Min(stacks, 1.3f) * VolleyFactor * HitFactor;
            }
            crowd += hitsCrowd * I(StatId.ArcTargets) * E(StatId.ArcDamage) * d; // Storm Touch: neighbours only

            int shock = I(StatId.ShockTargets); // Stormshock: every 4th hit, at most every 2 s
            if (shock > 0)
            {
                float share = E(StatId.ShockDamage) * d;
                single += Mathf.Min(hitsSingle / 4f, 0.5f) * share;
                crowd += Mathf.Min(hitsCrowd / 4f, 0.5f) * shock * share;
            }

            int wheel = I(StatId.WheelCount); // Ember Wheel: passes x (contact hit + the burn it keeps up)
            if (wheel > 0)
            {
                float passes = wheel / Mathf.Max(0.3f, E(StatId.WheelSpin));
                float burn = E(StatId.WheelBurn);
                single += NearContact * (passes * 0.2f * d + burn);
                crowd += PassFoes * (passes * 0.2f * d + burn);
            }
            int rime = I(StatId.RimeCount); // Rime Blades: 30 % contact hits (the slow is crowd control)
            if (rime > 0)
            {
                float passes = rime / 1.8f;
                single += NearContact * passes * 0.3f * d;
                crowd += PassFoes * passes * 0.3f * d;
            }
            int orbs = I(StatId.SatelliteCount); // Storm Satellites: ranged zaps, extra arcs at half damage
            if (orbs > 0)
            {
                float zap = orbs * 0.6f * d / Mathf.Max(0.25f, E(StatId.SatelliteInterval, 1.6f));
                single += zap;
                crowd += zap * (1f + 0.5f * I(StatId.SatelliteArc));
            }
            int scythes = I(StatId.ScytheCount); // Reaper Scythes: heavy sweeps on a wide, slow orbit
            if (scythes > 0)
            {
                float sweep = scythes / 3.6f * E(StatId.ScytheDamage) * d * (1f + E(StatId.ScytheSize));
                single += WideContact * sweep;
                crowd += PassFoes * 1.25f * sweep;
            }
            float pulse = E(StatId.EclipsePulse); // Eclipse Crown: one pulse per cooldown (an orbital is owned: the offer gate)
            if (pulse > 0f)
            {
                float perSecond = pulse * d / Mathf.Max(3f, E(StatId.EclipseCooldown, 14f));
                single += perSecond;
                crowd += perSecond * PulseFoes;
            }
            float echo = E(StatId.EchoDamage); // Stolen Moment: 3 echo volleys every N volleys
            if (echo > 0f)
            {
                float k = 3f * echo / Mathf.Max(2f, E(StatId.EchoVolleys, 8f));
                single += laneDps * k;
                crowd += crowdBolts * k;
            }
            float sword = E(StatId.SwordDamage); // Sovereign's Fall: every 6 crits, at most every 4 s
            if (sword > 0f)
            {
                single += Mathf.Min(hitsSingle * CritChance / 6f, 0.25f) * sword * d;
                crowd += Mathf.Min(hitsCrowd * CritChance / 6f, 0.25f) * sword * d * ImpactFoes;
            }
            float choir = E(StatId.ChoirChance); // Grave Choir: archers standing = chance x kills / s x life (cap 3)
            if (choir > 0f && b.HitsToKill > 0f)
            {
                float kills = (crowdBolts + crowd) / (b.HitsToKill * Mathf.Max(1f, d));
                float archers = Mathf.Min(3f, choir * kills * (6f + E(StatId.ChoirDuration)));
                float archerDps = archers * E(StatId.ChoirDamage) * d / 0.8f;
                single += 0.5f * archerDps; // archers rise from the adds of a boss fight
                crowd += archerDps;
            }
            BoonSingleDps = single;
            BoonCrowdDps = crowd;
        }
    }
}
