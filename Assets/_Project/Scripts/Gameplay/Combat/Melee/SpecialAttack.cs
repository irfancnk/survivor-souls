using System;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.Player;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Base of the special melee zombie attacks (KAN-106): <see cref="BloaterBurst"/>, <see cref="GhoulLeap"/>,
    /// <see cref="BruteSlam"/>. Sits next to <see cref="Enemy"/> (which still chases and deals contact damage) and borrows its
    /// warnings from the scene's shared pool (<see cref="EnemyTelegraphs"/>), like <see cref="RangedAttack"/>.
    /// Loop: Idle (cooldown, then the trigger distance) -> WindUp (the enemy stops, its warning fills) -> Strike (the attack
    /// plays out) -> Recover -> Idle. A dead Knight cancels the attack and hides its warnings; a dead enemy too, unless the
    /// type says otherwise (<see cref="OnDeadTick"/>: the Bloater still bursts).
    /// Every warning shows at least <see cref="MinTelegraph"/> s before the attack can hurt (boss rule).
    /// </summary>
    [RequireComponent(typeof(Enemy))]
    public abstract class SpecialAttack : MonoBehaviour
    {
        public enum Phase : byte { Idle, WindUp, Strike, Recover }

        /// <summary>A warning shows at least this long before damage can land (boss rule, BossData.MinTelegraph).</summary>
        public const float MinTelegraph = 0.6f;

        /// <summary>An attack struck (burst, leap take-off, slam): the attacker. Sounds, shake, tests.</summary>
        public static event Action<SpecialAttack> Struck;
        /// <summary>An attack hit the Knight: the attacker, damage dealt.</summary>
        public static event Action<SpecialAttack, float> HitKnight;

        private Enemy _enemy;
        protected Phase _phase;
        protected float _time;
        protected float _cooldown;

        public Enemy Enemy => _enemy != null ? _enemy : _enemy = GetComponent<Enemy>();
        public Phase CurrentPhase => _phase;
        /// <summary>Seconds into the current phase.</summary>
        public float PhaseTime => _time;
        public float Cooldown => _cooldown;
        /// <summary>Attacks started since spawn.</summary>
        public int Attacks { get; private set; }

        /// <summary>Seconds of cooldown at spawn (randomised 30-70 %, so a group never attacks in sync).</summary>
        protected abstract float SpawnCooldown { get; }

        protected virtual void Awake() => _enemy = GetComponent<Enemy>();

        protected virtual void OnEnable()
        {
            _phase = Phase.Idle;
            _time = 0f;
            Attacks = 0;
            _cooldown = SpawnCooldown * UnityEngine.Random.Range(0.3f, 0.7f);
        }

        protected virtual void OnDisable() => Cancel();

        private void Update() => Tick(Time.deltaTime);

        /// <summary>One step with <paramref name="dt"/> scaled seconds (0 while paused). Public for tests.</summary>
        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            var knight = Knight();
            if (!Enemy.IsAlive)
            {
                OnDeadTick(knight, dt);
                return;
            }
            if (knight == null || knight.IsDead)
            {
                Cancel();
                return;
            }
            if (_cooldown > 0f) _cooldown -= dt;
            if (_phase == Phase.Idle)
            {
                if (_cooldown <= 0f && WantsToStart(knight)) TryStart(knight);
                return;
            }
            _time += dt;
            Step(knight, dt);
        }

        /// <summary>Starts the attack now (debug, tests). False while busy, dead or when no warning is free.</summary>
        public bool ForceAttack()
        {
            if (_phase != Phase.Idle || !Enemy.IsAlive) return false;
            var knight = Knight();
            return knight != null && !knight.IsDead && TryStart(knight);
        }

        private bool TryStart(PlayerHealth knight)
        {
            if (!Begin(knight))
            {
                _cooldown = 0.2f; // no free warning: try again soon (an attack never starts without its warning)
                return false;
            }
            Attacks++;
            _phase = Phase.WindUp;
            _time = 0f;
            Enemy.Halted = true;
            return true;
        }

        /// <summary>Idle: true when the Knight is where this attack starts (distance, line).</summary>
        protected abstract bool WantsToStart(PlayerHealth knight);

        /// <summary>Rents and shows the warning, plays the wind-up clip. False = nothing free, do not start.</summary>
        protected abstract bool Begin(PlayerHealth knight);

        /// <summary>WindUp / Strike / Recover step (<see cref="_time"/> already advanced).</summary>
        protected abstract void Step(PlayerHealth knight, float dt);

        /// <summary>The enemy is dead. Default: cancel (hide the warning).</summary>
        protected virtual void OnDeadTick(PlayerHealth knight, float dt) => Cancel();

        /// <summary>Hides and returns this attack's warnings; the enemy walks again.</summary>
        public virtual void Cancel()
        {
            if (_phase != Phase.Idle && _enemy != null && _enemy.IsAlive)
            {
                _enemy.Halted = false;
                _enemy.ResetAction();
            }
            _phase = Phase.Idle;
            _time = 0f;
        }

        /// <summary>Recover is over: the enemy walks again, the cooldown runs.</summary>
        protected void EndAttack(float cooldown)
        {
            _phase = Phase.Idle;
            _time = 0f;
            _cooldown = cooldown;
            if (Enemy.IsAlive)
            {
                Enemy.Halted = false;
                Enemy.ResetAction();
            }
        }

        protected void Enter(Phase phase)
        {
            _phase = phase;
            _time = 0f;
        }

        protected void RaiseStruck() => Struck?.Invoke(this);

        /// <summary>Deals <paramref name="share"/> of the Knight's max health; raises <see cref="HitKnight"/>. Returns the damage dealt.</summary>
        protected float Hurt(PlayerHealth knight, float share)
        {
            if (knight == null || knight.IsDead || share <= 0f) return 0f;
            float dealt = knight.TakeDamage(share * knight.Max, transform.position); // KAN-187: telegraphed hit, arc on this side
            HitKnight?.Invoke(this, dealt);
            return dealt;
        }

        /// <summary>The Knight (from the shared shot pool, like <see cref="RangedAttack"/>); null without one.</summary>
        protected static PlayerHealth Knight()
        {
            var shots = EnemyProjectiles.Current;
            return shots != null ? shots.Player : null;
        }

        public static float Planar(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        /// <summary>Gives a rented circle back (hidden or still flashing). Returns null for the caller's field.</summary>
        protected static GroundTelegraph ReleaseCircle(GroundTelegraph circle, bool hide)
        {
            if (circle == null) return null;
            if (hide) circle.Hide();
            var pool = EnemyTelegraphs.Current;
            if (pool != null) pool.Release(circle);
            return null;
        }

        /// <summary>Gives a rented lane back (hidden or still flashing). Returns null for the caller's field.</summary>
        protected static LineTelegraph ReleaseLine(LineTelegraph line, bool hide)
        {
            if (line == null) return null;
            if (hide) line.Hide();
            var pool = EnemyTelegraphs.Current;
            if (pool != null) pool.Release(line);
            return null;
        }
    }
}
