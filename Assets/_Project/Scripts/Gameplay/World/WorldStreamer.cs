using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorSouls.Gameplay.World
{
    /// <summary>
    /// Open world made of square tiles that appear around the player and are recycled behind them.
    /// Each run picks a new seed, so the layout (tile variants, rotations, exit door spot) changes.
    /// Coastal layout (Realm 1): playable cells are x in [-halfTiles, halfTiles], z in [-southRows, shoreRow].
    /// Row <see cref="shoreRow"/> holds shore tiles (beach edge, foam, colliders against the sea); rows north of it
    /// are open sea (the sea mesh is a scene object, not a tile). One ring of "edge" tiles (cliffs) closes the
    /// other three sides; "edge shore" tiles (headlands) close the shore row ends. Invisible walls at the bounds.
    /// Tiles are prefabs made by the editor WorldBuilder; this class only places and pools them (no runtime
    /// mesh generation, so it stays cheap on phones).
    /// </summary>
    public sealed class WorldStreamer : MonoBehaviour
    {
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
        private int _seed;
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

        private void Awake()
        {
            _seed = fixedSeed != 0 ? fixedSeed : Environment.TickCount;
            foreach (var v in variants) _totalWeight += v.weight;
            foreach (var v in shoreVariants) _shoreWeight += v.weight;
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
            GameObject prefab;
            int turns = 0;
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

            var go = Take(prefab);
            go.transform.SetPositionAndRotation(CellCentre(cell), Quaternion.Euler(0f, turns * 90f, 0f));
            go.SetActive(true);
            return go;
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
