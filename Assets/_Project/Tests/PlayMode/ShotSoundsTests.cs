#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Audio;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Skills;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-236 (KAN-81.3) shot channel: no clip repeat of the last two (last one for a 3-clip hero set), pitch +-4 % and
    /// volume +-8 %, the attack speed pitch rise and its cap, the 3-voice cap (oldest cut) and the 40 ms gap, one voice per
    /// Spread Shot volley (with the volley layer), the quiet higher Double Shot follow-up, the shooter event path, the
    /// GameAudio hero hand-over, mixer routing by voice name, no GC per shot, Game scene wiring (run the Game scene builder
    /// first). Shot times are passed by hand from Time.unscaledTime + 100 (the [Test] clock stands still).
    /// </summary>
    public sealed class ShotSoundsTests
    {
        private const int TestRealm = 96; // never touches the saved progress of a real Realm

        private readonly List<Object> _objects = new List<Object>();
        private PlayerStats _stats;
        private AutoShooter _shooter;
        private ShotSounds _sounds;

        [SetUp]
        public void SetUp()
        {
            Random.InitState(236);
            var player = Track(new GameObject("Player"));
            player.SetActive(false); // Awake after every component exists (readers grab PlayerStats in Awake)
            _stats = player.AddComponent<PlayerStats>();
            player.AddComponent<CharacterController>();
            player.AddComponent<PlayerMover>();
            _shooter = player.AddComponent<AutoShooter>();
            player.AddComponent<PlayerHealth>();
            player.SetActive(true);
            _shooter.enabled = false; // tests fire by hand
            var template = Track(new GameObject("Bolt Template"));
            template.SetActive(false);
            _shooter.SetBoltPrefab(template.AddComponent<Bolt>());
            _sounds = Make(Clips("shot", 8, 0.5f), Clips("layer", 2, 0.4f), _shooter);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var b in Resources.FindObjectsOfTypeAll<Bolt>())
                if (b != null && b.gameObject.scene.IsValid()) Object.DestroyImmediate(b.gameObject);
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            Enemy.Active.Clear();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private AudioClip[] Clips(string name, int count, float seconds)
        {
            var clips = new AudioClip[count];
            for (int i = 0; i < count; i++)
                clips[i] = Track(AudioClip.Create($"{name} {i}", Mathf.Max(1, (int)(44100 * seconds)), 1, 44100, false));
            return clips;
        }

        /// <summary>The component on an inactive object, wired, then enabled (Awake makes the voices).</summary>
        private ShotSounds Make(AudioClip[] shots, AudioClip[] layers, AutoShooter shooter)
        {
            var go = Track(new GameObject("Audio"));
            go.SetActive(false);
            var s = go.AddComponent<ShotSounds>();
            s.Setup(shots, layers, shooter);
            go.SetActive(true);
            return s;
        }

        private static float T0 => Time.unscaledTime + 100f;

        // ------------------------------------------------------------ Variants + jitter

        [Test]
        public void Variants_NeverOneOfTheLastTwo()
        {
            float t = T0;
            int prev1 = -1, prev2 = -1;
            var seen = new HashSet<int>();
            for (int i = 0; i < 300; i++)
            {
                Assert.That(_sounds.Shot(t + i * 0.6f, false, 0), Is.True);
                int c = _sounds.LastClip;
                Assert.That(c, Is.Not.EqualTo(prev1).And.Not.EqualTo(prev2), $"shot {i}: clip {c} after {prev2}, {prev1}");
                prev2 = prev1;
                prev1 = c;
                seen.Add(c);
            }
            Assert.That(seen.Count, Is.EqualTo(8), "every variant gets played");
        }

        [Test]
        public void HeroSet_OfThree_NeverTheLastOne_AndBackToTheVariants()
        {
            var bow = Clips("bow", 3, 0.6f);
            _sounds.SetHeroClips(bow);
            Assert.That(_sounds.Clips, Is.SameAs(bow));
            float t = T0;
            int prev = -1;
            for (int i = 0; i < 60; i++)
            {
                _sounds.Shot(t + i * 0.7f, false, 0);
                Assert.That(_sounds.LastClip, Is.Not.EqualTo(prev));
                Assert.That(_sounds.LastSource.clip, Is.SameAs(bow[_sounds.LastClip]), "plays the hero's clip");
                prev = _sounds.LastClip;
            }
            _sounds.SetHeroClips(null);
            Assert.That(_sounds.Clips.Length, Is.EqualTo(8), "no hero clips = the crossbow variants");
        }

        [Test]
        public void Pitch_PlusMinus4Percent_Volume_PlusMinus8Percent()
        {
            float t = T0;
            float lo = 9f, hi = 0f, vlo = 9f, vhi = 0f;
            for (int i = 0; i < 400; i++)
            {
                _sounds.Shot(t + i * 0.6f, false, 0);
                lo = Mathf.Min(lo, _sounds.LastSource.pitch);
                hi = Mathf.Max(hi, _sounds.LastSource.pitch);
                float v = _sounds.LastSource.volume / Mathf.Max(1e-4f, GameSettings.SfxVolume);
                vlo = Mathf.Min(vlo, v);
                vhi = Mathf.Max(vhi, v);
            }
            Assert.That(lo, Is.GreaterThanOrEqualTo(0.96f - 1e-4f));
            Assert.That(hi, Is.LessThanOrEqualTo(1.04f + 1e-4f));
            Assert.That(hi - lo, Is.GreaterThan(0.06f), "the jitter is used");
            Assert.That(vlo, Is.GreaterThanOrEqualTo(0.8f * 0.92f - 1e-4f));
            Assert.That(vhi, Is.LessThanOrEqualTo(0.8f * 1.08f + 1e-4f));
            Assert.That(vhi - vlo, Is.GreaterThan(0.1f), "volume jitter is used");
        }

        [Test]
        public void AttackSpeed_RaisesThePitch_Capped()
        {
            Assert.That(_sounds.AttackSpeedPitch(), Is.EqualTo(1f).Within(1e-4f), "base rate: no rise");
            _stats.SetSource(_shooter, new[] { new StatModifier(StatId.AttackSpeed, ModifierKind.Percent, 1f) }, 1);
            float doubled = _sounds.AttackSpeedPitch();
            Assert.That(doubled, Is.EqualTo(1.03f).Within(0.002f), "+100 % attack speed = +3 % pitch");
            _stats.SetSource(_shooter, new[] { new StatModifier(StatId.AttackSpeed, ModifierKind.Percent, 6f) }, 1);
            Assert.That(_sounds.AttackSpeedPitch(), Is.EqualTo(1f + _sounds.MaxRise).Within(1e-4f), "capped");

            float t = T0, hi = 0f;
            for (int i = 0; i < 200; i++)
            {
                _sounds.Shot(t + i * 0.6f, false, 0);
                hi = Mathf.Max(hi, _sounds.LastSource.pitch);
            }
            Assert.That(hi, Is.LessThanOrEqualTo((1f + _sounds.MaxRise) * 1.04f + 1e-4f), "rise x jitter stays capped");
            Assert.That(hi, Is.GreaterThan(1.04f), "the rise is heard");
        }

        // ------------------------------------------------------------ Limits

        [Test]
        public void Gap_40ms_DropsTheSecondShot()
        {
            float t = T0;
            Assert.That(_sounds.MinGap, Is.EqualTo(0.04f).Within(1e-4f));
            Assert.That(_sounds.Shot(t, false, 0), Is.True);
            Assert.That(_sounds.Shot(t + 0.02f, false, 0), Is.False, "inside the gap");
            Assert.That(_sounds.Shot(t + 0.02f, true, 0), Is.False, "a follow-up inside the gap too");
            Assert.That(_sounds.Shot(t + 0.045f, false, 0), Is.True);
            Assert.That(_sounds.Dropped, Is.EqualTo(2));
            Assert.That(_sounds.Played, Is.EqualTo(2));
        }

        [Test]
        public void VoiceCap_Three_OldestIsCut()
        {
            float t = T0;
            Assert.That(_sounds.VoiceCap, Is.EqualTo(3));
            var used = new List<AudioSource>();
            for (int i = 0; i < 3; i++)
            {
                Assert.That(_sounds.Shot(t + i * 0.05f, false, 0), Is.True);
                used.Add(_sounds.LastSource);
            }
            Assert.That(new HashSet<AudioSource>(used).Count, Is.EqualTo(3), "3 voices");
            Assert.That(_sounds.ActiveVoices(t + 0.11f), Is.EqualTo(3));
            Assert.That(_sounds.Shot(t + 0.15f, false, 0), Is.True, "the newest shot always sounds");
            Assert.That(_sounds.Stolen, Is.EqualTo(1));
            Assert.That(_sounds.LastSource, Is.SameAs(used[0]), "the oldest voice is cut");
            Assert.That(_sounds.ActiveVoices(t + 0.16f), Is.EqualTo(3), "never more than 3");

            // 10 shots per second for 3 s: at most 3 voices at any moment.
            var s = _sounds;
            float u = t + 10f;
            for (int i = 0; i < 30; i++)
            {
                s.Shot(u + i * 0.1f, false, 0);
                Assert.That(s.ActiveVoices(u + i * 0.1f), Is.LessThanOrEqualTo(3));
            }
        }

        // ------------------------------------------------------------ Volley + follow-up

        [Test]
        public void SpreadVolley_OneVoice_WithTheVolleyLayer()
        {
            float t = T0;
            Assert.That(_sounds.Shot(t, false, 4), Is.True);
            Assert.That(_sounds.Layers, Is.EqualTo(1), "volley layer on the shot");
            Assert.That(_sounds.ActiveVoices(t + 0.01f), Is.EqualTo(1), "one voice for the whole volley");
            Assert.That(_sounds.Shot(t + 1f, false, 0), Is.True);
            Assert.That(_sounds.Layers, Is.EqualTo(1), "a single bolt has no layer");
            Assert.That(_sounds.Shot(t + 2f, true, 4), Is.True);
            Assert.That(_sounds.Layers, Is.EqualTo(1), "a follow-up has no layer");
        }

        [Test]
        public void FollowUp_QuietAndHigher()
        {
            float t = T0;
            float pitch = 0f, vol = 0f;
            for (int i = 0; i < 100; i++)
            {
                _sounds.Shot(t + i * 0.6f, true, 0);
                pitch += _sounds.LastSource.pitch;
                vol += _sounds.LastSource.volume / Mathf.Max(1e-4f, GameSettings.SfxVolume);
            }
            Assert.That(pitch / 100f, Is.EqualTo(1.1f).Within(0.01f));
            Assert.That(vol / 100f, Is.EqualTo(0.8f * 0.35f).Within(0.01f));
            Assert.That(_sounds.FollowUps, Is.EqualTo(100));
        }

        [Test]
        public void ShooterVolley_ReachesTheChannel_Once_WithSpreadLayer()
        {
            var e = Track(new GameObject("Enemy")).AddComponent<Enemy>();
            e.Spawn(new Vector3(0f, 0f, 4f), null, _ => { }, healthMultiplier: 50f);
            _stats.SetSource(_shooter, new[] { new StatModifier(StatId.Multishot, ModifierKind.Flat, 2f) }, 1);
            Assert.That(_shooter.CurrentVolleySpec.Spread, Is.EqualTo(2));
            _shooter.Fire(e); // real clock
            Assert.That(_sounds.Played, Is.EqualTo(1), "3 bolts, one shot sound");
            Assert.That(_sounds.Layers, Is.EqualTo(1), "Spread Shot volley layer");
        }

        [Test]
        public void GameAudio_HandsHeroClipsToTheChannel()
        {
            var go = Track(new GameObject("Game Audio"));
            go.SetActive(false);
            var audio = go.AddComponent<GameAudio>();
            var so = new SerializedObject(audio);
            so.FindProperty("shotChannel").objectReferenceValue = _sounds;
            so.ApplyModifiedPropertiesWithoutUndo();
            var bow = Clips("bow", 3, 0.6f);
            audio.SetPlayerShots(bow);
            Assert.That(_sounds.Clips, Is.SameAs(bow));
            audio.SetPlayerShots(null);
            Assert.That(_sounds.Clips.Length, Is.EqualTo(8));
        }

        [Test]
        public void Shots_NoGc()
        {
            float t = T0;
            for (int i = 0; i < 6; i++) _sounds.Shot(t + i * 0.05f, i % 2 == 1, i % 3 == 0 ? 2 : 0); // warm-up (all paths)
            _sounds.AttackSpeedPitch();
            Assert.That(() =>
            {
                for (int i = 0; i < 40; i++) _sounds.Shot(t + 2f + i * 0.03f, i % 2 == 1, i % 3 == 0 ? 4 : 0);
                _sounds.ActiveVoices(t + 4f);
            }, Is.Not.AllocatingGCMemory());
        }

        // ------------------------------------------------------------ Routing

        [Test]
        public void Routing_ByVoiceName()
        {
            Assert.That(AudioMixerRouting.ChannelOf("Shot Voice 2"), Is.EqualTo(AudioMixerRouting.Channel.Shots));
            Assert.That(AudioMixerRouting.ChannelOf("Footstep Left"), Is.EqualTo(AudioMixerRouting.Channel.Footsteps));
            Assert.That(AudioMixerRouting.ChannelOf("Zombie Death Voice 0"), Is.EqualTo(AudioMixerRouting.Channel.Deaths));
            Assert.That(AudioMixerRouting.ChannelOf("Zombie Fall Voice 1"), Is.EqualTo(AudioMixerRouting.Channel.Deaths));
            Assert.That(AudioMixerRouting.ChannelOf("Ambience"), Is.EqualTo(AudioMixerRouting.Channel.Music));
            Assert.That(AudioMixerRouting.ChannelOf("Voice 3"), Is.EqualTo(AudioMixerRouting.Channel.Sfx));
        }

        // ------------------------------------------------------------ Scene

        [UnityTest]
        public IEnumerator GameScene_ShotChannelLimiterAndMixerWired()
        {
            TearDown(); // this test uses the real scene's objects

            RunState.NewRun(TestRealm, 2361);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;

            var shots = Object.FindAnyObjectByType<ShotSounds>();
            Assert.That(shots, Is.Not.Null, "ShotSounds on the audio object");
            Assert.That(shots.VoiceCap, Is.EqualTo(3));
            var own = new SerializedObject(shots);
            Assert.That(own.FindProperty("variants").arraySize, Is.EqualTo(8));
            Assert.That(own.FindProperty("volleyLayers").arraySize, Is.GreaterThanOrEqualTo(1));
            Assert.That(own.FindProperty("shooter").objectReferenceValue, Is.Not.Null, "wired to the Knight's shooter");
            var audio = Object.FindAnyObjectByType<GameAudio>();
            Assert.That(new SerializedObject(audio).FindProperty("shotChannel").objectReferenceValue, Is.EqualTo(shots), "GameAudio hands shots over");

            var limiter = Object.FindAnyObjectByType<MasterLimiter>();
            Assert.That(limiter, Is.Not.Null, "limiter on the listener");
            Assert.That(limiter.GetComponent<AudioListener>(), Is.Not.Null);

            var routing = Object.FindAnyObjectByType<AudioMixerRouting>();
            Assert.That(routing, Is.Not.Null, "mixer routing on the audio object");
            Assert.That(routing.Routed, Is.GreaterThan(0), "Start routed the voices");
            AudioMixerGroup Group(string voice)
            {
                var t = audio.transform.Find(voice);
                Assert.That(t, Is.Not.Null, voice);
                return t.GetComponent<AudioSource>().outputAudioMixerGroup;
            }
            Assert.That(Group("Shot Voice 0").name, Is.EqualTo("Shots"));
            Assert.That(Group("Footstep Left").name, Is.EqualTo("Footsteps"));
            Assert.That(Group("Zombie Death Voice 0").name, Is.EqualTo("Deaths"));
            Assert.That(Group("Ambience").name, Is.EqualTo("Music"));
            Assert.That(Group("Voice 0").name, Is.EqualTo("SFX"));
        }
    }
}
#endif
