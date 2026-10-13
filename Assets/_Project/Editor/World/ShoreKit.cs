using System.Collections.Generic;
using SurvivorSouls.Editor.Procedural;
using UnityEngine;
using static SurvivorSouls.Editor.World.PropKit;

namespace SurvivorSouls.Editor.World
{
    /// <summary>
    /// Realm 1 props (snowy seaside shore), all procedural low-poly geometry written into MeshBuilders.
    /// Props use the palette atlas; the SnowCapLit material adds snow on every up-facing surface, so no prop
    /// needs its own snow mesh. <see cref="MeshBuilder.Tint"/> alpha = 0 keeps a part snow-free (ice, fire bowls).
    /// Sizes in metres, y = 0 is the play floor, the sea surface is at <see cref="SeaLevel"/>.
    /// </summary>
    public static class ShoreKit
    {
        public const float SeaLevel = -0.35f;
        private static readonly Color DarkRock = new Color(0.62f, 0.64f, 0.7f);
        private static readonly Color NoSnow = new Color(1f, 1f, 1f, 0f);
        private static readonly Color Patchy = new Color(1f, 1f, 1f, 0.5f); // sea-wet or wind-swept wood: snow in patches

        private static void WithTint(MeshBuilder mb, Color tint, System.Action draw)
        {
            var old = mb.Tint;
            mb.Tint = tint;
            draw();
            mb.Tint = old;
        }

        // ------------------------------------------------------------ Terrain pieces

        /// <summary>Lumpy rock: a main ellipsoid plus one or two side lumps. Size = rough radius.</summary>
        public static void Boulder(MeshBuilder mb, Vector3 p, System.Random rng, float size)
        {
            var tint = DarkRock * Range(rng, 0.85f, 1.05f);
            tint.a = 0.6f; // snow in patches: rocks stay dark enough to read as rock
            WithTint(mb, tint, () =>
            {
                var rot = Quaternion.Euler(Range(rng, -10f, 10f), Range(rng, 0f, 360f), Range(rng, -10f, 10f));
                mb.Ellipsoid(p + Vector3.up * size * 0.3f, new Vector3(size, size * Range(rng, 0.6f, 0.85f), size * Range(rng, 0.7f, 0.95f)), rot, Pal(Swatch.Stone), 0, 9, 6);
                int lumps = 1 + rng.Next(2);
                for (int i = 0; i < lumps; i++)
                {
                    float s = size * Range(rng, 0.45f, 0.7f);
                    var off = rot * new Vector3(Range(rng, -0.8f, 0.8f) * size, 0f, Range(rng, -0.8f, 0.8f) * size);
                    mb.Ellipsoid(p + off + Vector3.up * s * 0.2f, new Vector3(s, s * 0.7f, s * 0.85f),
                        Quaternion.Euler(0f, Range(rng, 0f, 360f), Range(rng, -12f, 12f)), Pal(Swatch.Stone), 0, 8, 5);
                }
            });
        }

        /// <summary>
        /// Cliff / rock outcrop module along +X of yaw: overlapping, flattened rock masses that rise to the middle,
        /// like weathered strata. Snow settles on the ledges (material snow cap).
        /// </summary>
        public static void Cliff(MeshBuilder mb, Vector3 p, float yaw, float length, float height, System.Random rng)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            int n = Mathf.Max(3, Mathf.RoundToInt(length / 1.4f));
            for (int i = 0; i < n; i++)
            {
                float t = n == 1 ? 0.5f : i / (float)(n - 1);
                float h = height * Mathf.Lerp(0.55f, 1f, Mathf.Sin(t * Mathf.PI)) * Range(rng, 0.8f, 1.1f);
                var c = p + rot * new Vector3((t - 0.5f) * length, 0f, Range(rng, -0.5f, 0.5f));
                var tint = DarkRock * Range(rng, 0.8f, 1f);
                tint.a = 0.7f;
                WithTint(mb, tint, () =>
                {
                    for (int layer = 0; layer < 2; layer++)
                    {
                        float r = Range(rng, 1.1f, 1.6f) * (layer == 0 ? 1f : 0.75f);
                        float lh = h * (layer == 0 ? 0.6f : 0.45f);
                        mb.Ellipsoid(c + Vector3.up * (layer == 0 ? lh * 0.35f : h * 0.62f) + rot * new Vector3(Range(rng, -0.3f, 0.3f), 0f, Range(rng, -0.3f, 0.3f)),
                            new Vector3(r, lh, r * Range(rng, 0.7f, 0.95f)), rot * Quaternion.Euler(Range(rng, -8f, 8f), Range(rng, -25f, 25f), Range(rng, -8f, 8f)), Pal(Swatch.Stone), 0, 8, 5);
                    }
                });
            }
        }

        /// <summary>Sea stack: tall, tapered, ragged rock column standing in the water.</summary>
        public static void SeaStack(MeshBuilder mb, Vector3 p, System.Random rng, float height)
        {
            float r = height * Range(rng, 0.22f, 0.3f);
            var prof = new[] { new Vector2(-1.5f, r * 1.25f), new Vector2(height * 0.35f, r), new Vector2(height * 0.75f, r * 0.8f), new Vector2(height, r * 0.45f), new Vector2(height + 0.3f, 0.05f) };
            var seed = rng.Next(1000);
            WithTint(mb, DarkRock * 0.9f, () => mb.Lathe(p, Quaternion.Euler(Range(rng, -4f, 4f), Range(rng, 0f, 360f), Range(rng, -4f, 4f)), prof, Pal(Swatch.Stone), 0, 9,
                rowShape: (row, a) => new Vector2(0.78f + 0.4f * TextureFactory.Hash(row, Mathf.RoundToInt(a * 3f), seed), Range(rng, -0.1f, 0.1f))));
        }

