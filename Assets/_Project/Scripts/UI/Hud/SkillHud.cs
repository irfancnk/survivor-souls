using System;
using System.Collections.Generic;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// Owned skills row under the health bar (KAN-19): one small rune icon per skill in pick order, rarity-coloured and -shaped rim (KAN-37),
    /// and a row of level pips under each icon, one per level up to the skill's max (KAN-74; lit = owned, glow colour at max).
    /// Rebuilt only when <see cref="SkillManager.Changed"/> fires (slots and pips are pooled); the slot that was just picked
    /// pops once (unscaled time: the pick happens during the level-up pause).
    /// <para>
    /// Level up of an OWNED skill (level-up pick or chest upgrade, <see cref="SkillManager.SkillChanged"/> with old level
    /// above 0): the slot gets a tier-coloured glow ring, its icon pulses and the new pips flash. The glow waits until no
    /// pause hold is open (the card screen covers the row), then all waiting slots glow together with one
    /// <see cref="UpgradeShown"/> (GameAudio chime) and one light haptic. <see cref="SkillManager.Restore"/> (Chamber
    /// carry) and new skills do not glow.
    /// </para>
    /// Looks: hud-skill* in GameHud.uss.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class SkillHud : MonoBehaviour
    {
        private const string RowName = "skill-row";
        private const string SlotClass = "hud-skill";
        private const string MaxClass = "hud-skill--max";
        private const string IconClass = "hud-skill__icon";
        private const string GlowClass = "hud-skill__glow";
        private const string PipRowClass = "hud-skill__pips";
        private const string PipClass = "hud-skill__pip";
        private const string PipOnClass = "hud-skill__pip--on";
        private const string PipNewClass = "hud-skill__pip--new";
        private static readonly string[] RarityClasses = { "hud-skill--common", "hud-skill--rare", "hud-skill--epic", "hud-skill--legendary" };

        [SerializeField] private SkillManager skills;
        [SerializeField, Min(0.05f)] private float popTime = 0.45f;
        [Tooltip("Seconds (unscaled) of the level-up glow: ring flare, icon pulse, new pips flashing.")]
        [SerializeField, Min(0.1f)] private float glowTime = 1.1f;
        [Tooltip("One light haptic tap when the glow starts (KAN-12 rules: vibration setting, rate limit).")]
        [SerializeField] private bool haptic = true;

        /// <summary>Level-up glow started after the pause ended: number of slots glowing (one per call, never 0).</summary>
        public event Action<int> UpgradeShown;

        private sealed class Slot
        {
            public VisualElement Root, Icon, Glow, PipRow;
            public readonly List<VisualElement> Pips = new List<VisualElement>(6);
            public SkillDefinition Skill;
            public float GlowAge = -1f;
            /// <summary>Pips [NewFrom, NewTo) flash during the glow (0-based).</summary>
            public int NewFrom, NewTo;
        }

        private struct PendingGlow
        {
            public SkillDefinition Skill;
            public int Old, New;
        }

        private readonly List<Slot> _slots = new List<Slot>();
        private readonly List<PendingGlow> _pending = new List<PendingGlow>(8);
        private VisualElement _row;
        private SkillDefinition _popSkill;
        private int _popSlot = -1;
        private float _popAge = -1f;

        public int VisibleSlots { get; private set; }
        /// <summary>Level-ups waiting for the pause to end.</summary>
        public int PendingGlows => _pending.Count;
        public float GlowTime => glowTime;

        /// <summary>True while slot <paramref name="index"/> plays its level-up glow.</summary>
        public bool IsGlowing(int index) => index >= 0 && index < _slots.Count && _slots[index].GlowAge >= 0f;

        /// <summary>Slot root element of owned skill <paramref name="index"/> (pick order), null when not shown. Tests, screenshots.</summary>
        public VisualElement SlotElement(int index) => index >= 0 && index < VisibleSlots && index < _slots.Count ? _slots[index].Root : null;

        public void Setup(SkillManager manager)
        {
            bool wasEnabled = isActiveAndEnabled;
            if (wasEnabled) OnDisable();
            skills = manager;
            if (wasEnabled) OnEnable();
        }

        private void OnEnable()
        {
            _row = GetComponent<UIDocument>().rootVisualElement?.Q(RowName);
            if (skills == null) return;
            skills.Changed += Rebuild;
            skills.Granted += OnGranted;
            skills.SkillChanged += OnSkillChanged;
            Rebuild();
        }

        private void OnDisable()
        {
            if (skills == null) return;
            skills.Changed -= Rebuild;
            skills.Granted -= OnGranted;
            skills.SkillChanged -= OnSkillChanged;
        }

        private void OnGranted(SkillDefinition skill, int level) => _popSkill = skill; // Changed follows and starts the pop

        private void OnSkillChanged(SkillDefinition skill, int oldLevel, int newLevel)
        {
            if (skill == null || oldLevel <= 0 || newLevel <= oldLevel) return; // new skill: the pick pop is enough
            for (int i = 0; i < _pending.Count; i++)
            {
                if (_pending[i].Skill != skill) continue;
                var p = _pending[i]; // same skill twice in one pause (queued chests): one glow over all new pips
                p.New = newLevel;
                _pending[i] = p;
                return;
            }
            _pending.Add(new PendingGlow { Skill = skill, Old = oldLevel, New = newLevel });
        }

        private void Rebuild()
        {
            var owned = skills.OwnedSkills;
            VisibleSlots = owned.Count;
            if (_row == null) return;
            while (_slots.Count < owned.Count) _slots.Add(NewSlot());
            _popSlot = -1;
            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                bool on = i < owned.Count;
                slot.Root.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                if (!on)
                {
                    slot.Skill = null;
                    EndGlow(slot);
                    continue;
                }
                var o = owned[i];
                if (slot.Skill != o.Skill) EndGlow(slot); // run reset: a glow never jumps to another skill
                slot.Skill = o.Skill;
                int r = (int)o.Skill.Rarity;
                for (int k = 0; k < RarityClasses.Length; k++) slot.Root.EnableInClassList(RarityClasses[k], k == r);
                slot.Root.EnableInClassList(MaxClass, o.Level >= o.Skill.MaxLevel);
                slot.Icon.style.backgroundImage = o.Skill.Icon != null ? new StyleBackground(o.Skill.Icon) : StyleKeyword.None;
                SetPips(slot, o.Skill.MaxLevel, o.Level);
                if (o.Skill == _popSkill) _popSlot = i;
            }
            if (owned.Count == 0) _pending.Clear();
            _popSkill = null;
            _popAge = _popSlot >= 0 ? 0f : -1f;
        }

        private void SetPips(Slot slot, int max, int level)
        {
            while (slot.Pips.Count < max)
            {
                var pip = new VisualElement { pickingMode = PickingMode.Ignore };
                pip.AddToClassList(PipClass);
                slot.PipRow.Add(pip);
                slot.Pips.Add(pip);
            }
            for (int i = 0; i < slot.Pips.Count; i++)
            {
                var pip = slot.Pips[i];
                pip.style.display = i < max ? DisplayStyle.Flex : DisplayStyle.None;
                pip.EnableInClassList(PipOnClass, i < level);
                pip.EnableInClassList(PipNewClass, slot.GlowAge >= 0f && i >= slot.NewFrom && i < slot.NewTo);
            }
        }

        private Slot NewSlot()
        {
            var slot = new Slot
            {
                Root = new VisualElement { pickingMode = PickingMode.Ignore },
                Glow = new VisualElement { pickingMode = PickingMode.Ignore },
                Icon = new VisualElement { pickingMode = PickingMode.Ignore },
                PipRow = new VisualElement { pickingMode = PickingMode.Ignore },
            };
            slot.Root.AddToClassList(SlotClass);
            slot.Glow.AddToClassList(GlowClass);
            slot.Icon.AddToClassList(IconClass);
            slot.PipRow.AddToClassList(PipRowClass);
            slot.Glow.style.opacity = 0f;
            slot.Root.Add(slot.Glow);
            slot.Root.Add(slot.Icon);
            slot.Root.Add(slot.PipRow);
            _row.Add(slot.Root);
            return slot;
        }

        private void Update() => Tick(Time.unscaledDeltaTime);

        /// <summary>Advances the pick pop and the level-up glows by <paramref name="dt"/> unscaled seconds (Update; tests call it directly).</summary>
        public void Tick(float dt)
        {
            if (_pending.Count > 0 && !GamePause.IsPaused) StartGlows();

            if (_popAge >= 0f && _popSlot >= 0 && _popSlot < _slots.Count)
            {
                _popAge += dt;
                float t = Mathf.Clamp01(_popAge / popTime);
                float s = 1f + 0.45f * Mathf.Sin(t * Mathf.PI) * (1f - t * 0.5f);
                _slots[_popSlot].Root.style.scale = new Scale(new Vector3(s, s, 1f));
                if (t >= 1f) _popAge = -1f;
            }

            for (int i = 0; i < _slots.Count; i++)
            {
                var slot = _slots[i];
                if (slot.GlowAge < 0f) continue;
                slot.GlowAge += dt;
                float t = Mathf.Clamp01(slot.GlowAge / glowTime);
                // Ring: flares in the first 15 %, holds, fades out; grows outwards a little while it fades.
                float flare = t < 0.15f ? t / 0.15f : 1f - Mathf.SmoothStep(0f, 1f, (t - 0.15f) / 0.85f);
                slot.Glow.style.opacity = flare;
                float ring = 1f + 0.22f * t;
                slot.Glow.style.scale = new Scale(new Vector3(ring, ring, 1f));
                // Icon: two quick pulses, the first one bigger.
                float pulse = 1f + 0.2f * Mathf.Abs(Mathf.Sin(t * Mathf.PI * 2f)) * (1f - t);
                slot.Icon.style.scale = new Scale(new Vector3(pulse, pulse, 1f));
                // New pips blink 3 times, then stay lit.
                bool blinkOn = t >= 1f || Mathf.Repeat(t * 3f, 1f) < 0.6f;
                for (int p = slot.NewFrom; p < slot.NewTo && p < slot.Pips.Count; p++)
                    slot.Pips[p].EnableInClassList(PipNewClass, blinkOn && t < 1f);
                if (t >= 1f) EndGlow(slot);
            }
        }

        private void StartGlows()
        {
            int started = 0;
            for (int i = 0; i < _pending.Count; i++)
            {
                var p = _pending[i];
                int index = SlotOf(p.Skill);
                if (index < 0) continue; // run reset while waiting
                var slot = _slots[index];
                slot.GlowAge = 0f;
                slot.NewFrom = Mathf.Max(0, p.Old);
                slot.NewTo = p.New;
                for (int k = 0; k < slot.Pips.Count; k++)
                    slot.Pips[k].EnableInClassList(PipNewClass, k >= slot.NewFrom && k < slot.NewTo);
                started++;
            }
            _pending.Clear();
            if (started == 0) return;
            if (haptic) Haptics.Light();
            UpgradeShown?.Invoke(started);
        }

        private int SlotOf(SkillDefinition skill)
        {
            for (int i = 0; i < VisibleSlots && i < _slots.Count; i++)
                if (_slots[i].Skill == skill) return i;
            return -1;
        }

        private static void EndGlow(Slot slot)
        {
            slot.GlowAge = -1f;
            slot.Glow.style.opacity = 0f;
            slot.Glow.style.scale = new Scale(Vector3.one);
            slot.Icon.style.scale = new Scale(Vector3.one);
            for (int p = 0; p < slot.Pips.Count; p++) slot.Pips[p].RemoveFromClassList(PipNewClass);
        }
    }
}
