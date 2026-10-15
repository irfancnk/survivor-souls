using System;
using UnityEngine;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// Text table + timing of the sealed-portal bubble (KAN-49). One asset (Assets/_Project/Data/UI/PortalSealLines.asset),
    /// the single place to edit or localise the lines. The game scene builder creates it once with these defaults and never
    /// overwrites it. A timed line reads <c>header</c> on top and <c>prefix + mm:ss + suffix</c> below (the time is its own
    /// label, so the live countdown allocates nothing). Boss lines have no time: the seal opens when the guardian falls.
    /// </summary>
    [CreateAssetMenu(menuName = "Survivor Souls/Portal Seal Lines", fileName = "PortalSealLines")]
    public sealed class PortalSealLines : ScriptableObject
    {
        [Serializable]
        public struct Line
        {
            public string header;
            public string prefix;
            public string suffix;

            public Line(string header, string prefix, string suffix)
            {
                this.header = header;
                this.prefix = prefix;
                this.suffix = suffix;
            }
        }

        [Tooltip("Survive Chambers: the seal opens when the clock ends.")]
        public Line[] timed =
        {
            new Line("The gate is sealed.", "Endure ", " more."),
            new Line("The seal holds.", "", " remains."),
            new Line("Not yet, wanderer.", "", "."),
            new Line("The flame within sleeps.", "It wakes in ", "."),
            new Line("Cold stone answers not.", "Survive ", " longer."),
        };

        [Tooltip("Boss Chambers: no clock, the seal breaks with the guardian.")]
        public Line[] boss =
        {
            new Line("The seal is bound to its keeper.", "Slay the guardian.", ""),
            new Line("Not while the keeper draws breath.", "Fell the guardian.", ""),
        };

        [Header("Timing (unscaled seconds)")]
        [Min(0.1f)] public float visibleSeconds = 2.5f;
        [Min(0f)] public float fadeIn = 0.15f;
        [Min(0f)] public float fadeOut = 0.3f;

        /// <summary>Code defaults for tests and a scene without the asset.</summary>
        public static PortalSealLines CreateDefault()
        {
            var lines = CreateInstance<PortalSealLines>();
            lines.name = "PortalSealLines (default)";
            lines.hideFlags = HideFlags.DontSave;
            return lines;
        }

        /// <summary>Random index in <paramref name="count"/> lines that is not <paramref name="last"/> (when there is a choice).</summary>
        public static int Pick(int count, int last, float random01)
        {
            if (count <= 0) return -1;
            if (count == 1) return 0;
            if (last < 0 || last >= count) return Mathf.Min(count - 1, (int)(random01 * count));
            int i = Mathf.Min(count - 2, (int)(random01 * (count - 1)));
            return i >= last ? i + 1 : i;
        }
    }
}
