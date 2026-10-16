using SurvivorSouls.Audio;
using SurvivorSouls.Editor.Audio;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.SceneSteps
{
    /// <summary>
    /// KAN-236 (KAN-81.3) shot channel + mix bus, Game scene side:
    /// <list type="bullet">
    /// <item>builds the 8 crossbow shot variants + volley layers (<see cref="SfxBuilder.BuildShotChannel"/>), puts
    /// <see cref="ShotSounds"/> on the audio object (wired to the shooter) and points <see cref="GameAudio"/> at it, so the
    /// Knight's shots stop using the shared pool;</item>
    /// <item>puts <see cref="MasterLimiter"/> on the game camera's AudioListener;</item>
    /// <item>loads (makes once) <c>SurvivorSouls.mixer</c> (<see cref="AudioMixerBuilder"/>) and adds
    /// <see cref="AudioMixerRouting"/> with its groups to the audio object.</item>
    /// </list>
    /// </summary>
    public sealed class ShotChannelStep : IGameSceneStep
    {
        public int Order => 236;

        public void Build(GameSceneContext ctx)
        {
            var clips = SfxBuilder.BuildShotChannel();
            var shots = ctx.Audio.gameObject.AddComponent<ShotSounds>();
            var so = new SerializedObject(shots);
            SetClips(so.FindProperty("variants"), clips.Variants);
            SetClips(so.FindProperty("volleyLayers"), clips.VolleyLayers);
            so.FindProperty("shooter").objectReferenceValue = ctx.Shooter;
            so.ApplyModifiedPropertiesWithoutUndo();
            GameSceneContext.SetRef(ctx.Audio, "shotChannel", shots);

            var listener = ctx.Camera != null ? ctx.Camera.GetComponent<AudioListener>() : Object.FindAnyObjectByType<AudioListener>();
            if (listener != null) listener.gameObject.AddComponent<MasterLimiter>();
            else Debug.LogWarning("[ShotChannelStep] no AudioListener: no master limiter");

            var groups = AudioMixerBuilder.Ensure();
            if (groups.Mixer == null)
            {
                Debug.LogWarning("[ShotChannelStep] no mixer: sources keep the direct path (the limiter still guards them)");
                return;
            }
            var routing = ctx.Audio.gameObject.AddComponent<AudioMixerRouting>();
            var rso = new SerializedObject(routing);
            rso.FindProperty("sfx").objectReferenceValue = groups.Sfx;
            rso.FindProperty("shots").objectReferenceValue = groups.Shots;
            rso.FindProperty("footsteps").objectReferenceValue = groups.Footsteps;
            rso.FindProperty("deaths").objectReferenceValue = groups.Deaths;
            rso.FindProperty("music").objectReferenceValue = groups.Music;
            rso.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetClips(SerializedProperty array, AudioClip[] clips)
        {
            array.arraySize = clips.Length;
            for (int i = 0; i < clips.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        }
    }
}
