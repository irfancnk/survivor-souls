using System;
using SurvivorSouls.Editor.Procedural;
using UnityEngine;

namespace SurvivorSouls.Editor.Skills
{
    /// <summary>
    /// Procedural skill rune icons (KAN-19 placeholder art). Each glyph is a signed distance field in [-1, 1] space
    /// (y up, negative = inside), drawn white with a soft top-to-bottom shade and a dark outline so it reads on any card;
    /// the UI tints it with the rarity colour. Replace with painted icons later: only the texture on the skill asset changes.
    /// </summary>
    public static class SkillIconPainter
    {
        public const int Size = 128;
        private const float Outline = 0.06f;

        /// <summary>Writes the icon PNG for <paramref name="glyph"/> and returns the imported texture.</summary>
        public static Texture2D Write(string path, Func<Vector2, float> glyph)
        {
            float px = 2f / Size;
            return TextureFactory.Write(path, Size, Size, (x, y) =>
            {
                var p = new Vector2((x + 0.5f) / Size * 2f - 1f, (y + 0.5f) / Size * 2f - 1f);
                float d = glyph(p);
                float fill = 1f - TextureFactory.SmoothStep(-px, px, d);
                float edge = 1f - TextureFactory.SmoothStep(Outline - px, Outline + px, d);
                float a = Mathf.Max(fill, edge * 0.85f);
                if (a <= 0.001f) return new Color(0f, 0f, 0f, 0f);
                float shade = Mathf.Lerp(0.78f, 1f, (p.y + 1f) * 0.5f);
                float k = edge > 0f ? fill / Mathf.Max(edge, 1e-4f) : 0f;
                float g = Mathf.Lerp(0.06f, shade, Mathf.Clamp01(k));
                return new Color(g, g, g, a);
            }, TextureFactory.Kind.Color, mipmaps: true, wrap: TextureWrapMode.Clamp, maxSize: Size);
        }

        // ------------------------------------------------------------ Glyphs

        /// <summary>Bolt head pointing up-right with fletching.</summary>
        public static float Damage(Vector2 p)
        {
            Vector2 dir = new Vector2(1f, 1f).normalized, n = new Vector2(-dir.y, dir.x);
            Vector2 tip = dir * 0.86f, baseC = dir * 0.3f;
            float head = Triangle(p, tip, baseC + n * 0.3f, baseC - n * 0.3f);
            float shaft = Segment(p, -dir * 0.72f, dir * 0.35f, 0.07f);
            float f1 = Segment(p, -dir * 0.72f, -dir * 0.45f + n * 0.22f, 0.06f);
            float f2 = Segment(p, -dir * 0.72f, -dir * 0.45f - n * 0.22f, 0.06f);
            return Min(head, shaft, f1, f2);
        }

        /// <summary>Double chevron ">>".</summary>
        public static float AttackSpeed(Vector2 p) => Mathf.Min(Chevron(p, -0.3f), Chevron(p, 0.3f));

        /// <summary>Arrowhead with three speed lines.</summary>
        public static float MoveSpeed(Vector2 p)
        {
            float head = Triangle(p, new Vector2(0.82f, 0f), new Vector2(0.05f, 0.52f), new Vector2(0.05f, -0.52f));
            float l1 = Segment(p, new Vector2(-0.8f, 0.36f), new Vector2(-0.12f, 0.36f), 0.07f);
            float l2 = Segment(p, new Vector2(-0.6f, 0f), new Vector2(-0.12f, 0f), 0.07f);
            float l3 = Segment(p, new Vector2(-0.8f, -0.36f), new Vector2(-0.12f, -0.36f), 0.07f);
            return Min(head, l1, l2, l3);
        }

        public static float MaxHealth(Vector2 p) => Heart((p + new Vector2(0f, 0.72f)) / 1.45f) * 1.45f;

        /// <summary>Crosshair: ring, four ticks, centre dot.</summary>
        public static float Crit(Vector2 p)
        {
            float ring = Mathf.Abs(p.magnitude - 0.52f) - 0.07f;
            float t1 = Segment(p, new Vector2(0f, 0.38f), new Vector2(0f, 0.86f), 0.07f);
            float t2 = Segment(p, new Vector2(0f, -0.38f), new Vector2(0f, -0.86f), 0.07f);
            float t3 = Segment(p, new Vector2(0.38f, 0f), new Vector2(0.86f, 0f), 0.07f);
            float t4 = Segment(p, new Vector2(-0.38f, 0f), new Vector2(-0.86f, 0f), 0.07f);
            return Min(ring, t1, t2, t3, t4, p.magnitude - 0.12f);
        }

