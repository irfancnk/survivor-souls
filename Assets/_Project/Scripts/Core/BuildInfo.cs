using System;
using UnityEngine;

namespace SurvivorSouls.Core
{
    /// <summary>
    /// Version of this build (KAN-142). The editor build stamp (<c>Editor/Platform/VersionStamp</c>) writes
    /// <c>Resources/BuildInfo.json</c> from <c>VERSION.json</c> + the git commit before every build and puts the
    /// committed placeholder (<c>0.0.0-dev</c>) back after it, so the editor shows the placeholder.
    /// </summary>
    [Serializable]
    public sealed class BuildInfo
    {
        public const string ResourceName = "BuildInfo";
        public const string PlaceholderVersion = "0.0.0-dev";

        public string version = PlaceholderVersion;
        /// <summary>Only goes up, +1 per version (iOS CFBundleVersion, Android versionCode).</summary>
        public int build;
        /// <summary>Short git commit hash of the build ("" when unknown).</summary>
        public string commit = "";
        /// <summary>Build date, UTC, ISO 8601 ("" in the placeholder).</summary>
        public string date = "";

        private static BuildInfo _current;

        /// <summary>The version this player was built with (placeholder in the editor). Loaded once.</summary>
        public static BuildInfo Current => _current ??= Load();

        /// <summary>Menu text: <c>v0.3.0 (14)</c>; dev builds add the commit: <c>v0.3.0 (14) a1b2c3d</c>.</summary>
        public static string DisplayLabel => Current.Label(Debug.isDebugBuild);

        public string Label(bool withCommit)
        {
            string label = build > 0 ? $"v{version} ({build})" : $"v{version}";
            return withCommit && !string.IsNullOrEmpty(commit) ? $"{label} {commit}" : label;
        }

        /// <summary>Parses BuildInfo.json; bad or empty text gives the placeholder.</summary>
        public static BuildInfo Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new BuildInfo();
            try
            {
                var info = JsonUtility.FromJson<BuildInfo>(json) ?? new BuildInfo();
                if (string.IsNullOrEmpty(info.version)) info.version = PlaceholderVersion;
                info.commit ??= "";
                info.date ??= "";
                return info;
            }
            catch (ArgumentException)
            {
                return new BuildInfo();
            }
        }

        private static BuildInfo Load()
        {
            var asset = Resources.Load<TextAsset>(ResourceName);
            var info = Parse(asset != null ? asset.text : null);
            if (asset != null) Resources.UnloadAsset(asset);
            return info;
        }

        /// <summary>Drops the cached value (tests, after the stamp rewrote the file).</summary>
        public static void Reload() => _current = null;
    }
}
