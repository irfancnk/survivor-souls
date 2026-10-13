using System.Collections.Generic;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Characters
{
    /// <summary>
    /// Measures how fast an in-place locomotion clip "walks" (KAN-30): samples the clip on a rig and tracks the feet while
    /// they are on the ground. A planted foot slides backwards (relative to the root) at exactly the speed the character
    /// must move for that foot to stay still in the world. So <see cref="Result.Speed"/> is the clip's reference ground
    /// speed: play it at rate = real speed / Speed and nothing slides.
    /// <see cref="Result.Spread"/> is the relative spread of the contact samples (0 = perfectly even stance phases).
    /// </summary>
    public static class GaitMeasure
    {
        public struct Result
        {
            /// <summary>Median backwards speed of grounded feet (m/s, in <c>frame</c> units).</summary>
            public float Speed;
            /// <summary>(p90 - p10) / median of the contact samples. Low = feet move evenly while planted.</summary>
            public float Spread;
            /// <summary>Contact sample pairs used.</summary>
            public int Samples;

            public override string ToString() => $"speed={Speed:F3} m/s spread={Spread:F3} samples={Samples}";
        }

        /// <summary>
        /// Samples <paramref name="clip"/> on <paramref name="animated"/> (the GameObject whose paths the clip uses; disable
        /// its Animator first) at <paramref name="samples"/> points over one loop. Foot positions are read in
        /// <paramref name="frame"/> space, forward = its +Z. A foot counts as grounded within <paramref name="contactBand"/> m
        /// of its lowest point in the loop. Leaves the rig in the last sampled pose.
        /// </summary>
        public static Result GroundSpeed(GameObject animated, AnimationClip clip, Transform frame, IReadOnlyList<Transform> feet,
            int samples = 240, float contactBand = 0.015f)
        {
            int n = Mathf.Max(8, samples);
            float dt = clip.length / n;
            var heights = new float[feet.Count, n];
            var forward = new float[feet.Count, n];
            for (int s = 0; s < n; s++)
            {
                clip.SampleAnimation(animated, s * dt);
                for (int f = 0; f < feet.Count; f++)
                {
                    Vector3 p = frame.InverseTransformPoint(feet[f].position);
                    heights[f, s] = p.y;
                    forward[f, s] = p.z;
                }
            }

            var speeds = new List<float>();
            for (int f = 0; f < feet.Count; f++)
            {
                float min = float.PositiveInfinity;
                for (int s = 0; s < n; s++) min = Mathf.Min(min, heights[f, s]);
                for (int s = 0; s < n; s++)
                {
                    int t = (s + 1) % n; // loop: last sample pairs with the first
                    if (heights[f, s] > min + contactBand || heights[f, t] > min + contactBand) continue;
                    float v = -(forward[f, t] - forward[f, s]) / dt;
                    if (v > 0f) speeds.Add(v); // foot travelling backwards = stance (a foot creeping forward on the ground is not)
                }
            }
            if (speeds.Count == 0) return default;
            speeds.Sort();
            float median = speeds[speeds.Count / 2];
            float p10 = speeds[(int)(speeds.Count * 0.1f)];
            float p90 = speeds[Mathf.Min(speeds.Count - 1, (int)(speeds.Count * 0.9f))];
            return new Result { Speed = median, Spread = median > 0f ? (p90 - p10) / median : 0f, Samples = speeds.Count };
        }
    }
}
