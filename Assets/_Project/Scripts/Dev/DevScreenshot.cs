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
    /// <c>-ss-near-exit</c> moves the player in front of the exit door (open world) or the arena portal (boss Chamber, KAN-59).
    /// <c>-ss-pos x,z</c> moves the player to a world position first (e.g. the shore).
    /// Every capture also logs render stats of the last second: <c>[DevScreenshot] Stats ...</c> (draw calls, batches,
    /// SetPass calls, triangles, average frame ms).
    /// <c>-ss-player-hp &lt;0..1&gt;</c> sets the player's health fraction after load (0 = death screen).
    /// <c>-ss-counters &lt;kills,souls&gt;</c> sets the run counters after load (KAN-51: numbers on the death screen).
    /// <c>-ss-hit-marker &lt;hit|kill&gt;</c> pops a hit (or kill) marker on the nearest enemy just before the capture.
    /// <c>-ss-release &lt;seconds&gt;</c> lets go of the -ss-move stick that long before the capture (e.g. wind trail tail-off).
    /// <c>-ss-souls &lt;n&gt;</c> drops n soul wisps around the player after load (mostly small, some elite, one boss orb).
    /// <c>-ss-xp &lt;amount&gt;</c> grants that much XP <see cref="XpLead"/> s before the capture (level-up burst + skill cards).
    /// <c>-ss-chests &lt;n&gt;</c> drops n Reliquary chests (KAN-72) ahead of the player after load: Light, then Rare, ...
    /// <c>-ss-open-chest &lt;light|rare&gt;</c> drops that chest at the Knight's feet: the real walk-in pickup opens it
    /// (KAN-73) and the upgrade screen follows (cards: owned skills; force them with -ss-offer).
    /// <c>-ss-skills id,id,...</c> grants those skills (by SkillDefinition id, repeat an id to stack) after load.
    /// <c>-ss-upgrade id,id,...</c> chest-upgrades those owned skills (+1 each) 0.3 s before the capture: HUD slot glow + new pips (KAN-74).
    /// <c>-ss-offer id,id,id</c> makes the next level-up offer those cards (KAN-37 tier look; use with -ss-xp).
    /// <c>-ss-horde &lt;n&gt;</c> places n enemies (wave table mix of the last phase) on a ring 4-11 m round the player after
    /// load, from the spawner's pools (perf runs; KAN-30). <c>-ss-immortal</c> turns off contact damage on the player.
    /// <c>-ss-hold &lt;n&gt;</c> keeps at least n enemies alive: every second, missing ones are spawned off screen (perf soak).
    /// <c>-ss-timer &lt;seconds&gt;</c> sets the Chamber survival clock after load (KAN-39; 0 = the portal opens now).
    /// <c>-ss-portal</c> walks the Knight into the portal right after load (KAN-40): pull, white fade, next Chamber,
    /// title card. With -ss-delay ~2.2 the capture shows the "CHAMBER II" card; skills / health set before it carry over.
    /// <c>-ss-portal-soak &lt;n&gt;</c> (KAN-47 leak check): n portal transitions in a row, each started 1 s after the last
    /// fade-in; logs per transition the Chamber, the time from portal to play, scene objects, Unity objects and memory
    /// (<c>[DevScreenshot] Soak ...</c>). From Chamber 1 the 10th portal of Realm 1 ends on the victory screen. The capture
    /// waits until the soak is done.
    /// <c>-ss-run-seed &lt;n&gt;</c> starts the run with that seed and <c>-ss-chamber &lt;n&gt;</c> starts it at that Chamber
    /// (KAN-46: layout seed + difficulty of that Chamber; same run seed + Chamber = same layout).
    /// <c>-ss-boss &lt;0..1&gt;</c> (KAN-58, with <c>-ss-chamber 5</c>): once the boss has risen, sets its health to that share and
    /// stands the Knight 2.6 m in front of it, so the slam winds up about 1.5 s later (add -ss-immortal to survive it).
    /// 0 kills it and moves the Knight in front of the arena portal, which then opens (KAN-59).
    /// <c>-ss-boss-attack fan|charge</c> (KAN-61, with <c>-ss-boss</c>): the Knight stands 8 m in front of the boss instead,
    /// and the boss starts that attack at once (charge: below the phase 2 share, e.g. <c>-ss-boss 0.4</c>, for the look of
    /// phase 2; the debug start works in phase 1 too).
    /// <c>-ss-hearts &lt;n&gt;</c> (KAN-60, boss Chamber): drops n healing hearts ahead of the Knight right after load (cap
    /// permitting); each shows its ground glow for 1 s, then floats.
    /// Main menu (KAN-20): <c>-ss-realms</c> opens the Realm selection page, <c>-ss-realm &lt;id&gt;</c> also opens that
    /// Realm's detail sheet. With either, the saved progress of Realms 1-3 is wiped first (repeatable shots); then
    /// <c>-ss-best &lt;realm&gt;:&lt;chamber&gt;</c> records a best Chamber and <c>-ss-cleared &lt;realm&gt;</c> marks a Realm completed.
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
        private string _counters;
        private string _hitMarker;
        private float _release;
        private int _souls;
        private int _xp;
        private string _skills;
        private string _upgrade;
        private int _chests;
        private string _openChest;
        private string _offer;
        private int _horde;
        private int _hold;
        private bool _immortal;
        private string _timer;
        private bool _portal;
        private int _soak;
        private bool _realms;
        private int _realmDetail;
        private string _best;
        private int _cleared;
        private string _boss;
        private string _bossAttack;
        private int _hearts;

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
            shot._counters = GetArg("-ss-counters");
            shot._hitMarker = GetArg("-ss-hit-marker");
            shot._release = float.TryParse(GetArg("-ss-release"), NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ? r : 0f;
            shot._souls = int.TryParse(GetArg("-ss-souls"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;
            shot._skills = GetArg("-ss-skills");
            shot._upgrade = GetArg("-ss-upgrade");
            shot._chests = int.TryParse(GetArg("-ss-chests"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int ch) ? ch : 0;
            shot._offer = GetArg("-ss-offer");
            shot._openChest = GetArg("-ss-open-chest");
            shot._xp = int.TryParse(GetArg("-ss-xp"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ? x : 0;
            shot._horde = int.TryParse(GetArg("-ss-horde"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) ? h : 0;
            shot._hold = int.TryParse(GetArg("-ss-hold"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int hold) ? hold : 0;
            shot._immortal = Array.IndexOf(Environment.GetCommandLineArgs(), "-ss-immortal") >= 0;
            shot._timer = GetArg("-ss-timer");
            shot._portal = Array.IndexOf(Environment.GetCommandLineArgs(), "-ss-portal") >= 0;
            shot._soak = int.TryParse(GetArg("-ss-portal-soak"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int sk) ? sk : 0;
            shot._realmDetail = int.TryParse(GetArg("-ss-realm"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int rd) ? rd : 0;
            shot._realms = shot._realmDetail > 0 || Array.IndexOf(Environment.GetCommandLineArgs(), "-ss-realms") >= 0;
            shot._best = GetArg("-ss-best");
            shot._boss = GetArg("-ss-boss");
            shot._bossAttack = GetArg("-ss-boss-attack");
            shot._hearts = int.TryParse(GetArg("-ss-hearts"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int hs) ? hs : 0;
            shot._cleared = int.TryParse(GetArg("-ss-cleared"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int cl) ? cl : 0;

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
            if (!string.IsNullOrEmpty(_counters)) SetCounters(_counters);
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
            if (_chests > 0) DropChests(_chests);
            if (!string.IsNullOrEmpty(_offer)) ForceOffer(_offer);
            if (!string.IsNullOrEmpty(_openChest)) DropChestUnderfoot(_openChest == "rare");
            if (!string.IsNullOrEmpty(_timer))
            {
                var timer = SurvivorSouls.Gameplay.Chamber.SurvivalTimer.Active;
                if (timer == null) Debug.LogWarning("[DevScreenshot] -ss-timer: no survival timer in the scene");
                else timer.SetRemaining(float.Parse(_timer, CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrEmpty(_boss)) StartCoroutine(SetupBoss(float.Parse(_boss, CultureInfo.InvariantCulture), _bossAttack));
            if (_hearts > 0) DropHearts(_hearts);
            if (_portal) EnterPortal();
            if (_soak > 0) yield return PortalSoak(_soak);
            if (_realms) OpenRealms();

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
            if (!string.IsNullOrEmpty(_upgrade))
            {
                UpgradeSkills(_upgrade);
                yield return new WaitForSecondsRealtime(0.3f); // glow near its peak, new pips mid-blink
            }
            if (!string.IsNullOrEmpty(_hitMarker)) ShowHitMarker(_hitMarker == "kill");
            yield return null;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(_path);
            yield return new WaitForSecondsRealtime(1f); // capture writes asynchronously
            if (_portal) Debug.Log($"[DevScreenshot] Chamber {SurvivorSouls.Gameplay.Chamber.RunState.Chamber} (Realm {SurvivorSouls.Gameplay.Chamber.RunState.Realm})");
            Debug.Log($"[DevScreenshot] Saved {_path}");
            Application.Quit();
        }

        /// <summary>Main menu: wipes Realm progress, applies -ss-best / -ss-cleared, opens the Realm page (and a sheet).</summary>
        private void OpenRealms()
        {
            for (int realm = 1; realm <= 3; realm++) SurvivorSouls.Gameplay.Chamber.RunProgress.Clear(realm);
            if (!string.IsNullOrEmpty(_best))
            {
                string[] rc = _best.Split(':');
                SurvivorSouls.Gameplay.Chamber.RunProgress.RecordChamberStart(
                    int.Parse(rc[0], CultureInfo.InvariantCulture), int.Parse(rc[1], CultureInfo.InvariantCulture));
            }
            if (_cleared > 0) SurvivorSouls.Gameplay.Chamber.RunProgress.MarkCompleted(_cleared);

            var menu = FindAnyObjectByType<SurvivorSouls.UI.MainMenu.MainMenuController>();
            if (menu == null || menu.RealmSelect == null)
            {
                Debug.LogWarning("[DevScreenshot] -ss-realms: no main menu with a Realm page in the scene");
                return;
            }
            menu.OpenRealms();
            if (_realmDetail > 0 && !menu.RealmSelect.OpenDetail(_realmDetail))
                Debug.LogWarning($"[DevScreenshot] -ss-realm: no Realm {_realmDetail}");
            Debug.Log($"[DevScreenshot] Realm page open, detail={menu.RealmSelect.Selected?.DisplayName ?? "none"} canStart={menu.RealmSelect.CanStart}");
        }

        /// <summary>Waits for the boss to rise, sets its health share and puts the Knight in slam range in front of it.</summary>
        private static IEnumerator SetupBoss(float health, string attack)
        {
            var fight = SurvivorSouls.Gameplay.Boss.BossFight.Active;
            if (fight == null || fight.Current == SurvivorSouls.Gameplay.Boss.BossFight.Stage.Inactive)
            {
                Debug.LogWarning("[DevScreenshot] -ss-boss: no boss fight in this Chamber (use -ss-chamber 5)");
                yield break;
            }
            while (fight.Boss == null || !fight.Boss.isActiveAndEnabled) yield return null;
            var boss = fight.Boss;
            float target = Mathf.Clamp01(health) * boss.MaxHealth;
            if (target <= 0f) // kill: reward drops, the portal opens; the Knight waits in front of the exit (KAN-59 arena)
            {
                boss.TakeDamage(boss.Health + 1f, Vector3.zero, DamageKind.Burst);
                MoveNearExit();
                Debug.Log($"[DevScreenshot] Boss {fight.Data.DisplayName} killed, stage={fight.Current}");
                yield break;
            }
            if (boss.Health > target) boss.TakeDamage(boss.Health - target, Vector3.zero, DamageKind.Burst);
            var b = boss.transform;
            bool ranged = attack == "fan" || attack == "charge";
            Teleport(b.position + b.forward * (ranged ? 8f : 2.6f));
            Debug.Log($"[DevScreenshot] Boss {fight.Data.DisplayName} hp={boss.Health:F0}/{boss.MaxHealth:F0} phase={fight.Phase} at {b.position}");
            if (!ranged) yield break;
            var move = attack == "fan" ? SurvivorSouls.Gameplay.Boss.BossFight.Move.Fan : SurvivorSouls.Gameplay.Boss.BossFight.Move.Charge;
            for (float t = 0f; t < 6f; t += Time.unscaledDeltaTime)
            {
                yield return null;
                if (fight.ForceAttack(move))
                {
                    Debug.Log($"[DevScreenshot] Boss attack {move} started, wind-up {fight.WindUpTime:F2} s");
                    yield break;
                }
            }
            Debug.LogWarning($"[DevScreenshot] -ss-boss-attack: {move} did not start");
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

        /// <summary>-ss-portal-soak: n portal transitions in a row with timing, object counts and memory per step (KAN-47).</summary>
        private static IEnumerator PortalSoak(int count)
        {
            for (int i = 1; i <= count; i++)
            {
                yield return new WaitForSecondsRealtime(1f);
                var transition = SurvivorSouls.Gameplay.Chamber.ChamberTransition.Active;
                var player = FindAnyObjectByType<PlayerHealth>();
                if (transition == null || player == null || player.IsDead)
                {
                    Debug.LogWarning($"[DevScreenshot] Soak {i}: no transition or living Knight, stop");
                    yield break;
                }
                int from = SurvivorSouls.Gameplay.Chamber.RunState.Chamber;
                float start = Time.realtimeSinceStartup;
                var t = player.transform;
                if (!transition.Enter(t.position + t.forward * 1.5f))
                {
                    Debug.LogWarning($"[DevScreenshot] Soak {i}: portal refused, stop");
                    yield break;
                }
                bool arriving = false, cleared = false;
                while (Time.realtimeSinceStartup - start < 20f)
                {
                    if (transition != null && transition.Current == SurvivorSouls.Gameplay.Chamber.ChamberTransition.Phase.Cleared)
                    {
                        cleared = true;
                        break;
                    }
                    var next = SurvivorSouls.Gameplay.Chamber.ChamberTransition.Active;
                    if (next != null && next != transition)
                    {
                        if (next.Current == SurvivorSouls.Gameplay.Chamber.ChamberTransition.Phase.Arriving) arriving = true;
                        else if (arriving && next.Current == SurvivorSouls.Gameplay.Chamber.ChamberTransition.Phase.Idle) break;
                    }
                    yield return null;
                }
                float took = Time.realtimeSinceStartup - start;
                yield return null;
                int sceneObjects = FindObjectsByType<Transform>(FindObjectsInactive.Include).Length;
                int allObjects = Resources.FindObjectsOfTypeAll<UnityEngine.Object>().Length;
                long total = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() >> 20;
                long mono = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() >> 20;
                Debug.Log($"[DevScreenshot] Soak {i}: Chamber {from}->{SurvivorSouls.Gameplay.Chamber.RunState.Chamber}" +
                          $"{(cleared ? " REALM CLEARED" : "")} took={took:F2}s sceneObjects={sceneObjects} unityObjects={allObjects} " +
                          $"allocMB={total} monoMB={mono}");
                if (cleared) yield break;
            }
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
            var arena = SurvivorSouls.Gameplay.World.BossArena.Active; // boss Chamber: the arena portal (KAN-59)
            var world = FindAnyObjectByType<SurvivorSouls.Gameplay.World.WorldStreamer>();
            if (arena != null) Teleport(arena.ExitPosition + new Vector3(0f, 0f, -5.5f));
            else if (world != null) Teleport(world.ExitPosition + new Vector3(0f, 0f, -5.5f));
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
            if (manager == null) return;
            foreach (string id in ids.Split(','))
            {
                if (!manager.Grant(id.Trim())) Debug.LogWarning($"[DevScreenshot] -ss-skills: cannot grant '{id}'");
            }
        }

        /// <summary>-ss-upgrade: +1 level on each owned skill listed (chest upgrade path, so the HUD glows).</summary>
        private static void UpgradeSkills(string ids)
        {
            var manager = FindAnyObjectByType<SurvivorSouls.Gameplay.Skills.SkillManager>();
            if (manager == null) return;
            foreach (string id in ids.Split(','))
            {
                if (!manager.Upgrade(id.Trim())) Debug.LogWarning($"[DevScreenshot] -ss-upgrade: cannot upgrade '{id}'");
            }
        }

        /// <summary>-ss-hearts n: healing hearts ahead of the Knight, left and right (KAN-60, cap permitting).</summary>
        private static void DropHearts(int count)
        {
            var hearts = FindAnyObjectByType<SurvivorSouls.Gameplay.Boss.BossHearts>();
            var player = FindAnyObjectByType<PlayerHealth>();
            if (hearts == null || player == null)
            {
                Debug.LogWarning("[DevScreenshot] -ss-hearts: no BossHearts or player in the scene");
                return;
            }
            var p = player.transform;
            for (int i = 0; i < count; i++)
            {
                float side = (i % 2 == 0 ? -1f : 1f) * (1.6f + 0.8f * (i / 2));
                var heart = hearts.DropNow(p.position + p.forward * (3f + 1.2f * (i / 2)) + p.right * side);
                Debug.Log(heart != null ? $"[DevScreenshot] Heart at {heart.transform.position}" : "[DevScreenshot] -ss-hearts: cap reached");
            }
        }

        /// <summary>-ss-chests n: chests on the ground ahead of the Knight, alternating Light / Rare (cap permitting).</summary>
        private static void DropChests(int count)
        {
            var dropper = FindAnyObjectByType<SurvivorSouls.Gameplay.Chests.ChestDropper>();
            var player = FindAnyObjectByType<PlayerHealth>();
            if (dropper == null || player == null) return;
            var p = player.transform;
            for (int i = 0; i < count; i++)
            {
                float side = (i % 2 == 0 ? -1f : 1f) * (1.3f + 0.6f * (i / 2));
                var at = p.position + p.forward * (2.4f + 0.8f * (i / 2)) + p.right * side;
                var tier = i % 2 == 0 ? SurvivorSouls.Gameplay.Chests.ChestTier.Light : SurvivorSouls.Gameplay.Chests.ChestTier.Rare;
                var chest = dropper.DropNow(at, tier);
                Debug.Log(chest != null
                    ? $"[DevScreenshot] Chest {tier} at {chest.transform.position} choices={chest.Choices} upgrades={chest.Upgrades}"
                    : "[DevScreenshot] -ss-chests: ground full");
            }
        }

        /// <summary>-ss-open-chest: a chest 0.2 m ahead of the Knight; the dropper's own pickup opens it once it has landed.</summary>
        private static void DropChestUnderfoot(bool rare)
        {
            var dropper = FindAnyObjectByType<SurvivorSouls.Gameplay.Chests.ChestDropper>();
            var player = FindAnyObjectByType<PlayerHealth>();
            if (dropper == null || player == null) return;
            var tier = rare ? SurvivorSouls.Gameplay.Chests.ChestTier.Rare : SurvivorSouls.Gameplay.Chests.ChestTier.Light;
            var chest = dropper.Drop(player.transform.position + player.transform.forward * 0.2f, tier, dropper.Choices,
                rare ? dropper.Config.RareUpgrades : 1);
            Debug.Log(chest != null
                ? $"[DevScreenshot] Open chest {tier}: choices={chest.Choices} upgrades={chest.Upgrades}"
                : "[DevScreenshot] -ss-open-chest: ground full");
        }

        private static void ForceOffer(string ids)
        {
            var manager = FindAnyObjectByType<SurvivorSouls.Gameplay.Skills.SkillManager>();
            if (manager == null) return;
            var offer = new System.Collections.Generic.List<SurvivorSouls.Gameplay.Skills.SkillDefinition>();
            foreach (string id in ids.Split(','))
            {
                var skill = manager.FindSkill(id.Trim());
                if (skill == null) Debug.LogWarning($"[DevScreenshot] -ss-offer: no skill '{id}'");
                else offer.Add(skill);
            }
            manager.ForceNextOffer(offer.ToArray());
        }

        /// <summary>-ss-counters kills,souls: run counters as if carried from earlier Chambers (death screen shots, KAN-51).</summary>
        private static void SetCounters(string arg)
        {
            var counters = FindAnyObjectByType<SurvivorSouls.Gameplay.Chamber.RunCounters>();
            string[] ks = arg.Split(',');
            if (counters == null || ks.Length < 2) return;
            counters.Restore(int.Parse(ks[0], CultureInfo.InvariantCulture), int.Parse(ks[1], CultureInfo.InvariantCulture));
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
