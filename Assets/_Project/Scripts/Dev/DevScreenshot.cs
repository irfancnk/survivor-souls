using System;
using System.Collections;
using System.Globalization;
using SurvivorSouls.Gameplay.CameraRig;
using SurvivorSouls.Gameplay.Controls;
using SurvivorSouls.Gameplay.Player;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SurvivorSouls.Dev
{
    /// <summary>
    /// Automation hook for development builds. Launch the player with
    /// <c>-ss-screenshot &lt;path.png&gt; [-ss-delay &lt;seconds&gt;] [-ss-quality &lt;name&gt;]</c>
    /// to save one frame and quit. -ss-quality picks a quality level by name (e.g. Mobile) to preview mobile visuals on desktop.
    /// Game options: <c>-ss-scene &lt;name&gt;</c> loads a scene first; <c>-ss-move x,y</c> holds the joystick;
    /// <c>-ss-closeup &lt;yawDeg&gt;</c> frames the player up close (0 = from the front).
    /// <c>-ss-near-exit</c> moves the player in front of the exit door (open world).
    /// <c>-ss-pos x,z</c> moves the player to a world position first (e.g. the shore).
    /// Every capture also logs render stats of the last second: <c>[DevScreenshot] Stats ...</c> (draw calls, batches,
    /// SetPass calls, triangles, average frame ms).
    /// Used by tools/agents to verify visuals without a human. Inactive in release builds (Debug.isDebugBuild).
    /// </summary>
    public sealed class DevScreenshot : MonoBehaviour
    {
        private string _path;
        private float _delay;
        private string _scene;
        private string _move;
        private string _closeup;
        private bool _nearExit;
        private string _pos;

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
            shot._delay = float.TryParse(GetArg("-ss-delay"), NumberStyles.Float, CultureInfo.InvariantCulture, out float d) ? d : 3f;
            shot._scene = GetArg("-ss-scene");
            shot._move = GetArg("-ss-move");
            shot._closeup = GetArg("-ss-closeup");
            shot._nearExit = Array.IndexOf(Environment.GetCommandLineArgs(), "-ss-near-exit") >= 0;
            shot._pos = GetArg("-ss-pos");
        }

        private IEnumerator Start()
        {
            if (!string.IsNullOrEmpty(_scene))
            {
                SceneManager.LoadScene(_scene);
                yield return null;
                yield return null;
            }
            if (_nearExit) MoveNearExit();
            if (!string.IsNullOrEmpty(_pos))
            {
                string[] xz = _pos.Split(',');
                Teleport(new Vector3(float.Parse(xz[0], CultureInfo.InvariantCulture), 0f, float.Parse(xz[1], CultureInfo.InvariantCulture)));
            }
            if (!string.IsNullOrEmpty(_move))
            {
                string[] xy = _move.Split(',');
                FloatingJoystick.SimulatedValue = new Vector2(
                    float.Parse(xy[0], CultureInfo.InvariantCulture), float.Parse(xy[1], CultureInfo.InvariantCulture));
            }

            yield return new WaitForSecondsRealtime(Mathf.Max(0f, _delay - 1f));
            yield return LogStats(1f);
            if (!string.IsNullOrEmpty(_closeup)) FrameCloseup(float.Parse(_closeup, CultureInfo.InvariantCulture));
            yield return null;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(_path);
            yield return new WaitForSecondsRealtime(1f); // capture writes asynchronously
            Debug.Log($"[DevScreenshot] Saved {_path}");
            Application.Quit();
        }

        /// <summary>Averages render counters over <paramref name="seconds"/> (development players expose them).</summary>
        private static IEnumerator LogStats(float seconds)
        {
            using var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            using var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            using var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            using var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            long d = 0, b = 0, sp = 0, t = 0;
            int frames = 0;
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < seconds)
            {
                yield return null;
                d += draws.LastValue; b += batches.LastValue; sp += setPass.LastValue; t += tris.LastValue;
                frames++;
            }
            frames = Mathf.Max(1, frames);
            float ms = (Time.realtimeSinceStartup - start) * 1000f / frames;
            Debug.Log($"[DevScreenshot] Stats drawCalls={d / frames} batches={b / frames} setPass={sp / frames} tris={t / frames} frameMs={ms:F1}");
        }

        private static void MoveNearExit()
        {
            var world = FindAnyObjectByType<SurvivorSouls.Gameplay.World.WorldStreamer>();
            if (world != null) Teleport(world.ExitPosition + new Vector3(0f, 0f, -5.5f));
        }

        private static void Teleport(Vector3 position)
        {
            var player = FindAnyObjectByType<PlayerMover>();
            if (player == null) return;
            var cc = player.GetComponent<CharacterController>();
            cc.enabled = false;
            player.transform.position = position;
            cc.enabled = true;
        }

        /// <summary>Stops the follow camera and frames the player's upper body from <paramref name="yaw"/> degrees.</summary>
        private static void FrameCloseup(float yaw)
        {
            var player = FindAnyObjectByType<PlayerMover>();
            var cam = Camera.main;
            if (player == null || cam == null) return;
            var follow = cam.GetComponent<TopDownFollowCamera>();
            if (follow != null) follow.enabled = false;
            foreach (var doc in FindObjectsByType<UIDocument>()) doc.gameObject.SetActive(false); // hide HUD

            Transform t = player.transform;
            Vector3 dir = Quaternion.AngleAxis(yaw, Vector3.up) * t.forward;
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 32f;
            cam.transform.position = t.position + dir * 3.6f + Vector3.up * 1.45f;
            cam.transform.LookAt(t.position + Vector3.up * 0.98f);
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
