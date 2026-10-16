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
    /// KAN-226: an enemy right behind the Knight never takes the bolt meant for the hugging enemy in front (bolt guard), and
    /// the aim point / bolt start blend between 1.25 and 1.5 m (no crossbow pitch jump at the edge, 3 deg rule from 1.5 m).
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
        public IEnumerator Hugging_Enemy_Is_Hit_By_A_Forward_Bolt([Values(0.2f, 0.45f, 1.2f, 1.3f, 1.42f)] float distance)
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

        [UnityTest]
        public IEnumerator Enemy_Behind_The_Knight_Never_Takes_The_Point_Blank_Bolt()
        {
            var front = Track(new GameObject("Front")).AddComponent<Enemy>();
            front.Spawn(new Vector3(0f, 0f, 0.2f), null, _ => { }, 100000f);
            var behind = Track(new GameObject("Behind")).AddComponent<Enemy>();
            behind.Spawn(new Vector3(0f, 0f, -0.3f), null, _ => { }, 100000f);
            Vector3 root = _player.transform.position;
            Vector3 start = PointBlankAim.Start(root, _muzzle.position, front.AimPoint);
            Assert.That(start.z, Is.LessThan(root.z), "set-up: the bolt starts behind the root");
            Assert.That(PointBlankAim.Guard(root, start, front.AimPoint), Is.GreaterThan(0.25f), "set-up: guarded path behind the root");

            float t = 0f;
            while ((_hits.Count < 4 || _dirs.Count < 4) && t < 6f)
            {
                yield return null;
                t += Time.deltaTime;
            }
            Assert.That(_dirs.Count, Is.GreaterThanOrEqualTo(4), "the Knight shoots");
            Assert.That(_hits.Count, Is.GreaterThanOrEqualTo(4), "every bolt hits");
            foreach (var hit in _hits) Assert.That(hit, Is.SameAs(front), "the enemy in front takes the bolt, never the one behind");
            Assert.That(behind.Health, Is.EqualTo(100000f), "the enemy behind is never hit");
        }

        [UnityTest]
        public IEnumerator Just_Outside_Point_Blank_Keeps_The_Three_Degree_Rule([Values(1.55f, 1.7f)] float distance)
        {
            var enemy = Track(new GameObject("Enemy")).AddComponent<Enemy>();
            enemy.Spawn(new Vector3(0f, 0f, distance), null, _ => { }, 100000f);
            Assert.That(PointBlankAim.IsPointBlank(_player.transform.position, enemy.AimPoint), Is.False, "set-up: normal shot");
            float t = 0f;
            while (_dirs.Count < 3 && t < 5f)
            {
                yield return null;
                t += Time.deltaTime;
            }
            Assert.That(_dirs.Count, Is.GreaterThanOrEqualTo(3), "the Knight shoots");
            foreach (var e in _lineErrors)
            {
                Debug.Log($"[KAN-226] {distance:F2} m: crossbow line vs bolt line {e:F2} deg");
                Assert.That(e, Is.LessThan(3f), "KAN-215 3 deg rule");
            }
        }

        [Test]
        public void Blend_Band_Is_Continuous_At_Both_Edges()
        {
            Vector3 root = Vector3.zero, muzzle = new Vector3(0.05f, 1.35f, 0.7f);
            const float eps = 1e-4f;
            foreach (float edge in new[] { PointBlankAim.BlendStart, PointBlankAim.Range })
            {
                var inside = new Vector3(0f, 1.08f, edge - eps);
                var outside = new Vector3(0f, 1.08f, edge + eps);
                Assert.That(Vector3.Distance(PointBlankAim.AimPoint(root, inside), PointBlankAim.AimPoint(root, outside)),
                    Is.LessThan(0.01f), $"aim point continuous at {edge} m");
                Assert.That(Vector3.Distance(PointBlankAim.Start(root, muzzle, inside), PointBlankAim.Start(root, muzzle, outside)),
                    Is.LessThan(0.01f), $"bolt start continuous at {edge} m");
            }
            Assert.That(PointBlankAim.Blend(root, new Vector3(0f, 1f, 1f)), Is.EqualTo(1f));
            Assert.That(PointBlankAim.Blend(root, new Vector3(0f, 1f, 1.375f)), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(PointBlankAim.Blend(root, new Vector3(0f, 1f, 1.6f)), Is.EqualTo(0f));

            // Across the band the crossbow pitch changes in small steps and follows the bolt pitch.
            float lastPitch = float.NaN;
            for (int i = 0; i <= 50; i++)
            {
                var target = new Vector3(0f, 1.08f, Mathf.Lerp(1.15f, 1.6f, i / 50f));
                Vector3 aim = PointBlankAim.AimPoint(root, target) - muzzle;
                Vector3 bolt = target - PointBlankAim.Start(root, muzzle, target);
                Assert.That(Vector3.Angle(aim, bolt), Is.LessThan(6f), $"crossbow line ~ bolt line at {target.z:F3} m");
                Assert.That(bolt.z, Is.GreaterThan(0f), "the bolt flies forward");
                float pitch = Mathf.Atan2(-aim.y, aim.z) * Mathf.Rad2Deg;
                if (!float.IsNaN(lastPitch)) Assert.That(Mathf.Abs(pitch - lastPitch), Is.LessThan(2f), "no pitch jump");
                lastPitch = pitch;
            }
        }

        [Test]
        public void Guard_Covers_Only_The_Path_Behind_The_Root()
        {
            Vector3 root = Vector3.zero, muzzle = new Vector3(0.05f, 1.35f, 0.7f);
            var hugging = new Vector3(0f, 1.08f, 0.2f);
            Vector3 from = PointBlankAim.Start(root, muzzle, hugging);
            Assert.That(PointBlankAim.Guard(root, from, hugging), Is.EqualTo(0.3f).Within(1e-3f), "0.5 m behind a 0.2 m target = 0.3 m behind the root");
            var near = new Vector3(0f, 1.08f, 1.4f);
            Assert.That(PointBlankAim.Guard(root, PointBlankAim.Start(root, muzzle, near), near), Is.EqualTo(0f), "start in front of the root");
            var far = new Vector3(0f, 1.08f, 5f);
            Assert.That(PointBlankAim.Guard(root, new Vector3(0f, 1.3f, -0.2f), far), Is.EqualTo(0f), "normal shot: no guard");
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
