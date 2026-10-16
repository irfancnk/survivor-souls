using System.Collections.Generic;
using NUnit.Framework;
using SurvivorSouls.Audio;
using SurvivorSouls.Core;
using SurvivorSouls.Editor.Audio;
using SurvivorSouls.Editor.Characters;
using SurvivorSouls.Editor.Combat;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Heroes;
using SurvivorSouls.Gameplay.Skills;
using UnityEditor;
using UnityEngine;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-153 hero rules: the shipped roster (Knight free default, Shadow Ranger 1,000 Ember Shards, fast-and-fragile
    /// profile with clear pros and cons, own model with the bow muzzle and shoot driver); buy (not enough = nothing changes,
    /// enough = charged once, owned after a "restart", picked); Test Mode (every hero free and pickable, nothing charged,
    /// no ownership written, off = back to the Knight); the saved pick (locked heroes cannot be picked, a lost pick = the
    /// default); a hand-edited ownership flag; the bow shoot phase rule. KAN-227: the Ranger's own arrow + bow twang (the
    /// Knight keeps the scene bolt and crossbow shots), the shot-clip override on GameAudio, the 8k hero tri budget.
    /// The editor's real wallet, Test Mode and hero keys
    /// are saved before each test and put back after it.
    /// </summary>
    public sealed class HeroProgressTests
    {
        private HeroRoster _shipped;
        private readonly List<Object> _objects = new List<Object>();
        private readonly Dictionary<string, int> _ints = new Dictionary<string, int>();
        private string _selected;
        private List<string> _keys;

        [SetUp]
        public void SetUp()
        {
            _shipped = AssetDatabase.LoadAssetAtPath<HeroRoster>(HeroBuilder.RosterPath);
            Assert.That(_shipped, Is.Not.Null, "shipped roster (run the Game scene rebuild)");
            _keys = new List<string> { GameSettings.KeyTestMode, TestMode.KeyGranted, SoulWallet.KeyBalance, SoulWallet.KeyCheck };
            _keys.AddRange(GemWallet.Keys);
            foreach (var hero in _shipped.Heroes)
            {
                _keys.Add(HeroProgress.KeyOf(hero));
                _keys.Add(HeroProgress.KeyOf(hero) + ".check");
            }
            _keys.Add(HeroProgress.OwnedPrefix + "test_hero");
            _keys.Add(HeroProgress.OwnedPrefix + "test_hero.check");
            _ints.Clear();
            foreach (var k in _keys)
                if (PlayerPrefs.HasKey(k)) _ints[k] = PlayerPrefs.GetInt(k);
            _selected = PlayerPrefs.HasKey(HeroProgress.KeySelected) ? PlayerPrefs.GetString(HeroProgress.KeySelected) : null;

            TestMode.ResetAll();
            SoulWallet.Clear();
            GemWallet.Clear();
            HeroProgress.ResetAll(_shipped);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            foreach (var k in _keys)
            {
                if (_ints.TryGetValue(k, out int v)) PlayerPrefs.SetInt(k, v);
                else PlayerPrefs.DeleteKey(k);
            }
            if (_selected != null) PlayerPrefs.SetString(HeroProgress.KeySelected, _selected);
            else PlayerPrefs.DeleteKey(HeroProgress.KeySelected);
            PlayerPrefs.Save();
        }

        private HeroDefinition Knight => _shipped.Find(HeroBuilder.KnightId);
        private HeroDefinition Ranger => _shipped.Find(HeroBuilder.RangerId);

        [Test]
        public void ShippedRoster_KnightFreeDefault_RangerExpensiveInShards()
        {
            Assert.That(_shipped.Heroes.Count, Is.EqualTo(2));
            Assert.That(_shipped.Heroes[0], Is.SameAs(Knight), "Knight card first");
            Assert.That(_shipped.Default, Is.SameAs(Knight));
            Assert.That(Knight.IsFree, Is.True);
            Assert.That(Knight.Modifiers, Is.Empty, "the Knight is the reference hero");
            Assert.That(Ranger.Currency, Is.EqualTo(HeroCurrency.EmberShards));
            Assert.That(Ranger.Price, Is.EqualTo(HeroBuilder.RangerPrice).And.EqualTo(1000));
            // Expensive: more than the 550 pack and more than every free shard source together (welcome 50 + first run 25).
            Assert.That(Ranger.Price, Is.GreaterThan(550 + 50 + 25));
            Assert.That(Ranger.Price, Is.LessThanOrEqualTo(1200), "the 1,200 pack covers it");
            Assert.That(Ranger.DisplayName, Is.EqualTo("The Shadow Ranger"));
            Assert.That(Ranger.Portrait, Is.Not.Null);
            Assert.That(Knight.Portrait, Is.Not.Null);
        }

        [Test]
        public void RangerProfile_FasterAndFragile_CardLinesMatchTheNumbers()
        {
            var r = Ranger;
            Assert.That(r.Apply(StatId.AttackSpeed, 1f), Is.EqualTo(1.3f).Within(1e-4f), "+30 % attack speed");
            Assert.That(r.Apply(StatId.MoveSpeed, 5f), Is.EqualTo(5.9f).Within(1e-4f), "+18 % move speed");
            Assert.That(r.Apply(StatId.MaxHealth, 400f), Is.EqualTo(300f).Within(1e-3f), "-25 % max health");
            Assert.That(r.Apply(StatId.Damage, 100f), Is.EqualTo(80f).Within(1e-3f), "-20 % damage per shot");
            Assert.That(r.Pros, Is.EquivalentTo(new[] { "+30% attack speed", "+18% move speed" }));
            Assert.That(r.Cons, Is.EquivalentTo(new[] { "-25% max health", "-20% arrow damage" }));
            foreach (var line in r.Pros) Assert.That(line.Split(' ').Length, Is.LessThanOrEqualTo(4), $"short card line '{line}'");
            foreach (var line in r.Cons) Assert.That(line.Split(' ').Length, Is.LessThanOrEqualTo(4), $"short card line '{line}'");
            // Same single-target DPS ballpark as the Knight (1.3 x 0.8 = 1.04): a trade-off, not a straight upgrade.
            float dps = r.Apply(StatId.AttackSpeed, 1f) * r.Apply(StatId.Damage, 1f);
            Assert.That(dps, Is.InRange(0.95f, 1.1f));
        }

        [Test]
        public void ProfileOnStats_IsPermanent_HordeBaselineSeesIt()
        {
            var go = new GameObject("Stats");
            _objects.Add(go);
            var stats = go.AddComponent<PlayerStats>();
            stats.SetSource(Ranger, Ranger.Modifiers, 1, permanent: true);
            Assert.That(stats.Modify(StatId.AttackSpeed, 1f), Is.EqualTo(1.3f).Within(1e-4f));
            Assert.That(stats.Modify(StatId.AttackSpeed, 1f, permanentOnly: true), Is.EqualTo(1.3f).Within(1e-4f),
                "permanent: the KAN-92 horde baseline counts it, so the horde does not scale up for the hero");
            Assert.That(stats.Modify(StatId.MaxHealth, 400f, permanentOnly: true), Is.EqualTo(300f).Within(1e-3f));
        }

        [Test]
        public void RangerModel_HasBowMuzzle_ShootDriver_AndDeathClip()
        {
            var model = Ranger.ModelPrefab;
            Assert.That(model, Is.Not.Null);
            Assert.That(model.name, Does.Contain("ShadowRanger"));
            var driver = model.GetComponent<BowShootDriver>();
            Assert.That(driver, Is.Not.Null);
            Assert.That(driver.Animator, Is.Not.Null);
            var ctrl = driver.Animator.runtimeAnimatorController;
            Assert.That(ctrl, Is.Not.Null);
            var clips = new HashSet<string>();
            foreach (var c in ctrl.animationClips) clips.Add(c.name);
            Assert.That(clips, Is.SupersetOf(new[] { "Idle", "Walking", "Running", "Shoot", "Death" }));
            bool muzzle = false;
            foreach (var t in model.GetComponentsInChildren<Transform>(true))
                if (t.name == HeroLoadout.MuzzleName) muzzle = t.parent != null && t.parent.name == "LeftHand";
            Assert.That(muzzle, Is.True, "Bow_Muzzle on the bow hand");
            foreach (var r in model.GetComponentsInChildren<Renderer>(true))
            foreach (var m in r.sharedMaterials)
                Assert.That(m, Is.Not.Null, $"{r.name}: every slot has a URP material");
        }

        [Test]
        public void RangerModel_InsideTheHeroTriBudget()
        {
            int tris = 0;
            foreach (var smr in Ranger.ModelPrefab.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                var mesh = smr.sharedMesh;
                Assert.That(mesh, Is.Not.Null, smr.name);
                for (int i = 0; i < mesh.subMeshCount; i++) tris += (int)mesh.GetIndexCount(i) / 3; // LOD0 indices
            }
            Assert.That(tris, Is.InRange(5000, 8000), "hero budget 5-8k tris (STATE.md DECISIONS), KAN-227 decimate");
        }

        [Test]
        public void Ranger_ShootsOwnArrowAndBowTwang_KnightKeepsTheSceneDefaults()
        {
            Assert.That(Knight.Projectile, Is.Null, "the Knight fires the scene bolt");
            Assert.That(Knight.ShotClips, Is.Empty, "the Knight plays the crossbow shots");

            var arrow = Ranger.Projectile;
            Assert.That(arrow, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(arrow), Is.EqualTo(CombatBuilder.ArrowPrefabPath));
            var bolt = AssetDatabase.LoadAssetAtPath<GameObject>(CombatBuilder.BoltPrefabPath).GetComponent<Bolt>();
            Assert.That(arrow, Is.Not.SameAs(bolt));
            var arrowMesh = arrow.transform.Find("Mesh").GetComponent<MeshFilter>().sharedMesh;
            var boltMesh = bolt.transform.Find("Mesh").GetComponent<MeshFilter>().sharedMesh;
            Assert.That(arrowMesh.bounds.size.z, Is.GreaterThan(boltMesh.bounds.size.z), "a bow arrow is longer than the bolt");
            Assert.That(arrowMesh.bounds.size.x, Is.LessThan(boltMesh.bounds.size.x), "and thinner");
            Assert.That(arrowMesh.triangles.Length / 3, Is.LessThan(200), "cheap pooled projectile");
            Assert.That(arrow.GetComponent<TrailRenderer>(), Is.Not.Null);
            Assert.That(arrow.transform.Find("Blaze Head"), Is.Not.Null, "Ember Bolts (KAN-200) still light the arrow head");

            var clips = Ranger.ShotClips;
            Assert.That(clips.Length, Is.EqualTo(3));
            foreach (var c in clips)
            {
                Assert.That(c.name, Does.StartWith(SfxBuilder.BowTwangName));
                Assert.That(c.length, Is.InRange(0.3f, 0.7f), "short twang");
                Assert.That(c.channels, Is.EqualTo(1));
            }
        }

        [Test]
        public void GameAudio_PlayerShots_HeroClipsOverride_EmptyFallsBackToCrossbow()
        {
            var go = new GameObject("Audio");
            _objects.Add(go);
            var audio = go.AddComponent<GameAudio>();
            var crossbow = new[] { AudioClip.Create("crossbow", 10, 1, 44100, false) };
            _objects.Add(crossbow[0]);
            var so = new SerializedObject(audio);
            var shots = so.FindProperty("shots");
            shots.arraySize = 1;
            shots.GetArrayElementAtIndex(0).objectReferenceValue = crossbow[0];
            so.ApplyModifiedPropertiesWithoutUndo();

            Assert.That(audio.PlayerShots, Is.EqualTo(crossbow));
            audio.SetPlayerShots(Ranger.ShotClips);
            Assert.That(audio.PlayerShots, Is.EqualTo(Ranger.ShotClips));
            audio.SetPlayerShots(Knight.ShotClips);
            Assert.That(audio.PlayerShots, Is.EqualTo(crossbow), "empty = back to the crossbow");
            audio.SetPlayerShots(null);
            Assert.That(audio.PlayerShots, Is.EqualTo(crossbow));
        }

        [Test]
        public void Buy_NotEnough_ChangesNothing_Enough_ChargesOnce_OwnedAfterRestart()
        {
            var r = Ranger;
            Assert.That(HeroProgress.IsOwned(r), Is.False);
            Assert.That(HeroProgress.IsUnlocked(r), Is.False);
            Assert.That(HeroProgress.Select(_shipped, r), Is.False, "a locked hero cannot be picked");
            Assert.That(HeroProgress.Current(_shipped), Is.SameAs(Knight));

            GemWallet.Earn(999);
            Assert.That(HeroProgress.TryBuy(r), Is.EqualTo(HeroBuyCheck.NotEnough));
            Assert.That(GemWallet.Balance, Is.EqualTo(999));
            Assert.That(HeroProgress.IsOwned(r), Is.False);

            GemWallet.Earn(501); // 1,500
            HeroDefinition bought = null;
            void OnBought(HeroDefinition h) => bought = h;
            HeroProgress.Bought += OnBought;
            try { Assert.That(HeroProgress.TryBuy(r), Is.EqualTo(HeroBuyCheck.Ok)); }
            finally { HeroProgress.Bought -= OnBought; }
            Assert.That(bought, Is.SameAs(r));
            Assert.That(GemWallet.Balance, Is.EqualTo(500), "charged 1,000 Ember Shards");
            Assert.That(SoulWallet.Balance, Is.EqualTo(0), "Souls untouched");
            Assert.That(PlayerPrefs.GetInt(HeroProgress.KeyOf(r)), Is.EqualTo(1), "saved");
            Assert.That(HeroProgress.IsOwned(r), Is.True, "owned on the next read (no in-memory state)");
            Assert.That(HeroProgress.TryBuy(r), Is.EqualTo(HeroBuyCheck.Owned));
            Assert.That(GemWallet.Balance, Is.EqualTo(500), "never charged twice");
        }

        [Test]
        public void Selection_Persists_LockedOrLostPickFallsBackToDefault()
        {
            var r = Ranger;
            Assert.That(HeroProgress.Select(_shipped, Knight), Is.True);
            Assert.That(PlayerPrefs.GetString(HeroProgress.KeySelected), Is.EqualTo(HeroBuilder.KnightId));

            GemWallet.Earn(1000);
            HeroProgress.TryBuy(r);
            Assert.That(HeroProgress.Select(_shipped, r), Is.True);
            Assert.That(PlayerPrefs.GetString(HeroProgress.KeySelected), Is.EqualTo(HeroBuilder.RangerId));
            Assert.That(HeroProgress.Current(_shipped), Is.SameAs(r), "the next run starts with the pick");

            PlayerPrefs.SetString(HeroProgress.KeySelected, "no_such_hero");
            Assert.That(HeroProgress.Current(_shipped), Is.SameAs(Knight), "lost pick = default");

            PlayerPrefs.SetString(HeroProgress.KeySelected, HeroBuilder.RangerId);
            PlayerPrefs.DeleteKey(HeroProgress.KeyOf(r)); // ownership gone (new device, cleared save)
            Assert.That(HeroProgress.Current(_shipped), Is.SameAs(Knight), "a saved pick of a locked hero is not honoured");
        }

        [Test]
        public void TestMode_EveryHeroFree_NothingChargedOrWritten_OffFallsBack()
        {
            var r = Ranger;
            TestMode.SetOn(true);
            GemWallet.Earn(5000);
            int souls = SoulWallet.Balance;
            Assert.That(HeroProgress.IsUnlocked(r), Is.True);
            Assert.That(HeroProgress.IsOwned(r), Is.False, "Test Mode is not ownership");
            Assert.That(HeroProgress.Check(r), Is.EqualTo(HeroBuyCheck.TestModeFree));
            Assert.That(HeroProgress.TryBuy(r), Is.EqualTo(HeroBuyCheck.TestModeFree));
            Assert.That(GemWallet.Balance, Is.EqualTo(5000), "nothing charged");
            Assert.That(SoulWallet.Balance, Is.EqualTo(souls));
            Assert.That(PlayerPrefs.HasKey(HeroProgress.KeyOf(r)), Is.False, "no fake ownership written");

            Assert.That(HeroProgress.Select(_shipped, r), Is.True);
            Assert.That(HeroProgress.Current(_shipped), Is.SameAs(r));

            TestMode.SetOn(false);
            Assert.That(HeroProgress.IsUnlocked(r), Is.False);
            Assert.That(HeroProgress.Current(_shipped), Is.SameAs(Knight), "real progress: back to the Knight");
            TestMode.SetOn(true);
            Assert.That(HeroProgress.Current(_shipped), Is.SameAs(r), "the saved pick works again");
        }

        [Test]
        public void TestMode_StoreBuildLock_KeepsHeroLocked()
        {
            TestMode.SetOn(true);
            TestMode.SimulateStoreBuild = true;
            try
            {
                Assert.That(HeroProgress.IsUnlocked(Ranger), Is.False);
                Assert.That(HeroProgress.Check(Ranger), Is.EqualTo(HeroBuyCheck.NotEnough));
            }
            finally { TestMode.SimulateStoreBuild = false; }
        }

        [Test]
        public void SoulsPrice_SpendsSouls_HandEditedFlag_ReadsNotOwned()
        {
            var hero = HeroDefinition.Create("test_hero", "Test", HeroCurrency.Souls, 300);
            _objects.Add(hero);
            SoulWallet.Earn(450);
            Assert.That(HeroProgress.TryBuy(hero), Is.EqualTo(HeroBuyCheck.Ok));
            Assert.That(SoulWallet.Balance, Is.EqualTo(150));
            Assert.That(GemWallet.Balance, Is.EqualTo(0));

            PlayerPrefs.SetInt(HeroProgress.KeyOf(Ranger), 1); // hand edit without the checksum
            Assert.That(HeroProgress.IsOwned(Ranger), Is.False);
            Assert.That(HeroProgress.TamperDetected, Is.True);
            Assert.That(PlayerPrefs.GetInt(HeroProgress.KeyOf(Ranger)), Is.EqualTo(0), "written back as 0");
        }

        [Test]
        public void BowPhase_ReleaseOnTheShot_OneLoopPerInterval_HoldsFullDraw()
        {
            const float release = 0.63f;
            Assert.That(BowShootDriver.PhaseAt(0f, 0.77f, release), Is.EqualTo(release).Within(1e-4f), "shot = release frame");
            Assert.That(BowShootDriver.PhaseAt(0.77f * 0.40f, 0.77f, release), Is.EqualTo(0.03f).Within(1e-3f), "clip loops after the follow-through");
            Assert.That(BowShootDriver.PhaseAt(0.77f * 0.99f, 0.77f, release), Is.EqualTo(0.62f).Within(1e-3f), "back at the release for the next shot");
            Assert.That(BowShootDriver.PhaseAt(5f, 0.77f, release), Is.EqualTo(release - 0.001f).Within(1e-3f), "no shot due: hold full draw");
            Assert.That(BowShootDriver.PhaseAt(float.PositiveInfinity, 1f, release), Is.EqualTo(release - 0.001f).Within(1e-3f));
        }

        [Test]
        public void DevArg_ValueAfterKey()
        {
            Assert.That(HeroLoadout.ArgValue(new[] { "exe", "-ss-hero", "shadow_ranger" }, HeroLoadout.DevArg), Is.EqualTo("shadow_ranger"));
            Assert.That(HeroLoadout.ArgValue(new[] { "exe", "-ss-hero" }, HeroLoadout.DevArg), Is.Null);
            Assert.That(HeroLoadout.ArgValue(new[] { "exe" }, HeroLoadout.DevArg), Is.Null);
        }
    }
}
