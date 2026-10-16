using UnityEngine;

namespace SurvivorSouls.Gameplay.World
{
    /// <summary>
    /// The stone walls and box colliders around the Chamber room (KAN-232, built by the editor WorldBuilder on the
    /// <see cref="WorldStreamer.PlayableMin"/> / <see cref="WorldStreamer.PlayableMax"/> edges). The boss arena (KAN-59)
    /// stands on the same ground and has its own ring, so the room hides itself in a boss Chamber: at
    /// <c>Start</c> when <see cref="BossArena.Active"/> is set (ChamberSetup enters the arena before any Start), or when an
    /// arena enters later (debug jump). No Update.
    /// </summary>
    public sealed class RoomWalls : MonoBehaviour
    {
        private void OnEnable() => BossArena.Entered += OnArenaEntered;

        private void OnDisable() => BossArena.Entered -= OnArenaEntered;

        private void Start()
        {
            if (BossArena.Active != null) gameObject.SetActive(false);
        }

        private void OnArenaEntered(BossArena arena) => gameObject.SetActive(false);
    }
}
