using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Gameplay.World;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.World
{
    /// <summary>
    /// Realm 1 (snowy seaside shore) textures and materials, all procedural. Values come from the
    /// <see cref="RealmLook"/> asset, so the look is tuned in one place.
    /// Snow = world-space ground shader (ground quad, beach slopes and mounds join without seams).
    /// SnowProps / SnowStone / SnowFloor = one snow-cap material each for props, masonry and flagstone patches.
    /// </summary>
    public sealed class RealmArt
    {
        public const string LookPath = "Assets/_Project/Art/World/Realm1_Look.asset";
        private const string TexDir = "Assets/_Project/Art/Textures/Realm1";
        private const string MatDir = "Assets/_Project/Art/Materials/Realm1";
        private const int Size = 256;

        public RealmLook Look;
        public Material Snow, SnowProps, SnowStone, SnowFloor, Sea, Foam, Sky, Decal, Footprint, Snowflake, SeaMist, Glow;

        public static RealmArt Build(GameArtKit kit)
        {
            var art = new RealmArt { Look = LoadOrCreateLook() };
            var look = art.Look;

            var detail = TextureFactory.Write($"{TexDir}/T_SnowDetail.png", Size, Size, SnowDetailPixel, TextureFactory.Kind.Color, maxSize: Size);
            var detailN = HeightNormal("T_SnowDetail_N", SnowHeight, 2.5f);
            var macro = TextureFactory.Write($"{TexDir}/T_SnowMacro.png", Size, Size, MacroPixel, TextureFactory.Kind.Linear, maxSize: Size);
            var ripple = HeightNormal("T_SeaRipple_N", RippleHeight, 5f);
            var patch = TextureFactory.Write($"{TexDir}/T_SnowPatch.png", Size, Size, PatchPixel, TextureFactory.Kind.Linear, maxSize: Size, compress: false);
            var foamNoise = TextureFactory.Write($"{TexDir}/T_FoamNoise.png", Size, Size, FoamPixel, TextureFactory.Kind.Linear, maxSize: Size);
            var print = TextureFactory.Write($"{TexDir}/T_Footprint.png", 64, 64, FootprintPixel, TextureFactory.Kind.Color,
                wrap: TextureWrapMode.Clamp, maxSize: 64);

            art.Snow = EditorAssetUtil.LoadOrCreateMaterial($"{MatDir}/M_Snow.mat", "SurvivorSouls/Snow");
            art.Snow.SetTexture("_DetailMap", detail);
            art.Snow.SetTexture("_NormalMap", detailN);
            art.Snow.SetTexture("_MacroMap", macro);
            art.Snow.SetColor("_FreshColor", look.freshSnow);
            art.Snow.SetColor("_PackedColor", look.packedSnow);
            art.Snow.SetColor("_ScouredColor", look.scouredGround);
            art.Snow.SetFloat("_ScouredAmount", look.scouredAmount);
            art.Snow.SetFloat("_Sparkle", look.sparkle);
            art.Snow.SetFloat("_NormalStrength", look.snowNormalStrength);

            art.SnowProps = SnowCap($"{MatDir}/M_SnowProps.mat", kit.Palette.GetTexture("_BaseMap"), Color.white, 1f, macro, look);
            art.SnowFloor = SnowCap($"{MatDir}/M_SnowFlagstone.mat", kit.Floor.GetTexture("_BaseMap"), new Color(0.95f, 0.93f, 0.9f), 1f, macro, look);
            art.SnowStone = SnowCap($"{MatDir}/M_SnowStone.mat", kit.Wall.GetTexture("_BaseMap"), new Color(0.74f, 0.74f, 0.76f), 1f, macro, look);

            art.Sea = EditorAssetUtil.LoadOrCreateMaterial($"{MatDir}/M_Sea.mat", "SurvivorSouls/Sea");
            art.Sea.SetTexture("_NormalMap", ripple);
            art.Sea.SetTexture("_FoamMap", foamNoise);
            art.Sea.SetColor("_ShallowColor", look.shallowWater);
            art.Sea.SetColor("_DeepColor", look.deepWater);
            art.Sea.SetColor("_SkyColor", look.seaSky);
            art.Sea.SetColor("_FoamColor", look.foam);
            art.Sea.SetFloat("_WaveHeight", look.waveHeight);
            art.Sea.SetFloat("_WaveLength", look.waveLength);
            art.Sea.SetFloat("_WaveSpeed", look.waveSpeed);
            art.Sea.SetFloat("_DepthRange", look.depthRange);

            art.Foam = EditorAssetUtil.LoadOrCreateMaterial($"{MatDir}/M_ShoreFoam.mat", "SurvivorSouls/ShoreFoam");
            art.Foam.SetTexture("_FoamMap", foamNoise);
            art.Foam.SetColor("_FoamColor", look.foam);

            art.Sky = EditorAssetUtil.LoadOrCreateMaterial($"{MatDir}/M_OvercastSky.mat", "SurvivorSouls/OvercastSky");
            art.Sky.SetColor("_ZenithColor", look.skyZenith);
            art.Sky.SetColor("_HorizonColor", look.skyHorizon);
            art.Sky.SetColor("_SunGlowColor", look.sunGlow);
            art.Sky.SetColor("_BelowColor", Color.Lerp(look.deepWater, look.skyHorizon, 0.45f));

            // Soft dark patches: trampled / dirty snow around rocks, wrecks and camps.
            art.Decal = EditorAssetUtil.TransparentMaterial($"{MatDir}/M_SnowDirt.mat", EditorAssetUtil.Unlit, kit.SoftDot,
                new Color(0.36f, 0.36f, 0.38f, 0.32f));
            art.Footprint = EditorAssetUtil.ParticleMaterial($"{MatDir}/M_FX_Footprint.mat", print, additive: false);
            art.Snowflake = EditorAssetUtil.ParticleMaterial($"{MatDir}/M_FX_Snowflake.mat", kit.SoftDot, additive: false);
            art.SeaMist = EditorAssetUtil.ParticleMaterial($"{MatDir}/M_FX_SeaMist.mat", kit.SoftDot, additive: false);
            art.Glow = EditorAssetUtil.ParticleMaterial($"{MatDir}/M_FX_Glow.mat", kit.SoftDot, additive: true);

            foreach (var m in new[] { art.Snow, art.SnowProps, art.SnowStone, art.SnowFloor, art.Sea, art.Foam, art.Sky }) EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssets();
            return art;
        }

        private static RealmLook LoadOrCreateLook()
        {
            var look = AssetDatabase.LoadAssetAtPath<RealmLook>(LookPath);
            if (look != null) return look;
            EditorAssetUtil.EnsureFolder(System.IO.Path.GetDirectoryName(LookPath).Replace('\\', '/'));
            look = ScriptableObject.CreateInstance<RealmLook>();
            AssetDatabase.CreateAsset(look, LookPath);
            return look;
        }

        private static Material SnowCap(string path, Texture baseMap, Color tint, float uvScale, Texture2D noise, RealmLook look)
        {
            var mat = EditorAssetUtil.LoadOrCreateMaterial(path, "SurvivorSouls/SnowCapLit");
            mat.SetTexture("_BaseMap", baseMap);
            mat.SetColor("_BaseColor", tint);
            mat.SetFloat("_UvScale", uvScale);
            mat.SetTexture("_SnowNoise", noise);
            mat.SetFloat("_SnowNoiseScale", 5f);
            mat.SetColor("_SnowColor", look.snowCap);
            mat.SetFloat("_SnowLevel", look.snowCapLevel);
            mat.enableInstancing = true;
            return mat;
        }

        private static Texture2D HeightNormal(string name, System.Func<float, float, float> height, float strength)
        {
            var h = new float[Size, Size];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
                h[x, y] = height((x + 0.5f) / Size, (y + 0.5f) / Size);
            return TextureFactory.Write($"{TexDir}/{name}.png", Size, Size, (x, y) => TextureFactory.HeightToNormal(h, x, y, strength),
                TextureFactory.Kind.NormalMap, maxSize: Size);
        }

        // ------------------------------------------------------------ Generators (all tileable)

        /// <summary>Wind ripples (sastrugi) stretched along X plus soft bumps. Low contrast on purpose.</summary>
        private static float SnowHeight(float u, float v)
        {
            float warp = TextureFactory.Fbm(u, v, 3, 3, 71) * 0.6f;
            float ripples = Mathf.Sin((v * 9f + warp * 3f) * Mathf.PI * 2f) * 0.5f + 0.5f;
            float bumps = TextureFactory.Fbm(u, v, 8, 4, 73);
            return ripples * 0.35f * TextureFactory.Fbm(u, v, 2, 2, 75) + bumps * 0.65f;
        }

        private static Color SnowDetailPixel(int x, int y)
        {
            float u = (x + 0.5f) / Size, v = (y + 0.5f) / Size;
            float h = SnowHeight(u, v);
            float tone = 0.5f + (h - 0.5f) * 0.22f + (TextureFactory.Hash(x, y, 77) - 0.5f) * 0.03f;
            // Sparkle: sparse single-pixel glints (mips average them away at distance, so no shimmer).
            float glint = TextureFactory.Hash(x, y, 79) > 0.992f ? 1f : 0f;
            return new Color(tone, tone, tone * 1.01f, glint);
        }

        /// <summary>Snow-patch noise for the snow-cap shader (B channel): irregular fbm blobs, no visible grid.</summary>
        private static Color PatchPixel(int x, int y)
        {
            float u = (x + 0.5f) / Size, v = (y + 0.5f) / Size;
            float warp = TextureFactory.Fbm(u, v, 3, 2, 101) * 0.25f;
            float n = TextureFactory.Fbm(u + warp, v - warp, 5, 5, 103);
            n = Mathf.Clamp01((n - 0.5f) * 1.8f + 0.5f); // more contrast: clear patches, clear gaps
            return new Color(n, n, n, 1f);
        }

        /// <summary>R = wind-packed amount, G = scoured patches (sand/shingle shows), B = brightness.</summary>
        private static Color MacroPixel(int x, int y)
        {
            float u = (x + 0.5f) / Size, v = (y + 0.5f) / Size;
            float packedAmount = TextureFactory.SmoothStep(0.35f, 0.7f, TextureFactory.Fbm(u, v, 4, 4, 81));
            float scoured = TextureFactory.SmoothStep(0.62f, 0.78f, TextureFactory.Fbm(u, v, 6, 4, 83));
            float tone = TextureFactory.Fbm(u, v, 3, 3, 85);
            return new Color(packedAmount, scoured, tone, 1f);
        }

        private static float RippleHeight(float u, float v)
        {
            float a = TextureFactory.Fbm(u, v, 6, 4, 91);
            float b = Mathf.Sin((u * 4f + v * 7f + TextureFactory.Fbm(u, v, 3, 2, 93) * 2f) * Mathf.PI * 2f) * 0.5f + 0.5f;
            return a * 0.7f + b * 0.3f;
        }

        /// <summary>Foam web: bright where two noise fields cross mid-value (stringy, lace-like).</summary>
        private static Color FoamPixel(int x, int y)
        {
            float u = (x + 0.5f) / Size, v = (y + 0.5f) / Size;
            float a = 1f - Mathf.Abs(TextureFactory.Fbm(u, v, 8, 3, 95) - 0.5f) * 4f;
            float b = TextureFactory.Fbm(u, v, 4, 3, 97);
            float f = Mathf.Clamp01(a) * 0.6f + b * 0.55f;
            return new Color(f, f, f, 1f);
        }

        /// <summary>Boot print: toe oval + heel oval (pointing +V). Dark blue-grey; alpha carries the shape.</summary>
        private static Color FootprintPixel(int x, int y)
        {
            float u = (x + 0.5f) / 64f * 2f - 1f, v = (y + 0.5f) / 64f * 2f - 1f;
            float toe = Ellipse(u, v - 0.28f, 0.42f, 0.55f);
            float heel = Ellipse(u, v + 0.58f, 0.34f, 0.3f);
            float a = Mathf.Max(toe, heel);
            return new Color(0.42f, 0.47f, 0.56f, a * 0.75f);
        }

        private static float Ellipse(float x, float y, float rx, float ry)
        {
            float d = Mathf.Sqrt(x * x / (rx * rx) + y * y / (ry * ry));
            return 1f - TextureFactory.SmoothStep(0.7f, 1f, d);
        }
    }
}
