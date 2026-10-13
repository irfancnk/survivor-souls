using SurvivorSouls.Gameplay.Chamber;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Realms
{
    /// <summary>
    /// One Realm (Archero chapter) as the Realm selection page shows it (KAN-20): id, name, texts, Estus cost, Chamber
    /// count and thumbnail. The Chamber count comes from the Realm's <see cref="ChamberDifficulty"/> when it has one, so
    /// the page and the game never disagree. <see cref="Playable"/> off = content not made yet ("coming soon"): the card
    /// shows, START stays off. Listed in order by <see cref="RealmCatalog"/>. Edit the assets; the builder never
    /// overwrites them.
    /// </summary>
    [CreateAssetMenu(menuName = "Survivor Souls/Realm", fileName = "Realm")]
    public sealed class RealmDefinition : ScriptableObject
    {
        [Tooltip("1, 2, 3, ... in play order. RunState.NewRun and RunProgress use it.")]
        [SerializeField, Min(1)] private int id = 1;
        [SerializeField] private string displayName = "Realm";
        [Tooltip("One short line under the name on the card.")]
        [SerializeField] private string tagline = "";
        [SerializeField, TextArea(2, 5)] private string description = "";
        [Tooltip("Enemy types, as shown in the detail panel (e.g. \"Drowned Hollows\").")]
        [SerializeField] private string enemies = "";
        [Tooltip("Rewards, as shown in the detail panel.")]
        [SerializeField] private string rewards = "";
        [Tooltip("Estus a run costs (GAME_PLAN: 5). Display only until Estus exists.")]
        [SerializeField, Min(0)] private int estusCost = 5;
        [Tooltip("Chamber rules of this Realm. Null = use the Chamber count below.")]
        [SerializeField] private ChamberDifficulty difficulty;
        [Tooltip("Chambers in this Realm when it has no difficulty asset yet.")]
        [SerializeField, Min(1)] private int chambers = 10;
        [SerializeField] private Texture2D thumbnail;
        [Tooltip("Accent colour of the card edge and progress bar.")]
        [SerializeField] private Color accent = new Color(0.91f, 0.77f, 0.47f);
        [Tooltip("Content exists. Off = \"coming soon\": the card shows, START stays off.")]
        [SerializeField] private bool playable = true;

        public int Id => id;
        public string DisplayName => displayName;
        public string Tagline => tagline;
        public string Description => description;
        public string Enemies => enemies;
        public string Rewards => rewards;
        public int EstusCost => estusCost;
        public ChamberDifficulty Difficulty => difficulty;
        public int Chambers => difficulty != null ? difficulty.ChambersInRealm : chambers;
        public Texture2D Thumbnail => thumbnail;
        public Color Accent => accent;
        public bool Playable => playable;

        /// <summary>Tests and tools: a Realm without an asset.</summary>
        public static RealmDefinition Create(int id, string name, int chambers = 10, bool playable = true,
            ChamberDifficulty difficulty = null)
        {
            var realm = CreateInstance<RealmDefinition>();
            realm.name = name;
            realm.id = Mathf.Max(1, id);
            realm.displayName = name;
            realm.chambers = Mathf.Max(1, chambers);
            realm.playable = playable;
            realm.difficulty = difficulty;
            return realm;
        }

        /// <summary>Builder: fills the texts of a new asset (only when it is made; later edits stay).</summary>
        public void Describe(string realmTagline, string realmDescription, string realmEnemies, string realmRewards,
            int estus, Color realmAccent, Texture2D realmThumbnail)
        {
            tagline = realmTagline;
            description = realmDescription;
            enemies = realmEnemies;
            rewards = realmRewards;
            estusCost = Mathf.Max(0, estus);
            accent = realmAccent;
            thumbnail = realmThumbnail;
        }
    }
}
