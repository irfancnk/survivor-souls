using System;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Chamber
{
    /// <summary>
    /// Portal to the next Chamber (KAN-40). One Game scene; a new Chamber = the same scene reloaded, with the run carried
    /// in <see cref="RunState"/>.
    /// <list type="number">
    /// <item><b>Pull</b> (<see cref="pullTime"/>): <see cref="PortalGate.PlayerEntered"/> fires once. Input, movement,
    /// attacks, contact damage and pickups are locked (<see cref="lockOnExit"/>), the spawner stops, and the Knight is
    /// drawn through the arch (<see cref="Departing"/>: whoosh).</item>
    /// <item><b>Fade out</b> (<see cref="fadeOutTime"/>): screen to white. At full white the run is captured
    /// (<see cref="Capture"/>), <see cref="RunState.AdvanceChamber"/>, and the Game scene reloads.</item>
    /// <item><b>Arrive</b>: the new scene starts full white and paused (<see cref="GamePause"/>); level, XP, health,
    /// skills and the run counters (<see cref="RunCounters"/>) are put back (<see cref="Apply"/>), the timer is full (fresh scene), the Knight stands at the spawn bonfire
    /// facing the play area and the camera starts on him (no jump). Fade in (<see cref="fadeInTime"/>), then the pause
    /// ends; <see cref="Arrived"/> shows the "CHAMBER N" card (HUD, unscaled time) over the first seconds of play.</item>
    /// </list>
    /// <b>End of Realm</b> (KAN-47): in the last Chamber of the Realm (<see cref="ChamberPlan.IsLast"/>) the portal does not
    /// load a next Chamber. At full white the run is captured into <see cref="ClearedRun"/>, the Realm is marked completed
    /// in the save (<see cref="RunProgress.MarkCompleted"/>, which unlocks the next one), the game pauses, and
    /// <see cref="RealmCleared"/> fires (victory screen, sound). The white then fades back over <see cref="clearedFadeTime"/>
    /// so the world shows dimmed under the victory screen. The pause holds until the scene is left.
    /// Every Chamber start is saved (<see cref="RunProgress"/>). All timing is unscaled with a clamped step, so an app pause
    /// in the middle neither skips nor breaks the sequence. Runs first among Start calls: the run is back in place before
    /// any HUD reads it.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class ChamberTransition : MonoBehaviour
    {
        public enum Phase
        {
            Idle = 0,
            Pulling = 1,
            FadingOut = 2,
            Loading = 3,
            Arriving = 4,
            /// <summary>The Realm is cleared (KAN-47): paused under the victory screen until the scene is left.</summary>
            Cleared = 5,
        }

        private const float MaxStep = 0.05f; // unscaled step cap: a resume after an app pause does not skip the sequence

        [Header("Run")]
        [SerializeField] private Transform player;
        [SerializeField] private PlayerHealth health;
        [SerializeField] private XpManager xp;
        [SerializeField] private SkillManager skills;
        [SerializeField] private RunCounters counters;
        [SerializeField] private EnemySpawner spawner;
        [Tooltip("Turned off when the Knight enters the portal: input, mover, shooter, contact damage, soul pickup.")]
        [SerializeField] private Behaviour[] lockOnExit;
        [Tooltip("Off while the Knight is pulled (the transform is moved directly).")]
        [SerializeField] private CharacterController controller;

        [Header("Timing (unscaled seconds)")]
        [SerializeField, Min(0.05f)] private float pullTime = 0.5f;
        [Tooltip("How far past the portal centre the Knight is drawn.")]
        [SerializeField, Min(0f)] private float pullDepth = 0.9f;
        [SerializeField, Min(0.05f)] private float fadeOutTime = 0.4f;
        [SerializeField, Min(0.05f)] private float fadeInTime = 0.45f;
        [Tooltip("End of Realm: white fades back to the world under the victory screen.")]
        [SerializeField, Min(0.05f)] private float clearedFadeTime = 0.9f;

        [Header("Rules")]
        [Tooltip("Share of max health healed when a new Chamber starts (0 = keep the health as it was).")]
        [SerializeField, Range(0f, 1f)] private float healOnArrive;
        [Tooltip("Off in tests: the run is captured and advanced, but no scene is loaded.")]
        [SerializeField] private bool loadScene = true;

        private float _time;
        private Vector3 _pullFrom, _pullTo;
        private bool _pauseHeld;

        public static ChamberTransition Active { get; private set; }

        /// <summary>The Knight entered the portal (pull starts).</summary>
        public event Action Departing;
        /// <summary>A new Chamber started after a portal (Chamber number). Raised when the fade-in begins.</summary>
        public event Action<int> Arrived;
        /// <summary>The last Chamber of the Realm was left through the portal (KAN-47): the run as it ended. Once, at full white.</summary>
        public event Action<RunSnapshot> RealmCleared;

        public Phase Current { get; private set; } = Phase.Idle;
        /// <summary>White overlay 0..1 (HUD draws it).</summary>
        public float Fade { get; private set; }
        public float HealOnArrive => healOnArrive;
        /// <summary>The run as it ended when the Realm was cleared (null before).</summary>
        public RunSnapshot ClearedRun { get; private set; }

        /// <summary>Tests: wiring without the scene builder.</summary>
        public void Setup(Transform playerTransform, PlayerHealth playerHealth, XpManager xpManager, SkillManager skillManager,
            Behaviour[] locked, bool load, RunCounters runCounters = null)
        {
            counters = runCounters;
            player = playerTransform;
            health = playerHealth;
            xp = xpManager;
            skills = skillManager;
            lockOnExit = locked;
            loadScene = load;
            controller = player != null ? player.GetComponent<CharacterController>() : null;
        }

        private void OnEnable()
        {
            Active = this;
            PortalGate.PlayerEntered += OnPortalEntered;
        }

        private void OnDisable()
        {
            if (Active == this) Active = null;
            PortalGate.PlayerEntered -= OnPortalEntered;
        }

        private void Start() => BeginChamber(RunState.TakeCarry());

        private void OnDestroy() => ReleasePause();

        /// <summary>
        /// Start of this scene's Chamber: saves progress and, after a portal, puts the run back and starts the fade-in.
        /// Public for tests (Start calls it with the carried snapshot).
        /// </summary>
        public void BeginChamber(RunSnapshot carry)
        {
            if (RunProgress.Recording) RunProgress.RecordChamberStart(RunState.Realm, RunState.Chamber);
            if (carry == null) return;
            Apply(carry, xp, skills, health, healOnArrive, counters);
            Current = Phase.Arriving;
            Fade = 1f;
            _time = 0f;
            if (!_pauseHeld)
            {
                _pauseHeld = true;
                GamePause.Acquire(); // enemies, timer and spawner wait for the fade-in
            }
            Arrived?.Invoke(RunState.Chamber);
        }

        private void OnPortalEntered(PortalGate gate) => Enter(gate.TriggerPosition);

        /// <summary>Starts the exit through the portal at <paramref name="portalPoint"/>. Once; ignored when dead or busy.</summary>
        public bool Enter(Vector3 portalPoint)
        {
            if (Current != Phase.Idle || player == null || (health != null && health.IsDead)) return false;
            if (lockOnExit != null)
                foreach (var b in lockOnExit)
                    if (b != null) b.enabled = false;
            if (spawner != null) spawner.enabled = false;
            if (controller != null) controller.enabled = false;

            _pullFrom = player.position;
            Vector3 dir = portalPoint - _pullFrom;
            dir.y = 0f;
            dir = dir.sqrMagnitude > 0.0001f ? dir.normalized : player.forward;
            _pullTo = new Vector3(portalPoint.x, _pullFrom.y, portalPoint.z) + dir * pullDepth;
            player.rotation = Quaternion.LookRotation(dir, Vector3.up);
            Current = Phase.Pulling;
            _time = 0f;
            Departing?.Invoke();
            return true;
        }

        private void Update() => Tick(Time.unscaledDeltaTime);

        /// <summary>One step of the sequence with <paramref name="dt"/> unscaled seconds (public for tests).</summary>
        public void Tick(float dt)
        {
            dt = Mathf.Min(dt, MaxStep);
            switch (Current)
            {
                case Phase.Pulling:
                    _time += dt;
                    float k = Mathf.Clamp01(_time / pullTime);
                    player.position = Vector3.LerpUnclamped(_pullFrom, _pullTo, k * k * (3f - 2f * k));
                    if (k >= 1f) SetPhase(Phase.FadingOut);
                    // The screen starts to whiten in the last part of the pull: one continuous move into the light.
                    Fade = Mathf.Max(0f, (k - 0.6f) / 0.4f) * 0.25f;
                    break;
                case Phase.FadingOut:
                    _time += dt;
                    Fade = Mathf.Lerp(0.25f, 1f, Mathf.Clamp01(_time / fadeOutTime));
                    if (_time >= fadeOutTime) Leave();
                    break;
                case Phase.Arriving:
                    // The old scene's teardown may run GamePause.Clear() after our Start: take the hold again.
                    if (_pauseHeld && !GamePause.IsPaused) GamePause.Acquire();
                    _time += dt;
                    Fade = 1f - Mathf.Clamp01(_time / fadeInTime);
                    if (_time >= fadeInTime)
                    {
                        Fade = 0f;
                        ReleasePause();
                        SetPhase(Phase.Idle);
                    }
                    break;
                case Phase.Cleared:
                    if (_pauseHeld && !GamePause.IsPaused) GamePause.Acquire();
                    _time += dt;
                    Fade = 1f - Mathf.Clamp01(_time / clearedFadeTime);
                    break;
            }
        }

        private void SetPhase(Phase phase)
        {
            Current = phase;
            _time = 0f;
        }

        private void Leave()
        {
            Fade = 1f;
            if (IsRealmEnd(ChamberSetup.Current))
            {
                ClearRealm();
                return;
            }
            SetPhase(Phase.Loading);
            RunState.AdvanceChamber(Capture(xp, skills, health, counters));
            if (loadScene && SceneLoader.IsInBuild(SceneNames.Game)) SceneLoader.LoadAsync(SceneNames.Game);
        }

        /// <summary>
        /// Whether leaving the Chamber of <paramref name="plan"/> ends the Realm: it is the last Chamber and it is the
        /// Chamber this run is in (a stale plan from another scene never ends a run).
        /// </summary>
        public static bool IsRealmEnd(ChamberPlan plan) =>
            plan.IsLast && plan.Realm == RunState.Realm && plan.Chamber == RunState.Chamber;

        private void ClearRealm()
        {
            ClearedRun = Capture(xp, skills, health, counters);
            if (RunProgress.Recording) RunProgress.MarkCompleted(RunState.Realm); // Test Mode: no fake completed flag
            RunState.EndRealm();
            SetPhase(Phase.Cleared);
            if (!_pauseHeld)
            {
                _pauseHeld = true;
                GamePause.Acquire(); // enemies, wisps and timers stop under the victory screen
            }
            RealmCleared?.Invoke(ClearedRun);
        }

        private void ReleasePause()
        {
            if (!_pauseHeld) return;
            _pauseHeld = false;
            GamePause.Release();
        }

        /// <summary>The run as it is now: level, XP, health, skills with stacks (pick order), kills and souls.</summary>
        public static RunSnapshot Capture(XpManager xpManager, SkillManager skillManager, PlayerHealth playerHealth,
            RunCounters runCounters = null)
        {
            var snap = new RunSnapshot();
            if (xpManager != null)
            {
                snap.Level = xpManager.Level;
                snap.Xp = xpManager.Xp;
                snap.TotalXp = xpManager.TotalXp;
            }
            if (playerHealth != null) snap.Health = playerHealth.Current;
            if (skillManager != null)
                foreach (var o in skillManager.OwnedSkills)
                    snap.Skills.Add(new SkillStack(o.Skill, o.Level));
            if (runCounters != null)
            {
                snap.Kills = runCounters.Kills;
                snap.Souls = runCounters.Souls;
            }
            return snap;
        }

        /// <summary>
        /// Puts <paramref name="snap"/> back. Skills first (max health skills raise the max), then XP, then health
        /// (+ <paramref name="heal"/> x max, capped), then the run counters.
        /// </summary>
        public static void Apply(RunSnapshot snap, XpManager xpManager, SkillManager skillManager, PlayerHealth playerHealth, float heal,
            RunCounters runCounters = null)
        {
            if (snap == null) return;
            if (skillManager != null)
                foreach (var s in snap.Skills)
                    skillManager.Restore(s.Skill, s.Stacks);
            if (xpManager != null) xpManager.Restore(snap.Level, snap.Xp, snap.TotalXp);
            if (playerHealth != null && snap.Health > 0f) playerHealth.Restore(snap.Health + heal * playerHealth.Max);
            if (runCounters != null) runCounters.Restore(snap.Kills, snap.Souls);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Active = null;
    }
}
