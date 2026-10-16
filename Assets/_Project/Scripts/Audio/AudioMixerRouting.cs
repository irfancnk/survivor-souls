using UnityEngine;
using UnityEngine.Audio;

namespace SurvivorSouls.Audio
{
    /// <summary>
    /// Mixer routing (KAN-236, KAN-81.3): at Start (after every Awake made its voices) it points each AudioSource in the scene
    /// that has no mixer group yet at a group of <c>SurvivorSouls.mixer</c>: shot voices to SFX/Shots, footsteps to
    /// SFX/Footsteps, zombie death and fall voices to SFX/Deaths, the ambience loop to Music, everything else to SFX.
    /// Sources made later (lazy pools) keep the direct path; the <see cref="MasterLimiter"/> on the listener still guards them.
    /// One pass at Start: no per-frame cost.
    /// </summary>
    public sealed class AudioMixerRouting : MonoBehaviour
    {
        public enum Channel { Sfx, Shots, Footsteps, Deaths, Music }

        [SerializeField] private AudioMixerGroup sfx;
        [SerializeField] private AudioMixerGroup shots;
        [SerializeField] private AudioMixerGroup footsteps;
        [SerializeField] private AudioMixerGroup deaths;
        [SerializeField] private AudioMixerGroup music;

        /// <summary>Sources routed by the last <see cref="RouteAll"/>.</summary>
        public int Routed { get; private set; }

        /// <summary>Channel of a source from its object name (voice names of ShotSounds, FootstepSounds, ZombieDeathSounds, GameAudio).</summary>
        public static Channel ChannelOf(string sourceName)
        {
            if (string.IsNullOrEmpty(sourceName)) return Channel.Sfx;
            if (sourceName.StartsWith("Shot Voice")) return Channel.Shots;
            if (sourceName.StartsWith("Footstep")) return Channel.Footsteps;
            if (sourceName.StartsWith("Zombie Death Voice") || sourceName.StartsWith("Zombie Fall Voice")) return Channel.Deaths;
            if (sourceName == "Ambience") return Channel.Music;
            return Channel.Sfx;
        }

        public AudioMixerGroup GroupOf(Channel channel)
        {
            switch (channel)
            {
                case Channel.Shots: return shots != null ? shots : sfx;
                case Channel.Footsteps: return footsteps != null ? footsteps : sfx;
                case Channel.Deaths: return deaths != null ? deaths : sfx;
                case Channel.Music: return music;
                default: return sfx;
            }
        }

        private void Start() => RouteAll();

        /// <summary>Routes every source in the loaded scenes that has no group yet. Public for tests.</summary>
        public void RouteAll()
        {
            Routed = 0;
            var sources = FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (var src in sources)
            {
                if (src == null || src.outputAudioMixerGroup != null) continue;
                var group = GroupOf(ChannelOf(src.gameObject.name));
                if (group == null) continue;
                src.outputAudioMixerGroup = group;
                Routed++;
            }
        }
    }
}
