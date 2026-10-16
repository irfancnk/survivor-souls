using SurvivorSouls.Audio;
using SurvivorSouls.Editor.Audio;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.SceneSteps
{
    /// <summary>
    /// KAN-234 (KAN-81.1) zombie death sounds, Game scene side: builds the procedural death voices, body falls and mass
    /// death clips (<see cref="SfxBuilder.BuildZombieDeaths"/>), puts <see cref="ZombieDeathSounds"/> on the audio object
    /// (Knight = distance fade, game camera = pan) and points <see cref="GameAudio"/> at it, so enemy kills stop using the
    /// shared groan.
    /// </summary>
    public sealed class ZombieDeathSoundsStep : IGameSceneStep
    {
        public int Order => 234;

        public void Build(GameSceneContext ctx)
        {
            var clips = SfxBuilder.BuildZombieDeaths();
            var sounds = ctx.Audio.gameObject.AddComponent<ZombieDeathSounds>();
            var so = new SerializedObject(sounds);
            SetClips(so.FindProperty("voices"), clips.Voices);
            SetClips(so.FindProperty("falls"), clips.Falls);
            SetClips(so.FindProperty("masses"), clips.Masses);
            so.FindProperty("listener").objectReferenceValue = ctx.Player != null ? ctx.Player.transform : null;
            so.FindProperty("view").objectReferenceValue = ctx.Camera;
            so.ApplyModifiedPropertiesWithoutUndo();
            GameSceneContext.SetRef(ctx.Audio, "zombieDeaths", sounds);
        }

        private static void SetClips(SerializedProperty array, AudioClip[] clips)
        {
            array.arraySize = clips.Length;
            for (int i = 0; i < clips.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        }
    }
}
