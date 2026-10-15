using System;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Progression;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Boss
{
    /// <summary>
    /// Boss Chamber flow (KAN-22, first slice KAN-58). Runs only when <see cref="ChamberSetup.Current"/> is a
    /// <see cref="ChamberType.Boss"/> Chamber (every 5th and the last of the Realm, from the difficulty asset):
    /// <list type="number">
    /// <item><b>Start</b>: the survival timer switches to <see cref="OpenCondition.KillBoss"/> rules (no clock; the HUD hides
    /// it) and the horde thins out (<see cref="BossData.HordeCap"/>, <see cref="BossData.HordeInterval"/>).</item>
    /// <item><b>Waiting</b> (<see cref="BossData.IntroDelay"/>), then <b>Rising</b>: a red ground warning where the boss will
    /// stand (<see cref="BossData.SpawnWarning"/>, walkable ground ahead of the Knight), then the boss spawns.</item>
    /// <item><b>Fighting</b>: the boss chases like any enemy (contact damage, hit markers, haptics come for free). When the
    /// cooldown is over it picks an attack by distance, stops (<see cref="Enemy.Halted"/>) and warns on the ground (never
    /// under 0.6 s), then strikes; short recovery, then the chase resumes. Nothing attacks a dead Knight.
    /// <list type="bullet">
    /// <item><b>Slam</b> (Knight within <see cref="BossData.SlamRange"/>): the circle telegraph fills, then the slam hits
    /// whatever is in the circle.</item>
    /// <item><b>Fan</b> (farther, within <see cref="BossData.FanRange"/>): red lanes fill towards the Knight, then a volley of
    /// <see cref="BossProjectiles"/> flies down them; pillars and the ring wall stop the bolts; one volley hits once.</item>
    /// <item><b>Charge</b> (phase 2 only, between <see cref="BossData.ChargeMinRange"/> and <see cref="BossData.ChargeMaxRange"/>):
    /// a wide lane fills, then the boss dashes down it (it stops early at a pillar or the wall) and stands still for a
    /// punish window. In phase 2 the fan and the charge take turns.</item>
    /// </list>
    /// Every hit is a share of max health, capped by the max-hit share (no instant kill).</item>
    /// <item><b>Phase 2</b> (<see cref="BossData.Phase2At"/> health): <see cref="PhaseChanged"/> (a heart drops, the bar's
    /// phase mark lights, a roar), shorter cooldowns and wind-ups, a faster walk, and the charge joins.</item>
    /// <item><b>Intro</b> (KAN-62, <see cref="BossData.IntroTime"/>): right after the boss rises it stands and faces the
    /// Knight; the Knight cannot move and takes no damage (<see cref="PlayerMover.InputLocked"/>,
    /// <see cref="PlayerHealth.Invulnerable"/>). <see cref="IntroStarted"/> / <see cref="IntroEnded"/> drive the camera pull-out
    /// and the name banner. Then the fight starts.</item>
    /// <item><b>Defeated</b>: gold soul wisps (<see cref="BossData.RewardXp"/>) burst from the body (<see cref="Defeated"/>:
    /// slow motion, "felled" banner). After the victory beat (<see cref="BossData.VictoryBeat"/>) the portal opens
    /// (<see cref="SurvivalTimer.Complete"/>: the usual toast, sound and camera shake) and <see cref="VictoryFinished"/> drops the
    /// guaranteed reward (<see cref="BossReward"/>).</item>
    /// </list>
    /// Everything runs on scaled time, so level-up cards and the Chamber fade-in pause it. On the "Boss" object (BossBuilder).
    /// KAN-108: the attacks come from <see cref="BossData.Attacks"/> (<see cref="BossAttack"/> entries per phase; the Warden's
    /// list is built from its old fields, so it plays as above). New types: <b>Lane</b> (cleaver chop, chain hook that drags
    /// the Knight in), <b>Spin</b> (a damage circle that follows the Knight), multi-dash <b>Charge</b> with a stun when a dash
    /// hits a pillar or the wall. Each attack plays its own animator state (<see cref="BossAttack.clip"/>); the intro and the
    /// phase 2 roar play <see cref="BossData.RoarClip"/>. Which boss a boss Chamber gets: <see cref="rotation"/> (KAN-112 asset; the first boss
    /// Chamber of the Realm the first entry, the next the second, round), else <see cref="data"/>.
    /// KAN-116: bullet and area types. <b>Nova</b> (a ring of bolts from the body, optional gaps) and <b>Spiral</b> (turning
    /// arms of bolts for a few seconds) warn with a circle around the body, then fire <see cref="BossProjectiles"/>; one ring
    /// or one whole spiral hits once. <b>Rain</b> drops circles one by one near the Knight (shared
    /// <see cref="EnemyTelegraphs"/> pool), each warns, then hits inside it. Spiral and Rain run in <see cref="Attack.Casting"/>.
    /// KAN-119: <b>Lobbed</b> (boulders thrown one by one at the Knight; each landing circle warns from the throw, the first
    /// already in the wind-up; a boulder leaves rubble that blocks the ground for a while, <see cref="BossBoulders"/>),
    /// <b>Summon</b> (enemies of a spawner type rise on a ring round the boss; circles warn where; never more than the
    /// entry's count alive) and <b>Teleport</b> (a circle warns at a walkable point near the Knight; the boss shrinks away,
    /// reappears there and grows back).
    /// KAN-121: <b>Shockwave</b> (a circle warns round the slam point; then rings grow from it one by one, each band hits
    /// once as it passes the Knight; outside the circle is safe), <b>Homing</b> (slow orbs that turn towards the Knight; the
    /// whole volley hits once) and <b>Beams</b> (lanes evenly round the body warn, then turn as live beams for a few
    /// seconds; the whole cast hits once). Shockwave and Beams run in <see cref="Attack.Casting"/>.
    /// KAN-111: timed entries (<see cref="BossAttack.every"/>): fight time counts while fighting; a timed entry is due
    /// <see cref="BossAttack.every"/> s after it last started (or after its phase began) and then wins the next pick over every
    /// band (the Bone Matriarch's phase 2 teleport every 8 s).
    /// KAN-174: <b>Barrage</b> entries are side casts, never picked: each has its own clock and, while the boss fights (any
    /// attack state) and the Knight lives, lobs a shot (<see cref="ShotKind.BlackFireball"/>, <see cref="EnemyProjectiles"/>)
    /// to a random point near the Knight with a landing circle from the shared pool (the Butcher's black fireballs, 1 per s).
    /// </summary>
    public sealed class BossFight : MonoBehaviour
    {
        public enum Stage
        {
            Inactive = 0,
            Waiting = 1,
            Rising = 2,
            Fighting = 3,
            Defeated = 4,
            /// <summary>KAN-62: the boss stands after rising; the Knight is held (no move, no damage).</summary>
            Intro = 5,
        }

        public enum Attack
        {
            Chase = 0,
            WindUp = 1,
            Recover = 2,
            /// <summary>The charge dash is moving the boss.</summary>
            Dashing = 3,
            /// <summary>KAN-108: the spin circle is live and follows the Knight.</summary>
            Spinning = 4,
            /// <summary>KAN-116: the boss stands and casts (spiral arms firing, rain circles falling).</summary>
            Casting = 5,
        }

        /// <summary>The boss's attack patterns (KAN-22).</summary>
        public enum Move
        {
            Slam = 0,
            Fan = 1,
            Charge = 2,
            /// <summary>KAN-108: a straight rectangle from the boss (cleaver chop; with pull, the chain hook).</summary>
            Lane = 3,
            /// <summary>KAN-108: a damage circle around the boss that follows the Knight for a while.</summary>
            Spin = 4,
            /// <summary>KAN-116: a ring of bolts from the body (optional gaps).</summary>
            Nova = 5,
            /// <summary>KAN-116: turning arms of bolts for a few seconds.</summary>
            Spiral = 6,
            /// <summary>KAN-116: ground circles that fall one by one near the Knight.</summary>
            Rain = 7,
            /// <summary>KAN-119: boulders lobbed at the Knight one by one; each leaves rubble for a while.</summary>
            Lobbed = 8,
            /// <summary>KAN-119: enemies of a spawner type rise round the boss.</summary>
            Summon = 9,
            /// <summary>KAN-119: the boss vanishes and reappears at a walkable point near the Knight.</summary>
            Teleport = 10,
            /// <summary>KAN-121: rings that grow from a slam point one by one (the Colossus double slam).</summary>
            Shockwave = 11,
            /// <summary>KAN-121: slow orbs that turn towards the Knight (the Matriarch's skulls).</summary>
            Homing = 12,
            /// <summary>KAN-121: lanes round the body that turn as live beams (the Matriarch's cross beams).</summary>
            Beams = 13,
            /// <summary>
            /// KAN-174: a side cast on its own clock, next to the attack pick: a lobbed shot every few seconds to a random
            /// point near the Knight (the Butcher's black fireballs). Never picked by score; never stops the chase.
            /// </summary>
            Barrage = 14,
        }

        private const float SpawnClearRadius = 1.4f; // free ground the boss body needs
        private const int SpawnTries = 12;
        private const float ChargeBodyRadius = 1.1f; // ground the boss body needs along the dash
        private const float ChargeProbeStep = 0.5f;
        private const float MinChargeLength = 2f;
        private const float BoltClearRadius = 0.15f; // = the bolt's block test
        private const float BoltProbeStep = 0.25f;
        private const float LaneClearRadius = 0.3f; // a cleaver / chain: thin; pillars cut it
        private const float LaneProbeStep = 0.25f;
        private const float PullSpeed = 18f;        // m/s the hook drags the Knight
        private const float PullGap = 0.8f;         // the hook stops this far outside the boss body
        private const float RedashPause = 0.25f;    // multi-charge: seconds between a dash end and the next warning
        private const float RoarCooldown = 0.3f;    // chase after the phase 2 roar before the next attack
        private const int NeverUsed = 1000;         // "attacks since last use" of an attack never used yet
        /// <summary>Most bolts in one nova ring (the bolt pool caps it too).</summary>
        public const int MaxRingBolts = 48;
        /// <summary>Bolts missing per nova gap.</summary>
        public const int GapBolts = 2;
        /// <summary>Most rain circles in one cast (the shared circle pool caps it too).</summary>
        public const int MaxRain = 16;
        /// <summary>Most summons of one boss alive at once (and per cast).</summary>
        public const int MaxSummons = 8;
        /// <summary>Seconds the boss takes to shrink away before a teleport (and to grow back after it).</summary>
        public const float VanishTime = 0.35f;
        /// <summary>KAN-174: a barrage shot leaves this high (m) above the boss's feet (plus the shot pool's throw height).</summary>
        public const float BarrageHand = 1.6f;
        /// <summary>KAN-174: apex (m) of a barrage lob over the straight line.</summary>
        public const float BarrageArc = 2.5f;
        /// <summary>KAN-174: hazard owner id of barrage shots (they leave no hazard; keeps them apart from the zombies').</summary>
        public const int BarrageOwner = -174;
        private const int BarrageTries = 6;     // random points tried for a landing spot (pillars / wall block one)
        private const float BarrageMinInterval = 0.2f;
        private const float VanishScale = 0.05f;
        private const float MinTeleportMove = 3f;   // a teleport always moves the boss at least this far
        /// <summary>
        /// Teleport points within this many metres sideways of the Knight come first: the portrait view is only ~11 m wide,
        /// so the warning circle and the boss stay on screen (in front of or behind the Knight).
        /// </summary>
        public const float TeleportMaxSide = 4f;
        private const float SummonClearRadius = 0.5f;
        /// <summary>Most shockwave rings in one attack (the shared circle pool caps it too).</summary>
        public const int MaxWaves = 4;
        /// <summary>KAN-109: most burning patches of one boss on the ground (a new one puts out its oldest).</summary>
        public const int MaxBurns = 8;
        /// <summary>KAN-109: hazard owner id of boss patches (ranged zombies count up from 1; one boss at a time).</summary>
        public const int BurnOwner = -109;

        [SerializeField] private BossData data;
        [Tooltip("KAN-112: bosses of this Realm's boss Chambers in order (round), a data asset. Empty = data above.")]
        [SerializeField] private BossRotation rotation;
        [Tooltip("KAN-109: other bosses in the build (Realm 2+ candidates). Never fought unless picked by the dev flag "
            + "-ss-boss-type (ForcedBoss) or a tool.")]
        [SerializeField] private BossData[] library = new BossData[0];
        [Tooltip("Rules for boss Chambers: OpenCondition KillBoss (no clock).")]
        [SerializeField] private ChamberRules bossRules;
        [SerializeField] private SurvivalTimer timer;
        [SerializeField] private EnemySpawner spawner;
        [SerializeField] private PlayerHealth player;
        [SerializeField] private SoulWispField souls;
        [SerializeField] private GroundTelegraph telegraph;
        [Tooltip("Lane warnings of the fan and the charge (KAN-61).")]
        [SerializeField] private LineTelegraph lines;
        [Tooltip("Fan bolts (KAN-61), nova and spiral bolts (KAN-116).")]
        [SerializeField] private BossProjectiles projectiles;
        [Tooltip("KAN-116: circle pool of the rain. Empty = the scene's EnemyTelegraphs.Current (the ranged zombies' pool).")]
        [SerializeField] private EnemyTelegraphs circles;
        [Tooltip("KAN-119: boulders of the lobbed attack. Empty = the scene's BossBoulders.Current.")]
        [SerializeField] private BossBoulders boulders;
        [Tooltip("KAN-174: shot pool of barrage entries (black fireballs). Empty = the scene's EnemyProjectiles.Current.")]
        [SerializeField] private EnemyProjectiles shots;

        private Enemy _boss;
        private float _bossHealthMultiplier = 1f; // BossData health over the body's type health, x the Chamber multiplier
        private float _time;
        private float _cooldown;
        private Vector3 _spawnPoint;
        private Vector3 _slamCentre;
        private float _windUp;   // seconds of the current warning (phase 2 shortens it)
        private float _recover;  // seconds of the current recovery
        private Vector3 _attackOrigin, _attackDir;
        private float _dashLeft, _dashDealt;
        private bool _dashHit;
        private BossAttack[] _attacks = new BossAttack[0];
        private int[] _lastUsed = new int[0];
        private float[] _dueAt = new float[0];  // KAN-111: fight time a timed entry is next due
        private float[] _barrageWait = new float[0]; // KAN-174: seconds until a barrage entry throws again
        private int _fireballs;
        private float _clock;                   // KAN-111: seconds of Fighting (scaled)
        private int _picks;
        private int _index = -1;          // the attack being warned / struck / recovered from
        private BossAttack _attack;
        private float _pendingCooldown;   // chase time after the current recovery
        private int _dashesLeft;
        private bool _dashBlocked;
        private float _spinLeft, _spinHitWait, _spinDealt;
        private float _pullLeft;
        private Vector3 _pullDir;
        private readonly Vector3[] _fanDirs = new Vector3[LineTelegraph.MaxLanes];
        private readonly Vector3[] _ringDirs = new Vector3[MaxRingBolts];
        private float _castLeft, _shotWait, _spiralAngle;
        private int _spiralFired;
        private readonly GroundTelegraph[] _rain = new GroundTelegraph[MaxRain];
        private readonly Vector3[] _rainAt = new Vector3[MaxRain];
        private readonly float[] _rainAge = new float[MaxRain];
        private int _rainPlaced, _rainLive;
        private float _rainNext;
        private EnemyTelegraphs _rainPool;
        private GroundTelegraph _throwCircle;   // the first boulder's landing circle while the throw winds up
        private EnemyTelegraphs _throwPool;
        private Vector3 _throwTarget;
        private int _thrown;
        private Enemy[] _summonTypes = new Enemy[0]; // per attack: the resolved summon prefab
        private readonly Enemy[] _summoned = new Enemy[MaxSummons];
        private readonly GroundTelegraph[] _summonCircles = new GroundTelegraph[MaxSummons];
        private readonly Vector3[] _summonAt = new Vector3[MaxSummons];
        private int _summonPlanned;
        private EnemyTelegraphs _summonPool;
        private Vector3 _teleportFrom, _teleportTo;
        private Vector3 _bossScale = Vector3.one;
        private bool _scaled;
        private float _growLeft;
        private PlayerMover _mover;
        private bool _moverChecked;
        private readonly GroundTelegraph[] _waves = new GroundTelegraph[MaxWaves];
        private readonly float[] _waveFront = new float[MaxWaves]; // front radius of each ring; -1 = over
        private readonly bool[] _waveHit = new bool[MaxWaves];
        private int _wavesStarted, _wavesLive;
        private float _wavesDealt;
        private EnemyTelegraphs _wavePool;
        private float _beamTurn, _beamDealt;
        private bool _beamHit;
        private bool _knightHeld;
        private bool _victoryDone;
        private Vector3 _deathPoint;

        public static BossFight Active { get; private set; }

        /// <summary>The boss stands up (after the spawn warning).</summary>
        public event Action<Enemy> Spawned;
        /// <summary>A slam starts winding up: centre on the ground, radius.</summary>
        public event Action<Vector3, float> SlamWarned;
        /// <summary>A slam landed: centre, damage dealt to the Knight (0 = dodged).</summary>
        public event Action<Vector3, float> Slammed;
        /// <summary>The boss died: wisps dropped, the victory beat starts (portal and reward follow).</summary>
        public event Action Defeated;
        /// <summary>KAN-62: the intro starts (boss risen, Knight held).</summary>
        public event Action<Enemy> IntroStarted;
        /// <summary>KAN-62: the intro is over (the fight starts, or the boss died during it).</summary>
        public event Action IntroEnded;
        /// <summary>KAN-62: the victory beat is over: the portal opened; the guaranteed reward goes here (where the boss fell).</summary>
        public event Action<Vector3> VictoryFinished;
        /// <summary>The boss entered a new phase (2). Hearts drop, the HUD lights the phase mark.</summary>
        public event Action<int> PhaseChanged;
        /// <summary>A fan winds up: boss ground point, aim direction.</summary>
        public event Action<Vector3, Vector3> FanWarned;
        /// <summary>A fan volley flew: boss ground point, bolts fired.</summary>
        public event Action<Vector3, int> FanFired;
        /// <summary>A charge winds up: start, direction, lane length.</summary>
        public event Action<Vector3, Vector3, float> ChargeWarned;
        /// <summary>KAN-168: a charge dash starts moving (start, direction), right after its warning.</summary>
        public event Action<Vector3, Vector3> DashStarted;
        /// <summary>A charge dash ended: where the boss stopped, damage dealt to the Knight (0 = dodged).</summary>
        public event Action<Vector3, float> Charged;
        /// <summary>KAN-108: a lane attack winds up: start, direction, length (after pillars), width.</summary>
        public event Action<Vector3, Vector3, float, float> LaneWarned;
        /// <summary>KAN-108: a lane attack struck: middle of the lane, damage dealt (0 = dodged).</summary>
        public event Action<Vector3, float> LaneStruck;
        /// <summary>KAN-108: the spin circle hit the Knight: where, damage dealt.</summary>
        public event Action<Vector3, float> SpinHit;
        /// <summary>KAN-108: an attack starts its wind-up (index into <see cref="Attacks"/>).</summary>
        public event Action<int> AttackStarted;
        /// <summary>KAN-116: a nova ring flew (boss ground point, bolts fired).</summary>
        public event Action<Vector3, int> NovaFired;
        /// <summary>KAN-116: a spiral ended (boss ground point, bolts fired in all).</summary>
        public event Action<Vector3, int> SpiralFired;
        /// <summary>KAN-116: a rain circle appeared (centre, radius).</summary>
        public event Action<Vector3, float> RainWarned;
        /// <summary>KAN-116: a rain circle struck (centre, damage dealt; 0 = dodged).</summary>
        public event Action<Vector3, float> RainStruck;
        /// <summary>KAN-119: a boulder left the boss's hand (landing centre, impact radius).</summary>
        public event Action<Vector3, float> BoulderThrown;
        /// <summary>KAN-119: a summon winds up (boss ground point, enemies coming).</summary>
        public event Action<Vector3, int> SummonWarned;
        /// <summary>KAN-119: summons rose (boss ground point, enemies spawned).</summary>
        public event Action<Vector3, int> Summoned;
        /// <summary>KAN-119: a teleport winds up (from, to).</summary>
        public event Action<Vector3, Vector3> TeleportWarned;
        /// <summary>KAN-119: the boss reappeared (from, to, damage dealt on arrival).</summary>
        public event Action<Vector3, Vector3, float> Teleported;
        /// <summary>KAN-121: a shockwave ring hit the Knight (where, damage dealt).</summary>
        public event Action<Vector3, float> WaveHit;
        /// <summary>KAN-121: the last shockwave ring reached the circle (centre, rings, damage dealt in all).</summary>
        public event Action<Vector3, int, float> ShockwaveEnded;
        /// <summary>KAN-121: a homing volley flew (boss ground point, orbs fired).</summary>
        public event Action<Vector3, int> HomingFired;
        /// <summary>KAN-121: beams wind up (boss ground point, lanes).</summary>
        public event Action<Vector3, int> BeamsWarned;
        /// <summary>KAN-121: the beams went out (boss ground point, damage dealt; 0 = dodged).</summary>
        public event Action<Vector3, float> BeamsEnded;
        /// <summary>KAN-109: a burning patch was left on the ground (centre, radius).</summary>
        public event Action<Vector3, float> BurnPlaced;
        /// <summary>KAN-174: a barrage shot (black fireball) was thrown: landing point, circle radius.</summary>
        public event Action<Vector3, float> FireballThrown;

        public BossData Data => data;
        /// <summary>The attack list of the fight (KAN-108).</summary>
        public BossAttack[] Attacks => _attacks;
        /// <summary>Index of the attack being warned / struck / recovered from (-1 before the first).</summary>
        public int CurrentAttackIndex => _index;
        /// <summary>The attack being warned / struck / recovered from.</summary>
        public BossAttack CurrentEntry => _attack;
        /// <summary>Dashes left after the current one (multi-charge).</summary>
        public int DashesLeft => _dashesLeft;
        /// <summary>The last dash hit a pillar or the wall (the boss is stunned in its recovery).</summary>
        public bool DashBlocked => _dashBlocked;
        /// <summary>Seconds of the current recovery (stun included).</summary>
        public float RecoverTime => _recover;
        public Stage Current { get; private set; } = Stage.Inactive;
        public Attack CurrentAttack { get; private set; } = Attack.Chase;
        /// <summary>The attack being warned / struck / recovered from (meaningless while chasing).</summary>
        public Move CurrentMove { get; private set; } = Move.Slam;
        /// <summary>1 until the boss falls to <see cref="BossData.Phase2At"/> health, then 2.</summary>
        public int Phase { get; private set; } = 1;
        /// <summary>KAN-111: scaled seconds spent Fighting (timed attacks count on it).</summary>
        public float FightTime => _clock;
        /// <summary>Seconds of the current (or last) warning, after the phase 2 factor and the 0.6 s floor.</summary>
        public float WindUpTime => _windUp;
        /// <summary>KAN-168: share of the current warning gone by, 0..1 (0 when no attack is winding up).</summary>
        public float WindUpProgress => CurrentAttack == Attack.WindUp && _windUp > 0f ? Mathf.Clamp01(_time / _windUp) : 0f;
        /// <summary>The boss body (null until it spawns).</summary>
        public Enemy Boss => _boss;
        /// <summary>Boss health 0..1 (1 before it spawns, 0 once defeated).</summary>
        public float HealthFraction => Current == Stage.Defeated ? 0f : _boss != null && _boss.IsAlive ? _boss.Health / _boss.MaxHealth : 1f;
        public GroundTelegraph Telegraph => telegraph;
        public LineTelegraph Lines => lines;
        public BossProjectiles Projectiles => projectiles;
        /// <summary>KAN-116: rain circles warning or striking right now.</summary>
        public int RainLive => _rainLive;
        /// <summary>KAN-116: bolts the current (or last) spiral fired.</summary>
        public int SpiralBolts => _spiralFired;
        /// <summary>KAN-119: boulders the current (or last) lobbed attack threw.</summary>
        public int Thrown => _thrown;
        /// <summary>KAN-174: barrage shots thrown this fight.</summary>
        public int Fireballs => _fireballs;
        /// <summary>KAN-174: the shot pool of barrage entries (wired, else the scene's).</summary>
        public EnemyProjectiles Shots => shots != null ? shots : EnemyProjectiles.Current;
        /// <summary>KAN-119: the boulder pool of the lobbed attack (wired, else the scene's).</summary>
        public BossBoulders Boulders => boulders != null ? boulders : BossBoulders.Current;
        /// <summary>KAN-119: summons of this boss still alive.</summary>
        public int SummonsAlive
        {
            get
            {
                int n = 0;
                for (int i = 0; i < MaxSummons; i++)
                    if (_summoned[i] != null && _summoned[i].IsAlive) n++;
                return n;
            }
        }
        /// <summary>KAN-121: shockwave rings growing right now.</summary>
        public int WavesLive => _wavesLive;
        /// <summary>KAN-121: front radius of shockwave ring <paramref name="i"/> (-1 = not growing).</summary>
        public float WaveFront(int i) => i >= 0 && i < _wavesStarted && i < MaxWaves ? _waveFront[i] : -1f;
        /// <summary>KAN-121: degrees per second the live beams turn (sign = way).</summary>
        public float BeamTurn => _beamTurn;
        /// <summary>KAN-119: where the current (or last) teleport goes.</summary>
        public Vector3 TeleportTarget => _teleportTo;
        /// <summary>The Knight is held by the intro (no move, no damage).</summary>
        public bool KnightHeld => _knightHeld;
        /// <summary>The victory beat is over (portal open, reward given).</summary>
        public bool VictoryDone => _victoryDone;
        /// <summary>Where the boss fell (valid once defeated).</summary>
        public Vector3 DeathPoint => _deathPoint;

        /// <summary>Tests: wiring without the scene builder.</summary>
        public void Setup(BossData bossData, ChamberRules rules, SurvivalTimer survivalTimer, EnemySpawner enemySpawner,
            PlayerHealth playerHealth, SoulWispField soulField, GroundTelegraph groundTelegraph,
            LineTelegraph laneTelegraph = null, BossProjectiles bolts = null, BossData[] roster = null,
            EnemyTelegraphs circlePool = null, BossBoulders boulderPool = null, EnemyProjectiles shotPool = null)
        {
            circles = circlePool;
            shots = shotPool;
            boulders = boulderPool;
            data = bossData;
            rotation = roster != null ? BossRotation.Create(roster) : null;
            bossRules = rules;
            timer = survivalTimer;
            spawner = enemySpawner;
            player = playerHealth;
            souls = soulField;
            telegraph = groundTelegraph;
            lines = laneTelegraph;
            projectiles = bolts;
        }

        private void OnEnable()
        {
            Active = this;
            Enemy.Damaged += OnEnemyDamaged;
        }

        private void OnDisable()
        {
            if (Active == this) Active = null;
            Enemy.Damaged -= OnEnemyDamaged;
            HoldKnight(false);
            CancelRain();
            CancelWaves();
            CancelPending();
            RestoreScale();
            ClearFireballs();
        }

        private void Start() => Begin(ChamberSetup.Current);

        /// <summary>Starts the fight when <paramref name="plan"/> is a boss Chamber; otherwise stays inactive. Public for tests.</summary>
        public bool Begin(in ChamberPlan plan)
        {
            if (plan.Type != ChamberType.Boss || Current != Stage.Inactive) return false;
            var pick = BossFor(plan.Chamber);
            if (pick != null) data = pick;
            if (data == null || data.Prefab == null) return false;
            _attacks = data.Attacks;
            _lastUsed = new int[_attacks.Length];
            for (int i = 0; i < _lastUsed.Length; i++) _lastUsed[i] = -NeverUsed;
            _clock = 0f;
            _dueAt = new float[_attacks.Length];
            for (int i = 0; i < _dueAt.Length; i++) _dueAt[i] = _attacks[i].every;
            _barrageWait = new float[_attacks.Length];
            for (int i = 0; i < _barrageWait.Length; i++) _barrageWait[i] = BarrageInterval(_attacks[i]);
            _fireballs = 0;
            _failed = new int[_attacks.Length];
            _summonTypes = new Enemy[_attacks.Length];
            for (int i = 0; i < _attacks.Length; i++) _summonTypes[i] = ResolveSummon(_attacks[i]);
            _picks = 0;
            _index = -1;
            if (timer != null && bossRules != null) timer.UseRules(bossRules);
            if (spawner != null)
                spawner.SetDifficulty(plan.CapMultiplier * data.HordeCap, plan.IntervalMultiplier * data.HordeInterval, plan.HealthMultiplier);

            _boss = Instantiate(data.Prefab, transform);
            _boss.name = data.Prefab.name;
            _boss.gameObject.SetActive(false);
            _bossHealthMultiplier = data.MaxHealth / Mathf.Max(1f, _boss.BaseMaxHealth) * plan.HealthMultiplier;
            SetStage(Stage.Waiting);
            return true;
        }

        /// <summary>
        /// Boss of boss Chamber <paramref name="chamber"/>: the n-th boss Chamber of the Realm (from the difficulty asset, else
        /// every 5th) gets <see cref="rotation"/>'s n-th boss (round). Null = no list (keep <see cref="data"/>).
        /// </summary>
        public BossData BossFor(int chamber)
        {
            var forced = FindBoss(ForcedBoss);
            if (forced != null) return forced;
            if (rotation == null || rotation.Count == 0) return null;
            var setup = FindAnyObjectByType<ChamberSetup>(); // once per boss Chamber
            var difficulty = setup != null ? setup.Difficulty : null;
            int ordinal = 0;
            for (int c = 1; c < chamber; c++)
                if (difficulty != null ? difficulty.TypeOf(c) == ChamberType.Boss : c % 5 == 0) ordinal++;
            return rotation.For(ordinal);
        }

        /// <summary>The boss list per boss Chamber (KAN-108).</summary>
        public BossData[] Bosses => rotation != null ? rotation.Bosses : Array.Empty<BossData>();

        /// <summary>The boss list asset (KAN-112). Null = every boss Chamber fights <see cref="data"/>.</summary>
        public BossRotation Rotation => rotation;

        /// <summary>KAN-109: other bosses in the build (see <see cref="ForcedBoss"/>).</summary>
        public BossData[] Library => library;

        /// <summary>
        /// KAN-109, dev builds and tests: roster id (e.g. PyreLord; body prefab Enemy_&lt;id&gt;) or asset name of the boss every
        /// boss Chamber gets, from the boss list, the library or the default data. Empty or unknown = the normal pick.
        /// </summary>
        public static string ForcedBoss { get; set; }

        /// <summary>KAN-109: the boss of this fight's lists whose body is Enemy_&lt;<paramref name="id"/>&gt; or whose asset is named so.</summary>
        public BossData FindBoss(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            var hit = FindIn(Bosses, id) ?? FindIn(library, id);
            return hit != null ? hit : Matches(data, id) ? data : null;
        }

        private static BossData FindIn(BossData[] list, string id)
        {
            if (list == null) return null;
            foreach (var d in list)
                if (Matches(d, id)) return d;
            return null;
        }

        private static bool Matches(BossData d, string id) =>
            d != null && (string.Equals(d.name, id, StringComparison.OrdinalIgnoreCase)
                          || d.Prefab != null && string.Equals(d.Prefab.name, "Enemy_" + id, StringComparison.OrdinalIgnoreCase));

        /// <summary>Tests and tools: the library without the scene builder.</summary>
        public void SetLibrary(BossData[] bossLibrary) => library = bossLibrary ?? new BossData[0];

        private void Update() => Tick(Time.deltaTime);

        /// <summary>One step with <paramref name="dt"/> scaled seconds (0 while paused). Public for tests.</summary>
        public void Tick(float dt)
        {
            if (dt <= 0f) return;
            switch (Current)
            {
                case Stage.Waiting:
                    _time += dt;
                    if (_time >= data.IntroDelay) BeginRising();
                    break;
                case Stage.Rising:
                    _time += dt;
                    if (telegraph != null) telegraph.SetProgress(_time / data.SpawnWarning);
                    if (_time >= data.SpawnWarning) SpawnBoss();
                    break;
                case Stage.Intro:
                    _time += dt;
                    if (_boss != null && player != null) FaceKnight();
                    if (_time >= data.IntroTime) EndIntro();
                    break;
                case Stage.Fighting:
                    _clock += dt;
                    TickAttack(dt);
                    break;
                case Stage.Defeated:
                    if (_victoryDone) break;
                    _time += dt;
                    if (_time >= data.VictoryBeat) FinishVictory();
                    break;
            }
        }

        private void BeginRising()
        {
            var p = player != null ? player.transform : null;
            _spawnPoint = p != null ? FindSpawnPoint(p.position, p.forward, data.SpawnDistance) : transform.position;
            if (telegraph != null) telegraph.Show(_spawnPoint, 1.6f);
            SetStage(Stage.Rising);
        }

        private void SpawnBoss()
        {
            if (telegraph != null) telegraph.Flash();
            _boss.Spawn(_spawnPoint, player != null ? player.transform : null, null, _bossHealthMultiplier);
            _cooldown = data.FirstSlamDelay;
            CurrentAttack = Attack.Chase;
            Phase = 1;
            if (data.IntroTime <= 0f)
            {
                SetStage(Stage.Fighting);
                Spawned?.Invoke(_boss);
                return;
            }
            SetStage(Stage.Intro);
            if (player != null) FaceKnight(); // also halts the boss: it stands for its entrance
            if (!string.IsNullOrEmpty(data.RoarClip)) _boss.PlayAction(data.RoarClip);
            HoldKnight(true);
            Spawned?.Invoke(_boss);
            IntroStarted?.Invoke(_boss);
        }

        private void EndIntro()
        {
            HoldKnight(false);
            if (_boss != null)
            {
                _boss.Halted = false;
                _boss.ResetAction();
            }
            SetStage(Stage.Fighting);
            IntroEnded?.Invoke();
        }

        /// <summary>Holds (or frees) the Knight: no stick input, no damage. Only the intro holds it.</summary>
        private void HoldKnight(bool hold)
        {
            if (_knightHeld == hold || player == null) return;
            if (_mover == null) _mover = player.GetComponent<PlayerMover>();
            if (_mover != null) _mover.InputLocked = hold;
            player.Invulnerable = hold;
            _knightHeld = hold;
        }

        private void TickAttack(float dt)
        {
            if (_boss == null || !_boss.IsAlive) return;
            bool playerDead = player == null || player.IsDead;
            if (!playerDead) TickBarrage(dt); // KAN-174: side casts run next to whatever the boss does
            switch (CurrentAttack)
            {
                case Attack.Chase:
                    _cooldown -= dt;
                    if (playerDead || _cooldown > 0f) break;
                    ChooseAttack(PlanarDistance(_boss.transform.position, player.transform.position));
                    break;
                case Attack.WindUp:
                    if (playerDead)
                    {
                        HideWarnings();
                        EndAttack(0f);
                        break;
                    }
                    _time += dt;
                    float progress = _time / _windUp;
                    if (CurrentMove == Move.Lobbed)
                    {
                        if (_throwCircle != null) _throwCircle.SetProgress(_time / (_windUp + Flight(_attack)));
                    }
                    else if (CurrentMove == Move.Summon)
                    {
                        for (int i = 0; i < _summonPlanned; i++)
                            if (_summonCircles[i] != null) _summonCircles[i].SetProgress(progress);
                    }
                    else if (UsesCircle(CurrentMove))
                    {
                        if (telegraph != null) telegraph.SetProgress(progress);
                        if (CurrentMove == Move.Teleport) ScaleBoss(Mathf.Clamp01((_windUp - _time) / Mathf.Min(VanishTime, _windUp * 0.5f)));
                    }
                    else if (lines != null) lines.SetProgress(progress, _windUp - _time); // KAN-167: snap in the last 0.15 s
                    if (_time < _windUp) break;
                    Release();
                    break;
                case Attack.Dashing:
                    TickDash(dt);
                    break;
                case Attack.Spinning:
                    TickSpin(dt, playerDead);
                    break;
                case Attack.Casting:
                    if (playerDead)
                    {
                        CancelRain();
                        CancelWaves();
                        if (CurrentMove == Move.Beams && lines != null) lines.Hide();
                        EndAttack(0f);
                        break;
                    }
                    if (CurrentMove == Move.Spiral) TickSpiral(dt);
                    else if (CurrentMove == Move.Lobbed) TickLobbed(dt);
                    else if (CurrentMove == Move.Shockwave) TickWaves(dt);
                    else if (CurrentMove == Move.Beams) TickBeams(dt);
                    else TickRain(dt);
                    break;
                case Attack.Recover:
                    _time += dt;
                    TickPull(dt);
                    TickGrow(dt);
                    if (_time < _recover) break;
                    if (CurrentMove == Move.Charge && _dashesLeft > 0 && !playerDead && TryBeginCharge(_index, true)) break;
                    EndAttack(_pendingCooldown);
                    break;
            }
        }

        private static bool UsesCircle(Move move) =>
            move == Move.Slam || move == Move.Spin || move == Move.Nova || move == Move.Spiral || move == Move.Teleport
            || move == Move.Shockwave || move == Move.Homing;

        private void Release()
        {
            switch (CurrentMove)
            {
                case Move.Slam: LandSlam(); break;
                case Move.Fan: ReleaseFan(); break;
                case Move.Charge: BeginDash(); break;
                case Move.Lane: StrikeLane(); break;
                case Move.Spin: BeginSpin(); break;
                case Move.Nova: ReleaseNova(); break;
                case Move.Spiral: BeginSpiral(); break;
                case Move.Lobbed: ReleaseLobbed(); break;
                case Move.Summon: ReleaseSummon(); break;
                case Move.Teleport: ReleaseTeleport(); break;
                case Move.Shockwave: ReleaseShockwave(); break;
                case Move.Homing: ReleaseHoming(); break;
                case Move.Beams: BeginBeams(); break;
            }
        }

        /// <summary>
        /// Picks the next attack (KAN-108): entries on in this phase whose range holds the Knight and that can start; the
        /// highest priority band wins; inside it the best weight x attacks-since-last-use (ties: list order). An attack that
        /// cannot start (a charge with no room) drops out and the pick runs again. Nothing fits: the chase goes on.
        /// </summary>
        private void ChooseAttack(float distance)
        {
            for (int tries = 0; tries < _attacks.Length; tries++)
            {
                int best = -1, bestPriority = int.MinValue;
                float bestScore = -1f;
                for (int i = 0; i < _attacks.Length; i++)
                {
                    var a = _attacks[i];
                    if (a.type == Move.Barrage) continue; // KAN-174: a side cast, never picked
                    if (!a.InPhase(Phase) || !a.InRange(distance) || !CanUse(i) || _lastUsed[i] == FailedPick) continue;
                    if (a.Timed && _clock < _dueAt[i]) continue; // KAN-111: not due yet
                    int priority = a.Timed ? TimedPriority : a.priority;
                    float score = a.weight * Mathf.Min(NeverUsed, _picks - _lastUsed[i]);
                    if (priority < bestPriority || (priority == bestPriority && score <= bestScore)) continue;
                    best = i;
                    bestPriority = priority;
                    bestScore = score;
                }
                if (best < 0) break;
                if (Begin(best)) break;
                _failed[best] = _lastUsed[best];
                _lastUsed[best] = FailedPick; // skip it in this pick only
            }
            RestoreFailed();
        }

        private const int FailedPick = int.MinValue;
        private const int TimedPriority = int.MaxValue; // KAN-111: a due timed entry beats every band
        private int[] _failed = new int[0];

        private void RestoreFailed()
        {
            for (int i = 0; i < _lastUsed.Length; i++)
                if (_lastUsed[i] == FailedPick) _lastUsed[i] = _failed[i];
        }

        /// <summary>The scene has what attack <paramref name="index"/> needs (lanes for lane attacks, bolts for the fan, ...).</summary>
        private bool CanUse(int index)
        {
            var a = _attacks[index];
            switch (a.type)
            {
                case Move.Fan: return a.count > 0 && lines != null && projectiles != null;
                case Move.Charge:
                case Move.Lane: return lines != null;
                case Move.Nova:
                case Move.Spiral: return a.count > 0 && projectiles != null;
                case Move.Rain:
                    var pool = RainPool;
                    return a.count > 0 && pool != null && pool.FreeCircles() > 0;
                case Move.Lobbed:
                    var circlePool = RainPool;
                    return a.count > 0 && Boulders != null && circlePool != null && circlePool.FreeCircles() > 0;
                case Move.Summon:
                    return a.count > 0 && spawner != null && _summonTypes[index] != null && SummonsAlive < Mathf.Min(a.count, MaxSummons);
                case Move.Shockwave: // every ring shows (no invisible wave)
                    var wavePool = RainPool;
                    return a.count > 0 && wavePool != null && wavePool.FreeCircles() >= Mathf.Min(a.count, MaxWaves);
                case Move.Homing: return a.count > 0 && projectiles != null;
                case Move.Beams: return a.count > 0 && lines != null && lines.Capacity > 0;
                case Move.Barrage:
                    var barragePool = RainPool;
                    return Shots != null && barragePool != null && barragePool.FreeCircles() > 0;
                default: return true;
            }
        }

        /// <summary>Starts attack <paramref name="index"/> (its warning). False when it cannot (no room for a charge).</summary>
        private bool Begin(int index)
        {
            var a = _attacks[index];
            switch (a.type)
            {
                case Move.Slam: BeginSlam(index); break;
                case Move.Fan: BeginFan(index); break;
                case Move.Charge:
                    if (!TryBeginCharge(index, false)) return false;
                    break;
                case Move.Lane: BeginLane(index); break;
                case Move.Spin: BeginSpinWindUp(index); break;
                case Move.Nova:
                case Move.Spiral: BeginCastWindUp(index); break;
                case Move.Rain: BeginRain(index); break;
                case Move.Lobbed: BeginLobbed(index); break;
                case Move.Summon: BeginSummon(index); break;
                case Move.Teleport:
                    if (!TryBeginTeleport(index)) return false;
                    break;
                case Move.Shockwave: BeginShockwave(index); break;
                case Move.Homing: BeginCastWindUp(index); break;
                case Move.Beams: BeginBeamsWindUp(index); break;
                default: return false;
            }
            return true;
        }

        /// <summary>
        /// Debug menu, dev screenshots and tests: starts the first <paramref name="move"/> attack of this phase (else of any
        /// phase) now, whatever the distance and cooldown (a charge still needs room). Only while fighting and chasing.
        /// Returns true when the attack began.
        /// </summary>
        public bool ForceAttack(Move move)
        {
            int any = -1;
            for (int i = 0; i < _attacks.Length; i++)
            {
                if (_attacks[i].type != move) continue;
                if (_attacks[i].InPhase(Phase)) return ForceAttack(i);
                if (any < 0) any = i;
            }
            return any >= 0 && ForceAttack(any);
        }

        /// <summary>Starts attack <paramref name="index"/> of <see cref="Attacks"/> now (see <see cref="ForceAttack(Move)"/>).</summary>
        public bool ForceAttack(int index)
        {
            // KAN-174: a barrage entry throws one shot now, whatever the boss is doing (it is not an attack state).
            if (index >= 0 && index < _attacks.Length && _attacks[index].type == Move.Barrage)
                return Current == Stage.Fighting && player != null && !player.IsDead && _boss != null && _boss.IsAlive
                       && ThrowFireball(index);
            if (Current != Stage.Fighting || CurrentAttack != Attack.Chase || player == null || player.IsDead) return false;
            if (_boss == null || !_boss.IsAlive || index < 0 || index >= _attacks.Length || !CanUse(index)) return false;
            return Begin(index);
        }

        /// <summary>Turns the boss to the Knight and stops its chase. Returns the ground direction.</summary>
        private Vector3 FaceKnight()
        {
            Vector3 from = _boss.transform.position;
            Vector3 to = player.transform.position - from;
            to.y = 0f;
            Vector3 dir = to.sqrMagnitude > 0.0001f ? to.normalized : _boss.transform.forward;
            _boss.transform.rotation = Quaternion.LookRotation(dir, Vector3.up); // commits to the attack: dodge sideways
            _boss.Halted = true;
            return dir;
        }

        /// <summary>Common start of every attack: bookkeeping for the pick, the wind-up timer, the body's clip.</summary>
        private void BeginWindUp(int index)
        {
            _attack = _attacks[index];
            bool pick = _index != index || CurrentAttack == Attack.Chase; // a re-dash of a multi-charge is not a new pick
            if (pick)
            {
                _lastUsed[index] = _picks++;
                if (_attack.Timed) _dueAt[index] = _clock + _attack.every;
                _dashesLeft = _attack.type == Move.Charge ? Mathf.Max(1, _attack.count) - 1 : 0;
            }
            _index = index;
            CurrentMove = _attack.type;
            CurrentAttack = Attack.WindUp;
            _windUp = data.TelegraphFor(_attack.Telegraph, Phase);
            _time = 0f;
            _pullLeft = 0f;
            _boss.PlayAction(_attack.clip);
            if (pick) AttackStarted?.Invoke(index);
        }

        private void BeginSlam(int index)
        {
            var a = _attacks[index];
            Vector3 from = _boss.transform.position;
            Vector3 dir = FaceKnight();
            _slamCentre = new Vector3(from.x, player.transform.position.y, from.z) + dir * a.reach;
            if (telegraph != null) telegraph.Show(_slamCentre, a.radius);
            BeginWindUp(index);
            SlamWarned?.Invoke(_slamCentre, a.radius);
        }

        private void LandSlam()
        {
            float dealt = 0f;
            if (player != null && !player.IsDead && PlanarDistance(player.transform.position, _slamCentre) <= _attack.radius)
                dealt = player.TakeDamage(data.DamageFor(_attack.damage, player.Max));
            if (telegraph != null) telegraph.Flash();
            BeginRecover(_attack.recover, _attack.cooldown);
            Burn(_slamCentre);
            Slammed?.Invoke(_slamCentre, dealt);
        }

        private void BeginFan(int index)
        {
            var a = _attacks[index];
            Vector3 from = _boss.transform.position;
            _attackDir = FaceKnight();
            _attackOrigin = new Vector3(from.x, player.transform.position.y, from.z);
            lines.ShowFan(_attackOrigin, _attackDir, a.count, a.spread, a.length, a.width);
            for (int i = 0; i < lines.LaneCount; i++) // each lane ends where its bolt will: the ring wall or a pillar (cover reads)
                lines.SetLaneLength(i, FreeRun(_attackOrigin, lines.LaneDirection(i), a.length, BoltClearRadius, BoltProbeStep));
            BeginWindUp(index);
            FanWarned?.Invoke(_attackOrigin, _attackDir);
        }

        private void ReleaseFan()
        {
            int count = Mathf.Min(lines.LaneCount, _fanDirs.Length);
            for (int i = 0; i < count; i++) _fanDirs[i] = lines.LaneDirection(i);
            int fired = projectiles.FireVolley(_attackOrigin, _fanDirs, count, _attack.speed, _attack.length,
                data.DamageFor(_attack.damage, player != null ? player.Max : 0f), _attack.radius);
            lines.Flash();
            BeginRecover(_attack.recover, _attack.cooldown);
            FanFired?.Invoke(_attackOrigin, fired);
        }

        /// <summary>
        /// Charge towards the Knight if the lane has room (it stops at pillars and the wall). <paramref name="redash"/>: the
        /// next dash of a multi-charge (same attack, warns again).
        /// </summary>
        private bool TryBeginCharge(int index, bool redash)
        {
            var a = _attacks[index];
            Vector3 from = _boss.transform.position;
            Vector3 to = player.transform.position - from;
            to.y = 0f;
            Vector3 dir = to.sqrMagnitude > 0.0001f ? to.normalized : _boss.transform.forward;
            float length = ChargeLength(from, dir, a.length);
            if (length < MinChargeLength) return false;
            if (redash) _dashesLeft--;
            _attackDir = FaceKnight();
            _attackOrigin = new Vector3(from.x, player.transform.position.y, from.z);
            _dashLeft = length;
            _dashBlocked = length < a.length - ChargeProbeStep * 0.5f; // cut short: it will slam into a pillar or the wall
            lines.ShowLine(_attackOrigin, _attackDir, length, a.width);
            BeginWindUp(index);
            ChargeWarned?.Invoke(_attackOrigin, _attackDir, length);
            return true;
        }

        /// <summary>Free run from <paramref name="from"/> along <paramref name="dir"/>, up to <paramref name="max"/> m.</summary>
        public static float ChargeLength(Vector3 from, Vector3 dir, float max) =>
            FreeRun(from, dir, max, ChargeBodyRadius, ChargeProbeStep);

        /// <summary>Length of a lane attack (chop, hook) from <paramref name="from"/>: pillars and the wall cut it.</summary>
        public static float LaneLength(Vector3 from, Vector3 dir, float max) =>
            FreeRun(from, dir, max, LaneClearRadius, LaneProbeStep);

        /// <summary>
        /// Free ground from <paramref name="from"/> along <paramref name="dir"/> for a body of <paramref name="radius"/>, probed in
        /// <paramref name="step"/> m steps, up to <paramref name="max"/> m (<see cref="Enemy.Walkability"/>, the sea edge).
        /// </summary>
        public static float FreeRun(Vector3 from, Vector3 dir, float max, float radius, float step)
        {
            var walk = Enemy.Walkability;
            float length = 0f;
            while (length + step <= max)
            {
                Vector3 p = from + dir * (length + step);
                if (!float.IsInfinity(Enemy.LandMaxZ) && p.z > Enemy.LandMaxZ - radius) break;
                if (walk != null && walk.IsBlocked(p, radius)) break;
                length += step;
            }
            return Mathf.Max(length, Mathf.Min(max, step)); // never a zero lane: the bolt still leaves the boss
        }

        private void BeginDash()
        {
            if (lines != null) lines.Flash();
            CurrentAttack = Attack.Dashing;
            _dashHit = false;
            _dashDealt = 0f;
            _time = 0f;
            DashStarted?.Invoke(_attackOrigin, _attackDir);
        }

        private void TickDash(float dt)
        {
            Vector3 from = _boss.transform.position;
            float step = Mathf.Min(_attack.speed * dt, _dashLeft);
            Vector3 next = from + _attackDir * step;
            _boss.transform.position = next;
            _dashLeft -= step;
            if (!_dashHit && player != null && !player.IsDead
                && BossProjectiles.SegmentDistance(from, next, player.transform.position) <= _attack.width * 0.5f)
            {
                _dashHit = true;
                _dashDealt = player.TakeDamage(data.DamageFor(_attack.damage, player.Max));
            }
            if (_dashLeft > 0.0001f) return;
            if (_dashBlocked) _dashesLeft = 0; // crashed: stunned, the combo ends
            float rest = _dashesLeft > 0 ? RedashPause : _attack.recover + (_dashBlocked ? _attack.stun : 0f);
            BeginRecover(rest, _attack.cooldown, _dashBlocked ? RecoverPose.Stunned : RecoverPose.Stand); // legs stop at once
            Charged?.Invoke(next, _dashDealt);
        }

        /// <summary>Lane attack (KAN-108): a rectangle from the boss towards the Knight; pillars and the wall shorten it.</summary>
        private void BeginLane(int index)
        {
            var a = _attacks[index];
            Vector3 from = _boss.transform.position;
            _attackDir = FaceKnight();
            _attackOrigin = new Vector3(from.x, player.transform.position.y, from.z);
            _dashLeft = LaneLength(_attackOrigin, _attackDir, a.length); // the lane's length after pillars
            lines.ShowLine(_attackOrigin, _attackDir, _dashLeft, a.width);
            BeginWindUp(index);
            LaneWarned?.Invoke(_attackOrigin, _attackDir, _dashLeft, a.width);
        }

        /// <summary>True when <paramref name="p"/> lies in the lane from <paramref name="origin"/> (length x width).</summary>
        public static bool InLane(Vector3 origin, Vector3 dir, float length, float width, Vector3 p)
        {
            float dx = p.x - origin.x, dz = p.z - origin.z;
            float along = dx * dir.x + dz * dir.z;
            float side = Mathf.Abs(dx * dir.z - dz * dir.x);
            return along >= 0f && along <= length && side <= width * 0.5f;
        }

        private void StrikeLane()
        {
            float dealt = 0f;
            if (player != null && !player.IsDead && InLane(_attackOrigin, _attackDir, _dashLeft, _attack.width, player.transform.position))
            {
                dealt = player.TakeDamage(data.DamageFor(_attack.damage, player.Max));
                if (_attack.pull > 0f)
                {
                    Vector3 to = _attackOrigin - player.transform.position;
                    to.y = 0f;
                    float room = to.magnitude - (_boss.HitRadius + PullGap);
                    _pullLeft = Mathf.Clamp(_attack.pull, 0f, Mathf.Max(0f, room));
                    _pullDir = to.sqrMagnitude > 0.0001f ? to.normalized : Vector3.zero;
                }
            }
            if (lines != null) lines.Flash();
            BeginRecover(_attack.recover, _attack.cooldown);
            LaneStruck?.Invoke(_attackOrigin + _attackDir * (_dashLeft * 0.5f), dealt);
        }

        /// <summary>The chain hook drags the Knight in during the recovery (walls stop it; a dead Knight stays).</summary>
        private void TickPull(float dt)
        {
            if (_pullLeft <= 0f) return;
            if (player == null || player.IsDead)
            {
                _pullLeft = 0f;
                return;
            }
            float step = Mathf.Min(PullSpeed * dt, _pullLeft);
            _pullLeft -= step;
            if (!_moverChecked) // once: GetComponent of a missing component allocates in the editor
            {
                _mover = player.GetComponent<PlayerMover>();
                _moverChecked = true;
            }
            if (_mover != null) _mover.Shove(_pullDir * step);
            else player.transform.position += _pullDir * step;
        }

        /// <summary>Spin (KAN-108): the circle around the boss fills, then the boss whirls and follows the Knight.</summary>
        private void BeginSpinWindUp(int index)
        {
            var a = _attacks[index];
            FaceKnight();
            Vector3 at = _boss.transform.position;
            if (telegraph != null) telegraph.Show(new Vector3(at.x, player.transform.position.y, at.z), a.radius);
            BeginWindUp(index);
            SlamWarned?.Invoke(at, a.radius);
        }

        private void BeginSpin()
        {
            CurrentAttack = Attack.Spinning;
            _spinLeft = _attack.duration;
            _spinHitWait = 0f;
            _spinDealt = 0f;
            _time = 0f;
        }

        private void TickSpin(float dt, bool playerDead)
        {
            Vector3 pos = _boss.transform.position;
            if (!playerDead)
            {
                Vector3 to = player.transform.position - pos;
                to.y = 0f;
                float d = to.magnitude;
                if (d > 0.05f)
                {
                    Vector3 dir = to / d;
                    Vector3 next = pos + dir * Mathf.Min(_attack.speed * dt, d);
                    var walk = Enemy.Walkability;
                    if (walk == null || !walk.IsBlocked(next, ChargeBodyRadius)) pos = next;
                    _boss.transform.position = pos;
                }
            }
            if (telegraph != null)
            {
                telegraph.MoveTo(new Vector3(pos.x, player != null ? player.transform.position.y : pos.y, pos.z));
                telegraph.SetProgress(1f);
            }
            _spinHitWait -= dt;
            if (!playerDead && _spinHitWait <= 0f && PlanarDistance(player.transform.position, pos) <= _attack.radius)
            {
                float dealt = player.TakeDamage(data.DamageFor(_attack.damage, player.Max));
                _spinDealt += dealt;
                _spinHitWait = Mathf.Max(0.1f, _attack.interval);
                SpinHit?.Invoke(player.transform.position, dealt);
            }
            _spinLeft -= dt;
            if (_spinLeft > 0f) return;
            if (telegraph != null) telegraph.Flash();
            BeginRecover(_attack.recover, _attack.cooldown);
        }

        /// <summary>Spin damage dealt in the current (or last) spin.</summary>
        public float SpinDealt => _spinDealt;

        // ------------------------------------------------------------ Nova, Spiral, Rain (KAN-116)

        /// <summary>Nova / spiral wind-up: the boss stands, a circle of <see cref="BossAttack.reach"/> m fills around it.</summary>
        private void BeginCastWindUp(int index)
        {
            var a = _attacks[index];
            Vector3 from = _boss.transform.position;
            _attackDir = FaceKnight();
            _attackOrigin = new Vector3(from.x, player.transform.position.y, from.z);
            if (telegraph != null) telegraph.Show(_attackOrigin, Mathf.Max(0.5f, a.reach));
            BeginWindUp(index);
            SlamWarned?.Invoke(_attackOrigin, a.reach);
        }

        private void ReleaseNova()
        {
            int n = RingDirections(_ringDirs, _attack.count, _attack.gaps, UnityEngine.Random.value * 360f);
            int fired = projectiles.FireVolley(_attackOrigin, _ringDirs, n, _attack.speed, _attack.length,
                data.DamageFor(_attack.damage, player != null ? player.Max : 0f), _attack.radius);
            if (telegraph != null) telegraph.Flash();
            BeginRecover(_attack.recover, _attack.cooldown);
            NovaFired?.Invoke(_attackOrigin, fired);
        }

        /// <summary>
        /// Fills <paramref name="buffer"/> with a ring of <paramref name="count"/> evenly spaced ground directions starting at
        /// <paramref name="startDegrees"/> (clockwise from +Z), minus <paramref name="gaps"/> evenly spaced gaps of
        /// <see cref="GapBolts"/> bolts. Returns the directions written. The first gap starts at slot 0.
        /// </summary>
        public static int RingDirections(Vector3[] buffer, int count, int gaps, float startDegrees)
        {
            count = Mathf.Clamp(count, 0, Mathf.Min(MaxRingBolts, buffer.Length));
            if (count == 0) return 0;
            gaps = Mathf.Clamp(gaps, 0, count / (GapBolts + 1)); // always bolts between the gaps
            float step = 360f / count;
            int n = 0;
            for (int i = 0; i < count; i++)
            {
                if (InGap(i, count, gaps)) continue;
                float rad = (startDegrees + i * step) * Mathf.Deg2Rad;
                buffer[n++] = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
            }
            return n;
        }

        private static bool InGap(int slot, int count, int gaps)
        {
            for (int g = 0; g < gaps; g++)
            {
                int first = g * count / gaps;
                if (slot >= first && slot < first + GapBolts) return true;
            }
            return false;
        }

        private void BeginSpiral()
        {
            if (telegraph != null) telegraph.Flash();
            CurrentAttack = Attack.Casting;
            _castLeft = _attack.duration;
            _shotWait = 0f;
            _spiralFired = 0;
            Vector3 d = _attackDir;
            _spiralAngle = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg; // the first arm fires at the Knight
            projectiles.NewVolley(); // the whole spiral hits once
            _time = 0f;
        }

        private void TickSpiral(float dt)
        {
            float interval = Mathf.Max(0.05f, _attack.interval);
            int arms = Mathf.Clamp(_attack.count, 1, MaxRingBolts);
            float damage = data.DamageFor(_attack.damage, player.Max);
            _shotWait -= dt;
            while (_shotWait <= 0f && _castLeft > 0f)
            {
                for (int k = 0; k < arms; k++)
                {
                    float rad = (_spiralAngle + k * 360f / arms) * Mathf.Deg2Rad;
                    if (projectiles.Fire(_attackOrigin, new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)), _attack.speed, _attack.length,
                        damage, _attack.radius)) _spiralFired++;
                }
                _shotWait += interval;
            }
            _spiralAngle += _attack.spread * dt;
            float face = _spiralAngle * Mathf.Deg2Rad;
            _boss.transform.rotation = Quaternion.LookRotation(new Vector3(Mathf.Sin(face), 0f, Mathf.Cos(face)), Vector3.up);
            _castLeft -= dt;
            if (_castLeft > 0f) return;
            BeginRecover(_attack.recover, _attack.cooldown);
            SpiralFired?.Invoke(_attackOrigin, _spiralFired);
        }

        /// <summary>
        /// KAN-109: a burning patch of the current attack at <paramref name="centre"/> (fire hazard of the scene's shared
        /// <see cref="GroundHazards"/> pool; none in the scene = no patch). One boss keeps at most <see cref="MaxBurns"/>.
        /// </summary>
        private void Burn(Vector3 centre)
        {
            if (!_attack.Burns || _boss == null || player == null) return;
            var pool = GroundHazards.Current;
            if (pool == null) return;
            var spec = new HazardSpec
            {
                kind = HazardKind.Fire, radius = _attack.burnRadius, life = _attack.burnLife,
                dps = _attack.burnDps * player.Max,
            };
            if (pool.Spawn(centre, spec, BurnOwner, MaxBurns)) BurnPlaced?.Invoke(centre, _attack.burnRadius);
        }

        /// <summary>The rain's circle pool: the wired one, else the scene's ranged-zombie pool.</summary>
        private EnemyTelegraphs RainPool => circles != null ? circles : EnemyTelegraphs.Current;

        /// <summary>Rain: the boss stands and casts; circles fall one by one near the Knight (each warns, then hits).</summary>
        private void BeginRain(int index)
        {
            FaceKnight();
            BeginWindUp(index); // the wind-up time is each circle's warning
            CurrentAttack = Attack.Casting;
            _rainPool = RainPool;
            _rainPlaced = 0;
            _rainLive = 0;
            _rainNext = 0f;
        }

        private void TickRain(float dt)
        {
            _time += dt;
            int count = Mathf.Min(_attack.count, MaxRain);
            while (_rainPlaced < count && _time >= _rainNext)
            {
                PlaceRain(_rainPlaced++);
                _rainNext += Mathf.Max(0f, _attack.interval);
            }
            for (int i = 0; i < _rainPlaced; i++)
            {
                var circle = _rain[i];
                if (circle == null) continue;
                _rainAge[i] += dt;
                if (_rainAge[i] < _windUp)
                {
                    circle.SetProgress(_rainAge[i] / _windUp);
                    continue;
                }
                float dealt = 0f;
                if (player != null && !player.IsDead && PlanarDistance(player.transform.position, _rainAt[i]) <= _attack.radius)
                    dealt = player.TakeDamage(data.DamageFor(_attack.damage, player.Max));
                circle.Flash();
                if (_rainPool != null) _rainPool.Release(circle);
                _rain[i] = null;
                _rainLive--;
                Burn(_rainAt[i]);
                RainStruck?.Invoke(_rainAt[i], dealt);
            }
            if (_rainPlaced >= count && _rainLive == 0) BeginRecover(_attack.recover, _attack.cooldown);
        }

        /// <summary>Rain circle <paramref name="i"/>: on the Knight (the first) or within the scatter of it. None free = skipped.</summary>
        private void PlaceRain(int i)
        {
            _rain[i] = null;
            _rainAge[i] = 0f;
            var circle = _rainPool != null ? _rainPool.RentCircle() : null;
            if (circle == null) return;
            Vector3 at = player.transform.position;
            if (i > 0)
            {
                Vector2 off = UnityEngine.Random.insideUnitCircle * _attack.reach;
                at += new Vector3(off.x, 0f, off.y);
            }
            if (!float.IsInfinity(Enemy.LandMaxZ)) at.z = Mathf.Min(at.z, Enemy.LandMaxZ - _attack.radius * 0.5f);
            _rainAt[i] = at;
            _rain[i] = circle;
            _rainLive++;
            circle.Show(at, _attack.radius);
            RainWarned?.Invoke(at, _attack.radius);
        }

        /// <summary>Hides and returns every live rain circle (Knight died, boss died, fight disabled).</summary>
        private void CancelRain()
        {
            for (int i = 0; i < MaxRain; i++)
            {
                var circle = _rain[i];
                if (circle == null) continue;
                circle.Hide();
                if (_rainPool != null) _rainPool.Release(circle);
                _rain[i] = null;
            }
            _rainLive = 0;
        }

        // ------------------------------------------------------------ Lobbed, Summon, Teleport (KAN-119)

        /// <summary>Boulder flight time with the warning floor: a landing circle shown at the throw warns at least 0.6 s.</summary>
        private static float Flight(in BossAttack a) => Mathf.Max(BossData.MinTelegraph, a.duration);

        /// <summary>Lobbed: the boss winds up; the first boulder's landing circle shows on the Knight already.</summary>
        private void BeginLobbed(int index)
        {
            var a = _attacks[index];
            Vector3 from = _boss.transform.position;
            _attackDir = FaceKnight();
            _attackOrigin = new Vector3(from.x, player.transform.position.y, from.z);
            _throwPool = RainPool;
            _throwTarget = ClampToLand(player.transform.position, a.radius);
            _throwCircle = _throwPool != null ? _throwPool.RentCircle() : null;
            if (_throwCircle != null) _throwCircle.Show(_throwTarget, a.radius);
            _thrown = 0;
            BeginWindUp(index);
        }

        private void ReleaseLobbed()
        {
            var circle = _throwCircle;
            _throwCircle = null; // the boulder owns it now
            ThrowBoulder(_throwTarget, circle, _windUp / (_windUp + Flight(_attack)));
            CurrentAttack = Attack.Casting;
            _shotWait = Mathf.Max(0.1f, _attack.interval);
            _time = 0f;
        }

        private void TickLobbed(float dt)
        {
            int count = Mathf.Max(1, _attack.count);
            _shotWait -= dt;
            if (_thrown < count && _shotWait <= 0f)
            {
                FaceKnight();
                Vector3 target = ClampToLand(player.transform.position, _attack.radius);
                var circle = _throwPool != null ? _throwPool.RentCircle() : null;
                if (circle != null) // no free circle: no boulder (nothing lands without its warning)
                {
                    circle.Show(target, _attack.radius);
                    ThrowBoulder(target, circle, 0f);
                }
                else _thrown++;
                _shotWait += Mathf.Max(0.1f, _attack.interval);
            }
            var rocks = Boulders;
            if (_thrown >= count && (rocks == null || rocks.Flying == 0)) BeginRecover(_attack.recover, _attack.cooldown);
        }

        /// <summary>One boulder to <paramref name="target"/> with its (rented) circle; the circle goes back when it cannot fly.</summary>
        private void ThrowBoulder(Vector3 target, GroundTelegraph circle, float progressStart)
        {
            _thrown++;
            var rocks = Boulders;
            bool thrown = rocks != null && rocks.Throw(_attackOrigin, target, Flight(_attack), _attack.radius,
                data.DamageFor(_attack.damage, player != null ? player.Max : 0f), _attack.width, _attack.obstacle, circle,
                _throwPool, progressStart);
            if (!thrown && circle != null)
            {
                circle.Hide();
                if (_throwPool != null) _throwPool.Release(circle);
            }
            if (thrown) BoulderThrown?.Invoke(target, _attack.radius);
        }

        private static Vector3 ClampToLand(Vector3 p, float radius)
        {
            if (!float.IsInfinity(Enemy.LandMaxZ)) p.z = Mathf.Min(p.z, Enemy.LandMaxZ - radius * 0.5f);
            return p;
        }

        /// <summary>The spawner type of a summon entry (prefab <c>Enemy_&lt;id&gt;</c>, else the id as the name). Null = none.</summary>
        private Enemy ResolveSummon(in BossAttack a)
        {
            if (a.type != Move.Summon || spawner == null || string.IsNullOrEmpty(a.summon)) return null;
            var type = spawner.FindType("Enemy_" + a.summon);
            return type != null ? type : spawner.FindType(a.summon);
        }

        /// <summary>Summon: the boss stands (roar clip); circles warn on a ring round it where the missing summons will rise.</summary>
        private void BeginSummon(int index)
        {
            var a = _attacks[index];
            Vector3 from = _boss.transform.position;
            _attackDir = FaceKnight();
            _attackOrigin = new Vector3(from.x, player.transform.position.y, from.z);
            int want = Mathf.Min(a.count, MaxSummons) - SummonsAlive;
            _summonPool = RainPool;
            _summonPlanned = 0;
            float baseAngle = Mathf.Atan2(_attackDir.x, _attackDir.z) * Mathf.Rad2Deg;
            float ring = Mathf.Max(1f, a.reach);
            var walk = Enemy.Walkability;
            for (int k = 0; k < want; k++)
            {
                float rad = (baseAngle + 360f * (k + 0.5f) / want) * Mathf.Deg2Rad; // none right on the Knight's line
                var dir = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
                Vector3 p = Clamp(_attackOrigin + dir * ring);
                if (walk != null && walk.IsBlocked(p, SummonClearRadius))
                {
                    p = Clamp(_attackOrigin + dir * (ring * 0.6f)); // a pillar or the wall: closer in
                    if (walk.IsBlocked(p, SummonClearRadius)) continue;
                }
                int n = _summonPlanned++;
                _summonAt[n] = p;
                var circle = _summonPool != null ? _summonPool.RentCircle() : null;
                _summonCircles[n] = circle;
                if (circle != null) circle.Show(p, 0.9f);
            }
            BeginWindUp(index);
            SummonWarned?.Invoke(_attackOrigin, _summonPlanned);
        }

        private void ReleaseSummon()
        {
            var type = _index >= 0 && _index < _summonTypes.Length ? _summonTypes[_index] : null;
            int spawned = 0;
            for (int i = 0; i < _summonPlanned; i++)
            {
                var circle = _summonCircles[i];
                if (circle != null)
                {
                    circle.Flash();
                    if (_summonPool != null) _summonPool.Release(circle);
                    _summonCircles[i] = null;
                }
                if (type == null || spawner == null) continue;
                int slot = FreeSummonSlot();
                if (slot < 0) break;
                _summoned[slot] = spawner.SpawnAt(type, _summonAt[i], onScreen: true); // they rise in sight, by design
                spawned++;
            }
            _summonPlanned = 0;
            BeginRecover(_attack.recover, _attack.cooldown);
            Summoned?.Invoke(_attackOrigin, spawned);
        }

        private int FreeSummonSlot()
        {
            for (int i = 0; i < MaxSummons; i++)
                if (_summoned[i] == null || !_summoned[i].IsAlive) return i;
            return -1;
        }

        /// <summary>A summon died: its pooled body may come back as a plain horde enemy, so it no longer counts.</summary>
        private void ForgetSummon(Enemy enemy)
        {
            for (int i = 0; i < MaxSummons; i++)
                if (_summoned[i] == enemy) _summoned[i] = null;
        }

        /// <summary>Teleport: a circle warns at a walkable point <see cref="BossAttack.length"/> m from the Knight. False = none.</summary>
        private bool TryBeginTeleport(int index)
        {
            var a = _attacks[index];
            if (!FindTeleportPoint(a.length, out var to)) return false;
            Vector3 from = _boss.transform.position;
            FaceKnight();
            _teleportFrom = from;
            _teleportTo = to;
            if (!_scaled) _bossScale = _boss.transform.localScale;
            if (telegraph != null) telegraph.Show(to, Mathf.Max(0.5f, a.radius));
            BeginWindUp(index);
            TeleportWarned?.Invoke(from, to);
            return true;
        }

        /// <summary>
        /// A walkable ground point <paramref name="distance"/> m from the Knight, on land, at least 3 m from where the boss
        /// stands: in front of or behind the Knight first (at most <see cref="TeleportMaxSide"/> m sideways, so it stays on the
        /// portrait screen), else any side. 24 tries per pass from a random start. False when there is none.
        /// </summary>
        private bool FindTeleportPoint(float distance, out Vector3 point)
        {
            Vector3 knight = player.transform.position;
            Vector3 from = _boss.transform.position;
            var walk = Enemy.Walkability;
            float start = UnityEngine.Random.value * 360f;
            const int tries = SpawnTries * 2;
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < tries; i++)
                {
                    float rad = (start + i * 360f / tries) * Mathf.Deg2Rad;
                    Vector3 p = Clamp(knight + new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad)) * Mathf.Max(1f, distance));
                    p.y = knight.y;
                    if (pass == 0 && Mathf.Abs(p.x - knight.x) > TeleportMaxSide) continue;
                    if (walk != null && walk.IsBlocked(p, SpawnClearRadius)) continue;
                    if (PlanarDistance(p, from) < MinTeleportMove) continue;
                    point = p;
                    return true;
                }
            point = from;
            return false;
        }

        private void ReleaseTeleport()
        {
            Vector3 at = _boss.transform.position;
            _boss.transform.position = new Vector3(_teleportTo.x, at.y, _teleportTo.z);
            FaceKnight();
            float dealt = 0f;
            if (_attack.damage > 0f && player != null && !player.IsDead
                && PlanarDistance(player.transform.position, _teleportTo) <= _attack.radius)
                dealt = player.TakeDamage(data.DamageFor(_attack.damage, player.Max));
            if (telegraph != null) telegraph.Flash();
            BeginRecover(Mathf.Max(_attack.recover, VanishTime), _attack.cooldown);
            _growLeft = VanishTime;
            Teleported?.Invoke(_teleportFrom, _teleportTo, dealt);
        }

        /// <summary>Boss size as a share of its own (the teleport vanish); never quite 0.</summary>
        private void ScaleBoss(float share)
        {
            if (!_scaled)
            {
                if (share >= 1f) return;
                _bossScale = _boss.transform.localScale;
                _scaled = true;
            }
            _boss.transform.localScale = _bossScale * Mathf.Max(VanishScale, Mathf.Clamp01(share));
        }

        private void TickGrow(float dt)
        {
            if (_growLeft <= 0f) return;
            _growLeft -= dt;
            if (_growLeft > 0f) ScaleBoss(1f - _growLeft / VanishTime);
            else RestoreScale();
        }

        private void RestoreScale()
        {
            _growLeft = 0f;
            if (!_scaled) return;
            _scaled = false;
            if (_boss != null) _boss.transform.localScale = _bossScale;
        }

        /// <summary>Hides and returns the circles of a throw or summon still winding up (Knight died, boss died, disabled).</summary>
        private void CancelPending()
        {
            if (_throwCircle != null)
            {
                _throwCircle.Hide();
                if (_throwPool != null) _throwPool.Release(_throwCircle);
                _throwCircle = null;
            }
            for (int i = 0; i < _summonPlanned; i++)
            {
                var circle = _summonCircles[i];
                if (circle == null) continue;
                circle.Hide();
                if (_summonPool != null) _summonPool.Release(circle);
                _summonCircles[i] = null;
            }
            _summonPlanned = 0;
        }

        // ------------------------------------------------------------ Barrage (KAN-174)

        private static float BarrageInterval(in BossAttack a) => Mathf.Max(BarrageMinInterval, a.interval);

        /// <summary>
        /// Barrage entries: each on its own clock (scaled time), next to whatever the boss does. Out of phase = the clock
        /// restarts; Knight out of range = it waits. A throw that cannot happen (no free circle, no landing spot) tries again
        /// next frame.
        /// </summary>
        private void TickBarrage(float dt)
        {
            float distance = -1f;
            for (int i = 0; i < _attacks.Length; i++)
            {
                if (_attacks[i].type != Move.Barrage) continue;
                if (!_attacks[i].InPhase(Phase))
                {
                    _barrageWait[i] = BarrageInterval(_attacks[i]);
                    continue;
                }
                if (distance < 0f) distance = PlanarDistance(_boss.transform.position, player.transform.position);
                if (!_attacks[i].InRange(distance)) continue;
                _barrageWait[i] -= dt;
                if (_barrageWait[i] > 0f || !ThrowFireball(i)) continue;
                _barrageWait[i] = Mathf.Max(0f, _barrageWait[i] + BarrageInterval(_attacks[i]));
            }
        }

        /// <summary>
        /// One barrage shot of entry <paramref name="index"/>: a random landing point near the Knight (never right on it), its
        /// circle shown for the whole flight, then a lob from the boss's hand. False when it cannot (no pool, no free circle,
        /// no landing spot).
        /// </summary>
        private bool ThrowFireball(int index)
        {
            var a = _attacks[index];
            var pool = Shots;
            var warnings = RainPool;
            if (pool == null || warnings == null || warnings.FreeCircles() <= 0 || !FindFireballTarget(a, out Vector3 target)) return false;
            var circle = warnings.RentCircle();
            if (circle == null) return false;
            circle.Show(target, a.radius);
            Vector3 from = _boss.transform.position;
            Vector3 hand = new Vector3(from.x, from.y + BarrageHand, from.z) + _boss.transform.forward * 0.6f;
            bool thrown = pool.FireLobbed(pool.NextVolley(), ShotKind.BlackFireball, hand, target, Flight(a), BarrageArc, a.radius,
                data.DamageFor(a.damage, player.Max), default, BarrageOwner, 0, false, circle, 0f);
            if (!thrown)
            {
                circle.Hide();
                warnings.Release(circle);
                return false;
            }
            _fireballs++;
            FireballThrown?.Invoke(target, a.radius);
            return true;
        }

        /// <summary>
        /// A landing point <see cref="BossAttack.length"/>..<see cref="BossAttack.reach"/> m from the Knight in a random
        /// direction, on land and off pillars / the ring wall. False when <see cref="BarrageTries"/> picks all fail.
        /// </summary>
        private bool FindFireballTarget(in BossAttack a, out Vector3 target)
        {
            Vector3 knight = player.transform.position;
            float near = Mathf.Min(a.length, a.reach), far = Mathf.Max(a.length, a.reach);
            var walk = Enemy.Walkability;
            for (int t = 0; t < BarrageTries; t++)
            {
                float angle = UnityEngine.Random.value * Mathf.PI * 2f;
                float d = UnityEngine.Random.Range(near, far);
                var p = ClampToLand(knight + new Vector3(Mathf.Cos(angle) * d, 0f, Mathf.Sin(angle) * d), a.radius);
                if (walk != null && walk.IsBlocked(p, a.radius * 0.5f)) continue;
                target = p;
                return true;
            }
            target = knight;
            return false;
        }

        /// <summary>Barrage shots still in the air go (boss died, fight disabled): nothing lands after the kill.</summary>
        private void ClearFireballs()
        {
            var pool = Shots;
            if (pool != null) pool.ClearKind(ShotKind.BlackFireball);
        }

        // ------------------------------------------------------------ Shockwave, Homing, Beams (KAN-121)

        /// <summary>Shockwave: the boss stands; a circle of the attack's radius fills round the slam point.</summary>
        private void BeginShockwave(int index)
        {
            var a = _attacks[index];
            Vector3 from = _boss.transform.position;
            Vector3 dir = FaceKnight();
            _attackOrigin = new Vector3(from.x, player.transform.position.y, from.z) + dir * a.reach;
            if (telegraph != null) telegraph.Show(_attackOrigin, Mathf.Max(0.5f, a.radius));
            BeginWindUp(index);
            SlamWarned?.Invoke(_attackOrigin, a.radius);
        }

        private void ReleaseShockwave()
        {
            if (telegraph != null) telegraph.Flash();
            CurrentAttack = Attack.Casting;
            _wavePool = RainPool;
            _wavesStarted = 0;
            _wavesLive = 0;
            _wavesDealt = 0f;
            _time = 0f;
        }

        /// <summary>Where a ring's front stops: its band's outer edge on the warning circle.</summary>
        private float WaveEnd(float half) => Mathf.Max(half, _attack.radius - half);

        private void TickWaves(float dt)
        {
            _time += dt;
            int count = Mathf.Clamp(_attack.count, 1, MaxWaves);
            float interval = Mathf.Max(0.05f, _attack.interval);
            while (_wavesStarted < count && _time >= _wavesStarted * interval) StartWave(_wavesStarted++);
            float half = Mathf.Max(0.05f, _attack.width) * 0.5f;
            float end = WaveEnd(half);
            float speed = Mathf.Max(0.5f, _attack.speed);
            bool canHit = player != null && !player.IsDead;
            float knight = canHit ? PlanarDistance(player.transform.position, _attackOrigin) : -1f;
            for (int i = 0; i < _wavesStarted; i++)
            {
                float prev = _waveFront[i];
                if (prev < 0f) continue;
                float next = Mathf.Min(end, prev + speed * dt);
                _waveFront[i] = next;
                if (canHit && !_waveHit[i] && knight >= prev - half && knight <= next + half) // the band swept over the Knight
                {
                    _waveHit[i] = true;
                    float dealt = player.TakeDamage(data.DamageFor(_attack.damage, player.Max));
                    _wavesDealt += dealt;
                    canHit = !player.IsDead;
                    WaveHit?.Invoke(player.transform.position, dealt);
                }
                var circle = _waves[i];
                if (circle != null) circle.ShowWave(_attackOrigin, next, next / end);
                if (next < end) continue;
                EndWave(i);
            }
            if (_wavesStarted < count || _wavesLive > 0) return;
            BeginRecover(_attack.recover, _attack.cooldown);
            ShockwaveEnded?.Invoke(_attackOrigin, count, _wavesDealt);
        }

        private void StartWave(int i)
        {
            _waveFront[i] = 0f;
            _waveHit[i] = false;
            _waves[i] = _wavePool != null ? _wavePool.RentCircle() : null;
            _wavesLive++;
        }

        private void EndWave(int i)
        {
            _waveFront[i] = -1f;
            _wavesLive--;
            var circle = _waves[i];
            if (circle == null) return;
            circle.Hide();
            if (_wavePool != null) _wavePool.Release(circle);
            _waves[i] = null;
        }

        /// <summary>Hides and returns every growing ring (Knight died, boss died, fight disabled).</summary>
        private void CancelWaves()
        {
            for (int i = 0; i < MaxWaves; i++)
            {
                var circle = _waves[i];
                if (circle != null)
                {
                    circle.Hide();
                    if (_wavePool != null) _wavePool.Release(circle);
                    _waves[i] = null;
                }
                _waveFront[i] = -1f;
            }
            _wavesLive = 0;
        }

        /// <summary>Homing: the orbs leave over the spread aimed at where the Knight stands now; they steer on their own.</summary>
        private void ReleaseHoming()
        {
            Vector3 aim = FaceKnight();
            int count = Mathf.Clamp(_attack.count, 1, MaxRingBolts);
            float damage = data.DamageFor(_attack.damage, player != null ? player.Max : 0f);
            projectiles.NewVolley(); // the whole volley hits once
            int fired = 0;
            for (int k = 0; k < count; k++)
            {
                float angle = count > 1 ? -_attack.spread * 0.5f + _attack.spread * k / (count - 1) : 0f;
                if (projectiles.Fire(_attackOrigin, Quaternion.Euler(0f, angle, 0f) * aim, _attack.speed, _attack.length, damage,
                    _attack.radius, _attack.turn)) fired++;
            }
            if (telegraph != null) telegraph.Flash();
            BeginRecover(_attack.recover, _attack.cooldown);
            HomingFired?.Invoke(_attackOrigin, fired);
        }

        /// <summary>Beams: lanes evenly round the body fill (the Knight stands between two of them).</summary>
        private void BeginBeamsWindUp(int index)
        {
            var a = _attacks[index];
            Vector3 from = _boss.transform.position;
            _attackDir = FaceKnight();
            _attackOrigin = new Vector3(from.x, player.transform.position.y, from.z);
            int lanes = Mathf.Clamp(a.count, 1, Mathf.Min(LineTelegraph.MaxLanes, lines.Capacity));
            float step = 360f / lanes;
            // ShowFan centres the lanes on the direction: an odd count would put one on the Knight, so turn half a step.
            Vector3 dir = lanes % 2 == 1 ? Quaternion.Euler(0f, step * 0.5f, 0f) * _attackDir : _attackDir;
            lines.ShowFan(_attackOrigin, dir, lanes, step * (lanes - 1), a.length, a.width);
            BeginWindUp(index);
            BeamsWarned?.Invoke(_attackOrigin, lanes);
        }

        private void BeginBeams()
        {
            lines.Hold();
            CurrentAttack = Attack.Casting;
            _castLeft = Mathf.Max(0f, _attack.duration);
            _beamTurn = _attack.spread * (UnityEngine.Random.value < 0.5f ? -1f : 1f);
            _beamHit = false;
            _beamDealt = 0f;
            _time = 0f;
        }

        private void TickBeams(float dt)
        {
            lines.Turn(_beamTurn * dt);
            Vector3 face = lines.LaneDirection(0);
            if (face.sqrMagnitude > 0.0001f) _boss.transform.rotation = Quaternion.LookRotation(face, Vector3.up);
            if (!_beamHit && player != null && !player.IsDead)
            {
                Vector3 knight = player.transform.position;
                for (int i = 0; i < lines.LaneCount; i++)
                {
                    if (!InLane(_attackOrigin, lines.LaneDirection(i), lines.LaneLength(i), _attack.width, knight)) continue;
                    _beamHit = true; // the whole cast hits once
                    _beamDealt = player.TakeDamage(data.DamageFor(_attack.damage, player.Max));
                    break;
                }
            }
            _castLeft -= dt;
            if (_castLeft > 0f) return;
            lines.Flash();
            BeginRecover(_attack.recover, _attack.cooldown);
            BeamsEnded?.Invoke(_attackOrigin, _beamDealt);
        }

        /// <summary>
        /// Debug menu and dev screenshots (KAN-116): adds <paramref name="entry"/> to this fight's list (a copy; the asset
        /// stays as it is) and returns its index. Allocates: not for gameplay.
        /// </summary>
        public int AddAttack(in BossAttack entry)
        {
            int n = _attacks.Length;
            Array.Resize(ref _attacks, n + 1);
            Array.Resize(ref _lastUsed, n + 1);
            Array.Resize(ref _dueAt, n + 1);
            Array.Resize(ref _failed, n + 1);
            Array.Resize(ref _summonTypes, n + 1);
            Array.Resize(ref _barrageWait, n + 1);
            _attacks[n] = entry;
            _barrageWait[n] = BarrageInterval(entry);
            _summonTypes[n] = ResolveSummon(entry);
            _lastUsed[n] = -NeverUsed;
            _dueAt[n] = _clock + entry.every;
            return n;
        }

        /// <summary>
        /// What the body plays while it recovers (KAN-162): it stands still (<see cref="Enemy.StandState"/>), reels after a
        /// crash (<see cref="StunnedState"/>, else the Roar clip, else it stands), or keeps its clip (the phase 2 roar).
        /// Without this the move clip (the Charge run, the Chop swing) looped in place for the whole recover.
        /// </summary>
        private enum RecoverPose
        {
            Stand,
            Stunned,
            Keep,
        }

        /// <summary>Animator state a boss plays while stunned after a crash, when its controller has one (KAN-162).</summary>
        public const string StunnedState = "Stunned";

        private void BeginRecover(float seconds, float cooldown, RecoverPose pose = RecoverPose.Stand)
        {
            CurrentAttack = Attack.Recover;
            _recover = seconds;
            _pendingCooldown = cooldown;
            _time = 0f;
            if (_boss == null || pose == RecoverPose.Keep) return;
            if (pose == RecoverPose.Stunned && (_boss.PlayPose(StunnedState) || _boss.PlayPose(data.RoarClip))) return;
            _boss.PlayPose(Enemy.StandState);
        }

        private void EndAttack(float cooldown)
        {
            if (_boss != null)
            {
                _boss.Halted = false;
                _boss.ResetAction();
            }
            CurrentAttack = Attack.Chase;
            _cooldown = data.CooldownFor(cooldown, Phase);
            _dashesLeft = 0;
            _pullLeft = 0f;
            _time = 0f;
            RestoreScale();
        }

        private void HideWarnings()
        {
            if (telegraph != null) telegraph.Hide();
            if (lines != null) lines.Hide();
            CancelPending();
        }

        private void EnterPhase2()
        {
            Phase = 2;
            if (_boss != null) _boss.SpeedBoost = data.Phase2Speed;
            for (int i = 0; i < _attacks.Length; i++) // KAN-111: a phase 2 only timer starts with phase 2
                if (_attacks[i].Timed && !_attacks[i].InPhase(1)) _dueAt[i] = _clock + _attacks[i].every;
            if (data.PhaseRoar > 0f && Current == Stage.Fighting && CurrentAttack == Attack.Chase && player != null && !player.IsDead)
            {
                FaceKnight(); // stands and roars; the next attack waits
                _boss.PlayAction(data.RoarClip);
                BeginRecover(data.PhaseRoar, RoarCooldown, RecoverPose.Keep);
            }
            PhaseChanged?.Invoke(2);
        }

        private void OnEnemyDamaged(Enemy enemy, float amount, bool killed)
        {
            if (killed && enemy != _boss) ForgetSummon(enemy);
            if (enemy != _boss || (Current != Stage.Fighting && Current != Stage.Intro)) return;
            if (!killed)
            {
                if (Phase == 1 && data.Phase2At > 0f && enemy.Health <= enemy.MaxHealth * data.Phase2At) EnterPhase2();
                return;
            }
            if (Current == Stage.Intro)
            {
                HoldKnight(false);
                IntroEnded?.Invoke();
            }
            if (CurrentAttack == Attack.WindUp || CurrentAttack == Attack.Spinning || CurrentAttack == Attack.Casting) HideWarnings();
            CancelRain();
            CancelWaves();
            CancelPending();
            RestoreScale();
            _pullLeft = 0f;
            if (projectiles != null) projectiles.Clear(); // the fight is over: no bolt lands after the kill
            ClearFireballs(); // KAN-174: nor a black fireball
            var rocks = Boulders;
            if (rocks != null) rocks.Clear(); // no boulder lands after the kill; the rubble goes too
            CurrentAttack = Attack.Chase;
            _deathPoint = enemy.transform.position;
            SetStage(Stage.Defeated);
            DropReward(_deathPoint);
            if (data.VictoryBeat <= 0f) FinishVictory();
            Defeated?.Invoke();
        }

        /// <summary>End of the victory beat: the portal opens and the reward pick drops where the boss fell. Once.</summary>
        private void FinishVictory()
        {
            if (_victoryDone) return;
            _victoryDone = true;
            if (timer != null) timer.Complete(); // the portal opens
            VictoryFinished?.Invoke(_deathPoint);
        }

        private void DropReward(Vector3 at)
        {
            if (souls == null || data.RewardXp <= 0) return;
            int count = Mathf.Max(1, data.RewardWisps);
            int each = data.RewardXp / count;
            int rest = data.RewardXp - each * count;
            for (int i = 0; i < count; i++)
            {
                float a = i * (Mathf.PI * 2f / count);
                souls.Spawn(at + new Vector3(Mathf.Cos(a) * 0.7f, 1.2f, Mathf.Sin(a) * 0.7f), each + (i == 0 ? rest : 0));
            }
        }

        /// <summary>
        /// Walkable point <paramref name="distance"/> m from <paramref name="from"/>: straight ahead first, then fanning out
        /// left and right in 30 degree steps; never past the sea edge. Falls back to straight ahead.
        /// </summary>
        public static Vector3 FindSpawnPoint(Vector3 from, Vector3 forward, float distance)
        {
            forward.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            Vector3 fallback = Clamp(from + forward * distance);
            var walk = Enemy.Walkability;
            if (walk == null) return fallback;
            for (int i = 0; i < SpawnTries; i++)
            {
                float angle = ((i + 1) / 2) * 30f * (i % 2 == 0 ? 1f : -1f);
                Vector3 p = Clamp(from + Quaternion.Euler(0f, angle, 0f) * forward * distance);
                if (!walk.IsBlocked(p, SpawnClearRadius)) return p;
            }
            return fallback;
        }

        private static Vector3 Clamp(Vector3 p)
        {
            if (!float.IsInfinity(Enemy.LandMaxZ)) p.z = Mathf.Min(p.z, Enemy.LandMaxZ - SpawnClearRadius);
            return p;
        }

        private void SetStage(Stage stage)
        {
            Current = stage;
            _time = 0f;
        }

        private static float PlanarDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => Active = null;
    }
}
