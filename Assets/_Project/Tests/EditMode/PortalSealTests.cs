using NUnit.Framework;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.UI.Hud;
using UnityEngine;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-49 sealed-portal bubble logic: the seal zone fires on enter only, has border hysteresis (no flicker), re-arms on
    /// exit, eats a quick re-entry inside the interval, clears when the seal is not armed; the line picker never repeats
    /// the last line; the default text table has timed and boss lines and the ticket's timing.
    /// </summary>
    public sealed class PortalSealTests
    {
        private const float Frame = 1f / 60f;

        private static PortalSealTrigger NewTrigger() =>
            new PortalSealTrigger { EnterRadius = 2.75f, ExitRadius = 3.5f, MinInterval = 1.5f };

        private static float Sq(float d) => d * d;

        [Test]
        public void Trigger_FiresOnEnter_OnlyOnce_WhileInside()
        {
            var t = NewTrigger();
            Assert.That(t.Step(Sq(10f), Frame, true), Is.False, "far away");
            Assert.That(t.Step(Sq(2f), Frame, true), Is.True, "walks in");
            int fires = 0;
            for (int i = 0; i < 600; i++)
                if (t.Step(Sq(1.5f), Frame, true)) fires++;
            Assert.That(fires, Is.Zero, "standing in the zone for 10 s: no repeat");
            Assert.That(t.Inside, Is.True);
        }

        [Test]
        public void Trigger_BorderJitter_DoesNotFlicker()
        {
            var t = NewTrigger();
            Assert.That(t.Step(Sq(2.7f), Frame, true), Is.True);
            int fires = 0;
            for (int i = 0; i < 600; i++)
            {
                float d = i % 2 == 0 ? 2.6f : 3.2f; // in and out of the enter radius, but inside the exit radius
                if (t.Step(Sq(d), Frame, true)) fires++;
            }
            Assert.That(fires, Is.Zero, "hysteresis: the exit radius holds the Knight inside");
            Assert.That(t.Inside, Is.True);
        }

        [Test]
        public void Trigger_LeaveAndComeBack_FiresAgain_AfterTheInterval()
        {
            var t = NewTrigger();
            Assert.That(t.Step(Sq(2f), Frame, true), Is.True);
            for (int i = 0; i < 120; i++) t.Step(Sq(2f), Frame, true); // 2 s inside
            Assert.That(t.Step(Sq(4f), Frame, true), Is.False, "walks out past the exit radius");
            Assert.That(t.Inside, Is.False);
            Assert.That(t.Step(Sq(2f), Frame, true), Is.True, "back in: shows again");
        }

        [Test]
        public void Trigger_QuickReentry_IsEaten_UntilTheKnightLeavesAgain()
        {
            var t = NewTrigger();
            Assert.That(t.Step(Sq(2f), Frame, true), Is.True);
            Assert.That(t.Step(Sq(4f), Frame, true), Is.False);
            Assert.That(t.Step(Sq(2f), Frame, true), Is.False, "back in 2 frames later: inside the 1.5 s interval");
            for (int i = 0; i < 180; i++)
                Assert.That(t.Step(Sq(2f), Frame, true), Is.False, "staying in does not fire later either");
            t.Step(Sq(4f), Frame, true);
            Assert.That(t.Step(Sq(2f), Frame, true), Is.True, "leave + enter after the interval");
        }

        [Test]
        public void Trigger_NotArmed_NeverFires_AndClearsTheZone()
        {
            var t = NewTrigger();
            for (int i = 0; i < 60; i++) Assert.That(t.Step(Sq(1f), Frame, false), Is.False, "open portal / dead Knight");
            Assert.That(t.Inside, Is.False);
            Assert.That(t.Step(Sq(1f), Frame, true), Is.True, "armed again with the Knight in the zone: counts as an entry");
            t.Reset();
            Assert.That(t.Inside, Is.False);
            Assert.That(t.Step(Sq(1f), Frame, true), Is.True, "reset clears the interval too");
        }

        [Test]
        public void Pick_NeverRepeatsTheLastLine_AndCoversAll()
        {
            Assert.That(PortalSealLines.Pick(0, -1, 0.5f), Is.EqualTo(-1));
            Assert.That(PortalSealLines.Pick(1, 0, 0.9f), Is.EqualTo(0));
            for (int count = 2; count <= 6; count++)
            {
                var seen = new bool[count];
                for (int last = -1; last < count; last++)
                    for (float r = 0f; r < 1f; r += 0.01f)
                    {
                        int i = PortalSealLines.Pick(count, last, r);
                        Assert.That(i, Is.InRange(0, count - 1));
                        Assert.That(i, Is.Not.EqualTo(last), $"count {count}: never the last line");
                        seen[i] = true;
                    }
                Assert.That(System.Array.TrueForAll(seen, s => s), Is.True, $"count {count}: every line can show");
            }
            Assert.That(PortalSealLines.Pick(5, -1, 0.999f), Is.EqualTo(4), "no last line: the top index is reachable");
        }

        [Test]
        public void DefaultLines_HaveTimedAndBossLines_AndTheTicketTiming()
        {
            var lines = PortalSealLines.CreateDefault();
            try
            {
                Assert.That(lines.timed.Length, Is.GreaterThanOrEqualTo(3), "a few variants so it does not feel repeated");
                Assert.That(lines.boss.Length, Is.GreaterThanOrEqualTo(1));
                foreach (var l in lines.timed) Assert.That(string.IsNullOrEmpty(l.header), Is.False);
                Assert.That(lines.visibleSeconds, Is.EqualTo(2.5f).Within(0.01f));
                Assert.That(lines.fadeIn, Is.EqualTo(0.15f).Within(0.01f));
                Assert.That(lines.fadeOut, Is.EqualTo(0.3f).Within(0.01f));
                Assert.That(TimerHud.Format(452), Is.EqualTo("07:32"), "mm:ss, same formatter as the HUD timer");
            }
            finally
            {
                Object.DestroyImmediate(lines);
            }
        }
    }
}
