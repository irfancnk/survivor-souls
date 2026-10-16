using System;
using UnityEngine;
using UnityEngine.UIElements;

namespace SurvivorSouls.UI.Hud
{
    /// <summary>
    /// Timings, sizes and thresholds of the medieval health bar (KAN-82). Serialized on <see cref="PlayerHealthHud"/>, so
    /// they can be tuned in the scene; colours live in GameHud.uss (<c>.hud-hp*</c>).
    /// </summary>
    [Serializable]
    public sealed class HealthBarFeel
    {
        [Tooltip("How fast the fill eases to the real value (1/s, exponential). The number counts with it.")]
        [Min(1f)] public float fillSharpness = 14f;
        [Tooltip("Seconds after the last damage before the ghost trail starts to drain.")]
        [Min(0f)] public float trailDelay = 0.4f;
        [Tooltip("Ghost trail drain speed (fraction of the bar per second).")]
        [Min(0.05f)] public float trailSpeed = 0.7f;
        [Tooltip("Damage is gathered into one feedback pulse per this many seconds (number, shards, shake): horde contact never spams.")]
        [Min(0.05f)] public float pulseInterval = 0.25f;
        [Tooltip("Heals are gathered into one '+N' per this many seconds (life steal hits often).")]
        [Min(0.05f)] public float healInterval = 0.4f;
        [Tooltip("Bar shake amplitude (px) and time (s) on a damage pulse.")]
        public float shakePixels = 4f;
        [Min(0.01f)] public float shakeTime = 0.15f;
        [Tooltip("White flash on the lost part (s).")]
        [Min(0.01f)] public float lostFlashTime = 0.3f;
        [Tooltip("Damage / heal number life (s) and drift (px, + = down).")]
        [Min(0.1f)] public float popTime = 0.6f;
        public float popDrift = 26f;
        [Tooltip("Shard life (s), gravity (px/s²); a pulse cracks off 2..4 shards, more for bigger hits.")]
        [Min(0.1f)] public float shardTime = 0.7f;
        public float shardGravity = 900f;
        [Tooltip("Damage share of max health for 3 shards / 4 shards.")]
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
        [Tooltip("Below this share: low health (heartbeat pulse, cracks glow). Below the critical share: faster, stronger.")]
        [Range(0f, 1f)] public float lowShare = 0.3f;
        [Range(0f, 1f)] public float criticalShare = 0.15f;
        [Min(0.1f)] public float heartbeatHz = 1.1f;
        [Min(0.1f)] public float criticalHeartbeatHz = 2f;
        [Tooltip("Track scale peak per heartbeat (low / critical).")]
        public float beatScale = 0.025f;
        public float criticalBeatScale = 0.045f;
        [Tooltip("Last flash on death (s).")]
        [Min(0.05f)] public float deathFlashTime = 0.6f;
    }

    /// <summary>
    /// The medieval player health bar (KAN-82) on the HUD document: crimson fill easing to the value with the number
    /// counting along ("180 / 240"), a pale ghost trail that drains after <see cref="HealthBarFeel.trailDelay"/>, a
    /// green-gold heal glow that fills ahead, a silver-blue shield segment, a white flash on the lost part, quarter ticks,
    /// red shards that crack off (pooled, max <see cref="MaxShards"/>), "-24" / "+12" pops (pooled), a bar shake and a
    /// heart medallion pulse. Damage is gathered into pulses (<see cref="HealthBarFeel.pulseInterval"/>), so horde
    /// contact never spams. Low / critical health: heartbeat pulse + glowing emblem cracks. Max health raise: the frame
    /// grows and the new part glows gold. Death: drains dark with a last flash.
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
        public const string LostName = "player-hp-lost";
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
        public const int MaxShards = 6;
        public const int MaxPops = 4;

        private readonly HealthBarFeel _feel;
        private readonly VisualElement _root, _track, _fill, _trail, _heal, _shield, _lost, _grow, _flash, _fx, _emblem, _cracks;
        private readonly Label _value;
        private readonly Shard[] _shards = new Shard[MaxShards];
        private readonly Pop[] _pops = new Pop[MaxPops];
        private readonly System.Random _random = new System.Random(82);

        private float _current, _max = 1f, _baseMax = 1f, _shieldValue;
        private float _shown = 1f, _trailShown = 1f, _healTarget = -1f, _healGlow;
        private float _time, _lastDamage = float.NegativeInfinity;
        private float _pending, _pendingFrom, _nextPulse;
        private float _pendingHeal, _nextHeal;
        private float _shake, _lostFlash, _lostFrom, _lostTo, _emblemPop, _growGlow, _growFrom, _deathFlash;
        private float _width = -1f, _beatPhase;
        private bool _dead;
        private int _nextShard, _nextPop;
        private string[] _numbers = Array.Empty<string>();
        private int _numbersMax = -1, _lastNumber = int.MinValue;
        // Last written values: an idle bar touches no style.
        private float _drawnFill = -1f, _drawnTrail = -1f, _drawnHeal = -1f, _drawnHealOpacity = -1f, _drawnShield = -1f;
        private float _drawnWidth = -1f, _drawnCracks = -1f, _drawnScale = -1f, _drawnEmblem = -1f, _drawnShake = float.NaN;
        private float _drawnLost = -1f, _drawnGrow = -1f, _drawnFlash = -1f;
        private int _drawnState = -1;

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

