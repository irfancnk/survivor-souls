using UnityEngine;

namespace SurvivorSouls.Editor.Audio
{
    /// <summary>
    /// Knight footsteps (KAN-235, KAN-81.2), in their own file so the shared SfxBuilder.Build list stays untouched.
    /// Soft and quiet on purpose (the KAN-15 snow steps irritated): a short low heel thump, a gentle surface layer, a very
    /// quiet cloth / leather layer, everything low-passed at ~5 kHz so a long run never hisses.
    /// <list type="bullet">
    /// <item>Snow (<see cref="StepSnowCount"/>): soft packed-snow crunch (dense fine crackle, slow swell, short tail).</item>
    /// <item>Shingle (<see cref="StepOtherCount"/>): a few small pebbles knocking under the boot.</item>
    /// <item>Wood (<see cref="StepOtherCount"/>): a dull hollow plank knock.</item>
    /// <item>Stone (<see cref="StepOtherCount"/>): a firm heel tap with a little grit.</item>
    /// </list>
    /// Odd variants (1, 3, ... = clip index 0, 2, ...) = left foot tone (a little lower), even variants = right foot tone.
    /// Built by <see cref="SceneSteps.FootstepSoundsStep"/> on every Game scene rebuild (deterministic, same files).
    /// </summary>
    public static partial class SfxBuilder
    {
        public const string StepSnowName = "sfx_step_snow";
        public const string StepShingleName = "sfx_step_shingle";
        public const string StepWoodName = "sfx_step_wood";
        public const string StepStoneName = "sfx_step_stone";
        public const int StepSnowCount = 8;
        public const int StepOtherCount = 6;
        /// <summary>Final low-pass of every step (Hz).</summary>
        public const float StepLowPass = 5000f;

        public struct FootstepClips
        {
            public AudioClip[] Snow, Shingle, Wood, Stone;
        }

        public static FootstepClips BuildFootsteps()
        {
            System.IO.Directory.CreateDirectory(Dir);
            return new FootstepClips
            {
                Snow = Many(StepSnowName, StepSnowCount, StepSnow),
                Shingle = Many(StepShingleName, StepOtherCount, StepShingle),
                Wood = Many(StepWoodName, StepOtherCount, StepWood),
                Stone = Many(StepStoneName, StepOtherCount, StepStone),
            };
        }

        /// <summary>Variant 1, 3, 5 ... = left foot (lower tone), 2, 4, 6 ... = right foot.</summary>
        public static bool StepIsLeft(int variant) => (variant & 1) == 1;

        /// <summary>Foot tone factor: left a little lower than right (~1.5 semitones apart).</summary>
        private static float StepTone(int variant) => StepIsLeft(variant) ? 0.95f : 1.04f;

        /// <summary>Soft snow step <paramref name="variant"/> (1..<see cref="StepSnowCount"/>), ~0.26 s. Public for tests.</summary>
        public static float[] StepSnow(int variant)
        {
            var rng = new System.Random(variant * 5113 + 235);
            float tone = StepTone(variant), v = 1f + (float)(rng.NextDouble() - 0.5) * 0.08f;
            var b = new float[(int)(Rate * 0.26f)];
            Sweep(b, 0f, 0.09f, 88f * tone * v, 62f * tone * v, 0.55f, 34f, 2);              // heel into the snow
            NoiseBurst(b, 0f, 0.07f, 0.22f, 70f, 380f, 40f, rng);                            // muffled thud body
            Crackle(b, 0.012f, 0.17f, 0.32f, 1400f + 250f * (variant % 3), 900f * tone, 3600f * tone, 0.045f, rng); // packed crunch
            Crackle(b, 0.06f, 0.12f, 0.12f, 700f, 600f * tone, 2200f * tone, 0.03f, rng);    // toe roll, softer
            ClothLayer(b, 0.0f, 0.2f, rng);
            return StepFinish(b, 0.8f);
        }

        /// <summary>Shingle step <paramref name="variant"/> (1..<see cref="StepOtherCount"/>), ~0.24 s. Public for tests.</summary>
        public static float[] StepShingle(int variant)
        {
            var rng = new System.Random(variant * 3301 + 81);
            float tone = StepTone(variant);
            var b = new float[(int)(Rate * 0.24f)];
            Sweep(b, 0f, 0.08f, 96f * tone, 70f * tone, 0.45f, 38f, 2);
            int pebbles = 4 + variant % 3;
            for (int i = 0; i < pebbles; i++)
            {
                float t = 0.008f + (float)rng.NextDouble() * 0.11f, f = (1700f + (float)rng.NextDouble() * 1500f) * tone;
                Sweep(b, t, 0.03f, f, f * 0.97f, 0.1f + (float)rng.NextDouble() * 0.08f, 140f, 1); // stone knock ring
                Click(b, t, 0.002f, 0.12f, 4200f, rng);
            }
            Crackle(b, 0.01f, 0.12f, 0.1f, 500f, 1200f, 3500f, 0.03f, rng); // grit settling
            ClothLayer(b, 0f, 0.18f, rng);
            return StepFinish(b, 0.78f);
        }

