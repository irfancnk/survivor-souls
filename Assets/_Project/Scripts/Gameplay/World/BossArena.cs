using System;
using SurvivorSouls.Gameplay.CameraRig;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.World
{
    /// <summary>
    /// Round arena of the boss Chambers (KAN-59 = KAN-22.2). A scene object made by the editor BossArenaBuilder, inactive
    /// by default and placed on the shore plain south of the streamed world (no overlap with the sea). <see cref="ChamberSetup"/>
    /// calls <see cref="Enter"/> in a <see cref="ChamberType.Boss"/> Chamber, before any other Awake / Start:
    /// <list type="bullet">
    /// <item>the <see cref="WorldStreamer"/> is switched off before its first Update, so no shore tile ever streams in (the
    /// snow ground quad, weather and footprints stay: they follow the Knight);</item>
    /// <item>the Knight stands at <see cref="PlayerStart"/> (south part of the ring) facing north, where the boss rises;</item>
    /// <item>the arena answers walkability (<see cref="IsBlocked"/>: the ring and the low pillars), so enemy spawns and wall
    /// slides stay inside the ring;</item>
    /// <item>the camera look point gets a soft circular clamp (<see cref="TopDownFollowCamera.SetBounds"/>): the ring and the
    /// boss stay on screen, little of the outside shows;</item>
    /// <item>the gold arrow points to the portal in the north wall once it is open; the portal opens when the boss dies
    /// (<see cref="SurvivalTimer.Complete"/>, the usual <see cref="PortalGate"/>);</item>
    /// <item>ambient and sun dim to <see cref="ambientScale"/> / <see cref="sunScale"/>, so the brazier ring is the key light.</item>
    /// </list>
    /// Collision for the Knight comes from the ring and pillar box colliders of the arena object; the walkability shapes
    /// match them. No allocation per frame: the arena has no Update.
    /// </summary>
    public sealed class BossArena : MonoBehaviour, IWalkability
    {
        /// <summary>Low pillar (partial cover), ground circle in arena-local metres.</summary>
        [Serializable]
        public struct Pillar
        {
            public Vector2 centre; // local x, z
            [Min(0.1f)] public float radius;
        }

        [Header("Shape (written by the builder)")]
        [Tooltip("Walkable radius: the inner face of the ring wall (m).")]
        [SerializeField, Min(4f)] private float radius = 16f;
        [SerializeField] private Pillar[] pillars = Array.Empty<Pillar>();
        [Tooltip("Local point where the Knight starts (south part of the ring).")]
        [SerializeField] private Vector3 playerStart = new Vector3(0f, 0f, -10f);
        [SerializeField] private PortalGate portal;

        [Header("Camera")]
        [Tooltip("Look point stays this far (m) inside the walkable radius.")]
        [SerializeField, Min(0f)] private float cameraInset = 3f;
        [Tooltip("Width (m) of the soft band where the look point slows down before the limit.")]
        [SerializeField, Min(0.1f)] private float cameraSoftBand = 4f;

        [Header("Mood")]
        [SerializeField, Range(0f, 1f)] private float ambientScale = 0.8f;
        [SerializeField, Range(0f, 1f)] private float sunScale = 0.7f;

        [Header("Scene")]
        [SerializeField] private WorldStreamer world;
        [SerializeField] private EnemySpawner spawner;
        [SerializeField] private Transform player;
        [SerializeField] private TopDownFollowCamera followCamera;
        [SerializeField] private ObjectiveArrow arrow;
        [Tooltip("Key light to dim. Empty = RenderSettings.sun.")]
        [SerializeField] private Light sun;

        private bool _dimmed;
        private Color _ambientSky, _ambientEquator, _ambientGround;
        private Light _sun;
        private float _sunIntensity;

        /// <summary>The arena in use this Chamber (null in normal Chambers).</summary>
        public static BossArena Active { get; private set; }

        /// <summary>The arena was entered (Knight placed, world off).</summary>
        public static event Action<BossArena> Entered;

        public Vector3 Centre => transform.position;
        public float Radius => radius;
        public int PillarCount => pillars.Length;
        public PortalGate Portal => portal;
        public Vector3 PlayerStart => transform.TransformPoint(playerStart);
        /// <summary>Where the Knight leaves: the portal trigger, or the north edge without a portal.</summary>
        public Vector3 ExitPosition => portal != null ? portal.TriggerPosition : Centre + Vector3.forward * radius;
        /// <summary>Look point limit of the camera (radius from <see cref="Centre"/>).</summary>
        public float CameraRadius => Mathf.Max(0.5f, radius - cameraInset);

        /// <summary>Tests: wiring without the scene builder.</summary>
        public void Setup(float walkRadius, Pillar[] lowPillars, Vector3 localStart, PortalGate exit,
            WorldStreamer worldStreamer, EnemySpawner enemySpawner, Transform knight, TopDownFollowCamera camera, ObjectiveArrow objective)
        {
            radius = walkRadius;
            pillars = lowPillars ?? Array.Empty<Pillar>();
            playerStart = localStart;
            portal = exit;
            world = worldStreamer;
            spawner = enemySpawner;
            player = knight;
            followCamera = camera;
            arrow = objective;
        }

        /// <summary>Boss Chamber: shows the arena, turns the streamed world off, places the Knight, sets walkability, camera and arrow.</summary>
        public void Enter()
        {
            gameObject.SetActive(true);
            Active = this;
            if (world != null) world.enabled = false; // before its first Update: no tile streams in
            if (spawner != null) spawner.UseWalkability(this);
            Enemy.Walkability = this; // enemies already awake (the boss body) steer by the ring too
            PlaceKnight();
            if (followCamera != null) followCamera.SetBounds(Centre, CameraRadius, cameraSoftBand);
            if (arrow != null) arrow.PointAt(portal);
            Dim();
            Entered?.Invoke(this);
        }

        /// <summary>Normal Chamber: the arena stays hidden.</summary>
        public void Leave()
        {
            Restore();
            if (Active == this) Active = null;
            gameObject.SetActive(false);
        }

        private void PlaceKnight()
        {
            if (player == null) return;
            var cc = player.GetComponent<CharacterController>();
            bool had = cc != null && cc.enabled;
            if (had) cc.enabled = false; // else the controller snaps the Knight back
            player.SetPositionAndRotation(PlayerStart, Quaternion.LookRotation(transform.forward, Vector3.up));
            if (had) cc.enabled = true;
        }

        /// <summary>
        /// KAN-119: obstacles that come and go during the fight (boss boulder rubble, set by BossBoulders while enabled). The
        /// arena's walkability asks them too, so enemies, boss bolts and charges treat rubble like a pillar.
        /// </summary>
        public static IWalkability Obstacles;

        /// <inheritdoc />
        public bool IsBlocked(Vector3 position, float bodyRadius)
        {
            var obstacles = Obstacles;
            if (obstacles != null && obstacles.IsBlocked(position, bodyRadius)) return true;
            Vector3 c = transform.position;
            float dx = position.x - c.x, dz = position.z - c.z;
            float reach = radius - bodyRadius;
            if (reach <= 0f || dx * dx + dz * dz > reach * reach) return true;
            for (int i = 0; i < pillars.Length; i++)
            {
                var p = pillars[i];
                float px = dx - p.centre.x, pz = dz - p.centre.y, r = p.radius + bodyRadius;
                if (px * px + pz * pz < r * r) return true;
            }
            return false;
        }

        private void Dim()
        {
            if (_dimmed) return;
            _dimmed = true;
            _ambientSky = RenderSettings.ambientSkyColor;
            _ambientEquator = RenderSettings.ambientEquatorColor;
            _ambientGround = RenderSettings.ambientGroundColor;
            RenderSettings.ambientSkyColor = _ambientSky * ambientScale;
            RenderSettings.ambientEquatorColor = _ambientEquator * ambientScale;
            RenderSettings.ambientGroundColor = _ambientGround * ambientScale;
            _sun = sun != null ? sun : RenderSettings.sun;
            if (_sun != null)
            {
                _sunIntensity = _sun.intensity;
                _sun.intensity = _sunIntensity * sunScale;
            }
        }

        private void Restore()
        {
            if (!_dimmed) return;
            _dimmed = false;
            RenderSettings.ambientSkyColor = _ambientSky;
            RenderSettings.ambientEquatorColor = _ambientEquator;
            RenderSettings.ambientGroundColor = _ambientGround;
            if (_sun != null) _sun.intensity = _sunIntensity;
        }

        private void OnDestroy()
        {
            Restore();
            if (Active == this) Active = null;
            if (ReferenceEquals(Enemy.Walkability, this)) Enemy.Walkability = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Active = null;
            Entered = null;
            Obstacles = null;
        }
    }
}
