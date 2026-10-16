using NUnit.Framework;
using SurvivorSouls.Editor.Boss;
using SurvivorSouls.Gameplay.Boss;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-136: the rune-circle masks. The danger edge (bold outer rim + dark backing) ends at the quad edge = the hit
    /// radius; the glyphs sit only in their band between the rims; the wave band sits on the rim share the wave scale uses;
    /// the fill has cracks and plain ground; the two shipped materials use the TelegraphCircle shader with their mask.
    /// </summary>
    public sealed class TelegraphArtTests
    {
        private const int Size = TelegraphArt.TextureSize;

        /// <summary>Mask pixel on the +X axis at radius share <paramref name="r"/> (rotated by <paramref name="turn"/>).</summary>
        private static Color Ring(float r, float turn = 0f)
        {
            float a = turn * Mathf.PI * 2f;
            int x = Mathf.Clamp(Mathf.RoundToInt((Mathf.Cos(a) * r + 1f) * 0.5f * Size - 0.5f), 0, Size - 1);
            int y = Mathf.Clamp(Mathf.RoundToInt((Mathf.Sin(a) * r + 1f) * 0.5f * Size - 0.5f), 0, Size - 1);
            return TelegraphArt.RingPixel(x, y);
        }

        [Test]
        public void OuterRim_IsBoldAndBacked_NothingPastTheRadius()
        {
            for (int i = 0; i < 16; i++)
            {
                float turn = i / 16f + 0.013f; // off the notch spokes
                Color rim = Ring(TelegraphArt.OuterRim, turn);
                Assert.That(rim.r, Is.GreaterThan(0.9f), $"bold outer rim at turn {turn}");
                Assert.That(rim.a, Is.GreaterThan(0.9f), "dark backing behind it (reads on snow)");
                Color past = Ring(1.0f, turn);
                Assert.That(past.r + past.g + past.a, Is.LessThan(0.05f), "nothing past the hit radius");
            }
        }

        [Test]
        public void Glyphs_OnlyInTheirBand_AndEveryGlyphHasInk()
        {
            for (int y = 0; y < Size; y += 2)
            for (int x = 0; x < Size; x += 2)
            {
                float u = (x + 0.5f) / Size * 2f - 1f, v = (y + 0.5f) / Size * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                if (Mathf.Abs(r - TelegraphArt.BandCentre) > TelegraphArt.BandHalf * 1.3f)
                    Assert.That(TelegraphArt.RingPixel(x, y).g, Is.LessThan(0.02f), $"glyph ink outside the band at r {r:0.000}");
            }
            for (int g = 0; g < TelegraphArt.GlyphCount; g++)
            {
                float ink = 0f;
                float centre = (g + 0.5f) / TelegraphArt.GlyphCount;
                for (int s = -6; s <= 6; s++)
                for (int k = -4; k <= 4; k++)
                    ink += Ring(TelegraphArt.BandCentre + k * TelegraphArt.BandHalf / 4f, centre + s * 0.002f).g;
                Assert.That(ink, Is.GreaterThan(4f), $"glyph {g} has strokes");
            }
        }

        [Test]
        public void WaveBand_SitsOnTheRimShare()
        {
            int on = 0;
            for (int i = 0; i < 64; i++)
            {
                float turn = i / 64f;
                if (Ring(GroundTelegraph.RimShare, turn).b > 0.4f) on++;
                Assert.That(Ring(0.6f, turn).b, Is.EqualTo(0f), "no wave inside");
            }
            Assert.That(on, Is.GreaterThan(48), "band round most of the circle (a few shattered gaps)");
        }

        [Test]
        public void Fill_HasCracksAndPlainGround()
        {
            int cracks = 0, plain = 0;
            for (int y = 0; y < Size; y += 4)
            for (int x = 0; x < Size; x += 4)
            {
                float c = TelegraphArt.DiscPixel(x, y).r;
                if (c > 0.6f) cracks++;
                else if (c < 0.1f) plain++;
            }
            int total = (Size / 4) * (Size / 4);
            Assert.That(cracks, Is.GreaterThan(total / 20), "visible cracks");
            Assert.That(plain, Is.GreaterThan(total / 2), "mostly plain scorched ground");
        }

        [TestCase("Assets/_Project/Art/Boss/M_FX_TelegraphRing.mat", 1f)]
        [TestCase("Assets/_Project/Art/Boss/M_FX_TelegraphDisc.mat", 0f)]
        public void ShippedMaterials_UseTheRuneShader(string path, float part)
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            Assert.That(mat, Is.Not.Null, path);
            Assert.That(mat.shader.name, Is.EqualTo(TelegraphArt.Shader));
            Assert.That(mat.GetFloat("_Part"), Is.EqualTo(part));
            Assert.That(mat.GetTexture("_MainTex"), Is.Not.Null, "mask wired");
        }
    }
}
