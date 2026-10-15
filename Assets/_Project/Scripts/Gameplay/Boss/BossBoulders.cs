using System;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.World;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Boss
{
    /// <summary>
    /// Boulders of the boss <see cref="BossFight.Move.Lobbed"/> attack (KAN-119), e.g. the Rotting Colossus's corpse boulder.
    /// A fixed pool of boulder bodies (mesh + capsule collider, built by BossBuilder); no allocation in play.
    /// <list type="bullet">
    /// <item><b>Flying</b>: the body follows a parabola to a ground point in a fixed time and tumbles; it hits nothing in the
    /// air. Its landing circle (<see cref="GroundTelegraph"/>, rented by the thrower) fills during the flight.</item>
    /// <item><b>Landing</b>: damage when the Knight is inside the impact radius, the circle flashes and goes back to its
    /// pool. With a rubble life the boulder stays as <b>rubble</b>: the Knight is pushed out of its body first, then its
    /// collider blocks the Knight, and <see cref="IsBlocked"/> (through <see cref="BossArena.Obstacles"/>) blocks enemies,
    /// boss bolts and charges. Rubble sinks into the ground at the end of its life.</item>
    /// <item>Nothing hits a dead Knight. <see cref="Clear"/> ends all (boss killed, scene end).</item>
    /// </list>
    /// On the "Boss/Boulders" object. Runs on scaled time.
    /// </summary>
    public sealed class BossBoulders : MonoBehaviour, IWalkability
    {
        /// <summary>Apex of a lob over the straight line (m): a high arc reads from the top-down camera.</summary>
        public const float ArcHeight = 4.5f;
        /// <summary>Hand height of the throw over the boss's ground point (m).</summary>
        public const float ThrowHeight = 3f;
        /// <summary>Seconds rubble takes to sink away at the end of its life.</summary>
        public const float SinkTime = 0.5f;
        /// <summary>The Knight's body radius for the push out of fresh rubble (m).</summary>
        public const float KnightRadius = 0.45f;
        private const float SpinSpeed = 420f; // degrees per second while flying

        private enum State : byte { Free, Flying, Rubble, Sinking }

        private struct Boulder
        {
            public State State;
            public Vector3 Start, Target;
            public float Time, Flight;
            public float Damage, Impact;
            public float RubbleRadius, Life;
            public GroundTelegraph Circle;
            public EnemyTelegraphs CirclePool;
            public float ProgressStart;
            public Vector3 SpinAxis;
        }

        [SerializeField] private PlayerHealth player;
        [Tooltip("Boulder bodies (unit radius mesh, scaled to the rubble radius) with a capsule collider each.")]
        [SerializeField] private Transform[] bodies = new Transform[0];

        private Boulder[] _boulders = new Boulder[0];
        private Collider[] _solids = new Collider[0];
        private PlayerMover _mover;
        private bool _moverChecked;

        /// <summary>The scene's boulder pool (set while enabled).</summary>
        public static BossBoulders Current { get; private set; }

        /// <summary>A boulder landed: centre, damage dealt to the Knight (0 = dodged).</summary>
        public event Action<Vector3, float> Landed;

        public int Capacity => bodies.Length;
        /// <summary>Boulders in the air.</summary>
        public int Flying { get; private set; }
        /// <summary>Rubble blocking the ground (sinking rubble no longer counts).</summary>
        public int RubbleCount { get; private set; }
        public Vector3 BodyPosition(int i) => bodies[i].position;
        public bool IsFlying(int i) => _boulders[i].State == State.Flying;
        public bool IsRubble(int i) => _boulders[i].State == State.Rubble;
        public bool SolidEnabled(int i) => _solids[i] != null && _solids[i].enabled;

        /// <summary>Tests and tools: wiring without the builder.</summary>
        public void Setup(PlayerHealth knight, Transform[] boulderBodies)
        {
            player = knight;
            bodies = boulderBodies ?? new Transform[0];
            Allocate();
        }

        private void Awake() => Allocate();

        private void OnEnable()
        {
            Current = this;
            BossArena.Obstacles = this;
        }

        private void OnDisable()
        {
            if (Current == this) Current = null;
            if (ReferenceEquals(BossArena.Obstacles, this)) BossArena.Obstacles = null;
            Clear();
        }

        private void Allocate()
        {
            if (_boulders.Length == bodies.Length) return;
            _boulders = new Boulder[bodies.Length];
            _solids = new Collider[bodies.Length];
            for (int i = 0; i < bodies.Length; i++)
            {
                if (bodies[i] == null) continue;
                _solids[i] = bodies[i].GetComponent<Collider>();
                Hide(i);
            }
            Flying = RubbleCount = 0;
        }

        /// <summary>
        /// Lobs a boulder from <paramref name="origin"/> (ground point under the boss) to <paramref name="target"/> (ground) in
        /// <paramref name="flight"/> s. On landing: <paramref name="damage"/> when the Knight is within
        /// <paramref name="impactRadius"/>, then rubble of <paramref name="rubbleRadius"/> m for <paramref name="rubbleLife"/> s
        /// (0 = none). <paramref name="circle"/> (rented from <paramref name="circlePool"/>, may be null) fills from
        /// <paramref name="progressStart"/> to 1 during the flight, then flashes and goes back. False when every boulder is in use
        /// (the circle stays with the caller).
        /// </summary>
        public bool Throw(Vector3 origin, Vector3 target, float flight, float impactRadius, float damage, float rubbleRadius,
            float rubbleLife, GroundTelegraph circle, EnemyTelegraphs circlePool, float progressStart)
        {
            Allocate();
            int slot = -1;
            for (int i = 0; i < _boulders.Length; i++)
                if (_boulders[i].State == State.Free && bodies[i] != null)
                {
                    slot = i;
                    break;
                }
            if (slot < 0 || flight <= 0f) return false;
            float spin = UnityEngine.Random.value * Mathf.PI * 2f;
            _boulders[slot] = new Boulder
            {
                State = State.Flying,
                Start = new Vector3(origin.x, origin.y + ThrowHeight, origin.z),
                Target = target,
                Flight = flight,
                Damage = damage,
                Impact = impactRadius,
                RubbleRadius = Mathf.Max(0.2f, rubbleRadius),
                Life = rubbleLife,
                Circle = circle,
                CirclePool = circlePool,
                ProgressStart = Mathf.Clamp01(progressStart),
                SpinAxis = new Vector3(Mathf.Cos(spin), 0.3f, Mathf.Sin(spin)).normalized,
            };
            var body = bodies[slot];
            body.position = _boulders[slot].Start;
            body.localScale = Vector3.one * _boulders[slot].RubbleRadius;
            if (_solids[slot] != null) _solids[slot].enabled = false; // a flying boulder blocks nothing
            body.gameObject.SetActive(true);
            Flying++;
            return true;
        }

        /// <inheritdoc />
        public bool IsBlocked(Vector3 position, float radius)
        {
            if (RubbleCount == 0) return false;
            for (int i = 0; i < _boulders.Length; i++)
            {
                ref readonly var b = ref _boulders[i];
                if (b.State != State.Rubble) continue;
                float dx = position.x - b.Target.x, dz = position.z - b.Target.z, r = b.RubbleRadius + radius;
                if (dx * dx + dz * dz < r * r) return true;
            }
            return false;
        }

        /// <summary>Removes every boulder and rubble at once and hides their circles (boss killed, scene end, tests).</summary>
        public void Clear()
        {
            for (int i = 0; i < _boulders.Length; i++)
            {
                if (_boulders[i].State == State.Free) continue;
                EndCircle(ref _boulders[i], hide: true);
                Hide(i);
            }
            Flying = RubbleCount = 0;
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>One step with <paramref name="dt"/> scaled seconds (0 while paused). Public for tests.</summary>
        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            for (int i = 0; i < _boulders.Length; i++)
            {
                ref var b = ref _boulders[i];
                switch (b.State)
                {
                    case State.Flying:
                        b.Time += dt;
                        float t = Mathf.Clamp01(b.Time / b.Flight);
                        var body = bodies[i];
                        body.position = Vector3.LerpUnclamped(b.Start, b.Target, t) + Vector3.up * (4f * ArcHeight * t * (1f - t));
                        body.Rotate(b.SpinAxis, SpinSpeed * dt, Space.World);
                        if (b.Circle != null) b.Circle.SetProgress(Mathf.Lerp(b.ProgressStart, 1f, t));
                        if (t >= 1f) Land(i, ref b);
                        break;
                    case State.Rubble:
                        b.Time += dt;
                        if (b.Time < b.Life) break;
                        b.State = State.Sinking; // sinking rubble blocks nothing: nobody gets stuck in it
                        b.Time = 0f;
                        RubbleCount--;
                        if (_solids[i] != null) _solids[i].enabled = false;
                        break;
                    case State.Sinking:
                        b.Time += dt;
                        bodies[i].position = b.Target + Vector3.down * (b.RubbleRadius * 1.6f * Mathf.Clamp01(b.Time / SinkTime));
                        if (b.Time >= SinkTime) Hide(i);
                        break;
                }
            }
        }

        private void Land(int i, ref Boulder b)
        {
            Flying--;
            Vector3 at = b.Target;
            float dealt = 0f;
            bool canHit = player != null && !player.IsDead;
            Vector3 knight = canHit ? player.transform.position : Vector3.zero;
            float dx = knight.x - b.Target.x, dz = knight.z - b.Target.z;
            float d2 = dx * dx + dz * dz;
            if (canHit && b.Damage > 0f && d2 <= b.Impact * b.Impact) dealt = player.TakeDamage(b.Damage);
            EndCircle(ref b, hide: false);
            bodies[i].position = b.Target;
            if (b.Life > 0f)
            {
                if (player != null) PushOut(b.Target, b.RubbleRadius + KnightRadius);
                b.State = State.Rubble;
                b.Time = 0f;
                RubbleCount++;
                if (_solids[i] != null) _solids[i].enabled = true;
            }
            else Hide(i);
            Landed?.Invoke(at, dealt);
        }

        /// <summary>The Knight stands where rubble lands: push it to the rubble's edge before the collider turns on.</summary>
        private void PushOut(Vector3 centre, float clear)
        {
            Vector3 p = player.transform.position;
            Vector3 away = p - centre;
            away.y = 0f;
            float d = away.magnitude;
            if (d >= clear) return;
            Vector3 dir = d > 0.001f ? away / d : player.transform.forward;
            if (!_moverChecked) // once: GetComponent of a missing component allocates in the editor
            {
                _mover = player.GetComponent<PlayerMover>();
                _moverChecked = true;
            }
            Vector3 offset = dir * (clear - d);
            if (_mover != null) _mover.Shove(offset);
            else player.transform.position += offset;
        }

        private void EndCircle(ref Boulder b, bool hide)
        {
            if (b.Circle == null) return;
            if (hide) b.Circle.Hide();
            else b.Circle.Flash();
            if (b.CirclePool != null) b.CirclePool.Release(b.Circle);
            b.Circle = null;
        }

        private void Hide(int i)
        {
            _boulders[i] = default; // State.Free
            if (_solids[i] != null) _solids[i].enabled = false;
            if (bodies[i] != null) bodies[i].gameObject.SetActive(false);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Current = null;
    }
}
