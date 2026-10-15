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
    /// KAN-104 runtime: the new roster prefabs play Move while chasing, Attack in reach, a short Hit flinch on a direct hit
    /// (rate limited, never on bosses) and the Death state before the body sinks back into its pool. The old zombie keeps its
    /// tip-over death. Uses the real built prefabs (run the Game scene builder first). No camera: every body is culled.
    /// </summary>
    public sealed class EnemyRosterTests
    {
        private const string Dir = "Assets/_Project/Prefabs/Enemies/";
        private static readonly string[] MeleeTiers = { "GraveyardLaborer", "RustedWatchman" };
        private static readonly int MoveHash = Animator.StringToHash("Move");
        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static readonly int HitHash = Animator.StringToHash("Hit");
        private static readonly int DeathHash = Animator.StringToHash("Death");

        private readonly List<Object> _objects = new List<Object>();
        private Transform _player;
        private int _released;

        [SetUp]
        public void SetUp()
        {
            Random.InitState(104);
            _released = 0;
            _player = Track(new GameObject("Player")).transform;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            Enemy.Active.Clear();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private Enemy Spawn(string path)
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<Enemy>(path);
            Assert.That(asset, Is.Not.Null, path);
            var enemy = Track(Object.Instantiate(asset));
            enemy.gameObject.SetActive(false);
            enemy.Spawn(Vector3.zero, _player, _ => _released++);
            return enemy;
        }

        private static bool Heads(Animator animator, int hash) => animator.IsInTransition(0)
            ? animator.GetNextAnimatorStateInfo(0).shortNameHash == hash
            : animator.GetCurrentAnimatorStateInfo(0).shortNameHash == hash;

        [UnityTest]
        public IEnumerator MeleeTier_ChasesWithMove_AttacksInReach([ValueSource(nameof(MeleeTiers))] string id)
        {
            var enemy = Spawn(Dir + "Enemy_" + id + ".prefab");
            _player.position = new Vector3(0f, 0f, 20f);
            yield return new WaitForSeconds(0.3f);
            Assert.That(Heads(enemy.Animator, MoveHash), Is.True, "Move while chasing");
            Assert.That(enemy.transform.position.z, Is.GreaterThan(0.2f), "walks to the Knight");
            Assert.That(enemy.Animator.speed, Is.InRange(0.2f, 2.2f));

            _player.position = enemy.transform.position + Vector3.forward * (enemy.ContactRange * 0.8f);
            float t = 0f;
            while (!Heads(enemy.Animator, AttackHash) && t < 1.5f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            Assert.That(enemy.IsAttacking, Is.True);
            Assert.That(Heads(enemy.Animator, AttackHash), Is.True, "Attack in reach");
        }

        [UnityTest]
        public IEnumerator DirectHit_Flinches_ThenWalksOn_RateLimited()
        {
            var enemy = Spawn(Dir + "Enemy_GraveyardLaborer.prefab");
            _player.position = new Vector3(0f, 0f, 30f);
            yield return new WaitForSeconds(0.3f);

            enemy.TakeDamage(1f, Vector3.back);
            Assert.That(enemy.IsFlinching, Is.True);
            yield return null;
            Assert.That(Heads(enemy.Animator, HitHash), Is.True, "Hit state");

            yield return new WaitForSeconds(0.2f);
            enemy.TakeDamage(1f, Vector3.back); // within the flinch: no restart
            float t = 0f;
            while (enemy.IsFlinching && t < 2f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            Assert.That(t, Is.LessThan(0.5f), "flinch length");
            yield return null;
            Assert.That(Heads(enemy.Animator, MoveHash), Is.True, "back to Move");

            enemy.TakeDamage(1f, Vector3.back); // inside the cooldown
            Assert.That(enemy.IsFlinching, Is.False, "cooldown");
            yield return new WaitForSeconds(Enemy.HitCooldown);
            enemy.TakeDamage(1f, Vector3.back, DamageKind.Burn);
            Assert.That(enemy.IsFlinching, Is.False, "burn ticks do not flinch");
            enemy.TakeDamage(1f, Vector3.back);
            Assert.That(enemy.IsFlinching, Is.True, "flinches again after the cooldown");
        }

        [UnityTest]
        public IEnumerator Attacking_DoesNotFlinch()
        {
            var enemy = Spawn(Dir + "Enemy_RustedWatchman.prefab");
            _player.position = Vector3.forward * (enemy.ContactRange * 0.8f);
            float t = 0f;
            while (!enemy.IsAttacking && t < 1.5f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            Assert.That(enemy.IsAttacking, Is.True);
            enemy.TakeDamage(1f, Vector3.back);
            Assert.That(enemy.IsFlinching, Is.False);
        }

        [UnityTest]
        public IEnumerator Boss_NeverFlinches()
        {
            var boss = Spawn(Dir + "Enemy_AshgraveButcher.prefab");
            _player.position = new Vector3(0f, 0f, 40f);
            yield return null;
            boss.TakeDamage(10f, Vector3.back);
            Assert.That(boss.IsFlinching, Is.False);
            Assert.That(boss.IsBoss, Is.True);
        }

        [UnityTest]
        public IEnumerator Kill_PlaysDeath_ThenReturnsToPool()
        {
            var enemy = Spawn(Dir + "Enemy_RustedWatchman.prefab");
            _player.position = new Vector3(0f, 0f, 30f);
            yield return new WaitForSeconds(0.2f);
            Quaternion before = enemy.transform.rotation;
            enemy.TakeDamage(enemy.MaxHealth + 1f, Vector3.back);
            Assert.That(enemy.IsAlive, Is.False);
            Assert.That(enemy.HasDeathState, Is.True);
            Assert.That(enemy.DeathDuration, Is.LessThan(2f), "dying bodies count against the cap: keep it short");
            yield return null;
            yield return null;
            Assert.That(Heads(enemy.Animator, DeathHash), Is.True, "Death state");
            yield return new WaitForSeconds(0.3f);
            Assert.That(Quaternion.Angle(before, enemy.transform.rotation), Is.LessThan(1f), "no tip-over on top of the Death clip");
            Assert.That(enemy.Animator.speed, Is.EqualTo(1f));
            float t = 0.3f;
            while (_released == 0 && t < 4f)
            {
                t += Time.deltaTime;
                yield return null;
            }
            Assert.That(_released, Is.EqualTo(1), "back in the pool");
            Assert.That(t, Is.EqualTo(enemy.DeathDuration).Within(0.25f));
            Assert.That(enemy.gameObject.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator OldZombie_KeepsTheTipOverDeath()
        {
            var enemy = Spawn("Assets/_Project/Prefabs/Combat/Enemy_Zombie.prefab");
            yield return null;
            Assert.That(enemy.HasDeathState, Is.False);
            Quaternion before = enemy.transform.rotation;
            enemy.TakeDamage(enemy.MaxHealth + 1f, Vector3.back);
            Assert.That(enemy.IsFlinching, Is.False);
            yield return new WaitForSeconds(0.3f);
            Assert.That(Quaternion.Angle(before, enemy.transform.rotation), Is.GreaterThan(40f), "tips over");
            Assert.That(enemy.Animator.speed, Is.EqualTo(0f), "pose held");
        }
    }
}
#endif
