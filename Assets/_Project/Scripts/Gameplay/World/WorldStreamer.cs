using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorSouls.Gameplay.World
{
    /// <summary>
    /// Open world made of square tiles that appear around the player and are recycled behind them.
    /// The seed decides the layout (tile variants, rotations, exit door spot). <see cref="UseSeed"/> sets it per Chamber
    /// (KAN-46, from the run seed + Realm + Chamber); without it the world picks a random seed.
    /// Coastal layout (Realm 1): playable cells are x in [-halfTiles, halfTiles], z in [-southRows, shoreRow].
    /// Row <see cref="shoreRow"/> holds shore tiles (beach edge, foam, colliders against the sea); rows north of it
    /// are open sea (the sea mesh is a scene object, not a tile). One ring of "edge" tiles (cliffs) closes the
    /// other three sides; "edge shore" tiles (headlands) close the shore row ends. Invisible walls at the bounds.
    /// Tiles are prefabs made by the editor WorldBuilder; this class only places and pools them (no runtime
    /// mesh generation, so it stays cheap on phones).
    /// Walkability (<see cref="IsBlocked"/>) is answered from the tile layout itself: every cell's prefab and rotation
    /// are a pure function of the seed, and each prefab's box colliders are cached once, so loaded and not-yet-loaded
    /// tiles answer the same way with no physics query and no allocation.
    /// </summary>
    public sealed class WorldStreamer : MonoBehaviour, IWalkability
    {
        /// <summary>Obstacle footprint in tile-local space (a box collider seen from above).</summary>
        private struct Block
        {
            public Vector2 Centre;   // x, z
            public Vector2 Half;     // half extents along the box's own x, z
            public float Cos, Sin;   // box yaw
        }

        // Only boxes that reach into this height band block walking (skips high arches, decals).
        private const float BlockMinY = 0.05f;
        private const float BlockMaxY = 2f;

        [Serializable]
        public struct Variant
        {
            public GameObject prefab;
            [Min(0f)] public float weight;
            public bool rotate; // random 0/90/180/270 degrees
        }

        [SerializeField] private Transform player;
        [SerializeField, Min(4f)] private float tileSize = 12f;
        [SerializeField, Min(1)] private int halfTiles = 5;
        [Tooltip("Tile row (z) of the shore. Rows above it are sea.")]
        [SerializeField, Min(1)] private int shoreRow = 1;
        [Tooltip("Playable rows south of the spawn row.")]
        [SerializeField, Min(1)] private int southRows = 4;
        [Tooltip("Shore line (water edge) Z inside a shore tile, local metres. Shore tiles keep land up to about here.")]
        [SerializeField] private float shoreLineLocalZ = 1f;
        [SerializeField] private Variant[] variants;
        [SerializeField] private Variant[] shoreVariants; // row shoreRow; never rotated
        [SerializeField] private GameObject spawnTile;  // centre tile (0, 0)
        [SerializeField] private GameObject exitTile;   // holds the exit door
        [SerializeField] private GameObject edgeTile;   // ring outside the bounds (cliffs)
        [SerializeField] private GameObject edgeShoreTile; // shore row, outside the bounds (headland)
        [Tooltip("Tiles kept around the player: x = sideways, y = behind, z = ahead (+Z, up the screen).")]
        [SerializeField] private Vector3Int keep = new Vector3Int(1, 1, 2);
        [Tooltip("Exit door tile distance from spawn, in tiles (Chebyshev).")]
        [SerializeField] private Vector2Int exitDistance = new Vector2Int(3, 4);
        [Tooltip("0 = random seed every run.")]
        [SerializeField] private int fixedSeed;

        private readonly Dictionary<Vector2Int, GameObject> _active = new Dictionary<Vector2Int, GameObject>();
        private readonly Dictionary<GameObject, Stack<GameObject>> _pools = new Dictionary<GameObject, Stack<GameObject>>();
        private readonly Dictionary<GameObject, GameObject> _prefabOf = new Dictionary<GameObject, GameObject>();
        private readonly List<Vector2Int> _release = new List<Vector2Int>();
        private readonly Dictionary<GameObject, Block[]> _blocks = new Dictionary<GameObject, Block[]>();
        private int _seed;
        private bool _seeded;
        private float _totalWeight, _shoreWeight;
        private Vector2Int _lastCell = new Vector2Int(int.MinValue, int.MinValue);

        public Vector2Int ExitCell { get; private set; }
        /// <summary>World position of the exit door (tile centre).</summary>
        public Vector3 ExitPosition => CellCentre(ExitCell);
        public float WorldHalfSize => (halfTiles + 0.5f) * tileSize;
        /// <summary>Playable area corners (XZ, metres). Max Z stops well before the water.</summary>
        public Vector2 PlayableMin => new Vector2(-WorldHalfSize, -(southRows + 0.5f) * tileSize);
        public Vector2 PlayableMax => new Vector2(WorldHalfSize, ShoreLineZ - 1.5f);
        /// <summary>Average water edge Z (world). The real edge wiggles about a metre around it.</summary>
        public float ShoreLineZ => shoreRow * tileSize + shoreLineLocalZ;
        public event Action<Vector3> ExitPlaced;

        /// <summary>Seed of the current layout.</summary>
        public int Seed => _seed;
        /// <summary>A non-zero fixed seed (tests, debug) wins over <see cref="UseSeed"/>.</summary>
        public int FixedSeed => fixedSeed;

        private void Awake()
        {
            if (variants != null) foreach (var v in variants) _totalWeight += v.weight;
            if (shoreVariants != null) foreach (var v in shoreVariants) _shoreWeight += v.weight;
            if (fixedSeed != 0) ApplySeed(fixedSeed);
            else if (!_seeded) ApplySeed(Environment.TickCount);
        }

        /// <summary>
        /// Layout of this Chamber (KAN-46). Called by <c>ChamberSetup</c> before the first tiles appear
        /// (it runs first); a later call rebuilds the loaded tiles. Ignored when <see cref="FixedSeed"/> is set.
        /// </summary>
        public void UseSeed(int seed)
        {
            if (fixedSeed != 0) return;
            ApplySeed(seed != 0 ? seed : 1);
        }

        private void ApplySeed(int seed)
        {
            _seed = seed;
            _seeded = true;
            foreach (var kv in _active) Release(kv.Value); // layout changed: loaded tiles are stale
            _active.Clear();
            _lastCell = new Vector2Int(int.MinValue, int.MinValue);
            ExitCell = PickExitCell();
            ExitPlaced?.Invoke(ExitPosition);
        }

        private void Update()
        {
            if (player == null) return;
            var cell = CellOf(player.position);
            if (cell == _lastCell) return;
            _lastCell = cell;
            Refresh(cell);
        }

        private void Refresh(Vector2Int centre)
        {
            _release.Clear();
            foreach (var kv in _active)
                if (!IsKept(kv.Key, centre)) _release.Add(kv.Key);
            foreach (var cell in _release)
            {
                Release(_active[cell]);
                _active.Remove(cell);
            }

            for (int z = centre.y - keep.y; z <= centre.y + keep.z; z++)
            for (int x = centre.x - keep.x; x <= centre.x + keep.x; x++)
            {
                var cell = new Vector2Int(x, z);
                if (_active.ContainsKey(cell) || !HasTile(cell)) continue;
                _active[cell] = Spawn(cell);
            }
        }

        private bool IsKept(Vector2Int cell, Vector2Int centre) =>
            cell.x >= centre.x - keep.x && cell.x <= centre.x + keep.x && cell.y >= centre.y - keep.y && cell.y <= centre.y + keep.z;

        private GameObject Spawn(Vector2Int cell)
        {
            ResolveTile(cell, out var prefab, out int turns);
            var go = Take(prefab);
            go.transform.SetPositionAndRotation(CellCentre(cell), Quaternion.Euler(0f, turns * 90f, 0f));
            go.SetActive(true);
            return go;
        }

        /// <summary>Which prefab sits in <paramref name="cell"/> and how many quarter turns it has. Pure function of the seed.</summary>
        private void ResolveTile(Vector2Int cell, out GameObject prefab, out int turns)
        {
            turns = 0;
            if (!HasTile(cell))
            {
                prefab = null; // open sea or beyond the cliffs
                return;
            }
            bool inside = Mathf.Abs(cell.x) <= halfTiles && cell.y >= -southRows;
            if (cell.y == shoreRow) prefab = inside ? PickVariant(cell, shoreVariants, _shoreWeight).prefab : edgeShoreTile;
            else if (!inside)
            {
                prefab = edgeTile;
                turns = Hash(cell, 7) % 4;
            }
            else if (cell == Vector2Int.zero) prefab = spawnTile;
            else if (cell == ExitCell) prefab = exitTile;
            else
            {
                var v = PickVariant(cell, variants, _totalWeight);
                prefab = v.prefab;
                if (v.rotate) turns = Hash(cell, 3) % 4;
            }
        }

        /// <inheritdoc />
        public bool IsBlocked(Vector3 position, float radius)
        {
            Vector2 min = PlayableMin, max = PlayableMax;
            if (position.x - radius < min.x || position.x + radius > max.x ||
                position.z - radius < min.y || position.z + radius > max.y) return true;

            // The circle can overlap up to four cells near a tile edge.
            var lo = CellOf(position - new Vector3(radius, 0f, radius));
            var hi = CellOf(position + new Vector3(radius, 0f, radius));
            for (int z = lo.y; z <= hi.y; z++)
            for (int x = lo.x; x <= hi.x; x++)
            {
                var cell = new Vector2Int(x, z);
                ResolveTile(cell, out var prefab, out int turns);
                if (prefab == null) continue;
                var blocks = BlocksOf(prefab);
                if (blocks.Length == 0) continue;

                // World -> tile local: undo the tile's quarter turns (Unity yaw is clockwise seen from above).
                Vector3 d = position - CellCentre(cell);
                float a = turns * 90f * Mathf.Deg2Rad;
                float c = Mathf.Cos(a), s = Mathf.Sin(a);
                var local = new Vector2(c * d.x - s * d.z, s * d.x + c * d.z);
                foreach (var b in blocks)
                {
                    Vector2 r = local - b.Centre;
                    float bx = b.Cos * r.x - b.Sin * r.y;
                    float bz = b.Sin * r.x + b.Cos * r.y;
                    if (Mathf.Abs(bx) <= b.Half.x + radius && Mathf.Abs(bz) <= b.Half.y + radius) return true;
                }
            }
            return false;
        }

        /// <summary>Box colliders of a tile prefab, flattened to tile-local ground rectangles. Read once per prefab.</summary>
        private Block[] BlocksOf(GameObject prefab)
        {
            if (_blocks.TryGetValue(prefab, out var cached)) return cached;
            var list = new List<Block>();
            var root = prefab.transform.worldToLocalMatrix;
            foreach (var box in prefab.GetComponentsInChildren<BoxCollider>(true))
            {
                if (box.isTrigger) continue;
                Matrix4x4 m = root * box.transform.localToWorldMatrix;
                Vector3 centre = m.MultiplyPoint3x4(box.center);
                var scale = new Vector3(m.GetColumn(0).magnitude, m.GetColumn(1).magnitude, m.GetColumn(2).magnitude);
                Vector3 half = Vector3.Scale(box.size, scale) * 0.5f;
                if (centre.y + half.y < BlockMinY || centre.y - half.y > BlockMaxY) continue;
                float yaw = m.rotation.eulerAngles.y * Mathf.Deg2Rad;
                list.Add(new Block
                {
                    Centre = new Vector2(centre.x, centre.z),
                    Half = new Vector2(half.x, half.z),
                    Cos = Mathf.Cos(yaw),
                    Sin = Mathf.Sin(yaw),
                });
            }
            var blocks = list.ToArray();
            _blocks[prefab] = blocks;
            return blocks;
        }

        private Variant PickVariant(Vector2Int cell, Variant[] list, float total)
        {
            float r = (Hash(cell, 1) % 10000) / 10000f * total;
            foreach (var v in list)
            {
                if (r < v.weight) return v;
                r -= v.weight;
            }
            return list[list.Length - 1];
        }

        /// <summary>Random inland cell (not on the shore row) at Chebyshev distance exitDistance from the spawn.</summary>
        private Vector2Int PickExitCell()
        {
            var rng = new System.Random(_seed);
            var cells = new List<Vector2Int>();
            for (int z = -southRows; z < shoreRow; z++)
            for (int x = -halfTiles; x <= halfTiles; x++)
            {
                int d = Mathf.Max(Mathf.Abs(x), Mathf.Abs(z));
                if (d >= exitDistance.x && d <= exitDistance.y) cells.Add(new Vector2Int(x, z));
            }
            if (cells.Count == 0) return new Vector2Int(halfTiles, 0);
            return cells[rng.Next(cells.Count)];
        }

        /// <summary>Playable cells, the cliff ring around them and the shore row ends. Nothing north of the shore (sea).</summary>
        private bool HasTile(Vector2Int c) =>
            Mathf.Abs(c.x) <= halfTiles + 1 && c.y >= -southRows - 1 && c.y <= shoreRow;

        private GameObject Take(GameObject prefab)
        {
            if (_pools.TryGetValue(prefab, out var pool) && pool.Count > 0) return pool.Pop();
            var go = Instantiate(prefab, transform);
            _prefabOf[go] = prefab;
            return go;
        }

        private void Release(GameObject go)
        {
            go.SetActive(false);
            var prefab = _prefabOf[go];
            if (!_pools.TryGetValue(prefab, out var pool)) _pools[prefab] = pool = new Stack<GameObject>();
            pool.Push(go);
        }

        private Vector2Int CellOf(Vector3 p) =>
            new Vector2Int(Mathf.FloorToInt(p.x / tileSize + 0.5f), Mathf.FloorToInt(p.z / tileSize + 0.5f));

        private Vector3 CellCentre(Vector2Int c) => new Vector3(c.x * tileSize, 0f, c.y * tileSize);

        private int Hash(Vector2Int c, int salt)
        {
            unchecked
            {
                uint h = (uint)(c.x * 73856093 ^ c.y * 19349663 ^ _seed * 83492791 ^ salt * 2654435761u);
                h ^= h >> 13;
                h *= 0x5bd1e995;
                h ^= h >> 15;
                return (int)(h & 0x7fffffff);
            }
        }
    }
}
