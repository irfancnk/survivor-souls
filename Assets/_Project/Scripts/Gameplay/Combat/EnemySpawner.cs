using System.Collections.Generic;
using SurvivorSouls.Gameplay.World;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Combat
{
    /// <summary>
    /// Keeps up to <see cref="maxAlive"/> enemies around the player: spawns one every <see cref="spawnInterval"/>
    /// seconds on a ring (<see cref="spawnDistance"/>) and clamps the spot inside the playable area (on land). Pools enemies.
    /// </summary>
    public sealed class EnemySpawner : MonoBehaviour
    {
        [SerializeField] private Enemy prefab;
        [SerializeField] private Transform player;
        [SerializeField] private WorldStreamer world;
        [SerializeField, Min(0)] private int maxAlive = 6;
        [SerializeField, Min(0.1f)] private float spawnInterval = 1.4f;
        [SerializeField, Min(0f)] private float firstSpawnDelay = 1f;
        [SerializeField] private Vector2 spawnDistance = new Vector2(7.5f, 10f);

        private readonly Stack<Enemy> _pool = new Stack<Enemy>();
        private int _inUse; // alive + dying
        private float _timer;

        private void Start()
        {
            _timer = firstSpawnDelay;
            Enemy.LandMaxZ = world != null ? world.PlayableMax.y + 0.8f : float.PositiveInfinity;
        }

        private void Update()
        {
            if (prefab == null || player == null) return;
            _timer -= Time.deltaTime;
            if (_timer > 0f || _inUse >= maxAlive) return;
            _timer = spawnInterval;
            Spawn();
        }

        private void Spawn()
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float dist = Random.Range(spawnDistance.x, spawnDistance.y);
            Vector3 pos = player.position + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * dist;
            if (world != null)
            {
                Vector2 min = world.PlayableMin + Vector2.one * 2f, max = world.PlayableMax - Vector2.one * 2f;
                pos.x = Mathf.Clamp(pos.x, min.x, max.x);
                pos.z = Mathf.Clamp(pos.z, min.y, max.y); // never in the sea
            }
            pos.y = 0f;

            var enemy = _pool.Count > 0 ? _pool.Pop() : Instantiate(prefab, transform);
            _inUse++;
            enemy.Spawn(pos, player, Release);
        }

        private void Release(Enemy enemy)
        {
            _inUse--;
            _pool.Push(enemy);
        }
    }
}
