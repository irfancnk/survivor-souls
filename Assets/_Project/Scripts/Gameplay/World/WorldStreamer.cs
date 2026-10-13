using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorSouls.Gameplay.World
{
    /// <summary>
    /// Open world made of square tiles that appear around the player and are recycled behind them.
    /// Each run picks a new seed, so the layout (tile variants, rotations, exit door spot) changes.
    /// The playable world is (2 * halfTiles + 1)^2 tiles; one extra ring outside uses the "edge" tile
    /// (dense forest) so the bounds look natural. Invisible walls at the bounds stop the player.
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
        [SerializeField] private Variant[] variants;
        [SerializeField] private GameObject spawnTile;  // centre tile (0, 0)
        [SerializeField] private GameObject exitTile;   // holds the exit door
        [SerializeField] private GameObject edgeTile;   // ring outside the bounds
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
        private float _totalWeight;
        private Vector2Int _lastCell = new Vector2Int(int.MinValue, int.MinValue);

        public Vector2Int ExitCell { get; private set; }
        /// <summary>World position of the exit door (tile centre).</summary>
        public Vector3 ExitPosition => CellCentre(ExitCell);
        public float WorldHalfSize => (halfTiles + 0.5f) * tileSize;
        public event Action<Vector3> ExitPlaced;

        private void Awake()
        {
            _seed = fixedSeed != 0 ? fixedSeed : Environment.TickCount;
            foreach (var v in variants) _totalWeight += v.weight;
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
                if (_active.ContainsKey(cell) || !InWorld(cell, halfTiles + 1)) continue;
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
            if (!InWorld(cell, halfTiles))
            {
                prefab = edgeTile;
                turns = Hash(cell, 7) % 4;
            }
            else if (cell == Vector2Int.zero) prefab = spawnTile;
            else if (cell == ExitCell) prefab = exitTile;
            else
            {
                var v = PickVariant(cell);
                prefab = v.prefab;
                if (v.rotate) turns = Hash(cell, 3) % 4;
            }
        }

        /// <inheritdoc />
        public bool IsBlocked(Vector3 position, float radius)
        {
            float limit = WorldHalfSize - radius;
            if (Mathf.Abs(position.x) > limit || Mathf.Abs(position.z) > limit) return true;

            // The circle can overlap up to four cells near a tile edge.
            var min = CellOf(position - new Vector3(radius, 0f, radius));
            var max = CellOf(position + new Vector3(radius, 0f, radius));
            for (int z = min.y; z <= max.y; z++)
            for (int x = min.x; x <= max.x; x++)
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

        private Variant PickVariant(Vector2Int cell)
        {
            float r = (Hash(cell, 1) % 10000) / 10000f * _totalWeight;
            foreach (var v in variants)
            {
                if (r < v.weight) return v;
                r -= v.weight;
            }
            return variants[variants.Length - 1];
        }

        private Vector2Int PickExitCell()
        {
            var rng = new System.Random(_seed);
            int d = rng.Next(exitDistance.x, exitDistance.y + 1);
            d = Mathf.Min(d, halfTiles);
            // A random cell on the square ring at distance d, biased to the upper half (ahead of the camera).
            int along = rng.Next(-d, d + 1);
            switch (rng.Next(4))
            {
                case 0: return new Vector2Int(along, d);
                case 1: return new Vector2Int(d, Mathf.Abs(along));
                case 2: return new Vector2Int(-d, Mathf.Abs(along));
                default: return new Vector2Int(along, d);
            }
        }

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

        private static bool InWorld(Vector2Int c, int half) => Mathf.Abs(c.x) <= half && Mathf.Abs(c.y) <= half;

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
