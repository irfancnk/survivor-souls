using System.Linq;
using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.FX;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurvivorSouls.Editor.World
{
    /// <summary>
    /// Reusable procedural props for world tiles: geometry goes into <see cref="MeshBuilder"/>s
    /// (palette = coloured props, stone = wall-textured masonry, floor = flagstone patches),
    /// FX (fire, mist) become scene objects. All sizes in metres, y = 0 is the ground.
    /// </summary>
    public static class PropKit
    {
        public static readonly Color TorchColor = new Color(1f, 0.6f, 0.3f);

        public static MeshBuilder.UvMapper StoneUv(float tile) => (p, n) =>
        {
            var a = new Vector3(Mathf.Abs(n.x), Mathf.Abs(n.y), Mathf.Abs(n.z));
            if (a.y >= a.x && a.y >= a.z) return new Vector2(p.x, p.z) / tile;
            return a.x >= a.z ? new Vector2(p.z, p.y) / tile : new Vector2(p.x, p.y) / tile;
        };

        public static MeshBuilder.UvMapper Pal(Swatch s) => PaletteAtlas.For(s);

        public static float Range(System.Random rng, float min, float max) => min + (float)rng.NextDouble() * (max - min);

        // ------------------------------------------------------------ Ground detail

        /// <summary>Flagstone patch: regular polygon (sides &gt;= 4) slightly above the grass.</summary>
        public static void Plaza(MeshBuilder floor, Vector3 centre, float radius, int sides, float rotationDeg = 0f)
        {
            var uv = StoneUv(4f);
            for (int i = 0; i < sides; i++)
            {
                float a0 = (rotationDeg + i * 360f / sides) * Mathf.Deg2Rad, a1 = (rotationDeg + (i + 1) * 360f / sides) * Mathf.Deg2Rad;
                var c = centre + Vector3.up * 0.02f;
                var p0 = c + new Vector3(Mathf.Sin(a0), 0f, Mathf.Cos(a0)) * radius;
                var p1 = c + new Vector3(Mathf.Sin(a1), 0f, Mathf.Cos(a1)) * radius;
                floor.Quad(c, p0, p1, p1, uv, 0); // triangle fan (degenerate 4th vertex)
            }
        }

        /// <summary>Straight flagstone strip from a to b.</summary>
        public static void Path(MeshBuilder floor, Vector3 a, Vector3 b, float width)
        {
            var dir = (b - a).normalized;
            var side = Vector3.Cross(Vector3.up, dir) * (width * 0.5f);
            var up = Vector3.up * 0.02f;
            floor.Quad(a - side + up, b - side + up, b + side + up, a + side + up, StoneUv(4f), 0);
        }

        public static void Tuft(MeshBuilder mb, Vector3 p, System.Random rng, float height)
        {
            int blades = 5 + rng.Next(4);
            var swatch = rng.NextDouble() < 0.3 ? Swatch.Moss : Swatch.Grass;
            for (int b = 0; b < blades; b++)
            {
                float a = Range(rng, 0f, Mathf.PI * 2f);
                var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                var basePos = p + dir * Range(rng, 0f, 0.07f);
                var lean = dir * Range(rng, 0.05f, 0.16f);
                float h = height * Range(rng, 0.6f, 1.1f);
                mb.Tube(new[] { basePos, basePos + lean * 0.4f + Vector3.up * h * 0.55f, basePos + lean + Vector3.up * h },
                    t => Mathf.Lerp(0.02f, 0.002f, t), Pal(swatch), 0, 3, caps: false);
            }
        }

        /// <summary>Small flower cluster: crimson or pale heads on short stems.</summary>
        public static void Flowers(MeshBuilder mb, Vector3 p, System.Random rng)
        {
            var head = rng.NextDouble() < 0.5 ? Swatch.Crimson : Swatch.BrightMetal;
            for (int i = 0; i < 5; i++)
            {
                var q = p + new Vector3(Range(rng, -0.25f, 0.25f), 0f, Range(rng, -0.25f, 0.25f));
                float h = Range(rng, 0.15f, 0.28f);
                mb.Tube(new[] { q, q + Vector3.up * h }, _ => 0.008f, Pal(Swatch.Grass), 0, 3, caps: false);
                mb.Ellipsoid(q + Vector3.up * h, Vector3.one * 0.045f, Quaternion.identity, Pal(head), 0, 6, 4);
            }
        }

        public static void Rock(MeshBuilder mb, Vector3 p, System.Random rng, float size)
        {
            mb.Ellipsoid(p + Vector3.up * size * 0.25f, new Vector3(size, size * Range(rng, 0.5f, 0.8f), size * Range(rng, 0.7f, 1f)),
                Quaternion.Euler(Range(rng, -8f, 8f), Range(rng, 0f, 360f), Range(rng, -8f, 8f)), Pal(Swatch.Stone), 0, 9, 6);
        }

        public static void Tree(MeshBuilder mb, Vector3 p, System.Random rng, float scale = 1f)
        {
            float h = Range(rng, 2.6f, 4.2f) * scale;
            mb.Lathe(p, Quaternion.Euler(Range(rng, -4f, 4f), 0f, Range(rng, -4f, 4f)),
                new[] { new Vector2(0f, 0.28f * scale), new Vector2(0.3f, 0.18f * scale), new Vector2(h, 0.1f * scale) }, Pal(Swatch.Bark), 0, 8);
            for (int k = 0; k < 4; k++)
            {
                var c = p + new Vector3(Range(rng, -0.6f, 0.6f) * scale, h - 0.2f + Range(rng, -0.3f, 0.6f) * scale, Range(rng, -0.6f, 0.6f) * scale);
                float r = Range(rng, 0.8f, 1.3f) * scale;
                mb.Ellipsoid(c, new Vector3(r, r * 0.8f, r), Quaternion.Euler(0f, Range(rng, 0f, 360f), 0f), Pal(Swatch.Foliage), 0, 9, 6);
            }
        }

        public static void Bush(MeshBuilder mb, Vector3 p, System.Random rng)
        {
            for (int k = 0; k < 3; k++)
            {
                var c = p + new Vector3(Range(rng, -0.4f, 0.4f), Range(rng, 0.25f, 0.45f), Range(rng, -0.4f, 0.4f));
                float r = Range(rng, 0.4f, 0.65f);
                mb.Ellipsoid(c, new Vector3(r, r * 0.75f, r), Quaternion.identity, Pal(k == 0 ? Swatch.Moss : Swatch.Foliage), 0, 8, 5);
            }
        }

        // ------------------------------------------------------------ Masonry

        /// <summary>Broken wall from a to b: blocks of random height, ragged cap stones, some gaps.</summary>
        public static void WallRun(MeshBuilder stone, Vector3 a, Vector3 b, System.Random rng, float minH, float maxH, float gapChance = 0.15f)
        {
            var uv = StoneUv(2f);
            var dir = (b - a).normalized;
            float length = (b - a).magnitude;
            var rot = Quaternion.LookRotation(dir, Vector3.up);
            float t = 0f;
            while (t < length - 0.05f)
            {
                float seg = Mathf.Min(Range(rng, 1.2f, 2.6f), length - t);
                bool gap = rng.NextDouble() < gapChance;
                float h = gap ? Range(rng, 0.15f, 0.35f) : Range(rng, minH, maxH);
                var centre = a + dir * (t + seg * 0.5f) + Vector3.up * (h * 0.5f);
                var segRot = rot * Quaternion.Euler(0f, Range(rng, -1.5f, 1.5f), 0f);
                stone.Box(centre, new Vector3(0.8f, h, seg + 0.05f), segRot, uv, 0);
                if (!gap && h > 0.9f && rng.NextDouble() < 0.7)
                    stone.Box(centre + Vector3.up * (h * 0.5f + 0.1f), new Vector3(0.7f, 0.2f, seg * Range(rng, 0.4f, 0.8f)),
                        segRot * Quaternion.Euler(Range(rng, -4f, 4f), Range(rng, -6f, 6f), Range(rng, -4f, 4f)), uv, 0);
                t += seg;
            }
        }

        public static void Pillar(MeshBuilder stone, Vector3 p, System.Random rng, float h)
        {
            var uv = StoneUv(2f);
            var rot = Quaternion.Euler(0f, Range(rng, -8f, 8f), 0f);
            stone.Box(p + new Vector3(0f, 0.12f, 0f), new Vector3(1.25f, 0.24f, 1.25f), rot, uv, 0);
            stone.Box(p + new Vector3(0f, h * 0.5f, 0f), new Vector3(0.9f, h, 0.9f), rot, uv, 0);
            if (h > 2.4f) stone.Box(p + new Vector3(0f, h + 0.14f, 0f), new Vector3(1.15f, 0.28f, 1.15f), rot, uv, 0);
            else stone.Box(p + new Vector3(0f, h + 0.12f, 0f), new Vector3(0.75f, 0.3f, 0.7f),
                rot * Quaternion.Euler(Range(rng, -12f, 12f), 0f, Range(rng, -15f, 15f)), uv, 0);
        }

        /// <summary>Crimson banner hanging on the pillar face that looks along <paramref name="facing"/>.</summary>
        public static void Banner(MeshBuilder props, Vector3 pillar, Vector3 facing, float h)
        {
            var rot = Quaternion.LookRotation(facing, Vector3.up);
            var face = pillar + facing * 0.48f;
            float top = h - 0.25f, len = Mathf.Min(1.9f, h - 0.6f);
            props.Box(face + Vector3.up * top, new Vector3(0.95f, 0.06f, 0.06f), rot, Pal(Swatch.Gunmetal), 0);
            props.Box(face + facing * 0.02f + Vector3.up * (top - len * 0.5f), new Vector3(0.78f, len, 0.03f), rot, Pal(Swatch.Crimson), 0);
            props.Box(face + facing * 0.04f + Vector3.up * (top - len * 0.45f), new Vector3(0.32f, 0.32f, 0.02f), rot * Quaternion.Euler(0f, 0f, 45f), Pal(Swatch.BrightMetal), 0);
            for (int i = 0; i < 3; i++)
                props.Box(face + facing * 0.02f + rot * new Vector3(-0.26f + i * 0.26f, 0f, 0f) + Vector3.up * (top - len - 0.1f),
                    new Vector3(0.2f, 0.22f - i % 2 * 0.08f, 0.03f), rot, Pal(Swatch.Crimson), 0);
        }

        /// <summary>Stone arch with a closed double door, facing -Z (towards the camera). Returns flame spots for sconces.</summary>
        public static Vector3[] DoorGate(MeshBuilder stone, MeshBuilder props, Vector3 c, System.Random rng)
        {
            var uv = StoneUv(2f);
            foreach (float side in new[] { -1f, 1f })
            {
                stone.Box(c + new Vector3(side * 1.3f, 1.8f, 0f), new Vector3(0.45f, 3.6f, 1.1f), Quaternion.identity, uv, 0);
                stone.Box(c + new Vector3(side * 1.3f, 0.15f, 0f), new Vector3(0.65f, 0.3f, 1.25f), Quaternion.identity, uv, 0);
                stone.Box(c + new Vector3(side * 2.4f, 1.6f, 0.05f), new Vector3(1.8f, 3.2f, 0.9f), Quaternion.identity, uv, 0); // wing walls
                stone.Box(c + new Vector3(side * 3.45f, 1.0f, 0.05f), new Vector3(0.6f, 2.0f, 0.9f), Quaternion.Euler(0f, 0f, side * -4f), uv, 0);
            }
            stone.Box(c + new Vector3(0f, 3.75f, 0f), new Vector3(3.3f, 0.55f, 1.15f), Quaternion.identity, uv, 0);
            stone.Box(c + new Vector3(0f, 3.75f, -0.61f), new Vector3(0.5f, 0.75f, 0.12f), Quaternion.identity, uv, 0);
            stone.Box(c + new Vector3(0f, 0.06f, -0.9f), new Vector3(3.0f, 0.12f, 0.7f), Quaternion.identity, uv, 0);

            float doorZ = c.z - 0.1f;
            foreach (float side in new[] { -1f, 1f })
            {
                for (int i = 0; i < 4; i++)
                {
                    float h = 3.3f - Range(rng, 0f, 0.08f);
                    props.Box(new Vector3(c.x + side * (0.13f + i * 0.27f), h * 0.5f, doorZ), new Vector3(0.26f, h, 0.14f), Quaternion.identity, Pal(i % 2 == 0 ? Swatch.Wood : Swatch.Bark), 0);
                }
                foreach (float y in new[] { 0.6f, 1.75f, 2.9f })
                {
                    props.Box(new Vector3(c.x + side * 0.55f, y, doorZ - 0.09f), new Vector3(1.05f, 0.12f, 0.04f), Quaternion.identity, Pal(Swatch.Gunmetal), 0);
                    for (int k = 0; k < 4; k++)
                        props.Ellipsoid(new Vector3(c.x + side * (0.15f + k * 0.27f), y, doorZ - 0.115f), Vector3.one * 0.025f, Quaternion.identity, Pal(Swatch.BrightMetal), 0, 6, 4);
                }
                props.Lathe(new Vector3(c.x + side * 0.22f, 1.5f, doorZ - 0.12f), Quaternion.Euler(90f, 0f, 0f),
                    new[] { new Vector2(0f, 0.09f), new Vector2(0.025f, 0.09f) }, Pal(Swatch.Gunmetal), 0, 10);
            }

            var flames = new Vector3[2];
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? -1f : 1f;
                var wall = c + new Vector3(side * 1.62f, 2.3f, -0.55f);
                flames[i] = Sconce(props, wall, Vector3.back);
            }
            return flames;
        }

        /// <summary>Iron wall torch: bracket + bowl. Returns the flame base position.</summary>
        public static Vector3 Sconce(MeshBuilder props, Vector3 wallPoint, Vector3 outward)
        {
            var bowl = wallPoint + outward * 0.28f;
            props.Box(wallPoint + outward * 0.14f + Vector3.down * 0.12f, new Vector3(0.06f, 0.05f, 0.3f),
                Quaternion.LookRotation(outward) * Quaternion.Euler(-25f, 0f, 0f), Pal(Swatch.Gunmetal), 0);
            props.Lathe(bowl + Vector3.down * 0.1f, Quaternion.identity, new[] { new Vector2(0f, 0.04f), new Vector2(0.08f, 0.14f), new Vector2(0.11f, 0.15f) }, Pal(Swatch.Gunmetal), 0, 10);
            props.Lathe(bowl, Quaternion.identity, new[] { new Vector2(0f, 0.13f), new Vector2(0.02f, 0.06f) }, Pal(Swatch.Coal), 0, 8, capTop: true);
            return bowl + Vector3.up * 0.04f;
        }

        /// <summary>Stone pedestal + iron bowl + coals. Flame base is at p + 1.05 up.</summary>
        public static void Brazier(MeshBuilder props, Vector3 p)
        {
            props.Lathe(p, Quaternion.identity, new[] { new Vector2(0f, 0.32f), new Vector2(0.12f, 0.3f), new Vector2(0.18f, 0.16f), new Vector2(0.75f, 0.12f), new Vector2(0.82f, 0.18f) },
                Pal(Swatch.Stone), 0, 10, capTop: true);
            props.Lathe(p + Vector3.up * 0.8f, Quaternion.identity, new[] { new Vector2(0f, 0.16f), new Vector2(0.12f, 0.34f), new Vector2(0.22f, 0.42f), new Vector2(0.26f, 0.44f) },
                Pal(Swatch.Gunmetal), 0, 12);
            props.Lathe(p + Vector3.up * 0.98f, Quaternion.identity, new[] { new Vector2(0f, 0.38f), new Vector2(0.04f, 0.3f), new Vector2(0.08f, 0.12f) },
                Pal(Swatch.Coal), 0, 10, capTop: true);
        }

        public static void Barrel(MeshBuilder mb, Vector3 p, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            mb.Lathe(p, rot, new[] { new Vector2(0f, 0.3f), new Vector2(0.25f, 0.35f), new Vector2(0.5f, 0.37f), new Vector2(0.75f, 0.35f), new Vector2(0.95f, 0.3f) },
                Pal(Swatch.Wood), 0, 12, capTop: true);
            foreach (float y in new[] { 0.14f, 0.8f })
                mb.Lathe(p + Vector3.up * y, rot, new[] { new Vector2(0f, y < 0.5f ? 0.33f : 0.335f), new Vector2(0.06f, y < 0.5f ? 0.335f : 0.33f) }, Pal(Swatch.Gunmetal), 0, 12);
        }

        public static void Crate(MeshBuilder mb, Vector3 p, float s, float yaw)
        {
            var rot = Quaternion.Euler(0f, yaw, 0f);
            mb.Box(p + Vector3.up * s * 0.5f, Vector3.one * s, rot, Pal(Swatch.Wood), 0);
            float e = s * 0.5f;
            foreach (float a in new[] { -1f, 1f })
            foreach (float b in new[] { -1f, 1f })
                mb.Box(p + rot * new Vector3(a * e, e, b * e), new Vector3(0.07f, s + 0.02f, 0.07f), rot, Pal(Swatch.Bark), 0);
            mb.Box(p + Vector3.up * s * 0.5f + rot * new Vector3(0f, 0f, -e - 0.01f), new Vector3(s * 1.1f, 0.07f, 0.03f), rot * Quaternion.Euler(0f, 0f, 45f), Pal(Swatch.Bark), 0);
        }

        public static void FallenColumn(MeshBuilder mb, Vector3 start, float yaw, int drums, System.Random rng)
        {
            var dir = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            for (int i = 0; i < drums; i++)
            {
                var c = start + dir * (i * 0.92f) + Vector3.up * 0.42f;
                mb.Lathe(c - dir * 0.42f, Quaternion.FromToRotation(Vector3.up, dir) * Quaternion.Euler(0f, Range(rng, 0f, 360f), 0f),
                    new[] { new Vector2(0f, 0.42f), new Vector2(0.84f, 0.42f) }, Pal(Swatch.Stone), 0, 12, capBottom: true, capTop: true);
            }
        }

        // ------------------------------------------------------------ FX (scene objects)

        /// <summary>Flames, embers and a flickering point light. Scale shrinks the FX for wall sconces.</summary>
        public static void Fire(Transform parent, Vector3 localPos, GameArtKit kit, float scale, float intensity, float range)
        {
            var go = new GameObject("Fire");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
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
            light.intensity = intensity;
            light.range = range;
            lightGo.AddComponent<LightFlicker>().Configure(intensity, intensity * 0.14f, range, range * 0.06f);
        }

        public static ParticleSystem Particles(string name, Transform parent, Material mat)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(-90f, 0f, 0f); // cone emits along +Z; point it up
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.prewarm = true;
            main.loop = true;
            main.scalingMode = ParticleSystemScalingMode.Hierarchy;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        public static void SetupRenderer(Renderer r, Material mat)
        {
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
        }

        public static Gradient Gradient((Color c, float t)[] colors, (float a, float t)[] alphas)
        {
            var g = new Gradient();
            g.SetKeys(colors.Select(k => new GradientColorKey(k.c, k.t)).ToArray(), alphas.Select(k => new GradientAlphaKey(k.a, k.t)).ToArray());
            return g;
        }
    }
}
