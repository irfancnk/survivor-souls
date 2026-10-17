using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using SurvivorSouls.Audio;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Progression;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.Gameplay.Skills.Effects;
using UnityEngine;
using UnityEngine.TestTools.Constraints;
using Is = UnityEngine.TestTools.Constraints.Is;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-123 Holy Tomes: the level's book count shows, evenly spaced on the orbit radius at chest height; a book hits a
    /// foe it passes for bolt damage x the share (damage skills help), the same book hits the same foe once per 0.5 s, no
    /// hit outside the reach; bosses take hits but are not pushed, level 7 knockback pushes, the level 8 holy flash splashes
    /// neighbours; the books hide and stop hitting when the Knight is dead; the shipped asset has the ticket's 8-level table,
    /// sits in the level 4 tier and its prefab holds 8 books of one mesh under 300 triangles with outward faces; the power
    /// snapshot counts the tome DPS; the thump is rate limited and the whoosh loop follows the books; a tome frame with
    /// hits allocates nothing.
    /// </summary>
    public sealed class HolyTomesTests
    {
        private const string AssetPath = "Assets/_Project/Data/Skills/Skill_HolyTomes.asset";
        private const string ProgressionPath = "Assets/_Project/Data/Skills/SkillProgression_Run.asset";

        private readonly List<Object> _objects = new List<Object>();
        private GameObject _player;
        private PlayerStats _stats;
        private AutoShooter _shooter;
        private PlayerHealth _health;
        private SkillManager _manager;

        [SetUp]
        public void SetUp()
        {
            Random.InitState(123);
            _player = Track(new GameObject("Player"));
            _player.SetActive(false); // Awake after every component exists (readers grab PlayerStats in Awake)
            _stats = _player.AddComponent<PlayerStats>();
            _player.AddComponent<CharacterController>();
            _player.AddComponent<PlayerMover>();
            _shooter = _player.AddComponent<AutoShooter>();
            _health = _player.AddComponent<PlayerHealth>();
            _player.AddComponent<SoulCollector>();
            _player.SetActive(true);
            _shooter.enabled = false;

            var template = Track(new GameObject("Bolt Template"));
            template.SetActive(false);
            _shooter.SetBoltPrefab(template.AddComponent<Bolt>());

            _manager = Track(new GameObject("Skills")).AddComponent<SkillManager>();
            _manager.Setup(Track(ScriptableObject.CreateInstance<SkillProgression>()), _stats, _shooter, _health, _player.transform, rngSeed: 123);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var b in Resources.FindObjectsOfTypeAll<Bolt>())
                if (b != null && b.gameObject.scene.IsValid()) Object.DestroyImmediate(b.gameObject);
            foreach (var o in _objects)
                if (o != null) Object.DestroyImmediate(o);
            _objects.Clear();
            GamePause.Clear();
        }

        private T Track<T>(T o) where T : Object
        {
            _objects.Add(o);
            return o;
        }

        private static StatModifier Add(StatId s, float v) => new StatModifier(s, ModifierKind.Flat, v);

        private static void Set(object target, string field, object value) =>
            target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(target, value);

        private static T Get<T>(object target, string field) =>
            (T)target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target);

        /// <summary>Grants a tome skill (spin 1000 s per turn by default: the books stand still for the hit tests).</summary>
        private OrbitingTomesEffect GrantTomes(int books, float radius = 2f, float spin = 1000f, float damage = 0.6f,
            float knock = 0f, float flash = 0f)
        {
            var mods = new List<StatModifier>
            {
                Add(StatId.TomeCount, books), Add(StatId.TomeRadius, radius), Add(StatId.TomeSpin, spin), Add(StatId.TomeDamage, damage),
            };
            if (knock > 0f) mods.Add(Add(StatId.TomeKnockback, knock));
            if (flash > 0f) mods.Add(Add(StatId.TomeFlash, flash));
            var s = Track(ScriptableObject.CreateInstance<SkillDefinition>()).Configure("tomes", "tomes", "tomes", SkillRarity.Rare, 1,
                mods.ToArray());
            var fx = Track(new GameObject("tomes FX"));
            var effect = fx.AddComponent<OrbitingTomesEffect>();
            var books_ = new Transform[OrbitingTomesEffect.MaxTomes];
            for (int i = 0; i < books_.Length; i++)
            {
                var b = new GameObject($"Tome {i}");
                b.transform.SetParent(fx.transform, false);
                b.SetActive(false);
                books_[i] = b.transform;
            }
            Set(effect, "tomes", books_);
            Set(effect, "glow", NewParticles(fx, "Glow"));
            s.SetPresentation(null, effect);
            _manager.Grant(s);
            return (OrbitingTomesEffect)_manager.OwnedSkills.Single().Effect;
        }

        private static ParticleSystem NewParticles(GameObject parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent.transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.maxParticles = 500;
            main.startLifetime = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.enabled = false;
            return ps;
        }

        private Enemy SpawnEnemy(Vector3 position, bool boss = false)
        {
            var e = Track(new GameObject("Enemy")).AddComponent<Enemy>();
            if (boss) Set(e, "isBoss", true);
            e.Spawn(position, null, _ => { }, healthMultiplier: 50f); // tanky: hits never kill it here
            return e;
        }

        private static int Shown(OrbitingTomesEffect fx) => fx.Tomes.Count(t => t != null && t.gameObject.activeSelf);

        // ------------------------------------------------------------ Orbit

        [Test]
        public void BookCount_FollowsTheLevel_EvenlySpacedOnTheRadius_AtChestHeight()
        {
            var fx = GrantTomes(books: 3, radius: 2.6f, spin: 1.8f);
            fx.Tick(0.1f);
            Assert.That(fx.Count, Is.EqualTo(3));
            Assert.That(Shown(fx), Is.EqualTo(3), "level book count on show, the rest hidden");
            var angles = new float[3];
            for (int i = 0; i < 3; i++)
            {
                Vector3 p = fx.BookPosition(i, 3);
                Vector3 flat = new Vector3(p.x, 0f, p.z);
                Assert.That(flat.magnitude, Is.EqualTo(2.6f).Within(1e-3f), $"book {i} on the orbit radius");
                Assert.That(p.y, Is.EqualTo(fx.Height).Within(1e-4f), "chest height");
                angles[i] = Mathf.Atan2(p.z, p.x);
            }
            for (int i = 0; i < 3; i++)
            {
                float gap = Mathf.Repeat(angles[(i + 1) % 3] - angles[i], Mathf.PI * 2f);
                Assert.That(gap, Is.EqualTo(Mathf.PI * 2f / 3f).Within(1e-3f), "even spacing");
            }
            Assert.That(fx.Angle, Is.EqualTo(0.1f * Mathf.PI * 2f / 1.8f).Within(1e-4f), "one turn per TomeSpin seconds");
        }

        [Test]
        public void Books_FollowTheKnight()
        {
            var fx = GrantTomes(books: 1, radius: 2f);
            _player.transform.position = new Vector3(5f, 0f, -3f);
            fx.Tick(0.016f);
            Vector3 p = fx.Tomes[0].position;
            Assert.That(new Vector2(p.x - 5f, p.z + 3f).magnitude, Is.EqualTo(2f).Within(0.01f));
        }

        // ------------------------------------------------------------ Hits

        [Test]
        public void Hit_DealsBoltDamageTimesShare_OncePerCooldownPerBook()
        {
            var fx = GrantTomes(books: 1, radius: 2f, damage: 0.6f);
            var e = SpawnEnemy(new Vector3(2f, 0f, 0f)); // on book 0 (angle 0)
            float start = e.Health;
            for (int i = 0; i < 4; i++) fx.Tick(0.1f);
            Assert.That(fx.HitCount, Is.EqualTo(1), "one hit inside the 0.5 s cooldown");
            Assert.That(start - e.Health, Is.EqualTo(_shooter.CurrentDamage * 0.6f).Within(1e-3f), "bolt damage x 60 %");
            for (int i = 0; i < 3; i++) fx.Tick(0.1f);
            Assert.That(fx.HitCount, Is.EqualTo(2), "hits again after the cooldown");
        }

        [Test]
        public void DamageSkills_HelpTheTomes()
        {
            var fx = GrantTomes(books: 1, damage: 0.7f);
            var owner = Track(ScriptableObject.CreateInstance<SkillDefinition>());
            _stats.SetSource(owner, new[] { new StatModifier(StatId.Damage, ModifierKind.Percent, 0.5f) }, 1);
            var e = SpawnEnemy(new Vector3(2f, 0f, 0f));
            float start = e.Health;
            fx.Tick(0.1f);
            Assert.That(start - e.Health, Is.EqualTo(150f * 0.7f).Within(1e-3f), "100 x 1.5 x 70 %");
        }

        [Test]
        public void NoHit_OutsideTheReach()
        {
            var fx = GrantTomes(books: 1, radius: 2f);
            float reach = fx.TomeRadius + 0.45f;
            var near = SpawnEnemy(new Vector3(0.6f, 0f, 0f)); // inside the orbit, off the book
            var far = SpawnEnemy(new Vector3(2f + reach + 0.1f, 0f, 0f));
            for (int i = 0; i < 10; i++) fx.Tick(0.1f);
            Assert.That(fx.HitCount, Is.EqualTo(0));
            Assert.That(near.IsHurt || far.IsHurt, Is.False);
        }

        [Test]
        public void TwoBooks_HitTheSameFoe_OnTheirOwnCooldowns()
        {
            var fx = GrantTomes(books: 2, radius: 0.6f); // both books overlap a foe on the Knight
            SpawnEnemy(Vector3.zero);
            fx.Tick(0.1f);
            Assert.That(fx.HitCount, Is.EqualTo(2), "each book hits once");
            fx.Tick(0.1f);
            Assert.That(fx.HitCount, Is.EqualTo(2));
        }

        [Test]
        public void Boss_TakesHits_IsNotPushed_Level7Knockback_PushesFoes()
        {
            var fx = GrantTomes(books: 2, radius: 2f, knock: 3f);
            var boss = SpawnEnemy(new Vector3(2f, 0f, 0f), boss: true);
            var foe = SpawnEnemy(new Vector3(-2f, 0f, 0f)); // on book 1 (angle pi)
            float bossStart = boss.Health;
            fx.Tick(0.1f);
            Assert.That(boss.Health, Is.LessThan(bossStart), "tomes work on bosses");
            Assert.That(Get<Vector3>(boss, "_knock").sqrMagnitude, Is.EqualTo(0f), "bosses are not pushed");
            Vector3 knock = Get<Vector3>(foe, "_knock");
            Assert.That(knock.magnitude, Is.EqualTo(3f).Within(1e-3f), "level 7 knockback speed");
            Assert.That(knock.x, Is.LessThan(0f), "pushed out, away from the Knight");
        }

        [Test]
        public void HolyFlash_SplashesNeighbours_ForTheShare()
        {
            var fx = GrantTomes(books: 1, radius: 2f, damage: 1f, flash: 1.3f);
            var struck = SpawnEnemy(new Vector3(2f, 0f, 0f));
            var neighbour = SpawnEnemy(new Vector3(2f, 0f, 1.2f)); // outside the book reach, inside the flash
            var away = SpawnEnemy(new Vector3(2f, 0f, 4f));
            float n0 = neighbour.Health;
            fx.Tick(0.1f);
            Assert.That(fx.HitCount, Is.EqualTo(1), "only the struck foe takes the book hit");
            Assert.That(fx.FlashHits, Is.EqualTo(1));
            Assert.That(n0 - neighbour.Health, Is.EqualTo(_shooter.CurrentDamage * fx.FlashShare).Within(1e-3f));
            Assert.That(away.IsHurt, Is.False);
            Assert.That(struck.IsHurt, Is.True);
        }

        [Test]
        public void DeadKnight_HidesTheBooks_AndStopsTheHits()
        {
            var fx = GrantTomes(books: 2, radius: 2f);
            fx.Tick(0.1f);
            Assert.That(Shown(fx), Is.EqualTo(2));
            Assert.That(OrbitingTomesEffect.VisibleTomes, Is.EqualTo(2));
            _health.TakeDamage(1e6f);
            Assert.That(_health.IsDead, Is.True);
            var e = SpawnEnemy(new Vector3(2f, 0f, 0f));
            fx.Tick(0.1f);
            Assert.That(fx.Hidden, Is.True);
            Assert.That(Shown(fx), Is.EqualTo(0));
            Assert.That(OrbitingTomesEffect.VisibleTomes, Is.EqualTo(0));
            Assert.That(e.IsHurt, Is.False);
        }

        [Test]
        public void Hits_EmitSparks_AndRaiseTheSoundEvent()
        {
            var fx = GrantTomes(books: 1, radius: 2f);
            SpawnEnemy(new Vector3(2f, 0f, 0f));
            int raised = 0;
            System.Action<Enemy, Vector3> onHit = (_, __) => raised++;
            OrbitingTomesEffect.TomeHit += onHit;
            try
            {
                fx.Tick(0.1f);
            }
            finally
            {
                OrbitingTomesEffect.TomeHit -= onHit;
            }
            Assert.That(raised, Is.EqualTo(1));
            Assert.That(Get<ParticleSystem>(fx, "glow").particleCount, Is.GreaterThan(1), "trail motes + hit sparks");
        }

        [Test]
        public void TomeFrame_WithHits_AllocatesNothing()
        {
            var fx = GrantTomes(books: 5, radius: 2f, spin: 1.3f, damage: 0.1f, knock: 3f, flash: 1.3f);
            for (int i = 0; i < 30; i++)
            {
                float a = i * 0.21f;
                SpawnEnemy(new Vector3(Mathf.Cos(a) * (1.5f + (i % 3) * 0.5f), 0f, Mathf.Sin(a) * (1.5f + (i % 3) * 0.5f)));
            }
            for (int i = 0; i < 10; i++) fx.Tick(0.05f); // warm up (JIT, first hits)
            int before = fx.HitCount;
            Assert.That(() =>
            {
                for (int i = 0; i < 20; i++) fx.Tick(0.05f);
            }, Is.Not.AllocatingGCMemory());
            Assert.That(fx.HitCount, Is.GreaterThan(before), "the measured frames did hit");
        }

        // ------------------------------------------------------------ Power curve

        [Test]
        public void PowerSnapshot_CountsTheTomeDps_SingleAndCrowd()
        {
            var b = PowerBase.Default;
            var bare = PowerSnapshot.Measure(_stats, b);
            var owner = Track(ScriptableObject.CreateInstance<SkillDefinition>());
            _stats.SetSource(owner, new[]
            {
                Add(StatId.TomeCount, 3f), Add(StatId.TomeSpin, 1.5f), Add(StatId.TomeDamage, 0.8f), Add(StatId.TomeRadius, 2.6f),
            }, 1);
            var s = PowerSnapshot.Measure(_stats, b);
            float tome = 100f * 0.8f * 3f / 1.5f * b.TomeContact;
            Assert.That(s.TomeDps, Is.EqualTo(tome).Within(1e-3f));
            Assert.That(s.SingleTargetDps - bare.SingleTargetDps, Is.EqualTo(tome).Within(1e-3f));
            Assert.That(s.CrowdDps - bare.CrowdDps, Is.EqualTo(tome * b.TomeFoes).Within(1e-3f));
            _stats.RemoveSource(owner);
            Assert.That(PowerSnapshot.Measure(_stats, b).TomeDps, Is.EqualTo(0f));
        }

        // ------------------------------------------------------------ Sound

        [Test]
        public void Thump_RateLimited_ToEightPerSecond_WhooshFollowsTheBooks()
        {
            var go = Track(new GameObject("Tome Sounds"));
            var clip = AudioClip.Create("thump", 4410, 1, 44100, false);
            var loop = AudioClip.Create("whoosh", 44100, 1, 44100, false);
            Track(clip);
            Track(loop);
            go.SetActive(false);
            var sounds = go.AddComponent<TomeSounds>();
            sounds.Setup(new[] { clip }, loop);
            go.SetActive(true);
            float now = Time.unscaledTime + 100f;
            Assert.That(sounds.Play(false, now), Is.True);
            Assert.That(sounds.Play(false, now + 0.05f), Is.False, "inside 0.125 s");
            Assert.That(sounds.Play(false, now + 0.13f), Is.True);
            Assert.That(sounds.Played, Is.EqualTo(2));
            Assert.That(sounds.Dropped, Is.EqualTo(1));

            sounds.TickLoop(3, paused: false, dt: 1f);
            Assert.That(sounds.LoopLevel, Is.EqualTo(1f), "whoosh up while books show");
            sounds.TickLoop(3, paused: true, dt: 0.2f);
            Assert.That(sounds.LoopLevel, Is.LessThan(1f), "fades on pause");
            sounds.TickLoop(0, paused: false, dt: 1f);
            Assert.That(sounds.LoopLevel, Is.EqualTo(0f), "silent while hidden");
        }