        /// <summary>Snow mound (smooth dome) on the snow ground mesh. Returns nothing: decorative, very low.</summary>
        public static void Mound(MeshBuilder snow, Vector3 c, Vector2 radius, float height, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            const int n = 10;
            snow.Surface(n, n, (i, j) =>
            {
                float u = i / (float)n * 2f - 1f, v = j / (float)n * 2f - 1f;
                float d = Mathf.Clamp01(u * u + v * v);
                float h = height * (1f - d) * (1f - d) - 0.03f; // edge sinks just under the floor: no seam
                return c + rot * new Vector3(u * radius.x, h, v * radius.y);
            }, (q, nrm) => Vector2.zero);
        }

        /// <summary>Flat frozen pool: pale ice plate just above the floor (never snowy).</summary>
        public static void IcePlate(MeshBuilder mb, Vector3 c, float radius, System.Random rng)
        {
            WithTint(mb, NoSnow, () => mb.Lathe(c, Quaternion.Euler(0f, Range(rng, 0f, 360f), 0f), new[] { new Vector2(0f, radius), new Vector2(0.025f, radius * 0.96f) },
                Pal(Swatch.Ice), 0, 9, new Vector2(1f, Range(rng, 0.6f, 0.9f)), capTop: true,
                rowShape: (row, a) => new Vector2(0.85f + 0.15f * Mathf.Sin(a * 3f + radius), 0f)));
        }

        /// <summary>Jagged ice chunk (floating at sea level or lying on the shelf). Snow dusting on top.</summary>
        public static void IceChunk(MeshBuilder mb, Vector3 p, System.Random rng, float size)
        {
            WithTint(mb, new Color(1f, 1f, 1f, 0.6f), () =>
            {
                for (int i = 0; i < 2; i++)
                    mb.Box(p + new Vector3(Range(rng, -0.3f, 0.3f), 0.05f, Range(rng, -0.3f, 0.3f)) * size,
                        new Vector3(Range(rng, 0.8f, 1.4f), Range(rng, 0.25f, 0.5f), Range(rng, 0.7f, 1.2f)) * size,
                        Quaternion.Euler(Range(rng, -10f, 10f), Range(rng, 0f, 360f), Range(rng, -10f, 10f)), Pal(Swatch.Ice), 0);
            });
        }

        // ------------------------------------------------------------ Vegetation

        /// <summary>Wind-bent pine, snow on its branch tiers. Dead pines lose tiers and needles.</summary>
        public static void Pine(MeshBuilder mb, Vector3 p, System.Random rng, float scale, bool dead, Vector3 wind)
        {
            float h = Range(rng, 3.2f, 4.6f) * scale;
            float bend = Range(rng, 0.2f, 0.6f) * scale;
            Vector3 At(float t) => p + Vector3.up * (h * t) + wind * (bend * t * t);
            mb.Tube(new[] { At(0f) + Vector3.down * 0.2f, At(0.3f), At(0.6f), At(0.85f), At(1f) }, t => Mathf.Lerp(0.17f, 0.03f, t) * scale, Pal(Swatch.Bark), 0, 6);
            int tiers = dead ? 2 + rng.Next(2) : 4;
            for (int k = 0; k < tiers; k++)
            {
                float t = 0.38f + k * 0.16f + Range(rng, -0.03f, 0.03f);
                float r = (1.15f - k * 0.22f) * scale * Range(rng, 0.85f, 1.1f);
                var tint = dead ? new Color(0.9f, 0.85f, 0.8f, 0.45f) : new Color(0.75f, 0.82f, 0.8f, 0.6f); // snow in clumps, needles show
                WithTint(mb, tint, () => mb.Lathe(At(t) + wind * 0.1f, Quaternion.Euler(Range(rng, -6f, 6f), Range(rng, 0f, 360f), Range(rng, -6f, 6f)),
                    new[] { new Vector2(0f, r), new Vector2(0.55f * scale, r * 0.12f) }, Pal(dead ? Swatch.Bark : Swatch.Foliage), 0, 7,
                    capBottom: true, rowShape: (row, a) => new Vector2(row == 0 ? 0.75f + 0.25f * Mathf.Sin(a * 5f + k) : 1f, row == 0 ? -0.08f * Mathf.Sin(a * 3f) : 0f)));
            }
        }

        public static void Stump(MeshBuilder mb, Vector3 p, System.Random rng)
        {
            float r = Range(rng, 0.22f, 0.32f);
            mb.Lathe(p + Vector3.down * 0.05f, Quaternion.Euler(0f, Range(rng, 0f, 360f), 0f), new[] { new Vector2(0f, r * 1.35f), new Vector2(0.15f, r), new Vector2(Range(rng, 0.35f, 0.55f), r * 0.95f) },
                Pal(Swatch.Bark), 0, 8, capTop: true, rowShape: (row, a) => new Vector2(1f, row == 2 ? 0.06f * Mathf.Sin(a * 4f) : 0f));
        }

        /// <summary>Bare shrub: a few crooked twigs.</summary>
        public static void Shrub(MeshBuilder mb, Vector3 p, System.Random rng)
        {
            int twigs = 5 + rng.Next(3);
            for (int i = 0; i < twigs; i++)
            {
                float a = Range(rng, 0f, Mathf.PI * 2f);
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                float h = Range(rng, 0.45f, 0.8f);
                mb.Tube(new[] { p, p + dir * 0.12f + Vector3.up * h * 0.5f, p + dir * Range(rng, 0.25f, 0.45f) + Vector3.up * h }, t => Mathf.Lerp(0.025f, 0.006f, t), Pal(Swatch.Bark), 0, 4, caps: false);
            }
        }

