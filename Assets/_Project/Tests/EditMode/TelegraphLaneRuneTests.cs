using NUnit.Framework;
using SurvivorSouls.Editor.Boss;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-204: the lane fill (TelegraphLane shader, shipped material) draws arrow-head runes that point to the far end,
    /// and its embers follow the tint hue: a violet lane glows violet, a red lane keeps the KAN-167 orange embers. The
    /// fill is rendered on the GPU with <see cref="TelegraphLanePreview.Render"/>, flow stopped (_FlowSpeed 0) so the
    /// runes sit still: rune tips at (k + 0.8) x spacing m from the attacker. Rune pixels = the difference between a
    /// render with runes and one with the runes pushed far past the lane end (huge spacing).
    /// </summary>
    public sealed class TelegraphLaneRuneTests
    {
        private const int Ppm = 64;
        private const float Span = 6f, Width = 2f, Progress = 0.9f;
        private Material _mat;

        [SetUp]
        public void SetUp()
        {
            var shipped = AssetDatabase.LoadAssetAtPath<Material>(TelegraphLanePreview.FillMaterialPath);
            Assert.That(shipped, Is.Not.Null, "lane fill material");
            Assert.That(shipped.shader.name, Is.EqualTo("SurvivorSouls/TelegraphLane"));
            _mat = new Material(shipped);
            _mat.SetFloat("_FlowSpeed", 0f);
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_mat);

        /// <summary>Rune core strength per pixel (brightness the runes add) + the rendered colour with runes.</summary>
        private (float[] glow, Color[] colour, int w, int h) RuneGlow(Color tint)
        {
            float spacing = _mat.GetFloat("_ChevronSpacing");
            var with = TelegraphLanePreview.Render(_mat, tint, Span, Width, Progress, 0f, Ppm);
            _mat.SetFloat("_ChevronSpacing", 1000f); // first rune tip at 800 m: none on the lane
            var without = TelegraphLanePreview.Render(_mat, tint, Span, Width, Progress, 0f, Ppm);
            _mat.SetFloat("_ChevronSpacing", spacing);
            var a = with.GetPixels();
            var b = without.GetPixels();
            var glow = new float[a.Length];
            for (int i = 0; i < a.Length; i++)
                glow[i] = a[i].maxColorComponent - b[i].maxColorComponent;
            int w = with.width, h = with.height;
            Object.DestroyImmediate(with);
            Object.DestroyImmediate(without);
            return (glow, a, w, h);
        }

        private static float At(float[] g, int w, int h, float acrossM, float alongM)
        {
            int x = Mathf.Clamp(Mathf.RoundToInt((acrossM + Width * 0.5f) * Ppm), 0, w - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt(alongM * Ppm), 0, h - 1);
            return g[y * w + x];
        }

        [Test]
        public void Runes_AreArrowHeads_TipOnTheCentreLine_ArmsTrailTowardsTheAttacker()
        {
            var (glow, _, w, h) = RuneGlow(TelegraphLanePreview.RedFill);
            float spacing = _mat.GetFloat("_ChevronSpacing"), slope = _mat.GetFloat("_RuneSlope");
            float reach = Width * 0.5f * _mat.GetFloat("_RuneReach");
            for (int k = 1; k <= 3; k++)
            {
                float tip = (k + 0.8f) * spacing;
                if (tip > Span - 0.4f) break;
                float across = reach * 0.6f, armAlong = tip - across * slope;
                Assert.That(At(glow, w, h, 0f, tip - 0.02f), Is.GreaterThan(0.08f), $"rune {k}: tip glows on the centre line");
                Assert.That(At(glow, w, h, across, armAlong), Is.GreaterThan(0.08f), $"rune {k}: arm trails back towards the attacker");
                Assert.That(At(glow, w, h, -across, armAlong), Is.GreaterThan(0.08f), $"rune {k}: both arms (symmetric)");
                Assert.That(At(glow, w, h, across, tip), Is.LessThan(0.02f), $"rune {k}: nothing beside the tip (arms do not point forward)");
                Assert.That(At(glow, w, h, 0f, tip - reach * slope), Is.GreaterThan(0.08f), $"rune {k}: stem behind the tip");
            }
        }

        [Test]
        public void VioletLane_GlowsViolet_RedLaneKeepsOrangeEmbers()
        {
            var violet = MeanRuneColour(TelegraphLanePreview.VioletFill);
            Assert.That(violet.b, Is.GreaterThan(violet.g + 0.1f), $"violet runes: blue over green ({violet})");
            Assert.That(violet.r, Is.GreaterThan(violet.g), $"violet runes: red over green ({violet})");

            var green = MeanRuneColour(TelegraphLanePreview.GreenFill);
            Assert.That(green.g, Is.GreaterThan(Mathf.Max(green.r, green.b) + 0.1f), $"green runes ({green})");

            var red = MeanRuneColour(TelegraphLanePreview.RedFill);
            Assert.That(red.r, Is.GreaterThan(red.g + 0.15f), $"red lane: orange embers, red first ({red})");
            Assert.That(red.g, Is.GreaterThan(red.b), $"red lane: orange embers, green over blue ({red})");
        }

        /// <summary>Mean un-premultiplied colour of the clearly glowing rune pixels.</summary>
        private Color MeanRuneColour(Color tint)
        {
            var (glow, colour, _, _) = RuneGlow(tint);
            Color sum = Color.clear;
            int n = 0;
            for (int i = 0; i < glow.Length; i++)
            {
                if (glow[i] < 0.15f || colour[i].a < 0.2f) continue;
                var c = colour[i];
                sum += new Color(c.r / c.a, c.g / c.a, c.b / c.a, 1f);
                n++;
            }
            Assert.That(n, Is.GreaterThan(20), "rune pixels found");
            return sum / n;
        }
    }
}
