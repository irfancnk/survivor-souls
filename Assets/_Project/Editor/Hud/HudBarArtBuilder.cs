using System;
using SurvivorSouls.Editor.Procedural;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Hud
{
    /// <summary>
    /// Medieval HUD bar art (KAN-35): forged iron rim with a gold inlay and rivets, dark glass track, molten-gold and
    /// crimson fills, a scrolling rune strip, a shine band, the heraldic level shield and a spark. All procedural PNGs
    /// (no outside art), small and uncompressed (crisp 9-slice edges), referenced by GameHud.uss (<c>.ss-bar*</c>,
    /// <c>.hud-xp__badge</c>). The PNGs are committed; run this again only after a change here:
    /// menu "Survivor Souls/Rebuild HUD Bar Art" or
    /// <c>-executeMethod SurvivorSouls.Editor.Hud.HudBarArtBuilder.BuildFromCommandLine</c>.
    /// Rim and glass share one rounded-rect shape: keep <see cref="BarHeight"/>, <see cref="BarRadius"/> and the USS
    /// <c>-unity-slice-*</c> values (left/right 26, top/bottom 18) in sync.
    /// </summary>
    public static class HudBarArtBuilder
    {
        public const string Dir = "Assets/_Project/Art/UI/HudBars";
        public const string RimPath = Dir + "/T_UI_BarRim.png";
        public const string GlassPath = Dir + "/T_UI_BarGlass.png";
        public const string FillGoldPath = Dir + "/T_UI_BarFillGold.png";
        public const string FillCrimsonPath = Dir + "/T_UI_BarFillCrimson.png";
        public const string RunesPath = Dir + "/T_UI_BarRunes.png";
        public const string ShinePath = Dir + "/T_UI_BarShine.png";
        public const string ShieldPath = Dir + "/T_UI_LevelShield.png";
        public const string SparkPath = Dir + "/T_UI_BarSpark.png";

        /// <summary>Every generated texture (tests check they exist and stay small).</summary>
        public static readonly string[] All =
            { RimPath, GlassPath, FillGoldPath, FillCrimsonPath, RunesPath, ShinePath, ShieldPath, SparkPath };

        public const int BarHeight = 40;
        public const float BarRadius = 16f;
        /// <summary>Iron band width (px). The USS clip inset (8 px) sits on its inner edge.</summary>
        public const float RimWidth = 8f;

        private static readonly Color Outline = new Color(0.035f, 0.03f, 0.03f);
        private static readonly Color Iron = new Color(0.25f, 0.24f, 0.24f);
        private static readonly Color Gold = new Color(0.88f, 0.68f, 0.31f);
        private static readonly Color Brass = new Color(0.8f, 0.62f, 0.33f);

        [MenuItem("Survivor Souls/Rebuild HUD Bar Art")]
        public static void Build()
        {
            WriteRim();
            WriteGlass();
            WriteFill(FillGoldPath, new[]
            {
                (0f, new Color(0.42f, 0.2f, 0.03f)), (0.35f, new Color(0.8f, 0.5f, 0.1f)),
                (0.62f, new Color(0.98f, 0.76f, 0.28f)), (0.74f, new Color(1f, 0.93f, 0.66f)),
                (0.82f, new Color(0.95f, 0.74f, 0.3f)), (1f, new Color(0.7f, 0.45f, 0.1f)),
            });
            WriteFill(FillCrimsonPath, new[]
            {
                (0f, new Color(0.22f, 0.02f, 0.02f)), (0.35f, new Color(0.52f, 0.05f, 0.04f)),
                (0.62f, new Color(0.78f, 0.12f, 0.08f)), (0.74f, new Color(0.98f, 0.45f, 0.36f)),
                (0.82f, new Color(0.8f, 0.14f, 0.1f)), (1f, new Color(0.48f, 0.05f, 0.04f)),
            });
            WriteRunes();
            WriteShine();
            WriteShield();
            WriteSpark();
            AssetDatabase.SaveAssets();
            Debug.Log($"[HudBarArt] wrote {All.Length} textures to {Dir}");
        }

        /// <summary>Batch entry point.</summary>
        public static void BuildFromCommandLine()
        {
            try
            {
                Build();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        // ------------------------------------------------------------ Bar rim + glass (9-slice)

        /// <summary>Iron band with a bevel lit from above, a thin gold inlay line, an inner shadow lip and brass rivets on
        /// both end caps. Centre transparent: the fill shows through.</summary>
        private static void WriteRim()
        {
            const int w = 192, h = BarHeight;
            Write(RimPath, w, h, (x, y) =>
            {
                float px = x + 0.5f, py = y + 0.5f;
                float d = RoundRect(px, py, w, h, BarRadius);
                float alpha = Mathf.Clamp01(0.5f - d) * Mathf.Clamp01(d + RimWidth + 0.5f);
                if (alpha <= 0f) return Clear;

                float depth = -d; // 0 at the outer edge, RimWidth at the inner edge
                float lit = Mathf.Lerp(0.62f, 1.4f, py / h); // light from above
                float ridge = 1f + 0.3f * (1f - Mathf.Abs(2f * Mathf.Clamp01((depth - 1.5f) / (RimWidth - 2.5f)) - 1f));
                float forged = 0.86f + 0.28f * TextureFactory.Fbm(px / w, py / h, 8, 3, 35);
                Color c = Iron * (lit * ridge * forged);
                c = Color.Lerp(c, Gold * Mathf.Lerp(0.75f, 1.2f, py / h), 0.95f * Gauss(depth, 4.4f, 0.7f));
                if (depth < 1.5f) c = Color.Lerp(Outline, c, Mathf.Clamp01(depth - 0.5f));
                if (depth > RimWidth - 1.4f) c = Color.Lerp(c, Outline, Mathf.Clamp01((depth - (RimWidth - 1.4f)) / 0.8f));

                // Brass rivets on both caps.
                c = Rivet(c, px, py, 5.5f, h * 0.5f, 2.7f, ref alpha);
                c = Rivet(c, px, py, w - 5.5f, h * 0.5f, 2.7f, ref alpha);
                c.a = alpha;
                return c;
            });
        }

        /// <summary>Dark glass inside the rim: deep near the top (inner shadow), a faint lighter reflection low down.</summary>
        private static void WriteGlass()
        {
            const int w = 64, h = BarHeight;
            Write(GlassPath, w, h, (x, y) =>
            {
                float px = x + 0.5f, py = y + 0.5f;
                float d = RoundRect(px, py, w, h, BarRadius);
                float alpha = Mathf.Clamp01(0.5f - d);
                if (alpha <= 0f) return Clear;
                float v = py / h;
                float shade = 0.05f + 0.03f * Gauss(v, 0.28f, 0.1f) - 0.025f * Mathf.Clamp01((v - 0.6f) / 0.3f);
                var c = new Color(shade, shade * 0.92f, shade * 1.05f, alpha * 0.9f);
                return c;
            });
        }

        // ------------------------------------------------------------ Fills, runes, shine, spark

        /// <summary>Vertical gradient (bottom to top stops) with a glossy highlight line; stretched to the fill.</summary>
        private static void WriteFill(string path, (float at, Color color)[] stops)
        {
            Write(path, 4, 32, (x, y) =>
            {
                float v = (y + 0.5f) / 32f;
                for (int i = 1; i < stops.Length; i++)
                {
                    if (v > stops[i].at) continue;
                    float k = Mathf.InverseLerp(stops[i - 1].at, stops[i].at, v);
                    return Color.Lerp(stops[i - 1].color, stops[i].color, k);
                }
                return stops[stops.Length - 1].color;
            });
        }

        /// <summary>White rune strip (eight futhark-like glyphs per tile, tiles in x). Tinted faint gold in USS.</summary>
        private static void WriteRunes()
        {
            const int w = 256, h = 32, cell = 32;
            // Glyph strokes in cell space (0..32, y up). Each glyph is the stave plus a hash-picked set of branches.
            var branches = new[]
            {
                new Vector4(16, 26, 24, 20), new Vector4(16, 20, 24, 14), new Vector4(16, 26, 8, 20),
                new Vector4(16, 20, 8, 14), new Vector4(16, 14, 24, 8), new Vector4(9, 22, 23, 10),
                new Vector4(23, 22, 9, 10), new Vector4(16, 17, 23, 17),
            };
            Write(RunesPath, w, h, (x, y) =>
            {
                int glyph = x / cell;
                float lx = x % cell + 0.5f, ly = y + 0.5f;
                float dist = Segment(lx, ly, 16, 6, 16, 26);
                int n = branches.Length;
                int b1 = (int)(TextureFactory.Hash(glyph, 7, 35) * n);
                int b2 = (b1 + 1 + (int)(TextureFactory.Hash(glyph, 11, 35) * (n - 1))) % n;
                dist = Mathf.Min(dist, Branch(branches[b1], lx, ly));
                dist = Mathf.Min(dist, Branch(branches[b2], lx, ly));
                if (TextureFactory.Hash(glyph, 13, 35) > 0.6f)
                    dist = Mathf.Min(dist, Branch(branches[(b2 + 3) % n], lx, ly));
                float a = Mathf.Clamp01(1.7f - dist);
                return new Color(1f, 1f, 1f, a);
            });
        }

        /// <summary>Soft slanted light band (the shine sweep).</summary>
        private static void WriteShine()
        {
            const int w = 64, h = 32;
            Write(ShinePath, w, h, (x, y) =>
            {
                float u = (x + 0.5f) / w, v = (y + 0.5f) / h;
                float a = 0.85f * Gauss(u - (v - 0.5f) * 0.35f, 0.5f, 0.15f);
                return new Color(1f, 0.97f, 0.88f, a);
            });
        }

        /// <summary>Warm four-point spark with a soft core: rides the XP fill tip and flies out as level-up motes.</summary>
        private static void WriteSpark()
        {
            const int s = 48;
            Write(SparkPath, s, s, (x, y) =>
            {
                float u = (x + 0.5f) / s * 2f - 1f, v = (y + 0.5f) / s * 2f - 1f;
                float r = Mathf.Sqrt(u * u + v * v);
                float core = Mathf.Exp(-r * r / (2f * 0.12f * 0.12f));
                float glow = 0.45f * Mathf.Exp(-r * r / (2f * 0.35f * 0.35f));
                float star = Mathf.Exp(-Mathf.Abs(u) / 0.035f) * Mathf.Exp(-Mathf.Abs(v) / 0.5f)
                           + Mathf.Exp(-Mathf.Abs(v) / 0.035f) * Mathf.Exp(-Mathf.Abs(u) / 0.5f);
                float a = Mathf.Clamp01(core + glow + 0.9f * star) * Mathf.Clamp01((1f - r) * 4f);
                return new Color(1f, 0.93f, 0.72f, a);
            });
        }

        // ------------------------------------------------------------ Level shield

        /// <summary>Heater shield (2x the 96 x 110 px badge): dark outline, bevelled iron rim with a gold inlay, oxblood field
        /// with a soft centre glow, brass rivets in the top corners. The level number is a Label on top.</summary>
        private static void WriteShield()
        {
            const int w = 192, h = 220;
            Write(ShieldPath, w, h, (x, y) =>
            {
                float px = x + 0.5f, py = y + 0.5f;
                float d = ShieldDistance(px, py, w, h); // px inside (positive = inside)
                float alpha = Mathf.Clamp01(d + 0.5f);
                if (alpha <= 0f) return Clear;
                float u = px / w * 2f - 1f, s = 1f - py / h; // s = 0 at the top

                Color c;
                const float outline = 3f, rim = 16f;
                if (d < rim)
                {
                    float lit = 1.2f - 0.45f * s - 0.18f * u; // light from the top left
                    float forged = 0.85f + 0.3f * TextureFactory.Fbm(px / w, py / h, 6, 3, 36);
                    c = Iron * (lit * forged * (1f + 0.25f * Gauss(d, (outline + rim) * 0.5f, 4f)));
                    c = Color.Lerp(c, Gold * (1.15f - 0.35f * s), 0.95f * Gauss(d, 9.5f, 1.3f));
                    if (d < outline) c = Color.Lerp(Outline, c, Mathf.Clamp01(d - outline + 1f));
                    if (d > rim - 2.5f) c = Color.Lerp(c, Outline, Mathf.Clamp01((d - (rim - 2.5f)) / 1.5f));
                }
                else
                {
                    float r = Mathf.Sqrt(u * u + (s - 0.42f) * (s - 0.42f) * 1.4f);
                    float glow = Mathf.Lerp(1.35f, 0.7f, Mathf.Clamp01(r));
                    float grain = 0.9f + 0.2f * TextureFactory.Fbm(px / w, py / h, 10, 3, 37);
                    c = new Color(0.2f, 0.055f, 0.05f) * (glow * grain);
                    c = Color.Lerp(c, Outline, 0.6f * Mathf.Clamp01(1f - (d - rim) / 6f)); // inner shadow under the rim
                }
                c = Rivet(c, px, py, w * 0.5f - 0.7f * w * 0.5f, h - 0.085f * h, 4.5f, ref alpha);
                c = Rivet(c, px, py, w * 0.5f + 0.7f * w * 0.5f, h - 0.085f * h, 4.5f, ref alpha);
                c.a = alpha;
                return c;
            });
        }

        /// <summary>Approximate signed distance (px, positive inside) to a heater shield: flat top, straight sides to 42 %,
        /// then a curved taper to the point.</summary>
        private static float ShieldDistance(float px, float py, float w, float h)
        {
            const float top = 3f;
            float halfW = w * 0.5f - 2f;
            float s = (h - py) / h;
            float u = Mathf.Abs(px - w * 0.5f);
            float hw = HalfWidth(s), slope = (HalfWidth(s + 0.002f) - HalfWidth(s - 0.002f)) / 0.004f;
            float side = (hw * halfW - u) / Mathf.Sqrt(1f + slope * halfW / h * (slope * halfW / h));
            float topDist = (h - py) - top;
            return Mathf.Min(side, topDist);
        }

        private static float HalfWidth(float s)
        {
            if (s <= 0.42f) return 1f;
            float k = Mathf.Clamp01((s - 0.42f) / 0.56f);
            return Mathf.Pow(Mathf.Max(0f, Mathf.Cos(k * Mathf.PI * 0.5f)), 0.75f); // cos(pi/2) < 0 in float: NaN guard
        }

        // ------------------------------------------------------------ Helpers

        private static readonly Color Clear = new Color(0f, 0f, 0f, 0f);

        /// <summary>Signed distance (px, negative inside) to a w x h rounded rectangle with corner radius r.</summary>
        private static float RoundRect(float px, float py, float w, float h, float r)
        {
            float qx = Mathf.Abs(px - w * 0.5f) - (w * 0.5f - r);
            float qy = Mathf.Abs(py - h * 0.5f) - (h * 0.5f - r);
            float outside = new Vector2(Mathf.Max(qx, 0f), Mathf.Max(qy, 0f)).magnitude;
            return outside + Mathf.Min(Mathf.Max(qx, qy), 0f) - r;
        }

        private static float Segment(float px, float py, float ax, float ay, float bx, float by)
        {
            var p = new Vector2(px - ax, py - ay);
            var ab = new Vector2(bx - ax, by - ay);
            float t = Mathf.Clamp01(Vector2.Dot(p, ab) / ab.sqrMagnitude);
            return (p - ab * t).magnitude;
        }

        private static float Branch(Vector4 s, float px, float py) => Segment(px, py, s.x, s.y, s.z, s.w);

        private static float Gauss(float x, float mean, float sigma)
        {
            float k = (x - mean) / sigma;
            return Mathf.Exp(-k * k);
        }

        /// <summary>Domed brass rivet with a dark rim, lit from the top left.</summary>
        private static Color Rivet(Color under, float px, float py, float cx, float cy, float radius, ref float alpha)
        {
            float dx = (px - cx) / radius, dy = (py - cy) / radius;
            float r = Mathf.Sqrt(dx * dx + dy * dy);
            if (r > 1.25f) return under;
            if (r > 1f) return Color.Lerp(Outline, under, (r - 1f) / 0.25f);
            float nz = Mathf.Sqrt(1f - r * r);
            float lit = 0.45f + 0.75f * Mathf.Clamp01(-dx * 0.45f + dy * 0.55f + nz * 0.7f);
            alpha = 1f;
            return Brass * lit;
        }

        private static void Write(string path, int width, int height, Func<int, int, Color> pixel)
        {
            TextureFactory.Write(path, width, height, pixel, TextureFactory.Kind.Color, mipmaps: false,
                wrap: TextureWrapMode.Clamp, maxSize: 256, compress: false);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer.alphaIsTransparency) return;
            importer.alphaIsTransparency = true; // no dark fringe on the 9-slice edges
            importer.SaveAndReimport();
        }
    }
}
