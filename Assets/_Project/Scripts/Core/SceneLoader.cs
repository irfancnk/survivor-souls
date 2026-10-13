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
            GamePause.Clear(); // a pause (level-up screen) must not freeze the next scene
            return SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Single);
        }
    }
}
