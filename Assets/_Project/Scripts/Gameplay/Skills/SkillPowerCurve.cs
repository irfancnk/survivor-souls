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
        /// Dense-horde assumptions for the Ember Bolts perks (KAN-188, <see cref="Effects.BurnOnHitEffect"/>). SpreadFoes:
        /// foes one bolt-lit fire keeps alight per <see cref="StatId.BurnSpread"/> (it catches one per 0.5 s tick, but most
        /// neighbours within 2.5 m already burn from bolts). BlastFoes: foes within 2.2 m of a burning death. HitsToKill:
        /// full-damage bolt hits a horde zombie takes (horde health follows the power index, KAN-92), so crowd kills per
        /// second = crowd DPS / (HitsToKill x bolt damage).
        /// </summary>
        public float SpreadFoes, BlastFoes, HitsToKill;
        /// <summary>
        /// Holy Tomes (KAN-123, <see cref="Effects.OrbitingTomesEffect"/>). TomeContact: share of book passes that meet a
        /// single target (the Knight moves; a boss is not always on the ring). TomeFoes: foes one book pass hits in a horde.
        /// </summary>
        public float TomeContact, TomeFoes;

        /// <summary>
        /// AutoShooter / PlayerHealth / PlayerMover defaults: 100 dmg, 1 shot/s, no crit, x2 crit, 400 HP, 5 m/s, angled
        /// Spread Shot bolts at 50 % damage, -33 % damage per pierce and -30 % per bounce (KAN-86). Ember perks (KAN-188):
        /// a spreading fire keeps 1.5 foes alight, a death blast reaches 1.5 foes, a horde zombie takes 3 bolt hits.
        /// Holy Tomes (KAN-123): half the passes meet a single target, a pass through a horde hits 2 foes.
        /// </summary>
        public static PowerBase Default => new PowerBase
        {
            Damage = 100f, ShotsPerSecond = 1f, CritChance = 0f, CritMultiplier = 2f, MaxHealth = 400f, MoveSpeed = 5f,
            SpreadDamage = 0.5f,
            PierceFalloff = Combat.BoltShot.DefaultPierceFalloff, BounceFalloff = Combat.BoltShot.DefaultBounceFalloff,
            SpreadFoes = 1.5f, BlastFoes = 1.5f, HitsToKill = 3f,
            TomeContact = 0.5f, TomeFoes = 2f,
        };
    }

    /// <summary>
    /// What a stat sheet is worth (KAN-75 debug): the numbers combat reads (same formulas as AutoShooter, PlayerHealth,
    /// PlayerMover, the burn effect) and two damage indexes. Single target = direct bolt DPS with crit + burn on that
    /// target, times the Double Shot chain (every follow-up bolt hits the same target at its damage share). Crowd = dense
    /// horde: every bolt of the fan (angled bolts at their damage share) and of the chain hits, then pierces and bounces into
    /// a new target each time at the falloff damage (<see cref="HitFactor"/>, KAN-86), and every target hit keeps burning. The volley cap (12) is applied to the bolt count. Soul Burst and life steal are listed but not in the DPS (they depend on kill rate).
    /// Ember perks (KAN-188) are crowd only: every bolt-lit fire also keeps <see cref="SpreadBurnDps"/> burning on its
    /// neighbours, and burning deaths blast (<see cref="BlastFactor"/>). Single target has no neighbours (a boss).
    /// An index for comparing builds and levels, not a fight simulation.
    /// </summary>
    public struct PowerSnapshot
    {
        public float BoltDamage, ShotsPerSecond, CritChance, CritMultiplier, Bolts, Pierce, Ricochet, BurnDps, KillBurst,
            LifeSteal, MaxHealth, MoveSpeed, Skills, Chain, SpreadDamage, ChainDamage, PierceFalloff, BounceFalloff;
        /// <summary>
        /// KAN-188: Ember perk stats (<see cref="StatId.BurnSpread"/>, <see cref="StatId.BurnDeathBlast"/>), SpreadBurn =
        /// burn DPS x BurnSpread of the same sheet (kept per sheet, so a run average does not mix a spread run with a
        /// no-burn run), and the <see cref="PowerBase"/> horde assumptions.
        /// </summary>
        public float BurnSpread, SpreadBurn, BurnDeathBlast, SpreadFoes, BlastFoes, HitsToKill;
        /// <summary>
        /// KAN-123 Holy Tomes: books, hit share of the bolt damage, seconds per turn, and TomeDps = bolt damage x share x
        /// books / seconds per turn x <see cref="PowerBase.TomeContact"/> (one hit per book pass, no crit; kept per sheet
        /// like SpreadBurn). Crowd: x <see cref="TomeFoes"/> (not scaled by the volley or the falloff: the books are not bolts).
        /// </summary>
        public float Tomes, TomeShare, TomeSpin, TomeDps, TomeFoes;

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
        /// <summary>
        /// Burn DPS a bolt-lit fire adds on its neighbours (KAN-188 spread perk): the same burn on
        /// <see cref="BurnSpread"/> x <see cref="SpreadFoes"/> foes. 0 without the perk.
        /// </summary>
        public float SpreadBurnDps => Mathf.Max(0f, SpreadBurn) * Mathf.Max(0f, SpreadFoes);
        /// <summary>
        /// Crowd damage the death blast adds, as a share of the crowd DPS (KAN-188): every kill burns, so blast DPS =
        /// <see cref="BurnDeathBlast"/> x bolt damage x kills per second x <see cref="BlastFoes"/>, with kills per second =
        /// crowd DPS / (<see cref="HitsToKill"/> x bolt damage). Bolt damage cancels. Blast-on-blast chains are not counted.
        /// </summary>
        public float BlastFactor => BurnDps > 0f && HitsToKill > 0f
            ? Mathf.Max(0f, BurnDeathBlast) * Mathf.Max(0f, BlastFoes) / HitsToKill
            : 0f;
        public float SingleTargetDps => BoltDamage * ShotsPerSecond * CritFactor * ChainFactor + BurnDps + TomeDps;
        public float CrowdDps => (BoltDamage * ShotsPerSecond * CritFactor + BurnDps + SpreadBurnDps) * VolleyFactor * HitFactor *
                                 (1f + BlastFactor) + TomeDps * Mathf.Max(0f, TomeFoes);

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
        public static PowerSnapshot Measure(PlayerStats stats, PowerBase b, int skills = 0, bool permanentOnly = false)
        {
            var s = Sheet(stats, b, skills, permanentOnly);
            s.SpreadBurn = s.BurnDps * Mathf.Max(0f, s.BurnSpread);
            s.TomeDps = s.Tomes > 0f && s.TomeSpin > 0f
                ? s.BoltDamage * s.TomeShare * s.Tomes / s.TomeSpin * Mathf.Max(0f, b.TomeContact)
                : 0f;
            return s;
        }

        private static PowerSnapshot Sheet(PlayerStats stats, PowerBase b, int skills, bool permanentOnly) => new PowerSnapshot
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
            BurnSpread = PlayerStats.Eval(stats, StatId.BurnSpread, 0f, permanentOnly),
            BurnDeathBlast = PlayerStats.Eval(stats, StatId.BurnDeathBlast, 0f, permanentOnly),
            SpreadFoes = b.SpreadFoes,
            BlastFoes = b.BlastFoes,
            HitsToKill = b.HitsToKill,
            Tomes = stats != null ? stats.ModifyInt(StatId.TomeCount, 0, permanentOnly) : 0,
            TomeShare = PlayerStats.Eval(stats, StatId.TomeDamage, 0f, permanentOnly),
            TomeSpin = Mathf.Max(0.2f, PlayerStats.Eval(stats, StatId.TomeSpin, 0f, permanentOnly)),
            TomeFoes = b.TomeFoes,
            KillBurst = PlayerStats.Eval(stats, StatId.KillBurst, 0f, permanentOnly),
            LifeSteal = PlayerStats.Eval(stats, StatId.LifeSteal, 0f, permanentOnly),
            PierceFalloff = Mathf.Clamp01(PlayerStats.Eval(stats, StatId.PierceFalloff, b.PierceFalloff, permanentOnly)), // KAN-220
            BounceFalloff = Mathf.Clamp01(PlayerStats.Eval(stats, StatId.BounceFalloff, b.BounceFalloff, permanentOnly)),
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
            BurnSpread += o.BurnSpread * w; SpreadBurn += o.SpreadBurn * w; BurnDeathBlast += o.BurnDeathBlast * w; SpreadFoes += o.SpreadFoes * w;
            BlastFoes += o.BlastFoes * w; HitsToKill += o.HitsToKill * w;
            Tomes += o.Tomes * w; TomeShare += o.TomeShare * w; TomeSpin += o.TomeSpin * w; TomeDps += o.TomeDps * w; TomeFoes += o.TomeFoes * w;
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
            sb.AppendLine("Lv  skills  bolt dmg  shots/s  crit   bolts  chain  pierce  bounce  burn  spread  blast  1-target DPS  crowd DPS  power  max HP  move");
            for (int i = 0; i < MaxLevel; i++)
            {
                var s = Average[i];
                sb.AppendLine($"{i + 1,2}  {s.Skills,6:0.0}  {s.BoltDamage,8:0}  {s.ShotsPerSecond,7:0.00}  {s.CritChance,4:P0}  " +
                              $"{s.Bolts,5:0.00}  {s.Chain,5:0.00}  {s.Pierce,6:0.00}  {s.Ricochet,6:0.00}  {s.BurnDps,4:0}  {s.BurnSpread,6:0.00}  " +
                              $"{s.BurnDeathBlast,5:0.00}  {s.SingleTargetDps,12:0}  " +
                              $"{s.CrowdDps,9:0}  {PowerAt(i + 1),4:0.0}x  {s.MaxHealth,6:0}  {s.MoveSpeed,4:0.0}");
            }
            return sb.ToString();
        }
    }
}
