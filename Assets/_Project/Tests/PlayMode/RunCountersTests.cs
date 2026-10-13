using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.UI.Hud;
using UnityEngine;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-51 run counters: a kill counts once (only the killing hit, never a hit on a dead enemy), souls = the XP value of
    /// every absorbed wisp (same total as <see cref="SoulWispField.CollectedXp"/>), a disabled counter hears nothing, and
    /// the death screen number format. The portal carry and the new-run reset are in <see cref="ChamberTransitionTests"/>.
    /// </summary>
    public sealed class RunCountersTests
    {
        private const float Frame = 1f / 60f;

        private readonly List<Object> _objects = new List<Object>();
        private SoulWispField _field;
        private RunCounters _counters;

        [SetUp]
        public void SetUp()
        {
            Random.InitState(5151);
            var curve = Track(ScriptableObject.CreateInstance<XpCurve>());
            curve.Configure(100, 1f, 1);
            var xpGo = Track(new GameObject("Progression"));
            xpGo.SetActive(false); // Awake after the curve is set
            var xp = xpGo.AddComponent<XpManager>();
            xp.Setup(curve);
            xpGo.SetActive(true);

            var collector = Track(new GameObject("Player")).AddComponent<SoulCollector>();
            collector.Setup(2.6f, 1f);
            _field = Track(new GameObject("Souls")).AddComponent<SoulWispField>();
            _field.Setup(xp, collector, 128);

            _counters = Track(new GameObject("Chamber")).AddComponent<RunCounters>();
            _counters.Setup(_field);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            GamePause.Clear();
            Time.timeScale = 1f;
            Haptics.ResetState();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private Enemy SpawnEnemy(float x)
        {
            var enemy = Track(new GameObject("Enemy")).AddComponent<Enemy>();
            enemy.Spawn(new Vector3(x, 0f, 5f), null, null);
            return enemy;
        }

        private void Run(float seconds)
        {
            for (float t = 0f; t < seconds; t += Frame) _field.Tick(Frame);
        }

        [Test]
        public void Kills_CountOnlyTheKillingHit()
        {
            var a = SpawnEnemy(0f);
            a.TakeDamage(1f, Vector3.forward);
            Assert.AreEqual(0, _counters.Kills, "a hit that does not kill");
            a.TakeDamage(1e6f, Vector3.forward);
            a.TakeDamage(1e6f, Vector3.forward);
            Assert.AreEqual(1, _counters.Kills, "a dead enemy is not killed again");
            SpawnEnemy(2f).TakeDamage(1e6f, Vector3.forward, DamageKind.Burn);
            Assert.AreEqual(2, _counters.Kills, "burn kills count too");
        }

        [Test]
        public void Souls_SumTheXpOfCollectedWisps()
        {
            _field.Spawn(new Vector3(1.5f, 1f, 0f), 30);
            _field.Spawn(new Vector3(-1f, 1f, 0.5f), 12);
            _field.Spawn(new Vector3(60f, 1f, 60f), 99); // far away: never picked up
            Run(2f);
            Assert.AreEqual(42, _counters.Souls);
            Assert.AreEqual(_field.CollectedXp, _counters.Souls);
        }

        [Test]
        public void Disabled_HearsNothing()
        {
            _counters.enabled = false;
            SpawnEnemy(0f).TakeDamage(1e6f, Vector3.forward);
            _field.Spawn(new Vector3(1.5f, 1f, 0f), 30);
            Run(2f);
            Assert.AreEqual(0, _counters.Kills);
            Assert.AreEqual(0, _counters.Souls);
        }

        [Test]
        public void Restore_ClampsNegativeValues()
        {
            _counters.Restore(-4, -10);
            Assert.AreEqual(0, _counters.Kills);
            Assert.AreEqual(0, _counters.Souls);
            _counters.AddSouls(-5);
            Assert.AreEqual(0, _counters.Souls, "no negative soul gain");
        }

        [Test]
        public void DeathScreen_NumberFormat()
        {
            Assert.AreEqual("0", PlayerHealthHud.FormatCount(0));
            Assert.AreEqual("987", PlayerHealthHud.FormatCount(987));
            Assert.AreEqual("12,480", PlayerHealthHud.FormatCount(12480));
            Assert.AreEqual("0", PlayerHealthHud.FormatCount(-3));
        }
    }
}
