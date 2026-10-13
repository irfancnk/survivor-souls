using System;
using System.Collections;
using System.Globalization;
using SurvivorSouls.Gameplay.CameraRig;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Controls;
using SurvivorSouls.Gameplay.Player;
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
    /// <c>-ss-player-hp &lt;0..1&gt;</c> sets the player's health fraction after load (0 = death screen).
    /// <c>-ss-hit-marker &lt;hit|kill&gt;</c> pops a hit (or kill) marker on the nearest enemy just before the capture.
    /// <c>-ss-release &lt;seconds&gt;</c> lets go of the -ss-move stick that long before the capture (e.g. wind trail tail-off).
    /// <c>-ss-souls &lt;n&gt;</c> drops n soul wisps around the player after load (mostly small, some elite, one boss orb).
    /// <c>-ss-xp &lt;amount&gt;</c> grants that much XP <see cref="XpLead"/> s before the capture (level-up burst + screen).
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
        private string _playerHp;
        private string _hitMarker;
        private float _release;
        private int _souls;
        private int _xp;

        private const float XpLead = 0.9f; // realtime seconds between the XP grant and the capture

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
            shot._playerHp = GetArg("-ss-player-hp");
            shot._hitMarker = GetArg("-ss-hit-marker");
            shot._release = float.TryParse(GetArg("-ss-release"), NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ? r : 0f;
            shot._souls = int.TryParse(GetArg("-ss-souls"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;
            shot._xp = int.TryParse(GetArg("-ss-xp"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ? x : 0;
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
            if (!string.IsNullOrEmpty(_playerHp)) SetPlayerHealth(float.Parse(_playerHp, CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(_move))
            {
                string[] xy = _move.Split(',');
                FloatingJoystick.SimulatedValue = new Vector2(
                    float.Parse(xy[0], CultureInfo.InvariantCulture), float.Parse(xy[1], CultureInfo.InvariantCulture));
            }

            if (_souls > 0) DropSouls(_souls);

            float release = Mathf.Clamp(_release, 0f, _delay);
            float xpLead = _xp > 0 ? Mathf.Min(XpLead, _delay - release) : 0f;
            yield return new WaitForSecondsRealtime(_delay - release - xpLead);
            if (_xp > 0)
            {
                FindAnyObjectByType<SurvivorSouls.Gameplay.Progression.XpManager>()?.AddXp(_xp);
                yield return new WaitForSecondsRealtime(xpLead);
            }
            if (release > 0f)
            {
                FloatingJoystick.SimulatedValue = null; // finger up: stick stops and glides back to idle
                yield return new WaitForSecondsRealtime(release);
            }
            if (!string.IsNullOrEmpty(_closeup)) FrameCloseup(float.Parse(_closeup, CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(_hitMarker)) ShowHitMarker(_hitMarker == "kill");
            yield return null;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(_path);
            yield return new WaitForSecondsRealtime(1f); // capture writes asynchronously
            Debug.Log($"[DevScreenshot] Saved {_path}");
            Application.Quit();
        }

        private static void MoveNearExit()
        {
            var world = FindAnyObjectByType<SurvivorSouls.Gameplay.World.WorldStreamer>();
            var player = FindAnyObjectByType<PlayerMover>();
            if (world == null || player == null) return;
            var cc = player.GetComponent<CharacterController>();
            cc.enabled = false;
            player.transform.position = world.ExitPosition + new Vector3(0f, 0f, -5.5f);
            cc.enabled = true;
        }

        private static void DropSouls(int count)
        {
            var field = FindAnyObjectByType<SurvivorSouls.Gameplay.Progression.SoulWispField>();
            var player = FindAnyObjectByType<PlayerMover>();
            if (field == null || player == null) return;
            for (int i = 0; i < count; i++)
            {
                float a = i * 2.39996f; // golden angle: an even scatter
                float d = 3.2f + (i % 5) * 0.9f;
                int xp = i == 0 ? 150 : i % 7 == 3 ? 40 : 10;
                field.Spawn(player.transform.position + new Vector3(Mathf.Cos(a) * d, 1f, Mathf.Sin(a) * d), xp);
            }
        }

        private static void SetPlayerHealth(float fraction)
        {
            var health = FindAnyObjectByType<PlayerHealth>();
            if (health != null) health.TakeDamage(health.Max * (1f - Mathf.Clamp01(fraction)));
        }

        private static void ShowHitMarker(bool kill)
        {
            var hud = FindAnyObjectByType<SurvivorSouls.UI.Hud.HitMarkerHud>();
            var player = FindAnyObjectByType<PlayerMover>();
            if (hud == null || player == null) return;
            Enemy nearest = null;
            float best = float.MaxValue;
            foreach (var e in Enemy.Active)
            {
                float d = (e.transform.position - player.transform.position).sqrMagnitude;
                if (d < best) { best = d; nearest = e; }
            }
            if (nearest != null) hud.Show(nearest, kill);
            else Debug.LogWarning("[DevScreenshot] -ss-hit-marker: no enemy alive");
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
