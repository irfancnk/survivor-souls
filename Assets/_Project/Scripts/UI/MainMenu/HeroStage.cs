using System.Collections.Generic;
using SurvivorSouls.Core;
using SurvivorSouls.Gameplay.Heroes;
using UnityEngine;
using UnityEngine.Rendering;

namespace SurvivorSouls.UI.MainMenu
{
    /// <summary>
    /// The 3D hero stage of the hero page (KAN-244): a dark ruin with a stone floor, torch key light, cool rim, embers and
    /// fog, far from the bonfire scene (MainMenuSceneBuilder makes it, inactive). Its camera renders into
    /// <see cref="Target"/>, which <see cref="HeroSelectScreen"/> shows full screen behind the page text, so the page
    /// fades in and out like every other page. One hero stands on the floor at a time: <see cref="Show"/> slides the old
    /// one out and the new one in; <see cref="HeroStageSpin"/> turns him with a drag / flick and back to the front; a
    /// <see cref="HeroStageLife"/> on each model plays the idle variants and the pick <see cref="Taunt"/> (flare light +
    /// the hero's taunt sound). Locked heroes stand in dimmed light. While the page is closed the stage object is off
    /// (camera, models, particles); while it is fully open the menu camera is off (it is hidden under the page).
    /// Models: <see cref="HeroDefinition.ModelPrefab"/>, or the Knight's "Model" of <see cref="knightPrefab"/>; the clone
    /// keeps only renderers + Animator (gameplay scripts are stripped before they wake). All roster heroes spawn on the
    /// first open (no hitch on a swipe); only the shown one (and the one sliding out) is active.
    /// </summary>
    public sealed class HeroStage : MonoBehaviour
    {
        [SerializeField] private Camera stageCamera;
        [Tooltip("The bonfire camera: off while the page fully covers it.")]
        [SerializeField] private Camera menuCamera;
        [Tooltip("Floor centre: the hero stands here.")]
        [SerializeField] private Transform heroAnchor;
        [SerializeField] private Light keyLight;
        [SerializeField] private Light rimLight;
        [SerializeField] private Light fillLight;
        [Tooltip("Gold light over the hero, follows the taunt glow.")]
        [SerializeField] private Light flareLight;
        [SerializeField] private AudioSource voice;
        [Tooltip("Player prefab whose 'Model' child is the Knight (heroes without their own model).")]
        [SerializeField] private GameObject knightPrefab;
        [SerializeField] private HeroStageSpinFeel spin = new HeroStageSpinFeel();
        [Tooltip("Fog density while the stage camera renders (the bonfire camera keeps the scene value).")]
        [SerializeField, Min(0f)] private float stageFog = 0.028f;
        [Tooltip("Key / rim / fill light share for a locked hero.")]
        [SerializeField, Range(0f, 1f)] private float lockedLight = 0.38f;
        [SerializeField, Min(0.05f)] private float lightBlendTime = 0.25f;
        [Tooltip("Flare light intensity at the taunt peak.")]
        [SerializeField, Min(0f)] private float flareIntensity = 5f;
        [Tooltip("Seconds and metres of the hero swap slide.")]
        [SerializeField, Min(0.05f)] private float swapTime = 0.35f;
        [SerializeField, Min(0.1f)] private float swapDistance = 2.4f;
        [Tooltip("Seconds after Open before the menu camera turns off (the page has faded in by then).")]
        [SerializeField, Min(0f)] private float menuCameraOffDelay = 0.3f;
        [Tooltip("Longest side of the render texture (px): 1920 keeps a 3x iPhone sharp enough at a quarter of the cost.")]
        [SerializeField, Min(256)] private int maxTextureSide = 1920;

        private sealed class Slot
        {
            public HeroDefinition Hero;
            public Transform Pivot;
            public HeroStageLife Life;
            public Animator Animator;
        }

