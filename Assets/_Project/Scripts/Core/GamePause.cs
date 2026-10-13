using UnityEngine;

namespace SurvivorSouls.Core
{
    /// <summary>
    /// Game-wide pause by hold count: every system that needs the world frozen (level-up choice, later a pause menu)
    /// calls <see cref="Acquire"/> and later <see cref="Release"/>. Time scale is 0 while at least one hold is open and
    /// goes back to the value it had before the first hold. Two systems pausing at once cannot unpause each other.
    /// UI and effects that must keep moving during a pause use unscaled time.
    /// <see cref="Clear"/> drops every hold (scene change), so a pause can never leak into the next scene.
    /// </summary>
    public static class GamePause
    {
        private static int _holds;
        private static float _resumeScale = 1f;

        public static bool IsPaused => _holds > 0;

        public static void Acquire()
        {
            if (_holds++ == 0)
            {
                _resumeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
                Time.timeScale = 0f;
            }
        }

        public static void Release()
        {
            if (_holds == 0) return;
            if (--_holds == 0) Time.timeScale = _resumeScale;
        }

        public static void Clear()
        {
            if (_holds > 0) Time.timeScale = _resumeScale;
            _holds = 0;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _holds = 0;
            _resumeScale = 1f;
        }
    }
}
