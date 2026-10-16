using System;
using UnityEngine;

namespace SurvivorSouls.Editor.Audio
{
    /// <summary>
    /// Zombie death sounds (KAN-234, KAN-81.1), in their own file so the shared SfxBuilder.Build list stays untouched.
    /// <list type="bullet">
    /// <item><see cref="ZombieVoiceCount"/> death voices, each a different design (not one sound with a new seed): wet gurgling
    /// groan, short rasp, low moan fading out, brittle bone crack + grunt, gasp, heavy grunt, cut-off growl, wheezing exhale.
    /// Mixed short (0.4 s) and long (1.3 s). Dry, no reverb, highs rolled off so a horde does not hiss.</item>
    /// <item><see cref="ZombieFallCount"/> body-fall thumps (cloth and bone, low): played as a delayed layer under the voice.</item>
    /// <item><see cref="ZombieMassCount"/> "mass death" clips: several voices and falls layered, for many kills in one moment.</item>
    /// </list>
    /// Built by <see cref="SceneSteps.ZombieDeathSoundsStep"/> on every Game scene rebuild (deterministic, same files).
    /// </summary>
    public static partial class SfxBuilder
    {
        public const string ZombieVoiceName = "sfx_zombie_death";
        public const string ZombieFallName = "sfx_zombie_fall";
        public const string ZombieMassName = "sfx_zombie_mass_death";
        public const int ZombieVoiceCount = 8;
        public const int ZombieFallCount = 3;
        public const int ZombieMassCount = 2;

        public struct ZombieDeathClips
        {
            public AudioClip[] Voices, Falls, Masses;
        }

        public static ZombieDeathClips BuildZombieDeaths()
        {
            System.IO.Directory.CreateDirectory(Dir);
            return new ZombieDeathClips
            {
                Voices = Many(ZombieVoiceName, ZombieVoiceCount, ZombieDeathVoice),
                Falls = Many(ZombieFallName, ZombieFallCount, ZombieBodyFall),
                Masses = Many(ZombieMassName, ZombieMassCount, ZombieMassDeath),
            };
        }

        /// <summary>Death voice <paramref name="variant"/> (1..<see cref="ZombieVoiceCount"/>). Public for tests.</summary>
        public static float[] ZombieDeathVoice(int variant)
        {
            var rng = new System.Random(variant * 7919 + 234);
            float v = 1f + (float)(rng.NextDouble() - 0.5) * 0.06f;
            float[] b;
            switch ((variant - 1) % ZombieVoiceCount)
            {
                case 0: // wet gurgling groan: falling voice, bubbling throat noise under it
                    b = new float[(int)(Rate * 0.95f)];
                    ThroatVoice(b, 0f, 0.8f, 118f * v, 66f * v, 620f, 400f, 1050f, 780f, 1f, 0.05f, 0f, 0f, 0.3f, rng);
                    Gurgle(b, 0.08f, 0.7f, 0.55f, 21f, rng);
                    return Finish(b, 0.85f);
                case 1: // short rasp: hoarse breath through the teeth, choked off
                    b = new float[(int)(Rate * 0.42f)];
                    ThroatVoice(b, 0f, 0.3f, 150f * v, 120f * v, 900f, 700f, 2100f, 1600f, 0.45f, 0.012f, 0f, 0f, 0.85f, rng);
                    NoiseBurst(b, 0f, 0.32f, 0.35f, 900f, 3200f, 7f, rng);
                    return Finish(b, 0.8f);
                case 2: // low moan fading out: slow vowel change (ah -> oh), long soft tail
                    b = new float[(int)(Rate * 1.3f)];
                    ThroatVoice(b, 0f, 1.25f, 96f * v, 58f * v, 720f, 420f, 1150f, 760f, 1f, 0.14f, 4.5f, 0.03f, 0.18f, rng);
                    return Finish(b, 0.8f);
                case 3: // brittle crack + grunt: two dry bone snaps, a short low grunt
                    b = new float[(int)(Rate * 0.55f)];
                    BoneSnap(b, 0f, 0.3f, rng);
                    BoneSnap(b, 0.035f, 0.2f, rng);
                    ThroatVoice(b, 0.06f, 0.32f, 110f * v, 80f * v, 560f, 430f, 980f, 820f, 1.2f, 0.015f, 0f, 0f, 0.25f, rng);
                    return Finish(b, 0.82f);
                case 4: // gasp: a sharp breath in, then a choked "uh"
                    b = new float[(int)(Rate * 0.5f)];
                    Breath(b, 0f, 0.17f, 0.55f, 1300f, 3800f, rising: true, rng);
                    ThroatVoice(b, 0.17f, 0.24f, 142f * v, 98f * v, 650f, 520f, 1250f, 980f, 0.85f, 0.008f, 0f, 0f, 0.35f, rng);
                    return Finish(b, 0.78f);
                case 5: // heavy grunt: deep chest voice, wide formants, pushed out
                    b = new float[(int)(Rate * 0.6f)];
                    ThroatVoice(b, 0f, 0.5f, 82f * v, 60f * v, 480f, 360f, 860f, 700f, 1.1f, 0.02f, 0f, 0f, 0.2f, rng);
                    Sweep(b, 0f, 0.45f, 64f * v, 52f * v, 0.28f, 6f, 2); // chest under the voice
                    return Finish(b, 0.84f);
                case 6: // cut-off growl: rough, fast-modulated throat, ends on a click
                    b = new float[(int)(Rate * 0.45f)];
                    ThroatVoice(b, 0f, 0.4f, 132f * v, 92f * v, 600f, 480f, 1180f, 900f, 1f, 0.02f, 31f, 0.35f, 0.3f, rng);
                    Click(b, 0.395f, 0.004f, 0.25f, 3000f, rng);
                    return Finish(b, 0.82f);
                default: // wheezing exhale: breathy, a faint voice inside, long fall-off
                    b = new float[(int)(Rate * 1.05f)];
                    Breath(b, 0f, 1f, 0.5f, 700f, 2600f, rising: false, rng);
                    ThroatVoice(b, 0.04f, 0.9f, 104f * v, 70f * v, 560f, 380f, 980f, 720f, 0.35f, 0.08f, 6f, 0.05f, 0.6f, rng);
                    return Finish(b, 0.76f);
            }
        }

