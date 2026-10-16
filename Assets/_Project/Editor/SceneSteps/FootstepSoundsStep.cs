using SurvivorSouls.Audio;
using SurvivorSouls.Editor.Audio;
using SurvivorSouls.Gameplay.Characters;
using SurvivorSouls.Gameplay.Player;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.SceneSteps
{
    /// <summary>
    /// KAN-235 (KAN-81.2) Knight footsteps, Game scene side: builds the procedural step sets (snow, shingle, wood, stone;
    /// <see cref="SfxBuilder.BuildFootsteps"/>) and puts <see cref="FootstepSounds"/> on the audio object, wired to the
    /// Knight (body, mover, foot bones of its <see cref="HumanoidRig"/>), the shooter and the Knight's health (fight ducking).
    /// <see cref="GameAudio"/>'s own footstep array stays empty (its stride timer stays silent).
    /// </summary>
    public sealed class FootstepSoundsStep : IGameSceneStep
    {
        public int Order => 235;

        public void Build(GameSceneContext ctx)
        {
            var clips = SfxBuilder.BuildFootsteps();
            var sounds = ctx.Audio.gameObject.AddComponent<FootstepSounds>();
            var so = new SerializedObject(sounds);
            SetClips(so.FindProperty("snow"), clips.Snow);
            SetClips(so.FindProperty("shingle"), clips.Shingle);
            SetClips(so.FindProperty("wood"), clips.Wood);
            SetClips(so.FindProperty("stone"), clips.Stone);
            if (ctx.Player != null)
            {
                so.FindProperty("body").objectReferenceValue = ctx.Player.transform;
                so.FindProperty("mover").objectReferenceValue = ctx.Player.GetComponent<PlayerMover>();
                var rig = ctx.Player.GetComponentInChildren<HumanoidRig>();
                if (rig != null)
                {
                    so.FindProperty("footL").objectReferenceValue = rig.footL;
                    so.FindProperty("footR").objectReferenceValue = rig.footR;
                }
                else Debug.LogWarning("[FootstepSoundsStep] Knight has no HumanoidRig: footsteps use the stride timer");
            }
            so.FindProperty("shooter").objectReferenceValue = ctx.Shooter;
            so.FindProperty("health").objectReferenceValue = ctx.Health;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetClips(SerializedProperty array, AudioClip[] clips)
        {
            array.arraySize = clips.Length;
            for (int i = 0; i < clips.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        }
    }
}
