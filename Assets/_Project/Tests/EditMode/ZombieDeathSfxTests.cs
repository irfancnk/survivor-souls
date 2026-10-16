using NUnit.Framework;
using SurvivorSouls.Editor.Audio;
using UnityEngine;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-234 (KAN-81.1) procedural zombie death clips: 8 really different voices (mixed short and long, no two alike),
    /// no clipping, no click at the start or end, body falls are short and low, the mass death is the longest.
    /// </summary>
    public sealed class ZombieDeathSfxTests
    {
        private const int Rate = 44100;

        [Test]
        public void Voices_EightDistinct_MixedLengths()
        {
            var clips = new float[SfxBuilder.ZombieVoiceCount][];
            for (int i = 0; i < clips.Length; i++) clips[i] = SfxBuilder.ZombieDeathVoice(i + 1);
            Assert.That(clips.Length, Is.GreaterThanOrEqualTo(8));
            float shortest = float.MaxValue, longest = 0f;
            for (int i = 0; i < clips.Length; i++)
            {
                float seconds = clips[i].Length / (float)Rate;
                shortest = Mathf.Min(shortest, seconds);
                longest = Mathf.Max(longest, seconds);
                Assert.That(seconds, Is.InRange(0.3f, 1.5f), "voice " + (i + 1));
                AssertClean(clips[i], "voice " + (i + 1));
                Assert.That(Rms(clips[i]), Is.GreaterThan(0.07f), "voice " + (i + 1) + " carries (not only a click peak)");
                for (int j = 0; j < i; j++)
                    Assert.That(Similarity(clips[i], clips[j]), Is.LessThan(0.5f), $"voices {j + 1} and {i + 1} too alike");
            }
            Assert.That(shortest, Is.LessThan(0.5f), "some short");
            Assert.That(longest, Is.GreaterThan(1f), "some long");
        }

        [Test]
        public void FallsAndMass_Shape()
        {
            for (int i = 1; i <= SfxBuilder.ZombieFallCount; i++)
            {
                var fall = SfxBuilder.ZombieBodyFall(i);
                Assert.That(fall.Length / (float)Rate, Is.LessThanOrEqualTo(0.45f), "fall " + i + " short");
                AssertClean(fall, "fall " + i);
                Assert.That(ZeroCrossingRate(fall), Is.LessThan(1500f), "fall " + i + " low");
            }
            for (int i = 1; i <= SfxBuilder.ZombieMassCount; i++)
            {
                var mass = SfxBuilder.ZombieMassDeath(i);
                Assert.That(mass.Length / (float)Rate, Is.GreaterThan(1.2f), "mass " + i);
                AssertClean(mass, "mass " + i);
            }
            Assert.That(SfxBuilder.ZombieDeathVoice(3), Is.EqualTo(SfxBuilder.ZombieDeathVoice(3)), "deterministic");
        }

        private static void AssertClean(float[] b, string label)
        {
            float peak = 0f;
            foreach (float x in b) peak = Mathf.Max(peak, Mathf.Abs(x));
            Assert.That(peak, Is.InRange(0.5f, 0.95f), label + " peak");
            Assert.That(Mathf.Abs(b[0]), Is.LessThan(0.01f), label + " starts silent");
            Assert.That(Mathf.Abs(b[b.Length - 1]), Is.LessThan(0.01f), label + " ends silent");
        }

        private static float Rms(float[] b)
        {
            double sum = 0;
            foreach (float x in b) sum += x * x;
            return (float)System.Math.Sqrt(sum / b.Length);
        }

        private static float ZeroCrossingRate(float[] b)
        {
            int n = 0;
            for (int i = 1; i < b.Length; i++)
                if ((b[i] >= 0f) != (b[i - 1] >= 0f)) n++;
            return n / (b.Length / (float)Rate) / 2f;
        }

        /// <summary>|Correlation| of the raw samples over the shorter length: a re-seeded copy of one design stays high, other designs near 0.</summary>
        private static float Similarity(float[] a, float[] b)
        {
            int n = Mathf.Min(a.Length, b.Length);
            double ab = 0, aa = 0, bb = 0;
            for (int i = 0; i < n; i++) { ab += a[i] * b[i]; aa += a[i] * a[i]; bb += b[i] * b[i]; }
            return (float)System.Math.Abs(ab / System.Math.Sqrt(aa * bb + 1e-12));
        }
    }
}
