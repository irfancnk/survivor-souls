using System;
using System.Collections.Generic;
using SurvivorSouls.Gameplay.World;
using UnityEngine;
using Random = UnityEngine.Random;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Off-screen enemy spawner (KAN-29). Pacing comes from an <see cref="EnemyWaveTable"/> asset (regular groups plus
    /// bursts, capped alive count, weighted enemy types); this component only decides <em>where</em>.
    /// <para>
    /// Where: candidates on a ring around the player, each pushed out until the body (feet and head) is at least
    /// <see cref="Rules.screenMargin"/> m outside what the live camera sees (<see cref="ScreenFootprint"/>), and never
    /// closer than <see cref="Rules.minDistance"/>. Enabled <see cref="SpawnGate"/>s (pier foot, wreck, archways) are
    /// candidates too. A candidate must be walkable (<see cref="IWalkability"/>: no sea, wall or prop) and not next to a
    /// spawn of the last moment (groups spread out). Candidates ahead of and beside the player's movement weigh more than
    /// ones behind. No valid candidate = no spawn: retry after <see cref="Rules.retryDelay"/> (never pop in on screen).
    /// </para>
    /// <para>
    /// Enemies the player outruns (beyond <see cref="Rules.recycleDistance"/> and hidden) are moved to a fresh spawn
    /// point instead of being deleted. Pools per enemy type, prewarmed: no Instantiate / Destroy during play.
    /// Every spawn is audited with <c>Camera.WorldToViewportPoint</c>; a body inside the viewport counts in
    /// <see cref="Counters.OnScreenSpawns"/> and logs a warning (acceptance check). Counters are logged every
    /// <see cref="statsLogInterval"/> s in development builds.
    /// </para>
    /// <para>
    /// Chamber difficulty (KAN-46): <see cref="SetDifficulty"/> scales the table at run time (caps and burst sizes up,
    /// intervals down, enemy max health up) without touching the asset. Call it before <c>Start</c> so the pools are
    /// prewarmed for the bigger caps.
    /// </para>
    /// </summary>
    public sealed class EnemySpawner : MonoBehaviour
    {
        public enum Verdict : byte { Valid, OnScreen, Blocked, Crowded, OutOfRange }

        [Serializable]
        public struct Rules
        {
            [Tooltip("No spawn closer than this to the player (m).")]
            [Min(0f)] public float minDistance;
            [Tooltip("Gap (m) between the body and the screen edge, on the ground and at head height.")]
            [Min(0f)] public float screenMargin;
            [Tooltip("Body height (m) used for the head test: taller than the enemy, so hair and raised arms stay hidden.")]
            [Min(0f)] public float bodyHeight;
            [Tooltip("Ground radius (m) that must be free of walls, props and the sea.")]
            [Min(0.05f)] public float bodyRadius;
            [Tooltip("Extra random distance (m) beyond the needed ring radius.")]
            [Min(0f)] public float ringJitter;
            [Tooltip("Ring candidates tested per spawn (stratified angles).")]
            [Range(4, 32)] public int candidates;
            [Tooltip("Weight of a candidate straight behind the movement (ahead and sides = 1).")]
            [Range(0f, 1f)] public float behindWeight;
            [Tooltip("A spawn needs this much room (m) from spawns of the last crowdTime seconds.")]
            [Min(0f)] public float crowdRadius;
            [Min(0f)] public float crowdTime;
            [Tooltip("Gates farther than this from the player (m) are ignored.")]
            [Min(0f)] public float gateMaxDistance;
            [Tooltip("Hidden enemies farther than this (m) are moved to a new spawn point.")]
            [Min(1f)] public float recycleDistance;
            [Min(0.05f)] public float recycleInterval;
            [Tooltip("Wait (s) after a spawn found no valid point.")]
            [Min(0.01f)] public float retryDelay;
            [Range(1, 8)] public int maxSpawnsPerFrame;
            [Tooltip("Corner rays above the horizon are cut here (m).")]
            [Min(10f)] public float farLimit;

            public static Rules Default => new Rules
            {
                minDistance = 12f,
                screenMargin = 2.5f,
                bodyHeight = 2.1f,
                bodyRadius = 0.5f,
                ringJitter = 3f,
                candidates = 12,
                behindWeight = 0.15f,
                crowdRadius = 2.5f,
                crowdTime = 1.2f,
                gateMaxDistance = 32f,
                recycleDistance = 30f,
                recycleInterval = 0.5f,
                retryDelay = 0.3f,
                maxSpawnsPerFrame = 2,
                farLimit = 120f,
            };
        }

        /// <summary>Running totals since the scene started. "Attempts" = point searches, "Candidates" = points tested.</summary>
        public struct Counters
        {
            public int Attempts, Candidates, RejectedOnScreen, RejectedBlocked, RejectedCrowded, RejectedRange;
            public int NoPoint, Spawned, Recycled, OnScreenSpawns;

            public override string ToString() =>
                $"attempts={Attempts} candidates={Candidates} rejectedOnScreen={RejectedOnScreen} blocked={RejectedBlocked} " +
                $"crowded={RejectedCrowded} range={RejectedRange} noPoint={NoPoint} spawned={Spawned} recycled={Recycled} " +
                $"onScreenSpawns={OnScreenSpawns}";
        }

        private struct Candidate
        {
            public Vector3 Position;
            public float Weight;
        }

        private const int RecentCapacity = 16;
        private const int DebugCapacity = 64;
        private const int MaxPushSteps = 4;
        private const float PushStep = 2f;
        private const float MovingSpeed = 0.6f; // m/s: below this the player counts as standing
        private const float IdleBias = 0.35f;  // standing still: keep a little of the last heading's preference

        [SerializeField] private EnemyWaveTable waves;
        [SerializeField] private Transform player;
        [SerializeField] private WorldStreamer world;
        [SerializeField] private Camera viewCamera;
        [SerializeField] private Rules rules = Rules.Default;
        [Tooltip("Seconds between counter log lines in development builds. 0 = never.")]
        [SerializeField, Min(0f)] private float statsLogInterval = 60f;

        private readonly Dictionary<Enemy, Stack<Enemy>> _pools = new Dictionary<Enemy, Stack<Enemy>>();
        private readonly Dictionary<Enemy, Enemy> _prefabOf = new Dictionary<Enemy, Enemy>();
        private readonly List<Enemy> _live = new List<Enemy>(); // spawned by this spawner: alive or dying
        private readonly Candidate[] _candidates = new Candidate[40];
        private readonly Vector3[] _recent = new Vector3[RecentCapacity];
        private readonly float[] _recentTime = new float[RecentCapacity];
        private readonly Vector3[] _debugPoints = new Vector3[DebugCapacity];
        private readonly Verdict[] _debugVerdicts = new Verdict[DebugCapacity];
        private Action<Enemy> _releaseAction;
        private IWalkability _walk;
        private ScreenFootprint _feet, _head;
        private Counters _stats;
        private float _clock, _groupTimer, _burstTimer, _retry, _recycleTimer, _statsTimer;
        private int _phase = -1, _pending, _recentNext, _debugNext, _debugCount, _created;
        private Vector3 _lastPlayerPos;
        private Vector2 _heading;
        private float _headingBias;
        private bool _started;
        private float _capMul = 1f, _intervalMul = 1f, _healthMul = 1f;

        /// <summary>An enemy was put into the world: position, true when it was a recycle (moved back in) rather than a spawn.</summary>
        public event Action<Enemy, Vector3, bool> Placed;

        public Counters Stats => _stats;
        /// <summary>Enemies out of the pool (alive + dying).</summary>
        public int InUse => _live.Count;
        /// <summary>Enemies instantiated so far (prewarm included). Stays flat during play when the pool is big enough.</summary>
        public int Created => _created;
        public float RunTime => _clock;
        public int Pending => _pending;
        public Rules SpawnRules => rules;
        public Camera ViewCamera => viewCamera;
        public EnemyWaveTable Waves => waves;
        public float CapMultiplier => _capMul;
        public float IntervalMultiplier => _intervalMul;
        public float HealthMultiplier => _healthMul;

        /// <summary>
        /// Chamber scaling (KAN-46): max alive and burst sizes x <paramref name="cap"/>, group and burst intervals x
        /// <paramref name="interval"/>, enemy max health x <paramref name="health"/>. 1 / 1 / 1 = the table as written.
        /// </summary>
        public void SetDifficulty(float cap, float interval, float health)
        {
            _capMul = Mathf.Max(0.1f, cap);
            _intervalMul = Mathf.Max(0.05f, interval);
            _healthMul = Mathf.Max(0.01f, health);
        }

        /// <summary>Max alive of <paramref name="phase"/> after Chamber scaling.</summary>
        public int MaxAliveOf(in EnemyWaveTable.Phase phase) => Mathf.RoundToInt(phase.maxAlive * _capMul);

        private void Awake() => _releaseAction = Release;

        private void Start() => Begin();

        /// <summary>Tests / tools: wires the spawner without a scene. Call before the first <see cref="Tick"/>.</summary>
        public void Configure(EnemyWaveTable table, Transform target, Camera camera, IWalkability walkability, Rules? spawnRules = null)
        {
            waves = table;
            player = target;
            viewCamera = camera;
            _walk = walkability;
            if (spawnRules.HasValue) rules = spawnRules.Value;
            _started = false;
            Begin();
        }

        /// <summary>
        /// Uses <paramref name="walkability"/> instead of the streamed world (boss arena, KAN-59). Call before
        /// <c>Start</c>; a started spawner switches at once.
        /// </summary>
        public void UseWalkability(IWalkability walkability)
        {
            _walk = walkability;
            if (_started) Enemy.Walkability = walkability;
        }

        /// <summary>Ground shape spawns and enemy steering test against.</summary>
        public IWalkability Walkability => _walk ?? world;

        private void Begin()
        {
            if (_started) return;
            _started = true;
            _releaseAction ??= Release;
            if (_walk == null && world != null) _walk = world;
            if (viewCamera == null) viewCamera = Camera.main;
            Enemy.Walkability = _walk;
            if (world != null) Enemy.LandMaxZ = world.PlayableMax.y + 0.8f;
            if (player != null) _lastPlayerPos = player.position;
            Prewarm();
        }

        private void OnDestroy()
        {
            if (ReferenceEquals(Enemy.Walkability, _walk)) Enemy.Walkability = null;
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>One spawner step: heading, wave clock, pending spawns, recycling, stats. Public for tests.</summary>
        public void Tick(float dt)
        {
            if (!_started) Begin();
            if (waves == null || player == null || viewCamera == null || dt <= 0f) return;
            TrackHeading(dt);
            _clock += dt;
            if (_clock >= waves.FirstSpawnDelay) AdvanceWaves(dt);
            SpawnPending(dt);
            RecycleFar(dt);
            LogStats(dt);
        }

        // ------------------------------------------------------------ Waves

        private void AdvanceWaves(float dt)
        {
            int index = waves.PhaseAt(_clock);
            if (index < 0) return;
            var phase = waves.GetPhase(index);
            if (index != _phase)
            {
                _phase = index;
                _groupTimer = 0f; // a new phase opens with a group
                _burstTimer = phase.burstInterval * _intervalMul;
            }
            float interval = phase.spawnInterval * _intervalMul;
            int maxAlive = MaxAliveOf(phase);

            _groupTimer -= dt;
            if (_groupTimer <= 0f)
            {
                _pending += Random.Range(Mathf.Max(1, phase.groupSize.x), Mathf.Max(1, phase.groupSize.y) + 1);
                _groupTimer = Mathf.Max(_groupTimer + interval, 0.5f * interval); // no catch-up flood after a hitch
            }
            if (phase.burstInterval > 0f)
            {
                _burstTimer -= dt;
                if (_burstTimer <= 0f)
                {
                    _pending += Mathf.RoundToInt(phase.burstSize * _capMul);
                    _burstTimer = phase.burstInterval * _intervalMul;
                }
            }
            _pending = Mathf.Clamp(_pending, 0, Mathf.Max(0, maxAlive - _live.Count)); // groups top up, never bank
        }

        private void SpawnPending(float dt)
        {
            if (_retry > 0f)
            {
                _retry -= dt;
                return;
            }
            if (_pending <= 0 || _phase < 0) return;
            var phase = waves.GetPhase(_phase);
            int maxAlive = MaxAliveOf(phase);
            for (int i = 0; i < rules.maxSpawnsPerFrame && _pending > 0 && _live.Count < maxAlive; i++)
            {
                var prefab = EnemyWaveTable.PickEnemy(phase, Random.value);
                if (prefab == null)
                {
                    _pending = 0;
                    return;
                }
                if (!TryFindSpawnPoint(out var pos))
                {
                    _retry = rules.retryDelay;
                    return;
                }
                SpawnAt(prefab, pos);
                _pending--;
            }
        }

        /// <summary>Takes <paramref name="prefab"/> from its pool and starts it at <paramref name="position"/>. Public for tests and boss spawners.</summary>
        public Enemy SpawnAt(Enemy prefab, Vector3 position)
        {
            var enemy = Take(prefab);
            _live.Add(enemy);
            enemy.Spawn(position, player, _releaseAction, _healthMul);
            Remember(position);
            Audit(position, "spawn");
            _stats.Spawned++;
            Placed?.Invoke(enemy, position, false);
            return enemy;
        }

        // ------------------------------------------------------------ Where

        /// <summary>
        /// Picks an off-screen, walkable, uncrowded point at least <see cref="Rules.minDistance"/> from the player,
        /// favouring the movement direction. False when there is none right now. No allocation.
        /// </summary>
        public bool TryFindSpawnPoint(out Vector3 position)
        {
            position = default;
            if (player == null || viewCamera == null) return false;
            _stats.Attempts++;
            RefreshFootprints();

            Vector3 p = player.position;
            var origin = new Vector2(p.x, p.z);
            int count = 0;
            float total = 0f;
            int n = Mathf.Clamp(rules.candidates, 1, _candidates.Length - 8);
            float slice = Mathf.PI * 2f / n;
            for (int i = 0; i < n; i++)
            {
                float angle = (i + Random.value) * slice;
                var dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
                float exit = Mathf.Max(_feet.ExitDistance(origin, dir), _head.ExitDistance(origin, dir));
                float r = Mathf.Max(rules.minDistance, exit + rules.screenMargin) + Random.value * rules.ringJitter;
                Vector3 pos = new Vector3(p.x + dir.x * r, 0f, p.z + dir.y * r);
                var verdict = Evaluate(ref pos, dir, push: true);
                Record(pos, verdict);
                if (verdict != Verdict.Valid) continue;
                float w = DirectionWeight(dir);
                _candidates[count++] = new Candidate { Position = pos, Weight = w };
                total += w;
            }

            var gates = SpawnGate.Active;
            for (int i = 0; i < gates.Count && count < _candidates.Length; i++)
            {
                var gate = gates[i];
                Vector2 jitter = Random.insideUnitCircle * gate.Spread;
                Vector3 pos = gate.transform.position + new Vector3(jitter.x, 0f, jitter.y);
                pos.y = 0f;
                var to = new Vector2(pos.x - p.x, pos.z - p.z);
                float d = to.magnitude;
                Verdict verdict;
                if (d < rules.minDistance || d > rules.gateMaxDistance)
                {
                    _stats.Candidates++;
                    _stats.RejectedRange++;
                    verdict = Verdict.OutOfRange;
                }
                else verdict = Evaluate(ref pos, to / d, push: false);
                Record(pos, verdict);
                if (verdict != Verdict.Valid) continue;
                float w = gate.Weight * DirectionWeight(to / d);
                _candidates[count++] = new Candidate { Position = pos, Weight = w };
                total += w;
            }

            if (count == 0 || total <= 0f)
            {
                _stats.NoPoint++;
                return false;
            }
            float roll = Random.value * total;
            for (int i = 0; i < count; i++)
            {
                roll -= _candidates[i].Weight;
                if (roll < 0f || i == count - 1)
                {
                    position = _candidates[i].Position;
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Checks one candidate. Ring points (<paramref name="push"/>) step outward along <paramref name="dir"/> when they
        /// are still near the screen or inside an obstacle; outward from a convex view only ever adds clearance.
        /// </summary>
        private Verdict Evaluate(ref Vector3 pos, Vector2 dir, bool push)
        {
            _stats.Candidates++;
            for (int step = 0; ; step++)
            {
                float clearance = Clearance(pos);
                bool onScreen = clearance < rules.screenMargin;
                bool blocked = !onScreen && _walk != null && _walk.IsBlocked(pos, rules.bodyRadius);
                if (!onScreen && !blocked) break;
                if (!push || step >= MaxPushSteps)
                {
                    if (onScreen) _stats.RejectedOnScreen++;
                    else _stats.RejectedBlocked++;
                    return onScreen ? Verdict.OnScreen : Verdict.Blocked;
                }
                float move = onScreen ? rules.screenMargin - clearance + 0.25f : PushStep;
                pos += new Vector3(dir.x, 0f, dir.y) * move;
            }
            if (IsCrowded(pos))
            {
                _stats.RejectedCrowded++;
                return Verdict.Crowded;
            }
            return Verdict.Valid;
        }

        /// <summary>Metres between the body at <paramref name="pos"/> and the visible area (min of feet and head). Negative = visible.</summary>
        private float Clearance(Vector3 pos)
        {
            var xz = new Vector2(pos.x, pos.z);
            return Mathf.Min(_feet.SignedDistance(xz), _head.SignedDistance(xz));
        }

        private void RefreshFootprints()
        {
            _feet = ScreenFootprint.Of(viewCamera, 0f, rules.farLimit);
            _head = ScreenFootprint.Of(viewCamera, rules.bodyHeight, rules.farLimit);
        }

        /// <summary>1 ahead and beside the movement, <see cref="Rules.behindWeight"/> straight behind; flat when there is no heading.</summary>
        private float DirectionWeight(Vector2 dir)
        {
            if (_headingBias <= 0f) return 1f;
            float dot = Vector2.Dot(dir, _heading);
            float t = Mathf.Clamp01((dot + 0.6f) / 0.8f);
            float shaped = Mathf.Lerp(rules.behindWeight, 1f, t * t * (3f - 2f * t));
            return Mathf.Lerp(1f, shaped, _headingBias);
        }

        private void TrackHeading(float dt)
        {
            Vector3 p = player.position;
            var delta = new Vector2(p.x - _lastPlayerPos.x, p.z - _lastPlayerPos.z);
            _lastPlayerPos = p;
            float speed = delta.magnitude / dt;
            if (speed > MovingSpeed)
            {
                var dir = delta / delta.magnitude;
                _heading = _heading == Vector2.zero ? dir : Vector2.Lerp(_heading, dir, 1f - Mathf.Exp(-8f * dt)).normalized;
                _headingBias = 1f;
            }
            else if (_heading != Vector2.zero)
            {
                _headingBias = Mathf.MoveTowards(_headingBias, IdleBias, dt);
            }
        }

        private bool IsCrowded(Vector3 pos)
        {
            float r2 = rules.crowdRadius * rules.crowdRadius;
            for (int i = 0; i < RecentCapacity; i++)
            {
                if (_recentTime[i] <= 0f || _clock - _recentTime[i] > rules.crowdTime) continue;
                Vector3 d = pos - _recent[i];
                if (d.x * d.x + d.z * d.z < r2) return true;
            }
            return false;
        }

        private void Remember(Vector3 pos)
        {
            _recent[_recentNext] = pos;
            _recentTime[_recentNext] = Mathf.Max(_clock, 1e-4f);
            _recentNext = (_recentNext + 1) % RecentCapacity;
        }

        private void Record(Vector3 pos, Verdict verdict)
        {
            _debugPoints[_debugNext] = pos;
            _debugVerdicts[_debugNext] = verdict;
            _debugNext = (_debugNext + 1) % DebugCapacity;
            _debugCount = Mathf.Min(_debugCount + 1, DebugCapacity);
        }

        /// <summary>Independent acceptance check: the body's feet or head inside the viewport at spawn time is a bug.</summary>
        private void Audit(Vector3 pos, string what)
        {
            if (!InViewport(pos) && !InViewport(pos + Vector3.up * rules.bodyHeight)) return;
            _stats.OnScreenSpawns++;
            Debug.LogWarning($"[EnemySpawner] {what} inside the visible screen at {pos} (camera {viewCamera.name}, aspect {viewCamera.aspect:F3})");
        }

        private bool InViewport(Vector3 world)
        {
            Vector3 v = viewCamera.WorldToViewportPoint(world);
            return v.z > 0f && v.x >= 0f && v.x <= 1f && v.y >= 0f && v.y <= 1f;
        }

        // ------------------------------------------------------------ Recycle

        private void RecycleFar(float dt)
        {
            _recycleTimer -= dt;
            if (_recycleTimer > 0f) return;
            _recycleTimer = rules.recycleInterval;
            float max2 = rules.recycleDistance * rules.recycleDistance;
            Vector3 p = player.position;
            bool fresh = false;
            int moved = 0;
            for (int i = 0; i < _live.Count && moved < 2; i++)
            {
                var e = _live[i];
                if (!e.IsAlive) continue;
                Vector3 d = e.transform.position - p;
                if (d.x * d.x + d.z * d.z < max2) continue;
                if (!fresh)
                {
                    RefreshFootprints();
                    fresh = true;
                }
                if (Clearance(e.transform.position) < 0f) continue; // visible: never pull it away in view
                if (!TryFindSpawnPoint(out var pos)) return;
                e.Relocate(pos);
                Remember(pos);
                Audit(pos, "recycle");
                _stats.Recycled++;
                Placed?.Invoke(e, pos, true);
                moved++;
            }
        }

        // ------------------------------------------------------------ Pool

        private void Prewarm()
        {
            if (waves == null) return;
            for (int i = 0; i < waves.PhaseCount; i++)
            {
                var entries = waves.GetPhase(i).enemies;
                if (entries == null) continue;
                foreach (var entry in entries)
                {
                    if (entry.prefab == null || _pools.ContainsKey(entry.prefab)) continue;
                    int count = Mathf.CeilToInt(waves.PrewarmFor(entry.prefab) * _capMul); // bigger Chamber caps (KAN-46)
                    var pool = _pools[entry.prefab] = new Stack<Enemy>(count);
                    for (int k = 0; k < count; k++) pool.Push(Create(entry.prefab));
                }
            }
        }

        private Enemy Create(Enemy prefab)
        {
            var enemy = Instantiate(prefab, transform);
            enemy.gameObject.SetActive(false);
            _prefabOf[enemy] = prefab;
            _created++;
            return enemy;
        }

        private Enemy Take(Enemy prefab)
        {
            if (!_pools.TryGetValue(prefab, out var pool)) _pools[prefab] = pool = new Stack<Enemy>();
            return pool.Count > 0 ? pool.Pop() : Create(prefab);
        }

        private void Release(Enemy enemy)
        {
            int i = _live.IndexOf(enemy);
            if (i >= 0)
            {
                _live[i] = _live[_live.Count - 1];
                _live.RemoveAt(_live.Count - 1);
            }
            if (_prefabOf.TryGetValue(enemy, out var prefab)) _pools[prefab].Push(enemy);
        }

        // ------------------------------------------------------------ Debug

        private void LogStats(float dt)
        {
            if (statsLogInterval <= 0f || !Debug.isDebugBuild) return;
            _statsTimer += dt;
            if (_statsTimer < statsLogInterval) return;
            _statsTimer = 0f;
            Debug.Log($"[EnemySpawner] t={_clock:F0}s alive={_live.Count} {_stats}");
        }

        private void OnDrawGizmos()
        {
            if (player == null || viewCamera == null) return;
            var feet = ScreenFootprint.Of(viewCamera, 0f, rules.farLimit);
            var head = ScreenFootprint.Of(viewCamera, rules.bodyHeight, rules.farLimit);
            DrawQuad(feet, 0.05f, new Color(1f, 0.9f, 0.2f));
            DrawQuad(head, rules.bodyHeight, new Color(1f, 0.55f, 0.1f));

            Gizmos.color = new Color(0.3f, 0.9f, 1f, 0.8f);
            DrawCircle(player.position, rules.minDistance);
            Gizmos.color = new Color(0.6f, 0.6f, 0.6f, 0.6f);
            DrawCircle(player.position, rules.recycleDistance);

            for (int i = 0; i < _debugCount; i++)
            {
                Gizmos.color = _debugVerdicts[i] switch
                {
                    Verdict.Valid => Color.green,
                    Verdict.OnScreen => Color.red,
                    Verdict.Blocked => new Color(0.45f, 0.45f, 0.45f),
                    Verdict.Crowded => new Color(0.3f, 0.5f, 1f),
                    _ => Color.magenta,
                };
                Gizmos.DrawSphere(_debugPoints[i] + Vector3.up * 0.3f, 0.3f);
            }
            Gizmos.color = Color.white;
            for (int i = 0; i < RecentCapacity; i++)
                if (_recentTime[i] > 0f) Gizmos.DrawWireSphere(_recent[i] + Vector3.up * 0.3f, 0.5f);

            Gizmos.matrix = viewCamera.transform.localToWorldMatrix;
            Gizmos.color = new Color(1f, 1f, 1f, 0.35f);
            Gizmos.DrawFrustum(Vector3.zero, viewCamera.fieldOfView, viewCamera.farClipPlane, viewCamera.nearClipPlane, viewCamera.aspect);
            Gizmos.matrix = Matrix4x4.identity;
        }

        private static void DrawQuad(in ScreenFootprint q, float y, Color color)
        {
            Gizmos.color = color;
            for (int i = 0; i < 4; i++)
            {
                Vector2 a = q[i], b = q[(i + 1) & 3];
                Gizmos.DrawLine(new Vector3(a.x, y, a.y), new Vector3(b.x, y, b.y));
            }
        }

        private static void DrawCircle(Vector3 centre, float radius)
        {
            const int segments = 48;
            Vector3 prev = centre + new Vector3(radius, 0.05f, 0f);
            for (int i = 1; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                Vector3 next = centre + new Vector3(Mathf.Cos(a) * radius, 0.05f, Mathf.Sin(a) * radius);
                Gizmos.DrawLine(prev, next);
                prev = next;
            }
        }
    }
}
