using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Audio;
using SurvivorSouls.Gameplay.Characters;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Foot = SurvivorSouls.Audio.FootstepSounds.Foot;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-235 (KAN-81.2) Knight footsteps: one step per foot plant (lower foot change), alternating feet, none while
    /// standing or from small jitter, left / right clip tone (even / odd index) with no repeat, walk quieter than run and
    /// about half the old level, fight ducking down and back, surface from the tag under the Knight (fallback snow, empty
    /// set = snow), stride timer without foot bones, a teleport is no run, feet found again after a hero swap, no GC.
    /// Frames are stepped by hand (<see cref="FootstepSounds.Advance"/>), so the [Test] clock does not matter.
    /// </summary>
    public sealed class FootstepSoundsTests
    {
        private const float Dt = 1f / 60f;
        private const float OldLevel = 0.35f; // KAN-15 footstep volume

        private readonly List<Object> _objects = new List<Object>();
        private FootstepSounds _sounds;
        private Transform _body, _footL, _footR;
        private AudioClip[] _snow, _shingle, _wood, _stone;
        private float _t;

        [SetUp]
        public void SetUp()
        {
            Random.InitState(235);
            _snow = Clips("snow", 8);
            _shingle = Clips("shingle", 6);
            _wood = Clips("wood", 6);
            _stone = Clips("stone", 6);
            _t = 0f;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private AudioClip[] Clips(string name, int count)
        {
            var clips = new AudioClip[count];
            for (int i = 0; i < count; i++) clips[i] = Track(AudioClip.Create($"step {name} {i}", 4410, 1, 44100, false));
            return clips;
        }

        /// <summary>Knight body (with two feet unless <paramref name="feet"/> is false) and the component, wired, enabled.</summary>
        private FootstepSounds Make(bool feet = true, AudioClip[] shingle = null)
        {
            _body = Track(new GameObject("Knight")).transform;
            _body.position = new Vector3(0f, 0f, 0f);
            if (feet)
            {
                _footL = new GameObject("footL").transform;
                _footL.SetParent(_body, false);
                _footR = new GameObject("footR").transform;
                _footR.SetParent(_body, false);
            }
            var go = Track(new GameObject("Audio"));
            go.SetActive(false);
            _sounds = go.AddComponent<FootstepSounds>();
            _sounds.Setup(_snow, shingle ?? _shingle, _wood, _stone, _body, _footL, _footR);
            go.SetActive(true);
            return _sounds;
        }

        /// <summary>
        /// Runs <paramref name="seconds"/> at <paramref name="speed"/> m/s along +X; the feet swing up and down in opposite
        /// phase at <paramref name="cycleHz"/> gait cycles per second (2 plants per cycle). Returns the feet of each step.
        /// </summary>
        private List<Foot> Run(float seconds, float speed, float cycleHz = 1.5f, float lift = 0.08f, System.Action perFrame = null)
        {
            var feet = new List<Foot>();
            int frames = Mathf.RoundToInt(seconds / Dt);
            for (int i = 0; i < frames; i++)
            {
                _t += Dt;
                _body.position += Vector3.right * speed * Dt;
                if (_footL != null)
                {
                    float s = Mathf.Sin(_t * cycleHz * Mathf.PI * 2f);
                    _footL.localPosition = new Vector3(-0.12f, 0.1f + lift * s, 0f);
                    _footR.localPosition = new Vector3(0.12f, 0.1f - lift * s, 0f);
                }
                perFrame?.Invoke();
                int before = _sounds.Steps;
                _sounds.Advance(Dt);
                if (_sounds.Steps > before) feet.Add(_sounds.LastFoot);
            }
            return feet;
        }

        private static void AssertAlternates(List<Foot> feet)
        {
            for (int i = 1; i < feet.Count; i++) Assert.That(feet[i], Is.Not.EqualTo(feet[i - 1]), $"step {i} alternates feet");
        }

        // ------------------------------------------------------------ Timing

        [Test]
        public void Running_OneStepPerFootPlant_Alternating()
        {
            Make();
            var feet = Run(4f, 5f, cycleHz: 1.5f);
            // 1.5 cycles/s x 2 plants x 4 s = 12 crossings; the first one only learns the lower foot.
            Assert.That(feet.Count, Is.InRange(10, 12), "one step per plant");
            Assert.That(Mathf.Abs(_sounds.LeftSteps - _sounds.RightSteps), Is.LessThanOrEqualTo(1), "both feet");
            AssertAlternates(feet);
            Assert.That(_sounds.UsingStrideTimer, Is.False, "rig feet drive the steps");
        }

        [Test]
        public void StandingStill_OrFootJitter_NoSteps()
        {
            Make();
            Assert.That(Run(2f, 0f).Count, Is.Zero, "standing: aim sway / turn steps make no footsteps");
            Assert.That(Run(2f, 5f, cycleHz: 3f, lift: 0.005f).Count, Is.Zero, "feet 1 cm apart: below the hysteresis");
        }

        [Test]
        public void NoFootBones_StrideTimer_StepsAlternate()
        {
            Make(feet: false);
            Assert.That(_sounds.UsingStrideTimer, Is.True);
            var feet = Run(3f, 5f);
            // 15 m / 1.2 m per step = 12.5, the first comes after 0.36 m.
            Assert.That(feet.Count, Is.InRange(12, 13));
            AssertAlternates(feet);
            Assert.That(Run(1f, 0f).Count, Is.Zero, "standing");
        }

        [Test]
        public void Teleport_IsNotARun()
        {
            Make();
            Run(0.5f, 0f);
            _body.position += new Vector3(30f, 0f, 0f); // portal / Chamber start
            _sounds.Advance(Dt);
            Assert.That(_sounds.Speed, Is.Zero);
        }

        [Test]
        public void HeroSwap_FeetFoundAgainOnTheActiveRig()
        {
            Make();
            Run(1f, 5f);
            int before = _sounds.Steps;

            // HeroLoadout hides the Knight model and adds the hero's own model under the body.
            _footL.gameObject.SetActive(false);
            _footR.gameObject.SetActive(false);
            var model = new GameObject("Hero Model");
            model.SetActive(false);
            model.transform.SetParent(_body, false);
            var rig = model.AddComponent<HumanoidRig>();
            rig.hips = model.transform;
            rig.footL = new GameObject("heroFootL").transform;
            rig.footL.SetParent(model.transform, false);
            rig.footR = new GameObject("heroFootR").transform;
            rig.footR.SetParent(model.transform, false);
            model.SetActive(true);
            _footL = rig.footL;
            _footR = rig.footR;

            var feet = Run(3f, 5f);
            Assert.That(_sounds.FootL, Is.SameAs(rig.footL), "left foot of the hero rig");
            Assert.That(_sounds.FootR, Is.SameAs(rig.footR));
            Assert.That(feet.Count, Is.GreaterThanOrEqualTo(6), "steps go on with the hero's feet");
            Assert.That(_sounds.Steps, Is.GreaterThan(before));
        }

        // ------------------------------------------------------------ Clips

        [Test]
        public void Clips_FootTone_ByParity_NoRepeatPerFoot()
        {
            Make();
            int[] last = { -1, -1 };
            int frames = Mathf.RoundToInt(20f / Dt);
            for (int i = 0; i < frames; i++)
            {
                int before = _sounds.Steps;
                Run(Dt, 5f, cycleHz: 2f);
                if (_sounds.Steps == before) continue;
                int foot = (int)_sounds.LastFoot, index = _sounds.LastIndex;
                Assert.That(index % 2, Is.EqualTo(foot), $"foot {_sounds.LastFoot} uses its own tone (index {index})");
                Assert.That(index, Is.Not.EqualTo(last[foot]), "never that foot's last clip");
                Assert.That(_sounds.LastClip, Is.SameAs(_snow[index]));
                last[foot] = index;
            }
            Assert.That(_sounds.Steps, Is.GreaterThan(60));

            for (int i = 0; i < 200; i++)
            {
                Assert.That(FootstepSounds.PickIndex(7, 1, 3) % 2, Is.EqualTo(1));
                Assert.That(FootstepSounds.PickIndex(7, 1, 3), Is.Not.EqualTo(3));
                Assert.That(FootstepSounds.PickIndex(1, 1, 0), Is.Zero, "a set of 1 is shared");
            }
        }

        // ------------------------------------------------------------ Mix

        [Test]
        public void Walk_QuieterThanRun_RunAboutHalfTheOldLevel()
        {
            Make();
            var walk = new List<float>();
            var run = new List<float>();
            for (int i = 0; i < Mathf.RoundToInt(6f / Dt); i++)
            {
                int before = _sounds.Steps;
                Run(Dt, 1.4f, cycleHz: 0.9f);
                if (_sounds.Steps > before) walk.Add(_sounds.LastLevel);
            }
            for (int i = 0; i < Mathf.RoundToInt(6f / Dt); i++)
            {
                int before = _sounds.Steps;
                Run(Dt, 5f, cycleHz: 1.5f);
                if (_sounds.Steps > before) run.Add(_sounds.LastLevel);
            }
            Assert.That(walk.Count, Is.GreaterThan(5));
            Assert.That(run.Count, Is.GreaterThan(10));
            float walkMax = Mathf.Max(walk.ToArray()), runMin = Mathf.Min(run.ToArray()), runMax = Mathf.Max(run.ToArray());
            Assert.That(walkMax, Is.LessThan(runMin), "walk quieter than run");
            Assert.That(runMax, Is.InRange(OldLevel * 0.4f, OldLevel * 0.6f), "run about half the old level");
        }

        [Test]
        public void HeavyFight_DucksSteps_ThenComesBack()
        {
            Make();
            Run(1f, 5f);
            float calm = _sounds.LastLevel;
            Assert.That(_sounds.Duck, Is.EqualTo(1f).Within(1e-4f));

            // 30 fight events per second (shots + hits) for 2 s.
            var ducked = new List<float>();
            for (int i = 0; i < Mathf.RoundToInt(2f / Dt); i++)
            {
                int before = _sounds.Steps;
                Run(Dt, 5f, perFrame: () => _sounds.AddFightActivity(30f * Dt));
                if (_sounds.Steps > before && i > 30) ducked.Add(_sounds.LastLevel);
            }
            Assert.That(_sounds.FightRate, Is.InRange(25f, 31f));
            Assert.That(_sounds.Duck, Is.EqualTo(0.4f).Within(0.02f), "full ducking");
            Assert.That(ducked.Count, Is.GreaterThan(1));
            Assert.That(Mathf.Max(ducked.ToArray()), Is.LessThan(calm * 0.6f), "steps sink under the fight");

            Run(3f, 5f); // quiet again
            Assert.That(_sounds.Duck, Is.EqualTo(1f).Within(1e-3f), "back to full level");

            // A light fight (a few shots per second) leaves the steps alone.
            Run(2f, 5f, perFrame: () => _sounds.AddFightActivity(4f * Dt));
            Assert.That(_sounds.Duck, Is.EqualTo(1f).Within(1e-3f));
        }

        // ------------------------------------------------------------ Surface

        private GameObject Ground(Vector3 centre, FootstepSurface? surface)
        {
            var ground = Track(GameObject.CreatePrimitive(PrimitiveType.Cube));
            ground.transform.position = centre + Vector3.down * 0.5f;
            ground.transform.localScale = new Vector3(4f, 1f, 4f);
            if (surface.HasValue) ground.AddComponent<FootstepSurfaceTag>().Surface = surface.Value;
            Physics.SyncTransforms();
            return ground;
        }

        [Test]
        public void Surface_FromTheTagUnderTheKnight_FallbackSnow()
        {
            Make();
            var far = new Vector3(500f, 0f, 500f);
            Ground(far, FootstepSurface.Wood);
            Ground(far + new Vector3(10f, 0f, 0f), null);
            Ground(far + new Vector3(20f, 0f, 0f), FootstepSurface.Stone);

            Assert.That(_sounds.SurfaceUnder(far), Is.EqualTo(FootstepSurface.Wood));
            Assert.That(_sounds.SurfaceUnder(far + new Vector3(10f, 0f, 0f)), Is.EqualTo(FootstepSurface.Snow), "untagged = snow");
            Assert.That(_sounds.SurfaceUnder(far + new Vector3(20f, 0f, 0f)), Is.EqualTo(FootstepSurface.Stone));
            Assert.That(_sounds.SurfaceUnder(far + new Vector3(40f, 0f, 0f)), Is.EqualTo(FootstepSurface.Snow), "no ground = snow");

            _body.position = far + new Vector3(-1.5f, 0f, 0f);
            Run(0.4f, 2f, cycleHz: 2.5f);
            Assert.That(_sounds.Steps, Is.GreaterThan(0));
            Assert.That(_sounds.LastSurface, Is.EqualTo(FootstepSurface.Wood));
            Assert.That(System.Array.IndexOf(_wood, _sounds.LastClip), Is.GreaterThanOrEqualTo(0), "a wood clip");
        }

        [Test]
        public void Surface_WithNoClips_PlaysSnow()
        {
            Make(shingle: new AudioClip[0]);
            var far = new Vector3(-500f, 0f, 500f);
            Ground(far, FootstepSurface.Shingle);
            _body.position = far + new Vector3(-1.5f, 0f, 0f);
            Run(0.4f, 2f, cycleHz: 2.5f);
            Assert.That(_sounds.Steps, Is.GreaterThan(0));
            Assert.That(_sounds.LastSurface, Is.EqualTo(FootstepSurface.Snow));
            Assert.That(System.Array.IndexOf(_snow, _sounds.LastClip), Is.GreaterThanOrEqualTo(0));
        }

        // ------------------------------------------------------------ GC

        [Test]
        public void Steps_AllocateNothing()
        {
            Make();
            Ground(Vector3.zero, FootstepSurface.Stone);
            Run(1f, 5f, cycleHz: 2f); // warm up
            int before = _sounds.Steps;
            Assert.That(() =>
            {
                for (int i = 0; i < 120; i++)
                {
                    _t += Dt;
                    _body.position += Vector3.right * 5f * Dt;
                    float s = Mathf.Sin(_t * 2f * Mathf.PI * 2f);
                    _footL.localPosition = new Vector3(-0.12f, 0.1f + 0.08f * s, 0f);
                    _footR.localPosition = new Vector3(0.12f, 0.1f - 0.08f * s, 0f);
                    _sounds.AddFightActivity(0.1f);
                    _sounds.Advance(Dt);
                }
            }, Is.Not.AllocatingGCMemory());
            Assert.That(_sounds.Steps, Is.GreaterThan(before), "steps played inside the measured frames");
        }
    }
}