        /// <summary>Orb with an inner ring and a broken outer ring (pull).</summary>
        public static float PickupRadius(Vector2 p)
        {
            float r = p.magnitude;
            float core = r - 0.2f;
            float inner = Mathf.Abs(r - 0.46f) - 0.055f;
            float angle = Mathf.Atan2(p.y, p.x);
            float gaps = Mathf.Abs(Mathf.Repeat(angle / (Mathf.PI * 0.5f) + 0.5f, 1f) - 0.5f) - 0.12f; // 4 gaps
            float outer = Mathf.Max(Mathf.Abs(r - 0.78f) - 0.06f, -gaps * r);
            return Min(core, inner, outer);
        }

        /// <summary>Three arrows fanned from the bottom (Spread Shot).</summary>
        public static float Multishot(Vector2 p)
        {
            Vector2 o = new Vector2(0f, -0.82f);
            float d = float.MaxValue;
            foreach (float deg in new[] { -28f, 0f, 28f })
            {
                float a = (90f + deg) * Mathf.Deg2Rad;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a)), n = new Vector2(-dir.y, dir.x);
                Vector2 tip = o + dir * 1.66f, b = o + dir * 1.28f;
                d = Min(d, Segment(p, o + dir * 0.2f, b, 0.055f), Triangle(p, tip, b + n * 0.17f, b - n * 0.17f));
            }
            return d;
        }

        /// <summary>Double Shot (KAN-83): two arrows on parallel lines, the second one a step behind.</summary>
        public static float DoubleShot(Vector2 p)
        {
            float d = float.MaxValue;
            foreach (var (x, lead) in new[] { (-0.26f, 0.22f), (0.26f, -0.12f) })
            {
                Vector2 tip = new Vector2(x, 0.68f + lead), b = new Vector2(x, 0.3f + lead);
                float shaft = Segment(p, new Vector2(x, -0.86f + lead), b, 0.06f);
                float head = Triangle(p, tip, b + new Vector2(0.19f, 0f), b - new Vector2(0.19f, 0f));
                float f1 = Segment(p, new Vector2(x, -0.86f + lead), new Vector2(x - 0.16f, -0.64f + lead), 0.05f);
                float f2 = Segment(p, new Vector2(x, -0.86f + lead), new Vector2(x + 0.16f, -0.64f + lead), 0.05f);
                d = Min(d, shaft, head, f1, f2);
            }
            return d;
        }

        /// <summary>Arrow through a ring.</summary>
        public static float Pierce(Vector2 p)
        {
            float ring = Mathf.Abs(p.magnitude - 0.4f) - 0.08f;
            ring = Mathf.Max(ring, -(Mathf.Abs(p.y) - 0.15f)); // gap where the shaft passes
            float shaft = Segment(p, new Vector2(-0.88f, 0f), new Vector2(0.5f, 0f), 0.07f);
            float head = Triangle(p, new Vector2(0.9f, 0f), new Vector2(0.45f, 0.26f), new Vector2(0.45f, -0.26f));
            return Min(ring, shaft, head);
        }

        /// <summary>Zigzag bounce path ending in an arrowhead, with impact dots at the corners.</summary>
        public static float Ricochet(Vector2 p)
        {
            Vector2 a = new Vector2(-0.82f, -0.55f), b = new Vector2(-0.3f, 0.42f), c = new Vector2(0.18f, -0.32f), e = new Vector2(0.55f, 0.38f);
            Vector2 dir = (e - c).normalized, n = new Vector2(-dir.y, dir.x);
            Vector2 tip = e + dir * 0.32f;
            return Min(Segment(p, a, b, 0.065f), Segment(p, b, c, 0.065f), Segment(p, c, e, 0.065f),
                Triangle(p, tip, e + n * 0.22f, e - n * 0.22f), (p - b).magnitude - 0.13f, (p - c).magnitude - 0.13f);
        }

        /// <summary>Flame: tall tongue with two side tongues, hollow heart.</summary>
        public static float Burn(Vector2 p)
        {
            float main = Teardrop(p, new Vector2(0f, -0.32f), 0.5f, 0.9f);
            float left = Teardrop(Rotate(p - new Vector2(-0.36f, -0.42f), -0.45f), Vector2.zero, 0.26f, 0.55f);
            float right = Teardrop(Rotate(p - new Vector2(0.36f, -0.42f), 0.45f), Vector2.zero, 0.26f, 0.55f);
            float outer = SmoothMin(SmoothMin(main, left, 0.12f), right, 0.12f);
            float hole = Teardrop(p, new Vector2(0f, -0.42f), 0.26f, 0.3f);
            return Mathf.Max(outer, -hole);
        }

        /// <summary>Blood drop with a highlight cut.</summary>
        public static float LifeSteal(Vector2 p)
        {
            float drop = Teardrop(p, new Vector2(0f, -0.28f), 0.55f, 0.88f);
            float shine = (p - new Vector2(-0.2f, -0.32f)).magnitude - 0.12f;
            return Mathf.Max(drop, -shine);
        }

        /// <summary>Eight-point starburst around a hollow core.</summary>
        public static float KillBurst(Vector2 p)
        {
            float r = p.magnitude;
            float a = Mathf.Atan2(p.y, p.x);
            float spikes = 0.36f + 0.52f * Mathf.Pow(Mathf.Abs(Mathf.Cos(a * 4f)), 7f);
            float star = (r - spikes) * 0.55f;
            float hole = r - 0.18f;
            return Mathf.Max(star, -hole);
        }

        /// <summary>Hallowed Shield (KAN-93): heater shield rim (flat top, pointed base) with a cross inside.</summary>
        public static float Ward(Vector2 p)
        {
            // Two big discs meet in the point (0, -0.88) and pass through the top corners (+-0.62, 0.62); flat top edge.
            const float c = 1.5045f, r = 2.1245f;
            float shield = Mathf.Max(Mathf.Max((p - new Vector2(-c, 0.62f)).magnitude - r, (p - new Vector2(c, 0.62f)).magnitude - r),
                p.y - 0.62f);
            float rim = Mathf.Max(shield, -(shield + 0.13f));
            float cross = Mathf.Min(Segment(p, new Vector2(0f, 0.42f), new Vector2(0f, -0.5f), 0.075f),
                Segment(p, new Vector2(-0.3f, 0.14f), new Vector2(0.3f, 0.14f), 0.075f));
            return Mathf.Min(rim, cross);
        }

        /// <summary>Frost star (KAN-91 Freeze Arrow): six arms with two branch pairs each and a small hex core.</summary>
        public static float Frost(Vector2 p)
        {
            float d = (p.magnitude) - 0.16f;
            for (int i = 0; i < 6; i++)
            {
                float a = i * Mathf.PI / 3f + Mathf.PI / 2f;
                Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a)), n = new Vector2(-dir.y, dir.x);
                d = Mathf.Min(d, Segment(p, Vector2.zero, dir * 0.86f, 0.065f));
                Vector2 b1 = dir * 0.42f, b2 = dir * 0.64f;
                d = Mathf.Min(d, Mathf.Min(Segment(p, b1, b1 + (dir + n * 1.1f) * 0.2f, 0.05f), Segment(p, b1, b1 + (dir - n * 1.1f) * 0.2f, 0.05f)));
                d = Mathf.Min(d, Mathf.Min(Segment(p, b2, b2 + (dir + n * 1.1f) * 0.13f, 0.045f), Segment(p, b2, b2 + (dir - n * 1.1f) * 0.13f, 0.045f)));
            }
            return d;
        }

        // ------------------------------------------------------------ SDF primitives

        /// <summary>Reliquary chest: box body, domed lid split by a gap, keyhole cut out, two light rays above.</summary>
        public static float Chest(Vector2 p)
        {
            float body = RoundBox(p, new Vector2(0f, -0.3f), new Vector2(0.72f, 0.3f), 0.06f);
            float lid = Mathf.Max(RoundBox(p, new Vector2(0f, 0.1f), new Vector2(0.72f, 0.24f), 0.22f), -(p.y - 0.08f));
            float gap = Mathf.Abs(p.y - 0.04f) - 0.035f;
            float keyhole = Mathf.Min((p - new Vector2(0f, -0.14f)).magnitude - 0.1f,
                RoundBox(p, new Vector2(0f, -0.28f), new Vector2(0.045f, 0.12f), 0f));
            float chest = Mathf.Max(Mathf.Min(body, lid), Mathf.Max(-gap, -keyhole));
            float r1 = Segment(p, new Vector2(-0.3f, 0.5f), new Vector2(-0.42f, 0.82f), 0.05f);
            float r2 = Segment(p, new Vector2(0f, 0.55f), new Vector2(0f, 0.9f), 0.05f);
            float r3 = Segment(p, new Vector2(0.3f, 0.5f), new Vector2(0.42f, 0.82f), 0.05f);
            return Min(chest, r1, r2, r3);
        }

        private static float RoundBox(Vector2 p, Vector2 centre, Vector2 half, float radius)
        {
            Vector2 q = new Vector2(Mathf.Abs(p.x - centre.x), Mathf.Abs(p.y - centre.y)) - half + Vector2.one * radius;
            return new Vector2(Mathf.Max(q.x, 0f), Mathf.Max(q.y, 0f)).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - radius;
        }

        private static float Chevron(Vector2 p, float x) =>
            Mathf.Min(Segment(p, new Vector2(x - 0.24f, 0.5f), new Vector2(x + 0.22f, 0f), 0.11f),
                Segment(p, new Vector2(x + 0.22f, 0f), new Vector2(x - 0.24f, -0.5f), 0.11f));

        private static float Teardrop(Vector2 p, Vector2 centre, float radius, float tipY)
        {
            float circle = (p - centre).magnitude - radius;
            float tri = Triangle(p, new Vector2(centre.x, tipY), centre + new Vector2(radius * 0.92f, 0f), centre - new Vector2(radius * 0.92f, 0f));
            return SmoothMin(circle, tri, 0.12f);
        }

        private static float Segment(Vector2 p, Vector2 a, Vector2 b, float r)
        {
            Vector2 pa = p - a, ba = b - a;
            float h = Mathf.Clamp01(Vector2.Dot(pa, ba) / Vector2.Dot(ba, ba));
            return (pa - ba * h).magnitude - r;
        }

        /// <summary>Exact signed distance to a triangle (Inigo Quilez).</summary>
        private static float Triangle(Vector2 p, Vector2 p0, Vector2 p1, Vector2 p2)
        {
            Vector2 e0 = p1 - p0, e1 = p2 - p1, e2 = p0 - p2;
            Vector2 v0 = p - p0, v1 = p - p1, v2 = p - p2;
            Vector2 pq0 = v0 - e0 * Mathf.Clamp01(Vector2.Dot(v0, e0) / Vector2.Dot(e0, e0));
            Vector2 pq1 = v1 - e1 * Mathf.Clamp01(Vector2.Dot(v1, e1) / Vector2.Dot(e1, e1));
            Vector2 pq2 = v2 - e2 * Mathf.Clamp01(Vector2.Dot(v2, e2) / Vector2.Dot(e2, e2));
            float s = Mathf.Sign(e0.x * e2.y - e0.y * e2.x);
            Vector2 d0 = new Vector2(pq0.sqrMagnitude, s * (v0.x * e0.y - v0.y * e0.x));
            Vector2 d1 = new Vector2(pq1.sqrMagnitude, s * (v1.x * e1.y - v1.y * e1.x));
            Vector2 d2 = new Vector2(pq2.sqrMagnitude, s * (v2.x * e2.y - v2.y * e2.x));
            Vector2 d = Vector2.Min(Vector2.Min(d0, d1), d2);
            return -Mathf.Sqrt(d.x) * Mathf.Sign(d.y);
        }

        /// <summary>Heart (Inigo Quilez), bottom tip at the origin, about 1.1 tall.</summary>
        private static float Heart(Vector2 p)
        {
            p.x = Mathf.Abs(p.x);
            if (p.y + p.x > 1f) return (p - new Vector2(0.25f, 0.75f)).magnitude - Mathf.Sqrt(2f) / 4f;
            float m = 0.5f * Mathf.Max(p.x + p.y, 0f);
            return Mathf.Sqrt(Mathf.Min((p - new Vector2(0f, 1f)).sqrMagnitude, (p - new Vector2(m, m)).sqrMagnitude)) * Mathf.Sign(p.x - p.y);
        }

        private static Vector2 Rotate(Vector2 p, float radians)
        {
            float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
            return new Vector2(c * p.x - s * p.y, s * p.x + c * p.y);
        }

        private static float SmoothMin(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }

        private static float Min(params float[] d)
        {
            float m = float.MaxValue;
            foreach (float v in d) m = Mathf.Min(m, v);
            return m;
        }
    }
}