        /// <summary>Body-fall thump <paramref name="variant"/>: low drop, a dull cloth / bone knock, a light rustle.</summary>
        public static float[] ZombieBodyFall(int variant)
        {
            var rng = new System.Random(variant * 4243 + 81);
            float v = 1f + (variant - 2) * 0.07f;
            var b = new float[(int)(Rate * 0.4f)];
            Sweep(b, 0f, 0.32f, 92f * v, 44f * v, 0.9f, 13f, 1);
            NoiseBurst(b, 0f, 0.22f, 0.5f, 60f, 420f, 22f, rng);
            NoiseBurst(b, 0.01f, 0.16f, 0.12f, 700f, 2400f, 26f, rng); // cloth rustle
            if (variant != 2) BoneSnap(b, 0.025f, 0.16f, rng);          // a knee or elbow knocks
            Sweep(b, 0.09f, 0.18f, 70f * v, 50f * v, 0.25f, 18f, 1);   // second, smaller settle
            return Finish(b, 0.8f);
        }

        /// <summary>
        /// Mass death <paramref name="variant"/> (~1.3 s): 4 voices at different pitches and starts (a crowd going down
        /// together, not one sound repeated), then a spread of body falls and one deep thud under them.
        /// </summary>
        public static float[] ZombieMassDeath(int variant)
        {
            var rng = new System.Random(variant * 6151 + 66);
            var b = new float[(int)(Rate * 1.35f)];
            float s = variant == 1 ? 1f : 0.93f;
            ThroatVoice(b, 0f, 0.85f, 120f * s, 70f * s, 640f, 410f, 1080f, 790f, 0.8f, 0.04f, 0f, 0f, 0.3f, rng);
            ThroatVoice(b, 0.05f, 0.7f, 96f * s, 60f * s, 560f, 380f, 920f, 720f, 0.75f, 0.06f, 5f, 0.04f, 0.25f, rng);
            ThroatVoice(b, 0.11f, 0.45f, 150f * s, 110f * s, 760f, 600f, 1400f, 1100f, 0.5f, 0.02f, 28f, 0.3f, 0.4f, rng);
            ThroatVoice(b, 0.18f, 0.9f, 84f * s, 54f * s, 500f, 360f, 860f, 680f, 0.65f, 0.09f, 0f, 0f, 0.2f, rng);
            Gurgle(b, 0.1f, 0.5f, 0.3f, 19f, rng);
            Sweep(b, 0.32f, 0.5f, 70f * s, 32f * s, 0.85f, 7f, 1);      // deep thud under the falls
            float[] falls = { 0.3f, 0.41f, 0.5f, 0.63f, 0.78f };
            for (int i = 0; i < falls.Length; i++)
            {
                float t = falls[i] + (float)rng.NextDouble() * 0.03f, f = 80f + (float)rng.NextDouble() * 30f;
                Sweep(b, t, 0.28f, f, f * 0.5f, 0.45f - i * 0.04f, 14f, 1);
                NoiseBurst(b, t, 0.18f, 0.28f, 60f, 450f, 22f, rng);
                if (i % 2 == 0) BoneSnap(b, t + 0.02f, 0.1f, rng);
            }
            return Finish(b, 0.9f);
        }

        // ------------------------------------------------------------ zombie DSP parts

