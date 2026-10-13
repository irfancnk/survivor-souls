#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Gameplay.Combat;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-43 acceptance: zombies go back to the walk cycle after attacking (10 times in a row, within 0.5 s), the walk /
    /// attack switch does not dither at the edge of reach, a paused frame does not freeze the walk, a lost cross-fade is
    /// healed, and the shipped prefabs keep the state machine running off screen. Uses the real built zombie prefabs and
    /// controllers. No camera: every zombie is culled, so these tests also cover the off-screen case.
    /// </summary>
    public sealed class EnemyAnimationTests
    {
        private const string PrefabDir = "Assets/_Project/Prefabs/Combat/";
        private static readonly string[] Prefabs = { "Enemy_Zombie", "Enemy_ZombieRunner", "Enemy_ZombieBrute" };
        private static readonly int WalkHash = Animator.StringToHash("Walk");
        private static readonly int AttackHash = Animator.StringToHash("Attack");

        private readonly List<Object> _objects = new List<Object>();
        private Transform _player;

        [SetUp]
        public void SetUp()
        {
            Random.InitState(43);
            _player = Track(new GameObject("Player")).transform;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            Enemy.Active.Clear();
            Time.timeScale = 1f;
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private Enemy SpawnZombie(string prefab, Vector3 position)
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<Enemy>(PrefabDir + prefab + ".prefab");
            Assert.That(asset, Is.Not.Null, prefab);
            var enemy = Track(Object.Instantiate(asset));
            enemy.gameObject.SetActive(false);
            enemy.Spawn(position, _player, null);
            return enemy;
        }

        private static bool Plays(Animator animator, int hash) =>
            !animator.IsInTransition(0) && animator.GetCurrentAnimatorStateInfo(0).shortNameHash == hash;

        /// <summary>Walk cycle playing, or the cross-fade into it already under way.</summary>
        private static bool HeadsToWalk(Animator animator) => animator.IsInTransition(0)
            ? animator.GetNextAnimatorStateInfo(0).shortNameHash == WalkHash
            : animator.GetCurrentAnimatorStateInfo(0).shortNameHash == WalkHash;

        /// <summary>Keeps the player exactly <paramref name="distance"/> m in front of the enemy (the enemy cannot close in).</summary>
        private void HoldPlayerAt(Enemy enemy, float distance) =>
            _player.position = enemy.transform.position + Vector3.forward * distance;

        [UnityTest]
        public IEnumerator WalkResumes_AfterAttack_TenTimesInARow([ValueSource(nameof(Prefabs))] string prefab)
        {
            var enemy = SpawnZombie(prefab, Vector3.zero);
            var animator = enemy.Animator;
            for (int round = 0; round < 10; round++)
            {
                // Player stands next to the zombie: it attacks.
                _player.position = enemy.transform.position + Vector3.forward * 1f;
                float t = 0f;
                while (!Plays(animator, AttackHash) && t < 2f)
                {
                    t += Time.deltaTime;
                    yield return null;
                }
                Assert.That(enemy.IsAttacking, Is.True, $"round {round}: not attacking");
                Assert.That(Plays(animator, AttackHash), Is.True, $"round {round}: attack state not reached");
                yield return new WaitForSeconds(Random.Range(0f, 0.4f));

                // Player runs away: the walk cycle is back within 0.5 s of leaving reach.
                _player.position = enemy.transform.position + Vector3.forward * 6f;
                t = 0f;
                while (!HeadsToWalk(animator) && t < 2f)
                {
                    _player.position = enemy.transform.position + Vector3.forward * 6f;
                    t += Time.deltaTime;
                    yield return null;
                }
                Assert.That(enemy.IsAttacking, Is.False, $"round {round}: still attacking");
                Assert.That(t, Is.LessThanOrEqualTo(0.5f), $"round {round}: walk back after {t:F2} s");
                Assert.That(float.IsFinite(animator.speed) && animator.speed > 0f, Is.True, $"round {round}: speed {animator.speed}");
            }
        }

        [UnityTest]
        public IEnumerator EdgeOfReach_DoesNotDither()
        {
            var enemy = SpawnZombie("Enemy_Zombie", Vector3.zero);
            float reach = enemy.ContactRange;
            // Crowd jostle across the old single switch line (contact range + 0.15): at most the first switch into attack.
            int switches = 0;
            bool was = enemy.IsAttacking;
            for (float t = 0f; t < 1.5f; t += Time.deltaTime)
            {
                HoldPlayerAt(enemy, reach + (Time.frameCount % 2 == 0 ? 0.05f : 0.3f));
                yield return null;
                if (enemy.IsAttacking != was) switches++;
                was = enemy.IsAttacking;
            }
            Assert.That(switches, Is.LessThanOrEqualTo(1), "switched inside the hysteresis band");
            Assert.That(enemy.IsAttacking, Is.True);

            // Jostle across the whole band: switches are rate limited by the minimum hold.
            switches = 0;
            float elapsed = 0f;
            for (; elapsed < 1.5f; elapsed += Time.deltaTime)
            {
                HoldPlayerAt(enemy, reach + (Time.frameCount % 2 == 0 ? 0.05f : Enemy.AttackRelease + 0.3f));
                yield return null;
                if (enemy.IsAttacking != was) switches++;
                was = enemy.IsAttacking;
            }
            Assert.That(switches, Is.LessThanOrEqualTo(Mathf.CeilToInt(elapsed / Enemy.MinStateHold) + 1));
        }

        [UnityTest]
        public IEnumerator PausedFrames_DoNotFreezeTheWalk()
        {
            var enemy = SpawnZombie("Enemy_Zombie", Vector3.zero);
            var animator = enemy.Animator;
            _player.position = new Vector3(0f, 0f, 30f);
            yield return new WaitForSeconds(0.3f);

            Time.timeScale = 0f; // level-up / pause screen
            for (int i = 0; i < 5; i++) yield return null;
            Time.timeScale = 1f;
            yield return new WaitForSeconds(0.3f);

            Assert.That(float.IsFinite(animator.speed), Is.True, "animator speed NaN after pause");
            Assert.That(animator.speed, Is.GreaterThan(0.5f), "walk frozen after pause");
            Assert.That(Plays(animator, WalkHash), Is.True);
            float before = animator.GetCurrentAnimatorStateInfo(0).normalizedTime;
            yield return new WaitForSeconds(0.2f);
            Assert.That(animator.GetCurrentAnimatorStateInfo(0).normalizedTime, Is.GreaterThan(before), "walk clip does not advance");
        }

        [UnityTest]
        public IEnumerator LostCrossFade_IsHealed()
        {
            var enemy = SpawnZombie("Enemy_Zombie", Vector3.zero);
            var animator = enemy.Animator;
            _player.position = new Vector3(0f, 0f, 30f);
            yield return new WaitForSeconds(0.5f);
            Assert.That(enemy.IsAttacking, Is.False);

            int resyncs = Enemy.AnimatorResyncs;
            animator.Play(AttackHash, 0, 0f); // the animator ends up in Attack although the zombie walks
            float t = 0f;
            yield return null;
            while (!Plays(animator, WalkHash) && t < 2f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            Assert.That(Plays(animator, WalkHash), Is.True, "stuck in Attack");
            Assert.That(t, Is.LessThan(Enemy.AnimCheckInterval + 0.35f), $"healed after {t:F2} s");
            Assert.That(Enemy.AnimatorResyncs, Is.GreaterThan(resyncs));
        }

        [Test]
        public void ShippedPrefabs_KeepTheStateMachineRunningOffScreen([ValueSource(nameof(Prefabs))] string prefab)
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<Enemy>(PrefabDir + prefab + ".prefab");
            Assert.That(asset.Animator, Is.Not.Null);
            Assert.That(asset.Animator.cullingMode, Is.EqualTo(AnimatorCullingMode.CullUpdateTransforms));
            Assert.That(asset.AnimRefSpeed, Is.GreaterThan(0f));
        }
    }
}
#endif
