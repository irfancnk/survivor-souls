using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Gameplay.Chamber;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurvivorSouls.Editor.World
{
    /// <summary>
    /// Exit portal in the gate arch of the exit tile (KAN-39), built into the tile prefab by <see cref="WorldBuilder"/>:
    /// portal surface quad (Portal shader), door blocker collider, light beam + ground glow quads (PortalGlow shader,
    /// additive, no real light), three small particle systems (locked embers, open in-pull motes, opening burst),
    /// a 3D hum AudioSource and the <see cref="PortalGate"/> state machine. Arch opening size comes from
    /// <see cref="PropKit.DoorGate"/> (pillars at x +-1.3, 0.45 wide; lintel bottom at 3.475 m).
    /// Materials keep their tuned style values across rebuilds (only the shader and geometry values are written).
    /// </summary>
    public static class PortalBuilder
    {
        private const string MatDir = "Assets/_Project/Art/Materials/Game";
        public const string PortalShader = "SurvivorSouls/Portal";
        public const string GlowShader = "SurvivorSouls/PortalGlow";

        private const float Width = 2.2f;    // a little wider than the 2.15 m opening: the pillars hide the edge
        private const float Height = 3.5f;   // ground to just under the lintel
        private const float TriggerRadius = 1f;

        /// <summary>Adds the portal under <paramref name="tile"/> at the arch base <paramref name="local"/> (door plane).</summary>
        public static PortalGate Build(Transform tile, Vector3 local, GameArtKit kit, Material particleGlow, AudioClip hum)
        {
            var holder = new GameObject("Portal");
            holder.transform.SetParent(tile, false);
            holder.transform.localPosition = local;

            var surfaceMat = EditorAssetUtil.LoadOrCreateMaterial($"{MatDir}/M_Portal.mat", PortalShader);
            surfaceMat.SetFloat("_Aspect", Width / Height);
            surfaceMat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(surfaceMat);
            var beamMat = GlowMaterial("M_PortalBeam.mat", shape: 1f, intensity: 0.85f);
            var groundMat = GlowMaterial("M_PortalGround.mat", shape: 0f, intensity: 0.75f);

            var surface = Quad("Surface", holder.transform, surfaceMat, new Vector3(0f, Height * 0.5f, 0f), Quaternion.identity, new Vector3(Width, Height, 1f), kit);
            var beam = Quad("Beam", holder.transform, beamMat, new Vector3(0f, 7.6f, 0.05f), Quaternion.identity, new Vector3(2.6f, 14f, 1f), kit);
            var ground = Quad("Ground Glow", holder.transform, groundMat, new Vector3(0f, 0.05f, -1.4f), Quaternion.Euler(90f, 0f, 0f), new Vector3(5.2f, 4.2f, 1f), kit);

            var block = new GameObject("Door Block");
            block.transform.SetParent(holder.transform, false);
            block.transform.localPosition = new Vector3(0f, 1.5f, 0.15f);
            block.AddComponent<BoxCollider>().size = new Vector3(2.2f, 3f, 1.1f);

            var locked = LockedEmbers(holder.transform, particleGlow);
            var motes = InPullMotes(holder.transform, particleGlow);
            var burst = Burst(holder.transform, particleGlow);
            var source = HumSource(holder, hum);

            var gate = holder.AddComponent<PortalGate>();
            var so = new SerializedObject(gate);
            so.FindProperty("surface").objectReferenceValue = surface;
            so.FindProperty("blocker").objectReferenceValue = block.GetComponent<BoxCollider>();
            so.FindProperty("beam").objectReferenceValue = beam;
            so.FindProperty("groundGlow").objectReferenceValue = ground;
            so.FindProperty("lockedFx").objectReferenceValue = locked;
            so.FindProperty("openFx").objectReferenceValue = motes;
            so.FindProperty("burstFx").objectReferenceValue = burst;
            so.FindProperty("hum").objectReferenceValue = source;
            so.FindProperty("triggerPoint").vector3Value = Vector3.zero;
            so.FindProperty("triggerRadius").floatValue = TriggerRadius;
            so.ApplyModifiedPropertiesWithoutUndo();
            return gate;
        }

        private static Material GlowMaterial(string file, float shape, float intensity)
        {
            var mat = EditorAssetUtil.LoadOrCreateMaterial($"{MatDir}/{file}", GlowShader);
            mat.SetFloat("_Shape", shape);
            if (!mat.HasProperty("_Intensity") || Mathf.Approximately(mat.GetFloat("_Intensity"), 1f)) mat.SetFloat("_Intensity", intensity);
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        private static MeshRenderer Quad(string name, Transform parent, Material mat, Vector3 pos, Quaternion rot, Vector3 scale, GameArtKit kit)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetLocalPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = kit.Quad;
            var r = go.AddComponent<MeshRenderer>();
            PropKit.SetupRenderer(r, mat);
            r.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            return r;
        }

        /// <summary>Locked: a few slow red embers rising from the threshold.</summary>
        private static ParticleSystem LockedEmbers(Transform parent, Material mat)
        {
            var ps = PropKit.Particles("Locked Embers", parent, mat);
            ps.transform.localPosition = new Vector3(0f, 0.3f, -0.15f);
            var main = ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.4f, 3.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.15f, 0.4f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.13f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.3f, 0.08f, 0.9f), new Color(1f, 0.55f, 0.2f, 0.8f));
            main.maxParticles = 16;
            var emission = ps.emission;
            emission.rateOverTime = 5f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(1.9f, 0.2f, 0.1f); // across the arch; emits along local +Z = world up
            FadeInOut(ps);
            return ps;
        }

        /// <summary>Open: motes drawn from the arch edge into the centre (local simulation, moves with the tile).</summary>
        private static ParticleSystem InPullMotes(Transform parent, Material mat)
        {
            var ps = PropKit.Particles("Open Motes", parent, mat);
            ps.transform.SetLocalPositionAndRotation(new Vector3(0f, Height * 0.5f, -0.08f), Quaternion.identity);
            var main = ps.main;
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.playOnAwake = false;
            main.prewarm = false;
            main.startLifetime = 1.05f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.07f, 0.15f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.6f, 0.9f, 1f, 0.95f), new Color(1f, 1f, 1f, 0.9f));
            main.maxParticles = 40;
            var emission = ps.emission;
            emission.rateOverTime = 30f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Circle; // XY plane = the portal plane
            shape.radius = 1.3f;
            shape.radiusThickness = 0f;
            shape.scale = new Vector3(Width / Height * 1.25f, 1.25f, 1f);
            var vel = ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.Local;
            vel.x = new ParticleSystem.MinMaxCurve(0f);
            vel.y = new ParticleSystem.MinMaxCurve(0f);
            vel.z = new ParticleSystem.MinMaxCurve(0f);
            vel.radial = new ParticleSystem.MinMaxCurve(-1.25f);
            vel.orbitalZ = new ParticleSystem.MinMaxCurve(1.6f); // swirl with the surface
            FadeInOut(ps);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        /// <summary>Opening: one-shot spark burst when the film breaks (PortalGate calls Emit).</summary>
        private static ParticleSystem Burst(Transform parent, Material mat)
        {
            var ps = PropKit.Particles("Open Burst", parent, mat);
            ps.transform.SetLocalPositionAndRotation(new Vector3(0f, Height * 0.5f, -0.1f), Quaternion.identity);
            var main = ps.main;
            main.playOnAwake = false;
            main.prewarm = false;
            main.loop = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 6.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.92f, 0.7f, 1f), new Color(0.6f, 0.9f, 1f, 1f));
            main.gravityModifier = 0.4f;
            main.maxParticles = 64;
            var emission = ps.emission;
            emission.rateOverTime = 0f;
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.5f;
            var limit = ps.limitVelocityOverLifetime;
            limit.enabled = true;
            limit.limit = 0.5f;
            limit.dampen = 0.08f;
            FadeInOut(ps);
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return ps;
        }

        private static void FadeInOut(ParticleSystem ps)
        {
            var col = ps.colorOverLifetime;
            col.enabled = true;
            col.color = PropKit.Gradient(new[] { (Color.white, 0f), (Color.white, 1f) }, new[] { (0f, 0f), (1f, 0.15f), (0.8f, 0.6f), (0f, 1f) });
        }

        private static AudioSource HumSource(GameObject holder, AudioClip hum)
        {
            var src = holder.AddComponent<AudioSource>();
            src.clip = hum;
            src.loop = true;
            src.playOnAwake = false;
            src.volume = 0f;
            src.spatialBlend = 1f;
            src.rolloffMode = AudioRolloffMode.Linear;
            src.minDistance = 3f;
            src.maxDistance = 26f;
            src.dopplerLevel = 0f;
            return src;
        }
    }
}