        /// <summary>Dry grass poking through the snow (or reeds at the water edge when tall).</summary>
        public static void DryTuft(MeshBuilder mb, Vector3 p, System.Random rng, float height, bool reeds = false)
        {
            int blades = reeds ? 7 + rng.Next(4) : 4 + rng.Next(3);
            WithTint(mb, NoSnow, () =>
            {
                for (int b = 0; b < blades; b++)
                {
                    float a = Range(rng, 0f, Mathf.PI * 2f);
                    var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    var basePos = p + dir * Range(rng, 0f, reeds ? 0.2f : 0.06f);
                    var lean = dir * Range(rng, 0.04f, reeds ? 0.12f : 0.18f);
                    float hh = height * Range(rng, 0.6f, 1.1f);
                    mb.Tube(new[] { basePos, basePos + lean * 0.4f + Vector3.up * hh * 0.55f, basePos + lean + Vector3.up * hh },
                        t => Mathf.Lerp(reeds ? 0.016f : 0.02f, 0.002f, t), Pal(reeds && b % 3 == 0 ? Swatch.Moss : Swatch.DryGrass), 0, 3, caps: false);
                }
            });
        }

        /// <summary>Pebbles and shells in a small cluster.</summary>
        public static void Pebbles(MeshBuilder mb, Vector3 p, System.Random rng)
        {
            int n = 4 + rng.Next(4);
            WithTint(mb, NoSnow, () =>
            {
                for (int i = 0; i < n; i++)
                {
                    float s = Range(rng, 0.04f, 0.1f);
                    var q = p + new Vector3(Range(rng, -0.4f, 0.4f), 0f, Range(rng, -0.4f, 0.4f));
                    mb.Ellipsoid(q + Vector3.up * s * 0.2f, new Vector3(s, s * 0.5f, s * 0.8f), Quaternion.Euler(0f, Range(rng, 0f, 360f), 0f),
                        Pal(rng.NextDouble() < 0.3 ? Swatch.Bleached : Swatch.Stone), 0, 6, 3);
                }
            });
        }

        // ------------------------------------------------------------ Shore debris

        /// <summary>Bleached driftwood log with branch stubs, half sunk into the snow.</summary>
        public static void Driftwood(MeshBuilder mb, Vector3 p, float yaw, float length, System.Random rng)
        {
            var old = mb.Tint;
            mb.Tint = new Color(0.85f, 0.85f, 0.85f, 0.25f);
            var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            var side = Vector3.Cross(Vector3.up, dir);
            var a = p - dir * length * 0.5f + Vector3.up * 0.08f;
            var b = p + dir * length * 0.5f + Vector3.up * 0.1f + side * Range(rng, -0.3f, 0.3f);
            mb.Tube(new[] { a, Vector3.Lerp(a, b, 0.5f) + side * Range(rng, -0.15f, 0.15f) + Vector3.up * 0.05f, b }, t => Mathf.Lerp(0.16f, 0.09f, t), Pal(Swatch.Bleached), 0, 6);
            for (int i = 0; i < 2; i++)
            {
                var root = Vector3.Lerp(a, b, Range(rng, 0.25f, 0.8f));
                var tip = root + (side * (i == 0 ? 1f : -1f) + dir * 0.4f + Vector3.up * 0.35f).normalized * Range(rng, 0.35f, 0.6f);
                mb.Tube(new[] { root, tip }, t => Mathf.Lerp(0.06f, 0.02f, t), Pal(Swatch.Bleached), 0, 4);
            }
            mb.Tint = old;
        }

