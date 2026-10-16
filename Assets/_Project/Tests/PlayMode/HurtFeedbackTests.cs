#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Audio;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.CameraRig;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.UI.Hud;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using UnityEngine.UIElements;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-187 Knight and screen hurt feedback: a discrete hit (<see cref="PlayerHealth.TakeDamage(float, Vector3)"/>) shows
    /// the arc on the side it came from, flashes the Knight white-hot, shakes, buzzes once and grunts; only a big hit
    /// freezes (hit-stop), never under a pause hold; horde contact (the plain overload) gives no arc, no hot flash, no shake,
    /// no haptic and no grunt; "Reduce screen effects" drops shake, flash and hit-stop but keeps the arc; the heartbeat
    /// beats below 30 % health, faster below 15 %, and has its own setting. A hit allocates nothing.
    /// </summary>
    public sealed class HurtFeedbackTests
    {
        private const float Frame = 1f / 60f;

        private readonly List<Object> _objects = new List<Object>();
        private PlayerHealth _health;
        private PlayerHurtFeedback _feedback;
        private PlayerHitFlash _flash;
        private HitDirectionHud _arcs;
        private HurtSounds _sounds;
        private TopDownFollowCamera _camera;
        private bool _savedReduce, _savedHeartbeat, _savedVibration;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _savedReduce = GameSettings.ReduceScreenEffects;
            _savedHeartbeat = GameSettings.HeartbeatSound;
            _savedVibration = GameSettings.Vibration;
            GameSettings.ReduceScreenEffects = false;
            GameSettings.HeartbeatSound = true;
            GameSettings.Vibration = true;
            GamePause.Clear();
            Time.timeScale = 1f;

            var knight = Track(new GameObject("Knight"));
            _health = knight.AddComponent<PlayerHealth>(); // 400 max
            _flash = knight.AddComponent<PlayerHitFlash>();
            _feedback = knight.AddComponent<PlayerHurtFeedback>();

            var camGo = Track(new GameObject("Camera"));
            camGo.transform.SetPositionAndRotation(new Vector3(0f, 12f, -8f), Quaternion.Euler(55f, 0f, 0f));
            var cam = camGo.AddComponent<Camera>();
            _camera = camGo.AddComponent<TopDownFollowCamera>(); // no target: LateUpdate does nothing, Shake still records
            _feedback.Setup(_camera);

            var hud = Track(new GameObject("HUD"));
            hud.SetActive(false);
            var doc = hud.AddComponent<UIDocument>();
            doc.panelSettings = UnityEditor.AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/_Project/UI/Hud/GameHudPanelSettings.asset");
            doc.visualTreeAsset = UnityEditor.AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/_Project/UI/Hud/GameHud.uxml");
            _arcs = hud.AddComponent<HitDirectionHud>();
            _arcs.Setup(_health, cam);
            hud.SetActive(true);

            var audio = Track(new GameObject("Audio"));
            _sounds = audio.AddComponent<HurtSounds>();
            _sounds.Setup(_health, new[] { AudioClip.Create("grunt", 441, 1, 44100, false) }, AudioClip.Create("beat", 441, 1, 44100, false));
            yield return null; // layout pass
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            GamePause.Clear();
            Time.timeScale = 1f;
            GameSettings.ReduceScreenEffects = _savedReduce;
            GameSettings.HeartbeatSound = _savedHeartbeat;
            GameSettings.Vibration = _savedVibration;
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private Vector3 From(float x, float z) => _health.transform.position + new Vector3(x, 0f, z);

        [Test]
        public void ScreenAngle_TopDownMapping_PicksTheSide()
        {
            Assert.That(HitDirectionHud.SideOf(HitDirectionHud.ScreenAngle(Vector3.zero, new Vector3(6f, 0f, 0f), null)), Is.EqualTo(HitDirectionHud.Side.Right));
            Assert.That(HitDirectionHud.SideOf(HitDirectionHud.ScreenAngle(Vector3.zero, new Vector3(-6f, 0f, 1f), null)), Is.EqualTo(HitDirectionHud.Side.Left));
            Assert.That(HitDirectionHud.SideOf(HitDirectionHud.ScreenAngle(Vector3.zero, new Vector3(1f, 0f, 6f), null)), Is.EqualTo(HitDirectionHud.Side.Top));
            Assert.That(HitDirectionHud.SideOf(HitDirectionHud.ScreenAngle(Vector3.zero, new Vector3(0f, 0f, -6f), null)), Is.EqualTo(HitDirectionHud.Side.Bottom));
        }

        [Test]
        public void ScreenAngle_ThroughTheFollowCamera_PicksTheSide()
        {
            var cam = _camera.GetComponent<Camera>();
            Vector3 k = new Vector3(0f, 0f, 2f); // in front of the camera, like the Knight
            Assert.That(HitDirectionHud.SideOf(HitDirectionHud.ScreenAngle(k, k + new Vector3(5f, 0f, 0f), cam)), Is.EqualTo(HitDirectionHud.Side.Right));
            Assert.That(HitDirectionHud.SideOf(HitDirectionHud.ScreenAngle(k, k + new Vector3(-5f, 0f, 0f), cam)), Is.EqualTo(HitDirectionHud.Side.Left));
            Assert.That(HitDirectionHud.SideOf(HitDirectionHud.ScreenAngle(k, k + new Vector3(0f, 0f, 5f), cam)), Is.EqualTo(HitDirectionHud.Side.Top));
            Assert.That(HitDirectionHud.SideOf(HitDirectionHud.ScreenAngle(k, k + new Vector3(0f, 0f, -5f), cam)), Is.EqualTo(HitDirectionHud.Side.Bottom));
        }

        [Test]
        public void ArcCentre_SitsInsideTheEdgeOnThatSide()
        {
            var panel = new Vector2(1000f, 2000f);
            Vector2 right = HitDirectionHud.ArcCentre(90f, panel, 50f);
            Vector2 top = HitDirectionHud.ArcCentre(0f, panel, 50f);
            Assert.That(right.x, Is.EqualTo(950f).Within(0.01f));
            Assert.That(right.y, Is.EqualTo(1000f).Within(0.01f));
            Assert.That(top.x, Is.EqualTo(500f).Within(0.01f));
            Assert.That(top.y, Is.EqualTo(50f).Within(0.01f), "panel y grows down: top arc near y = 0");
        }

        [Test]
        public void DiscreteHit_ShowsArcOnItsSide_ThenFades()
        {
            var root = _arcs.GetComponent<UIDocument>().rootVisualElement;
            Assert.That(root.Query(className: HitDirectionHud.ArcClass).ToList().Count, Is.EqualTo(HitDirectionHud.PoolSize), "arcs pooled up front");

            _health.TakeDamage(20f, From(-6f, 0f));
            Assert.That(_arcs.Shown, Is.EqualTo(1));
            Assert.That(HitDirectionHud.SideOf(_arcs.LastAngle), Is.EqualTo(HitDirectionHud.Side.Left));
            Assert.That(_arcs.Visible, Is.EqualTo(1));

            _health.TakeDamage(20f, From(0f, 7f)); // another side: a second arc
            Assert.That(_arcs.Visible, Is.EqualTo(2));
            _health.TakeDamage(20f, From(0.5f, 7f)); // same side again: refreshes, no third arc
            Assert.That(_arcs.Visible, Is.EqualTo(2));

            _arcs.Tick(1f);
            Assert.That(_arcs.Visible, Is.EqualTo(0), "faded out");
        }

        [Test]
        public void HordeContact_NoArc_NoHotFlash_NoShake_NoHaptic_NoGrunt()
        {
            for (int i = 0; i < 120; i++) _health.TakeDamage(45f * Frame); // 2 s of boss-level contact
            Assert.That(_health.Current, Is.LessThan(_health.Max));
            Assert.That(_arcs.Shown, Is.Zero);
            Assert.That(_flash.HotFlash, Is.False);
            Assert.That(_feedback.Shakes, Is.Zero);
            Assert.That(_feedback.HapticRequests, Is.Zero);
            Assert.That(_feedback.HitStops, Is.Zero);
            Assert.That(_sounds.Grunts, Is.Zero);
        }

        [Test]
        public void DiscreteHit_FlashesHot_Shakes_Buzzes_Grunts()
        {
            _health.TakeDamage(0.05f * _health.Max, From(5f, 0f));
            Assert.That(_flash.HotFlash, Is.True);
            Assert.That(_feedback.Shakes, Is.EqualTo(1));
            Assert.That(_feedback.LastShake, Is.GreaterThan(0f));
            Assert.That(_feedback.HapticRequests, Is.EqualTo(1));
            Assert.That(_sounds.Grunts, Is.EqualTo(1));

            _health.TakeDamage(0.05f * _health.Max, From(5f, 0f)); // same frame: haptic and grunt are rate limited
            Assert.That(_feedback.HapticRequests, Is.EqualTo(1));
            Assert.That(_sounds.Grunts, Is.EqualTo(1));
        }

        [Test]
        public void Shake_GrowsWithShare_AndIsCapped()
        {
            float small = PlayerHurtFeedback.ShakeFor(0.03f, 0.05f, 1.1f, 0.3f);
            float big = PlayerHurtFeedback.ShakeFor(0.2f, 0.05f, 1.1f, 0.3f);
            Assert.That(big, Is.GreaterThan(small));
            Assert.That(PlayerHurtFeedback.ShakeFor(0.9f, 0.05f, 1.1f, 0.3f), Is.EqualTo(0.3f));
            Assert.That(HurtSounds.GruntVolumeFor(0.3f, 0.55f, 0.85f, 0.3f), Is.GreaterThan(HurtSounds.GruntVolumeFor(0.02f, 0.55f, 0.85f, 0.3f)));
        }

        [Test]
        public void HitStop_OnlyAboveTheThreshold_AndEndsOnRealTime()
        {
            Assert.That(PlayerHurtFeedback.WantsHitStop(0.11f, 0.12f), Is.False);
            Assert.That(PlayerHurtFeedback.WantsHitStop(0.12f, 0.12f), Is.True);

            _health.TakeDamage(0.08f * _health.Max, From(5f, 0f));
            Assert.That(_feedback.HitStops, Is.Zero, "a small hit does not freeze");
            Assert.That(Time.timeScale, Is.EqualTo(1f));

            _health.TakeDamage(0.15f * _health.Max, From(5f, 0f));
            Assert.That(_feedback.HitStops, Is.EqualTo(1));
            Assert.That(Time.timeScale, Is.EqualTo(0f));
            _feedback.Tick(0.02f);
            Assert.That(Time.timeScale, Is.EqualTo(0f), "still inside the ~40 ms");
            _feedback.Tick(0.03f);
            Assert.That(_feedback.HitStopping, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        [Test]
        public void HitStop_NeverFightsGamePause()
        {
            GamePause.Acquire(); // level-up cards open
            _health.TakeDamage(0.2f * _health.Max, From(5f, 0f));
            Assert.That(_feedback.HitStops, Is.Zero, "no hit-stop under a pause hold");
            GamePause.Release();
            Assert.That(Time.timeScale, Is.EqualTo(1f));

            _health.TakeDamage(0.2f * _health.Max, From(5f, 0f));
            Assert.That(_feedback.HitStopping, Is.True);
            GamePause.Acquire(); // a pause opens during the freeze
            _feedback.Tick(0.1f);
            Assert.That(Time.timeScale, Is.EqualTo(0f), "the hit-stop end leaves the pause alone");
            GamePause.Release();
            Assert.That(Time.timeScale, Is.EqualTo(1f), "the pause resumes at normal speed, not at the frozen scale");
        }

        [Test]
        public void HitStop_NotOnTheKillingBlow()
        {
            _health.TakeDamage(_health.Max * 2f, From(5f, 0f));
            Assert.That(_health.IsDead, Is.True);
            Assert.That(_feedback.HitStops, Is.Zero);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        [Test]
        public void ReduceScreenEffects_DropsShakeFlashAndHitStop_KeepsArc()
        {
            GameSettings.ReduceScreenEffects = true;
            _health.TakeDamage(0.2f * _health.Max, From(-5f, 0f));
            Assert.That(_feedback.Shakes, Is.Zero);
            Assert.That(_feedback.HitStops, Is.Zero);
            Assert.That(_flash.Flashing, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(_arcs.Shown, Is.EqualTo(1), "the arc is information, it stays");
        }

        [Test]
        public void ShieldTakesTheHit_ArcOnly()
        {
            _health.SetShield(100f);
            _health.TakeDamage(50f, From(5f, 0f));
            Assert.That(_health.Current, Is.EqualTo(_health.Max));
            Assert.That(_arcs.Shown, Is.EqualTo(1));
            Assert.That(_feedback.Shakes, Is.Zero);
            Assert.That(_sounds.Grunts, Is.Zero);
        }

        [Test]
        public void Heartbeat_BelowLowHealth_FasterWhenCritical_OwnSetting()
        {
            var feel = new HealthBarFeel();
            Assert.That(HurtSounds.HeartState(0.29f, feel.lowShare, feel.criticalShare), Is.EqualTo(1), "same thresholds as the bar");
            Assert.That(HurtSounds.HeartState(0.14f, feel.lowShare, feel.criticalShare), Is.EqualTo(2));
            Assert.That(HurtSounds.HeartState(0.5f, feel.lowShare, feel.criticalShare), Is.EqualTo(0));

            for (int i = 0; i < 300; i++) _sounds.Tick(Frame);
            Assert.That(_sounds.Beats, Is.Zero, "full health: silent");

            _health.TakeDamage(_health.Max * 0.75f); // 25 %
            for (int i = 0; i < 300; i++) _sounds.Tick(Frame); // 5 s
            int low = _sounds.Beats;
            Assert.That(low, Is.InRange(5, 7), "about 1.1 beats per second");

            _health.TakeDamage(_health.Max * 0.15f); // 10 %
            int before = _sounds.Beats;
            for (int i = 0; i < 300; i++) _sounds.Tick(Frame);
            Assert.That(_sounds.Beats - before, Is.GreaterThan(low), "faster when critical");

            GameSettings.HeartbeatSound = false;
            before = _sounds.Beats;
            for (int i = 0; i < 300; i++) _sounds.Tick(Frame);
            Assert.That(_sounds.Beats, Is.EqualTo(before), "own setting silences it");
        }

        [Test]
        public void DiscreteHit_AllocatesNothing()
        {
            // Warm up: first-call JIT and the first inline style write of every pooled arc.
            _health.TakeDamage(1f, From(5f, 0f));
            _health.TakeDamage(1f, From(0f, 5f));
            _health.TakeDamage(1f, From(-5f, 0f));
            _arcs.Tick(Frame);
            _arcs.Tick(1f);
            _feedback.Tick(Frame);
            _sounds.Tick(Frame);
            Assert.That(() =>
            {
                _health.TakeDamage(1f, From(-5f, 0f));
                _arcs.Tick(Frame);
                _feedback.Tick(Frame);
                _sounds.Tick(Frame);
            }, Is.Not.AllocatingGCMemory());
        }
    }
}
#endif
