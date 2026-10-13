using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Audio
{
    /// <summary>
    /// Generates the game's sound effects procedurally (no downloads, no paid assets) and writes 16-bit mono WAVs
    /// to <see cref="Dir"/>. Deterministic: same seed, same sound. Re-run is safe (files are overwritten).
    /// Each sound = layers of simple DSP parts (decaying sines, filtered noise bursts, pitch sweeps).
    /// Tune a sound: change its layer numbers below, run "Survivor Souls/Rebuild Sounds", listen in the editor.
    /// </summary>
    public static class SfxBuilder
    {
        public const string Dir = "Assets/_Project/Audio/Sfx";
        private const int Rate = 44100;
        private const int AmbienceRate = 22050;

        public sealed class Set
        {
            public AudioClip[] Shots, Impacts, Deaths, Footsteps, HitMarkers, KillMarkers;
            public AudioClip Ambience;
        }

        [MenuItem("Survivor Souls/Rebuild Sounds")]
        public static void BuildMenu() => Build();

        public static Set Build()
        {
            Directory.CreateDirectory(Dir);
            var set = new Set
            {
                Shots = Many("sfx_crossbow_shot", 3, CrossbowShot),
                Impacts = Many("sfx_bolt_impact", 3, BoltImpact),
                Deaths = Many("sfx_hollow_death", 2, HollowDeath),
                Footsteps = Many("sfx_footstep_armour", 4, Footstep),
                HitMarkers = Many("sfx_hitmarker_tick", 2, HitMarkerTick),
                KillMarkers = Many("sfx_killmarker", 1, KillMarker),
            };
            set.Ambience = Save("amb_meadow_wind_loop", AmbienceRate, MeadowAmbience(AmbienceRate, 24f, 9), ambience: true);
            return set;
        }

        private static AudioClip[] Many(string name, int count, Func<int, float[]> make)
        {
            var clips = new AudioClip[count];
            for (int i = 0; i < count; i++) clips[i] = Save($"{name}_{i + 1:00}", Rate, make(i + 1), ambience: false);
            return clips;
        }

        // ------------------------------------------------------------ Sounds

        /// <summary>Crossbow release: trigger click, deep string thrum with pitch drop, wooden stock thump, air whoosh.</summary>
        private static float[] CrossbowShot(int seed)
        {
            var rng = new System.Random(seed * 101);
            var b = new float[(int)(Rate * 0.55f)];
            float v = 1f + (float)(rng.NextDouble() - 0.5) * 0.1f;
            Click(b, 0f, 0.004f, 0.5f, 5200f, rng);                       // trigger / latch
            Sweep(b, 0.004f, 0.35f, 165f * v, 96f * v, 0.55f, 9f, 2);      // string thrum (sine + 2 harmonics)
            NoiseBurst(b, 0.004f, 0.09f, 0.75f, 70f, 260f, 22f, rng);       // stock thump
            NoiseBurst(b, 0.01f, 0.22f, 0.32f, 900f, 3800f, 11f, rng);       // bolt whoosh
            Sweep(b, 0.005f, 0.12f, 820f * v, 610f * v, 0.08f, 30f, 1);      // limb snap ring
            return Normalize(b, 0.9f);
        }

        /// <summary>Bolt hit on a hollow body: sharp crack, heavy low thud, short dull rattle.</summary>
        private static float[] BoltImpact(int seed)
        {
            var rng = new System.Random(seed * 211);
            var b = new float[(int)(Rate * 0.4f)];
            float v = 1f + (float)(rng.NextDouble() - 0.5) * 0.15f;
            Click(b, 0f, 0.006f, 0.7f, 3000f, rng);
            Sweep(b, 0f, 0.22f, 120f * v, 52f * v, 0.9f, 16f, 1);
            NoiseBurst(b, 0f, 0.14f, 0.6f, 180f, 900f, 26f, rng);
            NoiseBurst(b, 0.02f, 0.2f, 0.18f, 1500f, 4200f, 18f, rng);
            return Normalize(b, 0.92f);
        }

        /// <summary>Hollow dies: breathy falling moan (noise through sweeping formants) and a body drop.</summary>
        private static float[] HollowDeath(int seed)
        {
            var rng = new System.Random(seed * 307);
            float len = 1.1f;
            var b = new float[(int)(Rate * len)];
            float v = 1f + (float)(rng.NextDouble() - 0.5) * 0.12f;
            // Moan: glottal-ish buzz (saw) through two formant filters, pitch and formants fall.
            var f1 = new Biquad(); var f2 = new Biquad();
            double phase = 0;
            for (int i = 0; i < (int)(Rate * 0.8f); i++)
            {
                float t = i / (float)Rate, k = t / 0.8f;
                float pitch = Mathf.Lerp(150f, 70f, k * k) * v * (1f + 0.02f * Mathf.Sin(t * 31f));
                phase += pitch / Rate;
                float saw = (float)(2 * (phase - Math.Floor(phase)) - 1) * 0.5f + Noise(rng) * 0.35f;
                f1.SetBandPass(Mathf.Lerp(700f, 380f, k), 6f, Rate);
                f2.SetBandPass(Mathf.Lerp(1150f, 820f, k), 8f, Rate);
                float env = Mathf.SmoothStep(0f, 1f, t / 0.06f) * (1f - k) * (1f - k);
                b[i] += (f1.Process(saw) + f2.Process(saw) * 0.6f) * env * 1.4f;
            }
            Sweep(b, 0.72f, 0.3f, 80f, 45f, 0.75f, 14f, 1);                 // body drop
            NoiseBurst(b, 0.72f, 0.25f, 0.5f, 90f, 500f, 18f, rng);
            NoiseBurst(b, 0.76f, 0.2f, 0.15f, 2200f, 5000f, 20f, rng);       // armour rattle
            return Normalize(b, 0.85f);
        }

        /// <summary>Armoured step on grass: soft muffled thud, grass rustle, faint metal clink.</summary>
        private static float[] Footstep(int seed)
        {
            var rng = new System.Random(seed * 401);
            var b = new float[(int)(Rate * 0.28f)];
            float v = 1f + (float)(rng.NextDouble() - 0.5) * 0.2f;
            Sweep(b, 0f, 0.12f, 95f * v, 60f * v, 0.7f, 30f, 1);
            NoiseBurst(b, 0f, 0.09f, 0.45f, 120f, 600f, 34f, rng);
            NoiseBurst(b, 0.005f, 0.16f, 0.22f, 2500f, 7000f, 22f, rng);
            Sweep(b, 0.012f, 0.12f, 2350f * v, 2300f * v, 0.05f, 40f, 2);
            return Normalize(b, 0.8f);
        }

        /// <summary>Hit marker confirm (KAN-13): dry, bright, very short metallic tick. Sits on top of the bolt impact.</summary>
        private static float[] HitMarkerTick(int seed)
        {
            var rng = new System.Random(seed * 503);
            var b = new float[(int)(Rate * 0.07f)];
            float v = 1f + (float)(rng.NextDouble() - 0.5) * 0.06f;
            Click(b, 0f, 0.0025f, 0.6f, 9000f, rng);
            Sweep(b, 0f, 0.06f, 3400f * v, 3150f * v, 0.5f, 85f, 2);        // tick ring
            Sweep(b, 0f, 0.04f, 5100f * v, 4900f * v, 0.18f, 120f, 1);      // shimmer
            NoiseBurst(b, 0f, 0.03f, 0.2f, 3000f, 9000f, 140f, rng);
            return Normalize(b, 0.8f);
        }

        /// <summary>Kill marker confirm (KAN-13): deeper thunk under a lower two-note ping. Clearly heavier than the hit tick.</summary>
        private static float[] KillMarker(int seed)
        {
            var rng = new System.Random(seed * 601);
            var b = new float[(int)(Rate * 0.22f)];
            Click(b, 0f, 0.003f, 0.55f, 7000f, rng);
            Sweep(b, 0f, 0.14f, 210f, 120f, 0.75f, 28f, 2);                  // thunk
            Sweep(b, 0f, 0.12f, 1850f, 1780f, 0.35f, 30f, 2);                // ping
            Sweep(b, 0.035f, 0.15f, 2470f, 2400f, 0.25f, 26f, 1);            // second ping, a fourth up
            NoiseBurst(b, 0f, 0.05f, 0.25f, 400f, 2400f, 60f, rng);
            return Normalize(b, 0.85f);
        }

        /// <summary>Seamless meadow loop: soft gusting wind (filtered brown noise) and sparse bird chirps.</summary>
        private static float[] MeadowAmbience(int rate, float seconds, int seed)
        {
            var rng = new System.Random(seed);
            int n = (int)(rate * seconds);
            int fade = rate * 2;
            var b = new float[n + fade];
            var lp = new Biquad();
            float brown = 0f;
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)rate;
                brown = Mathf.Clamp(brown + Noise(rng) * 0.06f, -1f, 1f) * 0.998f;
                float gust = 0.55f + 0.3f * Mathf.Sin(t * 0.41f) + 0.15f * Mathf.Sin(t * 1.13f + 1.7f);
                lp.SetLowPass(Mathf.Lerp(250f, 900f, gust), 0.7f, rate);
                b[i] = lp.Process(brown) * gust * 0.5f;
            }
            // Birds: short FM chirp phrases at random times.
            for (int k = 0; k < 9; k++)
            {
                float start = (float)rng.NextDouble() * (seconds - 1.5f);
                float basePitch = 2600f + (float)rng.NextDouble() * 1800f;
                int notes = 2 + rng.Next(4);
                for (int j = 0; j < notes; j++)
                    Chirp(b, rate, start + j * 0.13f, 0.08f, basePitch * (1f + j * 0.05f), 0.06f + (float)rng.NextDouble() * 0.04f);
            }
            // Loop: crossfade the tail into the head.
            var loop = new float[n];
            for (int i = 0; i < n; i++)
            {
                loop[i] = b[i];
                if (i < fade)
                {
                    float w = i / (float)fade;
                    loop[i] = b[i] * w + b[n + i] * (1f - w);
                }
            }
            return Normalize(loop, 0.6f);
        }

        // ------------------------------------------------------------ DSP parts

        /// <summary>Decaying sine whose pitch glides from f0 to f1, plus optional harmonics (1/h amplitude).</summary>
        private static void Sweep(float[] b, float start, float length, float f0, float f1, float amp, float decay, int harmonics)
        {
            int s = (int)(start * Rate), e = Mathf.Min(b.Length, s + (int)(length * Rate));
            double phase = 0;
            for (int i = s; i < e; i++)
            {
                float t = (i - s) / (float)Rate, k = t / length;
                phase += Mathf.Lerp(f0, f1, 1f - (1f - k) * (1f - k)) / Rate;
                float env = Mathf.Exp(-t * decay) * Mathf.Min(1f, t * 2000f);
                float sum = 0f;
                for (int h = 1; h <= harmonics; h++) sum += Mathf.Sin((float)(phase * h * Math.PI * 2)) / h;
                b[i] += sum * amp * env;
            }
        }

        /// <summary>Noise through a band-pass (lo..hi), exponential decay.</summary>
        private static void NoiseBurst(float[] b, float start, float length, float amp, float lo, float hi, float decay, System.Random rng)
        {
            int s = (int)(start * Rate), e = Mathf.Min(b.Length, s + (int)(length * Rate));
            var hp = new Biquad(); hp.SetHighPass(lo, 0.7f, Rate);
            var lp = new Biquad(); lp.SetLowPass(hi, 0.7f, Rate);
            for (int i = s; i < e; i++)
            {
                float t = (i - s) / (float)Rate;
                float env = Mathf.Exp(-t * decay) * Mathf.Min(1f, t * 1500f);
                b[i] += lp.Process(hp.Process(Noise(rng))) * amp * env * 2f;
            }
        }

        /// <summary>Very short bright tick.</summary>
        private static void Click(float[] b, float start, float length, float amp, float hi, System.Random rng)
        {
            int s = (int)(start * Rate), e = Mathf.Min(b.Length, s + (int)(length * Rate));
            var lp = new Biquad(); lp.SetLowPass(hi, 0.9f, Rate);
            for (int i = s; i < e; i++)
            {
                float k = (i - s) / (float)(e - s);
                b[i] += lp.Process(Noise(rng)) * amp * (1f - k) * 2f;
            }
        }

        private static void Chirp(float[] b, int rate, float start, float length, float pitch, float amp)
        {
            int s = (int)(start * rate), e = Mathf.Min(b.Length, s + (int)(length * rate));
            double phase = 0;
            for (int i = s; i < e; i++)
            {
                float t = (i - s) / (float)rate, k = t / length;
                float f = pitch * (1f + 0.25f * Mathf.Sin(k * Mathf.PI)) * (1f + 0.04f * Mathf.Sin(t * 180f));
                phase += f / rate;
                b[i] += Mathf.Sin((float)(phase * Math.PI * 2)) * amp * Mathf.Sin(k * Mathf.PI);
            }
        }

        private static float Noise(System.Random rng) => (float)(rng.NextDouble() * 2.0 - 1.0);

        private static float[] Normalize(float[] b, float peak)
        {
            float max = 1e-6f;
            foreach (float x in b) max = Mathf.Max(max, Mathf.Abs(x));
            float g = peak / max;
            for (int i = 0; i < b.Length; i++) b[i] *= g;
            // 3 ms fade-out so no clip ends with a click.
            int f = Mathf.Min(b.Length, Rate * 3 / 1000);
            for (int i = 0; i < f; i++) b[b.Length - 1 - i] *= i / (float)f;
            return b;
        }

        /// <summary>RBJ biquad (mono). Coefficients can change per sample (sweeping filters).</summary>
        private sealed class Biquad
        {
            private float _b0, _b1, _b2, _a1, _a2, _x1, _x2, _y1, _y2;

            public void SetLowPass(float f, float q, int rate) => Set(f, q, rate, 0);
            public void SetHighPass(float f, float q, int rate) => Set(f, q, rate, 1);
            public void SetBandPass(float f, float q, int rate) => Set(f, q, rate, 2);

            private void Set(float f, float q, int rate, int type)
            {
                float w = 2f * Mathf.PI * Mathf.Clamp(f, 10f, rate * 0.45f) / rate;
                float cos = Mathf.Cos(w), alpha = Mathf.Sin(w) / (2f * q);
                float a0 = 1f + alpha;
                switch (type)
                {
                    case 0: _b0 = (1f - cos) / 2f; _b1 = 1f - cos; _b2 = (1f - cos) / 2f; break;
                    case 1: _b0 = (1f + cos) / 2f; _b1 = -(1f + cos); _b2 = (1f + cos) / 2f; break;
                    default: _b0 = alpha; _b1 = 0f; _b2 = -alpha; break;
                }
                _b0 /= a0; _b1 /= a0; _b2 /= a0;
                _a1 = -2f * cos / a0;
                _a2 = (1f - alpha) / a0;
            }

            public float Process(float x)
            {
                float y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
                _x2 = _x1; _x1 = x; _y2 = _y1; _y1 = y;
                return y;
            }
        }

        // ------------------------------------------------------------ WAV + import

        private static AudioClip Save(string name, int rate, float[] samples, bool ambience)
        {
            string path = $"{Dir}/{name}.wav";
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = samples.Length * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + bytes);
                w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' }); w.Write(16);
                w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(bytes);
                foreach (float s in samples) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), -32768, 32767));
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            importer.forceToMono = true;
            importer.loadInBackground = ambience;
            var settings = importer.defaultSampleSettings;
            settings.loadType = ambience ? AudioClipLoadType.CompressedInMemory : AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = ambience ? AudioCompressionFormat.Vorbis : AudioCompressionFormat.ADPCM;
            settings.quality = 0.6f;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
    }
}
