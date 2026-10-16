using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills
{
    /// <summary>The Knight's base numbers before skills (power curve input). <see cref="Default"/> = the shipped bases.</summary>
    public struct PowerBase
    {
        public float Damage, ShotsPerSecond, CritChance, CritMultiplier, MaxHealth, MoveSpeed, SpreadDamage;
        /// <summary>Damage share lost per pierce / per ricochet bounce (KAN-86, <see cref="Combat.BoltShot.PierceFalloff"/>).</summary>
        public float PierceFalloff, BounceFalloff;

        /// <summary>
        /// AutoShooter / PlayerHealth / PlayerMover defaults: 100 dmg, 1 shot/s, no crit, x2 crit, 400 HP, 5 m/s, angled
        /// Spread Shot bolts at 50 % damage, -33 % damage per pierce and -30 % per bounce (KAN-86).
        /// </summary>
        public static PowerBase Default => new PowerBase
        {
            Damage = 100f, ShotsPerSecond = 1f, CritChance = 0f, CritMultiplier = 2f, MaxHealth = 400f, MoveSpeed = 5f,
            SpreadDamage = 0.5f,
            PierceFalloff = Combat.BoltShot.DefaultPierceFalloff, BounceFalloff = Combat.BoltShot.DefaultBounceFalloff,
        };
    }

    /// <summary>
    /// What a stat sheet is worth (KAN-75 debug): the numbers combat reads (same formulas as AutoShooter, PlayerHealth,
    /// PlayerMover, the burn effect) and two damage indexes. Single target = direct bolt DPS with crit + burn on that
    /// target, times the Double Shot chain (every follow-up bolt hits the same target at its damage share). Crowd = dense
    /// horde: every bolt of the fan (angled bolts at their damage share) and of the chain hits, then pierces and bounces into
    /// a new target each time at the falloff damage (<see cref="HitFactor"/>, KAN-86), and every target hit keeps burning. The volley cap (12) is applied to the bolt count. Soul Burst and life steal are listed but not in the DPS (they depend on kill rate).
    /// An index for comparing builds and levels, not a fight simulation.
    /// </summary>
    public struct PowerSnapshot
    {
        public float BoltDamage, ShotsPerSecond, CritChance, CritMultiplier, Bolts, Pierce, Ricochet, BurnDps, KillBurst,
            LifeSteal, MaxHealth, MoveSpeed, Skills, Chain, SpreadDamage, ChainDamage, PierceFalloff, BounceFalloff;

        public float CritFactor => 1f + CritChance * (CritMultiplier - 1f);
        /// <summary>Bolts per volley: fan x chain, capped like the shooter (<see cref="Combat.VolleyBuilder.DefaultCap"/>).</summary>
        public float VolleyBolts => Mathf.Min(Combat.VolleyBuilder.DefaultCap, Bolts * (1f + Chain));
        /// <summary>Full-damage bolts on one target per volley: the main bolt plus the Double Shot follow-ups at their share.</summary>
        public float ChainFactor => 1f + Chain * ChainDamage;
        /// <summary>Full-damage bolts per volley in a crowd: the fan (angled bolts at their share) times the chain, capped.</summary>
        public float VolleyFactor
        {
            get
            {
                float raw = (1f + (Bolts - 1f) * SpreadDamage) * ChainFactor;
                float all = Bolts * (1f + Chain);
                return all > VolleyBolts ? raw * VolleyBolts / all : raw;
            }
        }
        public float HitsPerVolley => VolleyBolts * (1f + Pierce + Ricochet);
        /// <summary>
        /// Full-damage hits of one bolt in a crowd (KAN-86): the first hit, then each pierce at (1 - pierce falloff)^i, then
        /// each bounce at the damage left after the pierces times (1 - bounce falloff)^j. No falloff = 1 + pierce + bounce.
        /// Fractional counts (run averages) use the continuous geometric sum.
        /// </summary>
        public float HitFactor
        {
            get
            {
                float p = 1f - Mathf.Clamp01(PierceFalloff), r = 1f - Mathf.Clamp01(BounceFalloff);
                return 1f + Geometric(p, Pierce) + Mathf.Pow(p, Pierce) * Geometric(r, Ricochet);
            }
        }
        public float SingleTargetDps => BoltDamage * ShotsPerSecond * CritFactor * ChainFactor + BurnDps;
        public float CrowdDps => (BoltDamage * ShotsPerSecond * CritFactor + BurnDps) * VolleyFactor * HitFactor;

        /// <summary>c + c^2 + ... + c^n (n may be fractional); n when c = 1.</summary>
        private static float Geometric(float c, float n)
        {
            if (n <= 0f) return 0f;
            if (c >= 0.9999f) return n;
            return c * (1f - Mathf.Pow(c, n)) / (1f - c);
        }

        /// <summary>
        /// Power of <paramref name="stats"/> on base <paramref name="b"/>. <paramref name="permanentOnly"/>: only the permanent
        /// sources (KAN-98 soul tree), i.e. the Knight as he enters the run.
        /// </summary>
        public static PowerSnapshot Measure(PlayerStats stats, PowerBase b, int skills = 0, bool permanentOnly = false) => new PowerSnapshot
        {
            BoltDamage = PlayerStats.Eval(stats, StatId.Damage, b.Damage, permanentOnly),
            ShotsPerSecond = Mathf.Max(0.05f, PlayerStats.Eval(stats, StatId.AttackSpeed, b.ShotsPerSecond, permanentOnly)),
            CritChance = Mathf.Clamp01(PlayerStats.Eval(stats, StatId.CritChance, b.CritChance, permanentOnly)),
            CritMultiplier = Mathf.Max(1f, PlayerStats.Eval(stats, StatId.CritDamage, b.CritMultiplier, permanentOnly)),
            Bolts = 1 + (stats != null ? stats.ModifyInt(StatId.Multishot, 0, permanentOnly) : 0),
            Chain = stats != null ? stats.ModifyInt(StatId.DoubleShot, 0, permanentOnly) : 0,
            SpreadDamage = Mathf.Clamp01(PlayerStats.Eval(stats, StatId.SpreadDamage, b.SpreadDamage, permanentOnly)),
            ChainDamage = Mathf.Max(0f, PlayerStats.Eval(stats, StatId.ChainDamage, 1f, permanentOnly)),
            Pierce = stats != null ? stats.ModifyInt(StatId.Pierce, 0, permanentOnly) : 0,
            Ricochet = stats != null ? stats.ModifyInt(StatId.Ricochet, 0, permanentOnly) : 0,
            BurnDps = PlayerStats.Eval(stats, StatId.BurnDps, 0f, permanentOnly),
            KillBurst = PlayerStats.Eval(stats, StatId.KillBurst, 0f, permanentOnly),
            LifeSteal = PlayerStats.Eval(stats, StatId.LifeSteal, 0f, permanentOnly),
            PierceFalloff = b.PierceFalloff,
            BounceFalloff = b.BounceFalloff,
            MaxHealth = PlayerStats.Eval(stats, StatId.MaxHealth, b.MaxHealth, permanentOnly),
            MoveSpeed = PlayerStats.Eval(stats, StatId.MoveSpeed, b.MoveSpeed, permanentOnly),
            Skills = skills,
        };

        internal void Add(in PowerSnapshot o, float w)
        {
            BoltDamage += o.BoltDamage * w; ShotsPerSecond += o.ShotsPerSecond * w; CritChance += o.CritChance * w;
            CritMultiplier += o.CritMultiplier * w; Bolts += o.Bolts * w; Pierce += o.Pierce * w; Ricochet += o.Ricochet * w;
            BurnDps += o.BurnDps * w; KillBurst += o.KillBurst * w; LifeSteal += o.LifeSteal * w; MaxHealth += o.MaxHealth * w;
            MoveSpeed += o.MoveSpeed * w; Skills += o.Skills * w; Chain += o.Chain * w; SpreadDamage += o.SpreadDamage * w;
            ChainDamage += o.ChainDamage * w; PierceFalloff += o.PierceFalloff * w; BounceFalloff += o.BounceFalloff * w;
        }
    }

    /// <summary>
    /// Power curve (KAN-75): <see cref="Runs"/> simulated runs pick one random card of every level-up offer (the real
    /// <see cref="SkillManager.Roll"/> with the shipped table, tiers, guarantees and pity) from level 2 to
    /// <see cref="MaxLevel"/>. Row L = the average stat sheet after the level L pick (row 1 = no skills). Chest upgrades are
    /// not in it (they need the drop rate; see the chest statistics). Debug only: allocates.
    /// </summary>
    public sealed class SkillPowerCurve
    {
        public int Runs { get; private set; }
        public int MaxLevel { get; private set; }
        public PowerBase Base { get; private set; }
        /// <summary>Index L - 1 = average after the level L pick.</summary>
        public PowerSnapshot[] Average { get; private set; }

        public PowerSnapshot AtLevel(int level) => Average[Mathf.Clamp(level, 1, MaxLevel) - 1];
        /// <summary>Crowd DPS at <paramref name="level"/> over crowd DPS with no skills.</summary>
        public float PowerAt(int level) => AtLevel(level).CrowdDps / Mathf.Max(0.0001f, Average[0].CrowdDps);

        public static SkillPowerCurve Simulate(SkillProgression table, PowerBase b, int runs = 200, int maxLevel = 20, int seed = 75)
        {
            runs = Mathf.Max(1, runs);
            maxLevel = Mathf.Max(1, maxLevel);
            var curve = new SkillPowerCurve { Runs = runs, MaxLevel = maxLevel, Base = b, Average = new PowerSnapshot[maxLevel] };
            var go = new GameObject("Power Curve (temp)") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                var stats = go.AddComponent<PlayerStats>();
                var manager = go.AddComponent<SkillManager>();
                var offer = new List<SkillDefinition>(3);
                var pick = new System.Random(seed);
                float w = 1f / runs;
                for (int r = 0; r < runs; r++)
                {
                    manager.Setup(table, stats, null, null, go.transform, seed + 7919 * (r + 1));
                    manager.SpawnEffects = false;
                    stats.Clear();
                    curve.Average[0].Add(PowerSnapshot.Measure(stats, b), w);
                    for (int level = 2; level <= maxLevel; level++)
                    {
                        int n = table != null ? manager.Roll(3, level, offer) : 0;
                        if (n > 0) manager.Grant(offer[pick.Next(n)]);
                        curve.Average[level - 1].Add(PowerSnapshot.Measure(stats, b, manager.OwnedSkills.Count), w);
                    }
                }
                manager.ResetRun();
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
            return curve;
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Power curve: {Runs} runs, random card of each level-up offer (no chest upgrades). Base: " +
                          $"{Base.Damage:0} dmg, {Base.ShotsPerSecond:0.##} shots/s, {Base.MaxHealth:0} HP.");
            sb.AppendLine("Lv  skills  bolt dmg  shots/s  crit   bolts  chain  pierce  bounce  burn  1-target DPS  crowd DPS  power  max HP  move");
            for (int i = 0; i < MaxLevel; i++)
            {
                var s = Average[i];
                sb.AppendLine($"{i + 1,2}  {s.Skills,6:0.0}  {s.BoltDamage,8:0}  {s.ShotsPerSecond,7:0.00}  {s.CritChance,4:P0}  " +
                              $"{s.Bolts,5:0.00}  {s.Chain,5:0.00}  {s.Pierce,6:0.00}  {s.Ricochet,6:0.00}  {s.BurnDps,4:0}  {s.SingleTargetDps,12:0}  " +
                              $"{s.CrowdDps,9:0}  {PowerAt(i + 1),4:0.0}x  {s.MaxHealth,6:0}  {s.MoveSpeed,4:0.0}");
            }
            return sb.ToString();
        }
    }
}
