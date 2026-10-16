using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using SurvivorSouls.Audio;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Editor.Audio
{
    /// <summary>
    /// Dev sound board (KAN-236, KAN-81.3): menu <c>Survivor Souls/Audio/Sound Board</c>. Lists every generated clip in
    /// <see cref="SfxBuilder.Dir"/> by family (file name without the number) with a play button per clip and per family
    /// (random pick). Edit mode plays through the editor preview; Play mode plays through a temporary source in the SFX
    /// group, so the limiter and the mixer hear it. In Play mode it shows the active voices per mixer group, the shot
    /// channel's voices and counters, and the master limiter's gain reduction.
    /// </summary>
    public sealed class SoundBoardWindow : EditorWindow
    {
        private readonly SortedDictionary<string, List<AudioClip>> _families = new SortedDictionary<string, List<AudioClip>>();
        private readonly Dictionary<string, int> _voices = new Dictionary<string, int>();
        private Vector2 _scroll;
        private string _filter = "";
        private float _limiterDb;
        private AudioSource _player;

        [MenuItem("Survivor Souls/Audio/Sound Board")]
        public static void Open() => GetWindow<SoundBoardWindow>("Sound Board").Reload();

        private void OnEnable() => Reload();

        private void OnDisable()
        {
            if (_player != null) DestroyImmediate(_player.gameObject);
        }

        /// <summary>Family name of a clip file: <c>sfx_zombie_death_03</c> -> <c>sfx_zombie_death</c>.</summary>
        public static string FamilyOf(string clipName)
        {
            int u = clipName.LastIndexOf('_');
            return u > 0 && clipName.Substring(u + 1).All(char.IsDigit) ? clipName.Substring(0, u) : clipName;
        }

        private void Reload()
        {
            _families.Clear();
            foreach (var guid in AssetDatabase.FindAssets("t:AudioClip", new[] { SfxBuilder.Dir }))
            {
                var clip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));
                if (clip == null) continue;
                string family = FamilyOf(Path.GetFileNameWithoutExtension(clip.name));
                if (!_families.TryGetValue(family, out var list)) _families[family] = list = new List<AudioClip>();
                list.Add(clip);
            }
            foreach (var list in _families.Values) list.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
        }

        private void Update()
        {
            if (EditorApplication.isPlaying) Repaint();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _filter = EditorGUILayout.TextField("Filter", _filter);
                if (GUILayout.Button("Reload", GUILayout.Width(70))) Reload();
                if (GUILayout.Button("Stop", GUILayout.Width(50))) StopAll();
            }
            if (EditorApplication.isPlaying) DrawVoices();
            else EditorGUILayout.HelpBox("Enter Play mode (Game scene) to see active voices per mixer group and the limiter.", MessageType.None);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var kv in _families)
            {
                if (_filter.Length > 0 && kv.Key.IndexOf(_filter, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField($"{kv.Key} ({kv.Value.Count})", GUILayout.Width(260));
                    if (GUILayout.Button("Random", GUILayout.Width(64))) PlayClip(kv.Value[Random.Range(0, kv.Value.Count)]);
                    for (int i = 0; i < kv.Value.Count; i++)
                        if (GUILayout.Button((i + 1).ToString(), GUILayout.Width(26))) PlayClip(kv.Value[i]);
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawVoices()
        {
            _voices.Clear();
            int total = 0;
            foreach (var src in FindObjectsByType<AudioSource>(FindObjectsSortMode.None))
            {
                if (!src.isPlaying) continue;
                string group = src.outputAudioMixerGroup != null ? src.outputAudioMixerGroup.name : "(direct)";
                _voices.TryGetValue(group, out int n);
                _voices[group] = n + 1;
                total++;
            }
            EditorGUILayout.LabelField($"Active voices: {total}  ({string.Join(", ", _voices.Select(v => $"{v.Key} {v.Value}"))})");
            var shots = FindAnyObjectByType<ShotSounds>();
            if (shots != null)
                EditorGUILayout.LabelField($"Shot channel: {shots.ActiveVoices(Time.unscaledTime)}/{shots.VoiceCap} voices, played {shots.Played} " +
                                           $"(follow-ups {shots.FollowUps}, layers {shots.Layers}), dropped {shots.Dropped}, stolen {shots.Stolen}, " +
                                           $"rise x{shots.AttackSpeedPitch():0.000}");
            var limiter = FindAnyObjectByType<MasterLimiter>();
            if (limiter != null)
            {
                // Hold the deepest reduction for a moment so a short peak is readable.
                float db = limiter.TakeGainReductionDb();
                _limiterDb = db < _limiterDb ? db : Mathf.MoveTowards(_limiterDb, db, 0.25f);
                EditorGUILayout.LabelField($"Master limiter: {_limiterDb:0.0} dB gain reduction (ceiling {limiter.Ceiling:0.00})");
            }
        }

        private void PlayClip(AudioClip clip)
        {
            if (clip == null) return;
            if (!EditorApplication.isPlaying)
            {
                StopAll();
                var util = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AudioUtil");
                var play = util?.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                    .FirstOrDefault(m => m.Name == "PlayPreviewClip" && m.GetParameters().Length == 3);
                play?.Invoke(null, new object[] { clip, 0, false });
                return;
            }
            if (_player == null)
            {
                var go = new GameObject("Sound Board Voice") { hideFlags = HideFlags.HideAndDontSave };
                _player = go.AddComponent<AudioSource>();
                _player.playOnAwake = false;
                _player.spatialBlend = 0f;
                var routing = FindAnyObjectByType<AudioMixerRouting>();
                if (routing != null) _player.outputAudioMixerGroup = routing.GroupOf(AudioMixerRouting.Channel.Sfx);
            }
            _player.PlayOneShot(clip, 1f);
        }

        private void StopAll()
        {
            if (_player != null) _player.Stop();
            var util = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.AudioUtil");
            util?.GetMethod("StopAllPreviewClips", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)?.Invoke(null, null);
        }
    }
}
