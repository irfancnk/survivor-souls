using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Gameplay.Characters;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-215 (KAN-130.1) acceptance on the real Knight prefab with its Animator running: at the moment of each shot the
    /// crossbow line is within 3 deg of the bolt line for targets at 0 / 90 / 180 deg, the head looks at the target, the
    /// muzzle axis is the crossbow's long axis, the fire rate is the same as a shooter without the aim (base and attack
    /// speed), and the aim allocates nothing.
    /// </summary>
    public sealed class CrossbowAimTests
    {
        private const float Frame = 1f / 60f;
        private const string PrefabPath = "Assets/_Project/Prefabs/Player_ArcherMeshy.prefab";

        private readonly List<Object> _objects = new List<Object>();
        private GameObject _player;
        private AutoShooter _shooter;
        private CrossbowAim _aim;
        private Transform _muzzle;
        private readonly List<float> _errors = new List<float>();

        /// <summary>Reads the final pose after every other LateUpdate (the aim, the recoil).</summary>
        [DefaultExecutionOrder(10000)]
        private sealed class PoseProbe : MonoBehaviour
        {
            public CrossbowAim Aim;
            public HumanoidRig Rig;
            public Vector3 Target;
            public float HeadError = 180f;
            public float LineError = 180f;

            private void LateUpdate()
            {
                Vector3 head = Aim.HeadForward, to = Target - Rig.head.position;
                head.y = to.y = 0f;
                HeadError = Vector3.Angle(head, to);
                LineError = Aim.AimError(Target);
            }
        }

        [SetUp]
        public void SetUp()
        {
            _errors.Clear();
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
            var animator = _player.GetComponentInChildren<Animator>(true);
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // no camera in the test: keep the clips running

            var template = Track(new GameObject("Bolt Template"));
            template.SetActive(false);
            _shooter.SetBoltPrefab(template.AddComponent<Bolt>());
            _player.transform.SetParent(null, false);
            _shooter.Fired += (from, dir) => _errors.Add(Vector3.Angle(_aim.MuzzleLine, dir));
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
            e.Spawn(position, null, _ => { }, 1000f);
            return e;
        }

        [UnityTest]
        public IEnumerator Muzzle_Line_Is_On_The_Bolt_Line_At_Each_Shot([Values(0f, 90f, 180f)] float bearing)
        {
            var enemy = SpawnEnemy(Quaternion.AngleAxis(bearing, Vector3.up) * new Vector3(0f, 0f, 6f));
            var probe = _player.AddComponent<PoseProbe>();
            probe.Aim = _aim;
            probe.Rig = _player.GetComponentInChildren<HumanoidRig>();
            probe.Target = enemy.AimPoint;

            // Wait game time, not frames (batch frames can be very short): 3 shots at the 1 s base interval.
            float t = 0f;
            while (_errors.Count < 3 && t < 5f)
            {
                yield return null;
                t += Time.deltaTime;
            }
            Assert.That(_errors.Count, Is.GreaterThanOrEqualTo(3), "the Knight shoots");
            foreach (float e in _errors) Assert.That(e, Is.LessThan(3f), $"crossbow line vs bolt line at bearing {bearing}");
            Assert.That(probe.LineError, Is.LessThan(3f), "the rendered pose aims at the target between shots too");
            Assert.That(probe.HeadError, Is.LessThan(5f), "the head looks at the target");
            Assert.That(_aim.Weight, Is.EqualTo(1f));
        }

        [Test]
        public void Muzzle_Axis_Is_The_Crossbow_Long_Axis()
        {
            var crossbow = _muzzle.parent;
            var mesh = crossbow.GetComponent<MeshFilter>();
            Assert.That(mesh, Is.Not.Null, "Crossbow mesh");
            Vector3 size = mesh.sharedMesh.bounds.size;
            Assert.That(size.x, Is.GreaterThan(size.y).And.GreaterThan(size.z), "the crossbow is long along its local X");
            Assert.That(_muzzle.localPosition.x, Is.GreaterThan(0.3f), "the muzzle sits at the +X end");
            Assert.That(Vector3.Angle(_aim.MuzzleLine, crossbow.right), Is.LessThan(1f), "aim axis = stock-to-muzzle line");
        }

        [Test]
        public void Fire_Rate_Is_Unchanged_By_The_Aim([Values(0f, 1.5f)] float attackSpeedBonus)
        {
            // A bare shooter (no aim, no model) next to the Knight, same target bearing and stats: both fire the same count.
            var bare = Track(new GameObject("Bare Shooter"));
            bare.SetActive(false);
            var bareStats = bare.AddComponent<PlayerStats>();
            bare.AddComponent<CharacterController>();
            bare.AddComponent<PlayerMover>();
            var bareShooter = bare.AddComponent<AutoShooter>();
            bare.transform.position = new Vector3(40f, 0f, 0f);
            bare.SetActive(true);
            var template = Track(new GameObject("Bolt Template 2"));
            template.SetActive(false);
            bareShooter.SetBoltPrefab(template.AddComponent<Bolt>());

            var stats = _player.GetComponent<PlayerStats>();
            if (attackSpeedBonus > 0f)
            {
                var mod = new[] { new StatModifier(StatId.AttackSpeed, ModifierKind.Flat, attackSpeedBonus) };
                stats.SetSource(_player, mod, 1);
                bareStats.SetSource(bare, mod, 1);
            }
            _shooter.enabled = bareShooter.enabled = false; // step exact frames
            SpawnEnemy(new Vector3(0f, 0f, 6f));
            SpawnEnemy(new Vector3(40f, 0f, 6f));
            int bareShots = 0;
            bareShooter.Fired += (_, __) => bareShots++;
            for (int i = 0; i < 600; i++)
            {
                _shooter.Tick(Frame);
                bareShooter.Tick(Frame);
            }
            Assert.That(_errors.Count, Is.EqualTo(bareShots), "same shots in 10 s");
            Assert.That(_errors.Count, Is.GreaterThanOrEqualTo(Mathf.FloorToInt(10f * (1f + attackSpeedBonus))));
            foreach (float e in _errors) Assert.That(e, Is.LessThan(3f));
        }

        [Test]
        public void Align_Allocates_Nothing()
        {
            var point = new Vector3(3f, 1f, 4f);
            _aim.AlignNow(point); // warm-up
            Assert.That(() => _aim.AlignNow(point), Is.Not.AllocatingGCMemory());
            Assert.That(_aim.AimError(point), Is.LessThan(1f));
        }
    }
}
