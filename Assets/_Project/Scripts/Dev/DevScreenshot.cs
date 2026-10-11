using System;
using System.Collections;
using UnityEngine;

namespace SurvivorSouls.Dev
{
    /// <summary>
    /// Automation hook for development builds. Launch the player with
    /// <c>-ss-screenshot &lt;path.png&gt; [-ss-delay &lt;seconds&gt;] [-ss-quality &lt;name&gt;]</c>
    /// to save one frame and quit. -ss-quality picks a quality level by name (e.g. Mobile) to preview mobile visuals on desktop.
    /// Used by tools/agents to verify visuals without a human. Inactive in release builds (Debug.isDebugBuild).
    /// </summary>
    public sealed class DevScreenshot : MonoBehaviour
    {
        private string _path;
        private float _delay;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.isEditor || !Debug.isDebugBuild) return;
            string path = GetArg("-ss-screenshot");
            if (string.IsNullOrEmpty(path)) return;

            Application.runInBackground = true; // keep frames ticking when the window is not focused
            ApplyQualityOverride(GetArg("-ss-quality"));

            var go = new GameObject(nameof(DevScreenshot));
            DontDestroyOnLoad(go);
            var shot = go.AddComponent<DevScreenshot>();
            shot._path = path;
            shot._delay = float.TryParse(GetArg("-ss-delay"), out float d) ? d : 3f;
        }

        private IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(_delay);
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(_path);
            yield return new WaitForSecondsRealtime(1f); // capture writes asynchronously
            Debug.Log($"[DevScreenshot] Saved {_path}");
            Application.Quit();
        }

        private static void ApplyQualityOverride(string qualityName)
        {
            if (string.IsNullOrEmpty(qualityName)) return;
            int index = Array.IndexOf(QualitySettings.names, qualityName);
            if (index >= 0) QualitySettings.SetQualityLevel(index, applyExpensiveChanges: true);
            else Debug.LogWarning($"[DevScreenshot] Unknown quality level '{qualityName}'");
        }

        private static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
