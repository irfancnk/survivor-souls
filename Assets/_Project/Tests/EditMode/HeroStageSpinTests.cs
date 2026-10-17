using NUnit.Framework;
using SurvivorSouls.UI.MainMenu;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-244 hero stage turn maths (<see cref="HeroStageSpin"/>): a drag turns the hero by the feel's degrees per panel
    /// pixel, a flick keeps spinning and stops in under 1 s, the hero waits 2 s untouched and then eases back to the front
    /// in 0.6 s the short way round, and a new drag stops a spin or a return at once.
    /// </summary>
    public sealed class HeroStageSpinTests
    {
        private const float Frame = 1f / 60f;

        private static HeroStageSpin Spin() => new HeroStageSpin(new HeroStageSpinFeel());

        private static void Run(HeroStageSpin spin, float seconds)
        {
            for (float t = 0f; t < seconds; t += Frame) spin.Tick(Frame);
        }

        [Test]
        public void Drag_TurnsByDegreesPerPixel_RightDragTurnsTheHeroToHisLeft()
        {
            var spin = Spin();
            spin.BeginDrag();
            spin.Drag(50f);
            spin.Tick(Frame);
            spin.Drag(50f);
            Assert.That(spin.Yaw, Is.EqualTo(-40f).Within(1e-3f), "100 px * 0.4 deg");
            Assert.That(spin.Dragging, Is.True);
            spin.Drag(-600f);
            Assert.That(spin.Yaw, Is.EqualTo(-160f).Within(1e-3f), "200 deg wraps into -180..180");
        }

        [Test]
        public void SlowDrag_ReleaseWithoutSpeed_DoesNotSpin()
        {
            var spin = Spin();
            spin.BeginDrag();
            for (int i = 0; i < 30; i++)
            {
                spin.Drag(i < 20 ? 3f : 0f); // the finger stops before it lifts
                spin.Tick(Frame);
            }
            spin.EndDrag();
            Assert.That(spin.Velocity, Is.EqualTo(0f));
        }

        [Test]
        public void Flick_KeepsSpinning_StopsInUnderOneSecond()
        {
            var spin = Spin();
            spin.BeginDrag();
            for (int i = 0; i < 6; i++)
            {
                spin.Drag(20f); // 20 px per frame = 480 deg/s
                spin.Tick(Frame);
            }
            float yawAtRelease = spin.Yaw;
            spin.EndDrag();
            Assert.That(System.Math.Abs(spin.Velocity), Is.EqualTo(480f).Within(1f));
            Run(spin, 0.2f);
            Assert.That(spin.Yaw, Is.Not.EqualTo(yawAtRelease), "keeps turning after the release");
            Run(spin, 0.8f);
            Assert.That(spin.Velocity, Is.EqualTo(0f), "stopped within 1 s");
        }

        [Test]
        public void Untouched_WaitsTwoSeconds_ThenEasesBackToTheFront_ShortWay()
        {
            var spin = Spin();
            spin.Snap(150f);
            Run(spin, 1.9f);
            Assert.That(spin.Yaw, Is.EqualTo(150f), "waits 2 s");
            Assert.That(spin.Returning, Is.False);
            Run(spin, 0.2f);
            Assert.That(spin.Returning, Is.True);
            Run(spin, 0.3f);
            Assert.That(spin.Yaw, Is.GreaterThan(0f).And.LessThan(150f), "150 -> 0 straight, never through 180");
            Run(spin, 0.4f);
            Assert.That(spin.Yaw, Is.EqualTo(0f));
            Assert.That(spin.AtRest, Is.True);
        }

        [Test]
        public void NewDrag_StopsSpinAndReturn()
        {
            var spin = Spin();
            spin.Snap(90f);
            Run(spin, 2.2f);
            Assert.That(spin.Returning, Is.True);
            spin.BeginDrag();
            Assert.That(spin.Returning, Is.False);
            float held = spin.Yaw;
            Run(spin, 1f);
            Assert.That(spin.Yaw, Is.EqualTo(held), "a held finger holds the hero");
        }

        [Test]
        public void Wrap_KeepsAnglesInRange()
        {
            Assert.That(HeroStageSpin.Wrap(190f), Is.EqualTo(-170f).Within(1e-4f));
            Assert.That(HeroStageSpin.Wrap(-190f), Is.EqualTo(170f).Within(1e-4f));
            Assert.That(HeroStageSpin.Wrap(540f), Is.EqualTo(180f).Within(1e-4f));
            Assert.That(HeroStageSpin.Wrap(-45f), Is.EqualTo(-45f).Within(1e-4f));
        }
    }
}
