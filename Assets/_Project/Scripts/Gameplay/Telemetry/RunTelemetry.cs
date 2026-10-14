using System;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chests;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;

namespace SurvivorSouls.Gameplay.Telemetry
{
    /// <summary>
    /// Run telemetry (KAN-75, part of KAN-50): turns skill and chest events of the Game scene into
    /// <see cref="GameTelemetry"/> events. SkillUpgraded = every level change (<see cref="SkillManager.SkillChanged"/>:
    /// level-up pick, chest upgrade, debug; not the silent Chamber carry). ChestDropped / ChestOpened / ChestExpired from
    /// <see cref="ChestDropper"/> (source "reward" for a boss reward chest that never fades, else "skill"). Listening only:
    /// no gameplay changes, no allocation per event.
    /// </summary>
    public sealed class RunTelemetry : MonoBehaviour
    {
        private static readonly string[] SkillSources = { "pick", "upgrade", "debug" };
        private static readonly string[] ChestTiers = { "Light", "Rare" };

        [SerializeField] private SkillManager skills;
        [SerializeField] private ChestDropper chests;

        private Action<SkillDefinition, int, int> _onSkill;
        private Action<ReliquaryChest> _onDropped, _onOpened, _onExpired;

        /// <summary>Wiring for code-built objects and tests.</summary>
        public void Setup(SkillManager skillManager, ChestDropper dropper)
        {
            bool wasEnabled = isActiveAndEnabled;
            if (wasEnabled) OnDisable();
            skills = skillManager;
            chests = dropper;
            if (wasEnabled) OnEnable();
        }

        private void OnEnable()
        {
            _onSkill ??= OnSkillChanged;
            _onDropped ??= c => TrackChest(GameTelemetry.ChestDropped, c);
            _onOpened ??= c => TrackChest(GameTelemetry.ChestOpened, c);
            _onExpired ??= c => TrackChest(GameTelemetry.ChestExpired, c);
            if (skills != null) skills.SkillChanged += _onSkill;
            if (chests != null)
            {
                chests.Dropped += _onDropped;
                chests.Opened += _onOpened;
                chests.Expired += _onExpired;
            }
        }

        private void OnDisable()
        {
            if (skills != null) skills.SkillChanged -= _onSkill;
            if (chests != null)
            {
                chests.Dropped -= _onDropped;
                chests.Opened -= _onOpened;
                chests.Expired -= _onExpired;
            }
        }

        private void OnSkillChanged(SkillDefinition skill, int oldLevel, int newLevel)
        {
            var player = skills.Stats != null ? skills.Stats.transform.position : Vector3.zero;
            GameTelemetry.Track(new TelemetryEvent(GameTelemetry.SkillUpgraded, skill != null ? skill.Id : null, oldLevel,
                newLevel, SkillSources[(int)skills.LastChangeSource], Time.timeSinceLevelLoad, player));
        }

        private static void TrackChest(string name, ReliquaryChest chest)
        {
            string source = float.IsPositiveInfinity(chest.Lifetime) ? "reward" : "skill";
            GameTelemetry.Track(new TelemetryEvent(name, ChestTiers[(int)chest.Tier], chest.Choices, chest.Upgrades, source,
                Time.timeSinceLevelLoad, chest.transform.position));
        }
    }
}