        /// <summary>Reduce screen effects (GameSettings, accessibility): no shake, shards, white flash or heartbeat scale.</summary>
        public bool ReduceEffects { get; set; }
        /// <summary>Fill fraction drawn now (eases to the value).</summary>
        public float Shown => _shown;
        /// <summary>Ghost trail fraction drawn now.</summary>
        public float TrailShown => _trailShown;
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
        /// <summary>Opacity of the white lost-part flash, the gold grow glow and the emblem cracks now.</summary>
        public float LostFlash => _drawnLost;
        public float GrowGlow => _drawnGrow;
        public float Cracks => _drawnCracks;
        /// <summary>Heal glow opacity written now.</summary>
        public float HealGlow => Mathf.Max(0f, _drawnHealOpacity);
        /// <summary>Shield segment width written now (fraction of the bar).</summary>
        public float ShieldShown => _drawnShield;
        /// <summary>Current bar shake offset (px).</summary>
        public float ShakeOffset => float.IsNaN(_drawnShake) ? 0f : _drawnShake;
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
            _lost = document.Q(LostName);
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
            _healTarget = -1f;
            _width = TargetWidth();
            _pending = _pendingHeal = 0f;
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
            _lastDamage = _time;
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

            // Fill + trail + heal glow.
            _shown += (target - _shown) * (1f - Mathf.Exp(-_feel.fillSharpness * dt));
            if (Mathf.Abs(target - _shown) < 0.0005f) _shown = target;
            if (_trailShown < _shown) _trailShown = _shown; // healing: the trail rides up with the fill
            else if (_time - _lastDamage > _feel.trailDelay)
                _trailShown = Mathf.MoveTowards(_trailShown, _shown, _feel.trailSpeed * dt);
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

            // Shield segment: on top of the red, clamped inside the bar.
            float shieldShare = Mathf.Clamp01(_shieldValue / _max);
            if (shieldShare != _drawnShield && _shield != null)
            {
                _drawnShield = shieldShare;
                float left = Mathf.Min(_shown, 1f - shieldShare);
                _shield.style.left = Length.Percent(left * 100f);
                _shield.style.width = Length.Percent(shieldShare * 100f);
            }

            UpdateNumber();
            UpdateWidth(dt);
            UpdateFlashes(dt);
            UpdateHeartbeat(dt, target);
            UpdateShards(dt);
            UpdatePops(dt);
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
            if (ReduceEffects) return;
            _shake = 1f;
            _lostFrom = to;
            _lostTo = Mathf.Max(from, to);
            _lostFlash = 1f;
            float share = lost / _max;
            int count = share >= _feel.shards4Share ? 4 : share >= _feel.shards3Share ? 3 : 2;
            for (int i = 0; i < count; i++) SpawnShard(Mathf.Lerp(to, _lostTo, (i + 0.5f) / count));
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
                p.Label.style.translate = new Translate(0f, 58f + drift * k);
                p.Label.style.scale = new Scale(new Vector2(scale, scale));
                p.Label.style.opacity = k < 0.6f ? 1f : 1f - (k - 0.6f) / 0.4f;
            }
        }

        private void UpdateFlashes(float dt)
        {
            // White flash on the lost part.
            _lostFlash = Mathf.MoveTowards(_lostFlash, 0f, dt / _feel.lostFlashTime);
            if (_lostFlash > 0f && _lost != null)
            {
                _lost.style.left = Length.Percent(_lostFrom * 100f);
                _lost.style.width = Length.Percent(Mathf.Max(0f, _lostTo - _lostFrom) * 100f);
            }
            SetOpacity(_lost, ref _drawnLost, _lostFlash * _lostFlash);

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

            // Shake (horizontal, decaying).
            _shake = Mathf.MoveTowards(_shake, 0f, dt / _feel.shakeTime);
            float offset = _shake > 0f ? Mathf.Sin(_time * 90f) * _feel.shakePixels * _shake : 0f;
            if (offset != _drawnShake && _root != null)
            {
                _drawnShake = offset;
                _root.style.translate = new Translate(offset, 0f);
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
            int state = _dead ? 3 : share < _feel.criticalShare ? 2 : share < _feel.lowShare ? 1 : 0;
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
                float depth = state == 2 ? 1f : Mathf.Clamp01(1f - (share - _feel.criticalShare) / Mathf.Max(0.01f, _feel.lowShare - _feel.criticalShare)) * 0.6f + 0.4f;
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
            if (_shown >= Mathf.Clamp01(_current / _max) && !_dead) n = Mathf.CeilToInt(_current - 0.001f); // settled: exact
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
