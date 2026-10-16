using SurvivorSouls.Gameplay.Skills;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Horde scaling to the Knight's power (KAN-92). Regular zombies spawn with max health x
    /// <see cref="DynamicMultiplier"/> on top of the Chamber multiplier (KAN-46):
    /// <list type="bullet">
    /// <item>Power index P = weighted geometric mean of single-target DPS and crowd DPS over the bare Knight
    /// (<see cref="PowerSnapshot"/> formulas, the same as the KAN-75 power curve): single ^ (1 - w) x crowd ^ w with
    /// w = <see cref="crowdWeight"/>. Both count, so a crowd build does not read as 30x stronger against one zombie,
    /// and the Knight keeps a single-target edge (crowd DPS grows much faster, KAN-86).</item>
    /// <item>Health x P ^ <see cref="powerExponent"/>. Below 1, so every skill still makes the Knight stronger: he nets
    /// P ^ (1 - exponent).</item>
    /// <item>Health x (1 + <see cref="timeGrowth"/> x Chamber progress): zombies get tougher through the 2 minute Chamber.</item>
    /// </list>
    /// Spawn pressure (KAN-113): the same power index also raises the field's refill, so a strong crowd build does not
    /// clear the field faster than the table refills it. Max alive and burst sizes x P ^ <see cref="capExponent"/>
    /// (at most <see cref="maxCapFactor"/>), group and burst intervals x P ^ -<see cref="intervalExponent"/> (at least
    /// <see cref="minIntervalFactor"/>). Pressure never lifts max alive past <see cref="maxAliveBudget"/> (phone budget,
    /// KAN-44); the table's own cap (Chamber scaling included) is never cut.
    /// Danger tiers: the total health multiplier (Chamber x dynamic) maps to a tier value between the
    /// <see cref="tierThresholds"/>; the body colour blends between the <see cref="tierColors"/>, quantised to
    /// <see cref="stepsPerTier"/> steps per tier (one shared material per step and zombie type, so the SRP Batcher stays on).
    /// Bosses are never scaled or tinted here. Edit the asset; the builder never overwrites it.
    /// </summary>
    [CreateAssetMenu(menuName = "Survivor Souls/Horde Scaling", fileName = "HordeScaling")]
    public sealed class HordeScaling : ScriptableObject
    {
        [Header("Power")]
        [Tooltip("Share of crowd DPS in the power index (0 = single target only, 0.5 = plain geometric mean).")]
        [SerializeField, Range(0f, 1f)] private float crowdWeight = 0.35f;
        [Tooltip("Zombie health x power index ^ this. 0 = no power scaling, 1 = skills never pay off.")]
        [SerializeField, Range(0f, 1f)] private float powerExponent = 0.6f;
        [Tooltip("Extra zombie health at the end of the Chamber clock (0.3 = +30 %), linear from the start.")]
        [SerializeField, Min(0f)] private float timeGrowth = 0.3f;
        [Tooltip("Cap of the dynamic multiplier (power x time). The Chamber multiplier comes on top.")]
        [SerializeField, Min(1f)] private float maxMultiplier = 6f;

        [Header("Spawn pressure (KAN-113)")]
        [Tooltip("Max alive and burst sizes x power index ^ this. 0 = the table's caps whatever the power.")]
        [SerializeField, Range(0f, 1f)] private float capExponent = 0.3f;
        [Tooltip("Most the power index raises max alive and burst sizes (1.6 = +60 %).")]
        [SerializeField, Min(1f)] private float maxCapFactor = 1.6f;
        [Tooltip("Group and burst intervals x power index ^ -this. 0 = the table's intervals whatever the power.")]
        [SerializeField, Range(0f, 1f)] private float intervalExponent = 0.2f;
        [Tooltip("Shortest interval factor from the power index (0.7 = refills at most 1 / 0.7 = 1.43x as often).")]
        [SerializeField, Range(0.1f, 1f)] private float minIntervalFactor = 0.7f;
        [Tooltip("Spawn pressure never lifts max alive above this (phone budget, KAN-44). The table's own cap is never cut.")]
        [SerializeField, Min(1)] private int maxAliveBudget = 50;

        [Header("Danger tiers")]
        [Tooltip("Total health multiplier (Chamber x dynamic) where each tier starts, ascending. Index 0 = calm (1).")]
        [SerializeField] private float[] tierThresholds = { 1f, 1.6f, 2.5f, 4f, 6.5f };
        [Tooltip("Body colour of each tier, multiplied with the zombie type's own tint. Same count as the thresholds.")]
        [SerializeField] private Color[] tierColors =
        {
            new Color(1f, 1f, 1f),          // calm: the type's own colour
            new Color(0.82f, 1.05f, 0.55f), // sickly: bile green
            new Color(1.2f, 0.85f, 0.4f),   // feral: amber
            new Color(1.3f, 0.48f, 0.36f),  // blood-mad: crimson
            new Color(0.78f, 0.42f, 1.15f), // abyssal: violet
        };
        [Tooltip("Colour steps between two tiers. More = smoother, one more material per step and zombie type.")]
        [SerializeField, Range(1, 8)] private int stepsPerTier = 3;

        public float CrowdWeight => crowdWeight;
        public float PowerExponent => powerExponent;
        public float TimeGrowth => timeGrowth;
        public float MaxMultiplier => maxMultiplier;
        public float CapExponent => capExponent;
        public float MaxCapFactor => maxCapFactor;
        public float IntervalExponent => intervalExponent;
        public float MinIntervalFactor => minIntervalFactor;
        public int MaxAliveBudget => maxAliveBudget;
        public int TierCount => Mathf.Min(tierThresholds?.Length ?? 0, tierColors?.Length ?? 0);
        public int StepsPerTier => stepsPerTier;
        /// <summary>Colour steps from calm to the last tier (step 0 = calm, no tint).</summary>
        public int StepCount => Mathf.Max(1, (TierCount - 1) * stepsPerTier + 1);

        /// <summary>
        /// Power index of <paramref name="now"/> against <paramref name="bare"/> (the Knight with no skills):
        /// single ratio ^ (1 - <paramref name="crowdShare"/>) x crowd ratio ^ <paramref name="crowdShare"/>. 1 = no skills.
        /// Never below 1: a debuff never makes zombies weaker.
        /// </summary>
        public static float PowerIndex(in PowerSnapshot now, in PowerSnapshot bare, float crowdShare = 0.5f)
        {
            float single = Mathf.Max(0.0001f, now.SingleTargetDps / Mathf.Max(0.0001f, bare.SingleTargetDps));
            float crowd = Mathf.Max(0.0001f, now.CrowdDps / Mathf.Max(0.0001f, bare.CrowdDps));
            float w = Mathf.Clamp01(crowdShare);
            return Mathf.Max(1f, Mathf.Pow(single, 1f - w) * Mathf.Pow(crowd, w));
        }

        /// <summary>Power index with this asset's <see cref="CrowdWeight"/>.</summary>
        public float PowerOf(in PowerSnapshot now, in PowerSnapshot bare) => PowerIndex(now, bare, crowdWeight);

        /// <summary>Health factor of power index <paramref name="power"/>: P ^ exponent.</summary>
        public float PowerMultiplier(float power) => Mathf.Pow(Mathf.Max(1f, power), powerExponent);

        /// <summary>Health factor at <paramref name="progress"/> of the Chamber clock (0 = start, 1 = portal opens).</summary>
        public float TimeMultiplier(float progress) => 1f + timeGrowth * Mathf.Clamp01(progress);

        /// <summary>Power x time, clamped to [1, <see cref="MaxMultiplier"/>].</summary>
        public float DynamicMultiplier(float power, float progress) =>
            Mathf.Clamp(PowerMultiplier(power) * TimeMultiplier(progress), 1f, maxMultiplier);

        /// <summary>Spawn pressure (KAN-113): max alive and burst size factor of power index <paramref name="power"/>.</summary>
        public float CapFactor(float power) =>
            Mathf.Clamp(Mathf.Pow(Mathf.Max(1f, power), capExponent), 1f, Mathf.Max(1f, maxCapFactor));

        /// <summary>Spawn pressure (KAN-113): group and burst interval factor of power index <paramref name="power"/> (1 or less).</summary>
        public float IntervalFactor(float power) =>
            Mathf.Clamp(Mathf.Pow(Mathf.Max(1f, power), -intervalExponent), Mathf.Clamp(minIntervalFactor, 0.1f, 1f), 1f);

        /// <summary>
        /// Max alive after spawn pressure: <paramref name="tableCap"/> (the phase cap after Chamber scaling) x
        /// <paramref name="capFactor"/>, but pressure never lifts it past <see cref="MaxAliveBudget"/> and never cuts it
        /// below <paramref name="tableCap"/>.
        /// </summary>
        public int PressuredMaxAlive(int tableCap, float capFactor)
        {
            int pressured = Mathf.RoundToInt(tableCap * Mathf.Max(1f, capFactor));
            return Mathf.Max(tableCap, Mathf.Min(pressured, maxAliveBudget));
        }

        /// <summary>
        /// Continuous tier of a zombie with total health multiplier <paramref name="total"/>: 0 at or below the first
        /// threshold, i + f between threshold i and i + 1, the last tier index at or above the last threshold.
        /// </summary>
        public float TierValue(float total)
        {
            int n = TierCount;
            if (n < 2 || total <= tierThresholds[0]) return 0f;
            for (int i = 0; i < n - 1; i++)
            {
                float lo = tierThresholds[i], hi = tierThresholds[i + 1];
                if (total < hi) return i + Mathf.Clamp01((total - lo) / Mathf.Max(0.0001f, hi - lo));
            }
            return n - 1;
        }

        /// <summary>Colour step of <paramref name="tierValue"/> (floor, so a step is reached, never rounded up).</summary>
        public int StepOf(float tierValue) => Mathf.Clamp(Mathf.FloorToInt(tierValue * stepsPerTier + 0.0001f), 0, StepCount - 1);

        /// <summary>Tier colour of step <paramref name="step"/>: a blend of the two tiers it sits between.</summary>
        public Color StepColor(int step)
        {
            int n = TierCount;
            if (n == 0) return Color.white;
            if (n == 1) return tierColors[0];
            float t = Mathf.Clamp(step, 0, StepCount - 1) / (float)stepsPerTier;
            int i = Mathf.Min(Mathf.FloorToInt(t), n - 2);
            return Color.LerpUnclamped(tierColors[i], tierColors[i + 1], t - i);
        }

        /// <summary>
        /// Balance table (KAN-92 debug): per Hunter level of <paramref name="curve"/> the power index, the zombie health
        /// factor at the start and end of a Chamber, the Knight's net edge (DPS ratio / health factor) and the
        /// single-target time to kill a zombie of <paramref name="zombieHealth"/> HP at Chamber multiplier
        /// <paramref name="chamberHealth"/>. Debug only: allocates.
        /// </summary>
        public string Table(SkillPowerCurve curve, float zombieHealth = 100f, float chamberHealth = 1f)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"Horde scaling: P = 1-target^{1f - crowdWeight:0.##} x crowd^{crowdWeight:0.##}, health x P^{powerExponent:0.##} x (1 + {timeGrowth:0.##} x clock), cap x{maxMultiplier:0.#}; " +
                          $"pressure: caps x P^{capExponent:0.##} (<= x{maxCapFactor:0.##}, <= {maxAliveBudget} alive), intervals x P^-{intervalExponent:0.##} (>= x{minIntervalFactor:0.##}); " +
                          $"zombie {zombieHealth:0} HP, Chamber x{chamberHealth:0.##}.");
            sb.AppendLine("Lv  1-target  crowd   power  hp start  hp end  edge 1-target  edge crowd  TTK start s  TTK end s  tier start  tier end  cap   interval");
            var bare = curve.AtLevel(1);
            for (int level = 1; level <= curve.MaxLevel; level++)
            {
                var s = curve.AtLevel(level);
                float single = s.SingleTargetDps / Mathf.Max(0.0001f, bare.SingleTargetDps);
                float crowd = s.CrowdDps / Mathf.Max(0.0001f, bare.CrowdDps);
                float p = PowerOf(s, bare);
                float start = DynamicMultiplier(p, 0f), end = DynamicMultiplier(p, 1f);
                float ttkStart = zombieHealth * chamberHealth * start / Mathf.Max(0.0001f, s.SingleTargetDps);
                float ttkEnd = zombieHealth * chamberHealth * end / Mathf.Max(0.0001f, s.SingleTargetDps);
                sb.AppendLine($"{level,2}  {single,7:0.00}x  {crowd,5:0.0}x  {p,5:0.00}  {start,7:0.00}x  {end,5:0.00}x  " +
                              $"{single / start,12:0.00}x  {crowd / start,9:0.00}x  {ttkStart,11:0.00}  {ttkEnd,9:0.00}  " +
                              $"{TierValue(chamberHealth * start),10:0.00}  {TierValue(chamberHealth * end),8:0.00}  " +
                              $"{CapFactor(p),4:0.00}x  {IntervalFactor(p),5:0.00}x");
            }
            return sb.ToString();
        }

        /// <summary>Tests and tools: rules without an asset.</summary>
        public static HordeScaling Create(float exponent = 0.6f, float time = 0.3f, float max = 6f, float[] thresholds = null,
            Color[] colors = null, int steps = 3, float crowd = 0.35f)
        {
            var s = CreateInstance<HordeScaling>();
            s.capExponent = 0f; // tests opt in with SetPressure; old tests keep the table's caps
            s.intervalExponent = 0f;
            s.crowdWeight = crowd;
            s.powerExponent = exponent;
            s.timeGrowth = time;
            s.maxMultiplier = max;
            if (thresholds != null) s.tierThresholds = thresholds;
            if (colors != null) s.tierColors = colors;
            s.stepsPerTier = steps;
            return s;
        }

        /// <summary>Tests and tools: spawn pressure rules (KAN-113).</summary>
        public HordeScaling SetPressure(float cap, float maxCap, float interval, float minInterval, int budget)
        {
            capExponent = Mathf.Clamp01(cap);
            maxCapFactor = Mathf.Max(1f, maxCap);
            intervalExponent = Mathf.Clamp01(interval);
            minIntervalFactor = Mathf.Clamp(minInterval, 0.1f, 1f);
            maxAliveBudget = Mathf.Max(1, budget);
            return this;
        }
    }
}
