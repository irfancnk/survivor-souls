using NUnit.Framework;
using SurvivorSouls.Gameplay.Controls;
using SurvivorSouls.UI.Hud;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-16 acceptance (the parts a test can see; the look needs a screenshot): a touch anywhere in the movement zone
    /// becomes the stick centre, the output is right in all 8 directions and zero on release, the dead zone holds,
    /// a second finger neither moves, steals nor releases the stick, presses on HUD buttons and the top strip are
    /// skipped, the centre follows a finger past the radius, a held frame allocates nothing, and the chevron opacity
    /// rule shows only the drag side(s). A virtual touchscreen (InputTestFixture) feeds real touch ids;
    /// <see cref="FloatingJoystick.Tick"/> is called right after each input update.
    /// </summary>
    public sealed class FloatingJoystickTests : InputTestFixture
    {
        private const float Eps = 1e-3f;

        private Touchscreen _screen;
        private GameObject _go;
        private FloatingJoystick _stick;
        private Vector2 _centre;
        private float _radius;

        [SetUp]
        public override void Setup()
        {
            base.Setup();
            FloatingJoystick.SimulatedValue = null;
            _screen = InputSystem.AddDevice<Touchscreen>();
            _screen.MakeCurrent();
            _go = new GameObject("Input");
            _stick = _go.AddComponent<FloatingJoystick>();
            _centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.3f);
            _radius = _stick.RadiusPixels;
        }

        [TearDown]
        public override void TearDown()
        {
            Object.DestroyImmediate(_go);
            FloatingJoystick.SimulatedValue = null;
            base.TearDown();
        }

        private void Begin(int id, Vector2 pos) { BeginTouch(id, pos); _stick.Tick(); }
        private void MoveTo(int id, Vector2 pos) { MoveTouch(id, pos); _stick.Tick(); }
        private void End(int id, Vector2 pos) { EndTouch(id, pos); _stick.Tick(); }

        private static void AssertVec(Vector2 expected, Vector2 actual)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(Eps), $"x of {actual}");
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(Eps), $"y of {actual}");
        }

        [Test]
        public void NoInput_IsIdleAndZero()
        {
            _stick.Tick();
            Assert.That(_stick.IsTouching, Is.False);
            AssertVec(Vector2.zero, _stick.Move);
        }

        [Test]
        public void TouchDown_MovesOriginToTouchPoint()
        {
            var p = new Vector2(Screen.width * 0.23f, Screen.height * 0.61f);
            Begin(1, p);
            Assert.That(_stick.IsTouching, Is.True);
            AssertVec(p, _stick.Origin);
            AssertVec(Vector2.zero, _stick.Move);
        }

        [Test]
        public void EightDirections_FullDrag_GiveUnitVector_AndReleaseGivesZero()
        {
            for (int i = 0; i < 8; i++)
            {
                float a = i * 45f * Mathf.Deg2Rad;
                var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                int id = i + 1;
                Begin(id, _centre);
                MoveTo(id, _centre + dir * _radius);
                AssertVec(dir, _stick.Move);
                End(id, _centre + dir * _radius);
                Assert.That(_stick.IsTouching, Is.False, $"direction {i}");
                AssertVec(Vector2.zero, _stick.Move);
            }
        }

        [Test]
        public void SmallDrag_InsideDeadZone_IsZero()
        {
            Begin(1, _centre);
            MoveTo(1, _centre + Vector2.right * (_radius * 0.05f));
            Assert.That(_stick.IsTouching, Is.True);
            AssertVec(Vector2.zero, _stick.Move);
            MoveTo(1, _centre + Vector2.right * (_radius * 0.55f));
            Assert.That(_stick.Move.x, Is.EqualTo(0.5f).Within(Eps)); // (0.55 - 0.1) / 0.9, rescaled past the dead zone
        }

        [Test]
        public void SecondFinger_DoesNotMoveOrStealOrRelease()
        {
            Begin(1, _centre);
            MoveTo(1, _centre + Vector2.right * _radius);
            var other = new Vector2(Screen.width * 0.15f, Screen.height * 0.15f);
            Begin(2, other);
            AssertVec(Vector2.right, _stick.Move);
            AssertVec(_centre, _stick.Origin);
            MoveTo(2, other + Vector2.up * (_radius * 2f));
            AssertVec(Vector2.right, _stick.Move);
            End(2, other);
            AssertVec(Vector2.right, _stick.Move);
            Assert.That(_stick.IsTouching, Is.True);
            End(1, _centre + Vector2.right * _radius);
            AssertVec(Vector2.zero, _stick.Move);
        }

        [Test]
        public void OwnerLifts_HeldSecondFinger_DoesNotTakeOver_NewTouchDoes()
        {
            Begin(1, _centre);
            var other = new Vector2(Screen.width * 0.8f, Screen.height * 0.2f);
            Begin(2, other);
            End(1, _centre);
            MoveTo(2, other + Vector2.left * _radius);
            Assert.That(_stick.IsTouching, Is.False);
            AssertVec(Vector2.zero, _stick.Move);

            Begin(3, _centre);
            MoveTo(3, _centre + Vector2.down * _radius);
            AssertVec(Vector2.down, _stick.Move);
        }

        [Test]
        public void PressOnHudButton_IsIgnored()
        {
            var button = new Rect(0f, 0f, Screen.width * 0.3f, Screen.height * 0.3f);
            _stick.IgnoreStartAt = p => button.Contains(p);
            var onButton = button.center;
            Begin(1, onButton);
            MoveTo(1, onButton + Vector2.up * _radius);
            Assert.That(_stick.IsTouching, Is.False);
            AssertVec(Vector2.zero, _stick.Move);
            End(1, onButton);

            Begin(2, _centre);
            Assert.That(_stick.IsTouching, Is.True);
        }

        [Test]
        public void PressInTopHudStrip_IsIgnored()
        {
            Begin(1, new Vector2(Screen.width * 0.5f, Screen.height * 0.95f));
            Assert.That(_stick.IsTouching, Is.False);
        }

        [Test]
        public void CentreFollowsFinger_SoReverseAnswersAtOnce()
        {
            Begin(1, _centre);
            var far = _centre + Vector2.right * (_radius * 3f);
            MoveTo(1, far);
            AssertVec(Vector2.right, _stick.Move);
            AssertVec(_centre + Vector2.right * (_radius * 2f), _stick.Origin);
            MoveTo(1, far + Vector2.left * (_radius * 2f)); // one radius left of the dragged centre
            AssertVec(Vector2.left, _stick.Move);
        }

        [Test]
        public void HeldFrame_AllocatesNothing()
        {
            Begin(1, _centre);
            MoveTo(1, _centre + new Vector2(0.6f, 0.8f) * _radius);
            Assert.That(() => _stick.Tick(), Is.Not.AllocatingGCMemory());
        }

        [Test]
        public void ChevronAlpha_OnlyDragSidesStay()
        {
            const float idle = 0.5f, faint = 0.2f;
            var dirs = JoystickView.SideDirections; // up, right, down, left
            float Side(Vector2 move, int i, bool touching = true) => JoystickView.SideAlpha(move, dirs[i], touching, idle, faint);

            for (int i = 0; i < 4; i++)
            {
                Assert.That(Side(Vector2.zero, i, touching: false), Is.EqualTo(idle), "idle shows all four");
                Assert.That(Side(Vector2.zero, i), Is.EqualTo(faint), "touch, no drag: all faint");
            }

            Assert.That(Side(Vector2.up, 0), Is.EqualTo(1f).Within(Eps));
            Assert.That(Side(Vector2.up, 1), Is.EqualTo(0f).Within(Eps));
            Assert.That(Side(Vector2.up, 2), Is.EqualTo(0f).Within(Eps));
            Assert.That(Side(Vector2.up, 3), Is.EqualTo(0f).Within(Eps));

            var diagonal = new Vector2(1f, 1f).normalized;
            Assert.That(Side(diagonal, 0), Is.EqualTo(0.7071f).Within(Eps), "diagonal: up shares");
            Assert.That(Side(diagonal, 1), Is.EqualTo(0.7071f).Within(Eps), "diagonal: right shares");
            Assert.That(Side(diagonal, 2), Is.EqualTo(0f).Within(Eps));

            var half = Vector2.up * 0.5f; // partial drag: the drag side grows, the rest fade
            Assert.That(Side(half, 0), Is.EqualTo(Mathf.Lerp(faint, 1f, 0.5f)).Within(Eps));
            Assert.That(Side(half, 1), Is.EqualTo(Mathf.Lerp(faint, 0f, 0.5f)).Within(Eps));
        }
    }
}
