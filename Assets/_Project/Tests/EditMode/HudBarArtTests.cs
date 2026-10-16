using System.IO;
using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Editor.Hud;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-35: the medieval HUD bar art (HudBarArtBuilder) is committed, small (ticket budget 300 KB), imported crisp
    /// for UI (no mips, uncompressed, alpha is transparency), referenced by GameHud.uss, and shaped right: the rim is
    /// opaque on its band and clear in the middle (the fill shows through), the shield has a clear outside and no
    /// broken pixels at its point. KAN-82: the heart medallion is round with a red heart; the cracks are thin lines.
    /// KAN-184: skill slot frames keep the tier shape cue with a dark field; the inlay is a thin grey line (tinted in USS).
    /// </summary>
    public sealed class HudBarArtTests
    {
        private const string UssPath = "Assets/_Project/UI/Hud/GameHud.uss";

        [Test]
        public void AllTextures_Exist_UnderBudget_ImportedForUi()
        {
            long bytes = 0;
            foreach (var path in HudBarArtBuilder.All)
            {
                Assert.That(File.Exists(path), $"missing {path}: run Survivor Souls/Rebuild HUD Bar Art");
                bytes += new FileInfo(path).Length;
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                Assert.That(importer, Is.Not.Null, path);
                Assert.That(importer.mipmapEnabled, Is.False, $"{path}: UI art needs no mips");
                Assert.That(importer.textureCompression, Is.EqualTo(TextureImporterCompression.Uncompressed),
                    $"{path}: crisp 9-slice edges");
                Assert.That(importer.alphaIsTransparency, $"{path}: no dark fringe");
            }
            Assert.That(bytes, Is.LessThan(300 * 1024), "KAN-35 art budget");
        }

        [Test]
        public void Uss_ReferencesEveryTexture()
        {
            string uss = File.ReadAllText(UssPath);
            foreach (var path in HudBarArtBuilder.All)
                Assert.That(uss, Does.Contain(Path.GetFileName(path)), $"{path} is not used by GameHud.uss");
            Assert.That(uss, Does.Contain("-unity-slice-left: 26"), "slices match HudBarArtBuilder");
            Assert.That(uss, Does.Contain("-unity-slice-top: 18"));
        }

        [Test]
        public void Rim_OpaqueBand_ClearCentre_RivetsOnCaps()
        {
            var rim = Load(HudBarArtBuilder.RimPath);
            int w = rim.width, h = rim.height;
            Assert.That(h, Is.EqualTo(HudBarArtBuilder.BarHeight));
            Assert.That(rim.GetPixel(w / 2, h / 2).a, Is.LessThan(0.01f), "centre clear: the fill shows through");
            Assert.That(rim.GetPixel(w / 2, h - 4).a, Is.GreaterThan(0.95f), "top band opaque");
            Assert.That(rim.GetPixel(w / 2, 3).a, Is.GreaterThan(0.95f), "bottom band opaque");
            Assert.That(rim.GetPixel(0, 0).a, Is.LessThan(0.05f), "rounded corner clear");
            var rivet = rim.GetPixel(5, h / 2);
            Assert.That(rivet.r, Is.GreaterThan(rivet.b + 0.1f), "brass rivet on the left cap");
            Object.DestroyImmediate(rim);
        }

        [Test]
        public void Shield_ClearOutside_SolidField_CleanPoint()
        {
            var shield = Load(HudBarArtBuilder.ShieldPath);
            int w = shield.width, h = shield.height;
            Assert.That(shield.GetPixel(w / 2, h / 2).a, Is.GreaterThan(0.99f), "field solid");
            Assert.That(shield.GetPixel(2, 2).a, Is.LessThan(0.01f), "bottom corners outside the point");
            Assert.That(shield.GetPixel(w - 3, 2).a, Is.LessThan(0.01f));
            var pixels = shield.GetPixels();
            Assert.That(pixels.All(p => !float.IsNaN(p.r) && !float.IsNaN(p.a)), "no NaN pixels");
            // Bottom row: only the tip may show, never a full-width line.
            int lit = Enumerable.Range(0, w).Count(x => shield.GetPixel(x, 0).a > 0.5f);
            Assert.That(lit, Is.LessThan(w / 8), "no stray line under the point");
            Object.DestroyImmediate(shield);
        }

        [Test]
        public void HeartMedallion_RedHeart_IronRing_ClearCorners_CracksInside()
        {
            var heart = Load(HudBarArtBuilder.HeartPath);
            int s = heart.width;
            Assert.That(s, Is.EqualTo(HudBarArtBuilder.HeartSize));
            var centre = heart.GetPixel(s / 2, s / 2);
            Assert.That(centre.a, Is.GreaterThan(0.99f), "medallion solid in the middle");
            Assert.That(centre.r, Is.GreaterThan(centre.g + 0.25f), "crimson heart in the middle");
            Assert.That(heart.GetPixel(1, 1).a, Is.LessThan(0.01f), "round: corners clear");
            Assert.That(heart.GetPixel(s / 2, s - 8).a, Is.GreaterThan(0.95f), "iron ring opaque at the top");
            Assert.That(heart.GetPixels().All(p => !float.IsNaN(p.r) && !float.IsNaN(p.a)), "no NaN pixels");

            var cracks = Load(HudBarArtBuilder.CracksPath);
            Assert.That(cracks.width, Is.EqualTo(s));
            Assert.That(cracks.GetPixel(1, 1).a, Is.LessThan(0.01f), "cracks clear outside the field");
            int lit = cracks.GetPixels().Count(p => p.a > 0.6f);
            Assert.That(lit, Is.GreaterThan(200), "crack lines drawn");
            Assert.That(lit, Is.LessThan(s * s / 4), "thin lines, not a filled disc");
            Object.DestroyImmediate(heart);
            Object.DestroyImmediate(cracks);
        }

        [Test]
        public void SkillSlots_TierShapes_DarkField_InlayInGroove()
        {
            for (int tier = 0; tier < HudBarArtBuilder.SlotFramePaths.Length; tier++)
            {
                var frame = Load(HudBarArtBuilder.SlotFramePaths[tier]);
                var inlay = Load(HudBarArtBuilder.SlotInlayPaths[tier]);
                int s = frame.width;
                Assert.That(s, Is.EqualTo(HudBarArtBuilder.SlotSize));
                Assert.That(inlay.width, Is.EqualTo(s));
                var field = frame.GetPixel(s / 2, s / 2);
                Assert.That(field.a, Is.GreaterThan(0.8f), $"tier {tier}: field mostly opaque");
                Assert.That(field.r + field.g + field.b, Is.LessThan(0.4f), $"tier {tier}: dark glass under the icon");
                Assert.That(frame.GetPixel(s / 2, s - 4).a, Is.GreaterThan(0.95f), $"tier {tier}: iron band at the top");
                float corner = frame.GetPixel(1, 1).a;
                if (tier == 0) Assert.That(corner, Is.LessThan(0.01f), "common is round: corners clear");
                else Assert.That(corner, Is.LessThan(0.5f), $"tier {tier}: rounded corner");
                if (tier == 3) Assert.That(frame.GetPixel(4, s / 2).a, Is.GreaterThan(0.95f), "legendary square edge");

                Assert.That(inlay.GetPixel(s / 2, s / 2).a, Is.LessThan(0.01f), $"tier {tier}: inlay clear in the middle");
                int groove = Enumerable.Range(0, 20).Count(y => inlay.GetPixel(s / 2, s - 1 - y).a > 0.6f);
                Assert.That(groove, Is.InRange(2, 7), $"tier {tier}: thin inlay line in the top band");
                var lit = inlay.GetPixels().Where(p => p.a > 0.6f).ToArray();
                Assert.That(lit.All(p => Mathf.Abs(p.r - p.b) < 0.01f), $"tier {tier}: inlay is grey (tinted in USS)");
                Assert.That(frame.GetPixels().All(p => !float.IsNaN(p.r) && !float.IsNaN(p.a)), "no NaN pixels");
                Object.DestroyImmediate(frame);
                Object.DestroyImmediate(inlay);
            }
        }

        private static Texture2D Load(string path)
        {
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            Assert.That(tex.LoadImage(File.ReadAllBytes(path)), path);
            return tex;
        }
    }
}
