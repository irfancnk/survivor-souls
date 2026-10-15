using System.Linq;
using NUnit.Framework;
using SurvivorSouls.Editor.Enemies;
using SurvivorSouls.Gameplay.Combat;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace SurvivorSouls.Tests
{
    /// <summary>
    /// KAN-104: the 15 roster prefabs as the builder left them (run the Game scene builder first): mesh, material, animator
    /// and clips wired, sizes from Docs/ENEMY_ROSTER.md, in-place walk clips, long action clips cut to their strike, and the
    /// one-time wave table move to the four melee tiers.
    /// </summary>
    public sealed class EnemyRosterBuilderTests
    {
        private const string WavesPath = "Assets/_Project/Data/Combat/EnemyWaves_Realm1.asset";
        private const string ShamblerPath = "Assets/_Project/Prefabs/Combat/Enemy_Zombie.prefab";

        /// <summary>Rig heights of Docs/ENEMY_ROSTER.md section 2 (m).</summary>
        private static readonly (string id, float height)[] DocHeights =
        {
            ("GraveyardLaborer", 1.72f), ("FallenMilitiaman", 1.78f), ("RustedWatchman", 1.8f), ("GraveboundKnight", 1.85f),
            ("PlagueBloater", 1.8f), ("BoneArcher", 1.78f), ("PyreThrower", 1.75f), ("BileSpitter", 1.7f),
            ("FrenziedGhoul", 1.7f), ("GraveBrute", 2.3f), ("HexboundShaman", 1.75f), ("AshgraveButcher", 4.2f),
            ("PyreLord", 4.5f), ("RottingColossus", 5.0f), ("BoneMatriarch", 4.0f),
        };

        private static string[] Ids => EnemyRosterBuilder.Roster.Select(s => s.Id).ToArray();

        private static AnimatorState State(AnimatorController controller, string name) =>
            controller.layers[0].stateMachine.states.Select(s => s.state).FirstOrDefault(s => s.name == name);

        [Test]
        public void Roster_HasAllFifteenCharacters()
        {
            Assert.That(Ids, Is.EquivalentTo(DocHeights.Select(d => d.id)));
            Assert.That(EnemyRosterBuilder.Roster.Count(s => s.Boss), Is.EqualTo(4));
        }

        [Test]
        public void Prefab_IsWired([ValueSource(nameof(Ids))] string id)
        {
            var spec = EnemyRosterBuilder.Find(id);
            var enemy = AssetDatabase.LoadAssetAtPath<Enemy>(spec.PrefabPath);
            Assert.That(enemy, Is.Not.Null, spec.PrefabPath);
            Assert.That(enemy.IsBoss, Is.EqualTo(spec.Boss));
            Assert.That(enemy.BaseMaxHealth, Is.EqualTo(spec.Health));
            Assert.That(enemy.XpValue, Is.EqualTo(spec.Xp));
            Assert.That(enemy.AnimRefSpeed, Is.GreaterThan(0.05f), "walk clip ground speed not measured");
            float rate = spec.Speed / enemy.AnimRefSpeed;
            Assert.That(rate, Is.InRange(EnemyRosterBuilder.MinMoveRate, EnemyRosterBuilder.MaxMoveRate), "walk clip rate (feet slide)");

            var body = enemy.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Assert.That(body, Is.Not.Null);
            Assert.That(body.sharedMesh, Is.Not.Null);
            Assert.That(body.sharedMesh.lodCount, Is.GreaterThan(1), "Mesh LODs");
            var mat = body.sharedMaterial;
            Assert.That(AssetDatabase.GetAssetPath(mat), Is.EqualTo(spec.MaterialPath));
            Assert.That(mat.GetTexture("_BaseMap"), Is.Not.Null, "base colour");
            Assert.That(mat.GetTexture("_BumpMap"), Is.Not.Null, "normal map");
            Assert.That(mat.IsKeywordEnabled("_NORMALMAP"), Is.True);
            Assert.That(body.shadowCastingMode, Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.Off));

            var animator = enemy.Animator;
            Assert.That(animator, Is.Not.Null);
            Assert.That(animator.applyRootMotion, Is.False);
            Assert.That(animator.cullingMode, Is.EqualTo(AnimatorCullingMode.CullUpdateTransforms));
            Assert.That(body.transform.IsChildOf(animator.transform), Is.True, "the Animator culls by the renderers below it");
            Assert.That(animator.transform.Find("Hips"), Is.Not.Null, "clips bind to paths that start at Hips");

            var controller = animator.runtimeAnimatorController as AnimatorController;
            Assert.That(AssetDatabase.GetAssetPath(controller), Is.EqualTo(spec.ControllerPath));
            Assert.That(controller.layers[0].stateMachine.defaultState.name, Is.EqualTo("Move"));
            foreach (var name in spec.Boss ? new[] { "Move", "Attack", "Death" } : new[] { "Move", "Attack", "Hit", "Death" })
            {
                var state = State(controller, name);
                Assert.That(state, Is.Not.Null, name);
                Assert.That(state.motion as AnimationClip, Is.Not.Null, name);
            }
            var move = (AnimationClip)State(controller, "Move").motion;
            var death = (AnimationClip)State(controller, "Death").motion;
            Assert.That(move.isLooping, Is.True, "Move loops");
            Assert.That(death.isLooping, Is.False, "Death plays once");
            float deathTime = death.length / State(controller, "Death").speed;
            Assert.That(deathTime, Is.EqualTo(spec.Boss ? EnemyRosterBuilder.BossDeathPlayTime : EnemyRosterBuilder.DeathPlayTime).Within(0.01f));
            if (!spec.Boss)
                Assert.That(((AnimationClip)State(controller, "Hit").motion).length / State(controller, "Hit").speed,
                    Is.EqualTo(EnemyRosterBuilder.HitPlayTime).Within(0.01f));
        }

        [Test]
        public void Prefab_SizeMatchesTheRosterDoc([ValueSource(nameof(Ids))] string id)
        {
            var enemy = EnemyRosterBuilder.LoadPrefab(id);
            float doc = DocHeights.First(d => d.id == id).height;
            Assert.That(enemy.Height, Is.EqualTo(doc).Within(doc * 0.05f), "height");
            Assert.That(enemy.HitRadius, Is.InRange(enemy.Height * 0.25f - 0.001f, enemy.Height * 0.4f), "hit radius");
            Assert.That(enemy.ContactRange, Is.GreaterThan(enemy.HitRadius));
        }

        [Test]
        public void Bosses_Are2_3To2_9TimesTheKnight()
        {
            foreach (var spec in EnemyRosterBuilder.Roster.Where(s => s.Boss))
                Assert.That(EnemyRosterBuilder.LoadPrefab(spec.Id).Height / EnemyRosterBuilder.KnightHeight,
                    Is.InRange(2.25f, 2.9f), spec.Id);
        }

        [Test]
        public void MoveClips_RunInPlace([ValueSource(nameof(Ids))] string id)
        {
            var enemy = EnemyRosterBuilder.LoadPrefab(id);
            var controller = (AnimatorController)enemy.Animator.runtimeAnimatorController;
            var move = (AnimationClip)State(controller, "Move").motion;
            var instance = Object.Instantiate(enemy);
            try
            {
                var rig = instance.Animator.gameObject;
                var hips = rig.transform.Find("Hips");
                move.SampleAnimation(rig, 0f);
                Vector3 start = hips.position;
                move.SampleAnimation(rig, move.length);
                Vector3 drift = hips.position - start;
                drift.y = 0f;
                Assert.That(drift.magnitude, Is.LessThan(0.1f * enemy.Height), $"{move.name} hips drift {drift}");
            }
            finally
            {
                Object.DestroyImmediate(instance.gameObject);
            }
        }

        [Test]
        public void LongActionClips_AreCutToTheirStrike()
        {
            int cut = 0;
            foreach (var spec in EnemyRosterBuilder.Roster)
            foreach (var path in AssetDatabase.FindAssets("t:Model", new[] { spec.ArtDir }).Select(AssetDatabase.GUIDToAssetPath)
                         .Where(p => System.IO.Path.GetFileName(p).StartsWith($"A_{spec.Id}_")))
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(path);
                var take = importer.defaultClipAnimations[0];
                var clip = importer.clipAnimations.Single();
                float fps = AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>().First(c => !c.name.StartsWith("__preview__")).frameRate;
                float full = (take.lastFrame - take.firstFrame) / fps, kept = (clip.lastFrame - clip.firstFrame) / fps;
                string name = clip.name;
                bool action = name != "Hit" && name != "Death" && !EnemyRosterBuilder.LoopClips.Contains(name);
                Assert.That(clip.loopTime, Is.EqualTo(name != "Hit" && name != "Death"), path);
                if (!action || full <= EnemyRosterBuilder.TrimAbove) continue;
                Assert.That(kept, Is.EqualTo(EnemyRosterBuilder.StrikeBefore + EnemyRosterBuilder.StrikeAfter).Within(0.1f), path);
                Assert.That(clip.firstFrame, Is.GreaterThanOrEqualTo(take.firstFrame), path);
                Assert.That(clip.lastFrame, Is.LessThanOrEqualTo(take.lastFrame), path);
                cut++;
            }
            Assert.That(cut, Is.GreaterThanOrEqualTo(8), "Chop, Leap, Throw, Shoot ... were not cut");
        }

        [Test]
        public void ShippedWaves_SpawnTheFourMeleeTiers_NotTheOldShambler()
        {
            var table = AssetDatabase.LoadAssetAtPath<EnemyWaveTable>(WavesPath);
            var shambler = AssetDatabase.LoadAssetAtPath<Enemy>(ShamblerPath);
            var all = Enumerable.Range(0, table.PhaseCount).SelectMany(i => table.GetPhase(i).enemies).ToArray();
            foreach (var id in EnemyRosterBuilder.MeleeTiers)
                Assert.That(all.Any(e => e.prefab == EnemyRosterBuilder.LoadPrefab(id) && e.weight > 0f), Is.True, id);
            Assert.That(all.Any(e => e.prefab == shambler), Is.False, "old Shambler still in the waves");
            Assert.That(table.GetPhase(0).enemies.Select(e => e.prefab), Has.Member(EnemyRosterBuilder.LoadPrefab("GraveyardLaborer")));
        }

        [Test]
        public void AddMeleeTiers_SplitsShamblerWeight_KeepsOthers_AndRunsOnce()
        {
            var shambler = AssetDatabase.LoadAssetAtPath<Enemy>(ShamblerPath);
            var runner = AssetDatabase.LoadAssetAtPath<Enemy>("Assets/_Project/Prefabs/Combat/Enemy_ZombieRunner.prefab");
            var table = ScriptableObject.CreateInstance<EnemyWaveTable>();
            try
            {
                table.Configure(1f, 4, new[]
                {
                    new EnemyWaveTable.Phase { startTime = 0f, maxAlive = 10, spawnInterval = 1f, enemies = new[] { new EnemyWaveTable.EnemyEntry { prefab = shambler, weight = 1f } } },
                    new EnemyWaveTable.Phase { startTime = 30f, maxAlive = 10, spawnInterval = 1f, enemies = new[]
                    {
                        new EnemyWaveTable.EnemyEntry { prefab = shambler, weight = 0.8f },
                        new EnemyWaveTable.EnemyEntry { prefab = runner, weight = 0.2f },
                    } },
                });
                Assert.That(EnemyRosterBuilder.AddMeleeTiers(table, shambler), Is.True);
                for (int i = 0; i < table.PhaseCount; i++)
                {
                    var entries = table.GetPhase(i).enemies;
                    Assert.That(entries.Any(e => e.prefab == shambler), Is.False);
                    Assert.That(entries.Sum(e => e.weight), Is.EqualTo(1f).Within(0.01f), $"phase {i} total weight");
                }
                Assert.That(table.GetPhase(0).enemies.Length, Is.EqualTo(2), "phase 0: Laborer + Militiaman only");
                Assert.That(table.GetPhase(1).enemies.Single(e => e.prefab == runner).weight, Is.EqualTo(0.2f));
                Assert.That(table.GetPhase(1).enemies.Length, Is.EqualTo(5), "4 tiers + Runner");

                // A designer edit after the move (the Watchman removed) is kept: the move never runs again.
                var p1 = table.GetPhase(1);
                p1.enemies = p1.enemies.Where(e => e.prefab != EnemyRosterBuilder.LoadPrefab("RustedWatchman")).ToArray();
                table.Configure(table.FirstSpawnDelay, table.PrewarmPerType, new[] { table.GetPhase(0), p1 });
                Assert.That(EnemyRosterBuilder.AddMeleeTiers(table, shambler), Is.False);
                Assert.That(table.GetPhase(1).enemies.Length, Is.EqualTo(4));
            }
            finally
            {
                Object.DestroyImmediate(table);
            }
        }
    }
}
