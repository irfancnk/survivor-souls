using System.Collections.Generic;
using System.Linq;
using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.FX;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurvivorSouls.Editor.Arena
{
    /// <summary>
    /// Builds the first level: a ruined courtyard in morning light (Archero-style chamber, portrait).
    /// Layout, south to north (+Z, up the screen):
    ///   1. Courtyard (wide free-move area, player spawns here).
    ///   2. Funnel (walls close in).
    ///   3. Corridor (narrow), ending at a big closed door (opens later, when the room is cleared).
    /// Detail: broken walls, pillars with banners, braziers + wall sconces (warm light), barrels, crates,
    /// fallen pillar, grass tufts, trees and rocks outside the walls, ground mist, rune circle.
    /// Performance rules (mobile, 60 FPS):
    /// - Static geometry is merged per material into a few meshes, split into Z chunks so each chunk gets
    ///   its own nearest 4 lights (URP forward per-object light limit is 4).
    /// - No realtime shadows. No colliders on decor; separate invisible box colliders block movement.
    /// The layout lives in <see cref="Outline"/> and <see cref="HalfWidthAt"/>; camera and spawn read the constants.
    /// </summary>
    public static class ArenaBuilder
    {
        public const float SouthZ = -14f;       // playable Z start (courtyard back wall)
        public const float FunnelStartZ = 3f;   // courtyard ends, walls start closing in
        public const float CorridorStartZ = 9f; // funnel ends
        public const float DoorZ = 17.5f;       // playable Z end (door)
        public const float CourtyardHalfWidth = 5f;
        public const float CorridorHalfWidth = 2f;
        public static readonly Vector3 PlayerSpawn = new Vector3(0f, 0f, -9.5f);

        private const string MeshDir = "Assets/_Project/Art/Arena";
        private const float WallOffset = 0.6f;  // wall centre line sits this far outside the playable edge
        private static readonly float[] ChunkEdges = { -6f, 3f, 10f }; // 4 chunks along Z
        private static readonly Color TorchColor = new Color(1f, 0.6f, 0.3f);

        /// <summary>Playable half width at <paramref name="z"/>.</summary>
        public static float HalfWidthAt(float z)
        {
            if (z <= FunnelStartZ) return CourtyardHalfWidth;
            if (z >= CorridorStartZ) return CorridorHalfWidth;
            return Mathf.Lerp(CourtyardHalfWidth, CorridorHalfWidth, Mathf.InverseLerp(FunnelStartZ, CorridorStartZ, z));
        }

        /// <summary>Wall centre line, one side (x &gt; 0), south to north. Mirror for the left side.</summary>
        private static readonly Vector2[] Outline =
        {
            new Vector2(CourtyardHalfWidth + WallOffset, SouthZ - WallOffset),
            new Vector2(CourtyardHalfWidth + WallOffset, FunnelStartZ),
            new Vector2(CorridorHalfWidth + WallOffset, CorridorStartZ),
            new Vector2(CorridorHalfWidth + WallOffset, DoorZ + WallOffset),
        };

        private sealed class Chunked
        {
            public readonly MeshBuilder[] Parts = { new MeshBuilder(), new MeshBuilder(), new MeshBuilder(), new MeshBuilder() };
            public MeshBuilder At(float z)
            {
                int i = 0;
                while (i < ChunkEdges.Length && z >= ChunkEdges[i]) i++;
                return Parts[i];
            }
        }

        private sealed class Layout
        {
            public readonly Chunked Floor = new Chunked(), Walls = new Chunked(), Props = new Chunked();
            public readonly List<(Vector3 centre, Vector3 size, Quaternion rot)> Colliders = new List<(Vector3, Vector3, Quaternion)>();
            public readonly List<Vector3> Braziers = new List<Vector3>();
            public readonly List<(Vector3 pos, float scale)> Sconces = new List<(Vector3, float)>();
            public readonly System.Random Rng = new System.Random(1337); // fixed seed: same layout every rebuild
        }

        public static void Build(Transform parent, GameArtKit kit)
        {
            var root = new GameObject("Arena").transform;
            root.SetParent(parent, false);
            var l = new Layout();

            BuildFloor(l);
            BuildSideWalls(l);
            BuildEndWalls(l);
            BuildDoor(l);
            BuildPillarsAndBanners(l);
            BuildBraziers(l);
            BuildSconces(l);
            BuildClutter(l);
            BuildGrass(l);
            BuildOutside(l);
            BuildRubble(l);

            Emit(root, "Floor", l.Floor, kit.Floor);
            Emit(root, "Walls", l.Walls, kit.Wall);
            Emit(root, "Props", l.Props, kit.Palette);
            BuildOuterGround(root, kit);
            BuildRuneDecal(root, kit);
            foreach (var p in l.Braziers) BuildFire(root, p + Vector3.up * 1.05f, kit, 1f, 7f, 9f);
            foreach (var (p, s) in l.Sconces) BuildFire(root, p, kit, s, 4.5f, 6.5f);
            BuildMist(root, kit);
            BuildColliders(root, l.Colliders);
        }

        // ------------------------------------------------------------ UV mappers

        private static MeshBuilder.UvMapper StoneUv(float tile) => (p, n) =>
        {
            var a = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
            if (a.y >= a.x && a.y >= a.z) return new Vector2(p.x, p.z) / tile;
            return a.x >= a.z ? new Vector2(p.z, p.y) / tile : new Vector2(p.x, p.y) / tile;
        };

        private static MeshBuilder.UvMapper Pal(Swatch s) => PaletteAtlas.For(s);

        // ------------------------------------------------------------ Floor + walls

        private static void BuildFloor(Layout l)
        {
            // Top surface strips that follow the outline (reaching under the walls), 1 m steps.
            var uv = StoneUv(4f);
            for (float z = SouthZ - 1.2f; z < DoorZ + 1f; z += 1f)
            {
                float z1 = Mathf.Min(z + 1f, DoorZ + 1f);
                float w0 = HalfWidthAt(z) + WallOffset + 0.5f, w1 = HalfWidthAt(z1) + WallOffset + 0.5f;
                l.Floor.At((z + z1) * 0.5f).Quad(new Vector3(-w0, 0f, z), new Vector3(-w1, 0f, z1), new Vector3(w1, 0f, z1), new Vector3(w0, 0f, z), uv, 0);
            }
        }

        private static void BuildSideWalls(Layout l)
        {
            var uv = StoneUv(2f);
            foreach (float side in new[] { -1f, 1f })
            {
                for (int s = 0; s < Outline.Length - 1; s++)
                {
                    var a = new Vector3(Outline[s].x * side, 0f, Outline[s].y);
                    var b = new Vector3(Outline[s + 1].x * side, 0f, Outline[s + 1].y);
                    var dir = (b - a).normalized;
                    float length = (b - a).magnitude;
                    var rot = Quaternion.LookRotation(dir, Vector3.up);
                    bool courtyard = s == 0;

                    // Collider: one box per section, inner face on the playable edge.
                    l.Colliders.Add(((a + b) * 0.5f + Vector3.up * 1.5f, new Vector3(WallOffset * 2f, 3f, length + 1f), rot));

                    float t = 0f;
                    while (t < length - 0.05f)
                    {
                        float seg = Mathf.Min(Range(l.Rng, 1.4f, 3f), length - t);
                        bool gap = courtyard && l.Rng.NextDouble() < 0.18;
                        float h = gap ? Range(l.Rng, 0.15f, 0.35f) : courtyard ? Range(l.Rng, 0.7f, 1.5f) : Range(l.Rng, 1.7f, 2.8f);
                        var centre = a + dir * (t + seg * 0.5f) + Vector3.up * (h * 0.5f);
                        var size = new Vector3(0.8f, h, seg + 0.06f);
                        var segRot = rot * Quaternion.Euler(0f, Range(l.Rng, -1.2f, 1.2f), 0f);
                        l.Walls.At(centre.z).Box(centre, size, segRot, uv, 0);
                        if (!gap && h > 1f) AddCapStones(l, centre, size, segRot);
                        t += seg;
                    }
                }
            }
        }

        private static void BuildEndWalls(Layout l)
        {
            var uv = StoneUv(2f);
            // South (bottom of screen): very low, so it never hides the player.
            float x = -(CourtyardHalfWidth + WallOffset);
            float zSouth = SouthZ - WallOffset;
            while (x < CourtyardHalfWidth + WallOffset)
            {
                float xEnd = Mathf.Min(x + Range(l.Rng, 1.3f, 2.6f), CourtyardHalfWidth + WallOffset);
                float h = Range(l.Rng, 0.25f, 0.55f);
                l.Walls.At(zSouth).Box(new Vector3((x + xEnd) * 0.5f, h * 0.5f, zSouth), new Vector3(xEnd - x + 0.04f, h, 0.8f), Quaternion.identity, uv, 0);
                x = xEnd;
            }
            l.Colliders.Add((new Vector3(0f, 1.5f, zSouth), new Vector3(CourtyardHalfWidth * 2f + 3f, 3f, WallOffset * 2f), Quaternion.identity));

            // North: tall wall around the door.
            float zNorth = DoorZ + WallOffset;
            float edge = CorridorHalfWidth + WallOffset + 0.4f;
            foreach (float side in new[] { -1f, 1f })
            {
                float inner = 1.45f;
                var c = new Vector3(side * (inner + edge) * 0.5f, 1.7f, zNorth);
                l.Walls.At(zNorth).Box(c, new Vector3(edge - inner, 3.4f, 0.9f), Quaternion.identity, uv, 0);
            }
            l.Colliders.Add((new Vector3(0f, 1.5f, zNorth), new Vector3(edge * 2f + 1f, 3f, WallOffset * 2f), Quaternion.identity));
        }

        private static void AddCapStones(Layout l, Vector3 centre, Vector3 size, Quaternion rot)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(size.z / 0.9f));
            for (int i = 0; i < count; i++)
            {
                if (l.Rng.NextDouble() < 0.35) continue;
                float t = (i + 0.5f) / count - 0.5f;
                var pos = centre + rot * new Vector3(Range(l.Rng, -0.05f, 0.05f), size.y * 0.5f + 0.1f, t * size.z);
                l.Walls.At(pos.z).Box(pos, new Vector3(0.7f, 0.2f, Range(l.Rng, 0.5f, 0.85f)),
                    rot * Quaternion.Euler(Range(l.Rng, -4f, 4f), Range(l.Rng, -6f, 6f), Range(l.Rng, -4f, 4f)), StoneUv(2f), 0);
            }
        }

        /// <summary>Stone arch with a closed double door (wood planks, iron bands, studs, ring handles).</summary>
        private static void BuildDoor(Layout l)
        {
            var uv = StoneUv(2f);
            float z = DoorZ + WallOffset;
            var walls = l.Walls.At(z);
            foreach (float side in new[] { -1f, 1f })
            {
                walls.Box(new Vector3(side * 1.3f, 1.8f, z - 0.05f), new Vector3(0.45f, 3.6f, 1.1f), Quaternion.identity, uv, 0); // jamb
                walls.Box(new Vector3(side * 1.3f, 0.15f, z - 0.05f), new Vector3(0.65f, 0.3f, 1.25f), Quaternion.identity, uv, 0); // base
            }
            walls.Box(new Vector3(0f, 3.75f, z - 0.05f), new Vector3(3.3f, 0.55f, 1.15f), Quaternion.identity, uv, 0);          // lintel
            walls.Box(new Vector3(0f, 3.75f, z - 0.66f), new Vector3(0.5f, 0.75f, 0.12f), Quaternion.identity, uv, 0);          // keystone
            walls.Box(new Vector3(0f, 0.06f, z - 0.95f), new Vector3(3.0f, 0.12f, 0.7f), Quaternion.identity, uv, 0);           // step

            var props = l.Props.At(z);
            float doorZ = z - 0.15f;
            foreach (float side in new[] { -1f, 1f })
            {
                // Planks: slightly uneven heights and tones.
                for (int i = 0; i < 4; i++)
                {
                    float px = side * (0.13f + i * 0.27f);
                    float h = 3.3f - Range(l.Rng, 0f, 0.08f);
                    props.Box(new Vector3(px, h * 0.5f, doorZ), new Vector3(0.26f, h, 0.14f), Quaternion.identity, Pal(i % 2 == 0 ? Swatch.Wood : Swatch.Bark), 0);
                }
                foreach (float y in new[] { 0.6f, 1.75f, 2.9f })
                {
                    props.Box(new Vector3(side * 0.55f, y, doorZ - 0.09f), new Vector3(1.05f, 0.12f, 0.04f), Quaternion.identity, Pal(Swatch.Gunmetal), 0);
                    for (int k = 0; k < 4; k++)
                        props.Ellipsoid(new Vector3(side * (0.15f + k * 0.27f), y, doorZ - 0.115f), Vector3.one * 0.025f, Quaternion.identity, Pal(Swatch.BrightMetal), 0, 6, 4);
                }
                props.Lathe(new Vector3(side * 0.22f, 1.5f, doorZ - 0.12f), Quaternion.Euler(90f, 0f, 0f),
                    new[] { new Vector2(0f, 0.09f), new Vector2(0.025f, 0.09f) }, Pal(Swatch.Gunmetal), 0, 10);       // ring handle
            }
        }

        private static void BuildPillarsAndBanners(Layout l)
        {
            var uv = StoneUv(2f);
            float cx = CourtyardHalfWidth + WallOffset, kx = CorridorHalfWidth + WallOffset;
            var spots = new List<(Vector3 pos, float h, bool banner)>
            {
                (new Vector3(-cx, 0f, SouthZ - WallOffset), 1.2f, false), (new Vector3(cx, 0f, SouthZ - WallOffset), 0.9f, false),
                (new Vector3(-cx, 0f, -9.5f), 3.4f, true), (new Vector3(cx, 0f, -9.5f), 3.2f, true),
                (new Vector3(-cx, 0f, -3f), 2.2f, false), (new Vector3(cx, 0f, -3f), 3.4f, true),
                (new Vector3(-cx, 0f, FunnelStartZ), 3.6f, true), (new Vector3(cx, 0f, FunnelStartZ), 2.6f, false),
                (new Vector3(-kx, 0f, CorridorStartZ), 3.8f, false), (new Vector3(kx, 0f, CorridorStartZ), 3.8f, false),
            };
            foreach (var (pos, h, banner) in spots)
            {
                var rot = Quaternion.Euler(0f, Range(l.Rng, -6f, 6f), 0f);
                var w = l.Walls.At(pos.z);
                w.Box(pos + new Vector3(0f, 0.12f, 0f), new Vector3(1.25f, 0.24f, 1.25f), rot, uv, 0);
                w.Box(pos + new Vector3(0f, h * 0.5f, 0f), new Vector3(0.95f, h, 0.95f), rot, uv, 0);
                if (h > 2.5f) w.Box(pos + new Vector3(0f, h + 0.14f, 0f), new Vector3(1.2f, 0.28f, 1.2f), rot, uv, 0);
                else w.Box(pos + new Vector3(Range(l.Rng, -0.1f, 0.1f), h + 0.12f, 0f), new Vector3(0.8f, 0.3f, 0.7f),
                    rot * Quaternion.Euler(Range(l.Rng, -12f, 12f), 0f, Range(l.Rng, -15f, 15f)), uv, 0);
                if (banner) AddBanner(l, pos, h);
            }
        }

        /// <summary>Crimson banner on the inner face of a pillar: iron rod, ragged cloth, gold-ish trim.</summary>
        private static void AddBanner(Layout l, Vector3 pillar, float h)
        {
            float inward = -Mathf.Sign(pillar.x);
            var props = l.Props.At(pillar.z);
            float x = pillar.x + inward * 0.5f;
            float top = h - 0.25f;
            props.Box(new Vector3(x, top, pillar.z), new Vector3(0.06f, 0.06f, 0.95f), Quaternion.identity, Pal(Swatch.Gunmetal), 0);
            float len = Mathf.Min(1.9f, h - 0.6f);
            props.Box(new Vector3(x + inward * 0.02f, top - len * 0.5f, pillar.z), new Vector3(0.03f, len, 0.78f), Quaternion.identity, Pal(Swatch.Crimson), 0);
            props.Box(new Vector3(x + inward * 0.035f, top - len * 0.45f, pillar.z), new Vector3(0.02f, 0.32f, 0.32f), Quaternion.Euler(45f, 0f, 0f), Pal(Swatch.BrightMetal), 0); // emblem
            for (int i = 0; i < 3; i++) // ragged tail
                props.Box(new Vector3(x + inward * 0.02f, top - len - 0.1f, pillar.z - 0.26f + i * 0.26f), new Vector3(0.03f, 0.22f - i % 2 * 0.08f, 0.2f), Quaternion.identity, Pal(Swatch.Crimson), 0);
        }

        // ------------------------------------------------------------ Fire

        private static void BuildBraziers(Layout l)
        {
            foreach (float z in new[] { -10.5f, -2f })
            foreach (float side in new[] { -1f, 1f })
                l.Braziers.Add(new Vector3(side * 4.3f, 0f, z));

            foreach (var p in l.Braziers)
            {
                var mb = l.Props.At(p.z);
                mb.Lathe(p, Quaternion.identity, new[] { new Vector2(0f, 0.32f), new Vector2(0.12f, 0.3f), new Vector2(0.18f, 0.16f), new Vector2(0.75f, 0.12f), new Vector2(0.82f, 0.18f) },
                    Pal(Swatch.Stone), 0, 10, capTop: true);
                mb.Lathe(p + Vector3.up * 0.8f, Quaternion.identity, new[] { new Vector2(0f, 0.16f), new Vector2(0.12f, 0.34f), new Vector2(0.22f, 0.42f), new Vector2(0.26f, 0.44f) },
                    Pal(Swatch.Gunmetal), 0, 12);
                mb.Lathe(p + Vector3.up * 0.98f, Quaternion.identity, new[] { new Vector2(0f, 0.38f), new Vector2(0.04f, 0.3f), new Vector2(0.08f, 0.12f) },
                    Pal(Swatch.Coal), 0, 10, capTop: true);
                l.Colliders.Add((p + Vector3.up, new Vector3(0.8f, 2f, 0.8f), Quaternion.identity));
            }
        }

        /// <summary>Wall torches in the corridor and beside the door. Flame position is stored for the fire FX.</summary>
        private static void BuildSconces(Layout l)
        {
            float wx = CorridorHalfWidth + WallOffset - 0.42f;
            var spots = new List<(Vector3 pos, float inward)>
            {
                (new Vector3(-wx, 1.9f, 12.5f), 1f), (new Vector3(wx, 1.9f, 14.5f), -1f),
                (new Vector3(-1.62f, 2.3f, DoorZ + 0.05f), 0f), (new Vector3(1.62f, 2.3f, DoorZ + 0.05f), 0f),
            };
            foreach (var (pos, inward) in spots)
            {
                var mb = l.Props.At(pos.z);
                var dir = inward != 0f ? new Vector3(inward, 0f, 0f) : Vector3.back;
                var bowl = pos + dir * 0.28f;
                mb.Box(pos + dir * 0.14f + Vector3.down * 0.12f, new Vector3(Mathf.Abs(dir.x) * 0.28f + 0.05f, 0.05f, Mathf.Abs(dir.z) * 0.28f + 0.05f),
                    Quaternion.Euler(dir.z * 25f, 0f, -dir.x * 25f), Pal(Swatch.Gunmetal), 0);
                mb.Lathe(bowl + Vector3.down * 0.1f, Quaternion.identity, new[] { new Vector2(0f, 0.04f), new Vector2(0.08f, 0.14f), new Vector2(0.11f, 0.15f) }, Pal(Swatch.Gunmetal), 0, 10);
                mb.Lathe(bowl, Quaternion.identity, new[] { new Vector2(0f, 0.13f), new Vector2(0.02f, 0.06f) }, Pal(Swatch.Coal), 0, 8, capTop: true);
                l.Sconces.Add((bowl + Vector3.up * 0.04f, 0.6f));
            }
        }

        // ------------------------------------------------------------ Detail

        private static void BuildClutter(Layout l)
        {
            // Barrel and crate clusters in courtyard corners and at the funnel mouth (never mid-floor).
            (Vector3 pos, int barrels, int crates)[] clusters =
            {
                (new Vector3(-4.2f, 0f, -12.9f), 2, 1), (new Vector3(4.1f, 0f, -12.7f), 1, 2),
                (new Vector3(-4.3f, 0f, 1.9f), 2, 1), (new Vector3(4.3f, 0f, 1.6f), 1, 1),
                (new Vector3(1.45f, 0f, 16.4f), 1, 0),
            };
            foreach (var (pos, barrels, crates) in clusters)
            {
                var mb = l.Props.At(pos.z);
                for (int i = 0; i < barrels; i++)
                {
                    var p = pos + new Vector3(Range(l.Rng, -0.5f, 0.5f), 0f, i * 0.75f * (pos.z > 0 ? -1f : 1f));
                    AddBarrel(mb, p, Range(l.Rng, 0f, 360f));
                    l.Colliders.Add((p + Vector3.up * 0.5f, new Vector3(0.75f, 1f, 0.75f), Quaternion.identity));
                }
                for (int i = 0; i < crates; i++)
                {
                    var p = pos + new Vector3(Mathf.Sign(-pos.x) * (0.85f + i * 0.2f), 0f, Range(l.Rng, -0.3f, 0.3f) + i * 0.8f);
                    float yaw = Range(l.Rng, -20f, 20f);
                    AddCrate(mb, p, 0.75f, yaw);
                    l.Colliders.Add((p + Vector3.up * 0.375f, Vector3.one * 0.8f, Quaternion.Euler(0f, yaw, 0f)));
                    if (i == 0 && crates > 1) AddCrate(mb, p + Vector3.up * 0.75f, 0.55f, yaw + 25f);
                }
            }

            // Fallen pillar drums along the left courtyard wall.
            var drums = l.Props.At(-6f);
            for (int i = 0; i < 3; i++)
            {
                var c = new Vector3(-4.35f + i * 0.15f, 0.42f, -7.2f + i * 0.92f);
                drums.Lathe(c + new Vector3(0f, 0f, -0.42f), Quaternion.Euler(90f, Range(l.Rng, -8f, 8f), 0f),
                    new[] { new Vector2(0f, 0.42f), new Vector2(0.84f, 0.42f) }, Pal(Swatch.Stone), 0, 12, capBottom: true, capTop: true);
            }
            l.Colliders.Add((new Vector3(-4.2f, 0.45f, -6.3f), new Vector3(0.9f, 0.9f, 2.8f), Quaternion.identity));
        }

        private static void AddBarrel(MeshBuilder mb, Vector3 p, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            mb.Lathe(p, rot, new[] { new Vector2(0f, 0.3f), new Vector2(0.25f, 0.35f), new Vector2(0.5f, 0.37f), new Vector2(0.75f, 0.35f), new Vector2(0.95f, 0.3f) },
                Pal(Swatch.Wood), 0, 12, capTop: true);
            foreach (float y in new[] { 0.14f, 0.8f })
                mb.Lathe(p + Vector3.up * y, rot, new[] { new Vector2(0f, y < 0.5f ? 0.33f : 0.335f), new Vector2(0.06f, y < 0.5f ? 0.335f : 0.33f) }, Pal(Swatch.Gunmetal), 0, 12);
        }

        private static void AddCrate(MeshBuilder mb, Vector3 p, float s, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            mb.Box(p + Vector3.up * s * 0.5f, Vector3.one * s, rot, Pal(Swatch.Wood), 0);
            // Edge planks.
            float e = s * 0.5f;
            foreach (float a in new[] { -1f, 1f })
            foreach (float b in new[] { -1f, 1f })
                mb.Box(p + rot * new Vector3(a * e, e, b * e), new Vector3(0.07f, s + 0.02f, 0.07f), rot, Pal(Swatch.Bark), 0);
            mb.Box(p + Vector3.up * s * 0.5f + rot * new Vector3(0f, 0f, -e - 0.01f), new Vector3(s * 1.1f, 0.07f, 0.03f), rot * Quaternion.Euler(0f, 0f, 45f), Pal(Swatch.Bark), 0);
        }

        /// <summary>Grass tufts along wall bases and in a few floor cracks: thin tapered blades, palette green.</summary>
        private static void BuildGrass(Layout l)
        {
            for (int i = 0; i < 70; i++)
            {
                float z = Range(l.Rng, SouthZ + 0.3f, DoorZ - 0.5f);
                float hw = HalfWidthAt(z);
                bool edge = l.Rng.NextDouble() < 0.8;
                float x = edge ? (l.Rng.NextDouble() < 0.5 ? -1f : 1f) * Range(l.Rng, hw - 0.5f, hw + 0.1f) : Range(l.Rng, -hw + 1f, hw - 1f);
                AddTuft(l.Props.At(z), new Vector3(x, 0f, z), l.Rng, Range(l.Rng, 0.2f, edge ? 0.45f : 0.25f));
            }
        }

        private static void AddTuft(MeshBuilder mb, Vector3 p, System.Random rng, float height)
        {
            int blades = 5 + rng.Next(4);
            var swatch = rng.NextDouble() < 0.3 ? Swatch.Moss : Swatch.Grass;
            for (int b = 0; b < blades; b++)
            {
                float a = Range(rng, 0f, Mathf.PI * 2f);
                var lean = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Range(rng, 0.05f, 0.16f);
                var basePos = p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * Range(rng, 0f, 0.07f);
                float h = height * Range(rng, 0.6f, 1.1f);
                mb.Tube(new[] { basePos, basePos + lean * 0.4f + Vector3.up * h * 0.55f, basePos + lean + Vector3.up * h },
                    t => Mathf.Lerp(0.018f, 0.002f, t), Pal(swatch), 0, 3, caps: false);
            }
        }

        /// <summary>Trees, bushes and rocks outside the walls, so the narrow part sits in a landscape, not a void.</summary>
        private static void BuildOutside(Layout l)
        {
            for (int i = 0; i < 34; i++)
            {
                float z = Range(l.Rng, SouthZ - 2f, DoorZ + 8f);
                float wall = HalfWidthAt(Mathf.Clamp(z, SouthZ, DoorZ)) + WallOffset;
                float side = l.Rng.NextDouble() < 0.5 ? -1f : 1f;
                var p = new Vector3(side * (wall + Range(l.Rng, 1.4f, 5.5f)), 0f, z);
                var mb = l.Props.At(z);
                double kind = l.Rng.NextDouble();
                if (kind < 0.45) AddTree(mb, p, l.Rng);
                else if (kind < 0.75) AddBush(mb, p, l.Rng);
                else mb.Ellipsoid(p + Vector3.up * 0.15f, new Vector3(Range(l.Rng, 0.4f, 0.9f), Range(l.Rng, 0.3f, 0.6f), Range(l.Rng, 0.4f, 0.8f)),
                    Quaternion.Euler(0f, Range(l.Rng, 0f, 360f), Range(l.Rng, -10f, 10f)), Pal(Swatch.Stone), 0, 8, 5);
            }
        }

        private static void AddTree(MeshBuilder mb, Vector3 p, System.Random rng)
        {
            float h = Range(rng, 2.6f, 4.2f);
            mb.Lathe(p, Quaternion.Euler(Range(rng, -4f, 4f), 0f, Range(rng, -4f, 4f)),
                new[] { new Vector2(0f, 0.26f), new Vector2(0.3f, 0.17f), new Vector2(h, 0.09f) }, Pal(Swatch.Bark), 0, 8);
            for (int k = 0; k < 4; k++)
            {
                var c = p + new Vector3(Range(rng, -0.6f, 0.6f), h - 0.2f + Range(rng, -0.3f, 0.6f), Range(rng, -0.6f, 0.6f));
                float r = Range(rng, 0.8f, 1.3f);
                mb.Ellipsoid(c, new Vector3(r, r * 0.8f, r), Quaternion.Euler(0f, Range(rng, 0f, 360f), 0f), Pal(Swatch.Foliage), 0, 9, 6);
            }
        }

        private static void AddBush(MeshBuilder mb, Vector3 p, System.Random rng)
        {
            for (int k = 0; k < 3; k++)
            {
                var c = p + new Vector3(Range(rng, -0.4f, 0.4f), Range(rng, 0.25f, 0.45f), Range(rng, -0.4f, 0.4f));
                float r = Range(rng, 0.4f, 0.65f);
                mb.Ellipsoid(c, new Vector3(r, r * 0.75f, r), Quaternion.identity, Pal(k == 0 ? Swatch.Moss : Swatch.Foliage), 0, 8, 5);
            }
        }

        private static void BuildRubble(Layout l)
        {
            var uv = StoneUv(1.5f);
            for (int i = 0; i < 30; i++)
            {
                float z = Range(l.Rng, SouthZ + 0.2f, DoorZ - 1f);
                float hw = HalfWidthAt(z);
                var pos = new Vector3((l.Rng.NextDouble() < 0.5 ? -1f : 1f) * Range(l.Rng, hw - 0.4f, hw + 0.1f), 0f, z);
                float s = Range(l.Rng, 0.12f, 0.4f);
                pos.y = s * 0.3f;
                l.Walls.At(pos.z).Box(pos, new Vector3(s * Range(l.Rng, 1f, 1.8f), s, s * Range(l.Rng, 0.8f, 1.4f)),
                    Quaternion.Euler(Range(l.Rng, -15f, 15f), Range(l.Rng, 0f, 360f), Range(l.Rng, -15f, 15f)), uv, 0);
            }
        }

        // ------------------------------------------------------------ Scene objects

        private static void Emit(Transform root, string name, Chunked chunked, Material mat)
        {
            for (int i = 0; i < chunked.Parts.Length; i++)
            {
                var mb = chunked.Parts[i];
                if (mb.VertexCount == 0) continue;
                var mesh = EditorAssetUtil.LoadOrCreateMesh($"{MeshDir}/Arena_{name}_{i}.asset");
                mb.WriteTo(mesh);
                EditorUtility.SetDirty(mesh);
                var go = new GameObject($"{name}_{i}");
                go.transform.SetParent(root, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                SetupRenderer(go.AddComponent<MeshRenderer>(), mat);
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            }
        }

        private static void BuildOuterGround(Transform root, GameArtKit kit)
        {
            var go = new GameObject("OuterGround");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(new Vector3(0f, -0.12f, 0f), Quaternion.Euler(90f, 0f, 0f));
            go.transform.localScale = new Vector3(60f, 80f, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = kit.Quad;
            SetupRenderer(go.AddComponent<MeshRenderer>(), kit.OuterGround);
            // Quad UVs are 0..1; scale the material tiling instead of the mesh.
            kit.OuterGround.SetTextureScale("_BaseMap", new Vector2(15f, 20f));
            kit.OuterGround.SetTextureScale("_BumpMap", new Vector2(15f, 20f));
            EditorUtility.SetDirty(kit.OuterGround);
        }

        private static void BuildRuneDecal(Transform root, GameArtKit kit)
        {
            var go = new GameObject("RuneCircle");
            go.transform.SetParent(root, false);
            go.transform.SetPositionAndRotation(new Vector3(0f, 0.01f, -4.5f), Quaternion.Euler(90f, 0f, 0f));
            go.transform.localScale = new Vector3(6.5f, 6.5f, 1f);
            go.AddComponent<MeshFilter>().sharedMesh = kit.Quad;
            SetupRenderer(go.AddComponent<MeshRenderer>(), kit.RuneDecal);
        }

        /// <summary>Flames, embers and a flickering point light at <paramref name="flameBase"/>. Scale shrinks the FX for wall sconces.</summary>
        private static void BuildFire(Transform root, Vector3 flameBase, GameArtKit kit, float scale, float intensity, float range)
        {
            var go = new GameObject("Fire");
            go.transform.SetParent(root, false);
            go.transform.position = flameBase;
            go.transform.localScale = Vector3.one * scale;

            var flames = Particles("Flames", go.transform, kit.Flame);
            var main = flames.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 1.3f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.3f, 0.6f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.62f, 0.22f), new Color(1f, 0.38f, 0.1f));
            main.maxParticles = 30;
            var emission = flames.emission;
            emission.rateOverTime = 26f;
            var shape = flames.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 10f;
            shape.radius = 0.18f;
            var col = flames.colorOverLifetime;
            col.enabled = true;
            col.color = Gradient(new[] { (new Color(1f, 0.9f, 0.6f), 0f), (new Color(1f, 0.5f, 0.15f), 0.4f), (new Color(0.6f, 0.12f, 0.04f), 1f) },
                new[] { (0f, 0f), (0.85f, 0.12f), (0.5f, 0.5f), (0f, 1f) });
            var size = flames.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.3f));

            var embers = Particles("Embers", go.transform, kit.Ember);
            main = embers.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.4f, 1f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.8f, 0.35f), new Color(1f, 0.45f, 0.12f));
            main.maxParticles = 16;
            emission = embers.emission;
            emission.rateOverTime = 7f;
            shape = embers.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 20f;
            shape.radius = 0.2f;
            var noise = embers.noise;
            noise.enabled = true;
            noise.strength = 0.4f;
            noise.frequency = 0.6f;
            noise.quality = ParticleSystemNoiseQuality.Low;
            col = embers.colorOverLifetime;
            col.enabled = true;
            col.color = Gradient(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (0f, 0f), (1f, 0.1f), (0.8f, 0.7f), (0f, 1f) });

            var lightGo = new GameObject("Torch Light");
            lightGo.transform.SetParent(go.transform, false);
            lightGo.transform.localPosition = new Vector3(0f, 0.5f, 0f);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = TorchColor;
            light.shadows = LightShadows.None;
            lightGo.AddComponent<LightFlicker>().Configure(intensity, intensity * 0.14f, range, range * 0.06f);
            light.intensity = intensity;
            light.range = range;
        }

        private static void BuildMist(Transform root, GameArtKit kit)
        {
            var ps = Particles("Ground Mist", root, kit.Mist);
            ps.transform.position = new Vector3(0f, 0.4f, 0f);
            ps.transform.rotation = Quaternion.identity;
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 16f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.05f, 0.2f);
            main.startSize = new ParticleSystem.MinMaxCurve(4f, 7f);
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.9f, 0.92f, 0.95f, 0.12f), new Color(1f, 0.96f, 0.9f, 0.2f)); // morning haze
            main.maxParticles = 40;
            var emission = ps.emission;
            emission.rateOverTime = 3.2f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            ps.transform.position = new Vector3(0f, 0.4f, (SouthZ + DoorZ) * 0.5f);
            shape.scale = new Vector3(CourtyardHalfWidth * 2.4f, 0.4f, DoorZ - SouthZ + 2f);
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = Gradient(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (0f, 0f), (1f, 0.3f), (1f, 0.7f), (0f, 1f) });
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0f);
            vel.z = new ParticleSystem.MinMaxCurve(-0.05f, 0.05f);
            // Lie flat: camera-facing billboards would cut into the floor with a hard edge.
            ps.GetComponent<ParticleSystemRenderer>().renderMode = ParticleSystemRenderMode.HorizontalBillboard;
        }

        private static void BuildColliders(Transform root, List<(Vector3 centre, Vector3 size, Quaternion rot)> colliders)
        {
            var parent = new GameObject("Colliders").transform;
            parent.SetParent(root, false);
            for (int i = 0; i < colliders.Count; i++)
            {
                var go = new GameObject($"Block_{i}");
                go.transform.SetParent(parent, false);
                go.transform.SetPositionAndRotation(colliders[i].centre, colliders[i].rot);
                go.AddComponent<BoxCollider>().size = colliders[i].size;
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
            }
            // Floor collider so the CharacterController stands on y = 0.
            var floor = new GameObject("Floor Collider");
            floor.transform.SetParent(parent, false);
            floor.transform.position = new Vector3(0f, -0.5f, (SouthZ + DoorZ) * 0.5f);
            floor.AddComponent<BoxCollider>().size = new Vector3(CourtyardHalfWidth * 2f + 4f, 1f, DoorZ - SouthZ + 4f);
        }

        // ------------------------------------------------------------ Helpers

        private static void SetupRenderer(Renderer r, Material mat)
        {
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        private static ParticleSystem Particles(string name, Transform parent, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // cone emits along +Z; point it up
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.prewarm = true;
            main.loop = true;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy; // parent scale shrinks sconce flames
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        private static Gradient Gradient((Color c, float t)[] colors, (float a, float t)[] alphas)
        {
            var g = new Gradient();
            g.SetKeys(colors.Select(k => new GradientColorKey(k.c, k.t)).ToArray(), alphas.Select(k => new GradientAlphaKey(k.a, k.t)).ToArray());
            return g;
        }

        private static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);
    }
}