        /// <summary>Wood step <paramref name="variant"/> (1..<see cref="StepOtherCount"/>), ~0.22 s. Public for tests.</summary>
        public static float[] StepWood(int variant)
        {
            var rng = new System.Random(variant * 2711 + 17);
            float tone = StepTone(variant), v = 1f + (float)(rng.NextDouble() - 0.5) * 0.08f;
            var b = new float[(int)(Rate * 0.22f)];
            float f0 = 150f * tone * v;
            Sweep(b, 0f, 0.16f, f0 * 1.03f, f0, 0.5f, 30f, 1);         // plank body
            Sweep(b, 0f, 0.1f, f0 * 2.3f, f0 * 2.3f, 0.2f, 55f, 1);     // wood mode
            Sweep(b, 0f, 0.07f, f0 * 4.1f, f0 * 4.1f, 0.08f, 80f, 1);
            Sweep(b, 0f, 0.12f, 70f * tone, 55f * tone, 0.35f, 30f, 1); // hollow under the planks
            Click(b, 0f, 0.003f, 0.12f, 2600f, rng);                     // heel contact
            Sweep(b, 0.05f, 0.06f, f0 * 1.6f, f0 * 1.5f, 0.06f, 60f, 1); // toe knock
            ClothLayer(b, 0f, 0.18f, rng);
            return StepFinish(b, 0.75f);
        }

        /// <summary>Stone step <paramref name="variant"/> (1..<see cref="StepOtherCount"/>), ~0.2 s. Public for tests.</summary>
        public static float[] StepStone(int variant)
        {
            var rng = new System.Random(variant * 1931 + 33);
            float tone = StepTone(variant);
            var b = new float[(int)(Rate * 0.2f)];
            Sweep(b, 0f, 0.07f, 125f * tone, 95f * tone, 0.5f, 50f, 2); // firm heel
            Click(b, 0f, 0.003f, 0.2f, 3200f * tone, rng);
            NoiseBurst(b, 0.002f, 0.05f, 0.12f, 1500f, 4200f, 60f, rng); // leather sole on stone
            Crackle(b, 0.03f, 0.09f, 0.08f, 600f, 1800f, 4200f, 0.02f, rng); // grit under the toe
            ClothLayer(b, 0f, 0.17f, rng);
            return StepFinish(b, 0.72f);
        }

        // ------------------------------------------------------------ footstep DSP parts

        /// <summary>
        /// Fine crackle: random tiny impulses (about <paramref name="density"/> per second) through a lo..hi band, under a
        /// quick swell (<paramref name="attack"/> s) and a falling tail. Packed snow / grit.
        /// </summary>
        private static void Crackle(float[] b, float start, float length, float amp, float density, float lo, float hi, float attack, System.Random rng)
        {
            int s = (int)(start * Rate), e = Mathf.Min(b.Length, s + (int)(length * Rate));
            var hp = new Biquad(); hp.SetHighPass(lo, 0.7f, Rate);
            var lp = new Biquad(); lp.SetLowPass(hi, 0.7f, Rate);
            float p = density / Rate;
            for (int i = s; i < e; i++)
            {
                float t = (i - s) / (float)Rate, k = (i - s) / (float)(e - s);
                float x = rng.NextDouble() < p ? Noise(rng) * (0.4f + (float)rng.NextDouble() * 0.6f) * 6f : 0f;
                x += Noise(rng) * 0.08f; // soft bed under the grains
                float env = Mathf.Min(1f, t / Mathf.Max(0.002f, attack)) * (1f - k) * (1f - k);
                b[i] += lp.Process(hp.Process(x)) * env * amp;
            }
        }

        /// <summary>Very quiet cloth / leather layer: a soft rustle and a tiny low creak.</summary>
        private static void ClothLayer(float[] b, float start, float length, System.Random rng)
        {
            NoiseBurst(b, start + 0.01f, length, 0.035f, 500f, 2200f, 14f, rng);
            Sweep(b, start + 0.02f, 0.06f, 310f, 290f, 0.025f, 40f, 2);
        }

        /// <summary>Low-pass at <see cref="StepLowPass"/>, then normalize with a click-free start and end.</summary>
        private static float[] StepFinish(float[] b, float peak)
        {
            var lp = new Biquad(); lp.SetLowPass(StepLowPass, 0.7f, Rate);
            var lp2 = new Biquad(); lp2.SetLowPass(StepLowPass, 0.7f, Rate);
            for (int i = 0; i < b.Length; i++) b[i] = lp2.Process(lp.Process(b[i]));
            return Finish(b, peak);
        }
    }
}
