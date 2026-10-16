using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Gameplay.Characters;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-217 (KAN-130.3) on the real Knight prefab with its Animator running: each volley plays Fire then Reload on the
    /// upper-body layer, the reload speed fits the fire interval, a fire rate too high for the reload plays the snap
    /// variant (Fire straight back to Aim), the loaded bolt hides on the shot and comes back at the seat point, and the
    /// animation never changes the shots per minute (base, Quick Draw max, Double Shot, very high rate).
    /// </summary>
    public sealed class CrossbowCycleTests
    {
        private const string PrefabPath = "Assets/_Project/Prefabs/Player_ArcherMeshy.prefab";
        private const float Frame = 1f / 60f;

        private readonly List<Object> _objects = new List<Object>();
        private GameObject _player;
        private AutoShooter _shooter;
        private CrossbowCycle _cycle;
        private Animator _animator;
        private PlayerStats _stats;
        private GameObject _loadedBolt;
        private int _layer;

        [SetUp]
        public void SetUp()
        {
            Enemy.Active.Clear();
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
            _stats = _player.AddComponent<PlayerStats>();
            _shooter = _player.AddComponent<AutoShooter>();
            _cycle = _player.GetComponent<CrossbowCycle>();
            Assert.That(_cycle, Is.Not.Null, "the Knight prefab carries CrossbowCycle");
            Transform muzzle = null;
            foreach (var t in _player.GetComponentsInChildren<Transform>(true))
            {
                if (t.name == "Crossbow_Muzzle") muzzle = t;
                if (t.name == "Crossbow_LoadedBolt") _loadedBolt = t.gameObject;
            }
            Assert.That(muzzle, Is.Not.Null, "Crossbow_Muzzle socket");
            Assert.That(_loadedBolt, Is.Not.Null, "loaded bolt on the rail");
            var aim = _player.AddComponent<CrossbowAim>();
            aim.Setup(_player.GetComponentInChildren<HumanoidRig>(true), muzzle);
            _shooter.SetMuzzle(muzzle);
            _animator = _player.GetComponentInChildren<Animator>(true);
            _animator.cullingMode = AnimatorCullingMode.AlwaysAnimate; // no camera in the test: keep the clips running

            var template = Track(new GameObject("Bolt Template"));
            template.SetActive(false);
            _shooter.SetBoltPrefab(template.AddComponent<Bolt>());
            _player.transform.SetParent(null, false);
            _layer = _animator.GetLayerIndex(CrossbowStance.UpperBodyLayer);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var b in Resources.FindObjectsOfTypeAll<Bolt>())
                if (b != null && b.gameObject.scene.IsValid()) Object.DestroyImmediate(b.gameObject);
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

        private void AttackSpeed(float percent)
        {
            if (percent > 0f) _stats.SetSource(_player, new[] { new StatModifier(StatId.AttackSpeed, ModifierKind.Percent, percent) }, 1);
        }

        private int StateHash => _animator.IsInTransition(_layer)
            ? _animator.GetNextAnimatorStateInfo(_layer).shortNameHash
            : _animator.GetCurrentAnimatorStateInfo(_layer).shortNameHash;

        [Test]
        public void Controller_Has_Fire_And_Reload_On_The_Upper_Body_Layer()
        {
            var clips = new Dictionary<string, AnimationClip>();
            foreach (var c in _animator.runtimeAnimatorController.animationClips) clips[c.name] = c;
            Assert.That(clips.ContainsKey("Fire") && clips.ContainsKey("Reload"), Is.True, string.Join(", ", clips.Keys));
            Assert.That(clips["Fire"].isLooping || clips["Reload"].isLooping, Is.False, "fire and reload are one shot");
            Assert.That(clips["Fire"].length, Is.InRange(0.08f, 0.2f), "fire is a short kick (~0.12 s)");
            Assert.That(_cycle.FireLength, Is.EqualTo(clips["Fire"].length).Within(0.001f), "importer wrote the fire length");
            Assert.That(_cycle.ReloadLength, Is.EqualTo(clips["Reload"].length).Within(0.001f), "importer wrote the reload length");
            Assert.That(_animator.HasState(_layer, CrossbowCycle.FireStateHash), Is.True);
            Assert.That(_animator.HasState(_layer, CrossbowCycle.ReloadStateHash), Is.True);
        }

        [Test]
        public void ReloadSpeed_Fits_The_Interval_And_Snaps_When_Too_Fast()
        {
            float fire = _cycle.FireLength, reload = _cycle.ReloadLength, fill = _cycle.Fill;
            var range = _cycle.SpeedRange;
            float speed = CrossbowCycle.SpeedFor(1f, fire, reload, fill, range);
            Assert.That(speed, Is.EqualTo(reload / ((1f - fire) * fill)).Within(1e-4f), "base: reload / free time");
            Assert.That(fire + reload / speed, Is.LessThan(1f), "the crossbow is back on aim before the next shot");
            // Quick Draw max (+120 %): 2.2 shots per second still plays the full reload, faster.
            float quick = CrossbowCycle.SpeedFor(1f / 2.2f, fire, reload, fill, range);
            Assert.That(quick, Is.GreaterThan(speed).And.LessThanOrEqualTo(range.y), "Quick Draw max: faster reload");
            Assert.That(CrossbowCycle.SpeedFor(1f / 3.5f, fire, reload, fill, range), Is.EqualTo(0f), "3.5 shots/s: snap");
            Assert.That(CrossbowCycle.SpeedFor(0.1f, fire, reload, fill, range), Is.EqualTo(0f), "interval under the fire clip: snap");
            Assert.That(CrossbowCycle.SpeedFor(5f, fire, reload, fill, range), Is.EqualTo(range.x), "slow fire: slowest reload");
        }

        /// <summary>Same shots in 20 s with the cycle as a bare shooter with the same stats (the animation never gates firing).</summary>
        [UnityTest]
        public IEnumerator ShotsPerMinute_Unchanged([Values(0f, 1.2f, 2.5f)] float attackSpeed, [Values(0, 2)] int doubleShot)
        {
            var holder = Track(new GameObject("Bare Holder"));
            holder.SetActive(false);
            var bare = Track(new GameObject("Bare"));
            bare.transform.SetParent(holder.transform, false);
            bare.AddComponent<CharacterController>();
            bare.AddComponent<SurvivorSouls.Gameplay.Player.PlayerMover>();
            var bareStats = bare.AddComponent<PlayerStats>();
            var bareShooter = bare.AddComponent<AutoShooter>();
            var template = Track(new GameObject("Bolt Template 2"));
            template.SetActive(false);
            bareShooter.SetBoltPrefab(template.AddComponent<Bolt>());
            bare.transform.SetParent(null, false);
            bare.transform.position = new Vector3(40f, 0f, 0f);

            var mods = new List<StatModifier>();
            if (attackSpeed > 0f) mods.Add(new StatModifier(StatId.AttackSpeed, ModifierKind.Percent, attackSpeed));
            if (doubleShot > 0) mods.Add(new StatModifier(StatId.DoubleShot, ModifierKind.Flat, doubleShot));
            if (mods.Count > 0)
            {
                _stats.SetSource(_player, mods.ToArray(), 1);
                bareStats.SetSource(bare, mods.ToArray(), 1);
            }
            yield return null; // Start: the cycle subscribes to the shooter
            _shooter.enabled = bareShooter.enabled = false; // step exact frames
            SpawnEnemy(new Vector3(0f, 0f, 6f));
            SpawnEnemy(new Vector3(40f, 0f, 6f));
            int shots = 0, bareShots = 0, followUps = 0, bareFollowUps = 0;
            _shooter.Fired += (_, __) => shots++;
            bareShooter.Fired += (_, __) => bareShots++;
            _shooter.FollowUpFired += (_, __) => followUps++;
            bareShooter.FollowUpFired += (_, __) => bareFollowUps++;
            for (int i = 0; i < 1200; i++)
            {
                _shooter.Tick(Frame);
                bareShooter.Tick(Frame);
                if (i % 60 == 0) yield return null; // let the Animator run the cycle now and then
            }
            Assert.That(shots, Is.EqualTo(bareShots), "same volleys in 20 s");
            Assert.That(followUps, Is.EqualTo(bareFollowUps), "same Double Shot follow-ups");
            Assert.That(shots, Is.GreaterThanOrEqualTo(Mathf.FloorToInt(20f * (1f + attackSpeed))), "volleys per 20 s");
            Assert.That(_cycle.Shots, Is.EqualTo(shots), "the cycle animated every volley");
            Assert.That(_cycle.Snap, Is.EqualTo(attackSpeed >= 2.5f), "snap only at the very high rate");
        }

        [UnityTest]
        public IEnumerator Volley_Plays_Fire_Then_Reload_And_Seats_A_Bolt()
        {
            yield return null;
            Assert.That(_loadedBolt.activeSelf, Is.True, "loaded before the first shot");
            SpawnEnemy(_player.transform.position + new Vector3(0f, 0f, 6f));
            int shots = 0;
            _shooter.Fired += (_, __) => shots++;
            float t = 0f;
            while (shots == 0 && t < 3f) { yield return null; t += Time.deltaTime; }
            Assert.That(shots, Is.EqualTo(1), "the shooter fired");
            Assert.That(_loadedBolt.activeSelf, Is.False, "the bolt left the rail");
            Assert.That(_cycle.Snap, Is.False, "base rate plays the full reload");
            yield return null;
            Assert.That(StateHash, Is.EqualTo(CrossbowCycle.FireStateHash), "Fire plays on the shot");

            bool sawReload = false, seatedInReload = false;
            float reloadSpeed = 0f;
            t = 0f;
            while (shots == 1 && t < 2f)
            {
                yield return null;
                t += Time.deltaTime;
                var info = _animator.GetCurrentAnimatorStateInfo(_layer);
                if (info.shortNameHash == CrossbowCycle.ReloadStateHash)
                {
                    sawReload = true;
                    reloadSpeed = info.speed * info.speedMultiplier;
                    if (_loadedBolt.activeSelf) seatedInReload = true;
                }
            }
            Assert.That(sawReload, Is.True, "Reload follows Fire");
            Assert.That(reloadSpeed, Is.EqualTo(_cycle.ReloadSpeed).Within(0.01f), "reload plays at the fitted speed");
            Assert.That(_cycle.ReloadSpeed, Is.EqualTo(CrossbowCycle.SpeedFor(_shooter.CurrentInterval, _cycle.FireLength,
                _cycle.ReloadLength, _cycle.Fill, _cycle.SpeedRange)).Within(1e-4f));
            Assert.That(seatedInReload, Is.True, "the bolt is seated during the reload");
            Assert.That(shots, Is.EqualTo(2), "next shot on the timer");
        }

        [UnityTest]
        public IEnumerator HighRate_Plays_The_Snap_Variant()
        {
            AttackSpeed(2.5f); // 3.5 shots per second: no time for the reload clip
            yield return null;
            SpawnEnemy(_player.transform.position + new Vector3(0f, 0f, 6f));
            bool sawReload = false, sawFire = false;
            float t = 0f;
            while (_cycle.Shots < 4 && t < 4f)
            {
                yield return null;
                t += Time.deltaTime;
                int hash = _animator.GetCurrentAnimatorStateInfo(_layer).shortNameHash;
                if (hash == CrossbowCycle.ReloadStateHash) sawReload = true;
                if (hash == CrossbowCycle.FireStateHash) sawFire = true;
            }
            Assert.That(_cycle.Shots, Is.GreaterThanOrEqualTo(4));
            Assert.That(_cycle.Snap, Is.True);
            Assert.That(sawFire, Is.True, "Fire still plays");
            Assert.That(sawReload, Is.False, "no reload clip at this rate");
            yield return Wait(_cycle.FireLength + 0.05f);
            Assert.That(_loadedBolt.activeSelf || _cycle.Shots > 4, Is.True, "the bolt pops back after the fire clip");
        }
    }
}
