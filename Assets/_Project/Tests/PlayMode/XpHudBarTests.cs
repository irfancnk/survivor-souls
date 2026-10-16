#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.UI.Hud;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-35 medieval XP bar on the real HUD document: an XP gain lights the fill-tip spark, which fades; a level wrap
    /// rings the shield (scale pop, then back to rest), bursts the pooled motes (then hides them), flashes the rim and
    /// shows the new level number on the shield. HudBarFx finds the XP and boss bars' shine + rune elements, sweeps the
    /// XP shine across the fill and scrolls the runes.
    /// </summary>
    public sealed class XpHudBarTests
    {
        private const float Frame = 1f / 60f;

        private readonly List<Object> _objects = new List<Object>();
        private XpManager _xp;
        private XpHud _hud;
        private HudBarFx _fx;
        private VisualElement _root;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            GamePause.Clear();
            var curve = Track(ScriptableObject.CreateInstance<XpCurve>());
            curve.Configure(100, 1f, 1); // flat 100 XP per level

            var xpGo = Track(new GameObject("Progression"));
            xpGo.SetActive(false);
            _xp = xpGo.AddComponent<XpManager>();
            _xp.Setup(curve);
            xpGo.SetActive(true);

            var go = Track(new GameObject("HUD"));
            go.SetActive(false);
            var doc = go.AddComponent<UIDocument>();
            doc.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/Hud/GameHudPanelSettings.asset");
            doc.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Hud/GameHud.uxml");
            _hud = go.AddComponent<XpHud>();
            _hud.Setup(_xp);
            _fx = go.AddComponent<HudBarFx>();
            go.SetActive(true);
            _root = doc.rootVisualElement;
            yield return null;
            yield return null; // layout pass
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            GamePause.Clear();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private void Run(float seconds)
        {
            for (float t = 0f; t < seconds; t += Frame) _hud.Tick(Frame);
        }

        [Test]
        public void Shield_ShowsLevelNumber_MotesPooled()
        {
            _hud.Tick(Frame);
            Assert.That(_root.Q<Label>("xp-level").text, Is.EqualTo("1"), "number only: the shield says LV above it");
            Assert.That(_root.Q("xp-badge"), Is.Not.Null);
            Assert.That(_hud.MoteCount, Is.EqualTo(10));
            Assert.That(_root.Q("xp-motes").childCount, Is.EqualTo(10));
            Assert.That(_root.Q("xp-motes").ClassListContains("hud-hidden"), "motes hidden at rest");
        }

        [Test]
        public void XpGain_LightsTipSpark_ThenFades_NoRing()
        {
            _hud.Tick(Frame);
            _xp.AddXp(30);
            _hud.Tick(Frame);
            Assert.That(_hud.SparkActive, "gain lights the tip spark");
            Assert.That(_root.Q("xp-spark").style.opacity.value, Is.GreaterThan(0.5f));
            Run(0.6f);
            Assert.That(_hud.SparkActive, Is.False, "spark fades");
            Assert.That(_root.Q("xp-spark").style.opacity.value, Is.EqualTo(0f));
            Assert.That(_hud.Ringing, Is.False, "no level wrap, no ring");
            Assert.That(_hud.Shown, Is.EqualTo(1.3f).Within(0.01f));
        }

        [Test]
        public void LevelWrap_RingsShield_BurstsMotes_FlashesRim_ThenSettles()
        {
            _hud.Tick(Frame);
            _xp.AddXp(120);
            float peak = 1f;
            bool flashed = false, motes = false;
            for (int i = 0; i < 120 && _hud.Shown < 2.2f - 0.001f; i++)
            {
                _hud.Tick(Frame);
                peak = Mathf.Max(peak, _hud.BadgeScale);
                flashed |= _root.Q("xp-track").ClassListContains("hud-xp--flash");
                motes |= _hud.MotesActive && !_root.Q("xp-motes").ClassListContains("hud-hidden");
            }
            Assert.That(_hud.Ringing || peak > 1.2f, "shield rang");
            Assert.That(peak, Is.GreaterThan(1.2f), "bell pop");
            Assert.That(flashed, "rim flashes on the wrap");
            Assert.That(motes, "motes burst on the wrap");
            Assert.That(_root.Q<Label>("xp-level").text, Is.EqualTo("2"));

            Run(1.2f);
            Assert.That(_hud.Ringing, Is.False);
            Assert.That(_hud.BadgeScale, Is.EqualTo(1f).Within(0.0001f), "shield back at rest");
            Assert.That(_hud.MotesActive, Is.False);
            Assert.That(_root.Q("xp-motes").ClassListContains("hud-hidden"), "motes hidden again");
            Assert.That(_root.Q("xp-track").ClassListContains("hud-xp--flash"), Is.False);
        }

        [UnityTest]
        public IEnumerator BarFx_FindsXpAndBossBars_SweepsShine_ScrollsRunes()
        {
            Assert.That(_fx.ShineCount, Is.EqualTo(3), "XP + boss + health (KAN-82) shine");
            Assert.That(_fx.RuneCount, Is.EqualTo(1), "rune strip on the XP fill");

            _xp.AddXp(60); // the shine needs a fill to cross
            for (int i = 0; i < 60; i++) yield return null; // bar eases in, layout runs
            var fill = _root.Q("xp-fill");
            Assert.That(fill.resolvedStyle.width, Is.GreaterThan(100f), "fill laid out");

            float min = float.MaxValue, max = float.MinValue;
            float runeBefore = _fx.RuneOffset;
            for (int i = 0; i < 330; i++) // one full period (4.5 s) at 60 FPS
            {
                _fx.Tick(Frame);
                float x = _fx.ShineX(0);
                if (float.IsNaN(x)) continue;
                min = Mathf.Min(min, x);
                max = Mathf.Max(max, x);
            }
            Assert.That(min, Is.LessThan(0f), "shine parks left of the fill");
            Assert.That(max, Is.GreaterThan(fill.resolvedStyle.width * 0.5f), "shine sweeps across the fill");
            Assert.That(_fx.RuneOffset, Is.Not.EqualTo(runeBefore).Within(0.01f), "runes scroll");
            Assert.That(_fx.RuneOffset, Is.InRange(-192f, 0f));
        }
    }
}
#endif
