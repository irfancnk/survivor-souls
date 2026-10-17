using SurvivorSouls.Gameplay.Chamber;
using UnityEngine;

namespace SurvivorSouls.Dev
{
    /// <summary>
    /// Dev-build helper for the island Chambers (KAN-245): <c>-ss-theme &lt;id&gt;</c> plays every non-boss Chamber in that island
    /// theme (<see cref="ChamberSetup.ForcedTheme"/>), e.g. <c>-ss-scene Game -ss-theme drowned_cathedral</c>. Dev builds only;
    /// no effect without the arg.
    /// </summary>
    public static class IslandShotArgs
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.isEditor || !Debug.isDebugBuild) return;
            var args = DevArgs.All; // command line or SS_ARGS (iOS)
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-ss-theme")
                {
                    ChamberSetup.ForcedTheme = args[i + 1];
                    Debug.Log($"[IslandShotArgs] theme {args[i + 1]}");
                    return;
                }
        }
    }
}
