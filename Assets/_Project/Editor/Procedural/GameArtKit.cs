using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Procedural
{
    /// <summary>
    /// Generates the Game scene's shared textures and materials (all procedural, no external art).
    /// Stone: warm grey, worn, lit by torches. Not dismal: some warmth in the stone and mortar.
    /// Re-run is safe: textures are rewritten, materials updated in place (references survive).
    /// </summary>
    public sealed class GameArtKit
    {
        private const string TexDir = "Assets/_Project/Art/Textures/Game";
        private const string MatDir = "Assets/_Project/Art/Materials/Game";
        private const string ReloadRingShader = "SurvivorSouls/ReloadRing";
        private const string SoftDotPath = "Assets/_Project/Art/Textures/T_SoftDot.png"; // made by MainMenuSceneBuilder

        public Material Palette, Floor, OuterGround, Grass, Wall, RuneDecal, BlobShadow, Flame, Ember, Mist;
        public Material HeroRing, DirectionMarker, Gold, GoldRim;
        public Texture2D SoftDot;
        public Mesh Quad;

        public static GameArtKit Build()
        {
            var kit = new GameArtKit();
            kit.SoftDot = AssetDatabase.LoadAssetAtPath<Texture2D>(SoftDotPath);
            if (kit.SoftDot == null) throw new System.InvalidOperationException($"Missing {SoftDotPath}. Rebuild the main menu scene first.");
            kit.Quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");

            var paletteAlbedo = TextureFactory.Write($"{TexDir}/T_Palette.png", PaletteAtlas.Width, PaletteAtlas.Height,
                PaletteAtlas.AlbedoPixel, TextureFactory.Kind.Color, mipmaps: false, wrap: TextureWrapMode.Clamp);
            var paletteSpec = TextureFactory.Write($"{TexDir}/T_Palette_Spec.png", PaletteAtlas.Width, PaletteAtlas.Height,
                PaletteAtlas.SpecularPixel, TextureFactory.Kind.Linear, mipmaps: false, wrap: TextureWrapMode.Clamp);
            kit.Palette = EditorAssetUtil.SimpleLitMaterial($"{MatDir}/M_Palette.mat", paletteAlbedo, Color.white, specMap: paletteSpec);

            var (floorAlbedo, floorNormal) = MakeStone("T_Flagstone", FlagstoneSample);
            kit.Floor = EditorAssetUtil.SimpleLitMaterial($"{MatDir}/M_Floor.mat", floorAlbedo, new Color(1.06f, 1.03f, 0.98f), floorNormal, // weathered pale stone: the dark knight reads on it
                smoothness: 0.25f, specColor: new Color(0.18f, 0.17f, 0.16f));
            var (groundAlbedo, groundNormal) = MakeStone("T_GrassDirt", GroundSample);
            kit.OuterGround = EditorAssetUtil.SimpleLitMaterial($"{MatDir}/M_OuterGround.mat", groundAlbedo, Color.white, groundNormal,
                smoothness: 0.1f, specColor: new Color(0.04f, 0.04f, 0.04f));

            kit.Grass = EditorAssetUtil.LoadOrCreateMaterial($"{MatDir}/M_Grass.mat", "SurvivorSouls/Grass");
            kit.Grass.enableInstancing = false; // one static mesh per tile; SRP Batcher handles it
            kit.Grass.SetFloat("_TipLight", 0.15f);
            EditorUtility.SetDirty(kit.Grass);

            var (wallAlbedo, wallNormal) = MakeStone("T_StoneBlocks", BlockSample);
            kit.Wall = EditorAssetUtil.SimpleLitMaterial($"{MatDir}/M_Wall.mat", wallAlbedo, new Color(0.78f, 0.76f, 0.76f), wallNormal,
                smoothness: 0.2f, specColor: new Color(0.12f, 0.12f, 0.12f));

            var rune = TextureFactory.Write($"{TexDir}/T_RuneCircle.png", 256, 256, RunePixel, TextureFactory.Kind.Color,
                wrap: TextureWrapMode.Clamp, maxSize: 256);
            kit.RuneDecal = EditorAssetUtil.TransparentMaterial($"{MatDir}/M_RuneDecal.mat", EditorAssetUtil.SimpleLit, rune,
                new Color(0.1f, 0.075f, 0.06f, 0.75f));

            kit.BlobShadow = EditorAssetUtil.TransparentMaterial($"{MatDir}/M_BlobShadow.mat", EditorAssetUtil.Unlit, kit.SoftDot,
                new Color(0f, 0f, 0f, 0.55f));
            kit.Flame = EditorAssetUtil.ParticleMaterial($"{MatDir}/M_FX_Flame.mat", kit.SoftDot, additive: true);
            kit.Ember = EditorAssetUtil.ParticleMaterial($"{MatDir}/M_FX_Ember.mat", kit.SoftDot, additive: true);
            kit.Mist = EditorAssetUtil.ParticleMaterial($"{MatDir}/M_FX_Mist.mat", kit.SoftDot, additive: false);

            // Player readability: warm ring under the feet, move-direction ring, gold objective arrow with a dark rim.
            // Saturated amber, not pale gold: it must read on white snow (Realm 1) as well as on stone.
            var ring = TextureFactory.Write($"{TexDir}/T_Ring.png", 128, 128, RingPixel, TextureFactory.Kind.Color,
                wrap: TextureWrapMode.Clamp, maxSize: 128);
            // Hero ring = reload ring (KAN-42): procedural radial fill, no texture. Style values live on the material.
            kit.HeroRing = EditorAssetUtil.LoadOrCreateMaterial($"{MatDir}/M_ReloadRing.mat", ReloadRingShader);
            kit.HeroRing.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            EditorUtility.SetDirty(kit.HeroRing);
            kit.DirectionMarker = EditorAssetUtil.TransparentMaterial($"{MatDir}/M_DirectionMarker.mat", EditorAssetUtil.Unlit, ring, new Color(0.9f, 0.42f, 0.08f, 0.9f));
            kit.Gold = EditorAssetUtil.LoadOrCreateMaterial($"{MatDir}/M_Gold.mat", EditorAssetUtil.Unlit);
            kit.Gold.SetColor("_BaseColor", new Color(1f, 0.78f, 0.22f));
            kit.Gold.SetFloat("_Cull", 0f); // flat arrow: visible from both sides
            EditorUtility.SetDirty(kit.Gold);
            kit.GoldRim = EditorAssetUtil.LoadOrCreateMaterial($"{MatDir}/M_GoldRim.mat", EditorAssetUtil.Unlit);
            kit.GoldRim.SetColor("_BaseColor", new Color(0.16f, 0.09f, 0.03f));
            kit.GoldRim.SetFloat("_Cull", 0f);
            EditorUtility.SetDirty(kit.GoldRim);
            AssetDatabase.SaveAssets();
            return kit;
        }

        // ------------------------------------------------------------ Stone generators

        private const int StoneSize = 512;

        /// <summary>One stone sample: albedo colour and height (0 = deep mortar, 1 = stone face).</summary>
        private delegate (Color albedo, float height) StoneSampler(float u, float v);

        private static (Texture2D albedo, Texture2D normal) MakeStone(string name, StoneSampler sample)
        {
            var heights = new float[StoneSize, StoneSize];
            var colors = new Color[StoneSize, StoneSize];
            for (int y = 0; y < StoneSize; y++)
            for (int x = 0; x < StoneSize; x++)
            {
                var (c, h) = sample((x + 0.5f) / StoneSize, (y + 0.5f) / StoneSize);
                colors[x, y] = c;
                heights[x, y] = h;
            }
            var albedo = TextureFactory.Write($"{TexDir}/{name}.png", StoneSize, StoneSize, (x, y) => colors[x, y], TextureFactory.Kind.Color);
            var normal = TextureFactory.Write($"{TexDir}/{name}_N.png", StoneSize, StoneSize,
                (x, y) => TextureFactory.HeightToNormal(heights, x, y, 6f), TextureFactory.Kind.NormalMap);
            return (albedo, normal);
        }

        /// <summary>Irregular flagstones: jittered Voronoi cells, worn bevels, dark mortar with a hint of moss.</summary>
        private static (Color, float) FlagstoneSample(float u, float v)
        {
            const int cells = 5;
            float px = u * cells, py = v * cells;
            int cx = Mathf.FloorToInt(px), cy = Mathf.FloorToInt(py);
            float d1 = 9f, d2 = 9f;
            int bestX = 0, bestY = 0;
            for (int oy = -1; oy <= 1; oy++)
            for (int ox = -1; ox <= 1; ox++)
            {
                int gx = cx + ox, gy = cy + oy;
                int wx = (gx % cells + cells) % cells, wy = (gy % cells + cells) % cells;
                // Mostly-grid jitter gives slab-like, not cellular, stones.
                float fx = gx + 0.5f + (TextureFactory.Hash(wx, wy, 1) - 0.5f) * 0.45f;
                float fy = gy + 0.5f + (TextureFactory.Hash(wx, wy, 2) - 0.5f) * 0.45f;
                // Chebyshev-ish metric for squarer stones.
                float dx = Mathf.Abs(px - fx), dy = Mathf.Abs(py - fy);
                float d = Mathf.Pow(Mathf.Pow(dx, 3f) + Mathf.Pow(dy, 3f), 1f / 3f);
                if (d < d1) { d2 = d1; d1 = d; bestX = wx; bestY = wy; }
                else if (d < d2) d2 = d;
            }

            float edge = d2 - d1;                       // 0 at the seam
            float grain = TextureFactory.Fbm(u, v, 16, 4, 7);
            float blotch = TextureFactory.Fbm(u, v, 4, 3, 21);
            float mortar = 1f - TextureFactory.SmoothStep(0.012f, 0.032f, edge);
            float bevel = TextureFactory.SmoothStep(0.015f, 0.14f, edge);
            float crack = TextureFactory.SmoothStep(0.035f, 0.0f, Mathf.Abs(TextureFactory.Fbm(u, v, 6, 3, 41) - 0.5f)) * TextureFactory.SmoothStep(0.55f, 0.7f, blotch);

            float tone = 0.72f + TextureFactory.Hash(bestX, bestY, 3) * 0.42f;
            float warm = TextureFactory.Hash(bestX, bestY, 4);
            var stone = Color.Lerp(new Color(0.46f, 0.43f, 0.4f), new Color(0.5f, 0.44f, 0.37f), warm) * tone;
            stone *= 0.8f + grain * 0.35f;
            stone *= Mathf.Lerp(0.82f, 1f, bevel);
            stone = Color.Lerp(stone, stone * 0.7f, TextureFactory.SmoothStep(0.5f, 0.75f, blotch)); // wear stains
            stone = Color.Lerp(stone, stone * 0.6f, crack * 0.5f);                                     // hairline cracks

            var mortarColor = Color.Lerp(new Color(0.12f, 0.11f, 0.1f), new Color(0.17f, 0.18f, 0.12f), blotch);
            var c = Color.Lerp(stone, mortarColor, mortar);
            c.a = 1f;
            float h = Mathf.Lerp(bevel * 0.85f + grain * 0.15f, 0f, mortar);
            return (c, h);
        }

        /// <summary>
        /// Soil under the grass blades: dark mossy earth with fine grain, small pebbles and some bare dirt patches.
        /// Darker than the blades so the 3D grass reads; gaps look like real soil, not green paint.
        /// </summary>
        private static (Color, float) GroundSample(float u, float v)
        {
            float patches = TextureFactory.Fbm(u, v, 2, 6, 51);
            float grain = TextureFactory.Fbm(u, v, 48, 3, 53);
            float fine = TextureFactory.Hash((int)(u * StoneSize), (int)(v * StoneSize), 7);
            float moss = TextureFactory.SmoothStep(0.3f, 0.48f, patches);
            float pebble = TextureFactory.SmoothStep(0.78f, 0.86f, TextureFactory.Fbm(u, v, 64, 2, 59));
            var dirt = Color.Lerp(new Color(0.24f, 0.19f, 0.14f), new Color(0.33f, 0.27f, 0.2f), grain) * (0.85f + fine * 0.25f);
            var green = Color.Lerp(new Color(0.16f, 0.2f, 0.09f), new Color(0.24f, 0.29f, 0.12f), grain) * (0.8f + fine * 0.35f);
            var c = Color.Lerp(dirt, green, moss);
            c = Color.Lerp(c, new Color(0.45f, 0.42f, 0.38f) * (0.8f + fine * 0.3f), pebble * 0.8f);
            c.a = 1f;
            return (c, moss * 0.35f + grain * 0.45f + pebble * 0.6f);
        }

        /// <summary>Wall blocks: rows of cut stone with offset joints.</summary>
        private static (Color, float) BlockSample(float u, float v)
        {
            const int rows = 5;
            float ry = v * rows;
            int row = Mathf.FloorToInt(ry);
            float rowT = ry - row;
            float offset = TextureFactory.Hash(row % rows, 0, 5) * 0.8f;
            const int cols = 2;
            float rx = u * cols + offset;
            int col = Mathf.FloorToInt(rx);
            float colT = rx - col;
            int wc = ((col % cols) + cols) % cols;

            float ex = Mathf.Min(colT, 1f - colT) / cols;   // distance to vertical joint (tile units)
            float ey = Mathf.Min(rowT, 1f - rowT) / rows;   // distance to horizontal joint
            float edge = Mathf.Min(ex, ey);

            float grain = TextureFactory.Fbm(u, v, 16, 4, 13);
            float blotch = TextureFactory.Fbm(u, v, 4, 3, 17);
            float mortar = 1f - TextureFactory.SmoothStep(0.006f, 0.014f, edge);
            float bevel = TextureFactory.SmoothStep(0.008f, 0.05f, edge);

            float tone = 0.8f + TextureFactory.Hash(wc, row % rows, 6) * 0.3f;
            var stone = new Color(0.44f, 0.41f, 0.38f) * tone * (0.82f + grain * 0.3f) * Mathf.Lerp(0.8f, 1f, bevel);
            stone = Color.Lerp(stone, stone * 0.7f, TextureFactory.SmoothStep(0.5f, 0.75f, blotch));
            var c = Color.Lerp(stone, new Color(0.1f, 0.095f, 0.09f), mortar);
            c.a = 1f;
            return (c, Mathf.Lerp(bevel * 0.8f + grain * 0.2f, 0f, mortar));
        }

        /// <summary>Soft ring (white + alpha) for the hero ring and the direction marker.</summary>
        private static Color RingPixel(int x, int y)
        {
            float u = (x + 0.5f) / 128f * 2f - 1f, v = (y + 0.5f) / 128f * 2f - 1f;
            float r = Mathf.Sqrt(u * u + v * v);
            float a = 1f - TextureFactory.SmoothStep(0.06f, 0.16f, Mathf.Abs(r - 0.8f));
            return new Color(1f, 1f, 1f, a);
        }

        /// <summary>Carved ring of runes for the arena centre. White + alpha; the material tints it.</summary>
        private static Color RunePixel(int x, int y)
        {
            float u = (x + 0.5f) / 256f * 2f - 1f, v = (y + 0.5f) / 256f * 2f - 1f;
            float r = Mathf.Sqrt(u * u + v * v);
            float angle = Mathf.Atan2(v, u);
            float Band(float centre, float width) => 1f - TextureFactory.SmoothStep(width * 0.5f, width * 0.5f + 0.012f, Mathf.Abs(r - centre));

            float a = Mathf.Max(Band(0.94f, 0.035f), Band(0.74f, 0.02f), Band(0.42f, 0.025f));
            // Rune ticks between the outer rings.
            float tick = Mathf.Repeat(angle / (Mathf.PI * 2f) * 24f, 1f);
            if (r > 0.77f && r < 0.91f && Mathf.Abs(tick - 0.5f) < 0.07f + 0.05f * Mathf.Sin(angle * 7f)) a = Mathf.Max(a, 0.9f);
            // Short glyph bars around the inner ring.
            float glyph = Mathf.Repeat(angle / (Mathf.PI * 2f) * 12f, 1f);
            if (r > 0.5f && r < 0.66f && glyph > 0.35f && glyph < 0.65f && Mathf.Repeat(r * 18f, 1f) < 0.5f) a = Mathf.Max(a, 0.7f);
            if (r > 0.99f) a = 0f;

            float wear = TextureFactory.Fbm((u + 1f) * 0.5f, (v + 1f) * 0.5f, 8, 3, 33);
            a *= TextureFactory.SmoothStep(0.25f, 0.6f, wear);
            return new Color(1f, 1f, 1f, a);
        }
    }
}
