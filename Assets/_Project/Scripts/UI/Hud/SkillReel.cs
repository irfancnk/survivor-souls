using UnityEngine;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// Slot-machine reel math for the skill pick (KAN-159). Pure functions, no state, no allocation: the screen keeps one
    /// reel per card and asks where it is. Units: the reel position counts items (one item = one skill icon + name moving
    /// one slot down). The reel runs at full speed, then slows down linearly over the last <c>decel</c> seconds and stops
    /// exactly on item <c>stopItem</c> at <c>stopTime</c>; the screen shows the rolled skill on that item.
    /// </summary>
    public static class SkillReel
    {
        /// <summary>
        /// Item the reel stops on, so it runs at about <paramref name="speed"/> items per second until the slow-down
        /// (distance with a linear stop = speed x (stopTime - decel / 2)). At least 2: every reel passes other skills.
        /// </summary>
        public static int StopItem(float stopTime, float decel, float speed)
        {
            float d = Mathf.Clamp(decel, 0f, stopTime);
            return Mathf.Max(2, Mathf.RoundToInt(speed * (stopTime - d * 0.5f)));
        }

        /// <summary>
        /// Reel position (items) at <paramref name="t"/> seconds after the roll started: 0 at t = 0, constant speed, linear
        /// slow-down over the last <paramref name="decel"/> s, exactly <paramref name="stopItem"/> from <paramref name="stopTime"/> on.
        /// </summary>
        public static float Position(float t, float stopTime, int stopItem, float decel)
        {
            if (stopTime <= 0f || t >= stopTime) return stopItem;
            if (t <= 0f) return 0f;
            float u = t / stopTime;
            float a = 1f - Mathf.Clamp(decel / stopTime, 0f, 1f); // share of the time at full speed
            float s = 2f / (1f + a);                                // full speed, so the area under the curve is 1
            float f = u <= a ? s * u : s * a + s * ((u - a) - (u - a) * (u - a) / (2f * (1f - a)));
            return Mathf.Min(stopItem, f * stopItem);
        }

        /// <summary>Reel speed at <paramref name="t"/> as a share of full speed (1 = full, 0 = stopped). Drives the stretch.</summary>
        public static float Speed01(float t, float stopTime, float decel)
        {
            if (t >= stopTime || stopTime <= 0f) return 0f;
            if (t <= 0f) return 1f;
            float slow = stopTime - Mathf.Clamp(decel, 0f, stopTime);
            return t <= slow ? 1f : (stopTime - t) / Mathf.Max(1e-4f, stopTime - slow);
        }

        /// <summary>
        /// Pool index shown on <paramref name="item"/> for a reel with start offset <paramref name="seed"/>: the pool in
        /// order, so two items in a row differ when the pool has 2+ skills. The stop item itself shows the rolled skill
        /// (the screen handles that); <paramref name="avoid"/> (the rolled skill's pool index, -1 = none) is skipped on the
        /// item just before the stop, so the reel never shows the same skill twice as it lands.
        /// </summary>
        public static int PoolIndex(int item, int stopItem, int seed, int poolCount, int avoid = -1)
        {
            if (poolCount <= 0) return -1;
            int k = Mod(seed + item, poolCount);
            if (item == stopItem - 1 && k == avoid && poolCount > 1) k = Mod(k + 1, poolCount);
            return k;
        }

        /// <summary>Overshoot of the landing bounce (0..1 of its amplitude) at <paramref name="k"/> = 0..1 of the bounce time.</summary>
        public static float Bounce(float k) => k <= 0f || k >= 1f ? 0f : Mathf.Sin(k * Mathf.PI) * (1f - k);

        private static int Mod(int a, int n)
        {
            int r = a % n;
            return r < 0 ? r + n : r;
        }
    }
}
