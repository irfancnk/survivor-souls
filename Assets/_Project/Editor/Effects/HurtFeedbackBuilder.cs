using SurvivorSouls.Audio;
using SurvivorSouls.Editor.Audio;
using SurvivorSouls.Gameplay.CameraRig;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.UI.Hud;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Effects
{
    /// <summary>
    /// Wires the Knight hurt feedback (KAN-187) into the generated Game scene: <see cref="PlayerHurtFeedback"/> on the Knight
    /// (camera shake, hit-stop, haptic), <see cref="HitDirectionHud"/> on the HUD document (hit direction arc) and
    /// <see cref="HurtSounds"/> on the audio host (grunt + low-health heartbeat, clips from <see cref="SfxBuilder"/>).
    /// The Knight hit flash itself stays <see cref="PlayerHitFlash"/> (added with the health in GameSceneBuilder).
    /// </summary>
    public static class HurtFeedbackBuilder
    {
        public static void Create(PlayerHealth health, TopDownFollowCamera followCamera, Camera worldCamera, GameObject hud,
            GameObject audioHost, SfxBuilder.Set sfx)
        {
            var feedback = health.gameObject.AddComponent<PlayerHurtFeedback>();
            var so = new SerializedObject(feedback);
            so.FindProperty("followCamera").objectReferenceValue = followCamera;
            so.ApplyModifiedPropertiesWithoutUndo();

            var arcs = hud.AddComponent<HitDirectionHud>();
            so = new SerializedObject(arcs);
            so.FindProperty("health").objectReferenceValue = health;
            so.FindProperty("worldCamera").objectReferenceValue = worldCamera;
            so.ApplyModifiedPropertiesWithoutUndo();

            var sounds = audioHost.AddComponent<HurtSounds>();
            so = new SerializedObject(sounds);
            so.FindProperty("health").objectReferenceValue = health;
            var grunts = so.FindProperty("grunts");
            grunts.arraySize = sfx.KnightGrunts.Length;
            for (int i = 0; i < sfx.KnightGrunts.Length; i++) grunts.GetArrayElementAtIndex(i).objectReferenceValue = sfx.KnightGrunts[i];
            so.FindProperty("heartbeat").objectReferenceValue = sfx.Heartbeat;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
