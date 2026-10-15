using System;
using UnityEngine;

namespace SurvivorSouls.Core
{
    /// <summary>One telemetry record (KAN-75). Plain struct: tracking allocates nothing.</summary>
    public readonly struct TelemetryEvent
    {
        /// <summary>Event name, one of the <see cref="GameTelemetry"/> constants.</summary>
        public readonly string Name;
        /// <summary>What it is about: skill id, chest tier.</summary>
        public readonly string Subject;
        /// <summary>Skill: old level (0 = new). Chest: upgrade cards offered.</summary>
        public readonly int From;
        /// <summary>Skill: new level. Chest: levels per upgrade.</summary>
        public readonly int To;
        /// <summary>Who caused it: pick / upgrade / debug (skills), skill / reward (chests).</summary>
        public readonly string Source;
        /// <summary>Seconds since the scene loaded (game time).</summary>
        public readonly float Time;
        public readonly Vector3 Position;
        /// <summary>Game version and build number of the player that tracked it (KAN-142, <see cref="BuildInfo"/>).</summary>
        public readonly string Version;
        public readonly int Build;

        public TelemetryEvent(string name, string subject, int from, int to, string source, float time, Vector3 position)
        {
            Name = name;
            Subject = subject;
            From = from;
            To = to;
            Source = source;
            Time = time;
            Position = position;
            BuildInfo build = BuildInfo.Current;
            Version = build.version;
            Build = build.build;
        }

        public override string ToString() =>
            $"{Time,7:0.0}s {Name} {Subject} {From}->{To} ({Source}) at ({Position.x:0.#}, {Position.z:0.#})";
    }

    /// <summary>
    /// Telemetry hook (KAN-75): events only, no backend yet. Gameplay code calls <see cref="Track"/>; a future analytics
    /// sender subscribes to <see cref="Tracked"/>. The last <see cref="RecentCapacity"/> events stay in a ring buffer (debug
    /// window, tests). Development players also print each event to the log (Player.log) so balance can be read from a
    /// dev build; the Editor and release builds stay quiet. Event names are the constants below.
    /// </summary>
    public static class GameTelemetry
    {
        public const string SkillUpgraded = "SkillUpgraded";
        public const string ChestDropped = "ChestDropped";
        public const string ChestOpened = "ChestOpened";
        public const string ChestExpired = "ChestExpired";

        public const int RecentCapacity = 64;

        private static readonly TelemetryEvent[] Ring = new TelemetryEvent[RecentCapacity];
        private static int _next, _count;

        /// <summary>Raised for every tracked event (analytics backend later).</summary>
        public static event Action<TelemetryEvent> Tracked;

        /// <summary>Print each event to the log. Default: development players only.</summary>
        public static bool LogToConsole { get; set; } = Debug.isDebugBuild && !Application.isEditor;

        /// <summary>Events tracked since the start (or the last <see cref="Clear"/>).</summary>
        public static int TotalCount { get; private set; }

        /// <summary>Events held in the ring buffer (at most <see cref="RecentCapacity"/>).</summary>
        public static int RecentCount => _count;

        public static void Track(in TelemetryEvent e)
        {
            Ring[_next] = e;
            _next = (_next + 1) % RecentCapacity;
            if (_count < RecentCapacity) _count++;
            TotalCount++;
            if (LogToConsole) Debug.Log("[Telemetry] " + e);
            Tracked?.Invoke(e);
        }

        /// <summary>The <paramref name="index"/>th newest event (0 = last one).</summary>
        public static TelemetryEvent Recent(int index)
        {
            if (index < 0 || index >= _count) throw new ArgumentOutOfRangeException(nameof(index));
            return Ring[(_next - 1 - index + RecentCapacity) % RecentCapacity];
        }

        /// <summary>Empties the buffer and the count (tests). Listeners stay.</summary>
        public static void Clear()
        {
            Array.Clear(Ring, 0, Ring.Length);
            _next = _count = 0;
            TotalCount = 0;
        }
    }
}
