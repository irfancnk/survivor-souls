using UnityEngine;

namespace SurvivorSouls.Editor.Procedural
{
    /// <summary>Named colour cells in the shared palette atlas. Order = cell index; append only.</summary>
    public enum Swatch
    {
        Skin, Beard, HoodCloth, HoodInner,
        TunicCloth, Leather, DarkLeather, Gunmetal,
        Rust, Crimson, Wood, BrightMetal,
        Stone, Coal, Pants, Fletching,
    }

    /// <summary>
    /// One small texture (4x4 cells) that colours every procedural character and prop, so they all share
    /// one material and batch well. Each cell holds a base colour plus wear noise (rust on metal, grain on
    /// cloth). A matching specular atlas (RGB spec, A smoothness) gives metal its sheen.
    /// To retune the look, edit <see cref="Define"/> and rebuild the scene.
    /// </summary>
    public static class PaletteAtlas
    {
        public const int Grid = 4;
        public const int CellPixels = 32;
        public const int Size = Grid * CellPixels;
        private const float Margin = 0.18f; // keep UVs inside the cell, away from bilinear bleed

        private readonly struct Cell
        {
            public readonly Color Base;       // sRGB albedo
            public readonly Color Wear;       // colour blended in by noise
            public readonly float WearAmount; // 0..1 coverage
            public readonly float Grain;      // fine per-pixel variation
            public readonly float Specular;   // 0..1 spec intensity
            public readonly float Smoothness; // 0..1

            public Cell(Color b, Color wear, float wearAmount, float grain, float spec, float smooth)
            {
                Base = b; Wear = wear; WearAmount = wearAmount; Grain = grain; Specular = spec; Smoothness = smooth;
            }
        }

        // Palette from the concept sheet: charcoal, gunmetal, rusty brown, crimson accent.
        private static Cell Define(Swatch s)
        {
            switch (s)
            {
                case Swatch.Skin: return new Cell(Hex(0x8E624C), Hex(0x6E4A3A), 0.3f, 0.04f, 0.12f, 0.35f);
                case Swatch.Beard: return new Cell(Hex(0x2E221A), Hex(0x4A3A2E), 0.35f, 0.12f, 0.05f, 0.2f);
                case Swatch.HoodCloth: return new Cell(Hex(0x2C2E32), Hex(0x2A2724), 0.45f, 0.1f, 0.04f, 0.15f);
                case Swatch.HoodInner: return new Cell(Hex(0x0E0D0D), Hex(0x161412), 0.3f, 0.05f, 0f, 0.05f);
                case Swatch.TunicCloth: return new Cell(Hex(0x3C3D40), Hex(0x3A3632), 0.5f, 0.12f, 0.04f, 0.15f);
                case Swatch.Leather: return new Cell(Hex(0x6B4429), Hex(0x4C2F1D), 0.45f, 0.08f, 0.25f, 0.45f);
                case Swatch.DarkLeather: return new Cell(Hex(0x33241B), Hex(0x251A14), 0.4f, 0.06f, 0.2f, 0.4f);
                case Swatch.Gunmetal: return new Cell(Hex(0x5A6066), Hex(0x7A4A2C), 0.28f, 0.06f, 0.85f, 0.62f);
                case Swatch.Rust: return new Cell(Hex(0x6E4630), Hex(0x4A4E52), 0.35f, 0.1f, 0.35f, 0.3f);
                case Swatch.Crimson: return new Cell(Hex(0x7C1418), Hex(0x4E0D10), 0.45f, 0.1f, 0.1f, 0.25f);
                case Swatch.Wood: return new Cell(Hex(0x4A2E1C), Hex(0x2E1C12), 0.5f, 0.1f, 0.25f, 0.45f);
                case Swatch.BrightMetal: return new Cell(Hex(0x9AA0A6), Hex(0x6E5038), 0.18f, 0.04f, 1f, 0.75f);
                case Swatch.Stone: return new Cell(Hex(0x6E6862), Hex(0x4E4944), 0.5f, 0.12f, 0.1f, 0.2f);
                case Swatch.Coal: return new Cell(Hex(0x1A1512), Hex(0xC2491A), 0.25f, 0.1f, 0.05f, 0.1f);
                case Swatch.Pants: return new Cell(Hex(0x2B2A29), Hex(0x3A3028), 0.4f, 0.08f, 0.05f, 0.15f);
                case Swatch.Fletching: return new Cell(Hex(0x8E1A1A), Hex(0x5A1010), 0.3f, 0.1f, 0.05f, 0.2f);
                default: return new Cell(Color.magenta, Color.magenta, 0f, 0f, 0f, 0f);
            }
        }

        /// <summary>
        /// UV mapper for one swatch. Samples the cell with a smooth function of position, so the wear
        /// noise varies across a part without seams.
        /// </summary>
        public static MeshBuilder.UvMapper For(Swatch swatch)
        {
            int index = (int)swatch;
            float cx = index % Grid, cy = index / Grid;
            return (p, n) =>
            {
                float u = 0.5f + 0.5f * Mathf.Sin(p.x * 23f + p.z * 17f + p.y * 5f);
                float v = 0.5f + 0.5f * Mathf.Sin(p.y * 29f - p.x * 7f + p.z * 11f);
                u = Mathf.Lerp(Margin, 1f - Margin, u);
                v = Mathf.Lerp(Margin, 1f - Margin, v);
                return new Vector2((cx + u) / Grid, (cy + v) / Grid);
            };
        }

        public static Color AlbedoPixel(int x, int y)
        {
            var cell = Define(CellAt(x, y));
            float wear = TextureFactory.Fbm(x / (float)Size, y / (float)Size, 8, 3, 11);
            float mask = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f - cell.WearAmount, 1f - cell.WearAmount + 0.25f, wear));
            float grain = (TextureFactory.Hash(x, y, 5) - 0.5f) * cell.Grain;
            var c = Color.Lerp(cell.Base, cell.Wear, mask);
            return new Color(c.r + grain, c.g + grain, c.b + grain, 1f);
        }

        public static Color SpecularPixel(int x, int y)
        {
            var cell = Define(CellAt(x, y));
            float wear = TextureFactory.Fbm(x / (float)Size, y / (float)Size, 8, 3, 11);
            float mask = Mathf.InverseLerp(1f - cell.WearAmount, 1f, wear);
            // Worn/rusted spots are duller.
            float spec = cell.Specular * Mathf.Lerp(1f, 0.35f, mask);
            float smooth = cell.Smoothness * Mathf.Lerp(1f, 0.5f, mask);
            return new Color(spec, spec, spec, smooth);
        }

        private static Swatch CellAt(int x, int y) => (Swatch)(y / CellPixels * Grid + x / CellPixels);

        private static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
