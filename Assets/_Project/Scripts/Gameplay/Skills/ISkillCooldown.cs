namespace SurvivorSouls.Gameplay.Skills
{
    /// <summary>
    /// A skill effect that recharges (KAN-192). The HUD skill row (SkillHud) reads it every frame from
    /// <see cref="SkillManager.Owned.Effect"/>: the cooldown shade on the slot and the gold "active" ring. Read-only
    /// getters, no events: polling a few floats costs less than wiring, and nothing allocates.
    /// </summary>
    public interface ISkillCooldown
    {
        /// <summary>Part of the cooldown still to run: 1 = just started, 0 = ready or active.</summary>
        float CooldownRemaining01 { get; }

        /// <summary>True while the skill's effect is up (e.g. the Hallowed Shield ward).</summary>
        bool CooldownEffectActive { get; }
    }
}
