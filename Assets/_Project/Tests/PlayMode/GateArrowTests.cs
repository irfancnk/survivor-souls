using System.Collections;
using System.IO;
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
    /// KAN-63 (+ KAN-33): the gate arrow lives on the screen edge, not under the Knight. Edge math (projection in all
    /// directions incl. straight up / down, angle, on-screen hysteresis); the built Game scene has no ground arrow, the HUD
    /// arrow sits on the allowed edge rectangle (clear of the HUD top band and the joystick), points at the gate, is dim
    /// while the portal is locked and bright once it opens, hides while the gate is on screen and when the Knight dies
    /// (target off), allocates nothing per frame; the baked Blender sprite is under 150 KB.
    /// KAN-176: the edge rectangle hugs the safe area (2.5 mm + half the arrow), no-go zones push the arrow along the same
    /// edge (inward only when a whole edge line is blocked), the arrow never overlaps a HUD zone, no distance label.
    /// Look: Docs/screenshots/game-kan63-*.png, Docs/screenshots/kan176-*.png.
    /// </summary>
    public sealed class GateArrowTests
    {
        private const int TestRealm = 97; // never touches the saved progress of a real Realm
        private const string SpritePath = "_Project/Art/UI/T_UI_GateArrow.png";
        private static readonly Rect Box = Rect.MinMaxRect(100f, 400f, 900f, 1400f);

        // ------------------------------------------------------------ Edge math

        [Test]
        public void ProjectToEdge_HitsTheRightSide_InEveryDirection()
        {
            var c = new Vector2(500f, 900f);
            Assert.That(EdgeMarker.ProjectToEdge(c, Vector2.up, Box), Is.EqualTo(new Vector2(500f, 1400f)), "panel +y = screen down");
            Assert.That(EdgeMarker.ProjectToEdge(c, Vector2.down, Box), Is.EqualTo(new Vector2(500f, 400f)), "straight up in portrait");
            Assert.That(EdgeMarker.ProjectToEdge(c, Vector2.right, Box), Is.EqualTo(new Vector2(900f, 900f)));
            Assert.That(EdgeMarker.ProjectToEdge(c, Vector2.left, Box), Is.EqualTo(new Vector2(100f, 900f)));
            Vector2 diag = EdgeMarker.ProjectToEdge(c, new Vector2(1f, -1f).normalized, Box);
            Assert.That(diag.x, Is.EqualTo(900f).Within(0.01f), "45 deg up-right leaves through the right side (box is tall)");
            Assert.That(diag.y, Is.EqualTo(500f).Within(0.01f));
        }

        [Test]
        public void ProjectToEdge_StartOutside_IsClampedIn_AndCornersAreContinuous()
        {
            Vector2 p = EdgeMarker.ProjectToEdge(new Vector2(500f, 100f), Vector2.down, Box);
            Assert.That(p, Is.EqualTo(new Vector2(500f, 400f)), "start above the box: clamped, stays on the top edge");
            var c = new Vector2(500f, 900f);
            Vector2 corner = new Vector2(900f, 400f) - c;
            float a = Mathf.Atan2(corner.y, corner.x);
            Vector2 before = EdgeMarker.ProjectToEdge(c, new Vector2(Mathf.Cos(a - 0.001f), Mathf.Sin(a - 0.001f)), Box);
            Vector2 after = EdgeMarker.ProjectToEdge(c, new Vector2(Mathf.Cos(a + 0.001f), Mathf.Sin(a + 0.001f)), Box);
            Assert.That(Vector2.Distance(before, after), Is.LessThan(2f), "no jump at the corner");
        }

        [Test]
        public void Angle_TurnsTheTipUpSprite_Clockwise()
        {
            Assert.That(EdgeMarker.Angle(Vector2.down), Is.EqualTo(0f).Within(0.01f), "panel -y = screen up = no turn");
            Assert.That(EdgeMarker.Angle(Vector2.right), Is.EqualTo(90f).Within(0.01f));
            Assert.That(Mathf.Abs(EdgeMarker.Angle(Vector2.up)), Is.EqualTo(180f).Within(0.01f));
            Assert.That(EdgeMarker.Angle(Vector2.left), Is.EqualTo(-90f).Within(0.01f));
        }

        [Test]
        public void OnScreen_HasHysteresis_AtTheBorder()
        {
            var view = new Rect(0f, 0f, 1080f, 1920f);
            var nearBorder = new Vector2(60f, 960f); // 60 inside the left edge
            Assert.That(EdgeMarker.OnScreen(false, nearBorder, view, 110f, 30f), Is.False, "off screen: must come 110 inside");
            Assert.That(EdgeMarker.OnScreen(true, nearBorder, view, 110f, 30f), Is.True, "on screen: stays until under 30 inside");
            Assert.That(EdgeMarker.OnScreen(true, new Vector2(10f, 960f), view, 110f, 30f), Is.False);
            Assert.That(EdgeMarker.OnScreen(false, new Vector2(540f, 960f), view, 110f, 30f), Is.True);
        }

        [Test]
        public void MillimetresToPanel_UsesDpiAndPanelScale()
        {
            // iPhone 14 Pro Max: 460 dpi, 1290 px wide, panel 1080 wide -> 2.5 mm = ~37.9 panel units.
            Assert.That(EdgeMarker.MillimetresToPanel(2.5f, 460f, 1080f / 1290f), Is.EqualTo(37.9f).Within(0.1f));
            Assert.That(EdgeMarker.MillimetresToPanel(25.4f, 100f, 1f), Is.EqualTo(100f).Within(0.001f), "1 inch = dpi pixels");
            Assert.That(EdgeMarker.MillimetresToPanel(0f, 460f, 1f), Is.EqualTo(0f));
        }

        // KAN-176 no-go zones: Box border = arrow centre line, half = 40 (an 80 px arrow).
        private const float Half = 40f;

        [Test]
        public void AvoidZones_FreePoint_StaysPut()
        {
            var zones = new[] { Rect.MinMaxRect(0f, 0f, 200f, 200f) };
            var p = new Vector2(500f, Box.yMax);
            Assert.That(EdgeMarker.AvoidZones(p, Box, Half, zones, 1), Is.EqualTo(p));
            Assert.That(EdgeMarker.AvoidZones(p, Box, Half, null, 0), Is.EqualTo(p));
        }

        [Test]
        public void AvoidZones_Joystick_SlidesAlongTheBottomEdge_ToTheNearestSide()
        {
            var joystick = Rect.MinMaxRect(380f, 1250f, 620f, 1500f);
            var zones = new[] { joystick };
            Vector2 left = EdgeMarker.AvoidZones(new Vector2(470f, Box.yMax), Box, Half, zones, 1);
            Assert.That(left.y, Is.EqualTo(Box.yMax), "stays on the bottom edge (never pulled inward)");
            Assert.That(left.x, Is.EqualTo(380f - Half).Within(0.1f), "nearest free spot: left of the ring");
            Vector2 right = EdgeMarker.AvoidZones(new Vector2(560f, Box.yMax), Box, Half, zones, 1);
            Assert.That(right.x, Is.EqualTo(620f + Half).Within(0.1f), "nearest free spot: right of the ring");
            Assert.That(EdgeMarker.Free(left, Half, zones, 1) && EdgeMarker.Free(right, Half, zones, 1), Is.True);
        }

        [Test]
        public void AvoidZones_SideEdge_SlidesDownBelowTheTopBand()
        {
            var zones = new[] { Rect.MinMaxRect(0f, 0f, 1080f, 460f), Rect.MinMaxRect(60f, 460f, 300f, 560f) }; // bars, skill icons
            Vector2 p = EdgeMarker.AvoidZones(new Vector2(Box.xMin, 430f), Box, Half, zones, 2);
            Assert.That(p.x, Is.EqualTo(Box.xMin), "stays on the left edge");
            Assert.That(p.y, Is.EqualTo(560f + Half).Within(0.1f), "just below the skill icons");
        }

        [Test]
        public void AvoidZones_FullyBlockedTopEdge_StepsInward_ThenSlidesClearOfTheTimer()
        {
            var bars = Rect.MinMaxRect(0f, 0f, 1080f, 470f); // full-width XP + health rows
            var timer = Rect.MinMaxRect(440f, 470f, 640f, 540f);
            var zones = new[] { bars, timer };
            Vector2 p = EdgeMarker.AvoidZones(new Vector2(620f, Box.yMin), Box, Half, zones, 2);
            Assert.That(p.y, Is.EqualTo(470f + Half).Within(0.1f), "steps inward only past the bars");
            Assert.That(p.x, Is.EqualTo(640f + Half).Within(0.1f), "then slides along that line clear of the timer");
            Vector2 straightUp = EdgeMarker.AvoidZones(new Vector2(540f, Box.yMin), Box, Half, zones, 2);
            Assert.That(EdgeMarker.Free(straightUp, Half, zones, 2), Is.True);
            Assert.That(straightUp.y, Is.LessThan(560f + Half + 0.5f), "close under the HUD, not pushed deep into the map");
        }

        [Test]
        public void AvoidZones_Corner_PicksTheNearestFreeEdge()
        {
            var bars = Rect.MinMaxRect(0f, 0f, 1080f, 470f);
            var zones = new[] { bars };
            Vector2 p = EdgeMarker.AvoidZones(new Vector2(Box.xMax, Box.yMin), Box, Half, zones, 1);
            Assert.That(p.x, Is.EqualTo(Box.xMax), "top-right corner: the right edge, just below the bars");
            Assert.That(p.y, Is.EqualTo(470f + Half).Within(0.1f));
        }

        [Test]
        public void Sprite_IsUnder150KB()
        {
            var info = new FileInfo(Path.Combine(Application.dataPath, SpritePath));
            Assert.That(info.Exists, Is.True, "baked by Tools/Blender/build_gate_arrow.py");
            Assert.That(info.Length, Is.LessThan(150 * 1024));
        }

        // ------------------------------------------------------------ Real scene

        [UnityTest]
        public IEnumerator GameScene_ArrowOnTheEdge_PointsAtTheGate_HidesWhenNeeded()
        {
            RunState.NewRun(TestRealm, 6301);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;

            var player = Object.FindAnyObjectByType<PlayerMover>().transform;
            foreach (var r in player.GetComponentsInChildren<MeshRenderer>(true))
                Assert.That(r.name.Contains("Arrow"), Is.False, $"no ground arrow under the Knight ({r.name})");
            var objective = player.GetComponent<ObjectiveArrow>();
            var hud = Object.FindAnyObjectByType<GateArrowHud>();
            Assert.That(objective, Is.Not.Null);
            Assert.That(hud, Is.Not.Null, "HUD has the edge arrow");
            var icon = hud.GetComponent<UIDocument>().rootVisualElement.Q("gate-arrow-icon");
            Assert.That(icon, Is.Not.Null);
            Assert.That(icon.resolvedStyle.backgroundImage.texture, Is.Not.Null, "baked arrow sprite wired");
            Assert.That(icon.pickingMode, Is.EqualTo(PickingMode.Ignore), "never blocks touches");

            Assert.That(objective.HasTarget, Is.True, "shore Chamber: points to the exit");
            Assert.That(objective.Open, Is.False, "timer running: portal locked (dim arrow)");
            for (float t = 0f; t < 1f; t += Time.unscaledDeltaTime) yield return null;
            Assert.That(hud.GateOnScreen, Is.False, "exit is far at the Chamber start");
            Assert.That(hud.Visible, Is.True, "locked portal: dim arrow already shows");

            Assert.That(hud.GetComponent<UIDocument>().rootVisualElement.Q("gate-arrow-dist"), Is.Null, "no distance label (KAN-176)");
            Rect edge = hud.EdgeRect;
            Vector2 p = hud.Position;
            float half = hud.Size * 0.5f;
            Vector2 panelSize = icon.panel.visualTree.layout.size;
            if (Mathf.Min(panelSize.x, panelSize.y) > 2f * (hud.EdgeInset + half) + 10f) // a tiny test window collapses the rectangle to the centre
            {
                // KAN-176: the rectangle hugs the safe area: outer arrow edge = 2.5 mm (EdgeInset) from it.
                Rect safe = Screen.safeArea;
                float sx = panelSize.x / Screen.width, sy = panelSize.y / Screen.height;
                Assert.That(edge.xMin, Is.EqualTo(safe.xMin * sx + hud.EdgeInset + half).Within(0.5f));
                Assert.That(edge.yMin, Is.EqualTo((Screen.height - safe.yMax) * sy + hud.EdgeInset + half).Within(0.5f));
                Assert.That(edge.yMax, Is.EqualTo((Screen.height - safe.yMin) * sy - hud.EdgeInset - half).Within(0.5f));
                Assert.That(hud.EdgeInset, Is.GreaterThan(0f));
            }
            Assert.That(hud.ZoneCount, Is.GreaterThanOrEqualTo(4), "XP bar, timer, pause, health (+ souls, joystick) are no-go zones");
            Assert.That(EdgeMarker.Free(p, half - 1f, hud.Zones, hud.ZoneCount), Is.True, $"arrow {p} overlaps no HUD zone");
            float toEdge = Mathf.Min(Mathf.Abs(p.x - edge.xMin), Mathf.Abs(p.x - edge.xMax), Mathf.Abs(p.y - edge.yMin), Mathf.Abs(p.y - edge.yMax));
            bool onEdge = toEdge < 3f;
            bool againstZone = !EdgeMarker.Free(p, half + 3f, hud.Zones, hud.ZoneCount);
            Assert.That(onEdge || againstZone, Is.True, $"arrow on the edge rectangle or right against a HUD zone ({p} in {edge})");

            var cam = Camera.main;
            Vector3 flat = objective.Target - player.position;
            flat.y = 0f;
            Vector2 a = cam.WorldToScreenPoint(player.position), b = cam.WorldToScreenPoint(player.position + flat.normalized * 6f);
            Vector2 screenDir = b - a;
            float expected = EdgeMarker.Angle(new Vector2(screenDir.x, -screenDir.y)); // screen y up -> panel y down
            Assert.That(Mathf.Abs(Mathf.DeltaAngle(hud.Angle, expected)), Is.LessThan(3f), "points at the gate");

            hud.Tick(1f / 60f); // warm up
            Assert.That(() => { for (int i = 0; i < 30; i++) hud.Tick(1f / 60f); }, Is.Not.AllocatingGCMemory(), "no allocation per frame");

            SurvivalTimer.Active.Complete();
            yield return null;
            Assert.That(objective.Open, Is.True, "timer done: portal open (bright arrow)");

            // Next to the gate: it is on screen, the arrow fades out.
            Teleport(player, objective.Target + new Vector3(0f, 0f, -5.5f));
            for (float t = 0f; t < 0.8f; t += Time.unscaledDeltaTime) yield return null;
            Assert.That(hud.GateOnScreen || !objective.HasTarget, Is.True);
            Assert.That(hud.Visible, Is.False, "gate in view: no arrow");

            // Far again, then the Knight dies (target off): the arrow fades out.
            Teleport(player, objective.Target + new Vector3(0f, 0f, -60f));
            for (float t = 0f; t < 0.8f; t += Time.unscaledDeltaTime) yield return null;
            Assert.That(hud.Visible, Is.True, "far again: arrow back");
            objective.enabled = false;
            for (float t = 0f; t < 0.6f; t += Time.unscaledDeltaTime) yield return null;
            Assert.That(hud.Visible, Is.False, "no target (death disables it): arrow gone");

            var empty = SceneManager.CreateScene("GateArrowEmpty");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
            GamePause.Clear();
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
