using System;
using System.Collections;
using System.Globalization;
using SurvivorSouls.Gameplay.CameraRig;
using SurvivorSouls.Gameplay.Combat;
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
    /// <c>-ss-player-hp &lt;0..1&gt;</c> sets the player's health fraction after load (0 = death screen).
    /// <c>-ss-hit-marker &lt;hit|kill&gt;</c> pops a hit (or kill) marker on the nearest enemy just before the capture.
    /// <c>-ss-release &lt;seconds&gt;</c> lets go of the -ss-move stick that long before the capture (e.g. wind trail tail-off).
    /// <c>-ss-souls &lt;n&gt;</c> drops n soul wisps around the player after load (mostly small, some elite, one boss orb).
    /// <c>-ss-xp &lt;amount&gt;</c> grants that much XP <see cref="XpLead"/> s before the capture (level-up burst + skill cards).
    /// <c>-ss-skills id,id,...</c> grants those skills (by SkillDefinition id, repeat an id to stack) after load.
    /// <c>-ss-horde &lt;n&gt;</c> places n enemies (wave table mix of the last phase) on a ring 4-11 m round the player after
    /// load, from the spawner's pools (perf runs; KAN-30). <c>-ss-immortal</c> turns off contact damage on the player.
    /// <c>-ss-hold &lt;n&gt;</c> keeps at least n enemies alive: every second, missing ones are spawned off screen (perf soak).
    /// <c>-ss-timer &lt;seconds&gt;</c> sets the Chamber survival clock after load (KAN-39; 0 = the portal opens now).
    /// <c>-ss-portal</c> walks the Knight into the portal right after load (KAN-40): pull, white fade, next Chamber,
    /// title card. With -ss-delay ~2.2 the capture shows the "CHAMBER II" card; skills / health set before it carry over.
    /// <c>-ss-run-seed &lt;n&gt;</c> starts the run with that seed and <c>-ss-chamber &lt;n&gt;</c> starts it at that Chamber
    /// (KAN-46: layout seed + difficulty of that Chamber; same run seed + Chamber = same layout).
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
        private string _playerHp;
        private string _hitMarker;
        private float _release;
        private int _souls;
        private int _xp;
        private string _skills;
        private int _horde;
        private int _hold;
        private bool _immortal;
        private string _timer;
        private bool _portal;

        private const float XpLead = 1.5f; // realtime seconds between the XP grant and the capture (cards fully in)

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
            shot._playerHp = GetArg("-ss-player-hp");
            shot._hitMarker = GetArg("-ss-hit-marker");
            shot._release = float.TryParse(GetArg("-ss-release"), NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ? r : 0f;
            shot._souls = int.TryParse(GetArg("-ss-souls"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;
            shot._skills = GetArg("-ss-skills");
            shot._xp = int.TryParse(GetArg("-ss-xp"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ? x : 0;
            shot._horde = int.TryParse(GetArg("-ss-horde"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) ? h : 0;
            shot._hold = int.TryParse(GetArg("-ss-hold"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int hold) ? hold : 0;
            shot._immortal = Array.IndexOf(Environment.GetCommandLineArgs(), "-ss-immortal") >= 0;
            shot._timer = GetArg("-ss-timer");
            shot._portal = Array.IndexOf(Environment.GetCommandLineArgs(), "-ss-portal") >= 0;

            // Before any scene wakes: ChamberSetup reads the run in Awake (KAN-46).
            int runSeed = int.TryParse(GetArg("-ss-run-seed"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int rs) ? rs : 0;
            int chamber = int.TryParse(GetArg("-ss-chamber"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int c) ? c : 0;
            if (runSeed != 0 || chamber > 0)
            {
                SurvivorSouls.Gameplay.Chamber.RunState.NewRun(1, runSeed);
                if (chamber > 0) SurvivorSouls.Gameplay.Chamber.RunState.JumpToChamber(chamber);
            }
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
            if (!string.IsNullOrEmpty(_playerHp)) SetPlayerHealth(float.Parse(_playerHp, CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(_move))
            {
                string[] xy = _move.Split(',');
                FloatingJoystick.SimulatedValue = new Vector2(
                    float.Parse(xy[0], CultureInfo.InvariantCulture), float.Parse(xy[1], CultureInfo.InvariantCulture));
            }

            if (_souls > 0) DropSouls(_souls);
            if (_immortal)
                foreach (var c in FindObjectsByType<ContactDamageReceiver>()) c.enabled = false;
            if (_horde > 0) PlaceHorde(_horde);
            if (_hold > 0) StartCoroutine(HoldHorde(_hold));
            if (!string.IsNullOrEmpty(_skills)) GrantSkills(_skills);
            if (!string.IsNullOrEmpty(_timer))
            {
                var timer = SurvivorSouls.Gameplay.Chamber.SurvivalTimer.Active;
                if (timer == null) Debug.LogWarning("[DevScreenshot] -ss-timer: no survival timer in the scene");
                else timer.SetRemaining(float.Parse(_timer, CultureInfo.InvariantCulture));
            }

            if (_portal) EnterPortal();

            float release = Mathf.Clamp(_release, 0f, _delay);
            float xpLead = _xp > 0 ? Mathf.Min(XpLead, _delay - release) : 0f;
            float wait = _delay - release - xpLead;
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, wait - 1f));
            yield return LogStats(Mathf.Min(1f, wait));
            var spawner = FindAnyObjectByType<SurvivorSouls.Gameplay.Combat.EnemySpawner>();
            if (spawner != null) Debug.Log($"[DevScreenshot] Spawner t={spawner.RunTime:F0}s inUse={spawner.InUse} {spawner.Stats}");
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
            if (_portal) Debug.Log($"[DevScreenshot] Chamber {SurvivorSouls.Gameplay.Chamber.RunState.Chamber} (Realm {SurvivorSouls.Gameplay.Chamber.RunState.Realm})");
            Debug.Log($"[DevScreenshot] Saved {_path}");
            Application.Quit();
        }

        /// <summary>Starts the portal exit as if the Knight walked into the arch 1.5 m ahead (KAN-40).</summary>
        private static void EnterPortal()
        {
            var transition = SurvivorSouls.Gameplay.Chamber.ChamberTransition.Active;
            var player = FindAnyObjectByType<PlayerHealth>();
            if (transition == null || player == null)
            {
                Debug.LogWarning("[DevScreenshot] -ss-portal: no ChamberTransition or player in the scene");
                return;
            }
            var t = player.transform;
            bool started = transition.Enter(t.position + t.forward * 1.5f);
            Debug.Log($"[DevScreenshot] Portal enter started={started}");
        }

        /// <summary>Averages render counters over <paramref name="seconds"/> (development players expose them).</summary>
        private static IEnumerator LogStats(float seconds)
        {
            using var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            using var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            using var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            using var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            using var main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            long d = 0, b = 0, sp = 0, t = 0, m = 0, mMax = 0;
            int frames = 0, enemies = 0;
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < seconds)
            {
                yield return null;
                d += draws.LastValue; b += batches.LastValue; sp += setPass.LastValue; t += tris.LastValue;
                m += main.LastValue;
                mMax = Math.Max(mMax, main.LastValue);
                enemies += Enemy.Active.Count;
                frames++;
            }
            frames = Mathf.Max(1, frames);
            float ms = (Time.realtimeSinceStartup - start) * 1000f / frames;
            Debug.Log($"[DevScreenshot] Stats drawCalls={d / frames} batches={b / frames} setPass={sp / frames} tris={t / frames} frameMs={ms:F1} " +
                      $"mainThreadMs={m / frames * 1e-6f:F2} mainThreadMaxMs={mMax * 1e-6f:F2} enemies={enemies / frames}");
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

        /// <summary>Puts <paramref name="count"/> enemies round the player, types picked like the last wave phase.</summary>
        private static void PlaceHorde(int count)
        {
            var spawner = FindAnyObjectByType<EnemySpawner>();
            var player = FindAnyObjectByType<PlayerMover>();
            var waves = spawner != null ? spawner.Waves : null;
            if (player == null || waves == null || waves.PhaseCount == 0) return;
            var phase = waves.GetPhase(waves.PhaseCount - 1);
            for (int i = 0; i < count; i++)
            {
                float a = i * 2.39996f;
                float r = 4f + 7f * Mathf.Sqrt((i + 0.5f) / count);
                var prefab = EnemyWaveTable.PickEnemy(phase, UnityEngine.Random.value);
                if (prefab != null) spawner.SpawnAt(prefab, player.transform.position + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
        }

        /// <summary>Tops the field up to <paramref name="count"/> enemies once a second (off-screen spawn points).</summary>
        private static IEnumerator HoldHorde(int count)
        {
            var spawner = FindAnyObjectByType<EnemySpawner>();
            var waves = spawner != null ? spawner.Waves : null;
            if (waves == null || waves.PhaseCount == 0) yield break;
            var phase = waves.GetPhase(waves.PhaseCount - 1);
            while (spawner != null)
            {
                for (int i = Enemy.Active.Count; i < count; i++)
                {
                    var prefab = EnemyWaveTable.PickEnemy(phase, UnityEngine.Random.value);
                    if (prefab == null || !spawner.TryFindSpawnPoint(out var pos)) break;
                    spawner.SpawnAt(prefab, pos);
                }
                yield return new WaitForSecondsRealtime(1f);
            }
        }

        private static void GrantSkills(string ids)
        {
            var manager = FindAnyObjectByType<SurvivorSouls.Gameplay.Skills.SkillManager>();
            var table = manager != null ? manager.Progression : null;
            if (table == null) return;
            var all = new System.Collections.Generic.List<SurvivorSouls.Gameplay.Skills.SkillDefinition>();
            table.CollectPool(int.MaxValue, all);
            foreach (string id in ids.Split(','))
            {
                var skill = all.Find(s => s.Id == id.Trim());
                if (skill == null || !manager.Grant(skill)) Debug.LogWarning($"[DevScreenshot] -ss-skills: cannot grant '{id}'");
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
