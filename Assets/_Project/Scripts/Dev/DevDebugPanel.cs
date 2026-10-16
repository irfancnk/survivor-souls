#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Chests;
using SurvivorSouls.Gameplay.Skills;
using SurvivorSouls.UI.Hud;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SurvivorSouls.Dev
{
    /// <summary>
    /// In-game touch debug panel for development builds (KAN-87): playtesters on a phone change the run without launch
    /// arguments. Open / close: a 3-finger tap anywhere (phone), F2 or the backquote key (Mac / Editor). While open the game
    /// is paused (<see cref="GamePause"/> hold).
    /// Panel: a level slider and - / + per skill (<see cref="SkillManager.SetLevel(SkillDefinition, int)"/>, 0 removes),
    /// drop a Light / Rare chest, level up now, boss kill / half health / jump to the boss Chamber, portal timer to 10 s /
    /// open now, and the last 10 <see cref="GameTelemetry"/> events. Every action is a <see cref="DevCheats"/> call (the
    /// same code as the Editor debug menus): the panel holds no game rules.
    /// No scene wiring: a bootstrap adds the panel to every scene with the Game HUD (<see cref="GameHudController"/>) and
    /// shares the HUD's PanelSettings, drawn above it. The UI is built in code (no UXML / USS assets to keep in sync).
    /// Compiled only into the Editor and development players, so a release build has no panel, gesture or cheat code.
    /// Dev arg: <c>-ss-debug-panel</c> opens it after load (screenshots); <c>-ss-debug-panel demo:&lt;skillId&gt;</c> runs the
    /// acceptance flow (open, skill to level 3, drop a Rare chest, close) through the panel's own controls and logs
    /// <c>[DevDebugPanel] Demo ...</c> lines.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class DevDebugPanel : MonoBehaviour
    {
        public const string RootName = "dev-panel";
        public const string CloseName = "dev-close";
        public const string DropLightName = "dev-drop-light";
        public const string DropRareName = "dev-drop-rare";
        public const string LevelUpName = "dev-level-up";
        public const string BossKillName = "dev-boss-kill";
        public const string BossHalfName = "dev-boss-half";
        public const string BossJumpName = "dev-boss-jump";
        public const string PortalTenName = "dev-portal-10";
        public const string PortalOpenName = "dev-portal-open";
        public const string SkillRowPrefix = "dev-skill-";
        public const string TelemetryName = "dev-telemetry";
        public const int TelemetryRows = 10;
        /// <summary>Above the HUD and the skill pick screen.</summary>
        public const int SortingOrder = 100;
        public const string ArgName = "-ss-debug-panel";

        private const float FontSize = 34f;
        private const float ButtonHeight = 100f;
        private static readonly Color Backdrop = new Color(0f, 0f, 0f, 0.72f);
        private static readonly Color CardColor = new Color(0.09f, 0.08f, 0.07f, 0.97f);
        private static readonly Color Gold = new Color(0.86f, 0.70f, 0.38f);
        private static readonly Color ButtonColor = new Color(0.22f, 0.19f, 0.16f);
        private static readonly Color TextColor = new Color(0.93f, 0.90f, 0.84f);
        private static readonly Color DimText = new Color(0.70f, 0.66f, 0.60f);

        public static DevDebugPanel Instance { get; private set; }
        public bool IsOpen { get; private set; }
        public VisualElement Root => _root;

        private UIDocument _document;
        private VisualElement _root;
        private VisualElement _skillList;
        private readonly List<SkillDefinition> _skills = new List<SkillDefinition>();
        private readonly List<SkillRow> _rows = new List<SkillRow>();
        private readonly Label[] _telemetry = new Label[TelemetryRows];
        private Button _bossKill, _bossHalf, _bossJump, _portalTen, _portalOpen, _levelUp, _dropLight, _dropRare;
        private Label _status;
        private SkillManager _manager;
        private bool _holding;
        private int _lastTouches;

        private sealed class SkillRow
        {
            public SkillDefinition Skill;
            public Label Level;
            public SliderInt Slider;
        }

        // ------------------------------------------------------------ bootstrap

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            AttachTo(SceneManager.GetActiveScene());
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode) => AttachTo(scene);

        /// <summary>Adds the panel to <paramref name="scene"/> when it has the Game HUD and no panel yet. Returns the panel or null.</summary>
        public static DevDebugPanel AttachTo(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            GameHudController hud = null;
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.GetComponentInChildren<DevDebugPanel>(true) != null) return null;
                if (hud == null) hud = root.GetComponentInChildren<GameHudController>(true);
            }
            var hudDocument = hud != null ? hud.GetComponent<UIDocument>() : null;
            if (hudDocument == null || hudDocument.panelSettings == null) return null;

            var go = new GameObject("Dev Debug Panel");
            go.SetActive(false);
            SceneManager.MoveGameObjectToScene(go, scene);
            var document = go.AddComponent<UIDocument>();
            document.panelSettings = hudDocument.panelSettings;
            document.sortingOrder = SortingOrder;
            var panel = go.AddComponent<DevDebugPanel>();
            go.SetActive(true);
            return panel;
        }

        // ------------------------------------------------------------ lifecycle

        private void Awake() => _document = GetComponent<UIDocument>();

        private void OnEnable()
        {
            Instance = this;
            Build(_document.rootVisualElement);
            GameTelemetry.Tracked += OnTracked;
            if (Application.isEditor) return;
            string arg = ReadArg();
            if (arg == null) return;
            if (arg.StartsWith("demo:", StringComparison.Ordinal)) StartCoroutine(Demo(arg.Substring(5)));
            else StartCoroutine(OpenAfterLoad());
        }

        private void OnDisable()
        {
            GameTelemetry.Tracked -= OnTracked;
            Unhook();
            if (_holding) GamePause.Release();
            _holding = false;
            IsOpen = false;
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (WasToggled()) Toggle();
        }

        /// <summary>3 fingers down at once (rising edge), or F2 / backquote. Reads devices directly: no allocation.</summary>
        private bool WasToggled()
        {
            int touches = 0;
            var screen = Touchscreen.current;
            if (screen != null)
            {
                var all = screen.touches;
                for (int i = 0; i < all.Count; i++)
                    if (all[i].press.isPressed) touches++;
            }
            bool tap = touches >= 3 && _lastTouches < 3;
            _lastTouches = touches;
            var keys = Keyboard.current;
            return tap || (keys != null && (keys.f2Key.wasPressedThisFrame || keys.backquoteKey.wasPressedThisFrame));
        }

        // ------------------------------------------------------------ open / close

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            if (!_holding)
            {
                GamePause.Acquire();
                _holding = true;
            }
            Hook();
            RebuildSkills();
            Refresh();
            _status.text = "";
            _root.style.display = DisplayStyle.Flex;
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            _root.style.display = DisplayStyle.None;
            Unhook();
            if (_holding) GamePause.Release();
            _holding = false;
        }

        private void Hook()
        {
            Unhook();
            _manager = DevCheats.Skills;
            if (_manager != null) _manager.SkillChanged += OnSkillChanged;
        }

        private void Unhook()
        {
            if (_manager != null) _manager.SkillChanged -= OnSkillChanged;
            _manager = null;
        }

        // ------------------------------------------------------------ actions (each one DevCheats call)

        public void DropChest(ChestTier tier)
        {
            var chest = DevCheats.DropChest(tier);
            Say(chest != null ? $"{tier} chest dropped" : "No chest: no dropper, or the ground is full");
        }

        public void LevelUp() => Say(DevCheats.LevelUpNow() ? "Level up: the pick opens when the panel closes" : "No XP manager");

        private void BossKill() => Say(DevCheats.KillBoss() ? "Boss killed" : "No boss");
        private void BossHalf() => Say(DevCheats.SetBossHealth(0.5f) ? "Boss at 50 %" : "No boss");

        private void BossJump()
        {
            int chamber = DevCheats.JumpToBossChamber();
            if (chamber == 0) Say("Not in the Game scene");
        }

        private void PortalTen()
        {
            Say(DevCheats.PortalTimerTo(10f) ? "Portal timer: 10 s" : "No portal timer");
            Refresh();
        }

        private void PortalOpen()
        {
            Say(DevCheats.OpenPortalNow() ? "Portal open" : "No portal timer");
            Refresh();
        }

        /// <summary>Sets <paramref name="skill"/> to <paramref name="level"/> (clamped 0..max) through <see cref="DevCheats"/>.</summary>
        public bool SetSkillLevel(SkillDefinition skill, int level)
        {
            if (skill == null) return false;
            bool ok = DevCheats.SetSkillLevel(skill, Mathf.Clamp(level, 0, skill.MaxLevel));
            RefreshRow(skill);
            return ok;
        }

        /// <summary>The panel row (level slider) of the skill with this id; null when the panel has none.</summary>
        public SliderInt SkillSlider(string skillId)
        {
            foreach (var row in _rows)
                if (row.Skill.Id == skillId) return row.Slider;
            return null;
        }

        private void Say(string text)
        {
            if (_status != null) _status.text = text;
            Debug.Log("[DevDebugPanel] " + text);
        }

        // ------------------------------------------------------------ refresh

        private void OnSkillChanged(SkillDefinition skill, int from, int to) => RefreshRow(skill);

        private void OnTracked(TelemetryEvent e)
        {
            if (IsOpen) RefreshTelemetry();
        }

        private void Refresh()
        {
            foreach (var row in _rows) RefreshRow(row);
            bool boss = DevCheats.HasBoss, timer = DevCheats.HasPortalTimer, game = DevCheats.InGameScene;
            _bossKill.SetEnabled(boss);
            _bossHalf.SetEnabled(boss);
            _bossJump.SetEnabled(game);
            _portalTen.SetEnabled(timer);
            _portalOpen.SetEnabled(timer);
            RefreshTelemetry();
        }

        private void RefreshRow(SkillDefinition skill)
        {
            foreach (var row in _rows)
                if (row.Skill == skill) RefreshRow(row);
        }

        private void RefreshRow(SkillRow row)
        {
            int level = _manager != null ? _manager.GetLevel(row.Skill) : 0;
            row.Level.text = $"{level}/{row.Skill.MaxLevel}";
            row.Slider.SetValueWithoutNotify(level);
        }

        private void RefreshTelemetry()
        {
            int shown = Mathf.Min(TelemetryRows, GameTelemetry.RecentCount);
            for (int i = 0; i < TelemetryRows; i++)
            {
                _telemetry[i].text = i < shown ? GameTelemetry.Recent(i).ToString() : "";
                _telemetry[i].style.display = i < shown ? DisplayStyle.Flex : DisplayStyle.None;
            }
            if (shown == 0)
            {
                _telemetry[0].text = "No events yet.";
                _telemetry[0].style.display = DisplayStyle.Flex;
            }
        }

        // ------------------------------------------------------------ UI

        private void Build(VisualElement documentRoot)
        {
            documentRoot.Clear();
            documentRoot.pickingMode = PickingMode.Ignore; // closed: taps reach the HUD below
            var r = documentRoot.style;
            r.position = Position.Absolute;
            r.left = r.right = r.top = r.bottom = 0;
            _rows.Clear();
            _root = new VisualElement { name = RootName, pickingMode = PickingMode.Position };
            var s = _root.style;
            s.position = Position.Absolute;
            s.left = s.right = s.top = s.bottom = 0;
            s.backgroundColor = Backdrop;
            s.alignItems = Align.Center;
            s.justifyContent = Justify.Center;
            s.display = DisplayStyle.None;
            documentRoot.Add(_root);

            var card = new VisualElement();
            card.style.width = Length.Percent(94);
            card.style.maxHeight = Length.Percent(88);
            card.style.backgroundColor = CardColor;
            Border(card, Gold, 3f, 18f);
            Pad(card, 24f);
            _root.Add(card);

            var header = Row();
            var title = Text($"DEV PANEL  {BuildInfo.Current.Label(false)}", 40f, Gold);
            title.style.flexGrow = 1;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(title);
            var close = MakeButton(CloseName, "Close", Close);
            close.style.width = 200f;
            header.Add(close);
            card.Add(header);

            _status = Text("", 30f, DimText);
            _status.style.marginTop = 8f;
            _status.style.minHeight = 40f;
            card.Add(_status);

            var scroll = new ScrollView(ScrollViewMode.Vertical);
            scroll.style.flexShrink = 1;
            scroll.touchScrollBehavior = ScrollView.TouchScrollBehavior.Clamped;
            scroll.horizontalScrollerVisibility = ScrollerVisibility.Hidden;
            card.Add(scroll);

            scroll.Add(Heading("Chests / XP"));
            var chests = Row();
            _dropLight = MakeButton(DropLightName, "Drop Light", () => DropChest(ChestTier.Light));
            _dropRare = MakeButton(DropRareName, "Drop Rare", () => DropChest(ChestTier.Rare));
            _levelUp = MakeButton(LevelUpName, "Level up", LevelUp);
            chests.Add(_dropLight);
            chests.Add(_dropRare);
            chests.Add(_levelUp);
            scroll.Add(chests);

            scroll.Add(Heading("Boss"));
            var boss = Row();
            _bossKill = MakeButton(BossKillName, "Kill", BossKill);
            _bossHalf = MakeButton(BossHalfName, "50 %", BossHalf);
            _bossJump = MakeButton(BossJumpName, "To boss", BossJump);
            boss.Add(_bossKill);
            boss.Add(_bossHalf);
            boss.Add(_bossJump);
            scroll.Add(boss);

            scroll.Add(Heading("Portal"));
            var portal = Row();
            _portalTen = MakeButton(PortalTenName, "Timer 10 s", PortalTen);
            _portalOpen = MakeButton(PortalOpenName, "Open now", PortalOpen);
            portal.Add(_portalTen);
            portal.Add(_portalOpen);
            scroll.Add(portal);

            scroll.Add(Heading("Skills"));
            _skillList = new VisualElement();
            scroll.Add(_skillList);

            scroll.Add(Heading($"Telemetry (last {TelemetryRows})"));
            var telemetry = new VisualElement { name = TelemetryName };
            for (int i = 0; i < TelemetryRows; i++)
            {
                _telemetry[i] = Text("", 26f, DimText);
                telemetry.Add(_telemetry[i]);
            }
            scroll.Add(telemetry);
        }

        /// <summary>One row per skill of the run's table (rebuilt on open: a new Chamber has a new SkillManager).</summary>
        private void RebuildSkills()
        {
            DevCheats.CollectSkills(_manager, _skills);
            if (_rows.Count == _skills.Count)
            {
                bool same = true;
                for (int i = 0; i < _rows.Count && same; i++) same = _rows[i].Skill == _skills[i];
                if (same) return;
            }
            _skillList.Clear();
            _rows.Clear();
            if (_skills.Count == 0) _skillList.Add(Text("No skill table in this scene.", FontSize, DimText));
            foreach (var skill in _skills)
            {
                var row = new SkillRow { Skill = skill };
                var box = new VisualElement { name = SkillRowPrefix + skill.Id };
                box.style.marginBottom = 14f;
                var line = Row();
                var name = Text(string.IsNullOrEmpty(skill.DisplayName) ? skill.Id : skill.DisplayName, FontSize, TextColor);
                name.style.flexGrow = 1;
                name.style.flexShrink = 1;
                line.Add(name);
                row.Level = Text("0/0", FontSize, Gold);
                row.Level.style.width = 110f;
                row.Level.style.unityTextAlign = TextAnchor.MiddleCenter;
                var minus = MakeButton(SkillRowPrefix + skill.Id + "-minus", "-", () => SetSkillLevel(skill, CurrentLevel(skill) - 1));
                var plus = MakeButton(SkillRowPrefix + skill.Id + "-plus", "+", () => SetSkillLevel(skill, CurrentLevel(skill) + 1));
                foreach (var b in new[] { minus, plus })
                {
                    b.style.flexGrow = 0;
                    b.style.flexShrink = 0;
                    b.style.flexBasis = 120f; // fixed square-ish touch target (MakeButton's basis 0 shares a row)
                    b.style.fontSize = 44f;
                }
                line.Add(minus);
                line.Add(row.Level);
                line.Add(plus);
                box.Add(line);
                row.Slider = new SliderInt(0, skill.MaxLevel) { name = SkillRowPrefix + skill.Id + "-slider", pageSize = 1 };
                row.Slider.style.height = 60f;
                row.Slider.style.marginTop = 6f;
                row.Slider.RegisterValueChangedCallback(e => SetSkillLevel(skill, e.newValue));
                box.Add(row.Slider);
                _skillList.Add(box);
                _rows.Add(row);
            }
        }

        private int CurrentLevel(SkillDefinition skill) => _manager != null ? _manager.GetLevel(skill) : 0;

        private static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            return row;
        }

        private static Label Heading(string text)
        {
            var label = Text(text, 36f, Gold);
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginTop = 22f;
            label.style.marginBottom = 8f;
            return label;
        }

        private static Label Text(string text, float size, Color color)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.color = color;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static Button MakeButton(string name, string text, Action onClick)
        {
            var button = new Button(onClick) { name = name, text = text };
            var s = button.style;
            s.height = ButtonHeight;
            s.flexGrow = 1;
            s.flexBasis = 0;
            s.fontSize = FontSize;
            s.color = TextColor;
            s.backgroundColor = ButtonColor;
            s.marginLeft = s.marginRight = 6f;
            s.marginTop = s.marginBottom = 4f;
            Border(button, Gold, 2f, 12f);
            return button;
        }

        private static void Border(VisualElement e, Color color, float width, float radius)
        {
            var s = e.style;
            s.borderLeftColor = s.borderRightColor = s.borderTopColor = s.borderBottomColor = color;
            s.borderLeftWidth = s.borderRightWidth = s.borderTopWidth = s.borderBottomWidth = width;
            s.borderTopLeftRadius = s.borderTopRightRadius = s.borderBottomLeftRadius = s.borderBottomRightRadius = radius;
        }

        private static void Pad(VisualElement e, float p)
        {
            var s = e.style;
            s.paddingLeft = s.paddingRight = s.paddingTop = s.paddingBottom = p;
        }

        // ------------------------------------------------------------ dev args

        private static string ReadArg()
        {
            var args = DevArgs.All;
            for (int i = 0; i < args.Length; i++)
                if (args[i] == ArgName)
                    return i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal) ? args[i + 1] : "";
            return null;
        }

        private IEnumerator OpenAfterLoad()
        {
            yield return new WaitForSecondsRealtime(0.5f);
            Open();
        }

        /// <summary>The acceptance flow through the panel's own controls: open, skill to 3 (slider), Rare chest (button), close.</summary>
        private IEnumerator Demo(string skillId)
        {
            yield return new WaitForSecondsRealtime(0.5f);
            Open();
            Debug.Log($"[DevDebugPanel] Demo open: paused={GamePause.IsPaused} timeScale={Time.timeScale}");
            yield return new WaitForSecondsRealtime(0.3f);
            var slider = SkillSlider(skillId);
            if (slider != null) slider.value = 3;
            int level = _manager != null ? _manager.GetLevel(skillId) : -1;
            Debug.Log($"[DevDebugPanel] Demo skill {skillId}: slider={(slider != null ? slider.value : -1)} level={level}");
            yield return new WaitForSecondsRealtime(0.3f);
            var dropper = FindAnyObjectByType<ChestDropper>();
            int before = dropper != null ? dropper.Active.Count : -1;
            DropChest(ChestTier.Rare);
            int after = dropper != null ? dropper.Active.Count : -1;
            var last = after > 0 ? dropper.Active[after - 1] : null;
            Debug.Log($"[DevDebugPanel] Demo chest: {before} -> {after} last={(last != null ? last.Tier.ToString() : "none")}");
            yield return new WaitForSecondsRealtime(0.4f);
            Close();
            Debug.Log($"[DevDebugPanel] Demo closed: paused={GamePause.IsPaused} timeScale={Time.timeScale}");
        }
    }
}
#endif
