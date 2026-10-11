using UnityEngine;
using UnityEngine.SceneManagement;

namespace SurvivorSouls.Core
{
    /// <summary>
    /// Thin wrapper over SceneManager so callers can check availability first.
    /// Later: add a loading screen here, not in callers.
    /// </summary>
    public static class SceneLoader
    {
        public static bool IsInBuild(string sceneName) => Application.CanStreamedLevelBeLoaded(sceneName);

        public static AsyncOperation LoadAsync(string sceneName)
        {
            return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        }
    }
}
