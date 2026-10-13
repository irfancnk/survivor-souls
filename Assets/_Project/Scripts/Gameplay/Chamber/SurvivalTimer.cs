using System;
using SurvivorSouls.Gameplay.Player;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Chamber
{
    /// <summary>
    /// Chamber survival clock (KAN-39). Counts <see cref="ChamberRules.SurvivalSeconds"/> down to 0 on scaled game time,
    /// so it stops with <see cref="Core.GamePause"/> (level-up cards, chests, pause menu) and while the app is in the
    /// background. At 0 it raises <see cref="Finished"/> once: the exit portal opens (<see cref="PortalGate"/>), the HUD
    /// shows "THE PORTAL IS OPEN", sound, haptic and a small camera shake follow (listeners of the event).
    /// Warnings (5, 2, 1 minute left) fire once each when the clock crosses them; a debug jump never replays skipped ones.
    /// Stops for good when the player dies. Boss Chambers (<see cref="OpenCondition.KillBoss"/>) do not count:
    /// the boss death calls <see cref="Complete"/>.
    /// <see cref="Active"/> = the live timer of the scene (the pooled portal tile finds it there, plus the player).
    /// </summary>
    public sealed class SurvivalTimer : MonoBehaviour
    {
        [SerializeField] private ChamberRules rules;
        [Tooltip("Who may enter the open portal.")]
        [SerializeField] private Transform player;
        [Tooltip("The clock stops when this player dies.")]
        [SerializeField] private PlayerHealth health;

        private int[] _warnings = Array.Empty<int>(); // descending
        private int _nextWarning;
        private bool _stopped;

        public static SurvivalTimer Active { get; private set; }

        /// <summary>Remaining seconds that crossed a warning mark (e.g. 300).</summary>
        public event Action<int> WarningReached;
        /// <summary>The portal may open (timer hit 0 or <see cref="Complete"/>). Raised once.</summary>
        public event Action Finished;

        public ChamberRules Rules => rules;
        public Transform Player => player;
        public float Duration { get; private set; }
        public float Remaining { get; private set; }
        public bool IsFinished { get; private set; }
        /// <summary>True while the clock counts (survive Chamber, not finished, player alive).</summary>
        public bool IsRunning => !IsFinished && !_stopped && UsesTimer;
        public bool UsesTimer => rules == null || rules.OpenCondition == OpenCondition.Survive;
        /// <summary>Last seconds before the portal opens: the HUD pulses.</summary>
        public bool InFinalCountdown => IsRunning && rules != null && Remaining <= rules.FinalCountdown;
        public float OpenSequence => rules != null ? rules.OpenSequence : 2f;

        /// <summary>Tests and tools: wires the timer without the scene builder, then restarts it.</summary>
        public void Setup(ChamberRules chamberRules, Transform playerTransform, PlayerHealth playerHealth)
        {
            if (isActiveAndEnabled && health != null) health.OnDeath -= Stop;
            rules = chamberRules;
            player = playerTransform;
            health = playerHealth;
            if (isActiveAndEnabled && health != null) health.OnDeath += Stop;
            Restart();
        }

        private void Awake() => Restart();

        private void OnEnable()
        {
            Active = this;
            if (health != null) health.OnDeath += Stop;
        }

        private void OnDisable()
        {
            if (Active == this) Active = null;
            if (health != null) health.OnDeath -= Stop;
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>
        /// Swaps the rules and restarts the clock. A boss Chamber (KAN-58) switches to <see cref="OpenCondition.KillBoss"/>
        /// rules at scene start: no clock, the boss death calls <see cref="Complete"/>.
        /// </summary>
        public void UseRules(ChamberRules chamberRules)
        {
            rules = chamberRules;
            Restart();
        }

        /// <summary>Full clock again (new Chamber, retry). Warnings re-armed.</summary>
        public void Restart()
        {
            Duration = rules != null ? rules.SurvivalSeconds : 600f;
            Remaining = Duration;
            IsFinished = false;
            _stopped = health != null && health.IsDead;
            var marks = rules != null ? rules.Warnings : null;
            _warnings = marks != null ? (int[])marks.Clone() : Array.Empty<int>();
            Array.Sort(_warnings);
            Array.Reverse(_warnings);
            _nextWarning = 0;
            SkipWarningsAtOrAbove(Remaining);
        }

        /// <summary>Advances the clock by <paramref name="dt"/> scaled seconds (0 while paused).</summary>
        public void Tick(float dt)
        {
            if (!IsRunning || dt <= 0f) return;
            Remaining = Mathf.Max(0f, Remaining - dt);
            while (_nextWarning < _warnings.Length && Remaining <= _warnings[_nextWarning])
            {
                int mark = _warnings[_nextWarning++];
                if (mark > 0) WarningReached?.Invoke(mark);
            }
            if (Remaining <= 0f) Complete();
        }

        /// <summary>Debug / tests: jump the clock. Warnings above the new value are skipped, not replayed.</summary>
        public void SetRemaining(float seconds)
        {
            if (IsFinished) return;
            Remaining = Mathf.Clamp(seconds, 0f, Duration);
            SkipWarningsAtOrAbove(Remaining);
            if (Remaining <= 0f && IsRunning) Complete();
        }

        /// <summary>Opens the portal now (timer end, boss death, debug). Raises <see cref="Finished"/> once.</summary>
        public void Complete()
        {
            if (IsFinished) return;
            Remaining = 0f;
            IsFinished = true;
            Finished?.Invoke();
        }

        /// <summary>Stops the clock for good (player death). The portal stays as it is.</summary>
        public void Stop() => _stopped = true;

        private void SkipWarningsAtOrAbove(float remaining)
        {
            while (_nextWarning < _warnings.Length && _warnings[_nextWarning] >= remaining) _nextWarning++;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Active = null;
    }
}
