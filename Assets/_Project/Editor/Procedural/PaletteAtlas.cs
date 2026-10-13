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
        Grass, Moss, Bark, Foliage,
        Snow, Ice, Bleached, DryGrass,
    }

    /// <summary>
    /// One small texture (4 x 6 cells) that colours every procedural character and prop, so they all share
    /// one material and batch well. Each cell holds a base colour plus wear noise (rust on metal, grain on
    /// cloth). A matching specular atlas (RGB spec, A smoothness) gives metal its sheen.
    /// To retune the look, edit <see cref="Define"/> and rebuild the scene.
    /// </summary>
    public static class PaletteAtlas
    {
        public const int Columns = 4;
        public const int Rows = 6;
        public const int CellPixels = 32;
        public const int Width = Columns * CellPixels;
        public const int Height = Rows * CellPixels;
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
                case Swatch.Skin: return new Cell(Hex(0x96705C), Hex(0x7A5646), 0.3f, 0.04f, 0.12f, 0.35f);
                case Swatch.Beard: return new Cell(Hex(0x2E221A), Hex(0x4A3A2E), 0.35f, 0.12f, 0.05f, 0.2f);
                case Swatch.HoodCloth: return new Cell(Hex(0x2E2C2A), Hex(0x221F1C), 0.5f, 0.1f, 0.04f, 0.15f);
                case Swatch.HoodInner: return new Cell(Hex(0x0E0D0D), Hex(0x161412), 0.3f, 0.05f, 0f, 0.05f);
                case Swatch.TunicCloth: return new Cell(Hex(0x35332F), Hex(0x28241F), 0.55f, 0.12f, 0.04f, 0.15f);
                case Swatch.Leather: return new Cell(Hex(0x4E3424), Hex(0x352318), 0.5f, 0.08f, 0.25f, 0.45f);
                case Swatch.DarkLeather: return new Cell(Hex(0x2A1E17), Hex(0x1E1510), 0.4f, 0.06f, 0.2f, 0.4f);
                case Swatch.Gunmetal: return new Cell(Hex(0x4C5156), Hex(0x6A3E24), 0.42f, 0.06f, 0.85f, 0.62f);
                case Swatch.Rust: return new Cell(Hex(0x5A3C2A), Hex(0x43474B), 0.35f, 0.1f, 0.35f, 0.3f);
                case Swatch.Crimson: return new Cell(Hex(0x6E1A22), Hex(0x4A1016), 0.45f, 0.1f, 0.1f, 0.25f);
                case Swatch.Wood: return new Cell(Hex(0x4A2E1C), Hex(0x2E1C12), 0.5f, 0.1f, 0.25f, 0.45f);
                case Swatch.BrightMetal: return new Cell(Hex(0x9AA0A6), Hex(0x6E5038), 0.18f, 0.04f, 1f, 0.75f);
                case Swatch.Stone: return new Cell(Hex(0x6E6862), Hex(0x4E4944), 0.5f, 0.12f, 0.1f, 0.2f);
                case Swatch.Coal: return new Cell(Hex(0x1A1512), Hex(0xC2491A), 0.25f, 0.1f, 0.05f, 0.1f);
                case Swatch.Pants: return new Cell(Hex(0x262422), Hex(0x332A22), 0.4f, 0.08f, 0.05f, 0.15f);
                case Swatch.Fletching: return new Cell(Hex(0x8E1A1A), Hex(0x5A1010), 0.3f, 0.1f, 0.05f, 0.2f);
                case Swatch.Grass: return new Cell(Hex(0x5E7A3A), Hex(0x7A8A44), 0.45f, 0.12f, 0.08f, 0.25f);
                case Swatch.Moss: return new Cell(Hex(0x46582E), Hex(0x33402A), 0.5f, 0.12f, 0.05f, 0.15f);
                case Swatch.Bark: return new Cell(Hex(0x3E3024), Hex(0x2A2019), 0.5f, 0.12f, 0.05f, 0.15f);
                case Swatch.Foliage: return new Cell(Hex(0x3C5528), Hex(0x55653A), 0.5f, 0.14f, 0.06f, 0.2f);
                case Swatch.Snow: return new Cell(Hex(0xDCE2EA), Hex(0xC4CCD8), 0.4f, 0.03f, 0.1f, 0.3f);
                case Swatch.Ice: return new Cell(Hex(0x9EC0CC), Hex(0xC8DCE2), 0.45f, 0.04f, 0.6f, 0.8f);
                case Swatch.Bleached: return new Cell(Hex(0xA49C8E), Hex(0x7E766A), 0.45f, 0.1f, 0.08f, 0.2f);
                case Swatch.DryGrass: return new Cell(Hex(0x8A7A52), Hex(0x6A5E40), 0.45f, 0.12f, 0.05f, 0.15f);
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
            float cx = index % Columns, cy = index / Columns;
            return (p, n) =>
            {
                float u = 0.5f + 0.5f * Mathf.Sin(p.x * 23f + p.z * 17f + p.y * 5f);
                float v = 0.5f + 0.5f * Mathf.Sin(p.y * 29f - p.x * 7f + p.z * 11f);
                u = Mathf.Lerp(Margin, 1f - Margin, u);
                v = Mathf.Lerp(Margin, 1f - Margin, v);
                return new Vector2((cx + u) / Columns, (cy + v) / Rows);
            };
        }

        public static Color AlbedoPixel(int x, int y)
        {
            var cell = Define(CellAt(x, y));
            float wear = TextureFactory.Fbm(x / (float)Width, y / (float)Height, 8, 3, 11);
            float mask = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(1f - cell.WearAmount, 1f - cell.WearAmount + 0.25f, wear));
            float grain = (TextureFactory.Hash(x, y, 5) - 0.5f) * cell.Grain;
            var c = Color.Lerp(cell.Base, cell.Wear, mask);
            return new Color(c.r + grain, c.g + grain, c.b + grain, 1f);
        }

        public static Color SpecularPixel(int x, int y)
        {
            var cell = Define(CellAt(x, y));
            float wear = TextureFactory.Fbm(x / (float)Width, y / (float)Height, 8, 3, 11);
            float mask = Mathf.InverseLerp(1f - cell.WearAmount, 1f, wear);
            // Worn/rusted spots are duller.
            float spec = cell.Specular * Mathf.Lerp(1f, 0.35f, mask);
            float smooth = cell.Smoothness * Mathf.Lerp(1f, 0.5f, mask);
            return new Color(spec, spec, spec, smooth);
        }

        private static Swatch CellAt(int x, int y) => (Swatch)(y / CellPixels * Columns + x / CellPixels);

        private static Color Hex(int rgb) =>
            new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, 1f);
    }
}
