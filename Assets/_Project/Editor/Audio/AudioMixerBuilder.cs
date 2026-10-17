using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

namespace SurvivorSouls.Editor.Audio
{
    /// <summary>
    /// The game's AudioMixer (KAN-236, KAN-81.3): <c>Master</c> > <c>SFX</c> > (<c>Shots</c>, <c>Footsteps</c>, <c>Deaths</c>) and
    /// <c>Master</c> > <c>Music</c>, all at 0 dB. Unity has no public API to make a mixer, so this uses the editor's own
    /// (internal) <c>AudioMixerController</c> by reflection. The asset is made ONCE and then only loaded (a mixer made again
    /// would get new sub-asset ids = a diff on every scene rebuild); a missing group is added to the existing asset.
    /// The limiter is not a mixer effect: <see cref="SurvivorSouls.Audio.MasterLimiter"/> on the listener sees every source,
    /// also the ones that are not routed to the mixer.
    /// </summary>
    public static class AudioMixerBuilder
    {
        public const string MixerPath = "Assets/_Project/Audio/SurvivorSouls.mixer";

        public struct Groups
        {
            public AudioMixer Mixer;
            public AudioMixerGroup Master, Sfx, Shots, Footsteps, Deaths, Music;
        }

        private static readonly (string name, string parent)[] Layout =
        {
            ("SFX", "Master"), ("Shots", "SFX"), ("Footsteps", "SFX"), ("Deaths", "SFX"), ("Music", "Master"),
        };

        [MenuItem("Survivor Souls/Audio/Ensure Mixer")]
        public static void EnsureMenu() => Ensure();

        /// <summary>Loads the mixer (makes it and any missing group first). Groups are null when the editor API is missing.</summary>
        public static Groups Ensure()
        {
            var mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            if (mixer == null || Layout.Any(g => Find(mixer, g.name) == null))
            {
                try
                {
                    Build(mixer);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[AudioMixerBuilder] could not build {MixerPath}: {e.GetBaseException().Message}");
                }
                mixer = AssetDatabase.LoadAssetAtPath<AudioMixer>(MixerPath);
            }
            if (mixer == null) return default;
            return new Groups
            {
                Mixer = mixer,
                Master = Find(mixer, "Master"),
                Sfx = Find(mixer, "SFX"),
                Shots = Find(mixer, "Shots"),
                Footsteps = Find(mixer, "Footsteps"),
                Deaths = Find(mixer, "Deaths"),
                Music = Find(mixer, "Music"),
            };
        }

        public static AudioMixerGroup Find(AudioMixer mixer, string name) =>
            mixer == null ? null : mixer.FindMatchingGroups(string.Empty).FirstOrDefault(g => g.name == name);

        private static void Build(AudioMixer existing)
        {
            var asm = typeof(UnityEditor.Editor).Assembly;
            var controllerType = asm.GetType("UnityEditor.Audio.AudioMixerController", true);
            const BindingFlags Any = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

            object controller = existing;
            if (controller == null)
            {
                System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(MixerPath));
                var create = controllerType.GetMethod("CreateMixerControllerAtPath", Any)
                             ?? throw new MissingMethodException("AudioMixerController.CreateMixerControllerAtPath");
                controller = create.Invoke(null, new object[] { MixerPath });
            }
            var master = controllerType.GetProperty("masterGroup", Any)?.GetValue(controller)
                         ?? throw new MissingMemberException("AudioMixerController.masterGroup");
            var newGroup = controllerType.GetMethods(Any).First(m => m.Name == "CreateNewGroup" && m.GetParameters().Length == 2);
            var addChild = controllerType.GetMethods(Any).First(m => m.Name == "AddChildToParent" && m.GetParameters().Length == 2);

            var made = new System.Collections.Generic.Dictionary<string, object> { ["Master"] = master };
            var mixer = (AudioMixer)controller;
            foreach (var (name, parent) in Layout)
            {
                var have = Find(mixer, name);
                if (have != null) { made[name] = have; continue; }
                var group = newGroup.Invoke(controller, new object[] { name, false });
                addChild.Invoke(controller, new[] { group, made[parent] });
                made[name] = group;
            }
            // No group views: a mixer made by code has none (AddGroupToCurrentView would index an empty list); the Audio Mixer
            // window shows every group without one.
            controllerType.GetMethod("OnSubAssetChanged", Any, null, Type.EmptyTypes, null)?.Invoke(controller, null);
            EditorUtility.SetDirty((UnityEngine.Object)controller);
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(MixerPath, ImportAssetOptions.ForceUpdate);
            Debug.Log($"[AudioMixerBuilder] {MixerPath}: groups {string.Join(", ", Layout.Select(g => g.parent + "/" + g.name))}");
        }
    }
}
