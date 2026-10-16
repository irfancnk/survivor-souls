using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.World;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-232 acceptance: the Chamber room limits the playable area inside the tiles, walkability blocks outside it, the
    /// exit door is always inside it; the Game scene has the 40 x 40 m room with stone walls and 4 edge colliders that
    /// hide in a boss Chamber.
    /// </summary>
    public sealed class ChamberRoomTests
    {
        private const int TestRealm = 97;
        private readonly List<Object> _objects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            RunProgress.Clear(TestRealm);
            RunState.NewRun();
        }

        private WorldStreamer World()
        {
            var go = new GameObject("World");
            _objects.Add(go);
            return go.AddComponent<WorldStreamer>();
        }

        [Test]
        public void Room_LimitsThePlayableArea()
        {
            var world = World();
            Assert.That(world.HasRoom, Is.False, "no room: the tile area as before");
            Assert.That(world.PlayableMin, Is.EqualTo(world.TileAreaMin));
            world.SetRoom(new Vector2(-10f, -20f), new Vector2(10f, 5f));
            Assert.That(world.HasRoom, Is.True);
            Assert.That(world.PlayableMin, Is.EqualTo(new Vector2(-10f, -20f)));
            Assert.That(world.PlayableMax, Is.EqualTo(new Vector2(10f, 5f)));
            Assert.That(world.IsBlocked(new Vector3(9.8f, 0f, 0f), 0.5f), Is.True, "body crosses the east wall");
            Assert.That(world.IsBlocked(new Vector3(0f, 0f, -19.8f), 0.5f), Is.True, "body crosses the south wall");
            Assert.That(world.IsBlocked(new Vector3(25f, 0f, 0f), 0.5f), Is.True, "outside the room");
            world.SetRoom(new Vector2(-500f, -500f), new Vector2(500f, 500f));
            Assert.That(world.PlayableMin, Is.EqualTo(world.TileAreaMin), "the room never grows past the tiles");
            Assert.That(world.PlayableMax, Is.EqualTo(world.TileAreaMax));
        }

        private static void Set(object target, string field, object value) =>
            typeof(WorldStreamer).GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);

        [Test]
        public void Room_ExitIsAlwaysInside()
        {
            var world = World();
            Set(world, "halfTiles", 2); // the WorldBuilder layout (KAN-232)
            Set(world, "southRows", 3);
            Set(world, "exitDistance", new Vector2Int(1, 2));
            var exits = new HashSet<Vector2Int>();
            world.SetRoom(new Vector2(-20f, -28.5f), new Vector2(20f, 11.5f));
            for (int s = 1; s <= 60; s++)
            {
                world.UseSeed(ChamberDifficulty.MixSeed(7, 1, s));
                var exit = world.ExitPosition;
                Assert.That(exit.x, Is.InRange(-16f, 16f), $"seed {s}: exit {exit}");
                Assert.That(exit.z, Is.InRange(-24.5f, 7.5f), $"seed {s}: exit {exit}");
                Assert.That(world.ExitCell, Is.Not.EqualTo(Vector2Int.zero), "never on the spawn tile");
                exits.Add(world.ExitCell);
            }
            Assert.That(exits.Count, Is.GreaterThanOrEqualTo(4), "Chamber seeds still move the exit around the room");
        }

        [UnityTest]
        public IEnumerator GameScene_HasTheWalledRoom()
        {
            RunState.NewRun(TestRealm, 2320);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;
            var world = Object.FindAnyObjectByType<WorldStreamer>();
            Assert.That(world, Is.Not.Null);
            Assert.That(world.HasRoom, Is.True);
            Vector2 size = world.PlayableMax - world.PlayableMin;
            Assert.That(size.x, Is.EqualTo(40f).Within(0.01f), "40 m wide");
            Assert.That(size.y, Is.EqualTo(40f).Within(0.01f), "40 m deep");
            Assert.That(world.PlayableMin.x, Is.EqualTo(-20f).Within(0.01f));
            var walls = Object.FindAnyObjectByType<RoomWalls>();
            Assert.That(walls, Is.Not.Null, "Room Walls object");
            Assert.That(walls.GetComponentsInChildren<BoxCollider>().Length, Is.EqualTo(4), "4 edge colliders");
            Assert.That(walls.GetComponentsInChildren<MeshRenderer>().Length, Is.GreaterThanOrEqualTo(2), "stone + snow");
            Assert.That(walls.gameObject.activeInHierarchy, Is.True, "Chamber 1: walls up");
            var exit = world.ExitPosition;
            Assert.That(exit.x, Is.InRange(world.PlayableMin.x, world.PlayableMax.x));
            Assert.That(exit.z, Is.InRange(world.PlayableMin.y, world.PlayableMax.y));

            var empty = SceneManager.CreateScene("ChamberRoomEmpty");
            SceneManager.SetActiveScene(empty);
            yield return SceneManager.UnloadSceneAsync("Game");
        }
    }
}
