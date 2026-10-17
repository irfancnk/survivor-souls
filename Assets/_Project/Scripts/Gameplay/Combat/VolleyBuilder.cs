using System.Text;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>One bolt of a volley: yaw off the aim line, launch delay, sideways offset and damage share.</summary>
    public struct VolleyArrow
    {
        /// <summary>Degrees around the up axis from the aim line (+ = right).</summary>
        public float Yaw;
        /// <summary>Seconds after the volley starts (0 = with the first bolt).</summary>
        public float Delay;
        /// <summary>Metres to the right of the muzzle (follow-up bolts sit a little to the side so they do not overlap).</summary>
        public float Lateral;
        /// <summary>Share of the shooter's bolt damage (1 = full).</summary>
        public float DamageScale;
        /// <summary>0 = the first bolts, N = the Nth Double Shot follow-up.</summary>
        public int Chain;
        /// <summary>The straight bolt of its chain step (Fired / FollowUpFired, recoil and sound go with it).</summary>
        public bool Lead;
    }

    /// <summary>Everything a volley depends on, read from the shooter's stats at the moment it fires (KAN-83).</summary>
    public struct VolleySpec
    {
        /// <summary>Extra angled bolts (Spread Shot): 2 = one each side of the main bolt, 4 = two each side.</summary>
        public int Spread;
        /// <summary>Degrees between neighbouring fan bolts.</summary>
        public float SpreadStep;
        /// <summary>Damage share of each angled bolt.</summary>
        public float SpreadDamage;
        /// <summary>Extra bolts fired one after another down the same lines (Double Shot).</summary>
        public int Chain;
        /// <summary>Seconds between two chain steps.</summary>
        public float ChainDelay;
        /// <summary>Sideways step (m) of the follow-up bolts: +1, -1, +2 ... steps.</summary>
        public float ChainLateral;
        /// <summary>Damage share of each follow-up bolt.</summary>
        public float ChainDamage;
        /// <summary>Most bolts in one volley (performance and balance cap).</summary>
        public int Cap;
        /// <summary>Front Arrow +1 (KAN-246): extra parallel forward bolts. The straight lanes spread evenly round the aim line.</summary>
        public int Front;
        /// <summary>Metres between two neighbouring forward lanes.</summary>
        public float FrontStep;
        /// <summary>Damage share of every forward-lane bolt (main included) while <see cref="Front"/> &gt; 0.</summary>
        public float FrontDamage;
        /// <summary>Rear Arrow +1 (KAN-246): bolts fired behind the hero (180 deg from the aim line).</summary>
        public int Rear;
        /// <summary>Degrees between two rear bolts.</summary>
        public float RearStep;
        /// <summary>Damage share of a rear bolt.</summary>
        public float RearDamage;
    }

    /// <summary>
    /// Builds the bolt list of one volley from the shooter's stats (KAN-83), so the weapon has no skill-specific code.
    /// Spread Shot: the main bolt flies down the aim line; the angled bolts fan out on both sides, nearest angles first
    /// (+step, -step, +2 steps, -2 steps ...). Double Shot: the whole fan fires again after each <see cref="VolleySpec.ChainDelay"/>,
    /// with a small sideways offset. Both together: every fan bolt is repeated by the chain. Over the cap, the last chain
    /// steps lose their outer bolts first (the straight bolts survive longest), so the volley never exceeds
    /// <see cref="VolleySpec.Cap"/>. Fills a caller-owned array: no allocation.
    /// KAN-246: each chain step is main bolt, fan, then the extra forward lanes (Front Arrow +1: the straight lanes sit side by
    /// side round the aim line, all at <see cref="VolleySpec.FrontDamage"/>), then the rear bolts (Rear Arrow +1). Multishot
    /// (the chain) repeats the whole pattern.
    /// </summary>
    public static class VolleyBuilder
    {
        /// <summary>Hard cap when a spec has none (also sizes the shooter's buffers).</summary>
        public const int DefaultCap = 12;

        /// <summary>Fan slot <paramref name="index"/> (0 = main): 0, +1, -1, +2, -2 ... steps.</summary>
        public static float FanYaw(int index, float step)
        {
            if (index <= 0) return 0f;
            int ring = (index + 1) / 2;
            return (index % 2 == 1 ? ring : -ring) * step;
        }

        /// <summary>Sideways offset of chain step <paramref name="chain"/>: 0, +1, -1, +2 ... steps.</summary>
        public static float ChainOffset(int chain, float step) => FanYaw(chain, step);

        /// <summary>Bolts the spec fires, after the cap.</summary>
        public static int Count(in VolleySpec spec)
        {
            int cap = spec.Cap > 0 ? spec.Cap : DefaultCap;
            int perStep = 1 + Mathf.Max(0, spec.Spread) + Mathf.Max(0, spec.Front) + Mathf.Max(0, spec.Rear);
            return Mathf.Min(cap, perStep * (1 + Mathf.Max(0, spec.Chain)));
        }

        /// <summary>Lateral offset (m) of straight lane <paramref name="lane"/> of <paramref name="lanes"/>, centred on the aim line.</summary>
        public static float LaneOffset(int lane, int lanes, float step) => (lane - (lanes - 1) * 0.5f) * step;

        /// <summary>Writes the volley into <paramref name="into"/> (first bolts first) and returns the bolt count.</summary>
        public static int Build(in VolleySpec spec, VolleyArrow[] into)
        {
            if (into == null || into.Length == 0) return 0;
            int fan = 1 + Mathf.Max(0, spec.Spread);
            int front = Mathf.Max(0, spec.Front);
            int rear = Mathf.Max(0, spec.Rear);
            int lanes = 1 + front;
            float laneDamage = front > 0 ? Mathf.Max(0f, spec.FrontDamage) : 1f;
            int chain = 1 + Mathf.Max(0, spec.Chain);
            int cap = Mathf.Min(spec.Cap > 0 ? spec.Cap : DefaultCap, into.Length);
            int n = 0;
            for (int c = 0; c < chain && n < cap; c++)
            {
                float chainScale = c == 0 ? 1f : Mathf.Max(0f, spec.ChainDamage);
                float lateral = ChainOffset(c, spec.ChainLateral);
                float delay = c * Mathf.Max(0f, spec.ChainDelay);
                for (int f = 0; f < fan && n < cap; f++)
                {
                    into[n++] = new VolleyArrow
                    {
                        Yaw = FanYaw(f, spec.SpreadStep),
                        Delay = delay,
                        Lateral = f == 0 && front > 0 ? lateral + LaneOffset(0, lanes, spec.FrontStep) : lateral,
                        DamageScale = (f == 0 ? laneDamage : Mathf.Max(0f, spec.SpreadDamage)) * chainScale,
                        Chain = c,
                        Lead = f == 0,
                    };
                }
                for (int l = 1; l < lanes && n < cap; l++)
                {
                    into[n++] = new VolleyArrow
                    {
                        Yaw = 0f, Delay = delay, Lateral = lateral + LaneOffset(l, lanes, spec.FrontStep),
                        DamageScale = laneDamage * chainScale, Chain = c,
                    };
                }
                for (int r = 0; r < rear && n < cap; r++)
                {
                    into[n++] = new VolleyArrow
                    {
                        Yaw = 180f + FanYaw(r, spec.RearStep), Delay = delay, Lateral = lateral,
                        DamageScale = Mathf.Max(0f, spec.RearDamage) * chainScale, Chain = c,
                    };
                }
            }
            return n;
        }

        /// <summary>Debug text, one bolt per line (Survivor Souls/Debug/Skills: Print Volley).</summary>
        public static string Describe(in VolleySpec spec, VolleyArrow[] arrows, int count)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Volley: {count} bolts (spread +{spec.Spread} at {spec.SpreadStep:0.#} deg, {spec.SpreadDamage:P0}; " +
                          $"double shot +{spec.Chain} every {spec.ChainDelay:0.###} s, {spec.ChainDamage:P0}; cap {spec.Cap})");
            for (int i = 0; i < count; i++)
            {
                var a = arrows[i];
                sb.AppendLine($"  #{i + 1,2}  chain {a.Chain}  t {a.Delay,5:0.000} s  yaw {a.Yaw,6:0.#}  side {a.Lateral,5:0.00} m  " +
                              $"damage {a.DamageScale:P0}{(a.Lead ? "  lead" : "")}");
            }
            return sb.ToString();
        }
    }
}
