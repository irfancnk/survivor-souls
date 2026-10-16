#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Audio;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.World;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;
using Sound = SurvivorSouls.Audio.ShamanSounds.Sound;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-138 Hexbound Shaman feel on the real built prefab (run the Game scene builder first): a sound for each cast
    /// (wind-up hum, orb whoosh, hex chime, raise crack, blink pops), the hum stops when a blink cuts the cast, rate limit +
    /// voice cap, embers rise off hexed zombies only (not off the Shaman), no GC, Game scene wiring.
    /// The Shaman is stepped by hand like in <see cref="HexShamanTests"/>. Sound events run on the frozen real clock of a
    /// [Test]; tests that pass their own times start from Time.unscaledTime + 100 (KAN-201 order rule).
    /// </summary>
    public sealed class ShamanFeelTests
    {
        private const float Frame = 1f / 60f;
        private const string Dir = "Assets/_Project/Prefabs/Enemies/";
        private const int TestRealm = 95; // never touches the saved progress of a real Realm

        private readonly List<Object> _objects = new List<Object>();
        private PlayerHealth _knight;
        private EnemyProjectiles _shots;
        private EnemyHazardsAndWarnings _pools;
        private HexFx _fx;
        private ShamanSounds _sounds;
        private IWalkability _savedWalk;
        private float _savedLandMaxZ;

        private struct EnemyHazardsAndWarnings
        {
            public EnemyTelegraphs Warnings;
            public GroundHazards Hazards;
            public GroundTelegraph[] Circles;
        }

        [SetUp]
        public void SetUp()
        {
            _savedWalk = Enemy.Walkability;
            Enemy.Walkability = null;
            _savedLandMaxZ = Enemy.LandMaxZ;
            Enemy.LandMaxZ = float.PositiveInfinity;
            Random.InitState(138);

            var knight = Track(new GameObject("Knight"));
            knight.SetActive(false);
            knight.AddComponent<CharacterController>();
            var mover = knight.AddComponent<PlayerMover>();
            _knight = knight.AddComponent<PlayerHealth>();
            knight.SetActive(true);
            mover.enabled = false;

            var pools = Track(new GameObject("Enemy Shots"));
            var warnings = pools.AddComponent<EnemyTelegraphs>();
            var lines = new LineTelegraph[2];
            for (int i = 0; i < lines.Length; i++)
            {
                lines[i] = Track(new GameObject("Lane " + i)).AddComponent<LineTelegraph>();
                lines[i].Setup(new Renderer[3], new Renderer[3]);
            }
            var circles = new GroundTelegraph[8];
            for (int i = 0; i < circles.Length; i++)
            {
                circles[i] = Track(new GameObject("Circle " + i)).AddComponent<GroundTelegraph>();
                circles[i].Setup(null, null);
            }
            warnings.Setup(lines, circles);
            var hazards = pools.AddComponent<GroundHazards>();
            hazards.Setup(_knight, mover);
            _shots = pools.AddComponent<EnemyProjectiles>();
            _shots.Setup(_knight, hazards, warnings);
            _pools = new EnemyHazardsAndWarnings { Warnings = warnings, Hazards = hazards, Circles = circles };

            var moteGo = Track(new GameObject("Hex Motes"));
            var motes = moteGo.AddComponent<ParticleSystem>();
            var main = motes.main;
            main.maxParticles = 500;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = motes.emission;
            emission.enabled = false;
            _fx = pools.AddComponent<HexFx>();
            _fx.Setup(null, null, motes);

            var audio = Track(new GameObject("Audio"));
            audio.SetActive(false);
            _sounds = audio.AddComponent<ShamanSounds>();
            var clips = new AudioClip[5][];
            for (int i = 0; i < clips.Length; i++)
                clips[i] = new[] { Track(AudioClip.Create("shaman test " + i, 44100, 1, 44100, false)) }; // 1 s
            _sounds.Setup(clips);
            audio.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            Enemy.Active.Clear();
            HexShaman.Active.Clear();
            Enemy.Walkability = _savedWalk;
            Enemy.LandMaxZ = _savedLandMaxZ;
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private Enemy Spawn(string id, Vector3 position)
        {
            var asset = AssetDatabase.LoadAssetAtPath<Enemy>(Dir + "Enemy_" + id + ".prefab");
            Assert.That(asset, Is.Not.Null, id);
            var enemy = Object.Instantiate(asset);
            Track(enemy.gameObject);
            enemy.gameObject.SetActive(false);
            enemy.Spawn(position, _knight.transform, _ => { });
            return enemy;
        }

        private HexShaman SpawnShaman(Vector3 position) => Spawn("HexboundShaman", position).GetComponent<HexShaman>();

        private void Run(float seconds, HexShaman shaman)
        {
            for (float t = 0f; t < seconds - 1e-4f; t += Frame)
            {
                shaman.Tick(Frame);
                _shots.Tick(Frame);
                _pools.Hazards.Tick(Frame);
                foreach (var c in _pools.Circles) c.Tick(Frame);
            }
        }

        private void PlaceKnight(Vector3 p) => _knight.transform.position = p;

        // ------------------------------------------------------------ Sounds

        [Test]
        public void OrbRing_HumAtTheWindUp_WhooshAtTheRelease()
        {
            var shaman = SpawnShaman(Vector3.zero);
            PlaceKnight(new Vector3(0f, 0f, 9f));
            Assert.That(shaman.ForceCast(HexShaman.Cast.Orbs), Is.True);
            Assert.That(_sounds.Played(Sound.Hum), Is.EqualTo(1), "hum at the wind-up start");
            Assert.That(_sounds.Humming(shaman, Time.unscaledTime), Is.True);
            Assert.That(_sounds.Played(Sound.Whoosh), Is.Zero, "nothing leaves before the release");
            Run(shaman.Config.orbWindUp + Frame, shaman);
            Assert.That(_sounds.Played(Sound.Whoosh), Is.EqualTo(1), "whoosh when the ring leaves");
            Assert.That(_sounds.HumsCut, Is.Zero, "a released cast keeps its hum tail");
        }

        [Test]
        public void Hex_ChimesWhenItLands()
        {
            var shaman = SpawnShaman(Vector3.zero);
            Spawn("GraveyardLaborer", new Vector3(2f, 0f, 0f));
            Spawn("GraveyardLaborer", new Vector3(-2f, 0f, 1f));
            PlaceKnight(new Vector3(0f, 0f, 9f));
            Assert.That(shaman.ForceCast(HexShaman.Cast.Hex), Is.True);
            Assert.That(shaman.CurrentCast, Is.EqualTo(HexShaman.Cast.Hex));
            Run(shaman.Config.castWindUp + Frame, shaman);
            Assert.That(_sounds.Played(Sound.Chime), Is.EqualTo(1));
            Assert.That(_sounds.Played(Sound.Crack), Is.Zero);
        }

        [Test]
        public void Raise_CracksTheGround()
        {
            var spawner = Track(new GameObject("Spawner")).AddComponent<EnemySpawner>();
            Assert.That(EnemySpawner.Current, Is.EqualTo(spawner));
            var shaman = SpawnShaman(Vector3.zero);
            PlaceKnight(new Vector3(0f, 0f, 9f));
            Assert.That(shaman.ForceCast(HexShaman.Cast.Raise), Is.True);
            Run(shaman.Config.castWindUp + Frame, shaman);
            Assert.That(_sounds.Played(Sound.Crack), Is.EqualTo(1));
            Assert.That(_sounds.Played(Sound.Chime), Is.Zero);
        }

        [Test]
        public void Blink_CutsTheHum_PopsWhereItVanishes()
        {
            var shaman = SpawnShaman(Vector3.zero);
            PlaceKnight(new Vector3(0f, 0f, 9f));
            Assert.That(shaman.ForceCast(HexShaman.Cast.Orbs), Is.True);
            Run(0.2f, shaman);
            Assert.That(_sounds.Humming(shaman, Time.unscaledTime), Is.True);
            PlaceKnight(new Vector3(0f, 0f, 2f)); // inside the blink trigger
            shaman.Tick(Frame);
            Assert.That(shaman.CurrentPhase, Is.EqualTo(HexShaman.Phase.Vanish));
            Assert.That(_sounds.HumsCut, Is.EqualTo(1), "the dropped cast goes silent");
            Assert.That(_sounds.Humming(shaman, Time.unscaledTime), Is.False);
            Assert.That(_sounds.Played(Sound.Pop), Is.EqualTo(1), "pop where it vanishes");
            Run(1f, shaman);
            Assert.That(_sounds.Played(Sound.Whoosh), Is.Zero, "the dropped ring never fires");
            // The reappear pop lands 0.25 s later on the real clock; inside a [Test] the clock stands still, so the rate
            // limit (0.06 s) drops it here: both pops are checked through Play below.
            Assert.That(_sounds.Played(Sound.Pop) + _sounds.Dropped(Sound.Pop), Is.EqualTo(2), "a pop at both ends of the blink");
        }

        [Test]
        public void Play_RateLimit_VoiceCap_Distance()
        {
            float t = Time.unscaledTime + 100f;
            PlaceKnight(Vector3.zero);
            var at = new Vector3(0f, 0f, 9f);
            Assert.That(_sounds.Voices(Sound.Whoosh), Is.EqualTo(2));
            Assert.That(_sounds.Play(Sound.Whoosh, at, t), Is.True);
            Assert.That(_sounds.Play(Sound.Whoosh, at, t + 0.05f), Is.False, "rate limit");
            Assert.That(_sounds.Play(Sound.Whoosh, at, t + 0.2f), Is.True, "second voice");
            Assert.That(_sounds.Play(Sound.Whoosh, at, t + 0.4f), Is.False, "both voices busy (1 s clips)");
            Assert.That(_sounds.Play(Sound.Whoosh, at, t + 1.3f), Is.True, "a voice is free again");
            Assert.That(_sounds.Proximity(at), Is.EqualTo(1f), "the hold band (8-11 m) plays at full volume");
            Assert.That(_sounds.Proximity(new Vector3(0f, 0f, 30f)), Is.Zero, "far off: silent");
            Assert.That(_sounds.Play(Sound.Chime, new Vector3(0f, 0f, 30f), t + 5f), Is.False);
        }

        // ------------------------------------------------------------ Hexed embers

        [Test]
        public void Motes_RiseOffHexedZombiesOnly()
        {
            var shaman = SpawnShaman(Vector3.zero);
            var hexed = Spawn("GraveyardLaborer", new Vector3(2f, 0f, 0f));
            Spawn("GraveyardLaborer", new Vector3(-6f, 0f, 0f));
            PlaceKnight(new Vector3(0f, 0f, 20f));
            for (int i = 0; i < 60; i++) _fx.EmitMotes(Frame);
            Assert.That(_fx.Motes, Is.Zero, "no hex: no embers (not off the Shaman either)");

            Assert.That(hexed.ApplyHex(1.3f, 5f), Is.True);
            for (int i = 0; i < 120; i++) _fx.EmitMotes(Frame);
            Assert.That(_fx.HexedCount, Is.EqualTo(1));
            Assert.That(_fx.Motes, Is.InRange(10, 28), "about 9 per second for 2 s");
            Assert.That(_fx.MoteSystem.particleCount, Is.GreaterThan(0));
            var particles = new ParticleSystem.Particle[_fx.MoteSystem.particleCount];
            int n = _fx.MoteSystem.GetParticles(particles);
            for (int i = 0; i < n; i++)
            {
                Vector3 p = particles[i].position;
                Assert.That(new Vector2(p.x - 2f, p.z).magnitude, Is.LessThan(hexed.HitRadius + 0.5f), "born on the hexed body");
                Assert.That(particles[i].velocity.y, Is.GreaterThan(0f), "rising");
            }
            int before = _fx.Motes;
            _fx.EmitMotes(0f);
            Assert.That(_fx.Motes, Is.EqualTo(before), "paused: none");
            Assert.That(shaman.Enemy.IsHexed, Is.False);
        }

        [Test]
        public void Hex_SkipsOtherShamans_SoOnlyZombiesShedEmbers()
        {
            var caster = SpawnShaman(Vector3.zero);
            var other = SpawnShaman(new Vector3(0f, 0f, -2f));
            var a = Spawn("GraveyardLaborer", new Vector3(2f, 0f, 0f));
            var b = Spawn("GraveyardLaborer", new Vector3(-2f, 0f, 1f));
            PlaceKnight(new Vector3(0f, 0f, 9f));
            Assert.That(HexShaman.IsCaster(other.Enemy), Is.True);
            Assert.That(HexShaman.IsCaster(a), Is.False);
            Assert.That(caster.HexTargets(), Is.EqualTo(2), "the other Shaman does not count");
            Assert.That(caster.ForceCast(HexShaman.Cast.Hex), Is.True);
            Run(caster.Config.castWindUp + Frame, caster);
            Assert.That(a.IsHexed && b.IsHexed, Is.True);
            Assert.That(other.Enemy.IsHexed, Is.False, "Shamans never hex each other");
            _fx.EmitMotes(Frame);
            Assert.That(_fx.HexedCount, Is.EqualTo(2));
        }

        [Test]
        public void SoundsAndMotes_NoGc()
        {
            var shaman = SpawnShaman(Vector3.zero);
            var zombie = Spawn("GraveyardLaborer", new Vector3(2f, 0f, 0f));
            Spawn("GraveyardLaborer", new Vector3(-2f, 0f, 0f));
            PlaceKnight(new Vector3(0f, 0f, 9f));
            shaman.ForceCast(HexShaman.Cast.Hex);
            Run(1.5f, shaman); // warm-up
            zombie.ApplyHex(1.3f, 5f);
            _fx.EmitMotes(Frame);
            float t = Time.unscaledTime + 100f;
            _sounds.Play(Sound.Pop, Vector3.zero, t);

            Assert.That(() =>
            {
                shaman.ForceCast(HexShaman.Cast.Orbs);
                Run(1.3f, shaman);
                for (int i = 0; i < 30; i++) _fx.EmitMotes(Frame);
                _sounds.Play(Sound.Pop, Vector3.zero, t + 1f);
                _sounds.StopHum(shaman, t + 1f);
            }, Is.Not.AllocatingGCMemory(), "cast sounds, embers");
        }

        // ------------------------------------------------------------ Scene

        [UnityTest]
        public IEnumerator GameScene_ShamanSoundsAndEmbersWired()
        {
            TearDown(); // this test uses the real scene's objects

            RunState.NewRun(TestRealm, 1381);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;

            var sounds = Object.FindAnyObjectByType<ShamanSounds>();
            Assert.That(sounds, Is.Not.Null, "ShamanSounds on the audio object");
            Assert.That(sounds.Voices(Sound.Hum), Is.EqualTo(2));
            Assert.That(HexFx.Current, Is.Not.Null);
            Assert.That(HexFx.Current.MoteSystem, Is.Not.Null, "Hex Motes wired");

            var shots = EnemyProjectiles.Current;
            var spawner = EnemySpawner.Current;
            var knight = shots.Player;
            knight.Invulnerable = true;
            foreach (var shooter in Object.FindObjectsByType<AutoShooter>()) shooter.enabled = false;
            var enemy = spawner.SpawnAt(spawner.FindType("Enemy_HexboundShaman"), knight.transform.position + new Vector3(0f, 0f, -9f));
            var shaman = enemy.GetComponent<HexShaman>();
            float limit = 12f;
            while (sounds.Played(Sound.Whoosh) == 0 && limit > 0f)
            {
                limit -= Time.deltaTime;
                yield return null;
            }
            Assert.That(sounds.Played(Sound.Hum), Is.GreaterThan(0), "hum heard in the scene (clips wired)");
            Assert.That(sounds.Played(Sound.Whoosh), Is.GreaterThan(0), "whoosh heard in the scene");

            var zombie = spawner.SpawnAt(spawner.FindType("Enemy_GraveyardLaborer") ?? spawner.FindType("Enemy_HexboundShaman"),
                knight.transform.position + new Vector3(3f, 0f, -9f));
            if (zombie != null && zombie != enemy && zombie.ApplyHex(1.3f, 5f))
            {
                int before = HexFx.Current.Motes;
                for (int i = 0; i < 20; i++) yield return null;
                Assert.That(HexFx.Current.Motes, Is.GreaterThan(before), "embers off the hexed zombie");
            }
            enemy.TakeDamage(1e6f, Vector3.zero);
            yield return null;
            Assert.That(shaman.CurrentPhase, Is.EqualTo(HexShaman.Phase.Idle), "a kill ends the cast");

            var empty = SceneManager.CreateScene("ShamanFeelEmpty");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
            GamePause.Clear();
            Time.timeScale = 1f;
        }
    }
}
#endif