        private readonly List<Slot> _slots = new List<Slot>();
        private readonly HashSet<HeroDefinition> _tauntedOnce = new HashSet<HeroDefinition>();
        private HeroStageSpin _spin;
        private Slot _shown, _leaving;
        private float _swapT = 1f, _swapDir;
        private float _keyBase, _rimBase, _fillBase, _lightShare = 1f, _lightGoal = 1f;
        private float _menuCameraOffIn = -1f, _tauntIn = -1f;
        private float _savedFog = -1f;
        private RenderTexture _target;
        private bool _open, _basesRead;
        private int _seed = 244;

        public bool IsOpen => _open;
        /// <summary>What the stage camera renders (null while closed).</summary>
        public RenderTexture Target => _target;
        /// <summary>Hero on the floor now (null before the first <see cref="Show"/>).</summary>
        public HeroDefinition Shown => _shown?.Hero;
        /// <summary>The shown hero's model pivot / life / animator (tests, screenshots).</summary>
        public Transform ShownPivot => _shown?.Pivot;
        public HeroStageLife ShownLife => _shown?.Life;
        public Animator ShownAnimator => _shown?.Animator;
        public HeroStageSpin Spin => _spin ??= new HeroStageSpin(spin);
        public Camera StageCamera => stageCamera;
        public Camera MenuCamera => menuCamera;
        public Light FlareLight => flareLight;
        public Light KeyLight => keyLight;
        /// <summary>True while a hero swap slides.</summary>
        public bool Swapping => _swapT < 1f;
        /// <summary>Models spawned so far (tests: every roster hero after the first open).</summary>
        public int SpawnedCount => _slots.Count;
        /// <summary>Active hero models now (tests: one at rest, two while swapping).</summary>
        public int ActiveModels
        {
            get
            {
                int n = 0;
                foreach (var s in _slots) if (s.Pivot.gameObject.activeSelf) n++;
                return n;
            }
        }

        /// <summary>Code-built stages (tests).</summary>
        public void Setup(Camera stage, Camera menu, Transform anchor, Light key, Light rim, Light fill, Light flare, AudioSource sound, GameObject knight)
        {
            stageCamera = stage;
            menuCamera = menu;
            heroAnchor = anchor;
            keyLight = key;
            rimLight = rim;
            fillLight = fill;
            flareLight = flare;
            voice = sound;
            knightPrefab = knight;
        }

        /// <summary>Turns the stage on and spawns every roster hero once (hidden). Call before <see cref="Show"/>.</summary>
        public void Open(HeroRoster roster)
        {
            ReadLightBases();
            _open = true;
            gameObject.SetActive(true);
            EnsureTarget();
            if (stageCamera != null)
            {
                stageCamera.targetTexture = _target;
                stageCamera.enabled = true;
            }
            if (roster != null)
                foreach (var hero in roster.Heroes)
                    if (hero != null) SlotOf(hero);
            _menuCameraOffIn = menuCameraOffDelay;
            _tauntedOnce.Clear();
        }

        /// <summary>Stage off, menu camera back on. Models stay spawned (inactive) for the next open.</summary>
        public void Close()
        {
            if (!_open) return;
            _open = false;
            _menuCameraOffIn = -1f;
            _tauntIn = -1f;
            if (menuCamera != null) menuCamera.enabled = true;
            if (stageCamera != null)
            {
                stageCamera.enabled = false;
                stageCamera.targetTexture = null;
            }
            if (_target != null)
            {
                _target.Release();
                Destroy(_target);
                _target = null;
            }
            _leaving = null;
            _swapT = 1f;
            foreach (var s in _slots) s.Pivot.gameObject.SetActive(false);
            _shown = null;
            RestoreFog();
            gameObject.SetActive(false);
        }

