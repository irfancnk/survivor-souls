using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Editor.World;
using SurvivorSouls.FX;
using SurvivorSouls.Gameplay.Player;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurvivorSouls.Editor.Effects
{
    /// <summary>
    /// Adds the running wind trail (KAN-14) under the player: one world-space particle system with no emission module
    /// (<see cref="WindTrail"/> emits by script), stretched billboards so each particle reads as a streak along its own
    /// velocity, alpha blend with the shared soft dot (URP Particles/Unlit, no lights, no shadows), cap 30 particles.
    /// Called by GameSceneBuilder on the player instance in the scene.
    /// </summary>
    public static class WindTrailBuilder
    {
        private const string MaterialPath = "Assets/_Project/Art/Materials/M_FX_Wind.mat";
        public const int MaxParticles = 30; // ticket budget

        public static WindTrail Create(GameArtKit kit, GameObject player)
        {
            var mat = EditorAssetUtil.ParticleMaterial(MaterialPath, kit.SoftDot, additive: false);

            var ps = PropKit.Particles("Wind Trail", player.transform, mat);
            ps.transform.localRotation = Quaternion.identity;
            var main = ps.main;
            main.loop = false;
            main.prewarm = false;
            main.playOnAwake = false;
            main.maxParticles = MaxParticles;
            main.startSpeed = 0f; // every value below comes per particle from WindTrail's EmitParams
            main.startLifetime = 0.4f;
            main.startSize = 0.05f;
            main.scalingMode = ParticleSystemScalingMode.Shape;
            main.cullingMode = ParticleSystemCullingMode.AlwaysSimulate; // world space, short-lived: no culling bookkeeping

            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = false;

            // Air drag slows each particle as it fades, so streaks shrink and settle instead of flying off.
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 100f;
            limit.drag = 2.5f;
            limit.multiplyDragByParticleSize = false;
            limit.multiplyDragByParticleVelocity = false;

            // Quick fade in (no pop at the spawn point), long fade out.
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = PropKit.Gradient(new[] { (Color.white, 0f), (Color.white, 1f) },
                new[] { (0f, 0f), (1f, 0.15f), (0.7f, 0.55f), (0f, 1f) });
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.8f), new Keyframe(0.25f, 1f), new Keyframe(1f, 0.55f)));

            var r = ps.GetComponent<ParticleSystemRenderer>();
            r.renderMode = ParticleSystemRenderMode.Stretch;
            r.lengthScale = 1.6f;
            r.velocityScale = 0.22f; // streak drifting 3 m/s is ~0.8 m long; slow specks stay short dashes
            r.cameraVelocityScale = 0f;
            r.lightProbeUsage = LightProbeUsage.Off;
            r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var trail = player.AddComponent<WindTrail>();
            var so = new SerializedObject(trail);
            so.FindProperty("mover").objectReferenceValue = player.GetComponent<PlayerMover>();
            so.FindProperty("system").objectReferenceValue = ps;
            so.ApplyModifiedPropertiesWithoutUndo();
            return trail;
        }
    }
}
