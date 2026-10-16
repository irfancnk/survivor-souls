using NUnit.Framework;
using SurvivorSouls.Audio;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-15: the old snow steps stay gone. The generated Game scene's <see cref="GameAudio"/> has no footstep clips (its
    /// stride timer stays silent; KAN-235 <see cref="FootstepSounds"/> plays the new quiet steps, see FootstepSfxTests), the
    /// old "sfx_footstep" clips are gone from the project, and every other sound set is still wired.
    /// </summary>
    public sealed class FootstepRemovalTests
    {
        private const string GameScene = "Assets/_Project/Scenes/Game.unity";

        private static readonly string[] OtherClipSets =
        {
            "shots", "impacts", "deaths", "hitMarkers", "killMarkers", "soulChimes", "levelUps", "boonPicks",
            "portalOpens", "portalEnters", "chamberArrives",
        };

        [Test]
        public void GameScene_GameAudio_HasNoFootstepClips_OtherSoundsStayWired()
        {
            var scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Additive);
            try
            {
                GameAudio audio = null;
                foreach (var root in scene.GetRootGameObjects())
                {
                    audio = root.GetComponentInChildren<GameAudio>(true);
                    if (audio != null) break;
                }
                Assert.That(audio, Is.Not.Null, "Game scene has a GameAudio");

                var so = new SerializedObject(audio);
                var footsteps = so.FindProperty("footsteps");
                Assert.That(footsteps, Is.Not.Null, "the footstep hook stays for a later, quieter set");
                Assert.That(footsteps.arraySize, Is.Zero, "no footstep clip: the Knight runs silently");
                Assert.That(so.FindProperty("player").objectReferenceValue, Is.Not.Null, "player stays wired for the hook");

                foreach (string set in OtherClipSets)
                {
                    var clips = so.FindProperty(set);
                    Assert.That(clips, Is.Not.Null, set);
                    Assert.That(clips.arraySize, Is.GreaterThan(0), $"{set} still has clips");
                    for (int i = 0; i < clips.arraySize; i++)
                        Assert.That(clips.GetArrayElementAtIndex(i).objectReferenceValue, Is.Not.Null, $"{set}[{i}] is not a missing reference");
                }
                Assert.That(so.FindProperty("ambience").objectReferenceValue, Is.Not.Null, "ambience still wired");
            }
            finally
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        [Test]
        public void SnowFootstepClips_AreGoneFromTheProject()
        {
            Assert.That(AssetDatabase.FindAssets("sfx_footstep t:AudioClip"), Is.Empty, "no unused footstep clip left behind");
        }
    }
}
