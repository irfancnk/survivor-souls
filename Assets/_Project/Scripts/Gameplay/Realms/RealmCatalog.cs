using System.Collections.Generic;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Realms
{
    /// <summary>
    /// All Realms in play order (KAN-20). The Realm selection page lists them top to bottom; the lock rule in
    /// <see cref="Chamber.RunProgress"/> uses their ids (1, 2, 3, ...). One asset: Data/Realms/RealmCatalog.asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Survivor Souls/Realm Catalog", fileName = "RealmCatalog")]
    public sealed class RealmCatalog : ScriptableObject
    {
        [SerializeField] private List<RealmDefinition> realms = new List<RealmDefinition>();

        public IReadOnlyList<RealmDefinition> Realms => realms;
        public int Count => realms.Count;

        /// <summary>The Realm with <paramref name="id"/>, or null.</summary>
        public RealmDefinition Get(int id)
        {
            for (int i = 0; i < realms.Count; i++)
                if (realms[i] != null && realms[i].Id == id) return realms[i];
            return null;
        }

        /// <summary>Tests and the builder: sets the list (order = play order).</summary>
        public void Configure(IEnumerable<RealmDefinition> list)
        {
            realms.Clear();
            realms.AddRange(list);
        }

        /// <summary>Tests and tools: a catalog without an asset.</summary>
        public static RealmCatalog Create(params RealmDefinition[] list)
        {
            var catalog = CreateInstance<RealmCatalog>();
            catalog.Configure(list);
            return catalog;
        }
    }
}
