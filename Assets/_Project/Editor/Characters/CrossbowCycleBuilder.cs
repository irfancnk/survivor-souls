using System.IO;
using SurvivorSouls.Editor.Procedural;
using SurvivorSouls.Gameplay.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurvivorSouls.Editor.Characters
{
    /// <summary>
    /// KAN-217 shoot cycle on the Knight prefab: <see cref="CrossbowCycle"/> (clip lengths from the GLB), a loaded bolt
    /// resting on the crossbow rail (child of Crossbow_Muzzle, palette mesh, hidden from the shot until the reload seats a new
    /// one) and the quiet bolt-seat click (procedural WAVs, same 16-bit mono format as SfxBuilder). Called by
    /// <see cref="MeshyCharacterImporter"/> while it builds the prefab.
    /// </summary>
    public static class CrossbowCycleBuilder
    {
        private const string MeshPath = "Assets/_Project/Art/Characters/ArcherMeshy/LoadedBolt_Mesh.asset";
        private const string PaletteMaterialPath = "Assets/_Project/Art/Materials/Game/M_Palette.mat"; // GameArtKit, built first
        private const string SfxDir = "Assets/_Project/Audio/Sfx";
        private const int Rate = 44100;

        // Bolt on the rail: along the crossbow's local +X (stock to muzzle, CrossbowAimTests), tip this far behind the muzzle (m).
        private const float BoltLength = 0.36f;
        private const float TipBehindMuzzle = 0.03f;

        public static void AddTo(GameObject root, Animator animator, float fireLength, float reloadLength)
        {
            Transform muzzle = null;
            foreach (var t in root.GetComponentsInChildren<Transform>(true))
                if (t.name == "Crossbow_Muzzle") muzzle = t;

            GameObject bolt = null;
            if (muzzle != null)
            {
                var crossbow = muzzle.parent;
                bolt = new GameObject("Crossbow_LoadedBolt");
                bolt.transform.SetParent(crossbow, false);
                // Mesh +Y (tip) onto the crossbow line; the crossbow carries a scale, the bolt keeps its own size in metres.
                Vector3 s = crossbow.lossyScale;
                bolt.transform.localRotation = Quaternion.FromToRotation(Vector3.up, Vector3.right);
                bolt.transform.localPosition = muzzle.localPosition - Vector3.right * (TipBehindMuzzle / Mathf.Max(0.001f, s.x));
                bolt.transform.localScale = new Vector3(1f / Mathf.Max(0.001f, s.x), 1f / Mathf.Max(0.001f, s.y), 1f / Mathf.Max(0.001f, s.z));
                bolt.AddComponent<MeshFilter>().sharedMesh = BuildMesh();
                var r = bolt.AddComponent<MeshRenderer>();
                r.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(PaletteMaterialPath);
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.lightProbeUsage = LightProbeUsage.Off;
                r.reflectionProbeUsage = ReflectionProbeUsage.Off;
            }
            else Debug.LogWarning("[CrossbowCycleBuilder] no Crossbow_Muzzle: the shoot cycle runs without a loaded bolt");

            var cycle = root.AddComponent<CrossbowCycle>();
            cycle.Setup(animator, bolt);
            var so = new SerializedObject(cycle);
            so.FindProperty("fireLength").floatValue = fireLength;
            so.FindProperty("reloadLength").floatValue = reloadLength;
            var clips = so.FindProperty("seatClips");
            var seat = BuildSeatClips();
            clips.arraySize = seat.Length;
            for (int i = 0; i < seat.Length; i++) clips.GetArrayElementAtIndex(i).objectReferenceValue = seat[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Bolt along +Y (tip at 0, tail at -<see cref="BoltLength"/>): wood shaft, steel head, two vanes.</summary>
        private static Mesh BuildMesh()
        {
            var mb = new MeshBuilder();
            var wood = PaletteAtlas.For(Swatch.Wood);
            var steel = PaletteAtlas.For(Swatch.BrightMetal);
            var vane = PaletteAtlas.For(Swatch.Fletching);
            mb.Tube(new[] { new Vector3(0f, -BoltLength, 0f), new Vector3(0f, -0.04f, 0f) }, t => 0.008f, wood, 0, sides: 6);
            mb.Tube(new[] { new Vector3(0f, -0.045f, 0f), new Vector3(0f, -0.005f, 0f), new Vector3(0f, 0f, 0f) },
                t => t < 0.9f ? 0.011f : 0.003f, steel, 0, sides: 6);
            for (int i = 0; i < 2; i++)
            {
                float side = i == 0 ? 1f : -1f;
                mb.Box(new Vector3(side * 0.012f, -BoltLength + 0.045f, 0f), new Vector3(0.016f, 0.07f, 0.002f), Quaternion.identity, vane, 0);
            }
            var mesh = EditorAssetUtil.LoadOrCreateMesh(MeshPath);
            mb.WriteTo(mesh);
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        // ------------------------------------------------------------ Seat click

        private static AudioClip[] BuildSeatClips()
        {
            Directory.CreateDirectory(SfxDir);
            var clips = new AudioClip[2];
            for (int i = 0; i < clips.Length; i++) clips[i] = Save($"sfx_crossbow_seat_{i + 1:00}", SeatClick(i + 1));
            return clips;
        }

        /// <summary>String catches the nut (dry wooden latch click) and the bolt drops onto the rail (small tick + knock).</summary>
        private static float[] SeatClick(int seed)
        {
            var rng = new System.Random(seed * 577);
            var b = new float[(int)(Rate * 0.16f)];
            float v = 1f + (float)(rng.NextDouble() - 0.5) * 0.1f;
            Click(b, 0f, 0.012f, 0.9f, 2600f * v, 70f, rng);     // latch
            Click(b, 0.055f, 0.008f, 0.45f, 3800f * v, 110f, rng); // bolt tick on the rail
            Tone(b, 0.055f, 0.05f, 0.25f, 420f * v, 70f);         // dull wooden knock
            float peak = 0f;
            foreach (float s in b) peak = Mathf.Max(peak, Mathf.Abs(s));
            if (peak > 0f) for (int i = 0; i < b.Length; i++) b[i] *= 0.85f / peak;
            return b;
        }

        /// <summary>Short noise click through a one-pole band around <paramref name="freq"/>, exponential decay.</summary>
        private static void Click(float[] b, float start, float length, float gain, float freq, float decay, System.Random rng)
        {
            int s0 = (int)(start * Rate), n = (int)(length * Rate);
            float lp = 0f, prev = 0f, a = Mathf.Clamp01(2f * Mathf.PI * freq / Rate);
            for (int i = 0; i < n && s0 + i < b.Length; i++)
            {
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += a * (noise - lp);
                float hp = lp - prev; // crude band: low-pass, then a first difference
                prev = lp;
                b[s0 + i] += hp * gain * 3f * Mathf.Exp(-i / (float)Rate * decay * 10f);
            }
        }

        private static void Tone(float[] b, float start, float length, float gain, float freq, float decay)
        {
            int s0 = (int)(start * Rate), n = (int)(length * Rate);
            for (int i = 0; i < n && s0 + i < b.Length; i++)
            {
                float t = i / (float)Rate;
                b[s0 + i] += Mathf.Sin(2f * Mathf.PI * freq * t) * gain * Mathf.Exp(-t * decay);
            }
        }

        private static AudioClip Save(string name, float[] samples)
        {
            string path = $"{SfxDir}/{name}.wav";
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int bytes = samples.Length * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + bytes);
                w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' }); w.Write(16);
                w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(bytes);
                foreach (float s in samples) w.Write((short)Mathf.Clamp(Mathf.RoundToInt(s * 32767f), -32768, 32767));
            }
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var importer = (AudioImporter)AssetImporter.GetAtPath(path);
            importer.forceToMono = true;
            var settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.ADPCM;
            settings.quality = 0.6f;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
    }
}
