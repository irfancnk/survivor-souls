using System;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Chamber
{
    /// <summary>
    /// Which island theme each Chamber of a Realm plays in (KAN-245). One entry per non-boss Chamber number; a Chamber with
    /// no entry keeps the streamed shore. Chamber numbers past <see cref="cycle"/> wrap (a 20-Chamber Realm repeats the
    /// 10-Chamber order). Boss Chambers never use a theme (the boss arena wins in <see cref="ChamberSetup"/>).
    /// Data: Data/Chamber/ChamberThemes_Realm1.asset (made by the IslandChamberStep; hand edits to the asset are kept).
    /// </summary>
    [CreateAssetMenu(menuName = "Survivor Souls/Chamber Theme Schedule", fileName = "ChamberThemes")]
    public sealed class ChamberThemeSchedule : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [Min(1)] public int chamber;
            public string theme;
        }

        [Tooltip("Realm this schedule belongs to.")]
        [SerializeField, Min(1)] private int realm = 1;
        [Tooltip("Chamber numbers repeat after this many (the Realm length the order was made for).")]
        [SerializeField, Min(1)] private int cycle = 10;
        [SerializeField] private Entry[] entries = Array.Empty<Entry>();

        public int Realm => realm;
        public int Cycle => cycle;
        public int Count => entries.Length;

        /// <summary>Theme id of <paramref name="chamber"/> in <paramref name="forRealm"/>, or null (streamed shore).</summary>
        public string ThemeFor(int forRealm, int chamber)
        {
            if (forRealm != realm || chamber < 1) return null;
            int c = (chamber - 1) % Mathf.Max(1, cycle) + 1;
            foreach (var e in entries)
                if (e.chamber == c && !string.IsNullOrEmpty(e.theme)) return e.theme;
            return null;
        }

        /// <summary>Tests and the builder: a schedule from code.</summary>
        public static ChamberThemeSchedule Create(int forRealm, int repeatEvery, params Entry[] list)
        {
            var s = CreateInstance<ChamberThemeSchedule>();
            s.realm = forRealm;
            s.cycle = repeatEvery;
            s.entries = list ?? Array.Empty<Entry>();
            return s;
        }
    }
}
