using UnityEngine;

namespace SurvivorSouls.Core
{
    /// <summary>
    /// One non-negative int in PlayerPrefs with a checksum key next to it (KAN-96 Souls, KAN-97 Ember Shards). Each value
    /// has its own salt, so a value + checksum pair copied from one key to another fails its check. A value that does not
    /// match its checksum (hand-edited prefs) or is negative reads as <see cref="Tampered"/>. Basic anti-tamper only; real
    /// protection needs a server (KAN-89).
    /// </summary>
    public readonly struct CheckedPrefInt
    {
        public readonly string Key;
        public readonly string CheckKey;
        private readonly uint _salt;

        public CheckedPrefInt(string key, uint salt)
        {
            Key = key;
            CheckKey = key + ".check";
            _salt = salt;
        }

        public bool Exists => PlayerPrefs.HasKey(Key);

        /// <summary>
        /// Saved value, or <paramref name="fallback"/> when nothing is saved. <paramref name="tampered"/> is true when a
        /// saved value fails its checksum or is negative (the caller decides what to write back).
        /// </summary>
        public int Read(int fallback, out bool tampered)
        {
            tampered = false;
            if (!PlayerPrefs.HasKey(Key)) return fallback;
            int value = PlayerPrefs.GetInt(Key, 0);
            if (value >= 0 && PlayerPrefs.GetInt(CheckKey, 0) == Checksum(value, _salt)) return value;
            tampered = true;
            return fallback;
        }

        /// <summary>Saves the value and its checksum (PlayerPrefs.Save: writes are rare).</summary>
        public void Write(int value)
        {
            PlayerPrefs.SetInt(Key, value);
            PlayerPrefs.SetInt(CheckKey, Checksum(value, _salt));
            PlayerPrefs.Save();
        }

        /// <summary>
        /// FNV-1a over the value bytes xor a salt. Stable across runs and platforms (no <c>GetHashCode</c>). The Souls salt
        /// 0x5EED50F1 keeps the KAN-96 saves valid.
        /// </summary>
        public static int Checksum(int value, uint salt)
        {
            unchecked
            {
                uint hash = 2166136261u;
                uint v = (uint)value ^ salt;
                for (int i = 0; i < 4; i++)
                {
                    hash ^= (v >> (i * 8)) & 0xFFu;
                    hash *= 16777619u;
                }
                return (int)hash;
            }
        }
    }
}
