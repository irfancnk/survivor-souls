using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.World;
using SurvivorSouls.UI.Hud;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using UnityEngine.UIElements;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-49: a sealed portal answers the Knight. PortalGate: walking into the seal zone while Locked raises
    /// <see cref="PortalGate.SealTouched"/> once (rune pulse that fades), never while Opening / Open or after death, again
    /// after leaving and coming back. Game scene: the HUD bubble shows over the arch with a line and the live time (same
    /// clock as the HUD timer), stays inside the panel below the top bars, never blocks touches, allocates nothing per
    /// frame, fades out by itself after ~2.5 s, shows again on a new visit, and never shows once the portal is open.
    /// Look: Docs/screenshots/kan49-*.png.
    /// </summary>
    public sealed class PortalSealTests
    {
        private const float Frame = 1f / 60f;
        private const int TestRealm = 96; // never touches the saved progress of a real Realm

        private readonly List<Object> _objects = new List<Object>();
        private int _touched;
        private System.Action<PortalGate> _onTouch;

        [SetUp]
        public void SetUp()
        {
            _touched = 0;
            _onTouch = _ => _touched++;
            PortalGate.SealTouched += _onTouch;
        }

        [TearDown]
        public void TearDown()
        {
            PortalGate.SealTouched -= _onTouch;
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

        private (SurvivalTimer timer, Transform player, PortalGate gate) NewChamber(ChamberRules rules)
        {
            var playerGo = Track(new GameObject("Player"));
            playerGo.transform.position = new Vector3(0f, 0f, -20f);
            var health = playerGo.AddComponent<PlayerHealth>();
            var timer = Track(new GameObject("Chamber")).AddComponent<SurvivalTimer>();
            timer.Setup(Track(rules), playerGo.transform, health);

            var go = Track(new GameObject("Portal"));
            var block = new GameObject("Door Block");
            block.transform.SetParent(go.transform, false);
            var blocker = block.AddComponent<BoxCollider>();
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.transform.SetParent(go.transform, false);
            var gate = go.AddComponent<PortalGate>();
            gate.Setup(quad.GetComponent<MeshRenderer>(), blocker, Vector3.zero, 1f);
            gate.enabled = false; // tests tick by hand
            return (timer, playerGo.transform, gate);
        }

        private static void Run(PortalGate gate, float seconds)
        {
            for (float t = 0f; t < seconds - 1e-4f; t += Frame) gate.Tick(Frame);
        }

        [Test]
        public void LockedPortal_WalkUp_TouchesTheSealOnce_WithAFadingPulse()
        {
            var (_, player, gate) = NewChamber(ChamberRules.Create(600f));
            Run(gate, 0.5f);
            Assert.That(_touched, Is.Zero, "far away");
            player.position = gate.TriggerPosition + new Vector3(0.3f, 0f, -2f);
            gate.Tick(Frame);
            Assert.That(_touched, Is.EqualTo(1), "walked into the zone in front of the arch");
            Assert.That(gate.InSealZone, Is.True);
            Assert.That(gate.SealPulse, Is.GreaterThan(0.9f), "runes flare");
            Run(gate, 3f);
            Assert.That(_touched, Is.EqualTo(1), "standing there: no repeat");
            Assert.That(gate.SealPulse, Is.Zero, "pulse faded");
            Assert.That(gate.State, Is.EqualTo(PortalState.Locked));

            player.position = gate.TriggerPosition + new Vector3(0f, 0f, -8f);
            gate.Tick(Frame);
            player.position = gate.TriggerPosition + new Vector3(0f, 0f, -2.5f);
            gate.Tick(Frame);
            Assert.That(_touched, Is.EqualTo(2), "left and came back: shows again");
        }

        [Test]
        public void OpeningOrOpenPortal_NeverTouchesTheSeal()
        {
            var (timer, player, gate) = NewChamber(ChamberRules.Create(600f, openSequence: 2f));
            timer.Complete();
            player.position = gate.TriggerPosition + new Vector3(0f, 0f, -2f);
            gate.Tick(Frame);
            Assert.That(gate.State, Is.EqualTo(PortalState.Opening));
            Run(gate, 3f);
            Assert.That(gate.State, Is.EqualTo(PortalState.Open));
            player.position = gate.TriggerPosition + new Vector3(0f, 0f, -8f);
            gate.Tick(Frame);
            player.position = gate.TriggerPosition + new Vector3(0f, 0f, -2f);
            Run(gate, 0.5f);
            Assert.That(_touched, Is.Zero, "the seal is gone");
        }

        [Test]
        public void DeadKnight_DoesNotTouchTheSeal()
        {
            var (timer, player, gate) = NewChamber(ChamberRules.Create(600f));
            timer.Stop(); // what the death event does
            player.position = gate.TriggerPosition + new Vector3(0f, 0f, -2f);
            Run(gate, 0.5f);
            Assert.That(_touched, Is.Zero);
        }

        [UnityTest]
        public IEnumerator GameScene_Bubble_ShowsTheLiveTime_FadesOut_AndComesBack()
        {
            RunState.NewRun(TestRealm, 4901);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;

            var player = Object.FindAnyObjectByType<PlayerMover>().transform;
            foreach (var h in Object.FindObjectsByType<PlayerHealth>()) h.Invulnerable = true;
            var hud = Object.FindAnyObjectByType<PortalSealBubbleHud>();
            Assert.That(hud, Is.Not.Null, "HUD has the seal bubble");
            Assert.That(hud.Bubble, Is.Not.Null);
            Assert.That(hud.Bubble.pickingMode, Is.EqualTo(PickingMode.Ignore), "never blocks touches");
            Assert.That(hud.Visible, Is.False, "nothing at the Chamber start");
            var timer = SurvivalTimer.Active;
            Assert.That(timer, Is.Not.Null);
            timer.SetRemaining(452f);

            // The exit tile is pooled: stream it in first (stand off the zone), then walk up.
            Vector3 exit = player.GetComponent<ObjectiveArrow>().Target;
            Teleport(player, exit + new Vector3(0f, 0f, -10f));
            for (float t = 0f; t < 0.5f; t += Time.unscaledDeltaTime) yield return null;
            Assert.That(_touched, Is.Zero, "10 m off the gate: outside the seal zone");
            var gate = NearestGate(exit);
            Assert.That(gate, Is.Not.Null, "exit portal streamed in");
            Assert.That(gate.State, Is.EqualTo(PortalState.Locked));
            Vector3 front = gate.TriggerPosition + new Vector3(0f, 0f, -2f);
            Teleport(player, front);
            for (float t = 0f; t < 0.4f; t += Time.unscaledDeltaTime) yield return null;
            Assert.That(_touched, Is.EqualTo(1), "walked up to the sealed gate");
            Assert.That(hud.Visible, Is.True, "bubble shows");
            Assert.That(hud.Alpha, Is.GreaterThan(0.9f), "faded in (0.15 s)");
            Assert.That(string.IsNullOrEmpty(hud.Header), Is.False);
            int seconds = Mathf.CeilToInt(timer.Remaining);
            Assert.That(hud.TimeText, Is.EqualTo(TimerHud.Format(seconds)), "same clock as the HUD timer");
            var hudTimer = hud.GetComponent<UIDocument>().rootVisualElement.Q<Label>("timer-label");
            if (hudTimer != null) Assert.That(hud.TimeText, Is.EqualTo(hudTimer.text), "matches the HUD timer text");
            StringAssert.Contains(hud.TimeText, hud.LowerLine);

            // Inside the panel, below the top bars.
            Vector2 panelSize = hud.Bubble.panel.visualTree.layout.size;
            Rect box = new Rect(hud.PanelPosition, new Vector2(hud.Bubble.resolvedStyle.width, hud.Bubble.resolvedStyle.height));
            Assert.That(box.xMin, Is.GreaterThanOrEqualTo(0f));
            Assert.That(box.xMax, Is.LessThanOrEqualTo(panelSize.x + 0.5f));
            var bar = hud.GetComponent<UIDocument>().rootVisualElement.Q("timer");
            if (bar != null && bar.resolvedStyle.display != DisplayStyle.None && panelSize.y > 900f)
                Assert.That(box.yMin, Is.GreaterThanOrEqualTo(bar.worldBound.yMax), "clear of the timer bar");

            // Live countdown.
            timer.SetRemaining(100.2f);
            hud.Tick(0f);
            Assert.That(hud.TimeText, Is.EqualTo("01:41"), "counts down live while visible");

            hud.Tick(Frame); // warm up
            Assert.That(() => { for (int i = 0; i < 30; i++) hud.Tick(Frame); }, Is.Not.AllocatingGCMemory(), "no allocation per frame");

            // Fades out by itself while the Knight stays.
            for (float t = 0f; t < 3f; t += Time.unscaledDeltaTime) yield return null;
            Assert.That(hud.Visible, Is.False, "gone after ~2.5 s + fade");
            Assert.That(_touched, Is.EqualTo(1), "staying in the zone: no repeat");

            // Walk away and back: shows again.
            Teleport(player, gate.TriggerPosition + new Vector3(0f, 0f, -10f));
            for (float t = 0f; t < 0.3f; t += Time.unscaledDeltaTime) yield return null;
            Teleport(player, front);
            for (float t = 0f; t < 0.3f; t += Time.unscaledDeltaTime) yield return null;
            Assert.That(_touched, Is.EqualTo(2));
            Assert.That(hud.Visible, Is.True, "every visit shows it");

            // Portal opens: the bubble goes at once and never comes back.
            timer.Complete();
            for (float t = 0f; t < 0.6f; t += Time.unscaledDeltaTime) yield return null;
            Assert.That(hud.Visible, Is.False, "seal broke: bubble faded out");
            Teleport(player, gate.TriggerPosition + new Vector3(0f, 0f, -10f));
            for (float t = 0f; t < 0.2f; t += Time.unscaledDeltaTime) yield return null;
            Teleport(player, front);
            for (float t = 0f; t < 0.3f; t += Time.unscaledDeltaTime) yield return null;
            Assert.That(_touched, Is.EqualTo(2), "open portal: no seal touch");
            Assert.That(hud.Visible, Is.False);

            var empty = SceneManager.CreateScene("PortalSealEmpty");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
            GamePause.Clear();
        }

        private static PortalGate NearestGate(Vector3 target)
        {
            PortalGate best = null;
            float bestD = float.MaxValue;
            foreach (var g in Object.FindObjectsByType<PortalGate>())
            {
                float d = (g.TriggerPosition - target).sqrMagnitude;
                if (d < bestD) { bestD = d; best = g; }
            }
            return best;
        }

        private static void Teleport(Transform player, Vector3 position)
        {
            var cc = player.GetComponent<CharacterController>();
            if (cc != null) cc.enabled = false;
            player.position = position;
            if (cc != null) cc.enabled = true;
        }
    }
}
