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
            GameObject prefab;
            int turns = 0;
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

            var go = Take(prefab);
            go.transform.SetPositionAndRotation(CellCentre(cell), Quaternion.Euler(0f, turns * 90f, 0f));
            go.SetActive(true);
            return go;
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
