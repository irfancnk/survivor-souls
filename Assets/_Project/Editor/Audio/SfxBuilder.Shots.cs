using UnityEngine;

namespace SurvivorSouls.Editor.Audio
{
    /// <summary>
    /// Crossbow shot channel clips (KAN-236, KAN-81.3), in their own file so the shared SfxBuilder.Build list stays untouched.
    /// <list type="bullet">
    /// <item><see cref="ShotVariantCount"/> crossbow shot variants: the KAN-231 tone (latch tick, dry wooden thock with struck-wood
    /// modes 1 : 2.57 : 4.3 : 6.1, stock clack, low string thrum, bolt whoosh), each variant with its own body pitch (evenly
    /// spread over +-3 %), mode balance, latch brightness, thrum and whoosh length. Not a new design: 8 takes of the same shot.</item>
    /// <item><see cref="VolleyLayerCount"/> volley layers for a multi-bolt volley (Spread Shot): a few staggered string thrums and
    /// bolt whooshes, no wood knock (the shot under it has one). Played on the same voice as the shot: one sound per volley.</item>
    /// </list>
    /// The 3 old <c>sfx_crossbow_shot_01..03</c> stay for the boss fan volley (pitched down) and as the fallback.
    /// Built by <see cref="SceneSteps.ShotChannelStep"/> on every Game scene rebuild (deterministic, same files).
    /// </summary>
    public static partial class SfxBuilder
    {
        public const string ShotVariantName = "sfx_crossbow_shot_var";
        public const string VolleyLayerName = "sfx_crossbow_volley_layer";
        public const int ShotVariantCount = 8;
        public const int VolleyLayerCount = 2;
        public const int ShotRate = Rate;

        public struct ShotClips
        {
            public AudioClip[] Variants, VolleyLayers;
        }

        public static ShotClips BuildShotChannel()
        {
            System.IO.Directory.CreateDirectory(Dir);
            return new ShotClips
            {
                Variants = Many(ShotVariantName, ShotVariantCount, CrossbowShotVariant),
                VolleyLayers = Many(VolleyLayerName, VolleyLayerCount, CrossbowVolleyLayer),
            };
        }

        /// <summary>Body pitch factor of shot variant <paramref name="seed"/> (1-based): evenly spread over 0.97..1.03, interleaved.</summary>
        public static float ShotVariantPitch(int seed)
        {
            // 1..8 -> 0, 4, 1, 5, 2, 6, 3, 7: neighbours in the file list are not neighbours in pitch.
            int k = (seed - 1) % ShotVariantCount;
            int step = (k % 2 == 0 ? k / 2 : k / 2 + ShotVariantCount / 2);
            return 0.97f + 0.06f * step / (ShotVariantCount - 1);
        }

        /// <summary>
        /// Crossbow shot variant <paramref name="seed"/> (1-based, KAN-236): the KAN-231 <c>CrossbowShot</c> layers with a
        /// per-variant body pitch (<see cref="ShotVariantPitch"/>), mode balance (+-15 %), latch brightness, thrum pitch and
        /// whoosh length. ~0.5 s, peak 0.9.
        /// </summary>
        public static float[] CrossbowShotVariant(int seed)
        {
            var rng = new System.Random(seed * 2357 + 11);
            float J(float spread) => 1f + (float)(rng.NextDouble() * 2.0 - 1.0) * spread;
            var b = new float[(int)(Rate * 0.5f)];
            float v = ShotVariantPitch(seed);
            float f0 = 205f * v;                                                     // stock / prod body
            Click(b, 0f, 0.003f, 0.32f * J(0.15f), 4200f * J(0.14f), rng);            // trigger latch
            Sweep(b, 0.003f, 0.30f, f0 * 1.04f, f0, 0.62f, 26f * J(0.1f), 1);         // wood modes: fundamental (slight pitch settle)
            Sweep(b, 0.003f, 0.18f, f0 * 2.57f, f0 * 2.57f, 0.34f * J(0.15f), 42f, 1);
            Sweep(b, 0.003f, 0.12f, f0 * 4.30f, f0 * 4.30f, 0.20f * J(0.15f), 62f, 1);
            Sweep(b, 0.003f, 0.08f, f0 * 6.10f, f0 * 6.10f, 0.10f * J(0.15f), 85f, 1);
            NoiseBurst(b, 0.002f, 0.035f, 0.42f * J(0.1f), 300f, 1500f * J(0.1f), 70f, rng);   // knock: the hit itself
            NoiseBurst(b, 0.006f, 0.025f, 0.22f * J(0.15f), 1600f, 4200f * J(0.1f), 110f, rng); // stock clack
            float thrum = J(0.04f);
            Sweep(b, 0.004f, 0.30f, 118f * thrum, 84f * thrum, 0.34f * J(0.12f), 11f * J(0.15f), 2); // low string thrum
            NoiseBurst(b, 0.012f, 0.20f * J(0.2f), 0.17f * J(0.15f), 700f, 2600f * J(0.1f), 12f, rng); // bolt whoosh
            return Normalize(b, 0.9f);
        }

        /// <summary>
        /// Volley layer <paramref name="seed"/> (1-based, KAN-236): 4 string thrums 5-9 ms apart (slightly detuned) and 4 bolt
        /// whooshes fanning out (each a little higher and later), no wood knock. ~0.4 s, peak 0.7: a "wider" shot under the main one.
        /// </summary>
        public static float[] CrossbowVolleyLayer(int seed)
        {
            var rng = new System.Random(seed * 2741 + 5);
            var b = new float[(int)(Rate * 0.4f)];
            float t = 0.004f;
            for (int i = 0; i < 4; i++)
            {
                float d = 1f + (i - 1.5f) * 0.025f + (float)(rng.NextDouble() - 0.5) * 0.01f;
                Sweep(b, t, 0.24f, 122f * d, 86f * d, 0.22f, 13f, 2);                            // string thrum
                NoiseBurst(b, t + 0.01f, 0.22f, 0.13f, 800f + i * 120f, 2800f + i * 260f, 11f, rng); // bolt whoosh
                t += 0.005f + (float)rng.NextDouble() * 0.004f;
            }
            return Normalize(b, 0.7f);
        }

        /// <summary>Bolt impact clip samples (KAN-236 limiter mix test): the shared <c>BoltImpact</c>.</summary>
        public static float[] ImpactSamples(int seed) => BoltImpact(seed);

        /// <summary>Hit marker tick samples (KAN-236 limiter mix test): the shared <c>HitMarkerTick</c>.</summary>
        public static float[] HitMarkerSamples(int seed) => HitMarkerTick(seed);
    }
}
