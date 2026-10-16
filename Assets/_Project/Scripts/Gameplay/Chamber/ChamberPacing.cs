using System.Text;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Chamber
{
    /// <summary>
    /// Run pacing model (KAN-113): a deterministic estimate of what one Realm run gives per Chamber on the Chamber clock.
    /// Per regular Chamber (boss Chambers are skipped: no clock, the boss gives no XP) it steps the wave table like
    /// <see cref="EnemySpawner"/> does (groups of the average size every spawn interval, bursts, max alive), with the
    /// Chamber scaling of <see cref="ChamberDifficulty"/> and the power-scaled health and spawn pressure of
    /// <see cref="HordeScaling"/>; the Knight kills with the KAN-75 power curve DPS of his Hunter level (single-target DPS
    /// on a thin field, crowd DPS on a dense one, x <see cref="Assumptions.Engagement"/>) and collects
    /// <see cref="Assumptions.Collect"/> of the XP. The Hunter level carries over to the next Chamber, as in a run. Chest
    /// upgrades are not counted. An index for tuning (XP curve, caps, pressure), not a fight simulation. Debug only: allocates.
    /// </summary>
    public static class ChamberPacing
    {
        /// <summary>Model inputs that are guesses, not data. <see cref="Default"/> = the KAN-113 calibration.</summary>
        public struct Assumptions
        {
            /// <summary>Model step in game seconds.</summary>
            public float Step;
            /// <summary>Share of the Knight's DPS that lands on the horde (aim turns, kiting, bolts past the field).</summary>
            public float Engagement;
            /// <summary>Alive count at which the crowd DPS applies in full; below it the DPS blends to single target.</summary>
            public float DenseAlive;
            /// <summary>Share of the dropped XP the Knight picks up.</summary>
            public float Collect;

            /// <summary>
            /// KAN-113 calibration: 0.25 s steps, 60 % engagement, crowd DPS from 14 alive, 90 % of the XP collected.
            /// Mac dev-build measure (immortal standing Knight, auto pick, Chamber 1): see the DONE note.
            /// </summary>
            public static Assumptions Default => new Assumptions { Step = 0.25f, Engagement = 0.6f, DenseAlive = 14f, Collect = 0.9f };
        }

        /// <summary>One Chamber of the model run.</summary>
        public struct Row
        {
            public int Chamber;
            public ChamberType Type;
            public int LevelStart, LevelEnd;
            public float Spawned, Kills, Xp, AverageAlive, PeakAlive;
            public float PowerStart, PowerEnd, CapFactorEnd, IntervalFactorEnd;
            public int Picks => LevelEnd - LevelStart;
            public bool Skipped => Type == ChamberType.Boss;
        }

        /// <summary>
        /// Model run over the Chambers 1..<see cref="ChamberDifficulty.ChambersInRealm"/> of <paramref name="difficulty"/>
        /// (or <paramref name="chambers"/> when above 0). <paramref name="scaling"/> null = no health scaling and no pressure.
        /// </summary>
        public static Row[] Simulate(EnemyWaveTable waves, ChamberDifficulty difficulty, HordeScaling scaling, SkillPowerCurve curve,
            XpCurve xp, float seconds, Assumptions a, int chambers = 0)
        {
            int count = chambers > 0 ? chambers : difficulty != null ? difficulty.ChambersInRealm : 1;
            var rows = new Row[count];
            if (waves == null || curve == null || xp == null || waves.PhaseCount == 0) return rows;
            var bare = curve.AtLevel(1);
            float step = Mathf.Max(0.02f, a.Step);
            int level = 1;
            float bank = 0f;
            for (int c = 1; c <= count; c++)
            {
                var row = new Row { Chamber = c, Type = difficulty != null ? difficulty.TypeOf(c) : ChamberType.Normal, LevelStart = level };
                float power = Power(scaling, curve, bare, level);
                row.PowerStart = power;
                if (row.Skipped)
                {
                    row.LevelEnd = level;
                    row.PowerEnd = power;
                    rows[c - 1] = row;
                    continue;
                }
                float capMul = difficulty != null ? difficulty.CapMultiplier(c) : 1f;
                float intervalMul = difficulty != null ? difficulty.IntervalMultiplier(c) : 1f;
                float healthMul = difficulty != null ? difficulty.HealthMultiplier(c) : 1f;
                float alive = 0f, pending = 0f, groupTimer = 0f, burstTimer = 0f, aliveSum = 0f;
                int phaseIndex = -1, steps = 0;
                for (float t = 0f; t < seconds; t += step)
                {
                    float capF = scaling != null ? scaling.CapFactor(power) : 1f;
                    float intervalF = scaling != null ? scaling.IntervalFactor(power) : 1f;
                    if (t >= waves.FirstSpawnDelay)
                    {
                        int index = waves.PhaseAt(t);
                        if (index >= 0)
                        {
                            var phase = waves.GetPhase(index);
                            if (index != phaseIndex)
                            {
                                phaseIndex = index;
                                groupTimer = 0f;
                                burstTimer = phase.burstInterval * intervalMul * intervalF;
                            }
                            int tableCap = Mathf.RoundToInt(phase.maxAlive * capMul);
                            int maxAlive = scaling != null && capF > 1f ? scaling.PressuredMaxAlive(tableCap, capF) : tableCap;
                            float interval = phase.spawnInterval * intervalMul * intervalF;
                            groupTimer -= step;
                            if (groupTimer <= 0f)
                            {
                                pending += 0.5f * (Mathf.Max(1, phase.groupSize.x) + Mathf.Max(1, phase.groupSize.y));
                                groupTimer = Mathf.Max(groupTimer + interval, 0.5f * interval);
                            }
                            if (phase.burstInterval > 0f)
                            {
                                burstTimer -= step;
                                if (burstTimer <= 0f)
                                {
                                    pending += Mathf.Round(phase.burstSize * capMul * capF);
                                    burstTimer = phase.burstInterval * intervalMul * intervalF;
                                }
                            }
                            pending = Mathf.Clamp(pending, 0f, Mathf.Max(0f, maxAlive - alive));
                            alive += pending;
                            row.Spawned += pending;
                            pending = 0f;
                            row.PeakAlive = Mathf.Max(row.PeakAlive, alive);

                            Mix(phase, c, out float hp, out float xpPerKill);
                            var s = curve.AtLevel(level);
                            float dense = Mathf.Clamp01(alive / Mathf.Max(1f, a.DenseAlive));
                            float dps = Mathf.Lerp(s.SingleTargetDps, Mathf.Max(s.SingleTargetDps, s.CrowdDps), dense) * a.Engagement;
                            float health = hp * healthMul * (scaling != null ? scaling.DynamicMultiplier(power, t / Mathf.Max(1f, seconds)) : 1f);
                            float kills = Mathf.Min(alive, dps * step / Mathf.Max(1f, health));
                            alive -= kills;
                            row.Kills += kills;
                            float gained = kills * xpPerKill * a.Collect;
                            row.Xp += gained;
                            bank += gained;
                            while (bank >= xp.XpToNext(level))
                            {
                                bank -= xp.XpToNext(level);
                                level++;
                                power = Power(scaling, curve, bare, level);
                            }
                        }
                    }
                    aliveSum += alive;
                    steps++;
                }
                row.AverageAlive = steps > 0 ? aliveSum / steps : 0f;
                row.LevelEnd = level;
                row.PowerEnd = power;
                row.CapFactorEnd = scaling != null ? scaling.CapFactor(power) : 1f;
                row.IntervalFactorEnd = scaling != null ? scaling.IntervalFactor(power) : 1f;
                rows[c - 1] = row;
            }
            return rows;
        }

        private static float Power(HordeScaling scaling, SkillPowerCurve curve, in PowerSnapshot bare, int level) =>
            scaling != null ? scaling.PowerOf(curve.AtLevel(level), bare) : HordeScaling.PowerIndex(curve.AtLevel(level), bare);

        /// <summary>Weighted base max health and XP per kill of the entries of <paramref name="phase"/> open in <paramref name="chamber"/>.</summary>
        public static void Mix(in EnemyWaveTable.Phase phase, int chamber, out float health, out float xpPerKill)
        {
            float total = 0f, hp = 0f, xp = 0f;
            if (phase.enemies != null)
                foreach (var e in phase.enemies)
                {
                    if (e.prefab == null || !e.OpenIn(chamber) || e.weight <= 0f) continue;
                    total += e.weight;
                    hp += e.weight * e.prefab.BaseMaxHealth;
                    xp += e.weight * e.prefab.XpValue;
                }
            health = total > 0f ? hp / total : 100f;
            xpPerKill = total > 0f ? xp / total : 0f;
        }

        /// <summary>The model run as a table (balance report, tests).</summary>
        public static string Table(Row[] rows, float seconds, Assumptions a)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Chamber pacing (KAN-113): {seconds:0} s Chambers, engagement {a.Engagement:P0}, crowd DPS from {a.DenseAlive:0} alive, " +
                          $"{a.Collect:P0} XP collected, no chest upgrades; boss Chambers skipped.");
            sb.AppendLine("Ch  type      lv start  lv end  picks  spawned  kills    XP  avg alive  peak  power  cap    interval");
            foreach (var r in rows)
            {
                if (r.Skipped)
                {
                    sb.AppendLine($"{r.Chamber,2}  {r.Type,-8}  {r.LevelStart,8}  (skipped)");
                    continue;
                }
                sb.AppendLine($"{r.Chamber,2}  {r.Type,-8}  {r.LevelStart,8}  {r.LevelEnd,6}  {r.Picks,5}  {r.Spawned,7:0}  {r.Kills,5:0}  {r.Xp,4:0}  " +
                              $"{r.AverageAlive,9:0.0}  {r.PeakAlive,4:0}  {r.PowerEnd,5:0.00}  {r.CapFactorEnd,4:0.00}x  {r.IntervalFactorEnd,5:0.00}x");
            }
            return sb.ToString();
        }
    }
}
