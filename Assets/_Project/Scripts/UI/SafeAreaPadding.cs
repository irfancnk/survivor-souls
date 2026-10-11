using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI
{
    /// <summary>
    /// Pads a UI Toolkit element so content stays inside Screen.safeArea (notch, home bar).
    /// Call Refresh() each frame or on geometry change; it is cheap when nothing changed.
    /// </summary>
    public sealed class SafeAreaPadding
    {
        private readonly VisualElement _target;
        private Rect _lastSafeArea;
        private Vector2Int _lastScreen;

        public SafeAreaPadding(VisualElement target)
        {
            _target = target;
            _target.RegisterCallback<GeometryChangedEvent>(_ => Refresh(force: true));
        }

        public void Refresh(bool force = false)
        {
            var safe = Screen.safeArea;
            var screen = new Vector2Int(Screen.width, Screen.height);
            if (!force && safe == _lastSafeArea && screen == _lastScreen) return;
            if (_target.panel == null || screen.x == 0 || screen.y == 0) return;

            _lastSafeArea = safe;
            _lastScreen = screen;

            // Convert pixel insets to panel units.
            var panelSize = _target.panel.visualTree.layout.size;
            if (panelSize.x <= 0f || panelSize.y <= 0f) return;
            float sx = panelSize.x / screen.x;
            float sy = panelSize.y / screen.y;

            _target.style.paddingLeft = safe.xMin * sx;
            _target.style.paddingRight = (screen.x - safe.xMax) * sx;
            _target.style.paddingTop = (screen.y - safe.yMax) * sy;
            _target.style.paddingBottom = safe.yMin * sy;
        }
    }
}
