using System.Collections.Generic;
using SurvivorSouls.Gameplay.Skills;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// Owned skills row under the health bar (KAN-19): one small rune icon per skill in pick order, rarity-coloured and -shaped rim (KAN-37),
    /// "xN" when stacked. Rebuilt only when <see cref="SkillManager.Changed"/> fires (slots are pooled); the slot that just
    /// gained a stack pops once (unscaled time: the pick happens during the level-up pause).
    /// Looks: hud-skill* in GameHud.uss.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class SkillHud : MonoBehaviour
    {
        private const string RowName = "skill-row";
        private const string SlotClass = "hud-skill";
        private const string IconClass = "hud-skill__icon";
        private const string StackClass = "hud-skill__stack";
        private static readonly string[] RarityClasses = { "hud-skill--common", "hud-skill--rare", "hud-skill--epic", "hud-skill--legendary" };

        [SerializeField] private SkillManager skills;
        [SerializeField, Min(0.05f)] private float popTime = 0.45f;

        private sealed class Slot
        {
            public VisualElement Root, Icon;
            public Label Stack;
        }

        private readonly List<Slot> _slots = new List<Slot>();
        private VisualElement _row;
        private SkillDefinition _popSkill;
        private int _popSlot = -1;
        private float _popAge = -1f;

        public int VisibleSlots { get; private set; }

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
            Rebuild();
        }

        private void OnDisable()
        {
            if (skills == null) return;
            skills.Changed -= Rebuild;
            skills.Granted -= OnGranted;
        }

        private void OnGranted(SkillDefinition skill, int stacks) => _popSkill = skill; // Changed follows and starts the pop

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
                if (!on) continue;
                var o = owned[i];
                int r = (int)o.Skill.Rarity;
                for (int k = 0; k < RarityClasses.Length; k++) slot.Root.EnableInClassList(RarityClasses[k], k == r);
                slot.Icon.style.backgroundImage = o.Skill.Icon != null ? new StyleBackground(o.Skill.Icon) : StyleKeyword.None;
                slot.Stack.text = o.Stacks > 1 ? "x" + o.Stacks : "";
                if (o.Skill == _popSkill) _popSlot = i;
            }
            _popSkill = null;
            _popAge = _popSlot >= 0 ? 0f : -1f;
        }

        private Slot NewSlot()
        {
            var root = new VisualElement { pickingMode = PickingMode.Ignore };
            root.AddToClassList(SlotClass);
            var icon = new VisualElement { pickingMode = PickingMode.Ignore };
            icon.AddToClassList(IconClass);
            var stack = new Label { pickingMode = PickingMode.Ignore };
            stack.AddToClassList(StackClass);
            root.Add(icon);
            root.Add(stack);
            _row.Add(root);
            return new Slot { Root = root, Icon = icon, Stack = stack };
        }

        private void Update()
        {
            if (_popAge < 0f || _popSlot < 0 || _popSlot >= _slots.Count) return;
            _popAge += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(_popAge / popTime);
            float s = 1f + 0.45f * Mathf.Sin(t * Mathf.PI) * (1f - t * 0.5f);
            _slots[_popSlot].Root.style.scale = new Scale(new Vector3(s, s, 1f));
            if (t >= 1f) _popAge = -1f;
        }
    }
}
