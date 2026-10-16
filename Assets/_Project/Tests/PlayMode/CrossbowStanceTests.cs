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
    /// KAN-216 (KAN-130.2) on the real Knight prefab with its Animator running: the controller has the upper-body aim
    /// layer (mask from Spine1 up) and the AimIdle / Lower / TurnStep clips; with a target the crossbow comes up to the
    /// right shoulder with both hands on it and the head at the stock; ~1 s after the last target the Lower clip plays and
    /// the layer fades out; a quick root turn while standing plays one turn step, slow drift does not.
    /// The 3 deg muzzle rule with this stance is CrossbowAimTests (the prefab now carries <see cref="CrossbowStance"/>).
    /// </summary>
    public sealed class CrossbowStanceTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Player_ArcherMeshy.prefab";

        private readonly List<Object> _objects = new List<Object>();
        private GameObject _player;
        private AutoShooter _shooter;
        private CrossbowStance _stance;
        private Animator _animator;
        private HumanoidRig _rig;
        private Transform _crossbow;

        /// <summary>Reads the final pose after every other LateUpdate (aim solver, recoil).</summary>
        [DefaultExecutionOrder(10000)]
        private sealed class PoseProbe : MonoBehaviour
        {
            public HumanoidRig Rig;
            public Transform Crossbow;
            public Transform Muzzle;
            public float GripHeight;     // crossbow origin (grip) above the root
            public float StockToShoulder; // stock end (0.25 m behind the grip) to the right shoulder joint
            public float LeftHandToLine; // left hand to the stock-muzzle segment
            public float HeadToLine;     // head to the stock-muzzle segment

            private void LateUpdate()
            {
                Vector3 grip = Crossbow.position, muzzle = Muzzle.position;
                GripHeight = grip.y - transform.position.y;
                StockToShoulder = Vector3.Distance(grip - (muzzle - grip).normalized * 0.25f, Rig.upperArmR.position);
                LeftHandToLine = ToSegment(Rig.handL.position, grip, muzzle);
                HeadToLine = ToSegment(Rig.head.position, grip - (muzzle - grip) * 0.5f, muzzle);
            }

            private static float ToSegment(Vector3 p, Vector3 a, Vector3 b)
            {
                Vector3 ab = b - a;
                float t = Mathf.Clamp01(Vector3.Dot(p - a, ab) / ab.sqrMagnitude);
                return Vector3.Distance(p, a + ab * t);
            }
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
            Assert.That(_stance, Is.Not.Null, "the Knight prefab carries CrossbowStance");
            _rig = _player.GetComponentInChildren<HumanoidRig>(true);
            Transform muzzle = null;
            foreach (var t in _player.GetComponentsInChildren<Transform>(true))
                if (t.name == "Crossbow_Muzzle") muzzle = t;
            Assert.That(muzzle, Is.Not.Null, "Crossbow_Muzzle socket");
            _crossbow = muzzle.parent;
            var aim = _player.AddComponent<CrossbowAim>();
            aim.Setup(_rig, muzzle);
            _shooter.SetMuzzle(muzzle);
            _animator = _player.GetComponentInChildren<Animator>(true);
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // no camera in the test: keep the clips running

            var template = Track(new GameObject("Bolt Template"));
            template.SetActive(false);
            _shooter.SetBoltPrefab(template.AddComponent<Bolt>());
            _player.transform.SetParent(null, false);
            var probe = _player.AddComponent<PoseProbe>();
            probe.Rig = _rig;
            probe.Crossbow = _crossbow;
            probe.Muzzle = muzzle;
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

        /// <summary>Waits game time (batch frames can be very short).</summary>
        private static IEnumerator Wait(float seconds)
        {
            float t = 0f;
            while (t < seconds)
            {
                yield return null;
                t += Time.deltaTime;
            }
        }

        private bool InState(int layer, string state)
        {
            int hash = Animator.StringToHash(state);
            if (_animator.GetCurrentAnimatorStateInfo(layer).shortNameHash == hash) return true;
            return _animator.IsInTransition(layer) && _animator.GetNextAnimatorStateInfo(layer).shortNameHash == hash;
        }

        [Test]
        public void Controller_Has_The_Upper_Body_Layer_And_The_Aim_Clips()
        {
            int layer = _animator.GetLayerIndex(CrossbowStance.UpperBodyLayer);
            Assert.That(layer, Is.GreaterThan(0), "UpperBody layer above the base layer");
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var c in _animator.runtimeAnimatorController.animationClips) clips[c.name] = c;
            Assert.That(clips.ContainsKey("AimIdle") && clips.ContainsKey("Lower") && clips.ContainsKey("TurnStep"), Is.True,
                string.Join(", ", clips.Keys));
            Assert.That(clips["AimIdle"].isLooping, Is.True, "aim idle loops");
            Assert.That(clips["Lower"].isLooping, Is.False, "lower is one shot");
            Assert.That(clips["TurnStep"].isLooping, Is.False, "turn step is one shot");
            Assert.That(clips["Lower"].length, Is.InRange(0.2f, 0.6f));
            Assert.That(_crossbow.IsChildOf(_rig.handR), Is.True, "the crossbow follows the right hand");
#if UNITY_EDITOR
            var ctrl = (UnityEditor.Animations.AnimatorController)_animator.runtimeAnimatorController;
            var upper = ctrl.layers[layer];
            Assert.That(upper.avatarMask, Is.Not.Null, "upper-body mask");
            Assert.That(upper.defaultWeight, Is.EqualTo(0f), "off until CrossbowStance raises it");
            bool spine1 = false, leg = true, hips = true, hand = false;
            for (int i = 0; i < upper.avatarMask.transformCount; i++)
            {
                string path = upper.avatarMask.GetTransformPath(i);
                bool on = upper.avatarMask.GetTransformActive(i);
                if (path.EndsWith("mixamorig:Spine1")) spine1 = on;
                if (path.EndsWith("mixamorig:LeftUpLeg")) leg = on;
                if (path.EndsWith("mixamorig:Hips")) hips = on;
                if (path.EndsWith("mixamorig:LeftHand")) hand = on;
            }
            Assert.That(spine1 && hand, Is.True, "Spine1 and the arms are in the mask");
            Assert.That(leg || hips, Is.False, "hips and legs stay with the base layer");
#endif
        }

        [UnityTest]
        public IEnumerator Target_Raises_The_Crossbow_To_The_Shoulder_With_Both_Hands()
        {
            var probe = _player.GetComponent<PoseProbe>();
            // KAN-217: shots play Fire / Reload on this layer; this test checks the held aim pose itself, so no shoot cycle.
            var cycle = _player.GetComponent<CrossbowCycle>();
            if (cycle != null) Object.DestroyImmediate(cycle);
            yield return Wait(0.4f); // no target: hip carry
            Assert.That(_stance.Weight, Is.EqualTo(0f), "no target: no aim layer");
            float carryHeight = probe.GripHeight, carryShoulder = probe.StockToShoulder;
            Assert.That(probe.LeftHandToLine, Is.LessThan(0.15f), "carry: left hand on the crossbow");

            SpawnEnemy(new Vector3(0f, 0f, 6f));
            yield return Wait(0.6f);
            Assert.That(_stance.Aiming, Is.True);
            Assert.That(_stance.Weight, Is.EqualTo(1f), "aim layer fully up");
            Assert.That(InState(_stance.Layer, CrossbowStance.AimState), Is.True, "Aim state");
            Assert.That(probe.GripHeight, Is.GreaterThan(carryHeight + 0.15f), $"crossbow up (carry {carryHeight:F2} m, aim {probe.GripHeight:F2} m)");
            Assert.That(probe.StockToShoulder, Is.LessThan(carryShoulder), $"stock nearer the right shoulder (carry {carryShoulder:F2} m, aim {probe.StockToShoulder:F2} m)");
            Assert.That(probe.StockToShoulder, Is.LessThan(0.2f), $"stock at the shoulder ({probe.StockToShoulder:F2} m)");
            Assert.That(probe.LeftHandToLine, Is.LessThan(0.15f), $"aim: left hand on the fore-stock ({probe.LeftHandToLine:F2} m)");
            Assert.That(probe.HeadToLine, Is.LessThan(0.2f), $"aim: cheek near the stock ({probe.HeadToLine:F2} m)");
        }

        [UnityTest]
        public IEnumerator Aiming_While_Standing_Plays_The_Low_Aim_Stance_On_The_Base_Layer()
        {
            // KAN-231: the legs and hips take part while aiming (knees bent, hips lower), not just the arms.
            Transform hips = null;
            foreach (var t in _player.GetComponentsInChildren<Transform>()) if (t.name == "mixamorig:Hips") hips = t;
            yield return Wait(0.6f);
            float idleHips = hips.position.y;
            Assert.That(InState(0, MeshyAimStanceState), Is.False, "no target: Idle");
            var enemy = SpawnEnemy(new Vector3(0f, 0f, 6f));
            yield return Wait(0.8f);
            Assert.That(InState(0, MeshyAimStanceState), Is.True, "aiming and standing: AimStance on the base layer");
            Assert.That(hips.position.y, Is.LessThan(idleHips - 0.02f), $"hips lower (idle {idleHips:F3} m, aim {hips.position.y:F3} m)");
            enemy.gameObject.SetActive(false);
            yield return Wait(2f); // Lower ~1 s after the last target, then Aim off
            Assert.That(InState(0, MeshyAimStanceState), Is.False, "crossbow lowered: back to Idle");
        }

        private const string MeshyAimStanceState = "AimStance"; // MeshyCharacterImporter.AimStanceState (editor assembly)

        [UnityTest]
        public IEnumerator Lower_Plays_About_One_Second_After_The_Last_Target()
        {
            var enemy = SpawnEnemy(new Vector3(0f, 0f, 6f));
            yield return Wait(0.5f);
            Assert.That(_stance.Weight, Is.EqualTo(1f));

            enemy.gameObject.SetActive(false); // the target is gone
            float t = 0f;
            while (!InState(_stance.Layer, CrossbowStance.LowerState) && t < 3f)
            {
                yield return null;
                t += Time.deltaTime;
            }
            Assert.That(t, Is.InRange(0.85f, 1.3f), "the Lower clip starts ~1 s after the last target");
            Assert.That(_stance.Weight, Is.EqualTo(1f), "the layer stays up while the crossbow comes down");
            yield return Wait(0.8f); // Lower 0.33 s + fade 0.15 s
            Assert.That(_stance.Weight, Is.EqualTo(0f), "back on the base hip carry");
            Assert.That(_stance.Aiming, Is.False);
        }

        [UnityTest]
        public IEnumerator Quick_Root_Turn_While_Standing_Plays_One_Step()
        {
            _shooter.enabled = false; // nothing else turns the root
            yield return Wait(0.2f);
            int steps = _stance.Steps;
            // 90 deg in ~0.2 s.
            float t = 0f;
            while (t < 0.2f)
            {
                yield return null;
                t += Time.deltaTime;
                _player.transform.Rotate(0f, 450f * Time.deltaTime, 0f);
            }
            float seen = 0f;
            bool stepped = false;
            while (seen < 0.3f && !stepped)
            {
                stepped = InState(0, CrossbowStance.TurnStepState);
                yield return null;
                seen += Time.deltaTime;
            }
            Assert.That(_stance.Steps, Is.EqualTo(steps + 1), "one step for a 90 deg turn");
            Assert.That(stepped, Is.True, "TurnStep state plays on the base layer");

            // Slow drift (15 deg/s for 3 s = 45 deg) never adds up to a step.
            yield return Wait(0.6f);
            steps = _stance.Steps;
            t = 0f;
            while (t < 3f)
            {
                yield return null;
                t += Time.deltaTime;
                _player.transform.Rotate(0f, 15f * Time.deltaTime, 0f);
            }
            Assert.That(_stance.Steps, Is.EqualTo(steps), "slow drift: no step");
        }

        [UnityTest]
        public IEnumerator Target_Behind_Turns_With_A_Step_And_Shoots()
        {
            var enemy = SpawnEnemy(new Vector3(0f, 0f, -6f));
            int shots = 0;
            _shooter.Fired += (_, __) => shots++;
            yield return Wait(2.2f);
            Assert.That(_stance.Steps, Is.GreaterThanOrEqualTo(1), "the 180 deg turn to the target plays a step");
            Assert.That(shots, Is.GreaterThanOrEqualTo(1), "still shoots");
            Assert.That(enemy, Is.Not.Null);
        }
    }
}
