#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chamber;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.Gameplay.Skills.Effects;
using SurvivorSouls.Gameplay.World;
using SurvivorSouls.UI.Hud;
using UnityEngine;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-246 Ember Boons: the shipped 33-skill catalog against <c>Docs/skills/ember-boons.json</c> (name, tier, portrait
    /// art, card text), the shared rules (proc guard / no kill chains, Pierce + Ricochet hit budget, one burn per foe, boss
    /// crowd control, caps), the UI art styles, and the level 1 effect of each of the 19 new skills on the shipped assets.
    /// </summary>
    public sealed class EmberBoonsTests
    {
        private const float Frame = 1f / 60f;
        private const string ProgressionPath = "Assets/_Project/Data/Skills/SkillProgression_Run.asset";

        private readonly List<Object> _objects = new List<Object>();
        private GameObject _player;
        private PlayerStats _stats;
        private PlayerMover _mover;
        private AutoShooter _shooter;
        private PlayerHealth _health;
        private SkillManager _manager;
        private SkillProgression _shipped;
        private readonly List<BoltHit> _hits = new List<BoltHit>();

        [SetUp]
        public void SetUp()
        {
            Random.InitState(246);
            RunState.NewRun(1, 246);
            ProcGuard.Reset();
            EclipseFormation.Reset();
            _player = Track(new GameObject("Player"));
            _player.SetActive(false);
            _stats = _player.AddComponent<PlayerStats>();
            _player.AddComponent<CharacterController>();
            _mover = _player.AddComponent<PlayerMover>();
            _shooter = _player.AddComponent<AutoShooter>();
            _health = _player.AddComponent<PlayerHealth>();
            _player.SetActive(true);
            _shooter.enabled = false; // tests fire by hand
            _shooter.EnemyHit += h => _hits.Add(h);
            var template = Track(new GameObject("Bolt Template"));
            template.SetActive(false);
            _shooter.SetBoltPrefab(template.AddComponent<Bolt>());
            _shipped = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillProgression>(ProgressionPath);
            Assert.That(_shipped, Is.Not.Null);
            _manager = Track(new GameObject("Skills")).AddComponent<SkillManager>();
            _manager.Setup(_shipped, _stats, _shooter, _health, _player.transform, rngSeed: 246);
        }

        [TearDown]
        public void TearDown()
        {
            _manager.ResetRun();
            foreach (var b in Resources.FindObjectsOfTypeAll<Bolt>())
                if (b != null && b.gameObject.scene.IsValid()) Object.DestroyImmediate(b.gameObject);
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            _hits.Clear();
            Enemy.Walkability = null;
            ProcGuard.Reset();
            EclipseFormation.Reset();
            RunState.NewRun();
            GamePause.Clear();
            Time.timeScale = 1f;
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private SkillDefinition Shipped(string id)
        {
            var s = _shipped.FindSkill(id);
            Assert.That(s, Is.Not.Null, $"{id} is in the run table");
            return s;
        }

        private T Grant<T>(string id) where T : SkillEffect
        {
            var skill = Shipped(id);
            Assert.That(_manager.Grant(skill), Is.True);
            var fx = _manager.OwnedSkills.First(o => o.Skill == skill).Effect as T;
            Assert.That(fx, Is.Not.Null, $"{id} spawns its {typeof(T).Name}");
            return fx;
        }

        private SkillDefinition Stat(string id, params StatModifier[] mods) =>
            Track(ScriptableObject.CreateInstance<SkillDefinition>()).Configure(id, id, id, SkillRarity.Common, 1, mods);

        private static StatModifier Add(StatId s, float v) => new StatModifier(s, ModifierKind.Flat, v);
        private static StatModifier Pct(StatId s, float v) => new StatModifier(s, ModifierKind.Percent, v);

        private Enemy SpawnEnemy(Vector3 position, float healthMultiplier = 100f)
        {
            var e = Track(new GameObject("Enemy")).AddComponent<Enemy>();
            e.Spawn(position, null, _ => { }, healthMultiplier);
            return e;
        }

        private static void MakeBoss(Enemy e)
        {
            var so = new UnityEditor.SerializedObject(e);
            so.FindProperty("isBoss").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static IEnumerable<Bolt> LiveBolts() =>
            Resources.FindObjectsOfTypeAll<Bolt>().Where(b => b.gameObject.scene.IsValid() && b.InFlight);

        private static void Fly(float seconds)
        {
            for (float t = 0f; t < seconds; t += Frame)
                foreach (var b in LiveBolts().ToArray()) b.Tick(Frame);
        }

        private void Shoot(Enemy target, int times)
        {
            for (int i = 0; i < times; i++)
            {
                _shooter.Fire(target);
                Fly(0.6f);
            }
        }

        // ------------------------------------------------------------ Catalog

        [System.Serializable] private sealed class CatalogFile { public int skill_count; public CatalogSkill[] skills; }
        [System.Serializable] private sealed class CatalogSkill { public string id; public string name; public string rarity; }

        /// <summary>JSON id → asset id (ids are stable save keys: the renamed skills keep theirs).</summary>
        private static string AssetId(string catalogId) => catalogId switch
        {
            "frost-bolts" => "freeze_arrow",
            "spread-shot" => "multishot",
            "multishot" => "double_shot",
            "hallowed-shield" => "temporary_shield",
            _ => catalogId.Replace('-', '_'),
        };

        [Test]
        public void Catalog_All33Skills_ShippedWithNameTierPortraitAndShortText()
        {
            string path = Path.Combine(Application.dataPath, "../Docs/skills/ember-boons.json");
            var catalog = JsonUtility.FromJson<CatalogFile>(File.ReadAllText(path));
            Assert.That(catalog.skills.Length, Is.EqualTo(33));
            foreach (var c in catalog.skills)
            {
                var skill = Shipped(AssetId(c.id));
                Assert.That(skill.DisplayName.Replace('\'', '’'), Is.EqualTo(c.name.Replace('\'', '’')), c.id);
                Assert.That(skill.Rarity.ToString(), Is.EqualTo(c.rarity), c.id);
                Assert.That(skill.HasPortraitArt, Is.True, $"{c.id}: T_Boon portrait");
                Assert.That(UnityEditor.AssetDatabase.GetAssetPath(skill.Icon), Does.Contain("/Boons/T_Boon_"), c.id);
                for (int level = 1; level <= skill.MaxLevel; level++)
                {
                    string text = skill.DescriptionAt(level);
                    Assert.That(text, Is.Not.Empty, $"{c.id} L{level}");
                    Assert.That(text.Length, Is.LessThanOrEqualTo(36), $"{c.id} L{level}: '{text}'");
                }
            }
        }

        [Test]
        public void Catalog_RenamesKeepIds_EmberBurnsOnly_EclipseNeedsAnOrbital()
        {
            Assert.That(Shipped("freeze_arrow").DisplayName, Is.EqualTo("Frost Bolts"));
            Assert.That(Shipped("double_shot").DisplayName, Is.EqualTo("Multishot"));
            Assert.That(Shipped("multishot").DisplayName, Is.EqualTo("Spread Shot"));
            var ember = Shipped("ember_bolts");
            foreach (var row in ember.Levels)
            foreach (var m in row.modifiers)
                Assert.That(m.stat == StatId.BurnDps || m.stat == StatId.BurnDuration, Is.True, $"Ember Bolts: burn only ({m.stat})");
            var eclipse = Shipped("eclipse_crown");
            Assert.That(eclipse.RequiresAnyOf.Select(s => s.Id), Is.EquivalentTo(new[]
                { "holy_tomes", "ember_wheel", "rime_blades", "storm_satellites", "reaper_scythes", "sentinel_halo" }));
            Assert.That(_manager.CanOffer(eclipse), Is.False, "no orbital owned");
            _manager.Grant(Shipped("rime_blades"));
            Assert.That(_manager.CanOffer(eclipse), Is.True);
        }

        // ------------------------------------------------------------ Shared rules

        [Test]
        public void SoulBurst_BurstKillsStartNoNewBurst_AndNoKillChainScopesStartNone()
        {
            var burst = Grant<KillBurstEffect>("soul_burst");
            var a = SpawnEnemy(new Vector3(0f, 0f, 4f));
            SpawnEnemy(new Vector3(1f, 0f, 4f), 0.01f);
            SpawnEnemy(new Vector3(2f, 0f, 4f), 0.01f);
            a.TakeDamage(1e6f, Vector3.forward);
            burst.Tick();
            Assert.That(burst.BurstCount, Is.EqualTo(1));
            Assert.That(Enemy.Active.Count(e => _objects.Contains(e.gameObject)), Is.EqualTo(0), "the burst killed both weak foes");
            burst.Tick();
            Assert.That(burst.BurstCount, Is.EqualTo(1), "burst kills do not burst again");

            var d = SpawnEnemy(new Vector3(5f, 0f, 5f));
            using (ProcGuard.NoKillChains()) d.TakeDamage(1e6f, Vector3.forward);
            burst.Tick();
            Assert.That(burst.BurstCount, Is.EqualTo(1), "no kill chains inside the scope");
            Assert.That(ProcGuard.KillsChain, Is.True, "the scope closed");
        }

        [Test]
        public void HitBudget_PierceAndRicochetShareEight_RicochetFirst()
        {
            _stats.SetSource(Stat("p"), new[] { Add(StatId.Pierce, 8f) }, 1);
            _stats.SetSource(Stat("r"), new[] { Add(StatId.Ricochet, 8f) }, 1);
            var shot = _shooter.MakeShot();
            Assert.That(shot.Ricochet + shot.Pierce, Is.EqualTo(BoltShot.MaxExtraHits));
            Assert.That(shot.Ricochet, Is.EqualTo(8));
        }

        [Test]
        public void OneBurnPerFoe_EmberWheelAndEmberBolts_StrongerWinsNoStacking()
        {
            var e = SpawnEnemy(new Vector3(3f, 0f, 3f));
            e.ApplyBurn(25f, 3f);
            e.ApplyBurn(20f, 3f); // Ember Wheel L1 contact
            Assert.That(e.BurnDps, Is.EqualTo(25f));
            e.ApplyBurn(35f, 3f);
            Assert.That(e.BurnDps, Is.EqualTo(35f));
            var wheel = Grant<EmberWheelEffect>("ember_wheel");
            var touched = SpawnEnemy(new Vector3(wheel.Radius, 0f, 0f));
            touched.ApplyBurn(25f, 3f);
            for (int i = 0; i < 160 && wheel.ContactCount == 0; i++) wheel.Tick(Frame);
            Assert.That(wheel.ContactCount, Is.GreaterThan(0));
            Assert.That(touched.BurnDps, Is.EqualTo(25f), "the wheel's 20/s does not replace or add to a stronger burn");
        }

        [Test]
        public void Bosses_AreSlowedNeverFrozenStunnedOrPushed()
        {
            var boss = SpawnEnemy(new Vector3(0f, 0f, 4f));
            MakeBoss(boss);
            var frost = Grant<FreezeArrowEffect>("freeze_arrow");
            Shoot(boss, 3);
            Assert.That(boss.IsFrozen, Is.False);
            Assert.That(boss.Slow, Is.GreaterThan(0f));
            Assert.That(frost.BossSlowCount, Is.EqualTo(1));
            Assert.That(boss.Stun(1f, 1f), Is.False);
            Assert.That(boss.TryKnock(Vector3.forward, 8f, 1f), Is.False);
            var shock = Grant<StormshockEffect>("stormshock");
            shock.Release(boss, boss.AimPoint, 3);
            Assert.That(boss.IsStunned, Is.False, "Stormshock: damage and a reduced slow on bosses");
        }

        [Test]
        public void Caps_CritChanceFireIntervalMoveSpeed()
        {
            _stats.SetSource(Stat("crit"), new[] { Add(StatId.CritChance, 1f) }, 1);
            Assert.That(_shooter.CurrentCritChance, Is.EqualTo(AutoShooter.MaxCritChance));
            _stats.SetSource(Stat("fast"), new[] { Pct(StatId.AttackSpeed, 20f) }, 1);
            Assert.That(_shooter.CurrentInterval, Is.EqualTo(AutoShooter.MinFireInterval).Within(1e-5f));
            _stats.SetSource(Stat("run"), new[] { Pct(StatId.MoveSpeed, 3f) }, 1);
            Assert.That(_mover.MaxSpeed, Is.EqualTo(_mover.BaseSpeed * PlayerMover.MaxSpeedShare).Within(1e-4f));
        }

        [Test]
        public void ArtStyle_PortraitsUntintedCroppedInSlots_GlyphsKeepTheTierTint()
        {
            var art = Shipped("stormshock");
            var glyph = Shipped("soul_siphon");
            Assert.That(glyph.HasPortraitArt, Is.False, "no catalog art: keeps its glyph");
            var card = new VisualElement();
            SkillArtStyle.SetCardIcon(card, art);
            Assert.That(card.style.unityBackgroundImageTintColor.value, Is.EqualTo(Color.white));
            SkillArtStyle.SetCardIcon(card, glyph);
            Assert.That(card.style.unityBackgroundImageTintColor.keyword, Is.EqualTo(StyleKeyword.Null));
            var slot = new VisualElement();
            SkillArtStyle.SetSlotIcon(slot, art);
            Assert.That(slot.style.width.value.value, Is.EqualTo(SkillArtStyle.SlotCrop));
            Assert.That(slot.style.backgroundSize.value.x.value, Is.EqualTo(SkillArtStyle.SlotZoom * 100f).Within(0.01f));
            SkillArtStyle.SetSlotIcon(slot, glyph);
            Assert.That(slot.style.width.keyword, Is.EqualTo(StyleKeyword.Null));
            Assert.That(SkillArtStyle.CardIconHeight, Is.EqualTo(188f), "the reel slot of KAN-159 keeps its 188 px step");
        }

        // ------------------------------------------------------------ Rare

        [Test]
        public void VenomBolts_HitsPoison_StacksCapped_TicksDamage()
        {
            var venom = Grant<VenomBoltsEffect>("venom_bolts");
            var e = SpawnEnemy(new Vector3(0f, 0f, 4f));
            Shoot(e, 3);
            Assert.That(e.IsPoisoned, Is.True);
            Assert.That(e.PoisonStacks, Is.EqualTo(2), "level 1: 2 stacks at most");
            Assert.That(venom.PoisonCount, Is.EqualTo(3));
            float before = e.Health;
            for (int i = 0; i < 62; i++) e.TickStatus(Frame); // just past 1 s: the 0.5 s and 1 s ticks
            Assert.That(before - e.Health, Is.EqualTo(2 * 12f).Within(0.5f), "2 stacks x 12/s for 1 s");
            Assert.That(e.LastDamageKind, Is.EqualTo(DamageKind.Poison));
        }

        [Test]
        public void StormTouch_WeaponHitArcsToANearbyFoe_ArcsAreNoWeaponHits()
        {
            var touch = Grant<StormTouchEffect>("storm_touch");
            var first = SpawnEnemy(new Vector3(0f, 0f, 4f));
            var second = SpawnEnemy(new Vector3(2f, 0f, 4.5f));
            Shoot(first, 1);
            Assert.That(touch.ArcHits, Is.EqualTo(1));
            Assert.That(second.MaxHealth - second.Health, Is.EqualTo(35f).Within(0.01f));
            Assert.That(_hits.Count, Is.EqualTo(1), "only the bolt hit is a weapon hit");
        }

        [Test]
        public void AshenHeart_HealthPickupsHealMoreAndDropSooner()
        {
            _manager.Grant(Shipped("ashen_heart"));
            Assert.That(_stats.Modify(StatId.PickupHeal, 40f), Is.EqualTo(52f).Within(1e-3f));
            _manager.Grant(Shipped("ashen_heart"));
            Assert.That(_stats.Modify(StatId.PickupDrop, 1f), Is.EqualTo(1.15f).Within(1e-3f));
        }

        [Test]
        public void RepellingBolts_EveryFourthHitPushesAFoe_PerFoeCooldown()
        {
            var repel = Grant<RepellingBoltsEffect>("repelling_bolts");
            var e = SpawnEnemy(new Vector3(0f, 0f, 4f));
            Shoot(e, 3);
            Assert.That(repel.KnockCount, Is.EqualTo(0));
            Shoot(e, 1);
            Assert.That(repel.KnockCount, Is.EqualTo(1));
            Assert.That(e.KnockCooldown, Is.GreaterThan(0f));
        }

        // ------------------------------------------------------------ Epic

        [Test]
        public void Stormshock_FourthHitChainsToThree_StunsNotBosses_CooldownHolds()
        {
            var shock = Grant<StormshockEffect>("stormshock");
            var first = SpawnEnemy(new Vector3(0f, 0f, 4f));
            var b = SpawnEnemy(new Vector3(1.5f, 0f, 4.5f));
            var c = SpawnEnemy(new Vector3(3.5f, 0f, 5f));
            var far = SpawnEnemy(new Vector3(9f, 0f, 9f));
            Shoot(first, 4);
            Assert.That(shock.ChainCount, Is.EqualTo(1));
            Assert.That(shock.LastChainLength, Is.EqualTo(3));
            Assert.That(b.Health, Is.LessThan(b.MaxHealth));
            Assert.That(c.Health, Is.LessThan(c.MaxHealth));
            Assert.That(far.Health, Is.EqualTo(far.MaxHealth), "out of the 3 m jump");
            Assert.That(b.IsStunned, Is.True);
            Shoot(first, 4);
            Assert.That(shock.ChainCount, Is.EqualTo(1), "2 s chain cooldown");
            shock.Tick(2.1f);
            Shoot(first, 1);
            Assert.That(shock.ChainCount, Is.EqualTo(2), "the ready charge fires once the cooldown is over");
        }

        [Test]
        public void FrontArrow_AddsAParallelBoltAt75Percent()
        {
            _manager.Grant(Shipped("front_arrow"));
            var spec = _shooter.CurrentVolleySpec;
            Assert.That(spec.Front, Is.EqualTo(1));
            Assert.That(spec.FrontDamage, Is.EqualTo(0.75f).Within(1e-4f));
            _shooter.Fire(SpawnEnemy(new Vector3(0f, 0f, 8f)));
            var bolts = LiveBolts().ToArray();
            Assert.That(bolts.Length, Is.EqualTo(2));
            Assert.That(Vector3.Angle(bolts[0].Direction, bolts[1].Direction), Is.LessThan(0.5f), "parallel");
            Assert.That(Mathf.Abs(bolts[0].transform.position.x - bolts[1].transform.position.x), Is.GreaterThan(0.3f), "side by side");
        }

        [Test]
        public void RearArrow_FiresABoltBehindTheKnight()
        {
            _manager.Grant(Shipped("rear_arrow"));
            _shooter.Fire(SpawnEnemy(new Vector3(0f, 0f, 8f)));
            var bolts = LiveBolts().ToArray();
            Assert.That(bolts.Length, Is.EqualTo(2));
            Assert.That(bolts.Count(b => b.Direction.z < -0.9f), Is.EqualTo(1));
        }

        private sealed class WallAt : IWalkability
        {
            public float Z;
            public bool IsBlocked(Vector3 p, float r) => p.z + r > Z;
        }

        [Test]
        public void BouncyWall_BoltsReboundOffWalls()
        {
            var fx = Grant<BouncyWallEffect>("bouncy_wall");
            Enemy.Walkability = new WallAt { Z = 6f };
            _shooter.Fire(SpawnEnemy(new Vector3(0f, 0f, 9f)));
            Fly(0.4f);
            Assert.That(fx.ReboundCount, Is.EqualTo(1));
            Assert.That(LiveBolts().Single().Direction.z, Is.LessThan(-0.9f), "flies back from the wall");
        }

        [Test]
        public void EmberWheel_FirebrandsBurnWhatTheyTouch()
        {
            var wheel = Grant<EmberWheelEffect>("ember_wheel");
            var e = SpawnEnemy(new Vector3(0f, 0f, wheel.Radius));
            for (int i = 0; i < 160 && !e.IsBurning; i++) wheel.Tick(Frame);
            Assert.That(wheel.Count, Is.EqualTo(2));
            Assert.That(e.IsBurning, Is.True);
            Assert.That(e.BurnDps, Is.EqualTo(20f));
        }

        [Test]
        public void RimeBlades_SlowWhatTheyTouch()
        {
            var rime = Grant<RimeBladesEffect>("rime_blades");
            var e = SpawnEnemy(new Vector3(rime.Radius, 0f, 0f));
            for (int i = 0; i < 160 && e.Slow <= 0f; i++) rime.Tick(Frame);
            Assert.That(e.Slow, Is.EqualTo(rime.SlowShare).Within(1e-4f));
            Assert.That(e.Health, Is.LessThan(e.MaxHealth));
        }

        [Test]
        public void StormSatellites_ZapTheNearestFoeInRange()
        {
            var orbs = Grant<StormSatellitesEffect>("storm_satellites");
            var e = SpawnEnemy(new Vector3(0f, 0f, 4f));
            for (int i = 0; i < 120; i++) orbs.Tick(Frame);
            Assert.That(orbs.Zaps, Is.GreaterThanOrEqualTo(1));
            Assert.That(e.Health, Is.LessThan(e.MaxHealth));
            Assert.That(_hits, Is.Empty, "zaps are no weapon hits");
        }

        [Test]
        public void ReaperScythes_HeavyHitOncePerPass()
        {
            var scythes = Grant<ReaperScythesEffect>("reaper_scythes");
            var e = SpawnEnemy(new Vector3(scythes.Radius, 0f, 0f));
            for (int i = 0; i < 240 && e.Health >= e.MaxHealth; i++) scythes.Tick(Frame);
            Assert.That(e.MaxHealth - e.Health, Is.EqualTo(120f).Within(0.01f), "120 % of the 100 bolt damage");
            int contacts = scythes.ContactCount;
            for (int i = 0; i < 20; i++) scythes.Tick(Frame);
            Assert.That(scythes.ContactCount, Is.EqualTo(contacts), "no second hit in the same pass");
        }

        [Test]
        public void SentinelHalo_FragmentsStopEnemyShots_NoDamage()
        {
            var shots = Track(new GameObject("Shots")).AddComponent<EnemyProjectiles>();
            shots.Setup(_health, null, null);
            var halo = Grant<SentinelHaloEffect>("sentinel_halo");
            halo.Tick(Frame);
            Vector3 at = halo.Bodies[0].position;
            Assert.That(shots.FireStraight(1, ShotKind.Arrow, new Vector3(at.x, 0f, at.z), -at, 1f, 10f, 30f, 0.4f), Is.True);
            halo.Tick(Frame);
            Assert.That(halo.Blocked, Is.EqualTo(1));
            Assert.That(shots.Count, Is.EqualTo(0));
            Assert.That(halo.ContactCount, Is.EqualTo(0), "fragments deal no contact damage");
        }

        // ------------------------------------------------------------ Legendary

        [Test]
        public void SecondDawn_FatalHitRevivesOncePerRun_ImmuneThenShockwave()
        {
            var dawn = Grant<SecondDawnEffect>("second_dawn");
            var near = SpawnEnemy(new Vector3(1f, 0f, 1f));
            _health.TakeDamage(10000f);
            Assert.That(_health.IsDead, Is.False);
            Assert.That(_health.Current, Is.EqualTo(_health.Max * 0.4f).Within(0.5f));
            Assert.That(_health.ImmunityLeft, Is.EqualTo(dawn.Immunity));
            Assert.That(_health.TakeDamage(50f), Is.EqualTo(0f), "immune right after the revive");
            Assert.That(RunState.ReviveSpent, Is.True);
            Assert.That(dawn.Ready, Is.False);
            Assert.That(_health.LastStand(), Is.EqualTo(0f), "one revive per run");
            dawn.Shockwave();
            Assert.That(dawn.LastWaveHits, Is.EqualTo(1));
            Assert.That(near.Health, Is.LessThan(near.MaxHealth));
        }

        [Test]
        public void EclipseCrown_OrbitersAlignThenPulse()
        {
            var wheel = Grant<EmberWheelEffect>("ember_wheel");
            wheel.Tick(Frame);
            var eclipse = Grant<EclipseCrownEffect>("eclipse_crown");
            var e = SpawnEnemy(new Vector3(0f, 0f, 3f));
            eclipse.Begin();
            for (int i = 0; i < 30; i++) { eclipse.Tick(Frame); wheel.Tick(Frame); }
            Assert.That(EclipseFormation.Weight, Is.EqualTo(1f).Within(1e-3f));
            float gap = Vector3.Distance(wheel.Bodies[0].position, wheel.Bodies[1].position);
            Assert.That(gap, Is.LessThan(1f), "the two firebrands bunched into the formation");
            for (int i = 0; i < 80 && eclipse.Pulses == 0; i++) eclipse.Tick(Frame);
            Assert.That(eclipse.Pulses, Is.EqualTo(1));
            Assert.That(e.MaxHealth - e.Health, Is.GreaterThanOrEqualTo(150f - 0.01f), "150 % pulse (+ any wheel contact)");
            for (int i = 0; i < 40; i++) eclipse.Tick(Frame);
            Assert.That(EclipseFormation.Weight, Is.EqualTo(0f));
        }

        [Test]
        public void GraveChoir_ArchersCappedTheirShotsAreNoWeaponHits()
        {
            var choir = Grant<GraveChoirEffect>("grave_choir");
            var e = SpawnEnemy(new Vector3(0f, 0f, 6f));
            for (int i = 0; i < 4; i++) choir.Raise(new Vector3(i, 0f, 1f));
            Assert.That(choir.Standing, Is.EqualTo(choir.MaxArchers));
            for (int i = 0; i < 60; i++) choir.Tick(Frame);
            Assert.That(choir.Shots, Is.GreaterThanOrEqualTo(choir.MaxArchers));
            Fly(1f);
            Assert.That(e.Health, Is.LessThan(e.MaxHealth));
            Assert.That(_hits, Is.Empty, "archer bolts set off no weapon-hit skill");
        }

        [Test]
        public void LastOath_LowHealthSurge_DamageUpThenCooldownAndRearm()
        {
            var oath = Grant<LastOathEffect>("last_oath");
            _health.TakeDamage(_health.Max * 0.75f);
            oath.Tick(Frame);
            Assert.That(oath.Surging, Is.True);
            Assert.That(_shooter.CurrentDamage, Is.EqualTo(160f).Within(0.01f));
            for (int i = 0; i < 7; i++) oath.Tick(1f);
            Assert.That(oath.Surging, Is.False);
            Assert.That(_shooter.CurrentDamage, Is.EqualTo(100f).Within(0.01f));
            _health.Heal(_health.Max);
            oath.Tick(1f);
            Assert.That(oath.Armed, Is.False, "long cooldown");
            for (int i = 0; i < 50; i++) oath.Tick(1f);
            Assert.That(oath.Armed, Is.True, "rearmed above the threshold after the cooldown");
        }

        [Test]
        public void StolenMoment_EighthVolleyLeavesAnEchoThatFiresThreeTimes()
        {
            var echo = Grant<StolenMomentEffect>("stolen_moment");
            var e = SpawnEnemy(new Vector3(0f, 0f, 6f), 1000f);
            Shoot(e, 7);
            Assert.That(echo.Echoes, Is.EqualTo(0));
            Shoot(e, 1);
            Assert.That(echo.Echoes, Is.EqualTo(1));
            _hits.Clear();
            float before = e.Health;
            for (int i = 0; i < 90; i++) echo.Tick(Frame);
            Assert.That(echo.EchoVolleys, Is.EqualTo(3));
            Fly(1f);
            Assert.That(e.Health, Is.LessThan(before), "echo bolts hit");
            Assert.That(_hits, Is.Empty, "echo bolts are no weapon hits");
            Assert.That(_shooter.VolleysFired, Is.EqualTo(8), "echoes are no volleys");
        }

        [Test]
        public void SovereignsFall_SwordFallsOnTheTarget_HitsTheArea()
        {
            var sword = Grant<SovereignsFallEffect>("sovereigns_fall");
            var target = SpawnEnemy(new Vector3(0f, 0f, 5f));
            var side = SpawnEnemy(new Vector3(1.5f, 0f, 5f));
            var far = SpawnEnemy(new Vector3(8f, 0f, 5f));
            sword.Drop(target, target.transform.position);
            Assert.That(sword.Falling, Is.True);
            for (int i = 0; i < 30; i++) sword.Tick(Frame);
            Assert.That(sword.Strikes, Is.EqualTo(1));
            Assert.That(target.MaxHealth - target.Health, Is.EqualTo(400f).Within(0.01f));
            Assert.That(side.Health, Is.LessThan(side.MaxHealth));
            Assert.That(far.Health, Is.EqualTo(far.MaxHealth));
        }
    }
}
#endif