        /// <summary>
        /// Puts <paramref name="hero"/> on the floor. <paramref name="direction"/> +1 = he comes from the right (next),
        /// -1 = from the left, 0 = at once. <paramref name="locked"/> dims the lights.
        /// </summary>
        public void Show(HeroDefinition hero, int direction, bool locked)
        {
            _lightGoal = locked ? lockedLight : 1f;
            if (hero == null) return;
            var slot = SlotOf(hero);
            if (slot == null) return;
            if (slot == _shown) return; // same hero: only the light blends (a buy, Test Mode)
            if (_leaving != null) _leaving.Pivot.gameObject.SetActive(false);
            _leaving = direction != 0 ? _shown : null;
            if (_leaving == null && _shown != null) _shown.Pivot.gameObject.SetActive(false);
            _shown = slot;
            slot.Pivot.gameObject.SetActive(true);
            slot.Life.ResetToIdle();
            Spin.Snap(0f);
            _tauntIn = -1f;
            if (direction == 0 || _leaving == null)
            {
                _swapT = 1f;
                _lightShare = _lightGoal;
                Place(slot, 0f);
            }
            else
            {
                _swapT = 0f;
                _swapDir = Mathf.Sign(direction);
                Place(slot, _swapDir * swapDistance);
            }
            ApplyLights();
            Pose(slot, 0f);
        }

        /// <summary>The shown hero taunts after <paramref name="delay"/> s (the page reveal), with flare and sound.</summary>
        public void Taunt(float delay = 0f)
        {
            if (_shown == null) return;
            _tauntIn = Mathf.Max(0f, delay);
            if (_tauntIn == 0f) StartTaunt();
        }

        /// <summary>The shown hero taunts once per open (the first time the page shows him picked).</summary>
        public void TauntOnce(float delay = 0f)
        {
            if (_shown == null || !_tauntedOnce.Add(_shown.Hero)) return;
            Taunt(delay);
        }

        public void BeginDrag() => Spin.BeginDrag();
        public void Drag(float panelPixels) => Spin.Drag(panelPixels);
        public void EndDrag() => Spin.EndDrag();

        private void StartTaunt()
        {
            _tauntIn = -1f;
            _shown?.Life.Taunt();
        }

        private void OnEnable() => RenderPipelineManager.beginCameraRendering += BeginCamera;

        private void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering -= BeginCamera;
            RestoreFog();
        }

        private void OnDestroy()
        {
            if (_target != null)
            {
                _target.Release();
                Destroy(_target);
            }
            if (menuCamera != null) menuCamera.enabled = true;
        }

        private void Update() => Tick(Time.deltaTime);

        /// <summary>Advances swap, turn, lights and timers (Update; tests call it with their own step).</summary>
        public void Tick(float dt)
        {
            if (!_open) return;
            if (_menuCameraOffIn >= 0f)
            {
                _menuCameraOffIn -= dt;
                if (_menuCameraOffIn < 0f && menuCamera != null) menuCamera.enabled = false;
            }
            if (_tauntIn > 0f)
            {
                _tauntIn -= dt;
                if (_tauntIn <= 0f) StartTaunt();
            }
            if (_target != null && (_target.width != TextureSize().x || _target.height != TextureSize().y)) EnsureTarget();

            if (_swapT < 1f)
            {
                _swapT = Mathf.Min(1f, _swapT + dt / swapTime);
                float k = _swapT * _swapT * (3f - 2f * _swapT);
                if (_shown != null) Place(_shown, _swapDir * swapDistance * (1f - k));
                if (_leaving != null)
                {
                    Place(_leaving, -_swapDir * swapDistance * k);
                    if (_swapT >= 1f)
                    {
                        _leaving.Pivot.gameObject.SetActive(false);
                        _leaving = null;
                    }
                }
            }

            Spin.Tick(dt);
            _lightShare = Mathf.MoveTowards(_lightShare, _lightGoal, dt / lightBlendTime);
            ApplyLights();
            if (_shown != null) Pose(_shown, Spin.Yaw);
        }

        private void Pose(Slot slot, float yaw)
        {
            // 180: the model faces +Z, the camera looks at the floor from -Z.
            slot.Pivot.localRotation = Quaternion.Euler(0f, 180f + yaw + slot.Life.BodyYaw, 0f);
        }

        private void Place(Slot slot, float x) => slot.Pivot.localPosition = new Vector3(x, 0f, 0f);

        private void ApplyLights()
        {
            if (!_basesRead) return;
            if (keyLight != null) keyLight.intensity = _keyBase * _lightShare;
            if (rimLight != null) rimLight.intensity = _rimBase * Mathf.Lerp(0.7f, 1f, _lightShare);
            if (fillLight != null) fillLight.intensity = _fillBase * _lightShare;
            if (flareLight != null)
            {
                float flare = _shown != null ? _shown.Life.Flare : 0f;
                flareLight.intensity = flareIntensity * flare;
                flareLight.enabled = flare > 0.001f;
            }
        }

