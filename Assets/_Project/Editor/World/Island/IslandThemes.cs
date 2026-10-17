using System;
using UnityEngine;

namespace SurvivorSouls.Editor.World.Island
{
    public enum IslandWallStyle
    {
        Arcade,  // gothic piers + pointed arches over a low parapet (sea shows through)
        Ashlar,  // solid dry-laid stone wall with a cap course
        Ruin,    // ashlar with a broken top
    }

    /// <summary>
    /// One island Chamber theme (KAN-245): wall style and heights, tints, light, sea colours and the theme's own dressing
    /// (obstacles + props). Numbers come from the concept generator (Tools/Blender/concepts/chamber_v3.py, THEMES3).
    /// </summary>
    public sealed class IslandThemeSpec
    {
        public string Id, Name;
        public int Seed = 1;

        // Walls (heights in m). South = the camera side: stays low so the Knight shows in front of it.
        public IslandWallStyle Walls = IslandWallStyle.Arcade;
        public float WallHeight = 5f, NorthHeight = 5f, SouthHeight = 0.8f;

        // Vertex tints on the shared Realm materials (SnowFloor / SnowStone / SnowProps). Snow = vertex alpha (0 = never snow).
        public Color FloorTint = Color.white, StoneTint = Color.white, CapTint = Color.white, GateTint = Color.white, FoundationTint = Color.grey;
        public float Snow;
        public float Wet;

        public Vector3 PlayerStart = new Vector3(-11f, 0f, -21.5f);

        // Light
        public Color AmbientSky, AmbientEquator, AmbientGround, FogColor;
        public float FogStart = 42f, FogEnd = 100f;
        public Color SunColor = Color.white;
        public float SunIntensity = 1f;
        public Vector3 SunEuler = new Vector3(18f, 200f, 0f);
        public Color GateLight = new Color(1f, 0.72f, 0.45f, 1f);

        // Sea + pools
        public Color SeaShallow, SeaDeep, SeaCrest, SeaSky, PoolColor;

        /// <summary>Theme obstacles and props (pools, wall runs, cover, set dressing).</summary>
        public Action<IslandChamberBuilder.IslandDresser> Dress;
    }

    /// <summary>The island themes built into the Game scene. KAN-247: Drowned Cathedral; KAN-248 / KAN-249 add the other seven.</summary>
    public static class IslandThemes
    {
        public const string DrownedCathedral = "drowned_cathedral";

        public static readonly IslandThemeSpec[] All =
        {
            new IslandThemeSpec
            {
                Id = DrownedCathedral, Name = "Drowned Cathedral", Seed = 245001,
                Walls = IslandWallStyle.Arcade, WallHeight = 6.5f, NorthHeight = 6.5f, SouthHeight = 0.8f,
                FloorTint = new Color(1.0f, 0.93f, 0.84f), StoneTint = new Color(1.12f, 1.02f, 0.88f), CapTint = new Color(0.85f, 0.78f, 0.7f),
                GateTint = new Color(0.62f, 0.58f, 0.56f), FoundationTint = new Color(0.58f, 0.56f, 0.53f), Snow = 0f, Wet = 0.7f,
                AmbientSky = new Color(0.36f, 0.34f, 0.44f), AmbientEquator = new Color(0.44f, 0.34f, 0.32f), AmbientGround = new Color(0.16f, 0.13f, 0.12f),
                FogColor = new Color(0.42f, 0.34f, 0.34f), FogStart = 42f, FogEnd = 100f,
                SunColor = new Color(1f, 0.66f, 0.44f), SunIntensity = 1.15f, SunEuler = new Vector3(18f, 200f, 0f),
                GateLight = new Color(1f, 0.72f, 0.45f, 1f),
                SeaShallow = new Color(0.1f, 0.19f, 0.23f), SeaDeep = new Color(0.02f, 0.05f, 0.09f), SeaCrest = new Color(0.22f, 0.34f, 0.38f),
                SeaSky = new Color(0.55f, 0.42f, 0.4f), PoolColor = new Color(0.05f, 0.09f, 0.1f),
                Dress = DressCathedral,
            },
        };

        /// <summary>
        /// Drowned Cathedral (concept 01): a red carpet from the SW entry to the NE gate, two stone-edged pools along the aisle,
        /// two blocks of pews either side of the carpet, two ruined wall runs, broken columns as cover, a candle altar under the
        /// north arcade and four braziers.
        /// </summary>
        private static void DressCathedral(IslandChamberBuilder.IslandDresser d)
        {
            var aisle = new Vector2(0.477f, 0.879f);   // SW -> NE
            var across = new Vector2(-0.879f, 0.477f);
            var entry = IslandChamberBuilder.EntryPoint(3f);
            var gate = IslandChamberBuilder.GatePoint(-3f);
            d.Carpet(new Vector2(entry.x, entry.z), new Vector2(gate.x, gate.z), 2.4f);

            d.RectPool(new Vector2(-5.4f, 1.4f), new Vector2(1.7f, 4.6f), 28.5f);
            d.RectPool(new Vector2(5.4f, -1.4f), new Vector2(1.7f, 4.6f), 28.5f);
            d.RuinWall(new Vector2(-14f, 14f), new Vector2(-7f, 14f), 1.6f);
            d.RuinWall(new Vector2(7f, -14f), new Vector2(14f, -14f), 1.6f);
            foreach (float s in new[] { -1f, 1f })
                d.Pews(-aisle * 17f + across * (3f * s), -aisle * 5f + across * (3f * s));

            d.Altar(new Vector2(-4f, IslandChamberBuilder.RZ - 2.6f), 0f);
            foreach (var p in new[] { new Vector2(-14.5f, -6f), new Vector2(14.5f, 4f), new Vector2(-14.5f, 18f), new Vector2(14.5f, -18f) })
                d.Brazier(p);

            int placed = 0;
            foreach (var p in new[]
                     {
                         new Vector2(-12f, 4f), new Vector2(11f, -2f), new Vector2(-3.5f, 13.5f), new Vector2(8.5f, 15f), new Vector2(-12f, 22f),
                         new Vector2(13f, 6.5f), new Vector2(-1.5f, 24f), new Vector2(9f, -16.5f), new Vector2(-9.5f, -6f),
                     })
            {
                if (placed >= 5 || !d.Clear(p, 2f)) continue;
                d.BrokenColumn(p, 1f + (float)d.Rng.NextDouble() * 1.6f);
                placed++;
            }
            d.Rubble(34);
        }
    }
}
