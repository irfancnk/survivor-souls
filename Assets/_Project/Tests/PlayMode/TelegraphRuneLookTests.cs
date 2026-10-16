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
    /// KAN-136: the circle warning feeds the TelegraphCircle shader (progress, the fill's share of the full circle, the
    /// glyph band turns, the hit flare, wave mode) through its one block; the glyph band turns while it warns and freezes
    /// on the hit; tints stay until Hide; the visual radius is still the hit radius; no GC per frame. The lane warning
    /// got the same <see cref="LineTelegraph.Tint"/> API. Look: screenshots.
    /// </summary>
    public sealed class TelegraphRuneLookTests
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ProgressId = Shader.PropertyToID("_Progress");
        private static readonly int FillScaleId = Shader.PropertyToID("_FillScale");
        private static readonly int RuneTurnsId = Shader.PropertyToID("_RuneTurns");
        private static readonly int FlareId = Shader.PropertyToID("_Flare");
        private static readonly int WaveId = Shader.PropertyToID("_Wave");

        private readonly List<Object> _objects = new List<Object>();
        private readonly MaterialPropertyBlock _read = new MaterialPropertyBlock();
        private Renderer _ring, _fill;
        private GroundTelegraph _circle;

        [SetUp]
        public void SetUp()
        {
            _ring = Quad();
            _fill = Quad();
            _circle = Track(new GameObject("Circle")).AddComponent<GroundTelegraph>();
            _circle.Setup(_ring, _fill);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
        }

        [Test]
        public void Progress_FeedsFillShareAndProgress_RadiusStaysTheHitRadius()
        {
            _circle.Show(new Vector3(1f, 0f, 2f), 3f);
            _circle.SetProgress(0.5f);
            Assert.That(Float(_fill, ProgressId), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(Float(_fill, FillScaleId), Is.EqualTo(0.75f).Within(1e-4f), "eased fill share 1 - (1 - p)^2");
            Assert.That(_fill.transform.localScale.x, Is.EqualTo(0.75f * 6f).Within(1e-4f), "fill quad = share x diameter");
            Assert.That(_ring.transform.localScale.x, Is.EqualTo(6f).Within(1e-4f), "ring quad edge = the danger radius");
            Assert.That(Float(_ring, WaveId), Is.EqualTo(0f));
            Assert.That(Float(_ring, FlareId), Is.EqualTo(0f));
        }

        [UnityTest]
        public IEnumerator GlyphBand_TurnsWhileWarning_FreezesOnTheHit()
        {
            _circle.Show(Vector3.zero, 2f);
            _circle.SetProgress(0.1f);
            float t0 = Float(_ring, RuneTurnsId);
            float start = Time.time;
            while (Time.time - start < 0.25f) yield return null; // Update (Tick) keeps the band turning without SetProgress
            float t1 = Float(_ring, RuneTurnsId);
            Assert.That(t1, Is.GreaterThan(t0 + 0.03f), "0.3 turns per second");
            Assert.That(t1 - t0, Is.LessThan(0.3f * (Time.time - start) + 0.05f));

            _circle.Flash();
            float frozen = _circle.RuneTurns;
            _circle.Tick(0.05f);
            Assert.That(Float(_ring, FlareId), Is.GreaterThan(0.5f), "the hit flares the runes");
            Assert.That(Float(_fill, FlareId), Is.GreaterThan(0.5f), "and the cracks");
            Assert.That(Float(_fill, FillScaleId), Is.EqualTo(1f).Within(1e-4f), "full circle on the hit");
            yield return null;
            _circle.Tick(0.05f);
            Assert.That(Float(_ring, RuneTurnsId), Is.EqualTo(frozen).Within(1e-5f), "band stops on the hit");
            _circle.Tick(1f);
            Assert.That(_circle.IsShowing, Is.False, "flash over");
        }

        [Test]
        public void Wave_SetsWaveMode_ShowResetsIt()
        {
            _circle.ShowWave(Vector3.zero, 2f, 0.2f);
            Assert.That(Float(_ring, WaveId), Is.EqualTo(1f));
            Assert.That(_ring.transform.localScale.x, Is.EqualTo(2f * 2f / GroundTelegraph.RimShare).Within(1e-4f),
                "wave band at the rim share = the wave radius");
            _circle.Hide();
            _circle.Show(Vector3.zero, 2f);
            Assert.That(Float(_ring, WaveId), Is.EqualTo(0f));
        }

        [Test]
        public void Tint_PaintsTheTintUntilHide()
        {
            var green = new Color(0.36f, 0.62f, 0.05f, 1f);
            _circle.Show(Vector3.zero, 2f);
            _circle.Tint(green, new Color(0.3f, 0.5f, 0.04f, 0.72f), new Color(0.78f, 1f, 0.35f, 0.85f));
            _circle.SetProgress(0.5f);
            Color painted = Colour(_ring);
            Assert.That(painted.g, Is.GreaterThan(painted.r), "green ring");
            _circle.Hide();
            _circle.Show(Vector3.zero, 2f);
            painted = Colour(_ring);
            Assert.That(painted.r, Is.GreaterThan(painted.g * 4f), "back to red after Hide");
        }

        [Test]
        public void LaneTint_PaintsTheTintUntilHide()
        {
            var lines = Track(new GameObject("Lanes")).AddComponent<LineTelegraph>();
            var edges = new[] { Quad() };
            var fills = new[] { Quad() };
            lines.Setup(edges, fills);
            var violet = new Color(0.62f, 0.25f, 0.95f, 1f);
            lines.ShowLine(Vector3.zero, Vector3.forward, 6f, 1f);
            lines.Tint(violet, new Color(0.45f, 0.15f, 0.75f, 0.6f), new Color(0.85f, 0.6f, 1f, 0.85f));
            lines.SetProgress(0.5f);
            Assert.That(Colour(fills[0]).b, Is.GreaterThan(Colour(fills[0]).r), "violet fill");
            lines.Flash();
            lines.Tick(0.05f);
            Assert.That(Colour(fills[0]).b, Is.EqualTo(1f).Within(1e-4f), "violet flash");
            lines.Tick(1f);
            Assert.That(lines.IsShowing, Is.False);
            lines.ShowLine(Vector3.zero, Vector3.forward, 6f, 1f);
            lines.SetProgress(0.5f);
            Assert.That(Colour(fills[0]).r, Is.GreaterThan(Colour(fills[0]).b * 4f), "back to red after Hide");
        }

        [Test]
        public void WarnAndFlash_NoGcPerFrame()
        {
            _circle.Show(Vector3.zero, 2f);
            _circle.SetProgress(0.1f);
            _circle.Tick(0.016f);
            Assert.That(() =>
            {
                for (int i = 0; i < 60; i++)
                {
                    _circle.SetProgress(i / 60f);
                    _circle.Tick(0.016f);
                }
                _circle.Flash();
                for (int i = 0; i < 10; i++) _circle.Tick(0.016f);
                _circle.ShowWave(Vector3.zero, 1.5f, 0.5f);
            }, Is.Not.AllocatingGCMemory());
        }

        private float Float(Renderer r, int id)
        {
            r.GetPropertyBlock(_read);
            return _read.GetFloat(id);
        }

        private Color Colour(Renderer r)
        {
            r.GetPropertyBlock(_read);
            return _read.GetColor(BaseColorId);
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
    }
}
