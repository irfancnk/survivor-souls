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
    /// <para>
    /// Medieval look (KAN-184): forged iron frame per tier (KAN-37 shape cue kept) with a rarity-tinted inlay, a small level
    /// shield with the level number (pips stay), the level-up glow adds a shine sweep and two sparks (shared KAN-35 bar art),
    /// and an optional cooldown shade (<see cref="SetCooldown"/>) for skills that recharge. Styles are written only on change.
    /// </para>
    /// <para>
    /// Glow layer (KAN-194): the glow rings do not live in their slots but in one underlay (first child of the row, absolute),
    /// each in an anchor that copies its slot's layout rect on <see cref="GeometryChangedEvent"/>. So every ring draws behind
    /// every frame, level shield and pip row (a ring reaches 13 px + growth past its slot, into the 12 px gap where the left
    /// neighbour's shield sits). Same elements, same draw calls, nothing per frame.
    /// </para>
    /// <para>
    /// Recharging skills (KAN-192): every frame the row reads each owned effect that is an <see cref="ISkillCooldown"/>
    /// (Hallowed Shield): the cooldown shade follows <see cref="ISkillCooldown.CooldownRemaining01"/> and, while the effect
    /// is up, the slot shows a steady gold ring (glow layer) and a gold inlay. Writes only on change, no GC.
    /// </para>
    /// Looks: hud-skill* in GameHud.uss; art from HudBarArtBuilder.
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
        private const string InlayClass = "hud-skill__inlay";
        private const string ClipClass = "hud-skill__clip";
        private const string CooldownClass = "hud-skill__cooldown";
        private const string CoolingClass = "hud-skill--cooling";
        private const string ShineClass = "hud-skill__shine";
        private const string SparkClass = "hud-skill__spark";
        private const string SparkLowClass = "hud-skill__spark--low";
        private const string BadgeClass = "hud-skill__badge";
        private const string LevelClass = "hud-skill__level";
        private const string GlowLayerClass = "hud-skills__glows";
        private const string GlowAnchorClass = "hud-skill-glow";
        private const string ActiveClass = "hud-skill--active";
        private const string GlowActiveClass = "hud-skill-glow--active";
        /// <summary>Opacity of the steady gold ring while a recharging skill's effect is up (KAN-192).</summary>
        private const float ActiveRingOpacity = 0.9f;
        /// <summary>Cooldown shade steps: the height is quantised so a per-frame call writes a style only ~64 times per cooldown.</summary>
        private const int CooldownSteps = 64;
        private static readonly string[] LevelText = BuildLevelText(20);
        private static readonly string[] RarityClasses = { "hud-skill--common", "hud-skill--rare", "hud-skill--epic", "hud-skill--legendary" };
        /// <summary>Tier classes of the glow anchor (KAN-194): the ring is not under its slot, so it gets its own tier cue.</summary>
        private static readonly string[] GlowRarityClasses = { "hud-skill-glow--common", "hud-skill-glow--rare", "hud-skill-glow--epic", "hud-skill-glow--legendary" };

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
            public VisualElement Root, Icon, Glow, PipRow, Inlay, Clip, Cooldown, Shine, SparkHigh, SparkLow, Badge;
            /// <summary>Holds <see cref="Glow"/> in the glow layer, on the slot's layout rect (KAN-194).</summary>
            public VisualElement GlowAnchor;
            public Label Level;
            public int ShownLevel = -1;
            /// <summary>Cooldown shade in steps of 1 / <see cref="CooldownSteps"/> (0 = ready).</summary>
            public int CooldownStep;
            /// <summary>Effect up (KAN-192): gold inlay + steady ring.</summary>
            public bool Active;
            /// <summary>The steady ring is on the glow element (a level-up glow takes the element over while it plays).</summary>
            public bool ActiveRing;
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
        private VisualElement _glowLayer;
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

        /// <summary>Level-up glow ring of slot <paramref name="index"/> (in the glow layer, not under the slot), null when not shown. Tests.</summary>
        public VisualElement GlowElement(int index) => index >= 0 && index < VisibleSlots && index < _slots.Count ? _slots[index].Glow : null;

        /// <summary>
        /// Cooldown shade on the slot of <paramref name="skill"/> (KAN-184; for skills that recharge, e.g. KAN-192): a dark
        /// shade over the icon covering <paramref name="remaining01"/> of the slot from the bottom (1 = just used, 0 = ready).
        /// Safe to call every frame: the style changes only when the quantised step changes. False when the skill has no slot.
        /// </summary>
        public bool SetCooldown(SkillDefinition skill, float remaining01)
        {
            int index = SlotOf(skill);
            if (index < 0) return false;
            ApplyCooldown(_slots[index], Mathf.CeilToInt(Mathf.Clamp01(remaining01) * CooldownSteps));
            return true;
        }

        /// <summary>Shown cooldown of slot <paramref name="index"/> (0..1, quantised). Tests.</summary>
        public float CooldownOf(int index) => index >= 0 && index < _slots.Count ? _slots[index].CooldownStep / (float)CooldownSteps : 0f;

        /// <summary>True while slot <paramref name="index"/> shows its recharging skill's effect as up (gold ring, KAN-192). Tests.</summary>
        public bool IsActiveShown(int index) => index >= 0 && index < VisibleSlots && index < _slots.Count && _slots[index].Active;

        /// <summary>Level number on the slot's shield (null when not shown). Tests, screenshots.</summary>
        public string LevelLabel(int index) => index >= 0 && index < VisibleSlots && index < _slots.Count ? _slots[index].Level.text : null;

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
            EnsureGlowLayer();
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
                slot.GlowAnchor.style.display = slot.Root.style.display;
                if (!on)
                {
                    slot.Skill = null;
                    EndGlow(slot);
                    ApplyCooldown(slot, 0);
                    ApplyActive(slot, false);
                    continue;
                }
                var o = owned[i];
                if (slot.Skill != o.Skill)
                {
                    EndGlow(slot); // run reset: a glow never jumps to another skill
                    ApplyCooldown(slot, 0); // nor a cooldown
                    ApplyActive(slot, false); // nor an active ring
                }
                slot.Skill = o.Skill;
                int r = (int)o.Skill.Rarity;
                for (int k = 0; k < RarityClasses.Length; k++)
                {
                    slot.Root.EnableInClassList(RarityClasses[k], k == r);
                    slot.GlowAnchor.EnableInClassList(GlowRarityClasses[k], k == r);
                }
                slot.Root.EnableInClassList(MaxClass, o.Level >= o.Skill.MaxLevel);
                slot.Icon.style.backgroundImage = o.Skill.Icon != null ? new StyleBackground(o.Skill.Icon) : StyleKeyword.None;
                SetPips(slot, o.Skill.MaxLevel, o.Level);
                if (slot.ShownLevel != o.Level)
                {
                    slot.ShownLevel = o.Level;
                    slot.Level.text = o.Level < LevelText.Length ? LevelText[o.Level] : o.Level.ToString();
                }
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
                GlowAnchor = new VisualElement { pickingMode = PickingMode.Ignore },
                Icon = new VisualElement { pickingMode = PickingMode.Ignore },
                PipRow = new VisualElement { pickingMode = PickingMode.Ignore },
                Inlay = new VisualElement { pickingMode = PickingMode.Ignore },
                Clip = new VisualElement { pickingMode = PickingMode.Ignore },
                Cooldown = new VisualElement { pickingMode = PickingMode.Ignore },
                Shine = new VisualElement { pickingMode = PickingMode.Ignore },
                SparkHigh = new VisualElement { pickingMode = PickingMode.Ignore },
                SparkLow = new VisualElement { pickingMode = PickingMode.Ignore },
                Badge = new VisualElement { pickingMode = PickingMode.Ignore },
                Level = new Label { pickingMode = PickingMode.Ignore },
            };
            slot.Root.AddToClassList(SlotClass);
            slot.Glow.AddToClassList(GlowClass);
            slot.GlowAnchor.AddToClassList(GlowAnchorClass);
            slot.Icon.AddToClassList(IconClass);
            slot.PipRow.AddToClassList(PipRowClass);
            slot.Inlay.AddToClassList(InlayClass);
            slot.Clip.AddToClassList(ClipClass);
            slot.Cooldown.AddToClassList(CooldownClass);
            slot.Shine.AddToClassList(ShineClass);
            slot.SparkHigh.AddToClassList(SparkClass);
            slot.SparkLow.AddToClassList(SparkClass);
            slot.SparkLow.AddToClassList(SparkLowClass);
            slot.Badge.AddToClassList(BadgeClass);
            slot.Level.AddToClassList(LevelClass);
            slot.Glow.style.opacity = 0f;
            slot.Shine.style.opacity = 0f;
            slot.SparkHigh.style.opacity = 0f;
            slot.SparkLow.style.opacity = 0f;
            slot.Cooldown.style.height = Length.Percent(0f);
            // Order = draw order: glow ring in the underlay (behind every slot, KAN-194), inlay on the frame, icon, then
            // shade + shine over the icon (clipped to the field), sparks, the level shield and the pips on top.
            slot.GlowAnchor.Add(slot.Glow);
            _glowLayer.Add(slot.GlowAnchor);
            slot.Root.RegisterCallback<GeometryChangedEvent>(_ => PlaceGlow(slot));
            slot.Root.Add(slot.Inlay);
            slot.Root.Add(slot.Icon);
            slot.Clip.Add(slot.Cooldown);
            slot.Clip.Add(slot.Shine);
            slot.Root.Add(slot.Clip);
            slot.Root.Add(slot.SparkHigh);
            slot.Root.Add(slot.SparkLow);
            slot.Badge.Add(slot.Level);
            slot.Root.Add(slot.Badge);
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
                // Shine (shared bar shine art) sweeps across the field once in the first 55 %.
                float sweep = Mathf.Clamp01(t / 0.55f);
                slot.Shine.style.opacity = sweep < 1f ? 1f : 0f;
                slot.Shine.style.translate = new Translate(Length.Percent(-100f + 300f * sweep), Length.Percent(0f));
                // Sparks (shared bar spark art) twinkle on two corners, the low one a beat later.
                SetSpark(slot.SparkHigh, t);
                SetSpark(slot.SparkLow, t - 0.18f);
                bool blinkOn = t >= 1f || Mathf.Repeat(t * 3f, 1f) < 0.6f;
                for (int p = slot.NewFrom; p < slot.NewTo && p < slot.Pips.Count; p++)
                    slot.Pips[p].EnableInClassList(PipNewClass, blinkOn && t < 1f);
                if (t >= 1f) EndGlow(slot);
            }

            PollCooldowns();
        }

        /// <summary>
        /// Recharging skills (KAN-192): shade + active ring from each owned <see cref="ISkillCooldown"/> effect. Reads the
        /// manager's list (no copy), an interface type test per slot; style writes only on change.
        /// </summary>
        private void PollCooldowns()
        {
            if (skills == null) return;
            var owned = skills.OwnedSkills;
            for (int i = 0; i < VisibleSlots && i < _slots.Count && i < owned.Count; i++)
            {
                var slot = _slots[i];
                if (owned[i].Skill != slot.Skill || !(owned[i].Effect is ISkillCooldown cd)) continue;
                ApplyCooldown(slot, Mathf.CeilToInt(Mathf.Clamp01(cd.CooldownRemaining01) * CooldownSteps));
                ApplyActive(slot, cd.CooldownEffectActive);
                if (slot.Active && !slot.ActiveRing && slot.GlowAge < 0f)
                {
                    PlaceGlow(slot); // a slot that never glowed may not have its anchor placed yet
                    slot.Glow.style.opacity = ActiveRingOpacity;
                    slot.ActiveRing = true;
                }
            }
        }

        private static void ApplyActive(Slot slot, bool active)
        {
            if (slot.Active == active) return;
            slot.Active = active;
            slot.Root.EnableInClassList(ActiveClass, active);
            slot.GlowAnchor.EnableInClassList(GlowActiveClass, active);
            if (active || !slot.ActiveRing) return;
            slot.ActiveRing = false;
            if (slot.GlowAge < 0f) slot.Glow.style.opacity = 0f; // a playing level-up glow keeps its own opacity
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
                PlaceGlow(slot); // layout may have settled without a geometry event reaching the anchor yet
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

        /// <summary>Glow underlay (KAN-194): first child of the row, absolute (out of the flex flow), so it draws before every slot.</summary>
        private void EnsureGlowLayer()
        {
            if (_row == null || (_glowLayer != null && _glowLayer.parent == _row)) return;
            if (_glowLayer == null)
            {
                _glowLayer = new VisualElement { pickingMode = PickingMode.Ignore };
                _glowLayer.AddToClassList(GlowLayerClass);
                _glowLayer.RegisterCallback<GeometryChangedEvent>(_ => PlaceGlows());
            }
            _row.Insert(0, _glowLayer);
        }

        private void PlaceGlows()
        {
            for (int i = 0; i < _slots.Count; i++) PlaceGlow(_slots[i]);
        }

        /// <summary>
        /// Puts the slot's glow anchor on the slot's layout rect. Both are children of the row, so the layer's own resolved
        /// position is the only offset (no padding or transform maths; the pick pop scale does not move the ring).
        /// </summary>
        private void PlaceGlow(Slot slot)
        {
            Rect r = slot.Root.layout;
            Rect l = _glowLayer.layout;
            if (float.IsNaN(r.width) || float.IsNaN(l.width)) return; // not laid out yet
            var s = slot.GlowAnchor.style;
            s.left = r.x - l.x;
            s.top = r.y - l.y;
            s.width = r.width;
            s.height = r.height;
        }

        private int SlotOf(SkillDefinition skill)
        {
            for (int i = 0; i < VisibleSlots && i < _slots.Count; i++)
                if (_slots[i].Skill == skill) return i;
            return -1;
        }

        private static void SetSpark(VisualElement spark, float t)
        {
            float k = Mathf.Clamp01(t / 0.6f);
            float a = t <= 0f ? 0f : Mathf.Sin(k * Mathf.PI);
            spark.style.opacity = a;
            float size = 0.5f + 0.9f * a;
            spark.style.scale = new Scale(new Vector3(size, size, 1f));
            spark.style.rotate = new Rotate(Angle.Degrees(120f * k));
        }

        private static void ApplyCooldown(Slot slot, int step)
        {
            if (slot.CooldownStep == step) return;
            slot.CooldownStep = step;
            slot.Cooldown.style.height = Length.Percent(100f * step / CooldownSteps);
            slot.Root.EnableInClassList(CoolingClass, step > 0);
        }

        private static string[] BuildLevelText(int max)
        {
            var text = new string[max + 1];
            for (int i = 0; i <= max; i++) text[i] = i.ToString();
            return text;
        }

        private static void EndGlow(Slot slot)
        {
            slot.GlowAge = -1f;
            slot.ActiveRing = false; // the next poll puts the steady ring back while the effect is up
            slot.Glow.style.opacity = 0f;
            slot.Shine.style.opacity = 0f;
            slot.SparkHigh.style.opacity = 0f;
            slot.SparkLow.style.opacity = 0f;
            slot.Glow.style.scale = new Scale(Vector3.one);
            slot.Icon.style.scale = new Scale(Vector3.one);
            for (int p = 0; p < slot.Pips.Count; p++) slot.Pips[p].RemoveFromClassList(PipNewClass);
        }
    }
}
