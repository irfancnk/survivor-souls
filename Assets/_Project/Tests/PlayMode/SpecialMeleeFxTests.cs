#if UNITY_EDITOR
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Audio;
using SurvivorSouls.Gameplay.Boss;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.World;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;
using Sound = SurvivorSouls.Audio.SpecialMeleeSounds.Sound;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-132 special melee feel on the real built prefabs (run the Game scene builder first): the burst Bloater vanishes
    /// (no Death clip after the burst), wind-up + strike sounds per attack, voice cap + rate limit + distance fade, toxic
    /// cloud hiss fades in / out, Brute slam crack + shake near the Knight only + dust on the wave front + ring band as wide
    /// as the hit band, Ghoul dazed sway in the stun. Attacks are stepped by hand (no Update inside a [Test]).
    /// </summary>
    public sealed class SpecialMeleeFxTests
    {
        private const float Frame = 1f / 60f;
        private const string Dir = "Assets/_Project/Prefabs/Enemies/";

        private readonly List<Object> _objects = new List<Object>();
        private readonly List<Enemy> _released = new List<Enemy>();
        private PlayerHealth _knight;
        private GroundHazards _hazards;
        private GroundTelegraph[] _circles;
        private LineTelegraph[] _lines;
        private SpecialMeleeFx _fx;
        private SpecialMeleeSounds _sounds;
        private IWalkability _savedWalk;
        private float _savedLandMaxZ;

        [SetUp]
        public void SetUp()
        {
            _savedWalk = Enemy.Walkability;
            Enemy.Walkability = null;
            _savedLandMaxZ = Enemy.LandMaxZ;
            Enemy.LandMaxZ = float.PositiveInfinity;
            Random.InitState(132);
            _released.Clear();

            var knight = Track(new GameObject("Knight"));
            knight.SetActive(false);
            knight.AddComponent<CharacterController>();
            var mover = knight.AddComponent<PlayerMover>();
            _knight = knight.AddComponent<PlayerHealth>();
            knight.SetActive(true);
            mover.enabled = false;

            var pools = Track(new GameObject("Enemy Shots"));
            var warnings = pools.AddComponent<EnemyTelegraphs>();
            _lines = new LineTelegraph[4];
            for (int i = 0; i < _lines.Length; i++)
            {
                _lines[i] = Track(new GameObject("Lane " + i)).AddComponent<LineTelegraph>();
                _lines[i].Setup(new Renderer[3], new Renderer[3]);
            }
            _circles = new GroundTelegraph[8];
            for (int i = 0; i < _circles.Length; i++)
            {
                _circles[i] = Track(new GameObject("Circle " + i)).AddComponent<GroundTelegraph>();
                _circles[i].Setup(null, null);
            }
            warnings.Setup(_lines, _circles);
            _hazards = pools.AddComponent<GroundHazards>();
            _hazards.Setup(_knight, mover);
            pools.AddComponent<EnemyProjectiles>().Setup(_knight, _hazards, warnings);

            _fx = Track(new GameObject("Special Melee FX")).AddComponent<SpecialMeleeFx>();
            _fx.Setup(System("Spores"), System("Puffs"), System("Dust"), System("Cracks"), null);

            var audio = Track(new GameObject("Audio"));
            audio.SetActive(false);
            _sounds = audio.AddComponent<SpecialMeleeSounds>();
            var clips = new AudioClip[7][];
            for (int i = 0; i < clips.Length; i++) clips[i] = new[] { Clip("melee" + i, 1f) };
            _sounds.Setup(clips, Clip("hiss", 2f));
            audio.SetActive(true);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            Enemy.Active.Clear();
            Enemy.Walkability = _savedWalk;
            Enemy.LandMaxZ = _savedLandMaxZ;
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private ParticleSystem System(string name)
        {
            var go = Track(new GameObject(name));
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 500;
            var emission = ps.emission;
            emission.enabled = false;
            return ps;
        }

        private AudioClip Clip(string name, float seconds) => Track(AudioClip.Create(name, (int)(44100 * seconds), 1, 44100, false));

        private Enemy Spawn(string id, Vector3 position)
        {
            var asset = AssetDatabase.LoadAssetAtPath<Enemy>(Dir + "Enemy_" + id + ".prefab");
            Assert.That(asset, Is.Not.Null, id);
            var enemy = Object.Instantiate(asset);
            Track(enemy.gameObject);
            enemy.gameObject.SetActive(false);
            enemy.Spawn(position, _knight.transform, e => _released.Add(e));
            return enemy;
        }

        private void PlaceKnight(Vector3 p) => _knight.transform.position = p;

        private void Run(float seconds, params SpecialAttack[] attacks)
        {
            for (float t = 0f; t < seconds - 1e-4f; t += Frame)
            {
                foreach (var a in attacks) a.Tick(Frame);
                _hazards.Tick(Frame);
                foreach (var c in _circles) c.Tick(Frame);
                foreach (var l in _lines) l.Tick(Frame);
                _fx.FollowFronts(Frame);
                _sounds.TickHiss(Frame, Frame);
            }
        }

        private void RunUntil(SpecialAttack attack, SpecialAttack.Phase phase, float limit = 3f)
        {
            for (float t = 0f; attack.CurrentPhase != phase && t < limit; t += Frame) Run(Frame, attack);
        }

        // ------------------------------------------------------------ Plague Bloater

        [Test]
        public void Bloater_OwnBurst_BodyVanishes_SporesAndSounds()
        {
            var bloater = Spawn("PlagueBloater", Vector3.zero);
            var burst = bloater.GetComponent<BloaterBurst>();
            PlaceKnight(new Vector3(0f, 0f, 1.8f));
            RunUntil(burst, SpecialAttack.Phase.WindUp);
            Assert.That(_sounds.Played(Sound.BloaterSwell), Is.EqualTo(1), "swell gurgle at the wind-up");
            Run(1.05f, burst);
            Assert.That(burst.HasBurst, Is.True);
            Assert.That(_sounds.Played(Sound.BloaterBurst), Is.EqualTo(1), "pop + splash at the burst");
            Assert.That(_fx.Bursts, Is.EqualTo(1));
            Assert.That(_fx.Shakes, Is.EqualTo(1), "Knight inside the circle: small shake");
            Assert.That(bloater.gameObject.activeSelf, Is.False, "the body vanishes at the burst: no Death clip after it");
            Assert.That(_released, Has.Member(bloater), "back to its pool at once");
        }

        [Test]
        public void Bloater_KilledCorpse_VanishesAtItsFuseBurst()
        {
            var bloater = Spawn("PlagueBloater", Vector3.zero);
            var burst = bloater.GetComponent<BloaterBurst>();
            PlaceKnight(new Vector3(0f, 0f, 8f));
            bloater.TakeDamage(bloater.MaxHealth * 2f, Vector3.forward);
            Run(0.3f, burst);
            Assert.That(bloater.gameObject.activeSelf, Is.True, "the corpse waits for its fuse");
            Run(0.4f, burst);
            Assert.That(burst.HasBurst, Is.True);
            Assert.That(bloater.gameObject.activeSelf, Is.False, "gone in the burst");
            Assert.That(_fx.Shakes, Is.EqualTo(0), "Knight outside the circle: no shake");
        }

        [Test]
        public void ToxicCloud_HissFadesIn_AndOutWhenPausedOrGone()
        {
            var bloater = Spawn("PlagueBloater", Vector3.zero);
            var burst = bloater.GetComponent<BloaterBurst>();
            PlaceKnight(new Vector3(0f, 0f, 1.8f));
            RunUntil(burst, SpecialAttack.Phase.WindUp);
            Run(1.05f, burst);
            Assert.That(_hazards.Count, Is.EqualTo(1));
            Run(1f, burst);
            Assert.That(_sounds.Clouds, Is.EqualTo(1));
            Assert.That(_sounds.HissLevel, Is.GreaterThan(0.3f), "hiss fades in while the cloud lives");
            for (int i = 0; i < 300; i++) _sounds.TickHiss(0f, Frame); // paused (level-up screen)
            Assert.That(_sounds.HissLevel, Is.EqualTo(0f), "fades out while paused");
            Run(6f, burst);
            Assert.That(_hazards.Count, Is.EqualTo(0));
            Assert.That(_sounds.Clouds, Is.EqualTo(0));
            Run(2.1f, burst); // 0.5 per second fade
            Assert.That(_sounds.HissLevel, Is.EqualTo(0f), "silent once the cloud is gone (faded out)");
        }

        // ------------------------------------------------------------ Frenzied Ghoul

        [Test]
        public void Ghoul_ScreechWhooshThud_DustAndDazedSway()
        {
            var ghoul = Spawn("FrenziedGhoul", Vector3.zero);
            var leap = ghoul.GetComponent<GhoulLeap>();
            PlaceKnight(new Vector3(0f, 0f, 4.5f));
            Assert.That(leap.ForceAttack(), Is.True);
            Assert.That(_sounds.Played(Sound.GhoulScreech), Is.EqualTo(1), "screech at the crouch");
            RunUntil(leap, SpecialAttack.Phase.Strike);
            Assert.That(_sounds.Played(Sound.GhoulWhoosh), Is.EqualTo(1), "whoosh at the take-off");
            RunUntil(leap, SpecialAttack.Phase.Recover);
            Assert.That(_sounds.Played(Sound.GhoulThud), Is.EqualTo(1), "thud on the landing");
            Assert.That(_fx.Landings, Is.EqualTo(1));

            var straight = Quaternion.LookRotation(leap.LeapDirection);
            Run(0.1f, leap);
            Assert.That(leap.IsStunned, Is.True);
            Assert.That(Quaternion.Angle(ghoul.transform.rotation, straight), Is.GreaterThan(1f), "sways dazed in the stun");
            RunUntil(leap, SpecialAttack.Phase.Idle);
            Assert.That(Quaternion.Angle(ghoul.transform.rotation, straight), Is.LessThan(0.5f), "stands straight after the stun");
        }

        // ------------------------------------------------------------ Grave Brute

        [Test]
        public void Brute_Slam_GruntBoomCrackShake_DustOnTheFront_WideBand()
        {
            var brute = Spawn("GraveBrute", Vector3.zero);
            var slam = brute.GetComponent<BruteSlam>();
            PlaceKnight(new Vector3(0f, 0f, 2f));
            Assert.That(slam.ForceAttack(), Is.True);
            Assert.That(_sounds.Played(Sound.BruteGrunt), Is.EqualTo(1), "grunt at the wind-up");
            RunUntil(slam, SpecialAttack.Phase.Strike);
            Assert.That(_sounds.Played(Sound.BruteSlam), Is.EqualTo(1), "boom at the slam");
            Assert.That(_fx.Slams, Is.EqualTo(1), "crack decal");
            Assert.That(_fx.Shakes, Is.EqualTo(1), "Knight within 6 m: shake");
            Assert.That(_fx.LastShake, Is.GreaterThan(0.08f));
            Assert.That(_fx.TrackedSlams, Is.EqualTo(1));

            Run(0.3f, slam);
            Assert.That(_fx.RingDust, Is.GreaterThan(0), "dust follows the wave front");
            Assert.That(slam.Wave, Is.Not.Null);
            Assert.That(slam.Wave.WaveBandWidth, Is.EqualTo(slam.Config.width).Within(0.01f), "ring band looks as wide as the hit band");
            RunUntil(slam, SpecialAttack.Phase.Recover);
            _fx.FollowFronts(Frame);
            Assert.That(_fx.TrackedSlams, Is.EqualTo(0), "ring over: dust stops");
        }

        [Test]
        public void Brute_Slam_FarKnight_NoShake()
        {
            var brute = Spawn("GraveBrute", Vector3.zero);
            var slam = brute.GetComponent<BruteSlam>();
            PlaceKnight(new Vector3(0f, 0f, 9f));
            Assert.That(slam.ForceAttack(), Is.True);
            RunUntil(slam, SpecialAttack.Phase.Strike);
            Assert.That(_fx.Slams, Is.EqualTo(1));
            Assert.That(_fx.Shakes, Is.EqualTo(0), "Knight 9 m away: no shake");
        }

        [Test]
        public void WaveQuad_DefaultKeepsTheTextureBand_WidthGivesMetres()
        {
            float half = GroundTelegraph.WaveQuadHalf(4.1f, 0f, out float rim, out float stretch);
            Assert.That(half, Is.EqualTo(4.1f / GroundTelegraph.RimShare).Within(1e-4f), "no band: as before (KAN-121)");
            Assert.That(rim, Is.EqualTo(GroundTelegraph.RimShare));
            Assert.That(stretch, Is.EqualTo(1f));
            foreach (float r in new[] { 0.05f, 0.5f, 2f, 4.1f })
            {
                half = GroundTelegraph.WaveQuadHalf(r, 0.8f, out rim, out stretch);
                float tex = GroundTelegraph.WaveBandInner + GroundTelegraph.WaveBandOuter;
                Assert.That(tex / stretch * half, Is.EqualTo(0.8f).Within(1e-4f), $"band 0.8 m at r {r}");
                Assert.That(rim * half, Is.EqualTo(r).Within(1e-4f), "front on the radius");
                Assert.That(rim + GroundTelegraph.WaveBandOuter / stretch, Is.LessThanOrEqualTo(0.981f), "outer band edge inside the quad");
            }
        }

        // ------------------------------------------------------------ Voices

        [Test]
        public void Sounds_VoiceCap_RateLimit_DistanceFade()
        {
            Vector3 near = _knight.transform.position + new Vector3(2f, 0f, 0f);
            _sounds.Play(Sound.BruteGrunt, near, Time.unscaledTime); // real clock first (KAN-201 order rule)
            float t0 = Time.unscaledTime + 100f;
            Assert.That(_sounds.Voices(Sound.BloaterBurst), Is.EqualTo(3));
            Assert.That(_sounds.Play(Sound.BloaterBurst, near, t0), Is.True);
            Assert.That(_sounds.Play(Sound.BloaterBurst, near, t0 + 0.02f), Is.False, "rate limit: 0.06 s");
            Assert.That(_sounds.Play(Sound.BloaterBurst, near, t0 + 0.1f), Is.True);
            Assert.That(_sounds.Play(Sound.BloaterBurst, near, t0 + 0.2f), Is.True);
            Assert.That(_sounds.Play(Sound.BloaterBurst, near, t0 + 0.3f), Is.False, "3 voices busy (1 s clips)");
            Assert.That(_sounds.Play(Sound.BloaterBurst, near, t0 + 1.5f), Is.True, "a voice is free again");
            Assert.That(_sounds.Played(Sound.BloaterBurst), Is.EqualTo(4));
            Assert.That(_sounds.Dropped(Sound.BloaterBurst), Is.EqualTo(2));

            Vector3 far = _knight.transform.position + new Vector3(25f, 0f, 0f);
            Assert.That(_sounds.Proximity(far), Is.EqualTo(0f));
            Assert.That(_sounds.Play(Sound.BruteSlam, far, t0), Is.False, "too far from the Knight: silent");
            Assert.That(_sounds.Proximity(_knight.transform.position + new Vector3(13f, 0f, 0f)), Is.InRange(0.05f, 0.95f), "fades between");
        }
    }
}
#endif
