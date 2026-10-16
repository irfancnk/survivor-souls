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
    /// KAN-224 acceptance on the real Knight prefab: an enemy closer than the crossbow muzzle (hugging the Knight) is hit,
    /// the bolt flies forward (not back through the Knight) and stays within 90 deg of the crossbow line, and the upper body
    /// does not twist to its yaw limit. Plus the <see cref="PointBlankAim"/> rule itself: no shift for a normal shot, and the
    /// shifted bolt line is the crossbow line.
    /// </summary>
    public sealed class PointBlankAimTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Player_ArcherMeshy.prefab";

        private readonly List<Object> _objects = new List<Object>();
        private readonly List<float> _lineErrors = new List<float>();
        private readonly List<Vector3> _dirs = new List<Vector3>();
        private readonly List<Enemy> _hits = new List<Enemy>();
        private GameObject _player;
        private AutoShooter _shooter;
        private CrossbowAim _aim;
        private Transform _muzzle;

        [SetUp]
        public void SetUp()
        {
            _lineErrors.Clear();
            _dirs.Clear();
            _hits.Clear();
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
            _aim = _player.AddComponent<CrossbowAim>();
            var rig = _player.GetComponentInChildren<HumanoidRig>(true);
            foreach (var t in _player.GetComponentsInChildren<Transform>(true))
                if (t.name == "Crossbow_Muzzle") _muzzle = t;
            Assert.That(_muzzle, Is.Not.Null, "Crossbow_Muzzle socket");
            _aim.Setup(rig, _muzzle);
            _shooter.SetMuzzle(_muzzle);
            var animator = _player.GetComponentInChildren<Animator>(true);
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            var template = Track(new GameObject("Bolt Template"));
            template.SetActive(false);
            _shooter.SetBoltPrefab(template.AddComponent<Bolt>());
            _player.transform.SetParent(null, false);
            _shooter.Fired += (from, dir) =>
            {
                _lineErrors.Add(Vector3.Angle(_aim.MuzzleLine, dir));
                _dirs.Add(dir);
            };
            _shooter.EnemyHit += hit => _hits.Add(hit.Enemy);
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

        [UnityTest]
        public IEnumerator Hugging_Enemy_Is_Hit_By_A_Forward_Bolt([Values(0.2f, 0.45f, 1.2f)] float distance)
        {
            var enemy = Track(new GameObject("Enemy")).AddComponent<Enemy>();
            enemy.Spawn(new Vector3(0f, 0f, distance), null, _ => { }, 100000f);
            Vector3 muzzle = _muzzle.position;
            if (distance < 0.5f)
                Assert.That(enemy.AimPoint.z, Is.LessThan(muzzle.z), $"set-up: the enemy is closer than the muzzle (z {muzzle.z:F2})");
            Assert.That(PointBlankAim.IsPointBlank(_player.transform.position, enemy.AimPoint), "set-up: point-blank");

            float t = 0f;
            while ((_hits.Count < 3 || _dirs.Count < 3) && t < 5f)
            {
                yield return null;
                t += Time.deltaTime;
            }
            Assert.That(_dirs.Count, Is.GreaterThanOrEqualTo(3), "the Knight shoots");
            Assert.That(_hits.Count, Is.GreaterThanOrEqualTo(3), "every bolt hits the hugging enemy");
            foreach (var hit in _hits) Assert.That(hit, Is.SameAs(enemy));
            for (int i = 0; i < _dirs.Count; i++)
            {
                Assert.That(_dirs[i].z, Is.GreaterThan(0f), "the bolt flies forward, not back through the Knight");
                Assert.That(_lineErrors[i], Is.LessThan(10f), "bolt line vs crossbow line (ticket: under 90)");
                Debug.Log($"[KAN-224] point-blank {distance:F2} m: crossbow line vs bolt line {_lineErrors[i]:F2} deg, dir {_dirs[i]}");
            }
            Vector3 line = _aim.MuzzleLine;
            line.y = 0f;
            Assert.That(Vector3.Angle(line, Vector3.forward), Is.LessThan(45f), "the upper body does not twist to its yaw limit");
        }

        [Test]
        public void Normal_Shot_Has_No_Shift()
        {
            Vector3 root = Vector3.zero, muzzle = new Vector3(0.1f, 1.3f, 0.5f), target = new Vector3(1f, 1f, 6f);
            Assert.That(PointBlankAim.IsPointBlank(root, target), Is.False);
            Assert.That(PointBlankAim.Start(root, muzzle, target), Is.EqualTo(muzzle));
            Assert.That(PointBlankAim.IsPointBlank(root, new Vector3(0f, 1f, PointBlankAim.Range + 0.01f)), Is.False, "edge of the range");
            Assert.That(PointBlankAim.AimPoint(root, target), Is.EqualTo(target));
        }

        [Test]
        public void Point_Blank_Bolt_Starts_Behind_The_Target_And_Flies_At_It([Values(-0.3f, 0f, 0.2f)] float ahead)
        {
            Vector3 root = Vector3.zero, muzzle = new Vector3(0.05f, 1.3f, 0.7f);
            var target = new Vector3(0f, 1.08f, muzzle.z + ahead);
            Vector3 from = PointBlankAim.Start(root, muzzle, target);
            Assert.That(from.y, Is.EqualTo(target.y), "a level bolt at the target aim height");
            Assert.That(from.x, Is.EqualTo(target.x).Within(1e-4f), "starts on the root-to-target line");
            Vector3 bolt = (target - from).normalized;
            Vector3 aim = PointBlankAim.AimPoint(root, target);
            Assert.That(target.z - from.z, Is.EqualTo(PointBlankAim.MinAhead).Within(1e-4f), "target sits MinAhead in front of the start");
            Assert.That(Vector3.Distance(aim, target), Is.EqualTo(PointBlankAim.AimReach).Within(1e-3f), "aim point far beyond the target");
            Assert.That(aim.z, Is.GreaterThan(target.z + 5f), "far down the forward line");
            Assert.That(Vector3.Angle(bolt, aim - muzzle), Is.LessThan(4f), "crossbow line ~ bolt line");
            Assert.That(bolt.z, Is.GreaterThan(0.8f), "the bolt flies forward");
        }
    }
}
