using UnityEngine;

namespace SurvivorSouls.Gameplay.Boss
{
    /// <summary>
    /// Bosses of one Realm's boss Chambers, in order (KAN-112). The n-th boss Chamber of the Realm (0-based, from the
    /// <see cref="Chamber.ChamberDifficulty"/> boss rule) fights <see cref="For"/>(n): the list repeats when there are more
    /// boss Chambers than bosses. Realm 1: The Butcher of Ashgrave (Chamber 5), The Rotting Colossus (Chamber 10).
    /// Read by <see cref="BossFight.BossFor"/>. Edit the asset; the builder never overwrites it.
    /// </summary>
    [CreateAssetMenu(menuName = "Survivor Souls/Boss Rotation", fileName = "BossRotation")]
    public sealed class BossRotation : ScriptableObject
    {
        [Tooltip("Boss of the 1st, 2nd, ... boss Chamber of the Realm. Repeats from the top when the Realm has more boss Chambers.")]
        [SerializeField] private BossData[] bosses = new BossData[0];

        public BossData[] Bosses => bosses;
        public int Count => bosses?.Length ?? 0;

        /// <summary>Boss of the <paramref name="ordinal"/>-th boss Chamber (0 = first). Null when the list is empty.</summary>
        public BossData For(int ordinal)
        {
            if (Count == 0) return null;
            var pick = bosses[((ordinal % Count) + Count) % Count];
            return pick != null ? pick : null;
        }

        /// <summary>Editor builder and tests: sets the list.</summary>
        public void Configure(BossData[] list) => bosses = list ?? new BossData[0];

        /// <summary>Tests and tools: a rotation without an asset.</summary>
        public static BossRotation Create(BossData[] list)
        {
            var r = CreateInstance<BossRotation>();
            r.Configure(list);
            return r;
        }
    }
}
