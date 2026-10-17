using System;
using System.Collections.Generic;
using SurvivorSouls.Gameplay.CameraRig;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using UnityEngine;

namespace SurvivorSouls.Gameplay.World
{
    /// <summary>
    /// Island Chamber (KAN-245 / KAN-247): a walled 36 x 60 m room on an island in the sea, entered from a bridge in the
    /// south-west and left through a gate in the north-east corner that faces north-east. One scene object per theme, made
    /// by the editor IslandChamberBuilder, inactive by default and placed far south of the streamed world.
    /// <see cref="ChamberSetup"/> calls <see cref="Enter"/> when the Chamber's theme (ChamberThemeSchedule) names this
    /// island, before any other Awake / Start:
    /// <list type="bullet">
    /// <item>the <see cref="WorldStreamer"/> is switched off before its first Update and the world's snow ground, sea,
    /// landmarks and weather are hidden (<see cref="hideOnEnter"/>): the island brings its own floor, sea and floor collider;</item>
    /// <item>the Knight stands at the SW start facing the NE gate;</item>
    /// <item>the island answers walkability (<see cref="IsBlocked"/>): the room polygon with its two chamfered corners,
    /// ponds and pools (ellipse or rectangle), wall runs (capsules) and round cover pieces;</item>
    /// <item>the camera look point is clamped to a rectangle over the room (<see cref="TopDownFollowCamera.SetRectBounds"/>);</item>
    /// <item>the gold arrow follows the NE gate portal (dim while locked, bright once open), like the shore exit;</item>
    /// <item>the theme light is applied: ambient trilight, fog, sun colour, intensity and direction.</item>
    /// </list>
    /// Collision for the Knight comes from the island's box colliders; the walkability shapes match them. No Update, no
    /// allocation per frame.
    /// </summary>
    public sealed class IslandChamber : MonoBehaviour, IWalkability
    {
        /// <summary>Pond, pool or channel: ellipse or rectangle in island-local metres (x, z), turned by <c>yaw</c> degrees.</summary>
        [Serializable]
        public struct Pond
        {
            public Vector2 centre;
            public Vector2 radii;  // ellipse radii or rectangle half sizes
            public float yaw;      // degrees, clockwise seen from above (Unity yaw)
            public bool rect;
        }

        /// <summary>Wall run (low ruined wall, hedge, fence ...): a capsule from <c>a</c> to <c>b</c> with half thickness <c>radius</c>.</summary>
        [Serializable]
        public struct WallRun
        {
            public Vector2 a, b;
            [Min(0.05f)] public float radius;
        }

        /// <summary>Round cover piece (broken column, brazier, statue) on the ground.</summary>
        [Serializable]
        public struct Round
        {
            public Vector2 centre;
            [Min(0.05f)] public float radius;
        }

        [Header("Theme")]
        [SerializeField] private string themeId = "drowned_cathedral";
        [SerializeField] private string displayName = "Drowned Cathedral";

        [Header("Shape (written by the builder)")]
        [Tooltip("Half size of the room interior (m): x = east-west, y = north-south.")]
        [SerializeField] private Vector2 halfSize = new Vector2(18f, 30f);
        [Tooltip("Chamfer of the NE and SW corners (m along each axis).")]
        [SerializeField, Min(0f)] private float chamfer = 7.5f;
        [SerializeField] private Pond[] ponds = Array.Empty<Pond>();
        [SerializeField] private WallRun[] walls = Array.Empty<WallRun>();
        [SerializeField] private Round[] rounds = Array.Empty<Round>();
        [Tooltip("Local point where the Knight starts (just inside the SW entry).")]
        [SerializeField] private Vector3 playerStart = new Vector3(-11f, 0f, -21.5f);
        [SerializeField] private PortalGate portal;

        [Header("Camera")]
        [Tooltip("Look point stays this far (m) inside the side walls.")]
        [SerializeField] private float cameraInsetX = 4.5f;
        [Tooltip("Look point range along Z (local m): x = south limit, y = north limit.")]
        [SerializeField] private Vector2 cameraZ = new Vector2(-24f, 26f);

