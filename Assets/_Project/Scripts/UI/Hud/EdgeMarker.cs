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
    }
}
