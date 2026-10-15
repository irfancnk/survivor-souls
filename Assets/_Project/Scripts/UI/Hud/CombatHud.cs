using System.Collections.Generic;
using System;
using SurvivorSouls.Gameplay.Chests;
using SurvivorSouls.Gameplay.Combat;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// World-anchored combat UI on the HUD document: a thin health bar over every wounded living enemy (KAN-30: a fresh
    /// horde shows no bars, which keeps the screen readable and the per-frame UI work small) and a
    /// floating "-N" number per hit (health actually deducted; red on the killing blow, gold "N!" on a crit, small orange for
    /// fire ticks, pale blue for soul bursts), and "+N SHARDS" in ember red over a Rare chest that gave Ember Shards (KAN-97).
    /// Elements are pooled and placed each frame from world positions (after the camera moved).
    /// Looks live in GameHud.uss (combat-* classes).
    /// </summary>
    [DefaultExecutionOrder(500)] // after TopDownFollowCamera.LateUpdate
    [RequireComponent(typeof(UIDocument))]
    public sealed class CombatHud : MonoBehaviour
    {
        private const string LayerClass = "combat-layer";
        private const string BarClass = "combat-bar";
        private const string BarFillClass = "combat-bar__fill";
        private const string NumberClass = "combat-number";
        private const string NumberKillClass = "combat-number--kill";
        private const string NumberCritClass = "combat-number--crit";
        private const string NumberBurnClass = "combat-number--burn";
        private const string NumberBurstClass = "combat-number--burst";
        private const string NumberGemsClass = "combat-number--gems";

        [SerializeField] private Camera worldCamera;
        [Tooltip("Rare chest shard pops (KAN-97). Null = none.")]
        [SerializeField] private ChestRewards chestRewards;
        [SerializeField, Min(0.1f)] private float numberLifetime = 0.85f;
        [SerializeField] private float numberRise = 0.9f; // metres over its lifetime

        private sealed class Bar { public VisualElement Root, Fill; }
        private struct Number { public Label Label; public Vector3 World; public float Age; public float Life; }

        private UIDocument _document;
        private VisualElement _layer;
        private readonly Dictionary<Enemy, Bar> _bars = new Dictionary<Enemy, Bar>();
        private readonly Stack<Bar> _freeBars = new Stack<Bar>();
        private readonly List<Enemy> _stale = new List<Enemy>();
        private readonly List<Number> _numbers = new List<Number>();
        private readonly Stack<Label> _freeLabels = new Stack<Label>();
        private Action<Vector3, int> _onGems;

        private void Awake() => _document = GetComponent<UIDocument>();

        private void OnEnable()
        {
            _layer = new VisualElement { pickingMode = PickingMode.Ignore };
            _layer.AddToClassList(LayerClass);
            _document.rootVisualElement.Insert(0, _layer); // under joystick and buttons
            Enemy.Damaged += OnDamaged;
            _onGems ??= OnGemsFound;
            if (chestRewards != null) chestRewards.GemsFound += _onGems;
        }

        private void OnDisable()
        {
            Enemy.Damaged -= OnDamaged;
            if (chestRewards != null) chestRewards.GemsFound -= _onGems;
            _layer?.RemoveFromHierarchy();
            _bars.Clear();
            _freeBars.Clear();
            _numbers.Clear();
            _freeLabels.Clear();
        }

        private void OnDamaged(Enemy enemy, float deducted, bool killed)
        {
            var label = _freeLabels.Count > 0 ? _freeLabels.Pop() : NewLabel();
            bool crit = enemy.LastHitCrit;
            var kind = enemy.LastDamageKind;
            label.text = crit ? Mathf.CeilToInt(deducted) + "!" : "-" + Mathf.CeilToInt(deducted);
            label.EnableInClassList(NumberKillClass, killed);
            label.EnableInClassList(NumberCritClass, crit);
            label.EnableInClassList(NumberBurnClass, kind == DamageKind.Burn);
            label.EnableInClassList(NumberBurstClass, kind == DamageKind.Burst);
            label.EnableInClassList(NumberGemsClass, false);
            label.style.display = DisplayStyle.Flex;
            Vector3 jitter = new Vector3(UnityEngine.Random.Range(-0.25f, 0.25f), 0f, 0f);
            _numbers.Add(new Number { Label = label, World = enemy.BarAnchor + Vector3.up * 0.15f + jitter, Age = 0f, Life = numberLifetime });
        }

        /// <summary>"+N SHARDS" over the chest, held 2.5x as long as a hit number (KAN-97).</summary>
        private void OnGemsFound(Vector3 at, int amount)
        {
            var label = _freeLabels.Count > 0 ? _freeLabels.Pop() : NewLabel();
            label.text = "+" + amount + " SHARDS";
            label.EnableInClassList(NumberKillClass, false);
            label.EnableInClassList(NumberCritClass, false);
            label.EnableInClassList(NumberBurnClass, false);
            label.EnableInClassList(NumberBurstClass, false);
            label.EnableInClassList(NumberGemsClass, true);
            label.style.display = DisplayStyle.Flex;
            _numbers.Add(new Number { Label = label, World = at + Vector3.up * 1.4f, Age = 0f, Life = numberLifetime * 2.5f });
        }

        private Label NewLabel()
        {
            var l = new Label { pickingMode = PickingMode.Ignore };
            l.AddToClassList(NumberClass);
            _layer.Add(l);
            return l;
        }

        private void LateUpdate()
        {
            if (_layer?.panel == null) return;
            var cam = worldCamera != null ? worldCamera : Camera.main;
            if (cam == null) return;
            UpdateBars(cam);
            UpdateNumbers(cam, Time.deltaTime);
        }

        private void UpdateBars(Camera cam)
        {
            _stale.Clear();
            foreach (var kv in _bars)
                if (!kv.Key.IsAlive || !kv.Key.IsHurt || !kv.Key.isActiveAndEnabled) _stale.Add(kv.Key);
            foreach (var e in _stale)
            {
                var bar = _bars[e];
                bar.Root.style.display = DisplayStyle.None;
                _freeBars.Push(bar);
                _bars.Remove(e);
            }

            foreach (var enemy in Enemy.Active)
            {
                if (!enemy.IsHurt || enemy.IsBoss) continue; // the boss has the HUD bar (BossHud)
                if (!_bars.TryGetValue(enemy, out var bar))
                {
                    bar = _freeBars.Count > 0 ? _freeBars.Pop() : NewBar();
                    bar.Root.style.display = DisplayStyle.Flex;
                    _bars[enemy] = bar;
                }
                bar.Fill.style.width = Length.Percent(100f * enemy.Health / enemy.MaxHealth);
                Place(bar.Root, cam, enemy.BarAnchor);
            }
        }

        private Bar NewBar()
        {
            var root = new VisualElement { pickingMode = PickingMode.Ignore };
            root.AddToClassList(BarClass);
            var fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.AddToClassList(BarFillClass);
            root.Add(fill);
            _layer.Add(root);
            return new Bar { Root = root, Fill = fill };
        }

        private void UpdateNumbers(Camera cam, float dt)
        {
            for (int i = _numbers.Count - 1; i >= 0; i--)
            {
                var n = _numbers[i];
                n.Age += dt;
                float t = n.Age / n.Life;
                if (t >= 1f)
                {
                    n.Label.style.display = DisplayStyle.None;
                    _freeLabels.Push(n.Label);
                    _numbers.RemoveAt(i);
                    continue;
                }
                _numbers[i] = n;
                float ease = 1f - (1f - t) * (1f - t);
                Place(n.Label, cam, n.World + Vector3.up * (numberRise * ease));
                n.Label.style.opacity = 1f - t * t;
                float pop = t < 0.15f ? Mathf.Lerp(1.5f, 1f, t / 0.15f) : 1f;
                n.Label.style.scale = new Scale(new Vector3(pop, pop, 1f));
            }
        }

        /// <summary>Centres <paramref name="e"/> on the screen point of <paramref name="world"/>; hides it behind the camera.</summary>
        private void Place(VisualElement e, Camera cam, Vector3 world)
        {
            Vector3 view = cam.WorldToViewportPoint(world);
            if (view.z <= 0f) { e.style.visibility = Visibility.Hidden; return; }
            e.style.visibility = Visibility.Visible;
            Vector2 p = RuntimePanelUtils.CameraTransformWorldToPanel(_layer.panel, world, cam);
            e.style.translate = new Translate(p.x, p.y);
        }
    }
}
