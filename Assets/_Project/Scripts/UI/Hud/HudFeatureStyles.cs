using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// Adds the HUD feature style sheets (KAN-228) to the HUD root, after GameHud.uss. Each feature keeps its rules in its own
    /// <c>UI/Hud/Features/*.uss</c> file instead of GameHud.uss, so parallel tickets do not conflict there. The scene builder
    /// step <c>HudFeatureStylesStep</c> fills the list from that folder. Runs once on enable: no per-frame work.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class HudFeatureStyles : MonoBehaviour
    {
        [SerializeField] private StyleSheet[] sheets = new StyleSheet[0];

        public StyleSheet[] Sheets => sheets;

        private void OnEnable() => Apply(GetComponent<UIDocument>().rootVisualElement, sheets);

        private void Start() => Apply(GetComponent<UIDocument>().rootVisualElement, sheets); // root may be built after our OnEnable

        /// <summary>Adds each sheet once (repeat calls change nothing). Returns how many sheets the root now has from the list.</summary>
        public static int Apply(VisualElement root, StyleSheet[] list)
        {
            if (root == null || list == null) return 0;
            int count = 0;
            foreach (var sheet in list)
            {
                if (sheet == null) continue;
                if (!root.styleSheets.Contains(sheet)) root.styleSheets.Add(sheet);
                count++;
            }
            return count;
        }
    }
}
