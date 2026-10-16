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
        /// <summary>File name stem of the Shadow Ranger bow shots (KAN-227): <c>sfx_bow_twang_01..03.wav</c>.</summary>
        public const string BowTwangName = "sfx_bow_twang";
        private const int Rate = 44100;
        private const int AmbienceRate = 22050;

        public sealed class Set
        {
            public AudioClip[] Shots, Impacts, Deaths, HitMarkers, KillMarkers, SoulChimes, LevelUps, BoonPicks, PortalOpens, PortalEnters, ChamberArrives;
            /// <summary>Reliquary chest lands (KAN-73) and opens.</summary>
            public AudioClip[] ChestDrops, ChestOpens;
            /// <summary>Owned skill levelled up, played when the HUD slot glows after the pause (KAN-74).</summary>
            public AudioClip[] SkillUpgrades;
            /// <summary>Boss fight healing heart picked up (KAN-60).</summary>
            public AudioClip[] HeartHeals;
            /// <summary>Level-up re-roll (KAN-67): cards flip to a new offer.</summary>
            public AudioClip[] Rerolls;
            /// <summary>Skill pick slot-machine roll (KAN-159): soft tick as a skill passes, clunk when a reel stops.</summary>
            public AudioClip[] ReelTicks, ReelStops;
            /// <summary>Boss charge (KAN-168): heavy foot-scrape at the warning, dash whoosh + clank + grunt, stop thud, wall crash.</summary>
            public AudioClip[] ChargeScrapes, ChargeDashes, ChargeStops, ChargeCrashes;
            /// <summary>Knight walks up to the sealed portal (KAN-49): muted low stone thud.</summary>
            public AudioClip[] PortalSeals;
            /// <summary>Hallowed Shield ward (KAN-93): rises, blocks a hit (glassy tink), runs out (soft shatter).</summary>
            public AudioClip[] WardUps, WardBlocks, WardDowns;
            /// <summary>Boss charge wind-up growl (KAN-168): seamless loop, pitch raised with the lane fill at runtime.</summary>
            public AudioClip ChargeRumble;
            public AudioClip Ambience, PortalHum;
            /// <summary>Skill card reveal per tier (KAN-37), index = SkillRarity: Common, Rare, Epic, Legendary.</summary>
            public AudioClip[] RarityReveals;
            /// <summary>Knight takes a discrete hit (KAN-187): dull body thud + short grunt.</summary>
            public AudioClip[] KnightGrunts;
            /// <summary>Low-health heartbeat (KAN-187): one quiet lub-dub, replayed at the beat rate.</summary>
            public AudioClip Heartbeat;
            /// <summary>Soul Burst (KAN-66): dull low whump with a short dark chime tail.</summary>
            public AudioClip[] SoulBursts;
            /// <summary>Ember Bolts (KAN-201): ignite whoomp + sizzle, seamless burning crackle loops, hot shot fire layer.</summary>
            public AudioClip[] EmberIgnites, EmberCrackles, EmberHotShots;
            /// <summary>Freeze Arrow (KAN-91): ice crack + glassy crystal ring on a freeze.</summary>
            public AudioClip[] FrostFreezes;
            /// <summary>Holy Tomes (KAN-123): leather thump + faint chime on a tome hit, quiet page whoosh loop.</summary>
            public AudioClip[] TomeThumps, TomeWhooshes;
            /// <summary>Special melee zombies (KAN-132): Bloater swell gurgle + burst pop, Ghoul screech / whoosh / thud, Brute grunt / slam boom, toxic cloud hiss loop.</summary>
            public AudioClip[] MeleeBloaterSwells, MeleeBloaterBursts, MeleeGhoulScreeches, MeleeGhoulWhooshes, MeleeGhoulThuds, MeleeBruteGrunts, MeleeBruteSlams;
            public AudioClip MeleeCloudHiss;
            /// <summary>Shadow Ranger bow shot (KAN-227, KAN-231): finger release tick, short twang, whistling arrow.</summary>
            public AudioClip[] BowTwangs;
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
                HitMarkers = Many("sfx_hitmarker_tick", 2, HitMarkerTick),
                KillMarkers = Many("sfx_killmarker", 1, KillMarker),
                SoulChimes = Many("sfx_soul_chime", 2, SoulChime),
                LevelUps = Many("sfx_level_up", 1, LevelUp),
                BoonPicks = Many("sfx_boon_pick", 1, BoonPick),
                ChestDrops = Many("sfx_chest_drop", 1, ChestDrop),
                ChestOpens = Many("sfx_chest_open", 1, ChestOpen),
                SkillUpgrades = Many("sfx_skill_upgrade", 1, SkillUpgrade),
                HeartHeals = Many("sfx_heart_heal", 1, HeartHeal),
                Rerolls = Many("sfx_card_reroll", 1, CardReroll),
                ReelTicks = Many("sfx_reel_tick", 2, ReelTick),
                ReelStops = Many("sfx_reel_stop", 1, ReelStop),
                PortalOpens = Many("sfx_portal_open", 1, PortalOpen),
                PortalEnters = Many("sfx_portal_enter", 1, PortalEnter),
                ChamberArrives = Many("sfx_chamber_arrive", 1, ChamberArrive),
                ChargeScrapes = Many("sfx_charge_scrape", 1, ChargeScrape),
                ChargeDashes = Many("sfx_charge_dash", 2, ChargeDash),
                ChargeStops = Many("sfx_charge_stop", 1, ChargeStop),
                ChargeCrashes = Many("sfx_charge_crash", 1, ChargeCrash),
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
            set.PortalSeals = Many("sfx_portal_sealed", 1, PortalSealThud); // KAN-49
            set.WardUps = Many("sfx_ward_up", 1, WardUp); // KAN-93
            set.WardBlocks = Many("sfx_ward_block", 2, WardBlock);
            set.WardDowns = Many("sfx_ward_down", 1, WardDown);
            set.ChargeRumble = Save("amb_charge_rumble_loop", AmbienceRate, ChargeRumble(AmbienceRate, 2f, 17), ambience: false);
            set.KnightGrunts = Many("sfx_knight_grunt", 3, KnightGrunt); // KAN-187
            set.Heartbeat = Save("sfx_heartbeat_01", Rate, Heartbeat(), ambience: false); // KAN-187
            set.SoulBursts = Many("sfx_soul_burst", 2, SoulBurst); // KAN-66
            set.EmberIgnites = Many("sfx_ember_ignite", 2, EmberIgnite); // KAN-201
            set.EmberCrackles = Many("sfx_ember_crackle_loop", 2, EmberCrackle);
            set.EmberHotShots = Many("sfx_ember_hot_shot", 2, EmberHotShot);
            set.FrostFreezes = Many("sfx_frost_freeze", 2, FrostFreeze); // KAN-91
            set.TomeThumps = Many("sfx_tome_thump", 2, TomeThump); // KAN-123
            set.TomeWhooshes = Many("sfx_tome_whoosh_loop", 1, TomeWhoosh);
            set.MeleeBloaterSwells = Many("sfx_bloater_swell", 2, BloaterSwell); // KAN-132
            set.MeleeBloaterBursts = Many("sfx_bloater_burst", 2, BloaterPop);
            set.MeleeGhoulScreeches = Many("sfx_ghoul_screech", 2, GhoulScreech);
            set.MeleeGhoulWhooshes = Many("sfx_ghoul_whoosh", 1, GhoulWhoosh);
            set.MeleeGhoulThuds = Many("sfx_ghoul_thud", 2, GhoulThud);
            set.MeleeBruteGrunts = Many("sfx_brute_grunt", 2, BruteGrunt);
            set.MeleeBruteSlams = Many("sfx_brute_slam", 2, BruteSlamBoom);
            set.MeleeCloudHiss = Save("sfx_toxic_cloud_hiss_loop", Rate, ToxicHiss(), ambience: false);
            set.BowTwangs = Many(BowTwangName, 3, BowTwang); // KAN-227
            return set;
        }

        private static AudioClip[] Many(string name, int count, Func<int, float[]> make)
        {
            var clips = new AudioClip[count];
            for (int i = 0; i < count; i++) clips[i] = Save($"{name}_{i + 1:00}", Rate, make(i + 1), ambience: false);
            return clips;
        }

        // ------------------------------------------------------------ Sounds

        /// <summary>
        /// Crossbow release (KAN-231: woody, no metal ring): trigger latch tick, a dry wooden "thock" (struck-wood modes at
        /// 1 : 2.57 : 4.3 : 6.1, the higher ones dying fast), a short stock clack, a low string thrum and a bolt whoosh.
        /// </summary>
        private static float[] CrossbowShot(int seed)
        {
            var rng = new System.Random(seed * 101);
            var b = new float[(int)(Rate * 0.5f)];
            float v = 1f + (float)(rng.NextDouble() - 0.5) * 0.1f;
            float f0 = 205f * v;                                          // stock / prod body
            Click(b, 0f, 0.003f, 0.32f, 4200f, rng);                      // trigger latch
            Sweep(b, 0.003f, 0.30f, f0 * 1.04f, f0, 0.62f, 26f, 1);        // wood modes: fundamental (slight pitch settle)
            Sweep(b, 0.003f, 0.18f, f0 * 2.57f, f0 * 2.57f, 0.34f, 42f, 1);
            Sweep(b, 0.003f, 0.12f, f0 * 4.30f, f0 * 4.30f, 0.20f, 62f, 1);
            Sweep(b, 0.003f, 0.08f, f0 * 6.10f, f0 * 6.10f, 0.10f, 85f, 1);
            NoiseBurst(b, 0.002f, 0.035f, 0.42f, 300f, 1500f, 70f, rng);   // knock: the hit itself
            NoiseBurst(b, 0.006f, 0.025f, 0.22f, 1600f, 4200f, 110f, rng);  // stock clack
            Sweep(b, 0.004f, 0.30f, 118f * v, 84f * v, 0.34f, 11f, 2);      // low string thrum
            NoiseBurst(b, 0.012f, 0.20f, 0.17f, 700f, 2600f, 12f, rng);     // bolt whoosh
            return Normalize(b, 0.9f);
        }

        /// <summary>
        /// Bow release (KAN-231): soft finger tick, a short light string twang, then the arrow whistle: a tonal sine and
        /// narrow band-passed noise that fall together from ~2.8 to ~1.9 kHz (the arrow flies off, Doppler) with a slight
        /// flutter from the fletching, plus a little air. Softer than the crossbow.
        /// </summary>
        private static float[] BowTwang(int seed)
        {
            var rng = new System.Random(seed * 131);
            float len = 0.6f;
            var b = new float[(int)(Rate * len)];
            float v = 1f + (float)(rng.NextDouble() - 0.5) * 0.1f;
            Click(b, 0f, 0.003f, 0.22f, 6500f, rng);                        // string leaves the fingers
            Sweep(b, 0.002f, 0.22f, 236f * v, 192f * v, 0.26f, 14f, 3);       // short string twang
            Sweep(b, 0f, 0.12f, 96f * v, 74f * v, 0.18f, 22f, 1);            // limb thrum
            // whistle: starts 15 ms after the release, swells in 40 ms, fades by 0.55 s
            int s0 = (int)(0.015f * Rate), e0 = Mathf.Min(b.Length, s0 + (int)(0.54f * Rate));
            var bp = new Biquad();
            double phase = 0;
            float hiF = 2800f * v, loF = 1900f * v, rate = 4.5f + (float)rng.NextDouble();
            for (int i = s0; i < e0; i++)
            {
                float t = (i - s0) / (float)Rate, k = t / 0.54f;
                float f = loF + (hiF - loF) * Mathf.Exp(-k * 2.6f);           // Doppler fall, fast at first
                f *= 1f + 0.012f * Mathf.Sin(t * rate * Mathf.PI * 2f);        // fletching flutter
                phase += f / Rate;
                float env = Mathf.Min(1f, t / 0.04f) * (1f - k) * (1f - k) * (1f - 0.35f * k);
                if ((i & 31) == 0) bp.SetBandPass(f, 14f, Rate);
                float tone = Mathf.Sin((float)(phase * Math.PI * 2));
                b[i] += (tone * 0.32f + bp.Process(Noise(rng)) * 1.6f) * env;
            }
            NoiseBurst(b, 0.01f, 0.25f, 0.12f, 900f, 3500f, 9f, rng);         // air
            return Normalize(b, 0.8f);
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
        /// Level-up re-roll (KAN-67, ~0.45 s): three quick card flicks (paper snaps) over an airy whoosh rising then falling,
        /// and a soft wooden die knock with a faint A5 ring at the end, as the new cards open.
        /// </summary>
        private static float[] CardReroll(int seed)
        {
            var rng = new System.Random(seed * 1171);
            float len = 0.5f;
            var b = new float[(int)(Rate * len)];
            var bp = new Biquad();
            for (int i = 0; i < (int)(Rate * 0.34f); i++)                      // whoosh: band sweeps up then down
            {
                float t = i / (float)Rate, k = t / 0.34f;
                float env = Mathf.Sin(k * Mathf.PI);
                bp.SetBandPass(Mathf.Lerp(700f, 3600f, Mathf.Sin(k * Mathf.PI)), 1.1f, Rate);
                b[i] += bp.Process(Noise(rng)) * env * env * 0.7f;
            }
            for (int i = 0; i < 3; i++)                                        // card flicks
                NoiseBurst(b, 0.04f + i * 0.06f, 0.03f, 0.35f, 2200f, 7500f, 120f, rng);
            NoiseBurst(b, 0.3f, 0.06f, 0.4f, 150f, 1400f, 60f, rng);             // die knock
            Sweep(b, 0.3f, 0.12f, 220f, 180f, 0.3f, 30f, 2);                     // knock body
            Sweep(b, 0.31f, 0.19f, 880f, 880f, 0.07f, 14f, 1);                   // A5 ring
            return Normalize(b, 0.7f);
        }

        /// <summary>Skill reel tick (KAN-159, ~40 ms): a dry wooden ratchet click, high and quiet, so 16 a second stay soft.</summary>
        private static float[] ReelTick(int seed)
        {
            var rng = new System.Random(seed * 1597);
            var b = new float[(int)(Rate * 0.04f)];
            Click(b, 0f, 0.003f, 0.5f, 6200f, rng);                             // pawl click
            NoiseBurst(b, 0f, 0.025f, 0.3f, 1800f, 5200f, 140f, rng);           // wood body
            Sweep(b, 0f, 0.03f, 1760f + seed * 90f, 1700f, 0.08f, 90f, 1);     // tiny ring, a hair apart per variant
            return Normalize(b, 0.45f);
        }

        /// <summary>
        /// Skill reel stop (KAN-159, ~0.22 s): a heavy wooden clunk with an iron latch, low enough to sit under the tier
        /// reveal that plays at the same moment.
        /// </summary>
        private static float[] ReelStop(int seed)
        {
            var rng = new System.Random(seed * 1609);
            var b = new float[(int)(Rate * 0.22f)];
            NoiseBurst(b, 0f, 0.05f, 0.7f, 90f, 900f, 55f, rng);                // wooden block
            Sweep(b, 0f, 0.14f, 150f, 104f, 0.55f, 26f, 2);                     // body thud
            Click(b, 0.006f, 0.005f, 0.35f, 4200f, rng);                        // iron latch
            Sweep(b, 0.008f, 0.12f, 523.3f, 520f, 0.06f, 30f, 1);               // faint C5 ring
            return Normalize(b, 0.7f);
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

        /// <summary>
        /// Owned skill levelled up (KAN-74), under the HUD slot glow once play resumes: a short rising bell pair (B5 then
        /// E6, a fourth up) with a soft shimmer. Small and bright, not the boon strike: it marks the HUD, not the pick.
        /// </summary>
        private static float[] SkillUpgrade(int seed)
        {
            var rng = new System.Random(seed * 1949);
            var b = new float[(int)(Rate * 0.75f)];
            Sweep(b, 0f, 0.4f, 987.8f, 987.8f, 0.3f, 9f, 2);                   // B5
            Sweep(b, 0f, 0.25f, 1975.5f, 1975.6f, 0.08f, 14f, 1);              // octave shimmer
            Sweep(b, 0.09f, 0.65f, 1318.5f, 1318.5f, 0.34f, 5.5f, 2);          // E6, rings out
            Sweep(b, 0.09f, 0.4f, 2637.6f, 2637f, 0.09f, 10f, 1);
            Sweep(b, 0.09f, 0.3f, 1318.5f * 2.76f, 1318.5f * 2.76f, 0.04f, 18f, 1); // bell partial
            NoiseBurst(b, 0.09f, 0.4f, 0.06f, 5000f, 11000f, 9f, rng);         // sparkle
            return Normalize(b, 0.65f);
        }

        /// <summary>
        /// Healing heart taken in the boss fight (KAN-60): soft and warm, not a loot sound. A low A major chord (A3 E4 A4)
        /// swells in over 80 ms, a C#5 joins on top, a breath of air rises under it; slow decay. Quiet peak.
        /// </summary>
        private static float[] HeartHeal(int seed)
        {
            var rng = new System.Random(seed * 2203);
            var b = new float[(int)(Rate * 1.1f)];
            Swell(b, 0f, 1.05f, 220f, 0.3f, 0.08f, 3.2f, 3);                     // A3
            Swell(b, 0f, 1.0f, 329.6f, 0.22f, 0.09f, 3.6f, 2);                   // E4
            Swell(b, 0.03f, 0.95f, 440f, 0.2f, 0.1f, 4f, 2);                     // A4
            Swell(b, 0.12f, 0.85f, 554.4f, 0.14f, 0.12f, 4.5f, 1);               // C#5, the lift
            Sweep(b, 0.02f, 0.5f, 300f, 330f, 0.05f, 6f, 1);                     // slight upward glide
            NoiseBurst(b, 0f, 0.6f, 0.05f, 500f, 2400f, 5f, rng);                // breath
            return Normalize(b, 0.5f);
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

        // ------------------------------------------------------------ Boss charge (KAN-168)

        /// <summary>
        /// Boss charge wind-up growl (seamless 2 s loop): two low saws (55 + 82.5 Hz) through a low-pass with a throat
        /// flutter (7 Hz), a deep noise rumble under it. The tail is crossfaded into the start, so it loops without a seam.
        /// The runtime raises its pitch and volume with the lane fill.
        /// </summary>
        private static float[] ChargeRumble(int rate, float seconds, int seed)
        {
            var rng = new System.Random(seed);
            int n = (int)(rate * seconds);
            var b = new float[n];
            var growl = new Biquad(); growl.SetLowPass(380f, 1.4f, rate);
            var rumble = new Biquad(); rumble.SetLowPass(110f, 0.8f, rate);
            double p1 = 0, p2 = 0;
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)rate;
                p1 += 55f / rate;
                p2 += 82.5f / rate;
                float flutter = 0.65f + 0.35f * Mathf.Sin(t * Mathf.PI * 2f * 7f);
                float saw = Saw(p1) * 0.6f + Saw(p2) * 0.35f;
                b[i] = growl.Process(saw) * flutter + rumble.Process(Noise(rng)) * 1.6f;
            }
            int fade = rate / 4;
            for (int i = 0; i < fade; i++)
            {
                float w = i / (float)fade;
                b[i] = b[i] * w + b[n - fade + i] * (1f - w);
            }
            var loop = new float[n - fade];
            Array.Copy(b, loop, loop.Length);
            float max = 1e-6f;
            foreach (float x in loop) max = Mathf.Max(max, Mathf.Abs(x));
            for (int i = 0; i < loop.Length; i++) loop[i] *= 0.7f / max;
            return loop;
        }

        /// <summary>Heavy foot plants and scrapes back through grit as the boss braces (~0.55 s): low thud, gritty scrape, crunch.</summary>
        private static float[] ChargeScrape(int seed)
        {
            var rng = new System.Random(seed * 4111);
            float len = 0.55f;
            var b = new float[(int)(Rate * len)];
            Sweep(b, 0f, 0.25f, 92f, 58f, 0.8f, 14f, 2);                       // foot plant
            NoiseBurst(b, 0f, 0.12f, 0.45f, 70f, 420f, 24f, rng);
            var bp = new Biquad();
            float grit = 1f;
            for (int i = (int)(Rate * 0.04f); i < b.Length; i++)
            {
                float t = i / (float)Rate, k = (t - 0.04f) / (len - 0.04f);
                if (i % (Rate / 180) == 0) grit = 0.45f + (float)rng.NextDouble() * 0.9f;  // uneven ground
                float env = Mathf.SmoothStep(0f, 1f, k / 0.15f) * (1f - k) * (1f - k);
                bp.SetBandPass(Mathf.Lerp(1400f, 650f, k), 1.1f, Rate);
                b[i] += bp.Process(Noise(rng)) * env * grit * 0.7f;
            }
            for (int i = 0; i < 6; i++)                                         // stones kicked
                Click(b, 0.06f + (float)rng.NextDouble() * 0.35f, 0.005f, 0.18f, 3200f, rng);
            return Normalize(b, 0.8f);
        }

        /// <summary>
        /// The dash (~0.75 s): heavy whoosh (band-passed noise swelling up and down), armour clank (clicks + inharmonic
        /// plate ring) and a short low grunt (saw through two formants).
        /// </summary>
        private static float[] ChargeDash(int seed)
        {
            var rng = new System.Random(seed * 4219);
            float len = 0.75f;
            var b = new float[(int)(Rate * len)];
            float v = 1f + (float)(rng.NextDouble() - 0.5) * 0.1f;
            var bp = new Biquad();
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate, k = t / len;
                float env = Mathf.Sin(Mathf.Min(1f, k / 0.85f) * Mathf.PI);
                bp.SetBandPass(Mathf.Lerp(260f, 900f, Mathf.Sin(k * Mathf.PI)) * v, 0.9f, Rate);
                b[i] += bp.Process(Noise(rng)) * env * env * 1.1f;
            }
            float[] plate = { 1240f, 1873f, 2650f };
            for (int n = 0; n < plate.Length; n++)                              // armour plates knock and ring
                Sweep(b, 0.015f, 0.3f, plate[n] * v, plate[n] * v * 0.995f, 0.12f / (n + 1), 16f, 1);
            Click(b, 0.01f, 0.006f, 0.45f, 4200f, rng);
            Click(b, 0.1f, 0.006f, 0.3f, 3800f, rng);
            var f1 = new Biquad(); var f2 = new Biquad();                       // grunt
            f1.SetBandPass(520f, 5f, Rate); f2.SetBandPass(950f, 6f, Rate);
            double phase = 0;
            int g0 = (int)(Rate * 0.02f), g1 = (int)(Rate * 0.32f);
            for (int i = g0; i < g1; i++)
            {
                float k = (i - g0) / (float)(g1 - g0);
                phase += Mathf.Lerp(98f, 72f, k) * v / Rate;
                float src = Saw(phase) * 0.5f + Noise(rng) * 0.3f;
                float env = Mathf.SmoothStep(0f, 1f, k / 0.12f) * (1f - k);
                b[i] += (f1.Process(src) + f2.Process(src) * 0.5f) * env * 1.3f;
            }
            return Normalize(b, 0.85f);
        }

        /// <summary>The dash ends on open ground (~0.6 s): dull thud, skid through snow and grit.</summary>
        private static float[] ChargeStop(int seed)
        {
            var rng = new System.Random(seed * 4327);
            var b = new float[(int)(Rate * 0.6f)];
            Sweep(b, 0f, 0.35f, 86f, 42f, 0.9f, 10f, 2);
            NoiseBurst(b, 0f, 0.18f, 0.55f, 60f, 480f, 16f, rng);
            NoiseBurst(b, 0.01f, 0.4f, 0.28f, 450f, 2600f, 8f, rng);           // skid
            for (int i = 0; i < 4; i++)
                Click(b, 0.03f + (float)rng.NextDouble() * 0.2f, 0.005f, 0.14f, 3000f, rng);
            return Normalize(b, 0.85f);
        }

        /// <summary>
        /// Hallowed Shield ward rises (KAN-93, ~0.9 s): an airy whoosh up, a low hum swell (D3) and a holy fifth (D5 A5)
        /// that rings out with a little shimmer. Warm, not a loot sound.
        /// </summary>
        private static float[] WardUp(int seed)
        {
            var rng = new System.Random(seed * 2311);
            var b = new float[(int)(Rate * 0.95f)];
            NoiseBurst(b, 0f, 0.3f, 0.22f, 700f, 3600f, 9f, rng);              // whoosh up
            Sweep(b, 0f, 0.35f, 300f, 600f, 0.08f, 8f, 1);                     // rising air tone
            Swell(b, 0f, 0.9f, 146.8f, 0.3f, 0.06f, 3.5f, 3);                  // D3 hum
            Swell(b, 0.06f, 0.85f, 587.3f, 0.2f, 0.03f, 4.5f, 2);              // D5
            Swell(b, 0.1f, 0.8f, 880f, 0.16f, 0.03f, 5f, 2);                   // A5
            NoiseBurst(b, 0.12f, 0.6f, 0.05f, 6000f, 11000f, 7f, rng);         // shimmer
            return Normalize(b, 0.6f);
        }

        /// <summary>Ward blocks a hit (KAN-93, ~0.3 s): a short glassy tink (bell partials) over a dull knock. Quiet.</summary>
        private static float[] WardBlock(int seed)
        {
            var rng = new System.Random(seed * 2333);
            var b = new float[(int)(Rate * 0.32f)];
            float f = seed == 1 ? 1760f : 1975.5f;                             // A6 / B6
            Click(b, 0f, 0.008f, 0.4f, 6000f, rng);
            Sweep(b, 0f, 0.3f, f, f, 0.22f, 16f, 1);
            Sweep(b, 0f, 0.2f, f * 2.76f, f * 2.76f, 0.07f, 26f, 1);           // bell partial
            Swell(b, 0f, 0.15f, 180f, 0.18f, 0.004f, 30f, 2);                  // knock
            return Normalize(b, 0.45f);
        }

        /// <summary>Ward runs out (KAN-93, ~0.7 s): a soft glass shatter falling in pitch, a down-sweep, and the fifth fading.</summary>
        private static float[] WardDown(int seed)
        {
            var rng = new System.Random(seed * 2341);
            var b = new float[(int)(Rate * 0.7f)];
            NoiseBurst(b, 0f, 0.35f, 0.16f, 3500f, 10000f, 10f, rng);          // shatter grit
            for (int k = 0; k < 5; k++)                                        // falling glass tinks
            {
                float f = 2400f - k * 260f + (float)rng.NextDouble() * 120f;
                Sweep(b, 0.01f + k * 0.035f, 0.18f, f, f, 0.08f, 22f, 1);
            }
            Sweep(b, 0.02f, 0.5f, 880f, 440f, 0.14f, 6f, 2);                   // fifth falls away
            Swell(b, 0f, 0.4f, 146.8f, 0.12f, 0.01f, 7f, 2);
            return Normalize(b, 0.5f);
        }

        /// <summary>
        /// Knight walks up to the sealed portal (KAN-49, ~0.7 s): a muted, low stone thud with a little grit and a faint
        /// low rune hum under it. Quiet and dull on purpose: "not yet", not an alarm.
        /// </summary>
        private static float[] PortalSealThud(int seed)
        {
            var rng = new System.Random(seed * 5003);
            var b = new float[(int)(Rate * 0.7f)];
            Sweep(b, 0f, 0.42f, 74f, 44f, 0.9f, 9f, 2);                // deep stone thud
            NoiseBurst(b, 0f, 0.12f, 0.4f, 45f, 300f, 20f, rng);        // muffled body
            NoiseBurst(b, 0.01f, 0.22f, 0.08f, 600f, 2000f, 14f, rng);  // grit
            Sweep(b, 0.03f, 0.6f, 196f, 188f, 0.1f, 4.5f, 1);           // faint rune hum
            return Normalize(b, 0.8f);
        }

        /// <summary>
        /// The dash slams into a pillar or the wall (~1 s): crack, very low heavy thud, stone debris that trickles down,
        /// one short armour ring.
        /// </summary>
        private static float[] ChargeCrash(int seed)
        {
            var rng = new System.Random(seed * 4421);
            float len = 1f;
            var b = new float[(int)(Rate * len)];
            Click(b, 0f, 0.008f, 0.8f, 5000f, rng);                             // stone cracks
            Sweep(b, 0f, 0.6f, 72f, 32f, 1f, 6f, 2);                           // body hits stone
            NoiseBurst(b, 0f, 0.35f, 0.7f, 40f, 360f, 9f, rng);
            NoiseBurst(b, 0.005f, 0.25f, 0.25f, 800f, 3400f, 14f, rng);
            Sweep(b, 0.01f, 0.35f, 1520f, 1505f, 0.08f, 12f, 1);               // armour ring
            for (int i = 0; i < 12; i++)                                        // debris falls, fewer and softer
            {
                float k = i / 12f;
                Click(b, 0.06f + k * 0.75f + (float)rng.NextDouble() * 0.05f, 0.006f, 0.3f * (1f - k) + 0.05f,
                    2400f + (float)rng.NextDouble() * 2600f, rng);
            }
            return Normalize(b, 0.92f);
        }

        /// <summary>
        /// Knight takes a hit (KAN-187, ~0.4 s): a dull body thud under the armour (low sweep + muffled noise, a faint
        /// mail rattle) and a short closed-mouth grunt ("hmph": saw voice through two formants, pitch falling). Each seed
        /// shifts the voice pitch and formants a little, so repeats do not sound the same.
        /// </summary>
        private static float[] KnightGrunt(int seed)
        {
            var rng = new System.Random(seed * 6007);
            float len = 0.42f;
            var b = new float[(int)(Rate * len)];
            float v = 1f + (seed - 2) * 0.06f;
            Sweep(b, 0f, 0.22f, 96f, 52f, 0.75f, 16f, 2);                    // body thud
            NoiseBurst(b, 0f, 0.1f, 0.35f, 60f, 520f, 30f, rng);             // muffled impact
            for (int i = 0; i < 4; i++)                                         // mail rattle
                Click(b, 0.005f + (float)rng.NextDouble() * 0.07f, 0.004f, 0.06f, 5200f, rng);
            var f1 = new Biquad(); var f2 = new Biquad();
            f1.SetBandPass(560f * v, 4.5f, Rate); f2.SetBandPass(1050f * v, 5.5f, Rate);
            var lp = new Biquad(); lp.SetLowPass(2200f, 0.7f, Rate);
            double phase = 0;
            int g0 = (int)(Rate * 0.015f), g1 = (int)(Rate * 0.27f);
            for (int i = g0; i < g1; i++)
            {
                float k = (i - g0) / (float)(g1 - g0);
                phase += Mathf.Lerp(150f, 104f, k) * v / Rate;
                float src = Saw(phase) * 0.55f + Noise(rng) * 0.22f;        // breathy voice
                float env = Mathf.SmoothStep(0f, 1f, k / 0.08f) * (1f - k) * (1f - k);
                b[i] += lp.Process(f1.Process(src) + f2.Process(src) * 0.45f) * env * 1.5f;
            }
            return Normalize(b, 0.8f);
        }

        /// <summary>
        /// Low-health heartbeat (KAN-187, 0.5 s): a soft deep "lub" and a weaker "dub" 0.2 s later, low-passed so it sits
        /// under the mix. Played once per beat at runtime (the beat rate is not baked in).
        /// </summary>
        private static float[] Heartbeat()
        {
            var b = new float[(int)(Rate * 0.5f)];
            Sweep(b, 0f, 0.16f, 62f, 40f, 1f, 22f, 2);                       // lub
            Sweep(b, 0.2f, 0.14f, 70f, 46f, 0.6f, 26f, 2);                   // dub
            var lp = new Biquad(); lp.SetLowPass(260f, 0.7f, Rate);
            for (int i = 0; i < b.Length; i++) b[i] = lp.Process(b[i]);
            return Normalize(b, 0.85f);
        }

        /// <summary>
        /// Soul Burst (KAN-66, ~0.6 s): a dull low "whump" (falling sine + muffled noise body, low-passed), a soft air puff,
        /// and a short dark chime tail (minor third, low bell partials) that dies fast. Quiet top end: many play in a chain.
        /// </summary>
        private static float[] SoulBurst(int seed)
        {
            var rng = new System.Random(seed * 6607);
            var b = new float[(int)(Rate * 0.62f)];
            float v = seed == 1 ? 1f : 0.94f;
            Sweep(b, 0f, 0.3f, 96f * v, 38f * v, 1f, 12f, 2);                 // whump
            NoiseBurst(b, 0f, 0.16f, 0.5f, 50f, 380f, 18f, rng);               // muffled body
            NoiseBurst(b, 0.005f, 0.22f, 0.12f, 700f, 2600f, 14f, rng);        // air puff
            var lp = new Biquad(); lp.SetLowPass(900f, 0.7f, Rate);
            for (int i = 0; i < b.Length; i++) b[i] = lp.Process(b[i]);
            Swell(b, 0.03f, 0.55f, 293.7f * v, 0.13f, 0.01f, 7f, 2);          // D4
            Swell(b, 0.05f, 0.5f, 349.2f * v, 0.09f, 0.01f, 8f, 2);           // F4: dark minor third
            Sweep(b, 0.03f, 0.35f, 293.7f * 2.76f * v, 293.7f * 2.76f * v, 0.03f, 14f, 1); // bell partial
            return Normalize(b, 0.8f);
        }

        /// <summary>
        /// Ember ignite (KAN-201, ~0.6 s): gas catching fire. A soft "whoomp" (low band noise that swells in over 25 ms,
        /// a falling low sine under it), then a hiss / sizzle tail (high band noise with fast crackle flutter) that dies out.
        /// </summary>
        private static float[] EmberIgnite(int seed)
        {
            var rng = new System.Random(seed * 7019);
            var b = new float[(int)(Rate * 0.62f)];
            float v = seed == 1 ? 1f : 0.92f;
            var lo = new Biquad(); lo.SetLowPass(520f * v, 0.8f, Rate);
            var hp = new Biquad(); hp.SetHighPass(2600f, 0.7f, Rate);
            var lp = new Biquad(); lp.SetLowPass(8500f, 0.7f, Rate);
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate;
                float whoomp = Mathf.Min(1f, t / 0.025f) * Mathf.Exp(-Mathf.Max(0f, t - 0.025f) * 9f);
                float sizzle = Mathf.Min(1f, Mathf.Max(0f, t - 0.03f) / 0.04f) * Mathf.Exp(-Mathf.Max(0f, t - 0.07f) * 6.5f);
                float flutter = 0.55f + 0.45f * (float)rng.NextDouble(); // crackly hiss: random grain level
                b[i] += lo.Process(Noise(rng)) * whoomp * 1.4f;
                b[i] += lp.Process(hp.Process(Noise(rng))) * sizzle * flutter * 0.42f;
            }
            Sweep(b, 0f, 0.28f, 120f * v, 52f * v, 0.55f, 11f, 2);               // body under the whoomp
            for (int k = 0; k < 7; k++)                                          // a few sparks pop off
                Click(b, 0.05f + (float)rng.NextDouble() * 0.4f, 0.003f, 0.25f + (float)rng.NextDouble() * 0.2f, 6000f, rng);
            return Normalize(b, 0.8f);
        }

        /// <summary>
        /// Burning crackle (KAN-201, seamless ~2.25 s loop): a quiet low fire roar (low-passed noise with a slow flicker) and
        /// random wood-fire pops (short bright ticks, a few bigger snaps). The tail crossfades into the start, so it loops.
        /// </summary>
        private static float[] EmberCrackle(int seed)
        {
            var rng = new System.Random(seed * 3491);
            float seconds = 2.5f;
            int n = (int)(Rate * seconds), fade = Rate / 4;
            var b = new float[n];
            var roar = new Biquad(); roar.SetLowPass(340f, 0.7f, Rate);
            var air = new Biquad(); air.SetBandPass(1400f, 0.8f, Rate);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float flicker = 0.75f + 0.25f * Mathf.Sin(t * Mathf.PI * 2f * 1.6f) * Mathf.Sin(t * Mathf.PI * 2f * 0.6f + seed);
                b[i] = roar.Process(Noise(rng)) * 0.9f * flicker + air.Process(Noise(rng)) * 0.06f;
            }
            for (float t = 0.02f; t < seconds - 0.01f; t += 0.025f + (float)rng.NextDouble() * 0.11f)
            {
                bool snap = rng.NextDouble() < 0.12;
                Click(b, t, snap ? 0.006f : 0.0025f, snap ? 0.55f : 0.12f + (float)rng.NextDouble() * 0.2f, snap ? 4200f : 7000f, rng);
            }
            for (int i = 0; i < fade; i++)
            {
                float w = i / (float)fade;
                b[i] = b[i] * w + b[n - fade + i] * (1f - w);
            }
            var loop = new float[n - fade];
            Array.Copy(b, loop, loop.Length);
            // Scale only: Normalize's end fade would dip to silence at the loop point.
            float max = 1e-6f;
            foreach (float x in loop) max = Mathf.Max(max, Mathf.Abs(x));
            for (int i = 0; i < loop.Length; i++) loop[i] *= 0.7f / max;
            return loop;
        }

        /// <summary>
        /// Hot shot layer (KAN-201, ~0.35 s) on top of the crossbow shot while Ember Bolts is owned: a fire "fwoosh"
        /// (band noise sweeping down from 2.4 kHz to 500 Hz as the burning bolt leaves) and two or three spark ticks.
        /// </summary>
        private static float[] EmberHotShot(int seed)
        {
            var rng = new System.Random(seed * 5303);
            var b = new float[(int)(Rate * 0.36f)];
            var bp = new Biquad();
            for (int i = 0; i < b.Length; i++)
            {
                float t = i / (float)Rate, k = t / 0.36f;
                bp.SetBandPass(Mathf.Lerp(2400f, 500f, Mathf.Sqrt(k)), 1.1f, Rate);
                float env = Mathf.Min(1f, t / 0.012f) * Mathf.Exp(-t * 10f);
                b[i] = bp.Process(Noise(rng)) * env;
            }
            int sparks = 2 + seed % 2;
            for (int s = 0; s < sparks; s++)
                Click(b, 0.01f + (float)rng.NextDouble() * 0.12f, 0.0025f, 0.18f, 7500f, rng);
            return Normalize(b, 0.75f);
        }

        /// <summary>
        /// Bloater swell (KAN-132, ~1.05 s, the 1 s swell): a wet belly gurgle. Low muffled noise that grows, bubble blips
        /// (short sines with a fast upward glide) that come faster and higher towards the end, so the swell rises to the pop.
        /// </summary>
        private static float[] BloaterSwell(int seed)
        {
            var rng = new System.Random(seed * 8111);
            float len = 1.05f;
            var b = new float[(int)(Rate * len)];
            var lp = new Biquad(); lp.SetLowPass(420f, 0.8f, Rate);
            for (int i = 0; i < b.Length; i++)
            {
                float k = i / (float)b.Length;
                b[i] += lp.Process(Noise(rng)) * (0.25f + 0.75f * k * k) * 0.9f;
            }
            for (float t = 0.02f; t < len - 0.08f;)
            {
                float k = t / len;
                float f = Mathf.Lerp(120f, 380f, k * k) * (0.85f + 0.3f * (float)rng.NextDouble());
                Sweep(b, t, 0.07f, f, f * 1.8f, 0.35f + 0.4f * k, 38f, 1); // bubble: pitch glides up
                t += Mathf.Lerp(0.11f, 0.035f, k) * (0.7f + 0.6f * (float)rng.NextDouble());
            }
            Sweep(b, 0f, len, 58f, 96f, 0.35f, 0.4f, 2); // the belly hum rises
            return Normalize(b, 0.75f);
        }

        /// <summary>
        /// Bloater burst (KAN-132, ~0.7 s): a wet pop (sharp low thump + click), then a splash (band noise that dies fast)
        /// with droplet ticks falling over 0.4 s.
        /// </summary>
        private static float[] BloaterPop(int seed)
        {
            var rng = new System.Random(seed * 8221);
            var b = new float[(int)(Rate * 0.7f)];
            float v = seed == 1 ? 1f : 0.9f;
            Sweep(b, 0f, 0.18f, 150f * v, 45f * v, 1f, 18f, 2);            // pop body
            Click(b, 0f, 0.004f, 0.9f, 3800f, rng);                          // skin snap
            NoiseBurst(b, 0.005f, 0.4f, 0.55f, 260f, 2600f, 9f, rng);        // wet splash
            NoiseBurst(b, 0.01f, 0.25f, 0.2f, 1800f, 6000f, 16f, rng);       // spray hiss
            for (int k = 0; k < 9; k++)                                      // droplets land
            {
                float t = 0.08f + (float)rng.NextDouble() * 0.45f;
                float f = 900f + (float)rng.NextDouble() * 1400f;
                Sweep(b, t, 0.03f, f, f * 0.6f, 0.12f, 70f, 1);
            }
            return Normalize(b, 0.82f);
        }

        /// <summary>
        /// Ghoul screech (KAN-132, ~0.45 s, the crouch): a harsh rasping shriek. Saw voice gliding up then down through two
        /// narrow formants, with throat noise; the second clip is a little higher.
        /// </summary>
        private static float[] GhoulScreech(int seed)
        {
            var rng = new System.Random(seed * 8317);
            float len = 0.45f;
            var b = new float[(int)(Rate * len)];
            float v = seed == 1 ? 1f : 1.12f;
            var f1 = new Biquad(); var f2 = new Biquad();
            f1.SetBandPass(1300f * v, 3.5f, Rate); f2.SetBandPass(2600f * v, 5f, Rate);
            double phase = 0;
            for (int i = 0; i < b.Length; i++)
            {
                float k = i / (float)b.Length;
                float pitch = Mathf.Lerp(420f, 640f, Mathf.Sin(Mathf.Min(1f, k * 1.4f) * Mathf.PI * 0.5f)) * (1f - 0.25f * k * k) * v;
                pitch *= 1f + 0.03f * Mathf.Sin(i / (float)Rate * 2f * Mathf.PI * 31f); // ragged throat
                phase += pitch / Rate;
                float src = Saw(phase) * 0.6f + Noise(rng) * 0.4f;
                float env = Mathf.Min(1f, k / 0.06f) * (1f - k) * (1f - k * 0.4f);
                b[i] = (f1.Process(src) + f2.Process(src) * 0.6f) * env * 1.6f;
            }
            return Normalize(b, 0.78f);
        }

        /// <summary>Ghoul take-off (KAN-132, ~0.35 s): an air whoosh (band noise sweeping 400 Hz up to 2.2 kHz and back).</summary>
        private static float[] GhoulWhoosh(int seed)
        {
            var rng = new System.Random(seed * 8419);
            var b = new float[(int)(Rate * 0.35f)];
            var bp = new Biquad();
            for (int i = 0; i < b.Length; i++)
            {
                float k = i / (float)b.Length;
                bp.SetBandPass(Mathf.Lerp(400f, 2200f, Mathf.Sin(k * Mathf.PI)), 1.3f, Rate);
                b[i] = bp.Process(Noise(rng)) * Mathf.Sin(k * Mathf.PI) * Mathf.Sin(k * Mathf.PI);
            }
            return Normalize(b, 0.7f);
        }

        /// <summary>Ghoul landing (KAN-132, ~0.35 s): a body thud on snow (low sine drop, muffled noise) + a soft snow crunch.</summary>
        private static float[] GhoulThud(int seed)
        {
            var rng = new System.Random(seed * 8521);
            var b = new float[(int)(Rate * 0.35f)];
            float v = seed == 1 ? 1f : 0.92f;
            Sweep(b, 0f, 0.2f, 110f * v, 48f * v, 1f, 20f, 2);
            NoiseBurst(b, 0f, 0.12f, 0.45f, 60f, 600f, 26f, rng);
            NoiseBurst(b, 0.01f, 0.18f, 0.18f, 1500f, 5000f, 20f, rng); // snow crunch
            for (int k = 0; k < 5; k++) Click(b, 0.01f + (float)rng.NextDouble() * 0.08f, 0.003f, 0.12f, 4500f, rng);
            return Normalize(b, 0.8f);
        }

        /// <summary>
        /// Brute wind-up (KAN-132, ~0.55 s): a deep strained grunt. Low saw voice (85 to 70 Hz) through a dark formant, with
        /// a breath rasp; lower and longer than the Knight's grunt.
        /// </summary>
        private static float[] BruteGrunt(int seed)
        {
            var rng = new System.Random(seed * 8623);
            float len = 0.55f;
            var b = new float[(int)(Rate * len)];
            float v = seed == 1 ? 1f : 0.92f;
            var f1 = new Biquad(); var f2 = new Biquad();
            f1.SetBandPass(380f * v, 4f, Rate); f2.SetBandPass(820f * v, 5f, Rate);
            var lp = new Biquad(); lp.SetLowPass(1600f, 0.7f, Rate);
            double phase = 0;
            for (int i = 0; i < b.Length; i++)
            {
                float k = i / (float)b.Length;
                phase += Mathf.Lerp(88f, 70f, k) * v / Rate;
                float src = Saw(phase) * 0.6f + Noise(rng) * 0.3f;
                float env = Mathf.Min(1f, k / 0.12f) * (1f - k) * (0.6f + 0.4f * Mathf.Sin(k * Mathf.PI));
                b[i] = lp.Process(f1.Process(src) * 1.2f + f2.Process(src) * 0.5f) * env * 1.6f;
            }
            Sweep(b, 0f, len, 70f * v, 60f * v, 0.25f, 3f, 2); // chest under the voice
            return Normalize(b, 0.78f);
        }

        /// <summary>
        /// Brute slam (KAN-132, ~0.95 s): a heavy ground boom. Deep sine drop (75 to 28 Hz), a low noise body, a sharp ground
        /// crack on top, and rubble ticks rolling off for half a second.
        /// </summary>
        private static float[] BruteSlamBoom(int seed)
        {
            var rng = new System.Random(seed * 8727);
            var b = new float[(int)(Rate * 0.95f)];
            float v = seed == 1 ? 1f : 0.93f;
            Sweep(b, 0f, 0.7f, 75f * v, 28f * v, 1f, 5.5f, 3);              // boom
            NoiseBurst(b, 0f, 0.35f, 0.7f, 40f, 420f, 10f, rng);             // earth body
            NoiseBurst(b, 0f, 0.06f, 0.5f, 900f, 5000f, 45f, rng);           // crack
            Click(b, 0f, 0.006f, 0.8f, 3000f, rng);
            for (int k = 0; k < 16; k++)                                     // rubble
                Click(b, 0.05f + (float)rng.NextDouble() * 0.5f, 0.004f + (float)rng.NextDouble() * 0.004f,
                    0.08f + (float)rng.NextDouble() * 0.12f, 2500f + (float)rng.NextDouble() * 2500f, rng);
            var lp = new Biquad(); lp.SetLowPass(5200f, 0.7f, Rate);
            for (int i = 0; i < b.Length; i++) b[i] = lp.Process(b[i]);
            return Normalize(b, 0.88f);
        }

        /// <summary>
        /// Toxic cloud hiss (KAN-132, seamless ~1.75 s loop): a quiet gas hiss (high band noise with a slow flutter) and a few
        /// soft wet pops. The tail crossfades into the start.
        /// </summary>
        private static float[] ToxicHiss()
        {
            var rng = new System.Random(132);
            float seconds = 2f;
            int n = (int)(Rate * seconds), fade = Rate / 4;
            var b = new float[n];
            var hp = new Biquad(); hp.SetHighPass(2200f, 0.7f, Rate);
            var lp = new Biquad(); lp.SetLowPass(7000f, 0.7f, Rate);
            var lo = new Biquad(); lo.SetLowPass(300f, 0.7f, Rate);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float flutter = 0.7f + 0.3f * Mathf.Sin(t * Mathf.PI * 2f * 2f) * Mathf.Sin(t * Mathf.PI * 2f * 0.5f + 1.3f);
                b[i] = lp.Process(hp.Process(Noise(rng))) * 0.5f * flutter + lo.Process(Noise(rng)) * 0.25f;
            }
            for (int k = 0; k < 6; k++)
            {
                float t = 0.1f + (float)rng.NextDouble() * (seconds - 0.4f);
                float f = 200f + (float)rng.NextDouble() * 200f;
                Sweep(b, t, 0.06f, f, f * 1.6f, 0.15f, 45f, 1);
            }
            for (int i = 0; i < fade; i++)
            {
                float w = i / (float)fade;
                b[i] = b[i] * w + b[n - fade + i] * (1f - w);
            }
            var loop = new float[n - fade];
            Array.Copy(b, loop, loop.Length);
            float max = 1e-6f; // scale only: Normalize's end fade would dip at the loop point
            foreach (float x in loop) max = Mathf.Max(max, Mathf.Abs(x));
            for (int i = 0; i < loop.Length; i++) loop[i] *= 0.6f / max;
            return loop;
        }

        /// <summary>
        /// Open portal hum (KAN-39, 3D loop near the arch): low drone with a slow beat, a faint airy swirl.
        /// Every partial and the swirl modulation make whole cycles over the loop length, so it loops without a seam.
        /// </summary>
        /// <summary>
        /// Freeze Arrow freeze (KAN-91, ~0.7 s): ice forming over a body. A sharp crack (bright click + short high noise
        /// snap), a crunchy crystallising rustle (band noise with fast random grain), and a cold glassy ring (inharmonic
        /// high partials, like a struck icicle) that fades out. Thin low end: it sits on top of the horde mix.
        /// </summary>
        private static float[] FrostFreeze(int seed)
        {
            var rng = new System.Random(seed * 5153);
            var b = new float[(int)(Rate * 0.72f)];
            float v = seed == 1 ? 1f : 1.07f;
            Click(b, 0f, 0.004f, 0.9f, 9000f, rng);                             // crack
            NoiseBurst(b, 0.002f, 0.09f, 0.55f, 2500f, 9500f, 34f, rng);         // snap
            var bp = new Biquad(); bp.SetBandPass(4200f * v, 1.1f, Rate);
            for (int i = (int)(Rate * 0.01f); i < (int)(Rate * 0.32f); i++)       // crystallising crunch
            {
                float t = i / (float)Rate;
                float env = Mathf.Min(1f, (t - 0.01f) / 0.03f) * Mathf.Exp(-Mathf.Max(0f, t - 0.04f) * 11f);
                float grain = rng.NextDouble() < 0.18 ? 1f : 0.2f;
                b[i] += bp.Process(Noise(rng)) * env * grain * 0.9f;
            }
            for (int k = 0; k < 6; k++)                                            // ice ticks
                Click(b, 0.03f + (float)rng.NextDouble() * 0.22f, 0.002f, 0.22f + (float)rng.NextDouble() * 0.15f, 11000f, rng);
            float f = 1568f * v;                                                    // G6 glassy ring, inharmonic partials
            Swell(b, 0.012f, 0.68f, f, 0.16f, 0.004f, 6f, 1);
            Swell(b, 0.012f, 0.5f, f * 2.32f, 0.08f, 0.004f, 9f, 1);
            Swell(b, 0.015f, 0.4f, f * 3.73f, 0.05f, 0.004f, 13f, 1);
            Sweep(b, 0f, 0.12f, 340f * v, 180f * v, 0.18f, 30f, 1);               // small body knock
            return Normalize(b, 0.78f);
        }

        /// <summary>
        /// Holy Tomes hit (KAN-123, ~0.35 s): a heavy book slapping a body. A soft low thump (falling sine), a dull leather
        /// slap (low band noise), and a faint, short holy chime on top (two soft high partials) so it reads as holy, not as a
        /// punch. Quiet by design: many hits per second in a horde.
        /// </summary>
        private static float[] TomeThump(int seed)
        {
            var rng = new System.Random(seed * 7121);
            var b = new float[(int)(Rate * 0.36f)];
            float v = seed == 1 ? 1f : 0.93f;
            Sweep(b, 0f, 0.16f, 150f * v, 70f * v, 0.7f, 22f, 2);                  // body thump
            NoiseBurst(b, 0f, 0.07f, 0.45f, 180f, 1400f, 45f, rng);                // leather slap
            Click(b, 0f, 0.003f, 0.25f, 3000f, rng);                                // cover edge
            float f = 1318.5f * v;                                                  // E6 chime, soft
            Swell(b, 0.01f, 0.32f, f, 0.07f, 0.008f, 9f, 1);
            Swell(b, 0.01f, 0.28f, f * 1.5f, 0.035f, 0.008f, 12f, 1);
            return Normalize(b, 0.72f);
        }

        /// <summary>
        /// Holy Tomes page whoosh loop (KAN-123, 2 s seamless): airy band noise that swells twice per loop (books passing),
        /// with a faint paper flutter. Whole cycles over the loop length + a cross-fade, so it loops without a seam.
        /// </summary>
        private static float[] TomeWhoosh(int seed)
        {
            var rng = new System.Random(seed * 2273);
            float seconds = 2f;
            int n = (int)(Rate * seconds), fade = Rate / 4;
            var b = new float[n];
            var air = new Biquad(); air.SetBandPass(900f, 0.7f, Rate);
            var paper = new Biquad(); paper.SetBandPass(3200f, 1.4f, Rate);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)Rate;
                float swell = 0.55f + 0.45f * Mathf.Sin(t * Mathf.PI * 2f * (2f / (seconds - 0.25f)));
                float flutter = 0.7f + 0.3f * Mathf.Sin(t * Mathf.PI * 2f * 23f);
                b[i] = air.Process(Noise(rng)) * swell * 0.8f + paper.Process(Noise(rng)) * swell * flutter * 0.12f;
            }
            for (int i = 0; i < fade; i++)
            {
                float w = i / (float)fade;
                b[i] = b[i] * w + b[n - fade + i] * (1f - w);
            }
            var loop = new float[n - fade];
            Array.Copy(b, loop, loop.Length);
            // Scale only: Normalize's end fade would dip to silence at the loop point.
            float max = 1e-6f;
            foreach (float x in loop) max = Mathf.Max(max, Mathf.Abs(x));
            for (int i = 0; i < loop.Length; i++) loop[i] *= 0.6f / max;
            return loop;
        }

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

        /// <summary>Tone with a soft linear attack (<paramref name="attack"/> s), then exponential decay. No click at the start.</summary>
        private static void Swell(float[] b, float start, float length, float f, float amp, float attack, float decay, int harmonics)
        {
            int s = (int)(start * Rate), e = Mathf.Min(b.Length, s + (int)(length * Rate));
            for (int i = s; i < e; i++)
            {
                float t = (i - s) / (float)Rate;
                float env = Mathf.Min(1f, t / Mathf.Max(0.001f, attack)) * Mathf.Exp(-Mathf.Max(0f, t - attack) * decay);
                float sum = 0f;
                for (int h = 1; h <= harmonics; h++) sum += Mathf.Sin(t * f * h * Mathf.PI * 2f) / (h * h);
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
