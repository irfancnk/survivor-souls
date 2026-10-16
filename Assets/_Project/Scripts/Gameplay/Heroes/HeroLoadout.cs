using System;
using SurvivorSouls.Audio;
using SurvivorSouls.Dev;
using SurvivorSouls.Gameplay.Characters;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Player;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Heroes
{
    /// <summary>
    /// Puts the picked hero (KAN-153, <see cref="HeroProgress.Current"/>) on the player when the Game scene wakes, before
    /// every other player script (execution order -450, the soul tree is -400):
    /// <list type="bullet">
    /// <item>Stat profile: the hero's modifiers as one permanent <see cref="PlayerStats"/> source (counted in the KAN-92
    /// horde baseline like the soul tree, so the horde does not scale up for it).</item>
    /// <item>Model: a hero with its own <see cref="HeroDefinition.ModelPrefab"/> replaces the scene's Knight "Model" (the
    /// Knight model is turned off and renamed). Health flash, death, locomotion driver and the shooter muzzle move to the
    /// new model; the crossbow-only parts (aim, stance, shoot cycle, recoil) are turned off.</item>
    /// <item>Shot (KAN-227): the hero's own projectile on the <see cref="AutoShooter"/> and own shot clips on
    /// <see cref="GameAudio"/>; a hero without them (the Knight) gets the scene defaults back.</item>
    /// </list>
    /// Dev / editor builds: <c>-ss-hero &lt;id&gt;</c> (<see cref="DevArgs"/>) plays that hero without owning it.
    /// On the player (GameSceneBuilder via HeroBuilder).
    /// </summary>
    [DefaultExecutionOrder(-450)]
    public sealed class HeroLoadout : MonoBehaviour
    {
        public const string DevArg = "-ss-hero";
        public const string ModelName = "Model";
        public const string KnightModelName = "Model_Knight";
        /// <summary>Socket on a hero model the bolts leave from (the bow hand).</summary>
        public const string MuzzleName = "Bow_Muzzle";

        [SerializeField] private HeroRoster roster;
        [SerializeField] private PlayerStats stats;
        [Tooltip("Game sound: takes the hero's shot clips (KAN-227). Empty = found in the scene.")]
        [SerializeField] private GameAudio sound;

        private Bolt _sceneBolt; // the scene's projectile (Knight bolt), restored for heroes without their own

        /// <summary>The hero applied by the last <see cref="Apply"/> (null without a roster).</summary>
        public HeroDefinition Hero { get; private set; }
        /// <summary>The model the player shows now (the Knight's or the hero's own).</summary>
        public Transform Model { get; private set; }
        public HeroRoster Roster => roster;

        /// <summary>Raised after a hero was applied (HUD, tests).</summary>
        public static event Action<HeroDefinition> Applied;

        /// <summary>Tests and code-built scenes: wiring, then apply <paramref name="hero"/> (null = the saved pick).</summary>
        public void Setup(HeroRoster heroes, PlayerStats playerStats, HeroDefinition hero = null)
        {
            roster = heroes;
            stats = playerStats;
            Apply(hero ?? Pick());
        }

        private void Awake()
        {
            if (stats == null) stats = GetComponent<PlayerStats>();
            if (roster != null) Apply(Pick());
        }

        /// <summary>The hero this run starts with: the dev arg (dev / editor builds), else the saved pick.</summary>
        public HeroDefinition Pick()
        {
            if (roster == null) return null;
            if (Debug.isDebugBuild || Application.isEditor)
            {
                string id = ArgValue(DevArgs.All, DevArg);
                var forced = roster.Find(id);
                if (forced != null) return forced;
                if (!string.IsNullOrEmpty(id)) Debug.LogWarning($"[HeroLoadout] {DevArg} '{id}': no such hero");
            }
            return HeroProgress.Current(roster);
        }

        /// <summary>Writes <paramref name="hero"/>'s profile to the stats and shows its model. Safe to call again.</summary>
        public void Apply(HeroDefinition hero)
        {
            if (Hero != null && Hero != hero && stats != null) stats.RemoveSource(Hero);
            Hero = hero;
            if (hero == null) return;
            if (stats != null) stats.SetSource(hero, hero.Modifiers, 1, permanent: true);
            if (hero.ModelPrefab != null) SwapModel(hero.ModelPrefab);
            else Model = transform.Find(ModelName);
            ApplyShot(hero);
            Debug.Log($"[HeroLoadout] Hero '{hero.Id}' ({hero.Modifiers.Length} stat changes, model {(hero.ModelPrefab != null ? hero.ModelPrefab.name : "Knight")})");
            Applied?.Invoke(hero);
        }

        /// <summary>The hero's projectile and shot sounds, or the scene defaults (KAN-227).</summary>
        private void ApplyShot(HeroDefinition hero)
        {
            var shooter = GetComponent<AutoShooter>();
            if (shooter != null)
            {
                if (_sceneBolt == null) _sceneBolt = shooter.BoltPrefab;
                shooter.SetBoltPrefab(hero.Projectile != null ? hero.Projectile : _sceneBolt);
            }
            if (sound == null) sound = FindFirstObjectByType<GameAudio>();
            if (sound != null) sound.SetPlayerShots(hero.ShotClips.Length > 0 ? hero.ShotClips : null);
        }

        private void SwapModel(GameObject prefab)
        {
            var knight = transform.Find(ModelName);
            if (knight != null && knight.name == ModelName && knight.GetComponent<BowShootDriver>() != null) Destroy(knight.gameObject); // re-apply
            else if (knight != null)
            {
                knight.name = KnightModelName;
                knight.gameObject.SetActive(false);
            }

            var model = Instantiate(prefab, transform, false).transform;
            model.name = ModelName;
            Model = model;
            var animator = model.GetComponentInChildren<Animator>();

            foreach (var driver in GetComponents<LocomotionAnimatorDriver>()) driver.SetAnimator(animator);
            var flash = GetComponent<PlayerHitFlash>();
            if (flash != null) flash.SetRenderers(model.GetComponentsInChildren<Renderer>(true));
            var death = GetComponent<PlayerDeath>();
            if (death != null) death.SetModel(model, animator, DeathClipSeconds(animator));
            var shooter = GetComponent<AutoShooter>();
            var muzzle = FindDeep(model, MuzzleName);
            if (shooter != null && muzzle != null) shooter.SetMuzzle(muzzle);

            // Crossbow-only parts of the Knight (KAN-215 .. KAN-217): they drive his rig and clips.
            Disable(GetComponent<CrossbowAim>());
            Disable(GetComponent<CrossbowStance>());
            Disable(GetComponent<CrossbowCycle>());
            Disable(GetComponent<CrossbowRecoil>());
        }

        private static float DeathClipSeconds(Animator animator)
        {
            if (animator == null || animator.runtimeAnimatorController == null) return 0f;
            foreach (var p in animator.parameters)
                if (p.nameHash == PlayerDeath.DieId)
                {
                    foreach (var clip in animator.runtimeAnimatorController.animationClips)
                        if (clip != null && clip.name == "Death") return clip.length;
                    return 1.6f;
                }
            return 0f;
        }

        private static void Disable(Behaviour b)
        {
            if (b != null) b.enabled = false;
        }

        private static Transform FindDeep(Transform root, string name)
        {
            if (root.name == name) return root;
            for (int i = 0; i < root.childCount; i++)
            {
                var hit = FindDeep(root.GetChild(i), name);
                if (hit != null) return hit;
            }
            return null;
        }

        /// <summary>Value after <paramref name="key"/> in <paramref name="args"/>, or null. Public for tests.</summary>
        public static string ArgValue(string[] args, string key)
        {
            if (args == null) return null;
            int i = Array.IndexOf(args, key);
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
    }
}
