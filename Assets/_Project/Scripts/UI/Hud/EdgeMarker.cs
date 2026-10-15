using UnityEngine;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// Off-screen marker math in panel space (x right, y down), used by <see cref="GateArrowHud"/> (KAN-63).
    /// Pure functions, no allocation: easy to test.
    /// </summary>
    public static class EdgeMarker
    {
        /// <summary>Whether <paramref name="p"/> is inside <paramref name="rect"/> shrunk by <paramref name="inset"/> on every side.</summary>
        public static bool Inside(Vector2 p, Rect rect, float inset) =>
            p.x >= rect.xMin + inset && p.x <= rect.xMax - inset && p.y >= rect.yMin + inset && p.y <= rect.yMax - inset;

        /// <summary>
        /// On-screen test with hysteresis, so the arrow does not flicker at the border: a target off screen must come
        /// <paramref name="enterInset"/> inside the view to count as on screen; once on screen it stays so until it is
        /// less than <paramref name="exitInset"/> inside (use exitInset &lt; enterInset).
        /// </summary>
        public static bool OnScreen(bool wasOnScreen, Vector2 p, Rect view, float enterInset, float exitInset) =>
            Inside(p, view, wasOnScreen ? exitInset : enterInset);

        /// <summary>
        /// Where the ray from <paramref name="from"/> in <paramref name="direction"/> leaves <paramref name="rect"/>.
        /// <paramref name="from"/> is clamped into the rect first. A zero direction returns the clamped start.
        /// </summary>
        public static Vector2 ProjectToEdge(Vector2 from, Vector2 direction, Rect rect)
        {
            from.x = Mathf.Clamp(from.x, rect.xMin, rect.xMax);
            from.y = Mathf.Clamp(from.y, rect.yMin, rect.yMax);
            float tx = direction.x > 1e-6f ? (rect.xMax - from.x) / direction.x
                : direction.x < -1e-6f ? (rect.xMin - from.x) / direction.x : float.PositiveInfinity;
            float ty = direction.y > 1e-6f ? (rect.yMax - from.y) / direction.y
                : direction.y < -1e-6f ? (rect.yMin - from.y) / direction.y : float.PositiveInfinity;
            float t = Mathf.Min(tx, ty);
            return float.IsInfinity(t) ? from : from + direction * t;
        }

        /// <summary>UI rotation (degrees, clockwise) that turns a sprite drawn tip-up toward <paramref name="direction"/> (panel space).</summary>
        public static float Angle(Vector2 direction) => Mathf.Atan2(direction.x, -direction.y) * Mathf.Rad2Deg;

        /// <summary>Physical millimetres to panel units: <paramref name="dpi"/> screen pixels per inch, <paramref name="panelPerPixel"/> panel units per screen pixel (KAN-176).</summary>
        public static float MillimetresToPanel(float millimetres, float dpi, float panelPerPixel) => millimetres / 25.4f * dpi * panelPerPixel;

        /// <summary>
        /// KAN-176: moves a marker (a square of half size <paramref name="half"/>, centre <paramref name="p"/> on the border of
        /// <paramref name="rect"/>) out of the no-go zones (HUD elements, idle joystick; already padded). First it slides along the
        /// same edge to the nearest free spot (a corner point tries both edges). Only when that whole edge line is blocked
        /// (for example the full-width XP bar at the top) does it step inward, just past the blocking zone, and slide again there.
        /// The result stays in <paramref name="rect"/>. Pure, no allocation.
        /// </summary>
        public static Vector2 AvoidZones(Vector2 p, Rect rect, float half, Rect[] zones, int zoneCount)
        {
            if (zones == null || zoneCount <= 0 || Free(p, half, zones, zoneCount)) return p;
            const float eps = 0.5f;
            bool top = p.y <= rect.yMin + eps, bottom = p.y >= rect.yMax - eps;
            bool left = p.x <= rect.xMin + eps, right = p.x >= rect.xMax - eps;
            if (!top && !bottom && !left && !right) top = true; // not on the border (degenerate rect): treat as top

            Vector2 best = p;
            float bestDist = float.PositiveInfinity;
            if (top || bottom) SlideNearest(p, true, rect, half, zones, zoneCount, ref best, ref bestDist);
            if (left || right) SlideNearest(p, false, rect, half, zones, zoneCount, ref best, ref bestDist);
            if (bestDist < float.PositiveInfinity) return best;

            // The whole edge line is blocked: step inward past the blocking zone, then try that line.
            bool alongX = top || bottom;
            Vector2 q = p;
            for (int step = 0; step < 8; step++)
            {
                int z = FirstOverlap(q, half, zones, zoneCount);
                if (z < 0) return q;
                Rect zone = zones[z];
                if (top) q.y = Mathf.Min(zone.yMax + half + 0.01f, rect.yMax);
                else if (bottom) q.y = Mathf.Max(zone.yMin - half - 0.01f, rect.yMin);
                else if (left) q.x = Mathf.Min(zone.xMax + half + 0.01f, rect.xMax);
                else q.x = Mathf.Max(zone.xMin - half - 0.01f, rect.xMin);
                if (Free(q, half, zones, zoneCount)) return q;
                bestDist = float.PositiveInfinity;
                SlideNearest(q, alongX, rect, half, zones, zoneCount, ref best, ref bestDist);
                if (bestDist < float.PositiveInfinity) return best;
            }
            return q;
        }

        /// <summary>Whether the marker square at <paramref name="p"/> touches no zone.</summary>
        public static bool Free(Vector2 p, float half, Rect[] zones, int zoneCount) => FirstOverlap(p, half, zones, zoneCount) < 0;

        private static int FirstOverlap(Vector2 p, float half, Rect[] zones, int zoneCount)
        {
            var box = new Rect(p.x - half, p.y - half, half * 2f, half * 2f);
            for (int i = 0; i < zoneCount; i++)
                if (zones[i].Overlaps(box)) return i;
            return -1;
        }

        /// <summary>Nearest free spot on the same line (x varies when <paramref name="alongX"/>), next to a zone end.</summary>
        private static void SlideNearest(Vector2 p, bool alongX, Rect rect, float half, Rect[] zones, int zoneCount, ref Vector2 best, ref float bestDist)
        {
            for (int i = 0; i < zoneCount; i++)
            {
                Rect z = zones[i];
                for (int s = 0; s < 2; s++)
                {
                    Vector2 c = p;
                    if (alongX) c.x = Mathf.Clamp(s == 0 ? z.xMin - half - 0.01f : z.xMax + half + 0.01f, rect.xMin, rect.xMax);
                    else c.y = Mathf.Clamp(s == 0 ? z.yMin - half - 0.01f : z.yMax + half + 0.01f, rect.yMin, rect.yMax);
                    float d = alongX ? Mathf.Abs(c.x - p.x) : Mathf.Abs(c.y - p.y);
                    if (d < bestDist && Free(c, half, zones, zoneCount))
                    {
                        best = c;
                        bestDist = d;
                    }
                }
            }
        }
    }
}