        private void ReadLightBases()
        {
            if (_basesRead) return;
            _basesRead = true;
            _keyBase = keyLight != null ? keyLight.intensity : 0f;
            _rimBase = rimLight != null ? rimLight.intensity : 0f;
            _fillBase = fillLight != null ? fillLight.intensity : 0f;
        }

        private Slot SlotOf(HeroDefinition hero)
        {
            foreach (var s in _slots)
                if (s.Hero == hero) return s;
            var source = hero.ModelPrefab != null ? hero.ModelPrefab : KnightModel();
            if (source == null)
            {
                Debug.LogWarning($"[HeroStage] no model for '{hero.Id}'");
                return null;
            }
            var parent = heroAnchor != null ? heroAnchor : transform;
            var pivot = new GameObject("Hero_" + hero.Id).transform;
            pivot.SetParent(parent, false);
            pivot.gameObject.SetActive(false);
            // Clone under the inactive pivot: no gameplay script wakes; strip them, keep renderers + Animator.
            var model = Instantiate(source, pivot, false);
            model.name = "Model";
            foreach (var mb in model.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(mb);
            var animator = model.GetComponentInChildren<Animator>(true);
            if (animator != null)
            {
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                animator.applyRootMotion = false;
            }
            var life = model.AddComponent<HeroStageLife>();
            life.Setup(animator, _seed++);
            var clips = hero.TauntClips;
            life.TauntStarted += () => PlayTaunt(clips);
            var slot = new Slot { Hero = hero, Pivot = pivot, Life = life, Animator = animator };
            _slots.Add(slot);
            return slot;
        }

        private GameObject KnightModel()
        {
            if (knightPrefab == null) return null;
            var model = knightPrefab.transform.Find(HeroLoadout.ModelName);
            return model != null ? model.gameObject : knightPrefab;
        }

        private void PlayTaunt(AudioClip[] clips)
        {
            if (voice == null || clips == null) return;
            foreach (var clip in clips)
                if (clip != null) voice.PlayOneShot(clip, GameSettings.SfxVolume);
        }

        private Vector2Int TextureSize()
        {
            int w = Mathf.Max(64, Screen.width), h = Mathf.Max(64, Screen.height);
            float scale = Mathf.Min(1f, maxTextureSide / (float)Mathf.Max(w, h));
            return new Vector2Int(Mathf.Max(64, Mathf.RoundToInt(w * scale)), Mathf.Max(64, Mathf.RoundToInt(h * scale)));
        }

        private void EnsureTarget()
        {
            var size = TextureSize();
            if (_target != null && _target.width == size.x && _target.height == size.y) return;
            if (_target != null)
            {
                if (stageCamera != null && stageCamera.targetTexture == _target) stageCamera.targetTexture = null;
                _target.Release();
                Destroy(_target);
            }
            _target = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32)
            {
                name = "HeroStage",
                antiAliasing = 2, // cheap on tile GPUs, clean hero edges
                useMipMap = false,
            };
            _target.Create();
            if (stageCamera != null && _open) stageCamera.targetTexture = _target;
            TargetChanged?.Invoke(_target);
        }

        /// <summary>Raised when the render texture is (re)made: the page shows the new one.</summary>
        public event System.Action<RenderTexture> TargetChanged;

        private void BeginCamera(ScriptableRenderContext context, Camera camera)
        {
            // Thinner fog for the stage camera only: the hero stays crisp, the far ruins fade.
            if (camera == stageCamera)
            {
                if (_savedFog < 0f) _savedFog = RenderSettings.fogDensity;
                RenderSettings.fogDensity = stageFog;
            }
            else RestoreFog();
        }

        private void RestoreFog()
        {
            if (_savedFog < 0f) return;
            RenderSettings.fogDensity = _savedFog;
            _savedFog = -1f;
        }
    }
}
