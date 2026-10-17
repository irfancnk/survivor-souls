using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>Health bar colour by health share (KAN-243).</summary>
    public enum HealthColour
    {
        Green,
        Orange,
        Red,
    }

    /// <summary>
    /// Timings, sizes and thresholds of the medieval health bar (KAN-82, colours + chunk + shake KAN-243). Serialized on
    /// <see cref="PlayerHealthHud"/>, so they can be tuned in the scene. Fields added after the Game scene was built take
    /// these defaults until the next scene rebuild writes them.
    /// </summary>
    [Serializable]
    public sealed class HealthBarFeel
    {
        [Tooltip("How fast the fill eases up on a heal (1/s, exponential). Damage drops the fill at once (KAN-243).")]
        [Min(1f)] public float fillSharpness = 14f;
        [Tooltip("Damage is gathered into one feedback pulse per this many seconds (number, shards, shake): horde contact never spams.")]
        [Min(0.05f)] public float pulseInterval = 0.25f;
        [Tooltip("Heals are gathered into one '+N' per this many seconds (life steal hits often).")]
        [Min(0.05f)] public float healInterval = 0.4f;

        [Header("Colour by health (KAN-243)")]
        [Tooltip("Below this share the fill turns from green to orange.")]
        [Range(0f, 1f)] public float orangeBelow = 0.5f;
        [Tooltip("Below this share the fill turns red, and the heartbeat is the fast, strong one (critical).")]
        [Range(0f, 1f)] public float redBelow = 0.2f;
        [Tooltip("Hysteresis band (share of max health): a threshold is crossed back only this far past it, so horde contact never flickers the colour.")]
        [Range(0f, 0.2f)] public float colourHysteresis = 0.02f;
        [Tooltip("Seconds of the colour blend when the state changes.")]
        [Min(0.01f)] public float colourBlendTime = 0.15f;
        public Color green = new Color(0.36f, 0.84f, 0.38f);
        public Color orange = new Color(1f, 0.58f, 0.14f);
        public Color red = new Color(0.95f, 0.16f, 0.11f);

        [Header("Damage chunk (KAN-243)")]
        [Tooltip("Seconds the white chunk (the health a hit took) holds before it drains. A new hit restarts the hold.")]
        [Min(0f)] public float chunkHold = 0.35f;
        [Tooltip("Seconds the white chunk takes to drain to the fill (ease-out).")]
        [Min(0.05f)] public float chunkDrain = 0.3f;

        [Header("Hit shake (KAN-243)")]
        [Tooltip("Bar shake (px) for a small hit (share <= shards3Share) and a big hit (share >= shards4Share); lerp between.")]
        public float shakeSmall = 6f;
        public float shakeBig = 12f;
        [Tooltip("Vertical part of the shake (px at full strength).")]
        public float shakeVertical = 2f;
        [Tooltip("Seconds the shake decays over.")]
        [Min(0.01f)] public float hitShakeTime = 0.25f;

        [Header("Pops, shards, glows")]
        [Tooltip("Damage / heal number life (s) and drift (px, + = down).")]
        [Min(0.1f)] public float popTime = 0.6f;
        public float popDrift = 26f;
        [Tooltip("Shard life (s), gravity (px/s²); a pulse cracks off 2..4 shards, more for bigger hits.")]
        [Min(0.1f)] public float shardTime = 0.7f;
        public float shardGravity = 900f;
        [Tooltip("Damage share of max health for 3 shards / 4 shards (also the small / big hit shake).")]
        [Range(0f, 1f)] public float shards3Share = 0.05f;
        [Range(0f, 1f)] public float shards4Share = 0.12f;
        [Tooltip("Emblem pulse on a hit or heal (scale peak, seconds).")]
        public float emblemPop = 1.18f;
        [Min(0.01f)] public float emblemPopTime = 0.25f;
        [Tooltip("Heal glow fade (s) after the fill catches up.")]
        [Min(0.05f)] public float healGlowFade = 0.5f;
        [Tooltip("Gold glow on new max health (s).")]
        [Min(0.1f)] public float growGlowTime = 1.3f;
        [Tooltip("Bar max width (px) at base max health, and at the top of the growth range.")]
        public float baseWidth = 560f;
        public float maxWidth = 660f;
        [Tooltip("Max health at this multiple of base (or more) gives the full width.")]
        [Min(1.01f)] public float fullWidthAt = 1.5f;
        [Tooltip("Bar width ease (1/s).")]
        [Min(0.5f)] public float widthSharpness = 6f;
        [Tooltip("Below this share: low health (heartbeat pulse, cracks glow). Below redBelow: critical (faster, stronger).")]
        [Range(0f, 1f)] public float lowShare = 0.3f;
        [Min(0.1f)] public float heartbeatHz = 1.1f;
        [Min(0.1f)] public float criticalHeartbeatHz = 2f;
        [Tooltip("Track scale peak per heartbeat (low / critical).")]
        public float beatScale = 0.025f;
        public float criticalBeatScale = 0.045f;
        [Tooltip("Last flash on death (s).")]
        [Min(0.05f)] public float deathFlashTime = 0.6f;

        /// <summary>The colour of <paramref name="state"/>.</summary>
        public Color ColourOf(HealthColour state) => state switch
        {
            HealthColour.Green => green,
            HealthColour.Orange => orange,
            _ => red,
        };
    }

    /// <summary>
    /// The medieval player health bar (KAN-82) on the HUD document. KAN-243: the fill is green above
    /// <see cref="HealthBarFeel.orangeBelow"/>, orange down to <see cref="HealthBarFeel.redBelow"/>, red below (a neutral
    /// gradient tinted in code, blended over <see cref="HealthBarFeel.colourBlendTime"/>, with a hysteresis band so horde
    /// contact never flickers it); a hit drops the fill at once and the health it took shows as a solid white chunk that
    /// holds <see cref="HealthBarFeel.chunkHold"/> s, then drains to the fill (a new hit restarts the hold, the chunk never
    /// jumps back right); every damage pulse shakes the bar, 6 px for a small hit up to 12 px for a big one. Kept from
    /// KAN-82: the number counting with the fill ("180 / 240"), the green-gold heal glow that fills ahead, the silver-blue
    /// shield segment, quarter ticks, red shards that crack off (pooled, max <see cref="MaxShards"/>), "-24" / "+12" pops
    /// (pooled), the heart medallion pulse, low / critical heartbeat + glowing emblem cracks (critical = red), the gold
    /// grow glow on a max raise and the dark death drain. Damage is gathered into pulses
    /// (<see cref="HealthBarFeel.pulseInterval"/>), so horde contact never spams. "Reduce screen effects": no shake,
    /// shards, death flash or heartbeat scale; the white chunk stays (it is information).
    /// Plain class: <see cref="PlayerHealthHud"/> feeds it events and calls <see cref="Tick"/>; tests drive it directly.
    /// Styles are written only while something moves; no allocation per frame (number strings cached per max health).
    /// </summary>
    public sealed class HealthBarView
    {
        public const string RootName = "player-hp";
        public const string TrackName = "player-hp-track";
        public const string FillName = "player-hp-fill";
        public const string TrailName = "player-hp-trail";
        public const string HealName = "player-hp-heal";
        public const string ShieldName = "player-hp-shield";
        public const string GrowName = "player-hp-grow";
        public const string FlashName = "player-hp-flash";
        public const string ValueName = "player-hp-value";
        public const string FxName = "player-hp-fx";
        public const string EmblemName = "player-hp-emblem";
        public const string CracksName = "player-hp-cracks";
        public const string ShardClass = "hud-hp__shard";
        public const string PopClass = "hud-hp__pop";
        public const string PopHealClass = "hud-hp__pop--heal";
        public const string LowClass = "hud-hp--low";
        public const string CriticalClass = "hud-hp--critical";
        public const string DeadClass = "hud-hp--dead";
        /// <summary>Colour state classes on the root (KAN-243), index = <see cref="HealthColour"/>.</summary>
        public static readonly string[] ColourClasses = { "hud-hp--green", "hud-hp--orange", "hud-hp--red" };
        public const int MaxShards = 6;
        public const int MaxPops = 4;

        private readonly HealthBarFeel _feel;
        private readonly VisualElement _root, _track, _fill, _trail, _heal, _shield, _grow, _flash, _fx, _emblem, _cracks;
        private readonly Label _value;
        private readonly Shard[] _shards = new Shard[MaxShards];
        private readonly Pop[] _pops = new Pop[MaxPops];
        private readonly System.Random _random = new System.Random(82);

        private float _current, _max = 1f, _baseMax = 1f, _shieldValue;
        private float _shown = 1f, _trailShown = 1f, _healTarget = -1f, _healGlow;
        private float _time, _lastDamage = float.NegativeInfinity, _drainT = -1f, _drainFrom;
        private float _pending, _pendingFrom, _nextPulse;
        private float _pendingHeal, _nextHeal;
        private float _shake, _shakeAmp, _emblemPop, _growGlow, _growFrom, _deathFlash;
        private float _width = -1f, _beatPhase;
        private HealthColour _colourState;
        private bool _colourSet;
        private Color _colour, _colourFrom;
        private float _colourBlend = 1f;
        private bool _dead;
        private int _nextShard, _nextPop;
        private string[] _numbers = Array.Empty<string>();
        private int _numbersMax = -1, _lastNumber = int.MinValue;
        // Last written values: an idle bar touches no style.
        private float _drawnFill = -1f, _drawnTrail = -1f, _drawnHeal = -1f, _drawnHealOpacity = -1f, _drawnShield = -1f;
        private float _drawnWidth = -1f, _drawnCracks = -1f, _drawnScale = -1f, _drawnEmblem = -1f;
        private Vector2 _drawnShake = new Vector2(float.NaN, float.NaN);
        private float _drawnGrow = -1f, _drawnFlash = -1f;
        private Color _drawnColour = new Color(-1f, -1f, -1f, -1f);
        private int _drawnState = -1, _drawnColourState = -1;

        private struct Shard
        {
            public VisualElement Element;
            public float Age, X, Y, Vx, Vy, Spin, Angle;
            public bool Alive;
        }

        private struct Pop
        {
            public Label Label;
            public float Age;
            public bool Alive;
        }

        /// <summary>Reduce screen effects (GameSettings, accessibility): no shake, shards, death flash or heartbeat scale. The white chunk stays.</summary>
        public bool ReduceEffects { get; set; }
        /// <summary>Fill fraction drawn now (drops at once on damage, eases up on a heal).</summary>
        public float Shown => _shown;
        /// <summary>Right edge of the white chunk now (fraction of the bar); equals <see cref="Shown"/> when there is no chunk.</summary>
        public float TrailShown => _trailShown;
        /// <summary>Width of the white chunk now (fraction of the bar): the health the last hits took, before it drains.</summary>
        public float Chunk => Mathf.Max(0f, _trailShown - _shown);
        /// <summary>True while the chunk drains (its hold is over).</summary>
        public bool ChunkDraining => _drainT >= 0f;
        /// <summary>Text on the bar now ("180 / 240").</summary>
        public string Text => _value?.text;
        /// <summary>Shards / pops alive now (tests).</summary>
        public int ShardsAlive { get { int n = 0; foreach (var s in _shards) if (s.Alive) n++; return n; } }
        public int PopsAlive { get { int n = 0; foreach (var p in _pops) if (p.Alive) n++; return n; } }
        /// <summary>Text of the newest pop ("-24" / "+12"), null before the first.</summary>
        public string LastPopText { get; private set; }
        /// <summary>Damage pulses fired so far (tests: horde contact gathers into few pulses).</summary>
        public int Pulses { get; private set; }
        /// <summary>Bar max width written now (px).</summary>
        public float Width => _width;
        /// <summary>Opacity of the gold grow glow and the emblem cracks now.</summary>
        public float GrowGlow => _drawnGrow;
        public float Cracks => _drawnCracks;
        /// <summary>Heal glow opacity written now.</summary>
        public float HealGlow => Mathf.Max(0f, _drawnHealOpacity);
        /// <summary>Shield segment width written now (fraction of the bar).</summary>
        public float ShieldShown => _drawnShield;
        /// <summary>Current horizontal bar shake offset (px).</summary>
        public float ShakeOffset => float.IsNaN(_drawnShake.x) ? 0f : _drawnShake.x;
        /// <summary>Peak shake (px) of the last damage pulse (6 small .. 12 big; 0 with Reduce Effects).</summary>
        public float ShakeAmplitude => _shakeAmp;
        /// <summary>Colour state now (with hysteresis).</summary>
        public HealthColour ColourState => _colourState;
        /// <summary>Fill tint written now (blends between the state colours).</summary>
        public Color FillColour => _colour;
        public bool IsLow => _drawnState >= 1;
        public bool IsCritical => _drawnState >= 2;

        public HealthBarView(VisualElement document, HealthBarFeel feel)
        {
            _feel = feel ?? new HealthBarFeel();
            _root = document.Q(RootName);
            _track = document.Q(TrackName);
            _fill = document.Q(FillName);
            _trail = document.Q(TrailName);
            _heal = document.Q(HealName);
            _shield = document.Q(ShieldName);
            _grow = document.Q(GrowName);
            _flash = document.Q(FlashName);
            _value = document.Q<Label>(ValueName);
            _fx = document.Q(FxName);
            _emblem = document.Q(EmblemName);
            _cracks = document.Q(CracksName);
            for (int i = 0; i < MaxShards; i++)
            {
                var e = new VisualElement { pickingMode = PickingMode.Ignore };
                e.AddToClassList(ShardClass);
                e.style.display = DisplayStyle.None;
                _fx?.Add(e);
                _shards[i].Element = e;
            }
            for (int i = 0; i < MaxPops; i++)
            {
                var l = new Label { pickingMode = PickingMode.Ignore };
                l.AddToClassList(PopClass);
                l.style.display = DisplayStyle.None;
                _fx?.Add(l);
                _pops[i].Label = l;
            }
        }

        /// <summary>True when every element was found (the HUD document matches).</summary>
        public bool IsBound => _root != null && _fill != null && _trail != null && _value != null && _fx != null;

        /// <summary>Snaps to a value without effects (scene start, carried health).</summary>
        public void Reset(float current, float max, float baseMax, float shield = 0f)
        {
            _current = current;
            _max = Mathf.Max(1f, max);
            _baseMax = Mathf.Max(1f, baseMax);
            _shieldValue = Mathf.Max(0f, shield);
            _shown = _trailShown = Mathf.Clamp01(_current / _max);
            _drainT = -1f;
            _healTarget = -1f;
            _width = TargetWidth();
            _pending = _pendingHeal = 0f;
            _colourSet = false;
            UpdateColour(0f, _shown); // the right colour from the first frame, no blend
        }

        /// <summary>Health or max health changed (PlayerHealth.OnHealthChanged). Heals and max raises start their effects
        /// here; damage effects come from <see cref="Damaged"/> (exact amount).</summary>
        public void Changed(float current, float max)
        {
            max = Mathf.Max(1f, max);
            float maxGain = max - _max;
            float delta = current - _current;
            if (maxGain > 0.5f)
            {
                _growFrom = Mathf.Clamp01(_max / max);
                _growGlow = 1f;
                _shown *= _max / max; // the old fill keeps its pixel length while the frame grows
                _trailShown *= _max / max;
                _drainFrom *= _max / max;
                delta -= maxGain; // Archero rule: the gain also heals; that is not a heal tick
            }
            _current = current;
            _max = max;
            if (delta > 0.01f && !_dead)
            {
                _healTarget = Mathf.Clamp01(_current / _max);
                _healGlow = 1f;
                _pendingHeal += delta;
            }
        }

        /// <summary>Health was deducted (PlayerHealth.OnDamaged).</summary>
        public void Damaged(float amount)
        {
            if (amount <= 0f) return;
            _lastDamage = _time; // the chunk holds again (it never jumps back right: it keeps its edge)
            _drainT = -1f;
            if (_pending <= 0f) _pendingFrom = Mathf.Clamp01((_current + amount) / _max);
            _pending += amount;
            if (_time >= _nextPulse) FirePulse(); // the first hit pulses at once
        }

        /// <summary>Shield value changed (PlayerHealth.OnShieldChanged).</summary>
        public void ShieldChanged(float shield) => _shieldValue = Mathf.Max(0f, shield);

        /// <summary>Health reached 0: the bar goes dark with a last flash.</summary>
        public void Died()
        {
            _dead = true;
            _deathFlash = 1f;
            _healTarget = -1f;
        }

        /// <summary>Advances the bar by <paramref name="dt"/> seconds (scaled time: the bar rests in the level-up pause).</summary>
        public void Tick(float dt)
        {
            if (!IsBound) return;
            _time += dt;
            float target = Mathf.Clamp01(_current / _max);

            if (_pending > 0f && _time >= _nextPulse) FirePulse();
            if (_pendingHeal > 0f && _time >= _nextHeal) FireHeal();

            // Fill: damage drops at once (the chunk shows what went), a heal eases up.
            if (target < _shown) _shown = target;
            else
            {
                _shown += (target - _shown) * (1f - Mathf.Exp(-_feel.fillSharpness * dt));
                if (Mathf.Abs(target - _shown) < 0.0005f) _shown = target;
            }
            UpdateChunk(dt);
            if (_healTarget >= 0f)
            {
                if (_shown >= _healTarget - 0.002f || target < _healTarget - 0.002f)
                    _healGlow = Mathf.MoveTowards(_healGlow, 0f, dt / _feel.healGlowFade);
                if (_healGlow <= 0f) _healTarget = -1f;
            }
            float healWidth = _healTarget >= 0f ? Mathf.Max(_healTarget, _shown) : 0f;
            float healOpacity = _healTarget >= 0f ? _healGlow : 0f;

            SetWidth(_fill, ref _drawnFill, _shown);
            SetWidth(_trail, ref _drawnTrail, _trailShown);
            SetWidth(_heal, ref _drawnHeal, healWidth);
            SetOpacity(_heal, ref _drawnHealOpacity, healOpacity);

            // Shield segment: on top of the fill, clamped inside the bar.
            float shieldShare = Mathf.Clamp01(_shieldValue / _max);
            if (shieldShare != _drawnShield && _shield != null)
            {
                _drawnShield = shieldShare;
                float left = Mathf.Min(_shown, 1f - shieldShare);
                _shield.style.left = Length.Percent(left * 100f);
                _shield.style.width = Length.Percent(shieldShare * 100f);
            }

            UpdateColour(dt, target);
            UpdateNumber();
            UpdateWidth(dt);
            UpdateFlashes(dt);
            UpdateHeartbeat(dt, target);
            UpdateShards(dt);
            UpdatePops(dt);
        }

        /// <summary>
        /// White chunk = the trail element behind the fill, from the fill to <see cref="TrailShown"/>. It holds
        /// <see cref="HealthBarFeel.chunkHold"/> s after the last hit, then drains to the fill in
        /// <see cref="HealthBarFeel.chunkDrain"/> s (ease-out). A heal makes it ride up with the fill.
        /// </summary>
        private void UpdateChunk(float dt)
        {
            if (_trailShown <= _shown)
            {
                _trailShown = _shown;
                _drainT = -1f;
                return;
            }
            if (_time - _lastDamage < _feel.chunkHold) return;
            if (_drainT < 0f)
            {
                _drainT = 0f;
                _drainFrom = _trailShown;
            }
            _drainT = Mathf.Min(1f, _drainT + dt / _feel.chunkDrain);
            float k = 1f - (1f - _drainT) * (1f - _drainT);
            _trailShown = Mathf.Max(_shown, Mathf.Lerp(_drainFrom, _shown, k));
            if (_drainT >= 1f)
            {
                _trailShown = _shown;
                _drainT = -1f;
            }
        }

        /// <summary>Green / orange / red with a hysteresis band at each threshold; the tint blends to the new colour.</summary>
        private void UpdateColour(float dt, float share)
        {
            var state = ColourFor(share, _colourSet ? _colourState : (HealthColour?)null, _feel);
            if (!_colourSet)
            {
                _colourSet = true;
                _colourState = state;
                _colour = _feel.ColourOf(state);
                _colourBlend = 1f;
            }
            else if (state != _colourState)
            {
                _colourState = state;
                _colourFrom = _colour;
                _colourBlend = 0f;
            }
            if (_colourBlend < 1f)
            {
                _colourBlend = Mathf.Min(1f, _colourBlend + dt / _feel.colourBlendTime);
                _colour = Color.Lerp(_colourFrom, _feel.ColourOf(_colourState), _colourBlend);
            }
            else _colour = _feel.ColourOf(_colourState);
            if (_colour != _drawnColour && _fill != null)
            {
                _drawnColour = _colour;
                _fill.style.unityBackgroundImageTintColor = _colour;
            }
            if ((int)_colourState != _drawnColourState && _root != null)
            {
                _drawnColourState = (int)_colourState;
                for (int i = 0; i < ColourClasses.Length; i++) _root.EnableInClassList(ColourClasses[i], i == _drawnColourState);
            }
        }

        /// <summary>
        /// Colour for <paramref name="share"/>: green above <see cref="HealthBarFeel.orangeBelow"/>, orange down to
        /// <see cref="HealthBarFeel.redBelow"/>, red below. With a <paramref name="current"/> state, a threshold counts
        /// half the hysteresis band further away from it (drop below it by that much, or climb back above it by that much).
        /// </summary>
        public static HealthColour ColourFor(float share, HealthColour? current, HealthBarFeel feel)
        {
            float h = current.HasValue ? feel.colourHysteresis * 0.5f : 0f;
            int now = current.HasValue ? (int)current.Value : -1;
            float orangeEdge = feel.orangeBelow + (now < 0 ? 0f : now == 0 ? -h : h);
            float redEdge = feel.redBelow + (now < 0 ? 0f : now <= 1 ? -h : h);
            return share < redEdge ? HealthColour.Red : share < orangeEdge ? HealthColour.Orange : HealthColour.Green;
        }

        private void FirePulse()
        {
            float lost = _pending;
            _pending = 0f;
            _nextPulse = _time + _feel.pulseInterval;
            Pulses++;
            float from = _pendingFrom, to = Mathf.Clamp01(_current / _max);
            ShowPop(-Mathf.Max(1, Mathf.RoundToInt(lost)), false);
            _emblemPop = 1f;
            float share = lost / _max;
            if (ReduceEffects)
            {
                _shakeAmp = 0f;
                return;
            }
            float big = Mathf.InverseLerp(_feel.shards3Share, Mathf.Max(_feel.shards3Share + 1e-4f, _feel.shards4Share), share);
            _shakeAmp = Mathf.Lerp(_feel.shakeSmall, _feel.shakeBig, big);
            _shake = 1f;
            int count = share >= _feel.shards4Share ? 4 : share >= _feel.shards3Share ? 3 : 2;
            float top = Mathf.Max(from, to);
            for (int i = 0; i < count; i++) SpawnShard(Mathf.Lerp(to, top, (i + 0.5f) / count));
        }

        private void FireHeal()
        {
            float healed = _pendingHeal;
            _pendingHeal = 0f;
            _nextHeal = _time + _feel.healInterval;
            if (healed < 0.5f) return;
            ShowPop(Mathf.RoundToInt(healed), true);
            _emblemPop = 1f;
        }

        private void ShowPop(int amount, bool heal)
        {
            ref var pop = ref _pops[_nextPop];
            _nextPop = (_nextPop + 1) % MaxPops;
            string text = heal ? "+" + amount : amount.ToString(); // one string per pulse (<= 4 per second)
            LastPopText = text;
            pop.Label.text = text;
            pop.Label.EnableInClassList(PopHealClass, heal);
            pop.Age = 0f;
            pop.Alive = true;
            pop.Label.style.display = DisplayStyle.Flex;
            pop.Label.style.left = Length.Percent(Mathf.Clamp(_current / _max, 0.08f, 0.92f) * 100f);
            pop.Label.BringToFront();
        }

        private void SpawnShard(float at)
        {
            ref var s = ref _shards[_nextShard];
            _nextShard = (_nextShard + 1) % MaxShards; // the oldest gives way: never more than MaxShards alive
            float lane = _fx.resolvedStyle.width;
            float height = _fx.resolvedStyle.height;
            if (float.IsNaN(lane) || lane <= 0f) lane = 500f;
            if (float.IsNaN(height) || height <= 0f) height = 30f;
            s.X = at * lane;
            s.Y = height * (0.3f + 0.4f * (float)_random.NextDouble());
            s.Vx = ((float)_random.NextDouble() - 0.35f) * 160f;
            s.Vy = -120f - 140f * (float)_random.NextDouble();
            s.Spin = ((float)_random.NextDouble() - 0.5f) * 720f;
            s.Angle = (float)_random.NextDouble() * 90f;
            s.Age = 0f;
            s.Alive = true;
            s.Element.style.display = DisplayStyle.Flex;
        }

        private void UpdateShards(float dt)
        {
            for (int i = 0; i < MaxShards; i++)
            {
                ref var s = ref _shards[i];
                if (!s.Alive) continue;
                s.Age += dt;
                if (s.Age >= _feel.shardTime)
                {
                    s.Alive = false;
                    s.Element.style.display = DisplayStyle.None;
                    continue;
                }
                s.Vy += _feel.shardGravity * dt;
                s.X += s.Vx * dt;
                s.Y += s.Vy * dt;
                s.Angle += s.Spin * dt;
                float k = s.Age / _feel.shardTime;
                s.Element.style.translate = new Translate(s.X, s.Y);
                s.Element.style.rotate = new Rotate(s.Angle);
                s.Element.style.opacity = 1f - k * k;
            }
        }

        private void UpdatePops(float dt)
        {
            for (int i = 0; i < MaxPops; i++)
            {
                ref var p = ref _pops[i];
                if (!p.Alive) continue;
                p.Age += dt;
                if (p.Age >= _feel.popTime)
                {
                    p.Alive = false;
                    p.Label.style.display = DisplayStyle.None;
                    continue;
                }
                float k = p.Age / _feel.popTime;
                float drift = p.Label.ClassListContains(PopHealClass) ? -_feel.popDrift : _feel.popDrift;
                float scale = 1f + 0.35f * Mathf.Exp(-k * 10f);
                float side = (i & 1) == 0 ? -16f : 16f; // back-to-back pops (horde pulses) sit apart
                p.Label.style.translate = new Translate(side, 58f + drift * k);
                p.Label.style.scale = new Scale(new Vector2(scale, scale));
                p.Label.style.opacity = k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f;
            }
        }

        private void UpdateFlashes(float dt)
        {
            // Gold glow on the part a max raise added.
            _growGlow = Mathf.MoveTowards(_growGlow, 0f, dt / _feel.growGlowTime);
            if (_growGlow > 0f && _grow != null)
            {
                _grow.style.left = Length.Percent(_growFrom * 100f);
                _grow.style.width = Length.Percent((1f - _growFrom) * 100f);
            }
            SetOpacity(_grow, ref _drawnGrow, 0.85f * Mathf.Sin(Mathf.Clamp01(_growGlow) * Mathf.PI * 0.5f));

            // Death: a last flash, then the bar stays dark.
            _deathFlash = Mathf.MoveTowards(_deathFlash, 0f, dt / _feel.deathFlashTime);
            SetOpacity(_flash, ref _drawnFlash, ReduceEffects ? 0f : _deathFlash);

            // Shake: decaying, mostly sideways with a little vertical (KAN-243: 6..12 px by hit size).
            _shake = Mathf.MoveTowards(_shake, 0f, dt / _feel.hitShakeTime);
            var offset = _shake > 0f
                ? new Vector2(Mathf.Sin(_time * 90f) * _shakeAmp * _shake, Mathf.Sin(_time * 67f + 1.3f) * _feel.shakeVertical * _shake)
                : Vector2.zero;
            if ((offset.x != _drawnShake.x || offset.y != _drawnShake.y) && _root != null) // exact: Vector2 == is approximate
            {
                _drawnShake = offset;
                _root.style.translate = new Translate(offset.x, offset.y);
            }

            // Emblem pulse.
            _emblemPop = Mathf.MoveTowards(_emblemPop, 0f, dt / _feel.emblemPopTime);
            float e = 1f + (_feel.emblemPop - 1f) * Mathf.Sin(Mathf.Clamp01(_emblemPop) * Mathf.PI * 0.5f);
            if (e != _drawnEmblem && _emblem != null)
            {
                _drawnEmblem = e;
                _emblem.style.scale = new Scale(new Vector2(e, e));
            }
        }

        private void UpdateHeartbeat(float dt, float share)
        {
            int state = _dead ? 3 : share < _feel.redBelow ? 2 : share < _feel.lowShare ? 1 : 0;
            if (state != _drawnState && _root != null)
            {
                _drawnState = state;
                _root.EnableInClassList(LowClass, state == 1 || state == 2);
                _root.EnableInClassList(CriticalClass, state == 2);
                _root.EnableInClassList(DeadClass, state == 3);
            }
            float cracks = 0f, scale = 1f;
            if (state == 1 || state == 2)
            {
                float hz = state == 2 ? _feel.criticalHeartbeatHz : _feel.heartbeatHz;
                _beatPhase = (_beatPhase + dt * hz) % 1f;
                // Lub-dub: two short bumps per beat.
                float beat = Bump(_beatPhase, 0.08f, 0.07f) + 0.6f * Bump(_beatPhase, 0.3f, 0.07f);
                float depth = state == 2 ? 1f : Mathf.Clamp01(1f - (share - _feel.redBelow) / Mathf.Max(0.01f, _feel.lowShare - _feel.redBelow)) * 0.6f + 0.4f;
                cracks = Mathf.Clamp01(depth * (0.45f + 0.55f * beat));
                if (!ReduceEffects) scale = 1f + beat * (state == 2 ? _feel.criticalBeatScale : _feel.beatScale);
            }
            else _beatPhase = 0f;
            SetOpacity(_cracks, ref _drawnCracks, cracks);
            if (scale != _drawnScale && _track != null)
            {
                _drawnScale = scale;
                _track.style.scale = new Scale(new Vector2(scale, scale));
            }
        }

        private static float Bump(float phase, float at, float width)
        {
            float k = (phase - at) / width;
            return Mathf.Exp(-k * k);
        }

        private float TargetWidth()
        {
            float k = Mathf.Clamp01((_max / _baseMax - 1f) / (_feel.fullWidthAt - 1f));
            return Mathf.Lerp(_feel.baseWidth, _feel.maxWidth, k);
        }

        private void UpdateWidth(float dt)
        {
            float target = TargetWidth();
            if (_width < 0f) _width = target;
            _width += (target - _width) * (1f - Mathf.Exp(-_feel.widthSharpness * dt));
            if (Mathf.Abs(target - _width) < 0.5f) _width = target;
            if (Mathf.Abs(_width - _drawnWidth) < 0.25f || _root == null) return;
            _drawnWidth = _width;
            _root.style.maxWidth = _width;
        }

        private void UpdateNumber()
        {
            int max = Mathf.CeilToInt(_max);
            int n = _dead ? 0 : Mathf.Clamp(Mathf.CeilToInt(_shown * _max - 0.001f), 0, max);
            if (_shown == Mathf.Clamp01(_current / _max) && !_dead) n = Mathf.CeilToInt(_current - 0.001f); // settled: exact
            if (n == _lastNumber && max == _numbersMax) return;
            if (max != _numbersMax)
            {
                // "0 / 240" .. "240 / 240": built once per max health, no string per frame while the number counts.
                _numbersMax = max;
                _numbers = new string[max + 1];
                string tail = " / " + max;
                for (int i = 0; i <= max; i++) _numbers[i] = i + tail;
            }
            _lastNumber = n;
            _value.text = n >= 0 && n < _numbers.Length ? _numbers[n] : n + " / " + max;
        }

        private static void SetWidth(VisualElement e, ref float drawn, float share)
        {
            if (e == null || share == drawn) return;
            drawn = share;
            e.style.width = Length.Percent(share * 100f);
        }

        private static void SetOpacity(VisualElement e, ref float drawn, float opacity)
        {
            if (e == null || opacity == drawn) return;
            drawn = opacity;
            e.style.opacity = opacity;
        }
    }
}
