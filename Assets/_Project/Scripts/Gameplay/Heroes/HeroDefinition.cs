using System;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Heroes
{
    /// <summary>What a hero costs. Stored as an int in the asset: append new values only at the end.</summary>
    public enum HeroCurrency
    {
        /// <summary>Owned from the start (the Knight).</summary>
        Free = 0,
        EmberShards = 1,
        Souls = 2,
    }

    /// <summary>
    /// One playable hero (KAN-153, first slice of the KAN-100 roster): name, card text, price and stat profile. The profile
    /// (<see cref="Modifiers"/>) is written to the player's <see cref="PlayerStats"/> as one permanent source at run start
    /// (<see cref="HeroLoadout"/>), like the soul tree (KAN-98), so the KAN-92 horde scaling counts it in its baseline and
    /// never scales the horde up because of it. Percent modifiers change the Knight's base values (the Knight is the
    /// reference hero: empty profile). <see cref="ModelPrefab"/> is the hero's "Model" (skinned body + Animator); null =
    /// the scene's Knight model. <see cref="Projectile"/> / <see cref="ShotClips"/> (KAN-227) replace the Knight bolt and
    /// crossbow shot sound; empty = the scene defaults. Data only: ownership and the selection are saved by <see cref="HeroProgress"/>.
    /// Asset: <c>Data/Heroes/Hero_&lt;Id&gt;.asset</c> (HeroBuilder, made once, never overwritten).
    /// </summary>
    public sealed class HeroDefinition : ScriptableObject
    {
        [Tooltip("Save key part. Never change it after release: ownership and the selection are stored under it.")]
        [SerializeField] private string id = "hero";
        [SerializeField] private string displayName = "Hero";
        [Tooltip("One short line under the name on the hero card.")]
        [SerializeField] private string tagline = "";
        [SerializeField] private Texture2D portrait;
        [SerializeField] private Color accent = new Color(0.91f, 0.77f, 0.47f);
        [SerializeField] private HeroCurrency currency = HeroCurrency.Free;
        [SerializeField, Min(0)] private int price;
        [Tooltip("Permanent stat profile against the Knight (Percent 0.2 = +20 %). Applied once per run.")]
        [SerializeField] private StatModifier[] modifiers = Array.Empty<StatModifier>();
        [Tooltip("Short card lines, strengths. Max ~4 words each.")]
        [SerializeField] private string[] pros = Array.Empty<string>();
        [Tooltip("Short card lines, weaknesses. Max ~4 words each.")]
        [SerializeField] private string[] cons = Array.Empty<string>();
        [Tooltip("The hero's 'Model' root (skinned body + Animator). Null = the Knight model already in the Game scene.")]
        [SerializeField] private GameObject modelPrefab;
        [Tooltip("The hero's own projectile (KAN-227: the Ranger's arrow). Null = the scene's Knight bolt.")]
        [SerializeField] private Bolt projectile;
        [Tooltip("The hero's own shot sounds (KAN-227: the Ranger's bow twang). Empty = the scene's crossbow shots.")]
        [SerializeField] private AudioClip[] shotClips = Array.Empty<AudioClip>();
        [Tooltip("Aim turn max rate (deg/s) toward a new target. 0 = the scene's shooter value (Knight 720, constant rate).")]
        [SerializeField, Min(0f)] private float aimTurnSpeed;
        [Tooltip("Aim turn smoothing (s): > 0 eases in and out (smooth damp) instead of a constant-rate turn. Used when aimTurnSpeed > 0.")]
        [SerializeField, Min(0f)] private float aimTurnSmoothing;

        public string Id => id;
        public string DisplayName => displayName;
        public string Tagline => tagline;
        public Texture2D Portrait => portrait;
        public Color Accent => accent;
        public HeroCurrency Currency => currency;
        /// <summary>Price in <see cref="Currency"/>; 0 when the hero is free.</summary>
        public int Price => currency == HeroCurrency.Free ? 0 : Mathf.Max(0, price);
        public bool IsFree => Price <= 0;
        public StatModifier[] Modifiers => modifiers;
        public string[] Pros => pros;
        public string[] Cons => cons;
        public GameObject ModelPrefab => modelPrefab;
        /// <summary>Own projectile prefab, or null for the scene default (the Knight bolt).</summary>
        public Bolt Projectile => projectile;
        /// <summary>Own shot clips; empty for the scene default (the crossbow shots).</summary>
        public AudioClip[] ShotClips => shotClips ?? Array.Empty<AudioClip>();
        /// <summary>Own aim turn (KAN-229): max deg/s, 0 = scene default.</summary>
        public float AimTurnSpeed => aimTurnSpeed;
        /// <summary>Own aim turn smoothing (s), used with <see cref="AimTurnSpeed"/>.</summary>
        public float AimTurnSmoothing => aimTurnSmoothing;

        /// <summary>Final value of <paramref name="stat"/> for a base value, with this profile only (card numbers, tests).</summary>
        public float Apply(StatId stat, float baseValue)
        {
            float flat = 0f, percent = 0f;
            foreach (var m in modifiers)
            {
                if (m.stat != stat) continue;
                if (m.kind == ModifierKind.Flat) flat += m.value;
                else percent += m.value;
            }
            return (baseValue + flat) * Mathf.Max(0f, 1f + percent);
        }

        /// <summary>Builder and tests.</summary>
        public static HeroDefinition Create(string heroId, string name, HeroCurrency cost, int amount, params StatModifier[] profile)
        {
            var hero = CreateInstance<HeroDefinition>();
            hero.name = "Hero_" + heroId;
            hero.Configure(heroId, name, cost, amount, profile);
            return hero;
        }

        public void Configure(string heroId, string name, HeroCurrency cost, int amount, StatModifier[] profile)
        {
            id = heroId;
            displayName = name;
            currency = cost;
            price = amount;
            modifiers = profile ?? Array.Empty<StatModifier>();
        }

        public void Describe(string line, string[] strengths, string[] weaknesses, Color color, Texture2D picture)
        {
            tagline = line;
            pros = strengths ?? Array.Empty<string>();
            cons = weaknesses ?? Array.Empty<string>();
            accent = color;
            portrait = picture;
        }

        public void SetModel(GameObject prefab) => modelPrefab = prefab;

        /// <summary>Own projectile + shot sounds (null / empty = the scene defaults). Builder and tests.</summary>
        /// <summary>Own aim turn (0 speed = scene default). Builder and tests.</summary>
        public void SetAimTurn(float degreesPerSecond, float smoothing)
        {
            aimTurnSpeed = Mathf.Max(0f, degreesPerSecond);
            aimTurnSmoothing = Mathf.Max(0f, smoothing);
        }

        public void SetShot(Bolt prefab, AudioClip[] clips)
        {
            projectile = prefab;
            shotClips = clips ?? Array.Empty<AudioClip>();
        }
    }
}
