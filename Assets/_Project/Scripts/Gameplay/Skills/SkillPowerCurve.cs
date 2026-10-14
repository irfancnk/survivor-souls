using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Skills
{
    /// <summary>The Knight's base numbers before skills (power curve input). <see cref="Default"/> = the shipped bases.</summary>
    public struct PowerBase
    {
        public float Damage, ShotsPerSecond, CritChance, CritMultiplier, MaxHealth, MoveSpeed;

        /// <summary>AutoShooter / PlayerHealth / PlayerMover defaults: 100 dmg, 1 shot/s, no crit, x2 crit, 400 HP, 5 m/s.</summary>
        public static PowerBase Default => new PowerBase
        {
            Damage = 100f, ShotsPerSecond = 1f, CritChance = 0f, CritMultiplier = 2f, MaxHealth = 400f, MoveSpeed = 5f,
        };
    }

    /// <summary>
    /// What a stat sheet is worth (KAN-75 debug): the numbers combat reads (same formulas as AutoShooter, PlayerHealth,
    /// PlayerMover, the burn effect) and two damage indexes. Single target = direct bolt DPS with crit + burn on that
    /// target. Crowd = dense horde: every bolt of the fan hits, then pierces and bounces into a new target each time, and
    /// every target hit keeps burning. Soul Burst and life steal are listed but not in the DPS (they depend on kill rate).
    /// An index for comparing builds and levels, not a fight simulation.
    /// </summary>
    public struct PowerSnapshot
    {
        public float BoltDamage, ShotsPerSecond, CritChance, CritMultiplier, Bolts, Pierce, Ricochet, BurnDps, KillBurst,
            LifeSteal, MaxHealth, MoveSpeed, Skills;

        public float CritFactor => 1f + CritChance * (CritMultiplier - 1f);
        public float HitsPerVolley => Bolts * (1f + Pierce + Ricochet);
        public float SingleTargetDps => BoltDamage * ShotsPerSecond * CritFactor + BurnDps;
        public float CrowdDps => (BoltDamage * ShotsPerSecond * CritFactor + BurnDps) * HitsPerVolley;

        public static PowerSnapshot Measure(PlayerStats stats, PowerBase b, int skills = 0) => new PowerSnapshot
        {
            BoltDamage = PlayerStats.Eval(stats, StatId.Damage, b.Damage),
            ShotsPerSecond = Mathf.Max(0.05f, PlayerStats.Eval(stats, StatId.AttackSpeed, b.ShotsPerSecond)),
            CritChance = Mathf.Clamp01(PlayerStats.Eval(stats, StatId.CritChance, b.CritChance)),
            CritMultiplier = Mathf.Max(1f, PlayerStats.Eval(stats, StatId.CritDamage, b.CritMultiplier)),
            Bolts = 1 + (stats != null ? stats.ModifyInt(StatId.Multishot, 0) : 0),
            Pierce = stats != null ? stats.ModifyInt(StatId.Pierce, 0) : 0,
            Ricochet = stats != null ? stats.ModifyInt(StatId.Ricochet, 0) : 0,
            BurnDps = PlayerStats.Eval(stats, StatId.BurnDps, 0f),
            KillBurst = PlayerStats.Eval(stats, StatId.KillBurst, 0f),
            LifeSteal = PlayerStats.Eval(stats, StatId.LifeSteal, 0f),
            MaxHealth = PlayerStats.Eval(stats, StatId.MaxHealth, b.MaxHealth),
            MoveSpeed = PlayerStats.Eval(stats, StatId.MoveSpeed, b.MoveSpeed),
            Skills = skills,
        };

        internal void Add(in PowerSnapshot o, float w)
        {
            BoltDamage += o.BoltDamage * w; ShotsPerSecond += o.ShotsPerSecond * w; CritChance += o.CritChance * w;
            CritMultiplier += o.CritMultiplier * w; Bolts += o.Bolts * w; Pierce += o.Pierce * w; Ricochet += o.Ricochet * w;
            BurnDps += o.BurnDps * w; KillBurst += o.KillBurst * w; LifeSteal += o.LifeSteal * w; MaxHealth += o.MaxHealth * w;
            MoveSpeed += o.MoveSpeed * w; Skills += o.Skills * w;
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
            sb.AppendLine("Lv  skills  bolt dmg  shots/s  crit   bolts  pierce  bounce  burn  1-target DPS  crowd DPS  power  max HP  move");
            for (int i = 0; i < MaxLevel; i++)
            {
                var s = Average[i];
                sb.AppendLine($"{i + 1,2}  {s.Skills,6:0.0}  {s.BoltDamage,8:0}  {s.ShotsPerSecond,7:0.00}  {s.CritChance,4:P0}  " +
                              $"{s.Bolts,5:0.00}  {s.Pierce,6:0.00}  {s.Ricochet,6:0.00}  {s.BurnDps,4:0}  {s.SingleTargetDps,12:0}  " +
                              $"{s.CrowdDps,9:0}  {PowerAt(i + 1),4:0.0}x  {s.MaxHealth,6:0}  {s.MoveSpeed,4:0.0}");
            }
            return sb.ToString();
        }
    }
}