        /// <summary>
        /// Voiced throat sound: a saw buzz (pitch f0a to f0b) plus breath noise, through two gliding formants (A, B). Optional
        /// growl: amplitude modulation at <paramref name="growlHz"/> with <paramref name="growlDepth"/>. Soft attack, falling
        /// tail. <paramref name="breath"/> 0 = pure voice, 1 = all breath.
        /// </summary>
        private static void ThroatVoice(float[] b, float start, float length, float f0a, float f0b, float fa0, float fa1, float fb0, float fb1,
            float amp, float attack, float growlHz, float growlDepth, float breath, System.Random rng)
        {
            int s = (int)(start * Rate), e = Mathf.Min(b.Length, s + (int)(length * Rate));
            var fa = new Biquad(); var fb = new Biquad();
            var lp = new Biquad(); lp.SetLowPass(4200f, 0.7f, Rate);
            double phase = 0;
            for (int i = s; i < e; i++)
            {
                float t = (i - s) / (float)Rate, k = t / length;
                float pitch = Mathf.Lerp(f0a, f0b, k * k) * (1f + 0.025f * Mathf.Sin(t * 27f));
                phase += pitch / Rate;
                float src = Saw(phase) * (1f - breath) * 0.55f + Noise(rng) * (0.2f + breath * 0.6f);
                fa.SetBandPass(Mathf.Lerp(fa0, fa1, k), 6f, Rate);
                fb.SetBandPass(Mathf.Lerp(fb0, fb1, k), 8f, Rate);
                float env = Mathf.Min(1f, t / Mathf.Max(0.002f, attack)) * (1f - k) * (1f - k * 0.5f);
                if (growlHz > 0f) env *= 1f - growlDepth * (0.5f + 0.5f * Mathf.Sin(t * growlHz * Mathf.PI * 2f));
                b[i] += lp.Process(fa.Process(src) * 1.3f + fb.Process(src) * 0.6f) * env * amp * 1.5f;
            }
        }

        /// <summary>Wet throat bubbling: low-passed noise in irregular pulses around <paramref name="rateHz"/>.</summary>
        private static void Gurgle(float[] b, float start, float length, float amp, float rateHz, System.Random rng)
        {
            int s = (int)(start * Rate), e = Mathf.Min(b.Length, s + (int)(length * Rate));
            var bp = new Biquad();
            double phase = 0;
            float jitter = 1f;
            for (int i = s; i < e; i++)
            {
                float t = (i - s) / (float)Rate, k = t / length;
                double before = phase;
                phase += rateHz * jitter / Rate;
                if (Math.Floor(phase) != Math.Floor(before)) jitter = 0.6f + (float)rng.NextDouble() * 0.9f;
                float frac = (float)(phase - Math.Floor(phase));
                float pulse = Mathf.Exp(-frac * 9f);
                bp.SetBandPass(Mathf.Lerp(420f, 260f, k) * (0.85f + 0.3f * frac), 3f, Rate);
                b[i] += bp.Process(Noise(rng)) * pulse * amp * Mathf.Min(1f, t / 0.03f) * (1f - k) * 2f;
            }
        }

        /// <summary>Dry bone crack: two very short band-limited clicks a few ms apart and a tiny woody ring.</summary>
        private static void BoneSnap(float[] b, float start, float amp, System.Random rng)
        {
            Click(b, start, 0.003f, amp, 4200f, rng);
            Click(b, start + 0.006f, 0.002f, amp * 0.6f, 3600f, rng);
            Sweep(b, start, 0.04f, 1300f, 1150f, amp * 0.25f, 90f, 1);
        }

        /// <summary>Breath noise in the lo..hi band; <paramref name="rising"/> = swells up (a gasp), else falls off (an exhale).</summary>
        private static void Breath(float[] b, float start, float length, float amp, float lo, float hi, bool rising, System.Random rng)
        {
            int s = (int)(start * Rate), e = Mathf.Min(b.Length, s + (int)(length * Rate));
            var hp = new Biquad(); hp.SetHighPass(lo, 0.7f, Rate);
            var lp = new Biquad(); lp.SetLowPass(hi, 0.7f, Rate);
            for (int i = s; i < e; i++)
            {
                float k = (i - s) / (float)(e - s);
                float env = rising ? k * k * (1f - Mathf.Clamp01((k - 0.9f) / 0.1f)) : Mathf.Min(1f, k / 0.06f) * (1f - k) * (1f - k);
                b[i] += lp.Process(hp.Process(Noise(rng))) * env * amp * 2f;
            }
        }

        /// <summary>Normalize, then a 2 ms fade-in so no clip starts with a click (Normalize fades the end).</summary>
        private static float[] Finish(float[] b, float peak)
        {
            Normalize(b, peak);
            int f = Mathf.Min(b.Length, Rate * 2 / 1000);
            for (int i = 0; i < f; i++) b[i] *= i / (float)f;
            return b;
        }
    }
}