        [Header("Light")]
        [SerializeField] private Color ambientSky = new Color(0.36f, 0.34f, 0.44f);
        [SerializeField] private Color ambientEquator = new Color(0.44f, 0.34f, 0.32f);
        [SerializeField] private Color ambientGround = new Color(0.16f, 0.13f, 0.12f);
        [SerializeField] private Color fogColor = new Color(0.42f, 0.34f, 0.34f);
        [SerializeField] private float fogStart = 42f;
        [SerializeField] private float fogEnd = 100f;
        [SerializeField] private Color sunColor = new Color(1f, 0.66f, 0.44f);
        [SerializeField, Min(0f)] private float sunIntensity = 1.15f;
        [Tooltip("Sun rotation (Euler degrees): low dusk sun from the NNE, behind the gate.")]
        [SerializeField] private Vector3 sunEuler = new Vector3(18f, 200f, 0f);

        [Header("Scene")]
        [SerializeField] private WorldStreamer world;
        [SerializeField] private EnemySpawner spawner;
        [SerializeField] private Transform player;
        [SerializeField] private TopDownFollowCamera followCamera;
        [SerializeField] private ObjectiveArrow arrow;
        [Tooltip("Key light to recolour. Empty = RenderSettings.sun.")]
        [SerializeField] private Light sun;
        [Tooltip("World objects hidden in this Chamber (snow ground, sea, landmarks, weather, footprints).")]
        [SerializeField] private GameObject[] hideOnEnter = Array.Empty<GameObject>();

        private bool _entered;

        /// <summary>The island in use this Chamber (null elsewhere).</summary>
        public static IslandChamber Active { get; private set; }

        /// <summary>An island was entered (Knight placed, world off).</summary>
        public static event Action<IslandChamber> Entered;

        public string ThemeId => themeId;
        public string DisplayName => displayName;
        public Vector3 Centre => transform.position;
        public Vector2 HalfSize => halfSize;
        public float Chamfer => chamfer;
        public PortalGate Portal => portal;
        public int PondCount => ponds.Length;
        public int WallCount => walls.Length;
        public IReadOnlyList<Pond> Ponds => ponds;
        public IReadOnlyList<WallRun> Walls => walls;
        public IReadOnlyList<Round> Rounds => rounds;
        public Vector3 PlayerStart => transform.TransformPoint(playerStart);
        /// <summary>Where the Knight leaves: the gate portal trigger, or the NE chamfer middle without a portal.</summary>
        public Vector3 ExitPosition => portal != null ? portal.TriggerPosition : transform.TransformPoint(new Vector3(halfSize.x - chamfer * 0.5f, 0f, halfSize.y - chamfer * 0.5f));
        /// <summary>Look point rectangle of the camera (world XZ).</summary>
        public Vector2 CameraMin => new Vector2(Centre.x - halfSize.x + cameraInsetX, Centre.z + cameraZ.x);
        public Vector2 CameraMax => new Vector2(Centre.x + halfSize.x - cameraInsetX, Centre.z + cameraZ.y);

        /// <summary>Tests: wiring without the scene builder.</summary>
        public void Setup(string id, Vector2 roomHalfSize, float cornerChamfer, Pond[] pondShapes, WallRun[] wallRuns, Round[] roundPieces,
            Vector3 localStart, PortalGate exit, WorldStreamer worldStreamer, EnemySpawner enemySpawner, Transform knight,
            TopDownFollowCamera camera, ObjectiveArrow objective, GameObject[] hide = null)
        {
            themeId = id;
            halfSize = roomHalfSize;
            chamfer = cornerChamfer;
            ponds = pondShapes ?? Array.Empty<Pond>();
            walls = wallRuns ?? Array.Empty<WallRun>();
            rounds = roundPieces ?? Array.Empty<Round>();
            playerStart = localStart;
            portal = exit;
            world = worldStreamer;
            spawner = enemySpawner;
            player = knight;
            followCamera = camera;
            arrow = objective;
            hideOnEnter = hide ?? Array.Empty<GameObject>();
        }

        /// <summary>Island Chamber: shows the island, turns the streamed world off, places the Knight, sets walkability, camera, arrow and light.</summary>
        public void Enter()
        {
            gameObject.SetActive(true);
            Active = this;
            _entered = true;
            if (world != null) world.enabled = false; // before its first Update: no tile streams in
            foreach (var go in hideOnEnter)
                if (go != null) go.SetActive(false);
            if (spawner != null) spawner.UseWalkability(this);
            Enemy.Walkability = this;
            PlaceKnight();
            if (followCamera != null) followCamera.SetRectBounds(CameraMin, CameraMax);
            if (arrow != null) arrow.Follow(portal);
            ApplyLight();
            Entered?.Invoke(this);
        }

