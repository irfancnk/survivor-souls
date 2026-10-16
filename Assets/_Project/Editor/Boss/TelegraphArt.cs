using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Gameplay.Boss;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Boss
{
    /// <summary>
    /// KAN-136: the rune-circle look of every circle attack warning (<see cref="GroundTelegraph"/>: boss slams, Brute slam,
    /// Bloater swell, Shaman orbs, Pyre landing, Butcher spin). Paints the two linear mask textures the
    /// <c>SurvivorSouls/TelegraphCircle</c> shader reads and makes its two materials. Same asset paths and GUIDs as the old
    /// flat ring + disc, so the scene keeps its references.
    /// Ring mask (<see cref="RingPixel"/>): R = bold outer rim, thin inner rim, sundial notches; G = band of occult glyphs
    /// between the rims (the shader turns it); B = shattered-earth wave band at <see cref="GroundTelegraph.RimShare"/>
    /// (0.5 = earth chunk, 1 = glowing crack); A = dark backing round the lines (reads on white snow).
    /// Fill mask (<see cref="DiscPixel"/>): R = ground cracks (cells + a few cracks out from the centre), G = scorch blotches.
    /// Abstract glyphs, no real-language letters (ticket default).
    /// </summary>
    public static class TelegraphArt
    {
        public const string Shader = "SurvivorSouls/TelegraphCircle";
        public const int TextureSize = 256;
        /// <summary>Centre of the bold outer rim (share of the quad's half size); its outer edge sits at ~0.98 = the radius.</summary>
        public const float OuterRim = 0.94f;
        /// <summary>Inner rim of the glyph band.</summary>
        public const float InnerRim = 0.77f;
        /// <summary>Glyph band centre and half height.</summary>
        public const float BandCentre = 0.84f, BandHalf = 0.05f;
        /// <summary>Glyphs round the band.</summary>
        public const int GlyphCount = 14;
        private const int NotchCount = 24;
        private const int Seed = 136;

        /// <summary>Writes the ring mask + material and the fill mask + material (rebuilt every scene build).</summary>
        public static (Material ring, Material fill) CreateMaterials(string ringTexturePath, string discTexturePath,
            string ringMaterialPath, string fillMaterialPath)
        {
            var ringTex = TextureFactory.Write(ringTexturePath, TextureSize, TextureSize, RingPixel, TextureFactory.Kind.Linear,
                wrap: TextureWrapMode.Clamp, maxSize: TextureSize);
            var discTex = TextureFactory.Write(discTexturePath, TextureSize, TextureSize, DiscPixel, TextureFactory.Kind.Linear,
                wrap: TextureWrapMode.Clamp, maxSize: TextureSize);
            return (MakeMaterial(ringMaterialPath, ringTex, part: 1f), MakeMaterial(fillMaterialPath, discTex, part: 0f));
        }

        private static Material MakeMaterial(string path, Texture2D mask, float part)
        {
            var mat = EditorAssetUtil.LoadOrCreateMaterial(path, Shader);
            mat.shaderKeywords = new string[0]; // drop the old Particles/Unlit keywords
            if (mask == null) throw new System.InvalidOperationException("TelegraphArt: mask texture missing for " + path);
            mat.SetTexture("_MainTex", mask);
            mat.SetFloat("_Part", part);
            mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        // ------------------------------------------------------------ Ring mask

        /// <summary>Ring mask pixel (public for the EditMode test).</summary>
        public static Color RingPixel(int x, int y)
        {
            Vector2 p = Point(x, y);
            float r = p.magnitude;
            float turn = Turn(p); // 0..1 round the circle

            // R: rims + notches.
            float outer = 1f - TextureFactory.SmoothStep(0.018f, 0.03f, Mathf.Abs(r - OuterRim));
            float inner = 1f - TextureFactory.SmoothStep(0.006f, 0.013f, Mathf.Abs(r - InnerRim));
            float notchArc = Mathf.Abs(Frac(turn * NotchCount + 0.5f) - 0.5f) / NotchCount * Mathf.PI * 2f * r; // arc dist (uv)
            bool major = Mathf.RoundToInt(turn * NotchCount) % 6 == 0;
            float notchIn = major ? 0.865f : 0.89f;
            float notch = (1f - TextureFactory.SmoothStep(0.006f, 0.012f, notchArc))
                * TextureFactory.SmoothStep(notchIn - 0.006f, notchIn + 0.004f, r) * (1f - TextureFactory.SmoothStep(0.915f, 0.925f, r));
            float lines = Mathf.Clamp01(Mathf.Max(outer, Mathf.Max(inner * 0.85f, notch * 0.9f)));

            // G: glyph band.
            float rune = Glyphs(p, r, turn);

            // A: dark backing behind the rims and (lighter) the glyph band.
            float backOuter = TextureFactory.SmoothStep(0.88f, 0.9f, r);
            float backInner = 1f - TextureFactory.SmoothStep(0.02f, 0.03f, Mathf.Abs(r - InnerRim));
            float backBand = (1f - TextureFactory.SmoothStep(BandHalf, BandHalf + 0.015f, Mathf.Abs(r - BandCentre))) * 0.5f;
            float backing = Mathf.Max(backOuter, Mathf.Max(backInner, backBand)) * (1f - TextureFactory.SmoothStep(0.975f, 0.995f, r));

            // B: shattered-earth wave band.
            float wave = WaveBand(r, turn);

            return new Color(lines, rune, wave, backing);
        }

        /// <summary>Occult glyphs: each one a stem plus two strokes picked by hash, small dots between them.</summary>
        private static float Glyphs(Vector2 p, float r, float turn)
        {
            float cellF = turn * GlyphCount;
            int cell = Mathf.FloorToInt(cellF);
            float arcPerCell = Mathf.PI * 2f * BandCentre / GlyphCount;
            float gx = (Frac(cellF) - 0.5f) * arcPerCell / BandHalf; // glyph box units: +-1 = band half height
            float gy = (r - BandCentre) / BandHalf;
            float best = 0f;
            if (Mathf.Abs(gx) < 1.2f && Mathf.Abs(gy) < 1.2f)
            {
                const float w = 0.17f;
                float stem = Mathf.Max(Mathf.Abs(gx), Mathf.Abs(gy) - 0.85f);
                float d = stem;
                int a = (int)(TextureFactory.Hash(cell, 1, Seed) * 7f);
                int b = (a + 1 + (int)(TextureFactory.Hash(cell, 2, Seed) * 6f)) % 7;
                d = Mathf.Min(d, Stroke(a, gx, gy));
                d = Mathf.Min(d, Stroke(b, gx, gy));
                best = 1f - TextureFactory.SmoothStep(w - 0.07f, w + 0.07f, d);
            }
            // Separator dot halfway between glyphs.
            float dotX = (Mathf.Abs(Frac(cellF) - 0.5f) - 0.5f) * arcPerCell / BandHalf;
            float dot = 1f - TextureFactory.SmoothStep(0.14f, 0.26f, Mathf.Sqrt(dotX * dotX + gy * gy));
            return Mathf.Clamp01(Mathf.Max(best, dot * 0.8f));
        }

        /// <summary>Distance (glyph box units) to stroke <paramref name="kind"/>.</summary>
        private static float Stroke(int kind, float x, float y)
        {
            switch (kind)
            {
                case 0: return Mathf.Max(Mathf.Abs(y - 0.72f), Mathf.Abs(x) - 0.6f);              // top bar
                case 1: return Mathf.Max(Mathf.Abs(y + 0.72f), Mathf.Abs(x) - 0.6f);              // bottom bar
                case 2: return Mathf.Max(Mathf.Abs(x - y) * 0.7071f, Mathf.Abs(x + y) * 0.7071f - 0.75f); // diagonal
                case 3: return Mathf.Max(Mathf.Abs(x + y) * 0.7071f, Mathf.Abs(x - y) * 0.7071f - 0.75f); // anti-diagonal
                case 4: return Mathf.Abs(Mathf.Sqrt(x * x + y * y) - 0.55f);                       // ring
                case 5: return Mathf.Max(Mathf.Abs(y - (0.75f - Mathf.Abs(x) * 1.1f)), Mathf.Abs(x) - 0.65f); // arrow-head
                default: return Mathf.Max(Mathf.Abs(y), Mathf.Abs(x) - 0.55f);                    // cross bar
            }
        }

        /// <summary>Wave band: jagged edges, broken into chunks, radial + one ring crack glowing.</summary>
        private static float WaveBand(float r, float turn)
        {
            float rim = GroundTelegraph.RimShare;
            float jag = (TextureFactory.ValueNoise(turn * 48f, 0.5f, 48, Seed) - 0.5f) * 0.035f;
            float jag2 = (TextureFactory.ValueNoise(turn * 48f, 7.5f, 48, Seed + 1) - 0.5f) * 0.035f;
            float band = TextureFactory.SmoothStep(rim - 0.065f + jag - 0.008f, rim - 0.065f + jag + 0.008f, r)
                * (1f - TextureFactory.SmoothStep(rim + 0.05f + jag2 - 0.008f, rim + 0.05f + jag2 + 0.008f, r));
            if (band <= 0.01f) return 0f;
            // Chunks: 36 round the ring with a hash gap between some of them.
            float chunkF = turn * 36f + (TextureFactory.ValueNoise(turn * 36f, r * 40f, 36, Seed + 2) - 0.5f) * 0.3f;
            float edge = Mathf.Abs(Frac(chunkF) - 0.5f); // 0.5 = chunk border
            float crackRadial = TextureFactory.SmoothStep(0.44f, 0.48f, edge);
            float ringLine = rim - 0.01f + (TextureFactory.ValueNoise(turn * 30f, 3.3f, 30, Seed + 3) - 0.5f) * 0.03f;
            float crackRing = 1f - TextureFactory.SmoothStep(0.004f, 0.009f, Mathf.Abs(r - ringLine));
            bool gap = TextureFactory.Hash(((Mathf.FloorToInt(chunkF) % 36) + 36) % 36, 4, Seed) < 0.12f;
            float crack = Mathf.Max(crackRadial, crackRing);
            if (gap) return crack * band; // a missing chunk: only the glow line stays
            return band * (0.5f + 0.5f * crack);
        }

        // ------------------------------------------------------------ Fill mask

        /// <summary>Fill mask pixel (public for the EditMode test).</summary>
        public static Color DiscPixel(int x, int y)
        {
            Vector2 p = Point(x, y);
            float r = p.magnitude;
            float turn = Turn(p);
            // Ground cells (~5 across the radius) with cracks on their borders.
            float cellD = CrackDistance(p * 5.5f);
            float cells = 1f - TextureFactory.SmoothStep(0.03f, 0.08f, cellD);
            // Seven cracks out from the centre, wavy, thinning outward.
            float rayF = turn * 7f + (TextureFactory.ValueNoise(turn * 7f, r * 9f, 7, Seed + 5) - 0.5f) * 0.5f;
            float rayArc = Mathf.Abs(Frac(rayF + 0.5f) - 0.5f) / 7f * Mathf.PI * 2f * r;
            float ray = (1f - TextureFactory.SmoothStep(0.008f, 0.02f + 0.01f * (1f - r), rayArc)) * TextureFactory.SmoothStep(0.05f, 0.15f, r);
            float crack = Mathf.Clamp01(Mathf.Max(cells * 0.85f, ray));
            float blotch = TextureFactory.Fbm((p.x + 1f) * 0.5f, (p.y + 1f) * 0.5f, 4, 3, Seed + 6);
            return new Color(crack, TextureFactory.SmoothStep(0.3f, 0.75f, blotch), 0f, 1f);
        }

        /// <summary>Distance to the nearest Voronoi cell border (same maths as the lane shader's cracks).</summary>
        private static float CrackDistance(Vector2 q)
        {
            int ix = Mathf.FloorToInt(q.x), iy = Mathf.FloorToInt(q.y);
            float d1 = 8f, d2 = 8f;
            for (int oy = -1; oy <= 1; oy++)
            for (int ox = -1; ox <= 1; ox++)
            {
                int cx = ix + ox, cy = iy + oy;
                var site = new Vector2(cx + 0.1f + 0.8f * TextureFactory.Hash(cx, cy, Seed + 7),
                    cy + 0.1f + 0.8f * TextureFactory.Hash(cx, cy, Seed + 8));
                float d = (site - q).sqrMagnitude;
                if (d < d1) { d2 = d1; d1 = d; }
                else if (d < d2) d2 = d;
            }
            return Mathf.Sqrt(d2) - Mathf.Sqrt(d1);
        }

        // ------------------------------------------------------------ Helpers

        /// <summary>Pixel centre in -1..1 quad space.</summary>
        private static Vector2 Point(int x, int y) =>
            new Vector2((x + 0.5f) / TextureSize * 2f - 1f, (y + 0.5f) / TextureSize * 2f - 1f);

        /// <summary>Angle round the centre as 0..1.</summary>
        private static float Turn(Vector2 p) => Frac(Mathf.Atan2(p.y, p.x) / (Mathf.PI * 2f) + 1f);

        private static float Frac(float v) => v - Mathf.Floor(v);
    }
}
