using System.Collections.Generic;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Neighbour lookup for enemy separation (KAN-30). Once per frame, on the first query, snapshots the positions of
    /// <see cref="Enemy.Active"/> into a uniform grid (hashed buckets, 1.25 m cells); each query then walks only the 3 x 3
    /// cells around the caller. O(n) per frame instead of every enemy scanning every other one. No allocation after
    /// the arrays have grown to the horde size.
    /// The snapshot is taken at the start of the frame: an enemy that moved earlier in the same frame is seen where it
    /// stood. One frame of lag is invisible for a soft push.
    /// </summary>
    public static class EnemyCrowd
    {
        /// <summary>Cell edge (m). A query radius above this is clamped (3 x 3 cells only reach one cell out).</summary>
        public const float CellSize = 1.25f;
        private const int BucketCount = 256; // power of two
        private const int BucketMask = BucketCount - 1;

        private static readonly int[] Heads = new int[BucketCount];
        private static Vector3[] _positions = new Vector3[64];
        private static Enemy[] _owners = new Enemy[64];
        private static int[] _next = new int[64];
        private static int _count;
        private static int _frame = -1;

        /// <summary>Enemies in the current snapshot (debug and tests).</summary>
        public static int Count => _count;

        /// <summary>Forces a fresh snapshot on the next query (tests that move enemies inside one frame).</summary>
        public static void Invalidate() => _frame = -1;

        /// <summary>
        /// Push away from neighbours closer than <paramref name="radius"/>: each adds (radius - distance) / radius x
        /// <paramref name="strength"/> along the line between the two. Same shape as the old all-pairs loop.
        /// </summary>
        public static Vector3 Separation(Enemy self, Vector3 position, float radius, float strength)
        {
            if (radius <= 0f || strength <= 0f) return Vector3.zero;
            Refresh();
            radius = Mathf.Min(radius, CellSize);
            float r2 = radius * radius;
            int cx = Cell(position.x), cz = Cell(position.z);
            Vector3 push = Vector3.zero;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int gx = cx + dx, gz = cz + dz;
                for (int i = Heads[Bucket(gx, gz)]; i >= 0; i = _next[i])
                {
                    if (_owners[i] == self) continue;
                    Vector3 p = _positions[i];
                    if (Cell(p.x) != gx || Cell(p.z) != gz) continue; // bucket shared with a far cell
                    float ox = position.x - p.x, oz = position.z - p.z;
                    float d2 = ox * ox + oz * oz;
                    if (d2 >= r2 || d2 < 1e-6f) continue;
                    float m = Mathf.Sqrt(d2);
                    float k = (radius - m) / radius * strength / m;
                    push.x += ox * k;
                    push.z += oz * k;
                }
            }
            return push;
        }

        private static void Refresh()
        {
            int frame = Time.frameCount;
            if (frame == _frame) return;
            _frame = frame;
            List<Enemy> active = Enemy.Active;
            _count = active.Count;
            if (_positions.Length < _count)
            {
                int size = Mathf.NextPowerOfTwo(_count);
                _positions = new Vector3[size];
                _owners = new Enemy[size];
                _next = new int[size];
            }
            for (int b = 0; b < BucketCount; b++) Heads[b] = -1;
            for (int i = 0; i < _count; i++)
            {
                var e = active[i];
                Vector3 p = e.transform.position;
                _positions[i] = p;
                _owners[i] = e;
                int bucket = Bucket(Cell(p.x), Cell(p.z));
                _next[i] = Heads[bucket];
                Heads[bucket] = i;
            }
            for (int i = _count; i < _owners.Length && _owners[i] != null; i++) _owners[i] = null; // no stale refs
        }

        private static int Cell(float v) => Mathf.FloorToInt(v * (1f / CellSize));
        private static int Bucket(int x, int z) => ((x * 73856093) ^ (z * 19349663)) & BucketMask;
    }
}
