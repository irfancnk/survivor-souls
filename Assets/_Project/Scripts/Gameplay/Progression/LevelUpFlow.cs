using System;
using System.Collections.Generic;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Player;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Progression
{
    /// <summary>
    /// Level-up sequence (KAN-21): queues every <see cref="XpManager.OnLevelUp"/> and plays them one after another.
    /// For each level: pause the game (<see cref="GamePause"/>), raise <see cref="Started"/> (burst ring on the Knight,
    /// sound, "LEVEL UP" text), wait <see cref="celebrateTime"/>, open the <see cref="LevelUpChoice"/> (KAN-19 skill cards),
    /// and after the pick raise <see cref="Finished"/>. The pause holds across queued levels and is released after the
    /// last one. Runs on unscaled time. The queue is dropped when the player dies.
    /// </summary>
    public sealed class LevelUpFlow : MonoBehaviour
    {
        private enum Phase { Idle, Celebrating, Choosing, Gap }

        [SerializeField] private XpManager xp;
        [SerializeField] private PlayerHealth player;
        [SerializeField] private LevelUpChoice choice;
        [Tooltip("Unscaled seconds of celebration (ring burst, text) before the choice opens.")]
        [SerializeField, Min(0f)] private float celebrateTime = 0.55f;
        [Tooltip("Unscaled seconds between two queued level-ups.")]
        [SerializeField, Min(0f)] private float gapTime = 0.2f;

        /// <summary>A level-up starts (game already paused): new level.</summary>
        public event Action<int> Started;
        /// <summary>The choice for this level is done.</summary>
        public event Action<int> Finished;

        private readonly Queue<int> _pending = new Queue<int>(8);
        private Action _onChosen;
        private Phase _phase;
        private float _timer;
        private bool _holdingPause;

        public bool IsBusy => _phase != Phase.Idle;
        public int PendingCount => _pending.Count;
        /// <summary>Level being shown (0 when idle).</summary>
        public int CurrentLevel { get; private set; }

        private void Awake() => _onChosen = OnChosen;

        /// <summary>Wiring for code-built objects and tests.</summary>
        public void Setup(XpManager xpManager, PlayerHealth playerHealth, LevelUpChoice levelUpChoice)
        {
            bool wasEnabled = isActiveAndEnabled;
            if (wasEnabled) OnDisable();
            xp = xpManager;
            player = playerHealth;
            choice = levelUpChoice;
            if (wasEnabled) OnEnable();
        }

        private void OnEnable()
        {
            _onChosen ??= OnChosen;
            if (xp != null) xp.OnLevelUp += Enqueue;
            if (player != null) player.OnDeath += OnPlayerDeath;
        }

        private void OnDisable()
        {
            if (xp != null) xp.OnLevelUp -= Enqueue;
            if (player != null) player.OnDeath -= OnPlayerDeath;
            ReleasePause();
        }

        private void Enqueue(int level)
        {
            if (player != null && player.IsDead) return;
            _pending.Enqueue(level);
        }

        private void OnPlayerDeath()
        {
            _pending.Clear();
            if (_phase == Phase.Gap) Finish();
        }

        private void Update() => Tick(Time.unscaledDeltaTime);

        /// <summary>Advances the sequence. Public for tests (Update calls it with unscaled time).</summary>
        public void Tick(float dt)
        {
            switch (_phase)
            {
                case Phase.Idle:
                    if (_pending.Count > 0) Begin(_pending.Dequeue());
                    break;
                case Phase.Celebrating:
                    _timer -= dt;
                    if (_timer > 0f) break;
                    _phase = Phase.Choosing;
                    if (choice != null && choice.isActiveAndEnabled) choice.Present(CurrentLevel, _onChosen);
                    else OnChosen();
                    break;
                case Phase.Gap:
                    _timer -= dt;
                    if (_timer > 0f) break;
                    if (_pending.Count > 0) Begin(_pending.Dequeue());
                    else Finish();
                    break;
            }
        }

        private void Begin(int level)
        {
            if (!_holdingPause)
            {
                GamePause.Acquire();
                _holdingPause = true;
            }
            CurrentLevel = level;
            _phase = Phase.Celebrating;
            _timer = celebrateTime;
            Started?.Invoke(level);
        }

        private void OnChosen()
        {
            if (_phase != Phase.Choosing) return; // a choice screen calling back twice must not skip a level
            int level = CurrentLevel;
            _phase = Phase.Gap;
            _timer = gapTime;
            if (_pending.Count == 0) ReleasePause(); // resume now; the gap only spaces queued screens
            Finished?.Invoke(level);
        }

        private void Finish()
        {
            _phase = Phase.Idle;
            CurrentLevel = 0;
            ReleasePause();
        }

        private void ReleasePause()
        {
            if (!_holdingPause) return;
            _holdingPause = false;
            GamePause.Release();
        }
    }
}