        /// <summary>Another Chamber: the island stays hidden.</summary>
        public void Leave()
        {
            if (Active == this) Active = null;
            if (_entered)
            {
                _entered = false;
                foreach (var go in hideOnEnter)
                    if (go != null) go.SetActive(true);
                if (world != null) world.enabled = true;
            }
            gameObject.SetActive(false);
        }

        private void PlaceKnight()
        {
            if (player == null) return;
            var cc = player.GetComponent<CharacterController>();
            bool had = cc != null && cc.enabled;
            if (had) cc.enabled = false; // else the controller snaps the Knight back
            Vector3 toGate = ExitPosition - PlayerStart;
            toGate.y = 0f;
            var facing = toGate.sqrMagnitude > 0.01f ? Quaternion.LookRotation(toGate.normalized, Vector3.up) : transform.rotation;
            player.SetPositionAndRotation(PlayerStart, facing);
            if (had) cc.enabled = true;
        }

        private void ApplyLight()
        {
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = ambientEquator;
            RenderSettings.ambientGroundColor = ambientGround;
            RenderSettings.fogColor = fogColor;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
            var key = sun != null ? sun : RenderSettings.sun;
            if (key != null)
            {
                key.color = sunColor;
                key.intensity = sunIntensity;
                key.transform.rotation = Quaternion.Euler(sunEuler);
            }
            if (followCamera != null && followCamera.TryGetComponent<Camera>(out var cam)) cam.backgroundColor = fogColor;
        }

        // ------------------------------------------------------------ Walkability

        /// <inheritdoc />
        public bool IsBlocked(Vector3 position, float bodyRadius)
        {
            Vector3 c = transform.position;
            float x = position.x - c.x, z = position.z - c.z;
            if (!InsideRoom(x, z, bodyRadius)) return true;
            for (int i = 0; i < ponds.Length; i++)
                if (InPond(ponds[i], x, z, bodyRadius)) return true;
            for (int i = 0; i < walls.Length; i++)
            {
                var w = walls[i];
                if (SegmentDistanceSq(new Vector2(x, z), w.a, w.b) < (w.radius + bodyRadius) * (w.radius + bodyRadius)) return true;
            }
            for (int i = 0; i < rounds.Length; i++)
            {
                var r = rounds[i];
                float dx = x - r.centre.x, dz = z - r.centre.y, rr = r.radius + bodyRadius;
                if (dx * dx + dz * dz < rr * rr) return true;
            }
            return false;
        }

        /// <summary>Room polygon test in local metres with a margin: the rectangle minus the NE and SW chamfer triangles.</summary>
        public bool InsideRoom(float x, float z, float margin)
        {
            if (Mathf.Abs(x) > halfSize.x - margin || Mathf.Abs(z) > halfSize.y - margin) return false;
            float diag = halfSize.x + halfSize.y - chamfer;
            float m = margin * 1.41421356f;
            if (x + z > diag - m) return false;  // NE chamfer
            if (x + z < -diag + m) return false; // SW chamfer
            return true;
        }

        /// <summary>Pond test in local metres, grown by <paramref name="margin"/>.</summary>
        public static bool InPond(Pond p, float x, float z, float margin)
        {
            float a = p.yaw * Mathf.Deg2Rad;
            float dx = x - p.centre.x, dz = z - p.centre.y;
            // Unity yaw turns clockwise seen from above: local = R(-yaw) * d
            float c = Mathf.Cos(a), s = Mathf.Sin(a);
            float u = c * dx - s * dz;
            float v = s * dx + c * dz;
            if (p.rect) return Mathf.Abs(u) <= p.radii.x + margin && Mathf.Abs(v) <= p.radii.y + margin;
            float rx = p.radii.x + margin, rz = p.radii.y + margin;
            return (u * u) / (rx * rx) + (v * v) / (rz * rz) <= 1f;
        }

        private static float SegmentDistanceSq(Vector2 p, Vector2 a, Vector2 b)
        {
            Vector2 d = b - a;
            float len = d.sqrMagnitude;
            float t = len > 1e-6f ? Mathf.Clamp01(Vector2.Dot(p - a, d) / len) : 0f;
            return (a + d * t - p).sqrMagnitude;
        }

        private void OnDestroy()
        {
            if (Active == this) Active = null;
            if (ReferenceEquals(Enemy.Walkability, this)) Enemy.Walkability = null;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Active = null;
            Entered = null;
        }
    }
}
