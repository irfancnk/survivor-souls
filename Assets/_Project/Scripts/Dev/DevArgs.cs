using System;

namespace SurvivorSouls.Dev
{
    /// <summary>
    /// Launch arguments for the dev helpers (<see cref="DevScreenshot"/>, <see cref="HealthBarShotArgs"/>,
    /// <see cref="SpecialMeleeShotArgs"/>, <see cref="ChargeFxShotArgs"/>, <see cref="DevDebugPanel"/>; new dev arg readers use this too)
    /// (KAN-179).
    /// Normally the process command line. When the command line has no
    /// <c>-ss-</c> argument, the space-separated <c>SS_ARGS</c> environment variable is used instead, so a launcher
    /// that cannot pass arguments still reaches the helpers, e.g. on iOS:
    /// <c>xcrun devicectl device process launch --device &lt;UDID&gt; --environment-variables '{"SS_ARGS":"-ss-scene Game ..."}' com.irfancnk.survivorsouls</c>.
    /// Values with spaces are not supported in <c>SS_ARGS</c>. Read once and cached.
    /// </summary>
    public static class DevArgs
    {
        public const string EnvVar = "SS_ARGS";

        private static string[] _all;

        /// <summary>The arguments, element 0 = the executable path (like <see cref="Environment.GetCommandLineArgs"/>).</summary>
        public static string[] All
        {
            get
            {
                if (_all == null)
                {
                    _all = Read(Environment.GetCommandLineArgs(), Environment.GetEnvironmentVariable(EnvVar), out bool fromEnv);
                    Source = fromEnv ? EnvVar : "argv";
                }
                return _all;
            }
        }

        /// <summary>Where <see cref="All"/> came from: "argv" or "SS_ARGS" (valid after the first <see cref="All"/> read).</summary>
        public static string Source { get; private set; } = "argv";

        /// <summary>The command line, or <c>exe + env</c> when the command line has no <c>-ss-</c> argument and env is set.</summary>
        public static string[] Read(string[] commandLine, string env, out bool fromEnv)
        {
            fromEnv = false;
            commandLine ??= Array.Empty<string>();
            for (int i = 1; i < commandLine.Length; i++)
                if (commandLine[i] != null && commandLine[i].StartsWith("-ss-", StringComparison.Ordinal))
                    return commandLine;

            if (string.IsNullOrWhiteSpace(env)) return commandLine;

            string[] extra = env.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            var all = new string[extra.Length + 1];
            all[0] = commandLine.Length > 0 ? commandLine[0] : "";
            Array.Copy(extra, 0, all, 1, extra.Length);
            fromEnv = true;
            return all;
        }
    }
}
