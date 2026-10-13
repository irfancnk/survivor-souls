using NUnit.Framework;
using SurvivorSouls.FX;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-14 acceptance (the parts a test can see; the look needs a screenshot): running emits wind, standing still
    /// (attacking only happens when standing) and slow drifts emit none, emission grows with speed, every particle
    /// streams back against the move direction (also right after a quick turn), a stop tails off instead of cutting,
    /// the 30-particle budget holds, and a frame allocates nothing.
    /// <see cref="WindTrail.Tick"/> is driven with a fixed velocity and 60 fps step; the system is a bare world-space one.
    /// </summary>
    public sealed class WindTrailTests
    {
        private const float Dt = 1f / 60f;
        private const int Budget = 30;
        private const float RunSpeed = 5f;

        private GameObject _go;
        private WindTrail _trail;
        private ParticleSystem _ps;
        private readonly ParticleSystem.Particle[] _particles = new ParticleSystem.Particle[Budget];

        [SetUp]
        public void SetUp()
        {
            Random.InitState(1234);
            _go = new GameObject("Player");
            var fx = new GameObject("Wind Trail");
            fx.transform.SetParent(_go.transform, false);
            _ps = fx.AddComponent<ParticleSystem>();
            _ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = _ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = Budget;
            main.playOnAwake = false;
            main.loop = false;
            main.startSpeed = 0f;
            var emission = _ps.emission;
            emission.enabled = false;
            _trail = _go.AddComponent<WindTrail>();
            _trail.Setup(null, _ps); // no mover: LateUpdate idles, the test drives Tick
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        private int Run(Vector3 velocity, float seconds)
        {
            int before = _trail.EmittedTotal;
            for (float t = 0f; t < seconds; t += Dt) _trail.Tick(velocity, Dt);
            return _trail.EmittedTotal - before;
        }

        [Test]
        public void StandingStill_EmitsNothing()
        {
            Assert.That(Run(Vector3.zero, 2f), Is.EqualTo(0));
            Assert.That(_trail.Intensity, Is.EqualTo(0f));
        }

        [Test]
        public void SlowDrift_BelowThreshold_EmitsNothing()
        {
            Assert.That(Run(new Vector3(1.5f, 0f, 0f), 2f), Is.EqualTo(0));
        }

        [Test]
        public void Running_Emits_AndRateGrowsWithSpeed()
        {
            int jog = Run(new Vector3(0f, 0f, 3.5f), 1f);
            _trail.enabled = false; // OnEnable resets the ramp so both speeds start from rest
            _trail.enabled = true;
            int run = Run(new Vector3(0f, 0f, RunSpeed), 1f);
            Assert.That(jog, Is.GreaterThan(0));
            Assert.That(run, Is.GreaterThan(jog));
            Assert.That(_trail.Intensity, Is.EqualTo(1f).Within(1e-4f));
        }

        [Test]
        public void Particles_StreamAgainstMoveDirection_AndSpawnBehind()
        {
            Run(new Vector3(RunSpeed, 0f, 0f), 0.5f);
            int n = _ps.GetParticles(_particles);
            Assert.That(n, Is.GreaterThan(0));
            for (int i = 0; i < n; i++)
            {
                Assert.That(_particles[i].velocity.x, Is.LessThan(0f), "particle drifts with the move direction");
                Assert.That(_particles[i].position.x, Is.LessThan(0f), "particle spawned in front of the Knight");
            }
        }

        [Test]
        public void QuickTurn_NewParticlesFollowNewDirection()
        {
            Run(new Vector3(RunSpeed, 0f, 0f), 0.5f);
            _ps.Clear();
            Run(new Vector3(0f, 0f, -RunSpeed), 0.1f); // sharp 90 degree turn, a few frames later
            int n = _ps.GetParticles(_particles);
            Assert.That(n, Is.GreaterThan(0));
            for (int i = 0; i < n; i++) Assert.That(_particles[i].velocity.z, Is.GreaterThan(0f));
        }

        [Test]
        public void Stop_TailsOffSmoothly_ThenEmitsNothing()
        {
            Run(new Vector3(0f, 0f, RunSpeed), 1f);
            _trail.Tick(Vector3.zero, Dt);
            Assert.That(_trail.Intensity, Is.GreaterThan(0.8f), "one frame after the stop the wind is still there");
            Assert.That(Run(Vector3.zero, 0.15f), Is.GreaterThan(0), "it keeps thinning out, not a hard cut");
            Run(Vector3.zero, 0.5f);
            Assert.That(_trail.Intensity, Is.EqualTo(0f));
            Assert.That(Run(Vector3.zero, 1f), Is.EqualTo(0));
        }

        [Test]
        public void Budget_NeverExceeded_EvenAfterFrameHitch()
        {
            Run(new Vector3(0f, 0f, RunSpeed), 3f);
            _trail.Tick(new Vector3(0f, 0f, RunSpeed), 0.5f); // a long frame must not dump a burst
            Assert.That(_ps.particleCount, Is.LessThanOrEqualTo(Budget));
            int before = _trail.EmittedTotal;
            _trail.Tick(new Vector3(0f, 0f, RunSpeed), 0.5f);
            Assert.That(_trail.EmittedTotal - before, Is.LessThanOrEqualTo(3));
        }

        [Test]
        public void TargetIntensity_IsZeroBelowStart_OneAtFull()
        {
            Assert.That(WindTrail.TargetIntensity(2f, 2.2f, 5f), Is.EqualTo(0f));
            Assert.That(WindTrail.TargetIntensity(5f, 2.2f, 5f), Is.EqualTo(1f));
            Assert.That(WindTrail.TargetIntensity(3.6f, 2.2f, 5f), Is.EqualTo(0.5f).Within(1e-4f));
        }

        [Test]
        public void RunningFrame_AllocatesNothing()
        {
            var v = new Vector3(0f, 0f, RunSpeed);
            Run(v, 0.5f); // warm up
            Assert.That(() =>
            {
                for (int i = 0; i < 30; i++) _trail.Tick(v, Dt);
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
