using SurvivorSouls.Gameplay.Skills;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// Ember Boons art in the HUD (KAN-246). The T_Boon_* portraits are full colour and 3:4 (<see cref="SkillDefinition.HasPortraitArt"/>),
    /// the old icons white glyphs tinted by the tier colour. Inline styles, set from code next to the background image, so
    /// GameHud.uss and the HUD scene stay untouched:
    /// <list type="bullet">
    /// <item>level-up card (<see cref="PortraitPlate"/> once per card, <see cref="SetCardIcon"/> per image): a 3:4 plate
    /// (<see cref="PlateWidth"/> x <see cref="PlateHeight"/>, same height as the old round plate, so the reel slot of KAN-159
    /// keeps its 188 px step) that shows the whole portrait untinted; a glyph keeps its tint and sits centred;</item>
    /// <item>small slots (<see cref="SetSlotIcon"/>, HUD skill row): a centre crop of the portrait (zoomed past the painted
    /// frame) in a <see cref="SlotCrop"/> px square that fits inside every tier's field.</item>
    /// </list>
    /// </summary>
    public static class SkillArtStyle
    {
        public const float PlateWidth = 150f, PlateHeight = 196f, PlateRadius = 16f, PlateBorder = 4f;
        public const float SlotCrop = 44f;
        /// <summary>Crop zoom of the small slots: the portrait is drawn this much wider than the slot (frame cut away).</summary>
        public const float SlotZoom = 1.22f;

        public static float CardIconWidth => PlateWidth - 2f * PlateBorder;
        public static float CardIconHeight => PlateHeight - 2f * PlateBorder;

        /// <summary>Makes the round icon plate of a level-up card a 3:4 portrait frame (icon, reel icon and burst sparks follow).</summary>
        public static void PortraitPlate(VisualElement plate, VisualElement icon, VisualElement reel, VisualElement[] sparks)
        {
            if (plate == null) return;
            plate.style.width = PlateWidth;
            plate.style.height = PlateHeight;
            plate.style.borderTopLeftRadius = PlateRadius;
            plate.style.borderTopRightRadius = PlateRadius;
            plate.style.borderBottomLeftRadius = PlateRadius;
            plate.style.borderBottomRightRadius = PlateRadius;
            plate.style.overflow = Overflow.Hidden;
            foreach (var el in new[] { icon, reel })
            {
                if (el == null) continue;
                el.style.width = CardIconWidth;
                el.style.height = CardIconHeight;
            }
            if (reel != null)
            {
                reel.style.left = 0f;
                reel.style.top = 0f;
            }
            if (sparks == null) return;
            foreach (var s in sparks)
            {
                if (s == null) continue;
                s.style.left = CardIconWidth * 0.5f - 9f;
                s.style.top = CardIconHeight * 0.5f - 9f;
            }
        }

        /// <summary>Card icon (still, reel or landing): the whole portrait untinted, or the tinted glyph from the USS.</summary>
        public static void SetCardIcon(VisualElement icon, SkillDefinition skill)
        {
            if (icon == null) return;
            icon.style.backgroundImage = skill != null && skill.Icon != null ? new StyleBackground(skill.Icon) : StyleKeyword.None;
            bool art = skill != null && skill.HasPortraitArt;
            icon.style.unityBackgroundImageTintColor = art ? new StyleColor(Color.white) : StyleKeyword.Null;
        }

        /// <summary>Small slot icon (HUD skill row): a centre crop of the portrait, or the tinted glyph from the USS.</summary>
        public static void SetSlotIcon(VisualElement icon, SkillDefinition skill)
        {
            if (icon == null) return;
            icon.style.backgroundImage = skill != null && skill.Icon != null ? new StyleBackground(skill.Icon) : StyleKeyword.None;
            if (skill != null && skill.HasPortraitArt)
            {
                icon.style.width = SlotCrop;
                icon.style.height = SlotCrop;
                icon.style.unityBackgroundImageTintColor = new StyleColor(Color.white);
                icon.style.backgroundSize = new BackgroundSize(Length.Percent(SlotZoom * 100f), Length.Auto());
                icon.style.backgroundPositionX = new BackgroundPosition(BackgroundPositionKeyword.Center);
                icon.style.backgroundPositionY = new BackgroundPosition(BackgroundPositionKeyword.Center);
                icon.style.backgroundRepeat = new BackgroundRepeat(Repeat.NoRepeat, Repeat.NoRepeat);
            }
            else
            {
                icon.style.width = StyleKeyword.Null;
                icon.style.height = StyleKeyword.Null;
                icon.style.unityBackgroundImageTintColor = StyleKeyword.Null;
                icon.style.backgroundSize = StyleKeyword.Null;
                icon.style.backgroundPositionX = StyleKeyword.Null;
                icon.style.backgroundPositionY = StyleKeyword.Null;
                icon.style.backgroundRepeat = StyleKeyword.Null;
            }
        }
    }
}