#if UNITY_EDITOR
        // ------------------------------------------------------------ Shipped asset

        [Test]
        public void ShippedAsset_EightLevels_TicketTable_LevelFourTier_PrefabBooks()
        {
            var skill = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillDefinition>(AssetPath);
            Assert.That(skill, Is.Not.Null);
            Assert.That(skill.Id, Is.EqualTo("holy_tomes"));
            Assert.That(skill.Rarity, Is.EqualTo(SkillRarity.Epic), "KAN-246 Ember Boons: Epic");
            Assert.That(skill.MaxLevel, Is.EqualTo(8));
            Assert.That(skill.Icon, Is.Not.Null);
            float Stat(int level, StatId id) => skill.Levels[level - 1].modifiers.Where(m => m.stat == id).Sum(m => m.value);
            int[] books = { 1, 2, 2, 3, 3, 4, 4, 5 };
            float[] radius = { 2f, 2f, 2.6f, 2.6f, 2.6f, 3f, 3.2f, 3.4f };
            float[] spin = { 2f, 2f, 2f, 1.8f, 1.5f, 1.5f, 1.4f, 1.3f };
            float[] damage = { 0.6f, 0.6f, 0.7f, 0.7f, 0.8f, 0.8f, 0.9f, 1f };
            for (int l = 1; l <= 8; l++)
            {
                Assert.That(Stat(l, StatId.TomeCount), Is.EqualTo(books[l - 1]), $"L{l} books");
                Assert.That(Stat(l, StatId.TomeRadius), Is.EqualTo(radius[l - 1]).Within(1e-4f), $"L{l} radius");
                Assert.That(Stat(l, StatId.TomeSpin), Is.EqualTo(spin[l - 1]).Within(1e-4f), $"L{l} spin");
                Assert.That(Stat(l, StatId.TomeDamage), Is.EqualTo(damage[l - 1]).Within(1e-4f), $"L{l} damage");
                Assert.That(Stat(l, StatId.TomeKnockback) > 0f, Is.EqualTo(l >= 7), $"L{l} knockback from level 7");
                Assert.That(Stat(l, StatId.TomeFlash) > 0f, Is.EqualTo(l == 8), $"L{l} holy flash at level 8");
            }

            var table = UnityEditor.AssetDatabase.LoadAssetAtPath<SkillProgression>(ProgressionPath);
            var tier = table.Tiers.Single(t => t.unlocks != null && t.unlocks.Contains(skill));
            Assert.That(tier.fromLevel, Is.EqualTo(4), "joins the level 4 tier");

            var fx = skill.EffectPrefab as OrbitingTomesEffect;
            Assert.That(fx, Is.Not.Null);
            Assert.That(fx.Tomes.Length, Is.EqualTo(OrbitingTomesEffect.MaxTomes));
            var filters = fx.Tomes.Select(t => t.GetComponent<MeshFilter>()).ToArray();
            Assert.That(filters.All(f => f != null && f.sharedMesh == filters[0].sharedMesh), Is.True, "one shared mesh");
            var mats = fx.Tomes.Select(t => t.GetComponent<MeshRenderer>().sharedMaterial).Distinct().ToArray();
            Assert.That(mats.Length, Is.EqualTo(1), "one shared material");
            Assert.That(fx.Tomes.All(t => !t.gameObject.activeSelf), Is.True, "books hidden until the effect shows them");

            var mesh = filters[0].sharedMesh;
            int[] tris = mesh.triangles;
            Assert.That(tris.Length / 3, Is.LessThan(300), "ticket budget: under 300 triangles");
            var v = mesh.vertices;
            var n = mesh.normals;
            for (int i = 0; i < tris.Length; i += 3)
            {
                Vector3 face = Vector3.Cross(v[tris[i + 1]] - v[tris[i]], v[tris[i + 2]] - v[tris[i]]);
                Assert.That(Vector3.Dot(face, n[tris[i]]), Is.GreaterThan(0f), $"triangle {i / 3} faces outward (clockwise winding)");
            }
        }
#endif
    }
}
