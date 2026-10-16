#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Audio;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Kind = SurvivorSouls.Audio.ZombieDeathSounds.Kind;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-234 (KAN-81.1) zombie death sounds: type from the real roster prefabs, pitch by type and scale, no clip repeat
    /// of the last two, the 4-voice cap and the 60 ms gap, a louder close death takes the quietest voice, a burst of kills
    /// gives one mass death sound, delayed body falls, distance fade, pan and off-screen level, the kill event path, no GC,
    /// Game scene wiring (run the Game scene builder first). Kill times are passed by hand from Time.unscaledTime + 100
    /// (KAN-201 order rule: the [Test] clock stands still).
    /// </summary>
    public sealed class ZombieDeathSoundsTests
    {
        private const string Dir = "Assets/_Project/Prefabs/Enemies/";
        private const int TestRealm = 96; // never touches the saved progress of a real Realm

        private readonly List<Object> _objects = new List<Object>();
        private ZombieDeathSounds _sounds;
        private AudioClip[] _voices;
        private Transform _knight;
        private Camera _camera;

        [SetUp]
        public void SetUp()
        {
            Random.InitState(234);
            _voices = Clips("voice", 8, 1f);
        }

        [TearDown]
        public void TearDown()
        {
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
                clips[i] = Track(AudioClip.Create($"zombie {name} {i}", Mathf.Max(1, (int)(44100 * seconds)), 1, 44100, false));
            return clips;
        }

        /// <summary>The component on an inactive object, wired, then enabled (Awake makes the pools).</summary>
        private ZombieDeathSounds Make(bool knight = false, bool camera = false, float voiceSeconds = 1f)
        {
            if (knight) _knight = Track(new GameObject("Knight")).transform;
            if (camera)
            {
                _camera = Track(new GameObject("Test Camera")).AddComponent<Camera>();
                _camera.transform.position = new Vector3(0f, 12f, -10f);
                _camera.transform.LookAt(Vector3.zero);
            }
            var go = Track(new GameObject("Audio"));
            go.SetActive(false);
            _sounds = go.AddComponent<ZombieDeathSounds>();
            if (voiceSeconds != 1f) _voices = Clips("voice short", 8, voiceSeconds);
            _sounds.Setup(_voices, Clips("fall", 3, 0.4f), Clips("mass", 2, 1.3f), _knight, _camera);
            go.SetActive(true);
            return _sounds;
        }

        private static float T0 => Time.unscaledTime + 100f;

        // ------------------------------------------------------------ Type + pitch

        [Test]
        public void Kind_FromTheRosterPrefabs()
        {
            var s = Make();
            Kind Of(string id)
            {
                var asset = AssetDatabase.LoadAssetAtPath<Enemy>(Dir + "Enemy_" + id + ".prefab");
                Assert.That(asset, Is.Not.Null, id);
                return s.KindOf(asset);
            }
            Assert.That(Of("GraveyardLaborer"), Is.EqualTo(Kind.Normal));
            Assert.That(Of("FrenziedGhoul"), Is.EqualTo(Kind.Runner), "4.2 m/s sprinter");
            Assert.That(Of("GraveBrute"), Is.EqualTo(Kind.Brute), "520 HP at scale 1");
            Assert.That(s.Classify(1.25f, 1.3f, 340f, false), Is.EqualTo(Kind.Brute), "old Brute: big body");
            Assert.That(s.Classify(0.93f, 2.6f, 70f, false), Is.EqualTo(Kind.Runner), "old Runner");
            Assert.That(s.Classify(1f, 4f, 60f, true), Is.EqualTo(Kind.Brute), "a mini boss falls heavy");
        }

        [Test]
        public void Pitch_RunnerHigher_BruteDeeper_BiggerBodyLower()
        {
            var s = Make();
            float normal = s.PitchOf(Kind.Normal, 1f);
            Assert.That(normal, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(s.PitchOf(Kind.Runner, 0.93f), Is.GreaterThan(normal * 1.08f));
            Assert.That(s.PitchOf(Kind.Brute, 1f), Is.LessThan(normal * 0.92f));
            Assert.That(s.PitchOf(Kind.Brute, 1.25f), Is.LessThan(s.PitchOf(Kind.Brute, 1f)), "bigger body, lower");
            Assert.That(s.PitchOf(Kind.Normal, 1.1f), Is.LessThan(s.PitchOf(Kind.Normal, 0.95f)));
            Assert.That(s.PitchOf(Kind.Brute, 5f), Is.GreaterThanOrEqualTo(0.88f * 0.7f - 1e-4f), "clamped");

            float t = T0;
            Assert.That(s.Kill(Vector3.zero, 1.25f, Kind.Brute, t), Is.True);
            Assert.That(s.LastSource.pitch, Is.InRange(s.PitchOf(Kind.Brute, 1.25f) * 0.919f, s.PitchOf(Kind.Brute, 1.25f) * 1.081f), "+-8 % jitter");
        }

        // ------------------------------------------------------------ Variety + limits

        [Test]
        public void Clips_NeverOneOfTheLastTwo_AllEightUsed()
        {
            var s = Make(voiceSeconds: 0.2f);
            float t = T0;
            var used = new HashSet<int>();
            int last1 = -1, last2 = -1;
            for (int i = 0; i < 40; i++)
            {
                Assert.That(s.Kill(Vector3.zero, 1f, Kind.Normal, t + i * 0.5f), Is.True, "kill " + i);
                int c = s.LastClip;
                Assert.That(c, Is.Not.EqualTo(last1).And.Not.EqualTo(last2), "kill " + i);
                used.Add(c);
                last2 = last1;
                last1 = c;
            }
            Assert.That(used.Count, Is.EqualTo(8));
            Assert.That(s.Played, Is.EqualTo(40));
        }

        [Test]
        public void Horde_FourVoicesAtOnce_AndA60msGap()
        {
            var s = Make();
            Assert.That(s.VoiceCap, Is.EqualTo(4));
            float t = T0;
            for (int i = 0; i < 10; i++) s.Kill(Vector3.zero, 1f, Kind.Normal, t + i * 0.07f); // 1 s clips: all still ringing
            Assert.That(s.Played, Is.EqualTo(4), "cap");
            Assert.That(s.Dropped, Is.EqualTo(6));
            Assert.That(s.Masses, Is.Zero, "0.07 s apart is not a burst");

            float later = t + 5f;
            Assert.That(s.Kill(Vector3.zero, 1f, Kind.Normal, later), Is.True);
            Assert.That(s.Kill(Vector3.zero, 1f, Kind.Normal, later + 0.03f), Is.False, "30 ms after the last: gap");
            Assert.That(s.Kill(Vector3.zero, 1f, Kind.Normal, later + 0.07f), Is.True);
        }

        [Test]
        public void Horde_CloseDeathTakesTheQuietestVoice()
        {
            var s = Make(knight: true);
            _knight.position = Vector3.zero;
            float t = T0;
            var far = new Vector3(15f, 0f, 0f);
            for (int i = 0; i < 4; i++) Assert.That(s.Kill(far, 1f, Kind.Normal, t + i * 0.07f), Is.True);
            Assert.That(s.Kill(far, 1f, Kind.Normal, t + 0.35f), Is.False, "as loud as the others: no steal");
            Assert.That(s.Kill(new Vector3(1f, 0f, 0f), 1f, Kind.Normal, t + 0.42f), Is.True, "close: takes a voice");
            Assert.That(s.Stolen, Is.EqualTo(1));
            Assert.That(s.Played, Is.EqualTo(5));
        }

        [Test]
        public void Burst_OneMassDeath_NotAWall()
        {
            var s = Make();
            float t = T0;
            for (int i = 0; i < 8; i++) s.Kill(Vector3.zero, 1f, Kind.Normal, t); // a Soul Burst chain in one frame
            Assert.That(s.Masses, Is.EqualTo(1));
            Assert.That(s.Played, Is.EqualTo(1), "first kill groans, the 5th turns it into the mass sound");
            Assert.That(s.Merged, Is.EqualTo(3), "kills after it fold into it");
            Assert.That(s.PendingFalls, Is.Zero, "the mass clip has its own falls");

            for (int i = 0; i < 6; i++) s.Kill(Vector3.zero, 1f, Kind.Normal, t + 0.2f); // still inside the hold
            Assert.That(s.Masses, Is.EqualTo(1), "no second mass sound inside the hold");
            Assert.That(s.Kill(Vector3.zero, 1f, Kind.Normal, t + 3f), Is.True, "later: single groans again");
            Assert.That(s.Played, Is.EqualTo(2));
        }

        // ------------------------------------------------------------ Fall layer

        [Test]
        public void BodyFall_AfterTheVoice_LaterForABrute()
        {
            var s = Make(voiceSeconds: 0.2f);
            float t = T0;
            s.Kill(Vector3.zero, 1f, Kind.Normal, t);
            Assert.That(s.PendingFalls, Is.EqualTo(1));
            s.Tick(t + 0.1f);
            Assert.That(s.Falls, Is.Zero, "not yet");
            s.Tick(t + 0.17f);
            Assert.That(s.Falls, Is.EqualTo(1));
            Assert.That(s.PendingFalls, Is.Zero);

            float b = t + 2f;
            s.Kill(Vector3.zero, 1.25f, Kind.Brute, b);
            s.Tick(b + 0.17f);
            Assert.That(s.Falls, Is.EqualTo(1), "a brute's body lands later");
            s.Tick(b + 0.25f);
            Assert.That(s.Falls, Is.EqualTo(2));
        }

        // ------------------------------------------------------------ Space

        [Test]
        public void Distance_FadesToNothingFarFromTheKnight()
        {
            var s = Make(knight: true);
            _knight.position = new Vector3(3f, 0f, 3f);
            Assert.That(s.Gain(new Vector3(4f, 0f, 3f), out _), Is.EqualTo(1f).Within(1e-4f));
            float mid = s.Gain(new Vector3(18f, 0f, 3f), out _);
            Assert.That(mid, Is.InRange(0.01f, 0.5f));
            Assert.That(s.Gain(new Vector3(33f, 0f, 3f), out _), Is.Zero);
            Assert.That(s.Kill(new Vector3(33f, 0f, 3f), 1f, Kind.Normal, T0), Is.False);
            Assert.That(s.Dropped, Is.EqualTo(1));
        }

        [Test]
        public void Pan_FromTheScreenX_QuieterOffScreen()
        {
            var s = Make(camera: true);
            float centre = s.Gain(Vector3.zero, out float pc);
            s.Gain(new Vector3(-5f, 0f, 0f), out float left);
            s.Gain(new Vector3(5f, 0f, 0f), out float right);
            Assert.That(centre, Is.EqualTo(1f).Within(1e-4f));
            Assert.That(pc, Is.EqualTo(0f).Within(0.02f));
            Assert.That(left, Is.LessThan(-0.1f));
            Assert.That(right, Is.GreaterThan(0.1f));
            Assert.That(Mathf.Abs(left), Is.LessThanOrEqualTo(0.55f + 1e-4f), "pan width");
            Assert.That(s.Gain(new Vector3(40f, 0f, 0f), out _), Is.EqualTo(0.55f).Within(1e-4f), "off screen");

            Assert.That(s.Kill(new Vector3(5f, 0f, 0f), 1f, Kind.Normal, T0), Is.True);
            Assert.That(s.LastSource.panStereo, Is.EqualTo(right).Within(1e-4f));
        }

        // ------------------------------------------------------------ Event + GC

        [Test]
        public void KillEvent_PlaysAVoice_HitDoesNot()
        {
            var s = Make();
            var asset = AssetDatabase.LoadAssetAtPath<Enemy>(Dir + "Enemy_GraveyardLaborer.prefab");
            var enemy = Track(Object.Instantiate(asset).gameObject).GetComponent<Enemy>();
            enemy.gameObject.SetActive(false);
            enemy.Spawn(Vector3.zero, Track(new GameObject("Target")).transform, _ => { });
            enemy.TakeDamage(1f, Vector3.forward);
            Assert.That(s.Played + s.Dropped, Is.Zero, "a hit is not a death");
            enemy.TakeDamage(enemy.MaxHealth * 10f, Vector3.forward);
            Assert.That(enemy.IsAlive, Is.False);
            Assert.That(s.Played, Is.EqualTo(1));
            Assert.That(s.PendingFalls, Is.EqualTo(1));
        }

        [Test]
        public void Kills_NoGc()
        {
            var s = Make(knight: true, camera: true, voiceSeconds: 0.2f);
            float t = T0;
            s.Kill(Vector3.zero, 1f, Kind.Normal, t); // warm-up
            s.Tick(t + 1f);
            for (int i = 0; i < 6; i++) s.Kill(Vector3.zero, 1f, Kind.Normal, t + 1.2f); // warm the mass path
            s.KindOf(AssetDatabase.LoadAssetAtPath<Enemy>(Dir + "Enemy_GraveBrute.prefab"));
            Assert.That(() =>
            {
                for (int i = 0; i < 20; i++)
                {
                    float at = t + 2f + i * 0.08f;
                    s.Kill(new Vector3(i % 5 - 2f, 0f, 1f), 1f + i * 0.01f, (Kind)(i % 3), at);
                    s.Tick(at);
                }
                for (int i = 0; i < 8; i++) s.Kill(Vector3.zero, 1f, Kind.Normal, t + 10f);
                s.Tick(t + 12f);
            }, Is.Not.AllocatingGCMemory());
        }

        // ------------------------------------------------------------ Scene

        [UnityTest]
        public IEnumerator GameScene_ZombieDeathSoundsWired()
        {
            TearDown(); // this test uses the real scene's objects

            RunState.NewRun(TestRealm, 2341);
            SceneManager.LoadScene("Game");
            yield return null;
            yield return null;

            var sounds = Object.FindAnyObjectByType<ZombieDeathSounds>();
            Assert.That(sounds, Is.Not.Null, "ZombieDeathSounds on the audio object");
            Assert.That(sounds.VoiceClips, Is.GreaterThanOrEqualTo(8));
            Assert.That(sounds.VoiceCap, Is.EqualTo(4));
            var audio = Object.FindAnyObjectByType<GameAudio>();
            Assert.That(audio, Is.Not.Null);
            Assert.That(audio.gameObject, Is.EqualTo(sounds.gameObject));
            var so = new SerializedObject(audio);
            Assert.That(so.FindProperty("zombieDeaths").objectReferenceValue, Is.EqualTo(sounds), "GameAudio hands kills over");
            var own = new SerializedObject(sounds);
            Assert.That(own.FindProperty("listener").objectReferenceValue, Is.Not.Null, "Knight for the distance fade");
            Assert.That(own.FindProperty("view").objectReferenceValue, Is.Not.Null, "camera for the pan");
            Assert.That(own.FindProperty("falls").arraySize, Is.GreaterThanOrEqualTo(2));
            Assert.That(own.FindProperty("masses").arraySize, Is.GreaterThanOrEqualTo(1));
            for (int i = 0; i < sounds.VoiceClips; i++)
                Assert.That(own.FindProperty("voices").GetArrayElementAtIndex(i).objectReferenceValue, Is.Not.Null, "voice " + i);
        }
    }
}
#endif
