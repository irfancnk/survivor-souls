using System.IO;
using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Audio;
using SurvivorSouls.Editor.Audio;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-236 (KAN-81.3) shot channel clips, master limiter and mix bus: 8 crossbow shot takes (same length / peak as the
    /// KAN-231 shot, click-free, each different, body pitch spread over +-3 %), volley layers, the limiter keeps a heavy
    /// fight (10 shots + 20 hits per second) under the ceiling without crushing it, the mixer has its groups, the sound board
    /// family names, and AUDIO_CREDITS.md lists every clip family in the Sfx folder.
    /// </summary>
    public sealed class ShotChannelSfxTests
    {
        private const int Rate = SfxBuilder.ShotRate;

        private static float Peak(float[] b)
        {
            float p = 0f;
            foreach (float x in b) p = Mathf.Max(p, Mathf.Abs(x));
            return p;
        }

        [Test]
        public void ShotVariants_Eight_SameShape_ClickFree_AllDifferent()
        {
            Assert.That(SfxBuilder.ShotVariantCount, Is.EqualTo(8));
            var takes = Enumerable.Range(1, 8).Select(SfxBuilder.CrossbowShotVariant).ToArray();
            for (int v = 0; v < 8; v++)
            {
                var b = takes[v];
                string id = $"take {v + 1}";
                Assert.That(b.Length, Is.EqualTo((int)(Rate * 0.5f)), $"{id}: 0.5 s like the KAN-231 shot");
                Assert.That(Peak(b), Is.EqualTo(0.9f).Within(0.001f), $"{id}: peak");
                Assert.That(Mathf.Abs(b[b.Length - 1]), Is.LessThan(0.01f), $"{id}: no click at the end");
                Assert.That(SfxBuilder.CrossbowShotVariant(v + 1), Is.EqualTo(b), $"{id}: deterministic (same files on every rebuild)");
                for (int o = 0; o < v; o++) Assert.That(b, Is.Not.EqualTo(takes[o]), $"{id} differs from take {o + 1}");
            }
            var pitches = Enumerable.Range(1, 8).Select(SfxBuilder.ShotVariantPitch).ToArray();
            Assert.That(pitches.Distinct().Count(), Is.EqualTo(8), "8 body pitches");
            Assert.That(pitches.Min(), Is.EqualTo(0.97f).Within(1e-4f));
            Assert.That(pitches.Max(), Is.EqualTo(1.03f).Within(1e-4f));
            for (int i = 1; i < 8; i++)
                Assert.That(Mathf.Abs(pitches[i] - pitches[i - 1]), Is.GreaterThan(0.015f), "neighbouring files are not neighbouring pitches");
        }

        [Test]
        public void VolleyLayers_SoftAndDifferent()
        {
            var a = SfxBuilder.CrossbowVolleyLayer(1);
            var b = SfxBuilder.CrossbowVolleyLayer(2);
            Assert.That(Peak(a), Is.EqualTo(0.7f).Within(0.001f));
            Assert.That(a.Length, Is.EqualTo((int)(Rate * 0.4f)));
            Assert.That(a, Is.Not.EqualTo(b));
            Assert.That(Mathf.Abs(a[a.Length - 1]), Is.LessThan(0.01f));
        }

        // ------------------------------------------------------------ Limiter

        [Test]
        public void Limiter_LeavesQuietSoundAlone_AndHoldsTheCeiling()
        {
            float coef = MasterLimiter.ReleaseCoefficient(0.12f, 48000);
            var quiet = new float[4800];
            for (int i = 0; i < quiet.Length; i++) quiet[i] = 0.5f * Mathf.Sin(i * 0.05f);
            var copy = (float[])quiet.Clone();
            float gain = 1f;
            Assert.That(MasterLimiter.Process(quiet, 2, 0.89f, coef, ref gain), Is.EqualTo(1f));
            Assert.That(quiet, Is.EqualTo(copy), "under the ceiling: untouched");

            var loud = new float[48000]; // 0.5 s stereo, +6 dB over full scale
            for (int i = 0; i < loud.Length; i++) loud[i] = 2f * Mathf.Sin(i * 0.05f);
            float low = MasterLimiter.Process(loud, 2, 0.89f, coef, ref gain);
            Assert.That(Peak(loud), Is.LessThanOrEqualTo(0.89f + 1e-5f));
            Assert.That(low, Is.LessThan(0.5f));

            var after = new float[48000]; // silence: the gain comes back
            MasterLimiter.Process(after, 2, 0.89f, coef, ref gain);
            Assert.That(gain, Is.GreaterThan(0.99f), "released after 0.5 s");
        }

        /// <summary>
        /// 3 s of a heavy fight, mono, at the game's levels: 10 shots per second (shot 0.8 +-8 %, never more than 3 at
        /// once), 20 bolt impacts (0.75) and 20 hit ticks (0.32) per second at random times. The plain sum clips; through the
        /// limiter no sample is over the ceiling and the gain never drops below -12 dB (no pumping to silence).
        /// </summary>
        [Test]
        public void HeavyFight_TenShotsTwentyHitsPerSecond_NoClipping()
        {
            var rng = new System.Random(236);
            var shots = Enumerable.Range(1, 8).Select(SfxBuilder.CrossbowShotVariant).ToArray();
            var impacts = Enumerable.Range(1, 3).Select(SfxBuilder.ImpactSamples).ToArray();
            var ticks = Enumerable.Range(1, 2).Select(SfxBuilder.HitMarkerSamples).ToArray();
            int length = Rate * 3;
            var mix = new float[length];
            void Add(float[] clip, int at, float vol, int max = int.MaxValue)
            {
                for (int i = 0; i < clip.Length && i < max && at + i < length; i++) mix[at + i] += clip[i] * vol;
            }
            for (int s = 0; s < 30; s++) // the 4th shot cuts the oldest voice: each shot sounds for 3 shot steps at most
                Add(shots[s % 8], s * Rate / 10, 0.8f * (1f + (float)(rng.NextDouble() * 0.16 - 0.08)), 3 * Rate / 10);
            for (int h = 0; h < 60; h++)
            {
                int at = rng.Next(length);
                Add(impacts[h % 3], at, 0.75f);
                Add(ticks[h % 2], at, 0.32f);
            }
            float raw = Peak(mix);
            Assert.That(raw, Is.GreaterThan(0.89f), $"the plain mix is over the ceiling (peak {raw:0.00}): the test means something");

            float gain = 1f;
            float low = MasterLimiter.Process(mix, 1, 0.89f, MasterLimiter.ReleaseCoefficient(0.12f, Rate), ref gain);
            Assert.That(Peak(mix), Is.LessThanOrEqualTo(0.89f + 1e-5f), "no clipping");
            Assert.That(20f * Mathf.Log10(low), Is.GreaterThan(-12f), $"limiting, not crushing (lowest gain {low:0.00})");
        }

        // ------------------------------------------------------------ Mixer, board, credits

        [Test]
        public void Mixer_HasTheGroups()
        {
            var g = AudioMixerBuilder.Ensure();
            Assert.That(g.Mixer, Is.Not.Null, AudioMixerBuilder.MixerPath);
            Assert.That(g.Master, Is.Not.Null);
            Assert.That(g.Music, Is.Not.Null);
            var sfx = g.Mixer.FindMatchingGroups("Master/SFX").Select(x => x.name).ToArray();
            Assert.That(sfx, Does.Contain("SFX").And.Contain("Shots").And.Contain("Footsteps").And.Contain("Deaths"), string.Join(", ", sfx));
            Assert.That(sfx, Does.Not.Contain("Music"), "Music sits beside SFX");
        }

        [Test]
        public void SoundBoard_FamilyNames()
        {
            Assert.That(SoundBoardWindow.FamilyOf("sfx_zombie_death_03"), Is.EqualTo("sfx_zombie_death"));
            Assert.That(SoundBoardWindow.FamilyOf("sfx_crossbow_shot_var_08"), Is.EqualTo("sfx_crossbow_shot_var"));
            Assert.That(SoundBoardWindow.FamilyOf("amb_shore_wind_surf_loop"), Is.EqualTo("amb_shore_wind_surf_loop"));
        }

        [Test]
        public void AudioCredits_ListEveryClipFamily()
        {
            string credits = File.ReadAllText("Docs/AUDIO_CREDITS.md");
            var families = AssetDatabase.FindAssets("t:AudioClip", new[] { SfxBuilder.Dir })
                .Select(guid => SoundBoardWindow.FamilyOf(Path.GetFileNameWithoutExtension(AssetDatabase.GUIDToAssetPath(guid))))
                .Distinct().ToArray();
            Assert.That(families.Length, Is.GreaterThan(40), "the Sfx folder is built");
            var missing = families.Where(f => !credits.Contains("`" + f + "`")).ToArray();
            Assert.That(missing, Is.Empty, "add these families to Docs/AUDIO_CREDITS.md: " + string.Join(", ", missing));
        }
    }
}