        /// <summary>Whale ribs arching out of the snow, with a few vertebrae along the spine line (+Z of yaw).</summary>
        public static void WhaleRibs(MeshBuilder mb, Vector3 p, float yaw, System.Random rng)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            var old = mb.Tint;
            mb.Tint = new Color(0.9f, 0.9f, 0.88f, 0.3f);
            for (int i = 0; i < 5; i++)
            {
                float z = -2f + i * 0.9f;
                float h = Mathf.Lerp(2.2f, 1.3f, Mathf.Abs(i - 1.5f) / 2.5f) * Range(rng, 0.9f, 1.05f);
                foreach (float s in new[] { -1f, 1f })
                {
                    if (rng.NextDouble() < 0.12) continue; // a missing rib
                    var pts = new List<Vector3>();
                    for (int k = 0; k <= 5; k++)
                    {
                        float t = k / 5f;
                        float x = s * (1.4f * Mathf.Sin(t * Mathf.PI * 0.55f) + 0.15f - t * t * 0.9f);
                        pts.Add(p + rot * new Vector3(x, h * Mathf.Sin(t * Mathf.PI * 0.5f) - 0.15f, z));
                    }
                    mb.Tube(pts, t => Mathf.Lerp(0.09f, 0.035f, t), Pal(Swatch.Bleached), 0, 5);
                }
                mb.Ellipsoid(p + rot * new Vector3(0f, 0.05f, z), new Vector3(0.22f, 0.16f, 0.3f), rot, Pal(Swatch.Bleached), 0, 7, 4);
            }
            mb.Ellipsoid(p + rot * new Vector3(0.2f, 0.15f, 2.9f), new Vector3(0.7f, 0.35f, 0.9f), rot * Quaternion.Euler(-8f, 20f, 6f), Pal(Swatch.Bleached), 0, 9, 6); // skull
            mb.Tint = old;
        }

        /// <summary>Broken rowboat lying tilted on the beach (open hull).</summary>
        public static void Rowboat(MeshBuilder mb, Vector3 p, float yaw, System.Random rng, float scale = 1f)
        {
            var old = mb.Tint;
            mb.Tint = Patchy;
            var rot = Quaternion.Euler(Range(rng, -6f, 6f), yaw, Range(rng, 12f, 22f));
            var prof = new[] { new Vector2(0f, 0.12f), new Vector2(0.18f, 0.5f), new Vector2(0.5f, 0.62f) };
            for (int i = 0; i < prof.Length; i++) prof[i] *= scale;
            mb.Lathe(p + Vector3.down * 0.12f * scale, rot, prof, Pal(Swatch.Wood), 0, 12, new Vector2(0.62f, 1.75f), capBottom: true,
                rowShape: (row, a) => new Vector2(1f, row == 2 && Mathf.Sin(a) > 0.6f ? -0.18f * scale : 0f)); // broken gunwale on one side
            mb.Box(p + rot * new Vector3(0f, 0.32f, 0.2f) * scale, new Vector3(0.75f, 0.05f, 0.22f) * scale, rot, Pal(Swatch.Bark), 0); // thwart
            mb.Tube(new[] { p + rot * new Vector3(0.1f, 0.2f, -0.4f) * scale, p + rot * new Vector3(1.4f, 0.05f, -1.1f) * scale }, _ => 0.03f * scale, Pal(Swatch.Bleached), 0, 4); // oar
        }

        /// <summary>
        /// Shipwreck: bow section of a big hull lying on its side, ribs exposed, a broken mast across the sand.
        /// Origin = keel middle at floor level; the hull runs along +Z of yaw.
        /// </summary>
        public static void Shipwreck(MeshBuilder mb, Vector3 p, float yaw, System.Random rng)
        {
            var old = mb.Tint;
            mb.Tint = Patchy;
            var rot = Quaternion.Euler(0f, yaw, -18f);
            const int ribs = 7;
            for (int i = 0; i < ribs; i++)
            {
                float z = -3f + i * 0.9f;
                float w = Mathf.Lerp(1.9f, 0.5f, Mathf.Pow(i / (float)(ribs - 1), 2f)); // narrows to the bow
                foreach (float s in new[] { -1f, 1f })
                {
                    if (s > 0f && i % 3 == 1) continue; // broken ribs on the high side
                    var pts = new List<Vector3>();
                    for (int k = 0; k <= 4; k++)
                    {
                        float t = k / 4f;
                        pts.Add(p + rot * new Vector3(s * w * Mathf.Sin(t * Mathf.PI * 0.5f), 2.4f * (1f - Mathf.Cos(t * Mathf.PI * 0.5f)) - 0.3f, z));
                    }
                    mb.Tube(pts, _ => 0.09f, Pal(Swatch.Wood), 0, 5);
                }
            }
            // Planking on the low side (lies on the sand), with gaps.
            for (int k = 0; k < 5; k++)
            {
                float t = (k + 0.5f) / 5f;
                for (int seg = 0; seg < 3; seg++)
                {
                    if (rng.NextDouble() < 0.25) continue;
                    float z0 = -3f + seg * 1.9f, z1 = z0 + 1.8f;
                    float w0 = Mathf.Lerp(1.9f, 0.5f, Mathf.Pow((z0 + 3f) / 5.4f, 2f)), w1 = Mathf.Lerp(1.9f, 0.5f, Mathf.Pow(Mathf.Min(z1 + 3f, 5.4f) / 5.4f, 2f));
                    float y = 2.4f * (1f - Mathf.Cos(t * Mathf.PI * 0.5f)) - 0.3f;
                    var a = p + rot * new Vector3(-w0 * Mathf.Sin(t * Mathf.PI * 0.5f) - 0.05f, y, z0);
                    var b = p + rot * new Vector3(-w1 * Mathf.Sin(t * Mathf.PI * 0.5f) - 0.05f, y, z1);
                    var mid = (a + b) * 0.5f;
                    mb.Box(mid, new Vector3(0.08f, 0.42f, (b - a).magnitude), Quaternion.LookRotation(b - a, rot * Vector3.right) * Quaternion.Euler(0f, 0f, 90f), Pal(k % 2 == 0 ? Swatch.Wood : Swatch.Bark), 0);
                }
            }
            // Keel and stem post rising at the bow.
            mb.Box(p + rot * new Vector3(0f, -0.2f, -0.3f), new Vector3(0.22f, 0.25f, 5.8f), rot, Pal(Swatch.Bark), 0);
            mb.Tube(new[] { p + rot * new Vector3(0f, -0.2f, 2.5f), p + rot * new Vector3(0f, 1.2f, 3.2f), p + rot * new Vector3(0f, 2.6f, 3.4f) }, t => Mathf.Lerp(0.14f, 0.09f, t), Pal(Swatch.Bark), 0, 6);
            // Broken mast lying across the sand + yard.
            var mastDir = Quaternion.Euler(0f, yaw + 70f, 0f) * Vector3.forward;
            var m0 = p + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0.6f, 0.12f, -1.2f);
            mb.Tube(new[] { m0, m0 + mastDir * 4.2f + Vector3.up * 0.05f }, t => Mathf.Lerp(0.15f, 0.1f, t), Pal(Swatch.Wood), 0, 7);
            mb.Tube(new[] { m0 + mastDir * 3f + Vector3.Cross(Vector3.up, mastDir) * 1.2f + Vector3.up * 0.08f, m0 + mastDir * 3.2f - Vector3.Cross(Vector3.up, mastDir) * 1.1f + Vector3.up * 0.08f },
                _ => 0.06f, Pal(Swatch.Bark), 0, 5);
            mb.Tint = old;
        }

        /// <summary>Distant ship on the horizon: dark hull, two masts, torn sails. Fog turns it into a silhouette.</summary>
        public static void GhostShip(MeshBuilder mb, Vector3 p)
        {
            mb.Lathe(p + Vector3.down * 0.6f, Quaternion.identity, new[] { new Vector2(0f, 0.4f), new Vector2(1.1f, 1.4f), new Vector2(1.7f, 1.55f) },
                Pal(Swatch.Bark), 0, 12, new Vector2(0.55f, 4.2f), capTop: true);
            foreach (float z in new[] { -1.8f, 1.5f })
            {
                var m = p + new Vector3(0f, 1.6f, z);
                mb.Tube(new[] { m, m + Vector3.up * 6.5f }, t => Mathf.Lerp(0.14f, 0.06f, t), Pal(Swatch.Bark), 0, 5);
                for (int s = 0; s < 2; s++)
                {
                    float y = 2.6f + s * 2.2f, w = 2.6f - s * 0.6f;
                    mb.Box(m + new Vector3(0f, y, 0f), new Vector3(w * 2f, 0.08f, 0.08f), Quaternion.identity, Pal(Swatch.Bark), 0);
                    mb.Quad(m + new Vector3(-w, y, 0.05f), m + new Vector3(w, y, 0.05f), m + new Vector3(w * 0.8f, y - 1.8f, 0.3f), m + new Vector3(-w * 0.9f, y - 1.6f, 0.3f), Pal(Swatch.TunicCloth), 0);
                }
            }
        }

        /// <summary>
        /// Wooden pier from <paramref name="start"/> (on the beach) running along +Z for <paramref name="length"/> m.
        /// Broken plank gaps, posts down into the sea, mooring posts, rope coil, crates and a lantern post.
        /// Returns the lantern glow position.
        /// </summary>
        public static Vector3 Pier(MeshBuilder mb, Vector3 start, float length, System.Random rng)
        {
            const float width = 1.8f, deck = 0.18f;
            var old = mb.Tint;
            mb.Tint = Patchy;
            int planks = Mathf.RoundToInt(length / 0.32f);
            for (int i = 0; i < planks; i++)
            {
                float z = start.z + i * 0.32f + 0.16f;
                bool broken = i > planks * 0.55f && rng.NextDouble() < 0.18;
                if (broken) continue;
                float w = width * (i > planks * 0.8f && rng.NextDouble() < 0.4 ? Range(rng, 0.45f, 0.8f) : 1f);
                mb.Box(new Vector3(start.x + (width - w) * 0.5f * (rng.NextDouble() < 0.5 ? -1f : 1f), deck + Range(rng, -0.015f, 0.015f), z),
                    new Vector3(w, 0.06f, 0.29f), Quaternion.Euler(Range(rng, -1.5f, 1.5f), Range(rng, -2f, 2f), 0f), Pal(i % 3 == 0 ? Swatch.Bark : Swatch.Wood), 0);
            }
            foreach (float side in new[] { -1f, 1f })
                mb.Box(new Vector3(start.x + side * (width * 0.5f - 0.1f), deck - 0.1f, start.z + length * 0.5f), new Vector3(0.12f, 0.14f, length), Quaternion.identity, Pal(Swatch.Bark), 0);
            for (float z = 0.4f; z < length; z += 1.6f)
            foreach (float side in new[] { -1f, 1f })
            {
                var post = new Vector3(start.x + side * (width * 0.5f + 0.02f), 0f, start.z + z);
                mb.Lathe(post + Vector3.down * 1.4f, Quaternion.identity, new[] { new Vector2(0f, 0.11f), new Vector2(1.4f + deck + (z > length - 2f ? 0.55f : 0.08f), 0.1f) },
                    Pal(Swatch.Bark), 0, 7, capTop: true);
            }
            // Rope coil + crates on the deck, snow on top from the material.
            var coil = new Vector3(start.x - 0.4f, deck + 0.04f, start.z + length * 0.35f);
            var ring = new List<Vector3>();
            for (int k = 0; k <= 14; k++)
            {
                float a = k / 14f * Mathf.PI * 2f * 2.2f;
                float r = 0.24f - k * 0.006f;
                ring.Add(coil + new Vector3(Mathf.Cos(a) * r, 0.04f + k * 0.006f, Mathf.Sin(a) * r));
            }
            mb.Tube(ring, _ => 0.035f, Pal(Swatch.Leather), 0, 4);
            mb.Tint = old;
            Crate(mb, new Vector3(start.x + 0.35f, deck + 0.03f, start.z + 1.6f), 0.6f, 12f);
            Barrel(mb, new Vector3(start.x + 0.45f, deck + 0.03f, start.z + 2.6f), 30f);
            return LanternPost(mb, new Vector3(start.x + width * 0.5f - 0.1f, deck, start.z + length - 0.35f), 200f);
        }

        // ------------------------------------------------------------ Man-made props

        /// <summary>Iron lantern on a wooden post with an arm. Returns the glow position (inside the lantern).</summary>
        public static Vector3 LanternPost(MeshBuilder mb, Vector3 p, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            mb.Box(p + Vector3.up * 1.1f, new Vector3(0.14f, 2.2f, 0.14f), rot, Pal(Swatch.Bark), 0);
            mb.Box(p + Vector3.up * 2.1f + rot * new Vector3(0f, 0f, 0.3f), new Vector3(0.08f, 0.08f, 0.62f), rot, Pal(Swatch.Bark), 0);
            var lantern = p + Vector3.up * 1.82f + rot * new Vector3(0f, 0f, 0.55f);
            WithTint(mb, NoSnow, () =>
            {
                mb.Box(lantern + Vector3.up * 0.17f, new Vector3(0.24f, 0.05f, 0.24f), rot, Pal(Swatch.Gunmetal), 0);
                mb.Box(lantern - Vector3.up * 0.15f, new Vector3(0.2f, 0.04f, 0.2f), rot, Pal(Swatch.Gunmetal), 0);
                foreach (var c in new[] { new Vector3(-1f, 0f, -1f), new Vector3(1f, 0f, -1f), new Vector3(-1f, 0f, 1f), new Vector3(1f, 0f, 1f) })
                    mb.Box(lantern + rot * (c * 0.095f), new Vector3(0.025f, 0.3f, 0.025f), rot, Pal(Swatch.Gunmetal), 0);
                mb.Ellipsoid(lantern, new Vector3(0.07f, 0.1f, 0.07f), Quaternion.identity, Pal(Swatch.Coal), 0, 6, 4);
            });
            return lantern;
        }

        /// <summary>Bonfire: stone ring, crossed logs, coals, and a sword stuck in the middle (menu bonfire). Flame base at p + 0.25 up.</summary>
        public static void Bonfire(MeshBuilder mb, Vector3 p, System.Random rng)
        {
            for (int i = 0; i < 9; i++)
            {
                float a = i / 9f * Mathf.PI * 2f;
                var q = p + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.72f;
                Boulder(mb, q, rng, Range(rng, 0.17f, 0.24f));
            }
            WithTint(mb, NoSnow, () =>
            {
                for (int i = 0; i < 4; i++)
                {
                    float a = i * 45f + Range(rng, -8f, 8f);
                    var dir = Quaternion.Euler(0f, a, 0f) * Vector3.forward;
                    mb.Tube(new[] { p - dir * 0.55f + Vector3.up * 0.06f, p + Vector3.up * 0.32f, p + dir * 0.55f + Vector3.up * 0.06f }, _ => 0.06f, Pal(Swatch.Bark), 0, 5);
                }
                mb.Lathe(p, Quaternion.identity, new[] { new Vector2(0f, 0.45f), new Vector2(0.1f, 0.25f), new Vector2(0.16f, 0.02f) }, Pal(Swatch.Coal), 0, 9, capTop: true);
                // Sword: blade, guard, grip, pommel - tilted, stuck in the coals.
                var tilt = Quaternion.Euler(-12f, 30f, 8f);
                var hilt = p + tilt * new Vector3(0f, 1.05f, 0f);
                mb.Box(p + tilt * new Vector3(0f, 0.55f, 0f), new Vector3(0.07f, 1.0f, 0.018f), tilt, Pal(Swatch.BrightMetal), 0);
                mb.Box(hilt, new Vector3(0.32f, 0.045f, 0.05f), tilt, Pal(Swatch.Gunmetal), 0);
                mb.Box(hilt + tilt * new Vector3(0f, 0.13f, 0f), new Vector3(0.04f, 0.22f, 0.04f), tilt, Pal(Swatch.DarkLeather), 0);
                mb.Ellipsoid(hilt + tilt * new Vector3(0f, 0.26f, 0f), Vector3.one * 0.045f, tilt, Pal(Swatch.Gunmetal), 0, 6, 4);
            });
        }

        /// <summary>Gravestone (rounded slab) or a wooden cross, leaning a little.</summary>
        public static void Grave(MeshBuilder mb, Vector3 p, float yaw, System.Random rng)
        {
            var rot = Quaternion.Euler(Range(rng, -8f, 8f), yaw, Range(rng, -7f, 7f));
            if (rng.NextDouble() < 0.6)
            {
                float h = Range(rng, 0.7f, 1.05f);
                mb.Box(p + rot * new Vector3(0f, h * 0.5f - 0.1f, 0f), new Vector3(0.55f, h, 0.14f), rot, Pal(Swatch.Stone), 0);
                mb.Lathe(p + rot * new Vector3(0f, h - 0.1f, -0.07f), rot * Quaternion.Euler(90f, 0f, 0f), new[] { new Vector2(0f, 0.275f), new Vector2(0.14f, 0.275f) },
                    Pal(Swatch.Stone), 0, 10, capBottom: true, capTop: true, rowShape: (row, a) => new Vector2(Mathf.Cos(a) < 0f ? 0.05f : 1f, 0f));
            }
            else
            {
                mb.Box(p + rot * new Vector3(0f, 0.5f, 0f), new Vector3(0.1f, 1.2f, 0.1f), rot, Pal(Swatch.Bark), 0);
                mb.Box(p + rot * new Vector3(0f, 0.78f, 0f), new Vector3(0.62f, 0.09f, 0.09f), rot, Pal(Swatch.Bark), 0);
            }
            WithTint(mb, NoSnow, () => mb.Ellipsoid(p + rot * new Vector3(0f, 0.02f, -0.75f), new Vector3(0.4f, 0.08f, 0.6f), rot, Pal(Swatch.Snow), 0, 8, 4)); // grave mound
        }

        /// <summary>Weapon rack with spears, and a sword stuck in the snow beside it.</summary>
        public static void WeaponRack(MeshBuilder mb, Vector3 p, float yaw, System.Random rng)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            foreach (float s in new[] { -1f, 1f }) mb.Box(p + rot * new Vector3(s * 0.7f, 0.55f, 0f), new Vector3(0.1f, 1.1f, 0.1f), rot, Pal(Swatch.Bark), 0);
            mb.Box(p + rot * new Vector3(0f, 1.0f, 0f), new Vector3(1.6f, 0.08f, 0.1f), rot, Pal(Swatch.Wood), 0);
            mb.Box(p + rot * new Vector3(0f, 0.3f, 0f), new Vector3(1.5f, 0.07f, 0.08f), rot, Pal(Swatch.Wood), 0);
            for (int i = 0; i < 3; i++)
            {
                var b = p + rot * new Vector3(-0.45f + i * 0.45f, 0.05f, 0.1f);
                var lean = rot * new Vector3(Range(rng, -0.05f, 0.05f), 1.6f, -0.18f);
                mb.Tube(new[] { b, b + lean }, _ => 0.022f, Pal(Swatch.Wood), 0, 4);
                mb.Box(b + lean + lean.normalized * 0.12f, new Vector3(0.05f, 0.24f, 0.012f), Quaternion.LookRotation(rot * Vector3.forward, lean), Pal(Swatch.Gunmetal), 0);
            }
            var tilt = rot * Quaternion.Euler(10f, 0f, -14f);
            var s0 = p + rot * new Vector3(1.25f, 0f, 0.3f);
            mb.Box(s0 + tilt * new Vector3(0f, 0.4f, 0f), new Vector3(0.07f, 0.85f, 0.018f), tilt, Pal(Swatch.BrightMetal), 0);
            mb.Box(s0 + tilt * new Vector3(0f, 0.84f, 0f), new Vector3(0.3f, 0.04f, 0.05f), tilt, Pal(Swatch.Gunmetal), 0);
            mb.Box(s0 + tilt * new Vector3(0f, 0.96f, 0f), new Vector3(0.04f, 0.2f, 0.04f), tilt, Pal(Swatch.DarkLeather), 0);
        }

        /// <summary>Anchor half buried in snow.</summary>
        public static void Anchor(MeshBuilder mb, Vector3 p, float yaw)
        {
            var rot = Quaternion.Euler(-60f, yaw, 0f);
            mb.Box(p + rot * new Vector3(0f, 0.6f, 0f), new Vector3(0.1f, 1.5f, 0.1f), rot, Pal(Swatch.Gunmetal), 0);
            mb.Box(p + rot * new Vector3(0f, 1.2f, 0f), new Vector3(0.7f, 0.08f, 0.08f), rot, Pal(Swatch.Gunmetal), 0);
            var arc = new List<Vector3>();
            for (int k = 0; k <= 6; k++)
            {
                float a = Mathf.Lerp(-1.2f, 1.2f, k / 6f);
                arc.Add(p + rot * new Vector3(Mathf.Sin(a) * 0.5f, 0.05f + (1f - Mathf.Cos(a)) * 0.45f, 0f));
            }
            mb.Tube(arc, _ => 0.06f, Pal(Swatch.Gunmetal), 0, 5);
            mb.Lathe(p + rot * new Vector3(0f, 1.42f, 0f), rot * Quaternion.Euler(0f, 0f, 90f), new[] { new Vector2(-0.03f, 0.11f), new Vector2(0.03f, 0.11f) }, Pal(Swatch.Rust), 0, 8);
        }

        /// <summary>Fishing net hung between two posts.</summary>
        public static void NetFrame(MeshBuilder mb, Vector3 p, float yaw, System.Random rng)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            foreach (float s in new[] { -1f, 1f }) mb.Box(p + rot * new Vector3(s * 1.1f, 0.85f, 0f), new Vector3(0.1f, 1.7f, 0.1f), rot, Pal(Swatch.Bark), 0);
            mb.Box(p + rot * new Vector3(0f, 1.65f, 0f), new Vector3(2.4f, 0.08f, 0.08f), rot, Pal(Swatch.Bark), 0);
            WithTint(mb, NoSnow, () =>
            {
                for (int i = 0; i < 9; i++)
                {
                    float x = -1f + i * 0.25f, sag = Range(rng, 0.9f, 1.3f);
                    mb.Tube(new[] { p + rot * new Vector3(x, 1.6f, 0f), p + rot * new Vector3(x + 0.04f, 1.6f - sag * 0.5f, 0.06f), p + rot * new Vector3(x - 0.02f, 1.6f - sag, 0.02f) },
                        _ => 0.009f, Pal(Swatch.Leather), 0, 3, caps: false);
                }
                for (int j = 0; j < 4; j++)
                {
                    float y = 1.45f - j * 0.22f;
                    mb.Tube(new[] { p + rot * new Vector3(-1f, y, 0.02f), p + rot * new Vector3(0f, y - 0.08f, 0.06f), p + rot * new Vector3(1f, y, 0.02f) }, _ => 0.008f, Pal(Swatch.Leather), 0, 3, caps: false);
                }
            });
        }

        /// <summary>Hanging iron cage on a gibbet post.</summary>
        public static void Gibbet(MeshBuilder mb, Vector3 p, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            mb.Box(p + Vector3.up * 1.5f, new Vector3(0.18f, 3f, 0.18f), rot, Pal(Swatch.Bark), 0);
            mb.Box(p + Vector3.up * 2.9f + rot * new Vector3(0f, 0f, 0.55f), new Vector3(0.12f, 0.12f, 1.2f), rot, Pal(Swatch.Bark), 0);
            mb.Tube(new[] { p + rot * new Vector3(-0.02f, 2.2f, 0f), p + rot * new Vector3(-0.02f, 2.75f, 0.45f) }, _ => 0.05f, Pal(Swatch.Bark), 0, 4);
            var cage = p + Vector3.up * 2.05f + rot * new Vector3(0f, 0f, 1.0f);
            mb.Tube(new[] { cage + Vector3.up * 0.85f, cage + Vector3.up * 0.42f }, _ => 0.012f, Pal(Swatch.Gunmetal), 0, 3); // chain
            for (int i = 0; i < 8; i++)
            {
                float a = i / 8f * Mathf.PI * 2f;
                var o = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                mb.Tube(new[] { cage + Vector3.up * 0.42f + o * 0.06f, cage + Vector3.up * 0.25f + o * 0.3f, cage - Vector3.up * 0.4f + o * 0.3f, cage - Vector3.up * 0.55f + o * 0.06f },
                    _ => 0.015f, Pal(Swatch.Gunmetal), 0, 3, caps: false);
            }
            foreach (float y in new[] { 0.2f, -0.4f })
                mb.Lathe(cage + Vector3.up * y, Quaternion.identity, new[] { new Vector2(-0.02f, 0.31f), new Vector2(0.02f, 0.31f) }, Pal(Swatch.Gunmetal), 0, 10);
        }

        /// <summary>Broken statue: pedestal, legs and a fallen torso/head beside it.</summary>
        public static void BrokenStatue(MeshBuilder stone, Vector3 p, float yaw, System.Random rng)
        {
            var uv = StoneUv(1.5f);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            stone.Box(p + Vector3.up * 0.35f, new Vector3(1.3f, 0.7f, 1.3f), rot, uv, 0);
            stone.Box(p + Vector3.up * 0.78f, new Vector3(1.05f, 0.16f, 1.05f), rot, uv, 0);
            foreach (float s in new[] { -1f, 1f }) stone.Box(p + Vector3.up * 1.3f + rot * new Vector3(s * 0.2f, 0f, 0f), new Vector3(0.24f, 0.9f, 0.28f), rot * Quaternion.Euler(0f, 0f, s * 3f), uv, 0);
            stone.Box(p + Vector3.up * 1.85f, new Vector3(0.6f, 0.3f, 0.35f), rot * Quaternion.Euler(0f, 0f, 9f), uv, 0); // broken hips
            var fallen = p + rot * new Vector3(1.4f, 0.3f, 0.6f);
            stone.Box(fallen, new Vector3(0.7f, 0.5f, 1.1f), rot * Quaternion.Euler(80f, 25f, 0f), uv, 0);
            stone.Box(fallen + rot * new Vector3(0.35f, -0.1f, 1.0f), new Vector3(0.38f, 0.42f, 0.4f), rot * Quaternion.Euler(Range(rng, 10f, 30f), 40f, 15f), uv, 0);
        }

        /// <summary>Ruined stone archway (two piers + lintel), opening along +X of yaw.</summary>
        public static void Archway(MeshBuilder stone, Vector3 p, float yaw, System.Random rng)
        {
            var uv = StoneUv(2f);
            var rot = Quaternion.Euler(0f, yaw, 0f);
            foreach (float s in new[] { -1f, 1f })
            {
                float h = s > 0f ? 3.1f : Range(rng, 2.2f, 3.1f);
                stone.Box(p + rot * new Vector3(s * 1.3f, h * 0.5f, 0f), new Vector3(0.7f, h, 0.8f), rot, uv, 0);
            }
            stone.Box(p + rot * new Vector3(0.45f, 3.3f, 0f), new Vector3(2.4f, 0.45f, 0.9f), rot * Quaternion.Euler(0f, 0f, -4f), uv, 0);
        }

        /// <summary>Ruined lighthouse on a rock islet: tapered tower, gallery, broken lantern room. Returns the beacon glow position.</summary>
        public static Vector3 Lighthouse(MeshBuilder props, MeshBuilder stone, Vector3 p, System.Random rng)
        {
            for (int i = 0; i < 5; i++)
            {
                float a = i / 5f * Mathf.PI * 2f;
                Boulder(props, p + new Vector3(Mathf.Cos(a), -0.9f, Mathf.Sin(a)) * 2.6f, rng, Range(rng, 1.3f, 1.9f));
            }
            Boulder(props, p + Vector3.down * 0.6f, rng, 2.6f);
            var baseP = p + Vector3.up * 0.6f;
            const float h = 8f;
            stone.Lathe(baseP, Quaternion.identity, new[] { new Vector2(0f, 1.7f), new Vector2(h * 0.5f, 1.4f), new Vector2(h, 1.1f) }, StoneUv(2f), 0, 14);
            WithTint(props, new Color(0.9f, 0.88f, 0.84f), () =>
            {
                props.Lathe(baseP + Vector3.up * (h * 0.62f), Quaternion.identity, new[] { new Vector2(0f, 1.33f), new Vector2(0.9f, 1.27f) }, Pal(Swatch.Crimson), 0, 14); // faded red band
            });
            props.Lathe(baseP + Vector3.up * h, Quaternion.identity, new[] { new Vector2(0f, 1.15f), new Vector2(0.06f, 1.55f), new Vector2(0.22f, 1.55f) }, Pal(Swatch.Gunmetal), 0, 14, capTop: true);
            for (int i = 0; i < 12; i++)
            {
                if (i % 5 == 3) continue; // bent / missing rails
                float a = i / 12f * Mathf.PI * 2f;
                var q = baseP + Vector3.up * (h + 0.22f) + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 1.45f;
                props.Box(q + Vector3.up * 0.35f, new Vector3(0.05f, 0.7f, 0.05f), Quaternion.identity, Pal(Swatch.Gunmetal), 0);
            }
            var lamp = baseP + Vector3.up * (h + 0.9f);
            WithTint(props, NoSnow, () =>
            {
                for (int i = 0; i < 6; i++)
                {
                    if (i == 2) continue; // broken pane
                    float a = i / 6f * Mathf.PI * 2f;
                    props.Box(lamp + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 0.78f, new Vector3(0.08f, 1.3f, 0.08f), Quaternion.identity, Pal(Swatch.Gunmetal), 0);
                }
                props.Lathe(lamp + Vector3.up * 0.65f, Quaternion.identity, new[] { new Vector2(0f, 0.95f), new Vector2(0.5f, 0.15f), new Vector2(0.62f, 0.05f) }, Pal(Swatch.Gunmetal), 0, 10, capBottom: true);
            });
            return lamp;
        }
    }
}
