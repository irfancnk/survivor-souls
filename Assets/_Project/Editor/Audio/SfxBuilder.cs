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
            public AudioClip[] Shots, Impacts, Deaths, Footsteps, HitMarkers, KillMarkers, SoulChimes, LevelUps, BoonPicks, PortalOpens, PortalEnters, ChamberArrives;
            /// <summary>Reliquary chest lands (KAN-73) and opens.</summary>
            public AudioClip[] ChestDrops, ChestOpens;
            public AudioClip Ambience, PortalHum;
            /// <summary>Skill card reveal per tier (KAN-37), index = SkillRarity: Common, Rare, Epic, Legendary.</summary>
            public AudioClip[] RarityReveals;
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
                Footsteps = Many("sfx_footstep_snow", 4, Footstep),
                HitMarkers = Many("sfx_hitmarker_tick", 2, HitMarkerTick),
                KillMarkers = Many("sfx_killmarker", 1, KillMarker),
                SoulChimes = Many("sfx_soul_chime", 2, SoulChime),
                LevelUps = Many("sfx_level_up", 1, LevelUp),
                BoonPicks = Many("sfx_boon_pick", 1, BoonPick),
                ChestDrops = Many("sfx_chest_drop", 1, ChestDrop),
                ChestOpens = Many("sfx_chest_open", 1, ChestOpen),
                PortalOpens = Many("sfx_portal_open", 1, PortalOpen),
                PortalEnters = Many("sfx_portal_enter", 1, PortalEnter),
                ChamberArrives = Many("sfx_chamber_arrive", 1, ChamberArrive),
            };
            set.RarityReveals = new[]
            {
                Save("sfx_reveal_common_01", Rate, RevealCommon(), ambience: false),
                Save("sfx_reveal_rare_01", Rate, RevealRare(), ambience: false),
                Save("sfx_reveal_epic_01", Rate, RevealEpic(), ambience: false),
                Save("sfx_reveal_legendary_01", Rate, RevealLegendary(), ambience: false),
            };
            set.Ambience = Save("amb_shore_wind_surf_loop", AmbienceRate, ShoreAmbience(AmbienceRate, 24f, 9), ambience: true);
            set.PortalHum = Save("amb_portal_hum_loop", AmbienceRate, PortalHum(AmbienceRate, 4f, 13), ambience: true);
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

        /// <summary>Armoured step in snow: muffled thud, a train of tiny crunch clicks (packed snow), faint metal clink.</summary>
        private static float[] Footstep(int seed)
        {
            var rng = new System.Random(seed * 401);
            var b = new float[(int)(Rate * 0.3f)];
            float v = 1f + (float)(rng.NextDouble() - 0.5) * 0.2f;
            Sweep(b, 0f, 0.12f, 90f * v, 55f * v, 0.6f, 30f, 1);
            NoiseBurst(b, 0f, 0.08f, 0.35f, 120f, 500f, 34f, rng);
            int crunches = 9 + rng.Next(6);
            for (int i = 0; i < crunches; i++)
            {
                float t = 0.005f + (float)Math.Pow(rng.NextDouble(), 1.6) * 0.17f;
                Click(b, t, 0.012f, 0.18f + (float)rng.NextDouble() * 0.2f, 1800f + (float)rng.NextDouble() * 2500f, rng);
            }
            NoiseBurst(b, 0.01f, 0.18f, 0.12f, 1500f, 4500f, 16f, rng);
            Sweep(b, 0.012f, 0.12f, 2350f * v, 2300f * v, 0.04f, 40f, 2);
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

        /// <summary>
        /// Soul wisp absorbed (KAN-21): soft glassy bell (two slightly detuned partials), a breath of air on top.
        /// Quiet and short: it repeats fast during a pickup streak and its pitch climbs, so it must not get harsh.
        /// </summary>
        private static float[] SoulChime(int seed)
        {
            var rng = new System.Random(seed * 701);
            var b = new float[(int)(Rate * 0.4f)];
            float f = seed == 1 ? 1318.5f : 1396.9f; // E6 / F6
            Sweep(b, 0f, 0.38f, f, f, 0.5f, 11f, 1);
            Sweep(b, 0f, 0.3f, f * 2.005f, f * 2.0f, 0.18f, 16f, 1);         // octave shimmer, slight beat
            Sweep(b, 0f, 0.2f, f * 2.76f, f * 2.76f, 0.07f, 24f, 1);         // bell-like inharmonic partial
            NoiseBurst(b, 0f, 0.12f, 0.06f, 4000f, 11000f, 30f, rng);         // airy breath
            return Normalize(b, 0.6f);
        }

        /// <summary>Level up (KAN-21): rising three-note bell arpeggio over a soft airy swell, ends on an open chord.</summary>
        private static float[] LevelUp(int seed)
        {
            var rng = new System.Random(seed * 809);
            var b = new float[(int)(Rate * 1.3f)];
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f }; // C5 E5 G5 C6
            for (int i = 0; i < notes.Length; i++)
            {
                float start = i * 0.085f;
                float decay = i == notes.Length - 1 ? 3.2f : 6f;
                Sweep(b, start, 1.3f - start, notes[i], notes[i], 0.38f, decay, 2);
                Sweep(b, start, 0.6f, notes[i] * 2.003f, notes[i] * 2f, 0.1f, decay * 1.6f, 1);
            }
            // Swell: band-passed noise rising in brightness, fades out under the chord.
            var bp = new Biquad();
            int n = (int)(Rate * 0.9f);
            for (int i = 0; i < n; i++)
            {
                float k = i / (float)n;
                bp.SetBandPass(Mathf.Lerp(900f, 5200f, k), 1.2f, Rate);
                float env = Mathf.Sin(k * Mathf.PI) * 0.22f;
                b[i] += bp.Process(Noise(rng)) * env;
            }
            Sweep(b, 0f, 0.5f, 98f, 130.8f, 0.3f, 5f, 2);                    // low body under the rise
            return Normalize(b, 0.85f);
        }

        /// <summary>
        /// Skill picked (KAN-19): a low ember "whumpf" (fire catching), a bright rune strike (fifth + octave bell) and a
        /// short crackle tail. Distinct from the level-up arpeggio that plays just before it.
        /// </summary>
        private static float[] BoonPick(int seed)
        {
            var rng = new System.Random(seed * 907);
            var b = new float[(int)(Rate * 0.9f)];
            NoiseBurst(b, 0f, 0.35f, 0.5f, 90f, 600f, 9f, rng);                // fire catching
            Sweep(b, 0f, 0.3f, 110f, 73.4f, 0.45f, 10f, 2);                    // low body
            Sweep(b, 0.03f, 0.85f, 880f, 880f, 0.3f, 5f, 2);                   // A5 strike
            Sweep(b, 0.03f, 0.7f, 1318.5f, 1318.5f, 0.18f, 6f, 1);             // E6 fifth
            Sweep(b, 0.03f, 0.5f, 1761f, 1760f, 0.08f, 9f, 1);                 // octave shimmer
            for (int i = 0; i < 9; i++)                                        // ember crackle
                Click(b, 0.12f + (float)rng.NextDouble() * 0.45f, 0.004f, 0.12f + (float)rng.NextDouble() * 0.1f, 4200f, rng);
            return Normalize(b, 0.8f);
        }

        /// <summary>
        /// Reliquary chest lands (KAN-73): a dull wooden thud on snow, a short iron rattle and one soft high bell (D6) so it
        /// reads as treasure through the fight noise.
        /// </summary>
        private static float[] ChestDrop(int seed)
        {
            var rng = new System.Random(seed * 1913);
            var b = new float[(int)(Rate * 0.7f)];
            NoiseBurst(b, 0f, 0.09f, 0.6f, 80f, 700f, 40f, rng);               // wood on snow
            Sweep(b, 0f, 0.16f, 140f, 96f, 0.5f, 22f, 2);                      // low thud body
            for (int i = 0; i < 4; i++)                                        // iron straps rattle
                Click(b, 0.03f + i * 0.035f + (float)rng.NextDouble() * 0.01f, 0.006f, 0.16f - i * 0.03f, 3600f, rng);
            Sweep(b, 0.06f, 0.6f, 1174.7f, 1174.7f, 0.16f, 7f, 2);             // D6 bell
            return Normalize(b, 0.7f);
        }

        /// <summary>
        /// Reliquary chest opens (KAN-73): a short hinge creak, the lid knock, then a rising gold shimmer (A5 C#6 E6 A6
        /// quick arpeggio over bright noise). About 0.4 s to the top note, matching the open animation.
        /// </summary>
        private static float[] ChestOpen(int seed)
        {
            var rng = new System.Random(seed * 1931);
            var b = new float[(int)(Rate * 1.1f)];
            Sweep(b, 0f, 0.16f, 520f, 760f, 0.12f, 6f, 4);                     // hinge creak
            NoiseBurst(b, 0f, 0.16f, 0.12f, 900f, 2600f, 12f, rng);
            NoiseBurst(b, 0.15f, 0.08f, 0.45f, 120f, 900f, 45f, rng);          // lid knock
            Sweep(b, 0.15f, 0.12f, 120f, 90f, 0.35f, 25f, 2);
            float[] notes = { 880f, 1108.7f, 1318.5f, 1760f };
            for (int i = 0; i < notes.Length; i++)                             // gold arpeggio
                Sweep(b, 0.2f + i * 0.06f, 0.75f - i * 0.08f, notes[i], notes[i], 0.22f - i * 0.025f, 5f + i, 2);
            NoiseBurst(b, 0.2f, 0.7f, 0.1f, 4000f, 9000f, 5f, rng);           // shimmer
            return Normalize(b, 0.8f);
        }

        // Skill card reveals (KAN-37): one short sound per tier, each a step bigger. Common is a soft card slap + one dull
        // tone; Rare adds a second bell note; Epic an arpeggio with shimmer; Legendary a low boom, a full bright chord and
        // a sparkle tail. Peaks rise with the tier too (the style asset scales the volume again).

        /// <summary>Common: paper-card slap and one soft wooden tone (G4).</summary>
        private static float[] RevealCommon()
        {
            var rng = new System.Random(1301);
            var b = new float[(int)(Rate * 0.32f)];
            NoiseBurst(b, 0f, 0.05f, 0.5f, 600f, 2400f, 60f, rng);            // card slap
            Sweep(b, 0.004f, 0.28f, 392f, 390f, 0.35f, 16f, 1);                // G4 tone, fast decay
            return Normalize(b, 0.5f);
        }

        /// <summary>Rare: slap, then two clean bell notes (E5, B5) a beat apart.</summary>
        private static float[] RevealRare()
        {
            var rng = new System.Random(1303);
            var b = new float[(int)(Rate * 0.6f)];
            NoiseBurst(b, 0f, 0.05f, 0.4f, 700f, 2800f, 55f, rng);
            Sweep(b, 0.005f, 0.5f, 659.3f, 659.3f, 0.32f, 7f, 2);              // E5
            Sweep(b, 0.075f, 0.5f, 987.8f, 987.8f, 0.26f, 7f, 1);              // B5
            return Normalize(b, 0.62f);
        }

        /// <summary>Epic: soft whoosh, three-note rising arpeggio (D5 F#5 A5 + D6) and a noise shimmer.</summary>
        private static float[] RevealEpic()
        {
            var rng = new System.Random(1307);
            var b = new float[(int)(Rate * 0.95f)];
            NoiseBurst(b, 0f, 0.18f, 0.28f, 900f, 4200f, 14f, rng);            // whoosh in
            Sweep(b, 0.02f, 0.7f, 587.3f, 587.3f, 0.26f, 5f, 2);               // D5
            Sweep(b, 0.08f, 0.7f, 740f, 740f, 0.24f, 5f, 2);                   // F#5
            Sweep(b, 0.14f, 0.7f, 880f, 880f, 0.24f, 5f, 2);                   // A5
            Sweep(b, 0.2f, 0.7f, 1174.7f, 1174.7f, 0.14f, 6f, 1);              // D6
            NoiseBurst(b, 0.18f, 0.6f, 0.08f, 5000f, 9000f, 6f, rng);           // shimmer
            return Normalize(b, 0.72f);
        }

        /// <summary>Legendary: low boom, bright major chord swell (A4 C#5 E5 A5), rising sweep and a sparkle tail.</summary>
        private static float[] RevealLegendary()
        {
            var rng = new System.Random(1319);
            var b = new float[(int)(Rate * 1.5f)];
            Sweep(b, 0f, 0.6f, 110f, 55f, 0.6f, 6f, 2);                        // boom
            NoiseBurst(b, 0f, 0.3f, 0.45f, 60f, 400f, 12f, rng);               // body
            Sweep(b, 0.02f, 0.35f, 400f, 1600f, 0.12f, 6f, 1);                 // rising sweep
            Sweep(b, 0.05f, 1.3f, 440f, 440f, 0.22f, 3f, 2);                   // A4
            Sweep(b, 0.05f, 1.3f, 554.4f, 554.4f, 0.2f, 3f, 2);                // C#5
            Sweep(b, 0.05f, 1.3f, 659.3f, 659.3f, 0.2f, 3f, 2);                // E5
            Sweep(b, 0.1f, 1.2f, 880f, 880f, 0.16f, 3.5f, 1);                  // A5
            for (int i = 0; i < 16; i++)                                       // sparkle tail
                Sweep(b, 0.15f + (float)rng.NextDouble() * 0.9f, 0.12f, 2600f + (float)rng.NextDouble() * 2400f,
                    3000f + (float)rng.NextDouble() * 2400f, 0.05f, 30f, 1);
            return Normalize(b, 0.88f);
        }

        /// <summary>
        /// Portal opens (KAN-39): distant low horn (two detuned saw-like voices, slow swell, a fifth above joins), a rising
        /// airy rush and a bright bell chord on top when the film bursts. Long tail: it plays once, wherever the player is.
        /// </summary>
        private static float[] PortalOpen(int seed)
        {
            var rng = new System.Random(seed * 1009);
            float len = 2.8f;
            var b = new float[(int)(Rate * len)];
            // Horn: harmonic-rich tone through a low-pass that opens with the swell.
            var lp = new Biquad();
            double p1 = 0, p2 = 0, p3 = 0;
            int hornEnd = (int)(Rate * 2.4f);
            for (int i = 0; i < hornEnd; i++)
            {
                float t = i / (float)Rate;
                float swell = Mathf.SmoothStep(0f, 1f, t / 0.55f) * Mathf.Exp(-Mathf.Max(0f, t - 1.1f) * 1.6f);
                float fifth = Mathf.SmoothStep(0f, 1f, (t - 0.35f) / 0.4f);
                p1 += 98f * (1f + 0.003f * Mathf.Sin(t * 5.1f)) / Rate;    // G2
                p2 += 98.6f / Rate;                                          // slight beat
                p3 += 146.8f / Rate;                                         // D3, a fifth up
                float v = Saw(p1) * 0.5f + Saw(p2) * 0.4f + Saw(p3) * 0.35f * fifth;
                lp.SetLowPass(Mathf.Lerp(250f, 1400f, swell), 0.8f, Rate);
                b[i] += lp.Process(v) * swell * 0.9f;
            }
            // Rush: band-passed noise rising into the burst at ~1.3 s.
            var bp = new Biquad();
            int rushEnd = (int)(Rate * 1.7f);
            for (int i = 0; i < rushEnd; i++)
            {
                float k = i / (float)rushEnd;
                bp.SetBandPass(Mathf.Lerp(400f, 4200f, k * k), 1.4f, Rate);
                b[i] += bp.Process(Noise(rng)) * k * k * (1f - Mathf.SmoothStep(0.85f, 1f, k)) * 0.5f;
            }
            // Burst: bell chord (G5 B5 D6 G6) + shimmer, at the film break.
            float[] bell = { 783.99f, 987.77f, 1174.66f, 1567.98f };
            for (int n = 0; n < bell.Length; n++)
            {
                Sweep(b, 1.3f + n * 0.03f, 1.45f - n * 0.03f, bell[n], bell[n], 0.22f, 2.6f, 2);
                Sweep(b, 1.3f + n * 0.03f, 0.9f, bell[n] * 2.004f, bell[n] * 2f, 0.06f, 4f, 1);
            }
            NoiseBurst(b, 1.28f, 0.6f, 0.35f, 2500f, 9000f, 7f, rng);
            Sweep(b, 1.28f, 0.6f, 70f, 45f, 0.5f, 6f, 1);                      // low thump of the burst
            return Normalize(b, 0.88f);
        }

        /// <summary>
        /// Knight steps into the portal (KAN-40, ~1.1 s): airy rush sweeping up into the white (band-passed noise with a
        /// swirling centre), a low swell under it and a soft high shimmer at the peak, then a quick fall to silence.
        /// </summary>
        private static float[] PortalEnter(int seed)
        {
            var rng = new System.Random(seed * 2003);
            float len = 1.15f;
            var b = new float[(int)(Rate * len)];
            var bp = new Biquad();
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate, k = t / len;
                float env = Mathf.SmoothStep(0f, 1f, k / 0.65f) * (1f - Mathf.SmoothStep(0.7f, 1f, k));
                float swirl = Mathf.Sin(t * Mathf.PI * 2f * 7f) * 0.18f;
                bp.SetBandPass(Mathf.Lerp(300f, 5200f, k * k) * (1f + swirl), 1.2f, Rate);
                b[i] += bp.Process(Noise(rng)) * env * 0.9f;
            }
            Sweep(b, 0f, 0.95f, 55f, 110f, 0.45f, 1.2f, 2);                      // low swell rising with the rush
            float[] shimmer = { 1318.5f, 1760f, 2637f };
            for (int n = 0; n < shimmer.Length; n++)
                Sweep(b, 0.62f + n * 0.04f, 0.5f, shimmer[n], shimmer[n] * 1.01f, 0.07f, 5f, 1);
            return Normalize(b, 0.8f);
        }

        /// <summary>
        /// New Chamber begins (KAN-40, under the title card): one deep bell toll (inharmonic bell partials on G2, long
        /// decay) with a faint breath of wind in front.
        /// </summary>
        private static float[] ChamberArrive(int seed)
        {
            var rng = new System.Random(seed * 3001);
            float len = 3f;
            var b = new float[(int)(Rate * len)];
            NoiseBurst(b, 0f, 0.6f, 0.08f, 400f, 2400f, 4f, rng);
            float f = 98f;
            float[] ratio = { 0.5f, 1f, 1.19f, 1.56f, 2f, 2.74f, 3.76f };
            float[] amp = { 0.35f, 0.5f, 0.3f, 0.22f, 0.25f, 0.12f, 0.06f };
            float[] decay = { 0.9f, 1.3f, 1.8f, 2.2f, 2.6f, 3.6f, 5f };
            for (int n = 0; n < ratio.Length; n++)
                Sweep(b, 0.08f, len - 0.1f, f * ratio[n], f * ratio[n] * 0.998f, amp[n], decay[n], 1);
            return Normalize(b, 0.8f);
        }

        /// <summary>
        /// Open portal hum (KAN-39, 3D loop near the arch): low drone with a slow beat, a faint airy swirl.
        /// Every partial and the swirl modulation make whole cycles over the loop length, so it loops without a seam.
        /// </summary>
        private static float[] PortalHum(int rate, float seconds, int seed)
        {
            var rng = new System.Random(seed);
            int n = (int)(rate * seconds);
            var b = new float[n];
            var bp = new Biquad();
            // Whole cycles in `seconds`: 55, 82.5 (x 4 s = 330), 110.25 (441), swirl 0.75 Hz (3 cycles).
            float[] f = { 55f, 82.5f, 110.25f, 165f };
            float[] a = { 0.5f, 0.28f, 0.22f, 0.08f };
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                float swirl = 0.5f + 0.5f * Mathf.Sin(t * Mathf.PI * 2f * 0.75f);
                float v = 0f;
                for (int k = 0; k < f.Length; k++) v += Mathf.Sin(t * Mathf.PI * 2f * f[k]) * a[k];
                v *= 0.8f + 0.2f * Mathf.Sin(t * Mathf.PI * 2f * 1.5f);
                bp.SetBandPass(Mathf.Lerp(700f, 1600f, swirl), 2f, rate);
                b[i] = v + bp.Process(Noise(rng)) * 0.12f * swirl;
            }
            // The filtered noise is not periodic: crossfade the last 0.25 s into the start.
            int fade = rate / 4;
            for (int i = 0; i < fade; i++)
            {
                float w = i / (float)fade;
                b[i] = b[i] * w + b[n - fade + i] * (1f - w);
            }
            var loop = new float[n - fade];
            System.Array.Copy(b, loop, loop.Length);
            // Scale only: Normalize's end fade would dip to silence at the loop point.
            float max = 1e-6f;
            foreach (float x in loop) max = Mathf.Max(max, Mathf.Abs(x));
            for (int i = 0; i < loop.Length; i++) loop[i] *= 0.6f / max;
            return loop;
        }

        private static float Saw(double phase) => (float)(2 * (phase - Math.Floor(phase)) - 1);

        /// <summary>
        /// Seamless cold shore loop: gusting wind (filtered brown noise), surf (white noise swells every ~6 s with a
        /// rising then falling low-pass, like a wave breaking and draining) and a few distant gull calls.
        /// </summary>
        private static float[] ShoreAmbience(int rate, float seconds, int seed)
        {
            var rng = new System.Random(seed);
            int n = (int)(rate * seconds);
            int fade = rate * 2;
            var b = new float[n + fade];
            var lp = new Biquad();
            var surfLp = new Biquad();
            float brown = 0f;
            // Wave start times: about every 6 s with some jitter, covering the crossfade tail too.
            var waves = new System.Collections.Generic.List<float>();
            for (float w = 0.3f; w < seconds + 2f; w += 5.2f + (float)rng.NextDouble() * 1.8f) waves.Add(w);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)rate;
                brown = Mathf.Clamp(brown + Noise(rng) * 0.06f, -1f, 1f) * 0.998f;
                float gust = 0.5f + 0.3f * Mathf.Sin(t * 0.37f) + 0.2f * Mathf.Sin(t * 1.07f + 1.7f);
                lp.SetLowPass(Mathf.Lerp(220f, 800f, gust), 0.7f, rate);
                float wind = lp.Process(brown) * gust * 0.45f;

                float surf = 0f;
                foreach (float w in waves)
                {
                    float u = t - w;
                    if (u < 0f || u > 5f) continue;
                    surf = Mathf.Max(surf, u < 1.4f ? Mathf.SmoothStep(0f, 1f, u / 1.4f) : Mathf.Exp(-(u - 1.4f) * 0.9f));
                }
                surfLp.SetLowPass(Mathf.Lerp(300f, 2200f, surf), 0.6f, rate);
                b[i] = wind + surfLp.Process(Noise(rng)) * surf * 0.28f;
            }
            // Gulls: two or three falling cries, far away (quiet).
            for (int k = 0; k < 4; k++)
            {
                float start = (float)rng.NextDouble() * (seconds - 2f);
                int notes = 2 + rng.Next(2);
                for (int j = 0; j < notes; j++)
                    Chirp(b, rate, start + j * 0.32f, 0.24f, 1500f - j * 120f, 0.035f);
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
