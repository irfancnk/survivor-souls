using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// What the camera sees of a horizontal plane (KAN-29): the four viewport corner rays cut the plane y = h, giving a
    /// convex quad in XZ. A body standing at a point is hidden when its feet are outside the ground quad (h = 0) and its
    /// head is outside the head quad (h = body height). Built from the live camera, so portrait / landscape, any aspect,
    /// zoom or a boss arena camera all work. Struct with four fixed corners: no allocation.
    /// </summary>
    public struct ScreenFootprint
    {
        public Vector2 A, B, C, D; // XZ corners in order: viewport (0,0), (1,0), (1,1), (0,1)

        /// <summary>
        /// Footprint of <paramref name="camera"/> on the plane y = <paramref name="height"/>. A corner ray that never
        /// reaches the plane (above the horizon) is cut at <paramref name="farLimit"/> m from the camera.
        /// </summary>
        public static ScreenFootprint Of(Camera camera, float height, float farLimit)
        {
            return new ScreenFootprint
            {
                A = Corner(camera, 0f, 0f, height, farLimit),
                B = Corner(camera, 1f, 0f, height, farLimit),
                C = Corner(camera, 1f, 1f, height, farLimit),
                D = Corner(camera, 0f, 1f, height, farLimit),
            };
        }

        private static Vector2 Corner(Camera camera, float vx, float vy, float height, float farLimit)
        {
            Ray ray = camera.ViewportPointToRay(new Vector3(vx, vy, 0f));
            Vector3 o = ray.origin, d = ray.direction;
            float t = farLimit;
            if (d.y < -1e-4f)
            {
                float hit = (height - o.y) / d.y;
                if (hit > 0f) t = Mathf.Min(hit, farLimit);
            }
            else if (d.y > 1e-4f && o.y < height)
            {
                float hit = (height - o.y) / d.y; // camera below the plane looking up
                t = Mathf.Min(hit, farLimit);
            }
            Vector3 p = o + d * t;
            return new Vector2(p.x, p.z);
        }

        public Vector2 this[int i] => i switch { 0 => A, 1 => B, 2 => C, _ => D };

        /// <summary>
        /// Signed distance (m) from <paramref name="p"/> to the quad: negative inside (on screen), positive outside.
        /// Exact for convex quads of either winding.
        /// </summary>
        public float SignedDistance(Vector2 p)
        {
            float min = float.MaxValue;
            bool inside = true;
            float sign = Winding();
            for (int i = 0; i < 4; i++)
            {
                Vector2 a = this[i], b = this[(i + 1) & 3];
                Vector2 e = b - a;
                float cross = (e.x * (p.y - a.y) - e.y * (p.x - a.x)) * sign;
                if (cross < 0f) inside = false;
                min = Mathf.Min(min, SegmentDistance(p, a, b));
            }
            return inside ? -min : min;
        }

        /// <summary>
        /// Distance along <paramref name="dir"/> (unit, XZ) from <paramref name="origin"/> to where the ray leaves the quad.
        /// 0 when the origin is already outside.
        /// </summary>
        public float ExitDistance(Vector2 origin, Vector2 dir)
        {
            if (SignedDistance(origin) >= 0f) return 0f;
            float best = 0f;
            for (int i = 0; i < 4; i++)
            {
                Vector2 a = this[i], e = this[(i + 1) & 3] - a;
                float denom = dir.x * e.y - dir.y * e.x;
                if (Mathf.Abs(denom) < 1e-6f) continue;
                Vector2 ao = a - origin;
                float t = (ao.x * e.y - ao.y * e.x) / denom; // along the ray
                float u = (ao.x * dir.y - ao.y * dir.x) / denom; // along the edge
                if (t > 0f && u >= -1e-4f && u <= 1f + 1e-4f) best = Mathf.Max(best, t);
            }
            return best;
        }

        private float Winding()
        {
            float area = 0f;
            for (int i = 0; i < 4; i++)
            {
                Vector2 a = this[i], b = this[(i + 1) & 3];
                area += a.x * b.y - b.x * a.y;
            }
            return area >= 0f ? 1f : -1f;
        }

        private static float SegmentDistance(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float len2 = ab.sqrMagnitude;
            float t = len2 > 1e-8f ? Mathf.Clamp01(Vector2.Dot(p - a, ab) / len2) : 0f;
            return Vector2.Distance(p, a + ab * t);
        }
    }
}
