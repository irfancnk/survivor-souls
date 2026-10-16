using System;
using System.Collections.Generic;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Heroes
{
    /// <summary>
    /// Every playable hero in card order (KAN-153, KAN-100 roster). The first free hero is the default: a new player and a
    /// lost selection start with it. Asset: <c>Data/Heroes/HeroRoster.asset</c> (HeroBuilder). Read by the main menu hero
    /// page (<c>HeroSelectScreen</c>) and the Game scene (<see cref="HeroLoadout"/>).
    /// </summary>
    public sealed class HeroRoster : ScriptableObject
    {
        [SerializeField] private HeroDefinition[] heroes = Array.Empty<HeroDefinition>();

        public IReadOnlyList<HeroDefinition> Heroes => heroes;

        /// <summary>First free hero, else the first hero; null when empty.</summary>
        public HeroDefinition Default
        {
            get
            {
                foreach (var h in heroes)
                    if (h != null && h.IsFree) return h;
                foreach (var h in heroes)
                    if (h != null) return h;
                return null;
            }
        }

        public HeroDefinition Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var h in heroes)
                if (h != null && h.Id == id) return h;
            return null;
        }

        public bool Contains(HeroDefinition hero) => hero != null && Array.IndexOf(heroes, hero) >= 0;

        /// <summary>Builder and tests.</summary>
        public static HeroRoster Create(params HeroDefinition[] list)
        {
            var roster = CreateInstance<HeroRoster>();
            roster.name = "HeroRoster";
            roster.SetHeroes(list);
            return roster;
        }

        public void SetHeroes(HeroDefinition[] list) => heroes = list ?? Array.Empty<HeroDefinition>();
    }
}
