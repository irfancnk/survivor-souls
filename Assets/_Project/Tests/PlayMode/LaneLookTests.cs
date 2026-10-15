using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Gameplay.Boss;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-167 (KAN-160.2): the lane telegraph feeds the TelegraphLane shader (quad length, lane width, progress, the snap
    /// of the last 0.15 s) and emits smoke wisps through the one shared <see cref="LaneSmoke"/> while it warns: wide lanes
    /// more than thin ones, none once hidden, no GC allocation per frame. Look: screenshots.
    /// </summary>
    public sealed class LaneLookTests
    {
        private readonly List<Object> _objects = new List<Object>();
        private readonly MaterialPropertyBlock _read = new MaterialPropertyBlock();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            Time.captureDeltaTime = 0f;
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private Renderer Quad()
        {
            var go = Track(GameObject.CreatePrimitive(PrimitiveType.Quad));
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go.GetComponent<Renderer>();
        }

        private LineTelegraph Lane(out Renderer edge, out Renderer fill)
        {
            edge = Quad();
            fill = Quad();
            var lane = Track(new GameObject("Lane")).AddComponent<LineTelegraph>();
            lane.Setup(new[] { edge }, new[] { fill });
            return lane;
        }

        private LaneSmoke Smoke()
        {
            var go = Track(new GameObject("Smoke"));
            go.AddComponent<ParticleSystem>();
            return go.AddComponent<LaneSmoke>();
        }

        private float Read(Renderer r, string property)
        {
            r.GetPropertyBlock(_read);
            return _read.GetFloat(property);
        }

        [Test]
        public void SetProgress_FeedsTheShader_LengthWidthProgressAndSnap()
        {
            var lane = Lane(out var edge, out var fill);
            lane.ShowLine(Vector3.zero, Vector3.forward, 8f, 2f);
            lane.SetProgress(0.5f, 1f);
            Assert.That(Read(fill, "_Progress"), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(Read(fill, "_Span"), Is.EqualTo(8f * 0.75f).Within(1e-3f), "fill quad length (eased fill)");
            Assert.That(Read(edge, "_Span"), Is.EqualTo(8f).Within(1e-3f), "edge spans the whole lane (the sigil sits at its end)");
            Assert.That(Read(fill, "_LaneWidth"), Is.EqualTo(2f).Within(1e-4f));
            Assert.That(Read(fill, "_Snap"), Is.EqualTo(0f), "no snap 1 s before the attack");

            lane.SetProgress(0.95f, 0.075f);
            Assert.That(lane.Snap, Is.EqualTo(0.5f).Within(1e-3f), "half way into the last 0.15 s");
            Assert.That(Read(fill, "_Snap"), Is.EqualTo(0.5f).Within(1e-3f));
            Assert.That(Read(edge, "_Snap"), Is.EqualTo(0.5f).Within(1e-3f));
            lane.SetProgress(1f, 0f);
            Assert.That(lane.Snap, Is.EqualTo(1f).Within(1e-4f));

            // Callers without the time left (ranged zombies, the Ghoul leap): the snap starts at 88 %.
            lane.ShowLine(Vector3.zero, Vector3.forward, 8f, 0.5f);
            Assert.That(lane.Snap, Is.EqualTo(0f), "a new warning starts calm");
            lane.SetProgress(0.5f);
            Assert.That(lane.Snap, Is.EqualTo(0f));
            lane.SetProgress(0.94f);
            Assert.That(lane.Snap, Is.EqualTo(0.5f).Within(1e-3f));
            lane.Hide();
            Assert.That(lane.Snap, Is.EqualTo(0f));
        }

        [Test]
        public void Flash_KeepsTheFullLengthOnTheFill()
        {
            var lane = Lane(out _, out var fill);
            lane.ShowLine(Vector3.zero, Vector3.right, 6f, 1.5f);
            lane.SetProgress(0.3f, 0.6f);
            lane.Flash();
            lane.Tick(0.05f);
            Assert.That(Read(fill, "_Span"), Is.EqualTo(6f).Within(1e-3f));
            Assert.That(Read(fill, "_Progress"), Is.EqualTo(1f).Within(1e-4f));
        }

        [UnityTest]
        public IEnumerator Smoke_WhileWarning_WideLanesMore_NoneWhenHidden()
        {
            var smoke = Smoke();
            Assert.That(LaneSmoke.Active, Is.SameAs(smoke));
            var wide = Lane(out _, out _);
            var thin = Lane(out _, out _);
            Time.captureDeltaTime = 1f / 30f;
            yield return null;

            wide.ShowLine(Vector3.zero, Vector3.forward, 8f, 2f);
            for (int f = 0; f < 60; f++)
            {
                wide.SetProgress(f / 60f, (60 - f) / 30f);
                yield return null;
            }
            int wideCount = smoke.Emitted;
            wide.Hide();

            thin.ShowLine(Vector3.zero, Vector3.forward, 8f, 0.4f);
            for (int f = 0; f < 60; f++)
            {
                thin.SetProgress(f / 60f, (60 - f) / 30f);
                yield return null;
            }
            int thinCount = smoke.Emitted - wideCount;
            thin.Hide();

            Assert.That(wideCount, Is.InRange(8, 40), "a 2 m charge lane smokes over a 2 s warning");
            Assert.That(thinCount, Is.GreaterThan(0));
            Assert.That(wideCount, Is.GreaterThan(thinCount * 2), "thin fan / aim lanes smoke less");
            Assert.That(smoke.System.particleCount, Is.LessThanOrEqualTo(smoke.System.main.maxParticles));

            int before = smoke.Emitted;
            for (int f = 0; f < 20; f++)
            {
                wide.SetProgress(0.8f, 0.3f); // hidden: ignored
                yield return null;
            }
            Assert.That(smoke.Emitted, Is.EqualTo(before), "no smoke from a hidden lane");
        }

        [Test]
        public void SetProgress_WithSmoke_DoesNotAllocate()
        {
            Smoke();
            var lane = Lane(out _, out _);
            lane.ShowFan(Vector3.zero, Vector3.forward, 1, 0f, 8f, 2f);
            for (int i = 0; i < 5; i++) lane.SetProgress(0.5f, 0.5f); // warm up the block and the emit params
            Assert.That(() => lane.SetProgress(0.6f, 0.4f), Is.Not.AllocatingGCMemory());
        }
    }
}
