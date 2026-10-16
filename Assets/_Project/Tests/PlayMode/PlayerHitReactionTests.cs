using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Gameplay.Characters;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-225 on the real Knight prefab with its Animator running: a discrete hit while the Knight stands and aims plays
    /// the Hit clip on the base layer, the "UpperBody" aim layer dips (so the chest and arms flinch) and comes back after the
    /// clip; a fast hit chain plays one clip; a running Knight gets no clip and a clip is cut short when he starts to run;
    /// the killing blow plays nothing here; the crossbow still lines up with the bolt at each shot during a flinch.
    /// </summary>
    public sealed class PlayerHitReactionTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Player_ArcherMeshy.prefab";

        private readonly List<Object> _objects = new List<Object>();
        private GameObject _player;
        private AutoShooter _shooter;
        private CrossbowStance _stance;
        private CrossbowAim _aim;
        private PlayerHealth _health;
        private PlayerHitReaction _reaction;
        private Animator _animator;
        private HumanoidRig _rig;

        /// <summary>Chest rotation of the final pose (after the aim solver and recoil).</summary>
        [DefaultExecutionOrder(10000)]
        private sealed class ChestProbe : MonoBehaviour
        {
            public Transform Chest;
            public Quaternion Rotation;

            private void LateUpdate() => Rotation = Quaternion.Inverse(transform.rotation) * Chest.rotation;
        }

        [SetUp]
        public void SetUp()
        {
#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
#else
            GameObject prefab = null;
#endif
            if (prefab == null) Assert.Ignore("needs the editor asset database and the Knight prefab");

            var ground = Track(GameObject.CreatePrimitive(PrimitiveType.Plane)); // the CharacterController falls without it
            ground.transform.localScale = new Vector3(10f, 1f, 10f);
            var holder = Track(new GameObject("Holder"));
            holder.SetActive(false); // add the combat parts before any Awake
            _player = Track(Object.Instantiate(prefab, holder.transform));
            _player.AddComponent<PlayerStats>();
            _shooter = _player.AddComponent<AutoShooter>();
            _stance = _player.GetComponent<CrossbowStance>();
            _rig = _player.GetComponentInChildren<HumanoidRig>(true);
            Transform muzzle = null;
            foreach (var t in _player.GetComponentsInChildren<Transform>(true))
                if (t.name == "Crossbow_Muzzle") muzzle = t;
            Assert.That(muzzle, Is.Not.Null, "Crossbow_Muzzle socket");
            _aim = _player.AddComponent<CrossbowAim>();
            _aim.Setup(_rig, muzzle);
            _shooter.SetMuzzle(muzzle);
            _health = _player.AddComponent<PlayerHealth>();
            _reaction = _player.AddComponent<PlayerHitReaction>();
            _reaction.Setup(_player.GetComponent<LocomotionAnimatorDriver>());
            _animator = _player.GetComponentInChildren<Animator>(true);
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // no camera in the test: keep the clips running

            var template = Track(new GameObject("Bolt Template"));
            template.SetActive(false);
            _shooter.SetBoltPrefab(template.AddComponent<Bolt>());
            _player.transform.SetParent(null, false);
            var probe = _player.AddComponent<ChestProbe>();
            probe.Chest = _rig.chest;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var b in Resources.FindObjectsOfTypeAll<Bolt>())
                if (b != null && b.gameObject.scene.IsValid()) Object.DestroyImmediate(b.gameObject);
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private Enemy SpawnEnemy(Vector3 position)
        {
            var e = Track(new GameObject("Enemy")).AddComponent<Enemy>();
            e.Spawn(position, null, _ => { }, 100000f);
            return e;
        }

        private static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                yield return null;
                t += Time.deltaTime;
            }
        }

        private bool InHit()
        {
            if (_animator.GetCurrentAnimatorStateInfo(0).shortNameHash == PlayerHitReaction.HitStateHash) return true;
            return _animator.IsInTransition(0) && _animator.GetNextAnimatorStateInfo(0).shortNameHash == PlayerHitReaction.HitStateHash;
        }

        private static float HitClipLength(Animator animator)
        {
            foreach (var c in animator.runtimeAnimatorController.animationClips)
                if (c.name == "Hit_Reaction") return c.length;
            return 0f;
        }

        [UnityTest]
        public IEnumerator Hit_While_Aiming_Plays_The_Clip_And_Dips_The_Upper_Body_Layer()
        {
            var enemy = SpawnEnemy(new Vector3(0f, 0f, 6f));
            yield return Wait(0.8f);
            Assert.That(_stance.Weight, Is.EqualTo(1f), "aiming: upper-body layer up");
            Assert.That(_stance.LayerWeight, Is.EqualTo(1f), "no hit: no dip");
            var probe = _player.GetComponent<ChestProbe>();
            Quaternion aimChest = probe.Rotation;

            _health.TakeDamage(10f, enemy.transform.position);
            Assert.That(_reaction.Reactions, Is.EqualTo(1), "a standing Knight plays the hit clip");
            float t = 0f, minLayer = 1f, maxChest = 0f;
            bool sawHit = false;
            while (t < 0.5f)
            {
                yield return null;
                t += Time.deltaTime;
                sawHit |= InHit();
                minLayer = Mathf.Min(minLayer, _animator.GetLayerWeight(_stance.Layer));
                maxChest = Mathf.Max(maxChest, Quaternion.Angle(aimChest, probe.Rotation));
            }
            Debug.Log($"[PlayerHitReactionTests] Hit_Reaction {HitClipLength(_animator):F2} s, layer weight low {minLayer:F2}, chest flinch {maxChest:F1} deg");
            Assert.That(sawHit, Is.True, "the base layer plays Hit");
            Assert.That(minLayer, Is.LessThan(0.35f), "the aim layer dips so the flinch shows on the upper body");
            Assert.That(maxChest, Is.GreaterThan(3f), "the chest moves with the flinch");
            Assert.That(_stance.Weight, Is.EqualTo(1f), "the stance itself stays up (target still there)");

            yield return Wait(HitClipLength(_animator) + 0.6f);
            Assert.That(InHit(), Is.False, "the clip is over");
            Assert.That(_stance.HitBlend, Is.EqualTo(0f), "flinch blend back to 0");
            Assert.That(_animator.GetLayerWeight(_stance.Layer), Is.EqualTo(1f), "full aim layer again");
            Assert.That(_stance.AimShare, Is.EqualTo(1f), "full spine aim again");
        }

        [UnityTest]
        public IEnumerator Fast_Hit_Chain_Plays_One_Clip()
        {
            var enemy = SpawnEnemy(new Vector3(0f, 0f, 6f));
            yield return Wait(0.5f);
            _health.TakeDamage(5f, enemy.transform.position);
            yield return Wait(0.15f);
            _health.TakeDamage(5f, enemy.transform.position);
            Assert.That(_reaction.Reactions, Is.EqualTo(1), "second hit inside the cooldown: no new clip");
            yield return Wait(0.7f);
            _health.TakeDamage(5f); // contact damage (no OnHit): never a clip
            Assert.That(_reaction.Reactions, Is.EqualTo(1), "contact damage plays no clip");
            _health.TakeDamage(5f, enemy.transform.position);
            Assert.That(_reaction.Reactions, Is.EqualTo(2), "after the cooldown the next discrete hit plays again");
        }

        [UnityTest]
        public IEnumerator Running_Knight_Gets_No_Clip_And_Running_Cuts_A_Clip_Short()
        {
            _shooter.enabled = false;
            _player.GetComponent<PlayerMover>().enabled = false; // the test moves the root itself (no CharacterController.Move)
            yield return Wait(0.3f);
            // Run: 3 m/s along +x.
            float t = 0f;
            while (t < 0.3f)
            {
                yield return null;
                t += Time.deltaTime;
                _player.transform.position += new Vector3(3f * Time.deltaTime, 0f, 0f);
            }
            _health.TakeDamage(5f, _player.transform.position + Vector3.forward);
            Assert.That(_reaction.Reactions, Is.EqualTo(0), "running: no base-layer clip (no foot slide)");

            yield return Wait(0.8f); // stand still
            _health.TakeDamage(5f, _player.transform.position + Vector3.forward);
            Assert.That(_reaction.Reactions, Is.EqualTo(1), "standing: clip");
            yield return Wait(0.1f);
            Assert.That(InHit(), Is.True, "Hit plays");
            t = 0f;
            while (t < 0.4f)
            {
                yield return null;
                t += Time.deltaTime;
                _player.transform.position += new Vector3(3f * Time.deltaTime, 0f, 0f);
            }
            Assert.That(_reaction.Cancels, Is.GreaterThanOrEqualTo(1), "running cut the clip");
            Assert.That(_animator.GetCurrentAnimatorStateInfo(0).shortNameHash, Is.Not.EqualTo(PlayerHitReaction.HitStateHash),
                "back to locomotion within 0.4 s of running");
        }

        [UnityTest]
        public IEnumerator Killing_Blow_Plays_No_Hit_Clip_Here()
        {
            yield return Wait(0.3f);
            _health.TakeDamage(1e6f, _player.transform.position + Vector3.forward);
            Assert.That(_health.IsDead, Is.True);
            Assert.That(_reaction.Reactions, Is.EqualTo(0), "PlayerDeath owns the death pose");
        }

        [UnityTest]
        public IEnumerator Crossbow_Still_On_The_Bolt_Line_At_Each_Shot_During_A_Flinch()
        {
            var enemy = SpawnEnemy(new Vector3(1.5f, 0f, 6f));
            var errors = new List<float>();
            bool flinching = false;
            _shooter.Fired += (from, dir) => { if (flinching) errors.Add(Vector3.Angle(_aim.MuzzleLine, dir)); };
            yield return Wait(0.8f);
            float t = 0f;
            while (t < 4f)
            {
                if (!InHit() && _stance.HitBlend <= 0f) _health.TakeDamage(1f, enemy.transform.position); // keep flinching
                yield return null;
                t += Time.deltaTime;
                flinching = _stance.HitBlend > 0f;
            }
            Assert.That(_reaction.Reactions, Is.GreaterThanOrEqualTo(2), "several flinches");
            Assert.That(errors.Count, Is.GreaterThanOrEqualTo(1), "shots during a flinch");
            float worst = 0f;
            foreach (var e in errors) worst = Mathf.Max(worst, e);
            Debug.Log($"[PlayerHitReactionTests] {errors.Count} shots during a flinch, worst crossbow vs bolt line {worst:F2} deg");
            Assert.That(worst, Is.LessThan(3f), "KAN-215 3 deg rule at the shot");
        }
    }
}
