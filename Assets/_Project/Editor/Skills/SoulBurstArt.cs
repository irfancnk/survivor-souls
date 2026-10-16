using SurvivorSouls.Editor.Procedural;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Skills
{
    /// <summary>
    /// Soul Burst look (KAN-66): the one new texture of the burst (a soul-fire wisp) and the layer materials. The burst
    /// reuses T_Ring (shockwave + dome shell), T_RuneCircle (ground mark) and T_SoftDot (flash + shards).
    /// Wisp: torn flame blob, white rim and darker core in RGB, so a purple tint reads "dark fire with a magenta edge" on
    /// snow and on dark ground. 128 px, ASTC, one alpha-blended material shared by the fire and smoke puffs.
    /// </summary>
    public static class SoulBurstArt
    {
        public const string WispPath = "Assets/_Project/Art/Skills/T_FX_SoulWisp.png";
        private const string RuneCirclePath = "Assets/_Project/Art/Textures/Game/T_RuneCircle.png"; // made by GameArtKit
        private const int Size = 128;

        public sealed class Materials
        {
            /// <summary>Shockwave + dome shell (additive ring).</summary>
            public Material Ring;
            /// <summary>Ground shockwave (alpha-blended ring: darker than snow, brighter than stone).</summary>
            public Material GroundRing;
            /// <summary>Soul fire + smoke (alpha-blended wisp).</summary>
            public Material Wisp;
            /// <summary>Rune mark (alpha-blended rune circle).</summary>
            public Material Mark;
        }

        public static Materials Build(string fxDir, Texture2D ringTex)
        {
            var wisp = TextureFactory.Write(WispPath, Size, Size, WispPixel, TextureFactory.Kind.Color,
                wrap: TextureWrapMode.Clamp, maxSize: Size);
            var runes = AssetDatabase.LoadAssetAtPath<Texture2D>(RuneCirclePath);
            if (runes == null) throw new System.InvalidOperationException($"Missing {RuneCirclePath} (GameArtKit builds it).");
            return new Materials
            {
                Ring = EditorAssetUtil.ParticleMaterial(fxDir + "/M_FX_SoulBurstRing.mat", ringTex, additive: true),
                GroundRing = EditorAssetUtil.ParticleMaterial(fxDir + "/M_FX_SoulBurstGround.mat", ringTex, additive: false),
                Wisp = EditorAssetUtil.ParticleMaterial(fxDir + "/M_FX_SoulWisp.mat", wisp, additive: false),
                Mark = EditorAssetUtil.ParticleMaterial(fxDir + "/M_FX_SoulBurstMark.mat", runes, additive: false),
            };
        }

        /// <summary>Flame wisp: teardrop blob torn by noise; alpha falls off at the edge, RGB is bright at the rim and dim in the core.</summary>
        private static Color WispPixel(int x, int y)
        {
            float u = (x + 0.5f) / Size * 2f - 1f, v = (y + 0.5f) / Size * 2f - 1f;
            // Teardrop: wider at the bottom, licks up to a point.
            float taper = Mathf.Lerp(1.15f, 0.62f, Mathf.Clamp01((v + 1f) * 0.5f));
            float d = Mathf.Sqrt(u * u / (taper * taper) + v * v * 0.85f);
            float n = TextureFactory.Fbm((u + 1f) * 0.5f, (v + 1f) * 0.5f, 4, 4, 66);
            d += (n - 0.5f) * 0.55f;
            float alpha = 1f - TextureFactory.SmoothStep(0.45f, 0.95f, d);
            float rim = TextureFactory.SmoothStep(0.2f, 0.72f, d);
            float g = Mathf.Lerp(0.22f, 1f, rim);
            return new Color(g, g, g, alpha);
        }
    }
}
