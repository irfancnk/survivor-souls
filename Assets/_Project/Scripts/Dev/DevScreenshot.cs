using System;
using System.Collections;
using System.Globalization;
using SurvivorSouls.Gameplay.CameraRig;
using SurvivorSouls.Gameplay.Combat;
using SurvivorSouls.Gameplay.Controls;
using SurvivorSouls.Gameplay.Player;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace SurvivorSouls.Dev
{
    /// <summary>
    /// Automation hook for development builds. Launch the player with
    /// <c>-ss-screenshot &lt;path.png&gt; [-ss-delay &lt;seconds&gt;] [-ss-quality &lt;name&gt;]</c>
    /// to save one frame and quit. -ss-quality picks a quality level by name (e.g. Mobile) to preview mobile visuals on desktop
    /// (the Mac dev build ships the Mobile level too, KAN-52; the active level + URP asset is logged).
    /// Game options: <c>-ss-scene &lt;name&gt;</c> loads a scene first; <c>-ss-move x,y</c> holds the joystick;
    /// <c>-ss-closeup &lt;yawDeg&gt;</c> frames the player up close (0 = from the front).
    /// <c>-ss-near-exit</c> moves the player in front of the exit door (open world) or the arena portal (boss Chamber, KAN-59).
    /// <c>-ss-pos x,z</c> moves the player to a world position first (e.g. the shore).
    /// Every capture also logs render stats of the last second: <c>[DevScreenshot] Stats ...</c> (draw calls, batches,
    /// SetPass calls, triangles, average frame ms).
    /// <c>-ss-player-hp &lt;0..1&gt;</c> sets the player's health fraction after load (0 = death screen).
    /// <c>-ss-counters &lt;kills,souls&gt;</c> sets the run counters after load (KAN-51: numbers on the death screen).
    /// <c>-ss-hit-marker &lt;hit|kill&gt;</c> pops a hit (or kill) marker on the nearest enemy just before the capture.
    /// <c>-ss-release &lt;seconds&gt;</c> lets go of the -ss-move stick that long before the capture (e.g. wind trail tail-off).
    /// <c>-ss-souls &lt;n&gt;</c> drops n soul wisps around the player after load (mostly small, some elite, one boss orb).
    /// <c>-ss-souls-pull &lt;seconds&gt;[,&lt;freeze&gt;]</c> that long after load, every soul crystal flies to the Knight
    /// (KAN-177 collect arc + trail); with a freeze time, time stops that long after the pull so a later -ss-delay capture
    /// shows the crystals mid-flight.
    /// <c>-ss-xp &lt;amount&gt;</c> grants that much XP <see cref="XpLead"/> s before the capture (level-up burst + skill cards).
    /// <c>-ss-chests &lt;n&gt;</c> drops n Reliquary chests (KAN-72) ahead of the player after load: Light, then Rare, ...
    /// <c>-ss-open-chest &lt;light|rare&gt;</c> drops that chest at the Knight's feet: the real walk-in pickup opens it
    /// (KAN-73) and the upgrade screen follows (cards: owned skills; force them with -ss-offer).
    /// <c>-ss-skills id,id,...</c> grants those skills (by SkillDefinition id, repeat an id to stack) after load;
    /// <c>id:N</c> puts that skill at level N (KAN-75 debug set level, 0 removes).
    /// <c>-ss-volley</c> prints the live volley list (KAN-83: Spread Shot / Double Shot bolts after -ss-skills) and the Double
    /// Shot stack odds (1000 simulated runs) to the log after load.
    /// <c>-ss-power-curve</c> prints the power curve (KAN-75: 200 simulated runs, levels 1-20, live base numbers) to the log after load.
    /// <c>-ss-upgrade id,id,...</c> chest-upgrades those owned skills (+1 each) 0.3 s before the capture: HUD slot glow + new pips (KAN-74).
    /// <c>-ss-offer id,id,id</c> makes the next level-up offer those cards (KAN-37 tier look; use with -ss-xp).
    /// <c>-ss-reroll &lt;n&gt;</c> (KAN-67, with -ss-xp) taps the level-up re-roll n times once the cards are in (0.5 s apart,
    /// the capture follows the last flip); logs each new offer and the re-rolls left. n above the re-rolls left shows the
    /// disabled "x0" button.
    /// <c>-ss-horde &lt;n&gt;</c> places n enemies (wave table mix of the last phase) on a ring 4-11 m round the player after
    /// load, from the spawner's pools (perf runs; KAN-30). <c>-ss-immortal</c> turns off all damage on the player (KAN-44).
    /// <c>-ss-hold &lt;n&gt;</c> keeps at least n enemies alive: every second, missing ones are spawned off screen (perf soak).
    /// <c>-ss-timer &lt;seconds&gt;</c> sets the Chamber survival clock after load (KAN-39; 0 = the portal opens now).
    /// <c>-ss-portal</c> walks the Knight into the portal right after load (KAN-40): pull, white fade, next Chamber,
    /// title card. With -ss-delay ~2.2 the capture shows the "CHAMBER II" card; skills / health set before it carry over.
    /// <c>-ss-portal-soak &lt;n&gt;</c> (KAN-47 leak check): n portal transitions in a row, each started 1 s after the last
    /// fade-in; logs per transition the Chamber, the time from portal to play, scene objects, Unity objects and memory
    /// (<c>[DevScreenshot] Soak ...</c>). From Chamber 1 the 10th portal of Realm 1 ends on the victory screen. The capture
    /// waits until the soak is done.
    /// <c>-ss-run-seed &lt;n&gt;</c> starts the run with that seed and <c>-ss-chamber &lt;n&gt;</c> starts it at that Chamber
    /// (KAN-46: layout seed + difficulty of that Chamber; same run seed + Chamber = same layout).
    /// <c>-ss-boss &lt;0..1&gt;</c> (KAN-58, with <c>-ss-chamber 5</c>): once the boss has risen, sets its health to that share and
    /// stands the Knight 2.6 m in front of it, so the slam winds up about 1.5 s later (add -ss-immortal to survive it).
    /// 0 kills it and moves the Knight in front of the arena portal, which then opens (KAN-59) after the victory beat (KAN-62).
    /// <c>-ss-boss-stay</c> (KAN-62, with <c>-ss-boss 0</c>): the Knight stays where it was (victory beat, reward chest).
    /// <c>-ss-boss-attack fan|charge</c> (KAN-61, with <c>-ss-boss</c>): the Knight stands 8 m in front of the boss instead,
    /// and the boss starts that attack at once (charge: below the phase 2 share, e.g. <c>-ss-boss 0.4</c>, for the look of
    /// phase 2; the debug start works in phase 1 too). KAN-108: also <c>slam|lane|spin</c> (first attack of that type, this
    /// phase first) or an index into the boss's attack list (e.g. <c>3</c> = the Butcher's chain hook); the Knight stands
    /// inside that attack's range. KAN-116: also <c>nova|spiral|rain</c>; a boss without that type gets the sample entry
    /// (<c>BossAttack.Sample</c>, Pyre Lord numbers) added to its fight for the shot.
    /// KAN-119: also <c>lobbed|summon|teleport</c> (samples: Rotting Colossus boulder / ghoul summon, Bone Matriarch teleport).
    /// KAN-121: also <c>shockwave|homing|beams</c> (samples: Rotting Colossus double slam, Bone Matriarch skulls / cross beams).
    /// KAN-174: also <c>barrage</c>: the Knight stands 6 m away and the boss throws one black fireball at once (the Butcher's
    /// own entry; the rest keep coming on its 1 s clock).
    /// <c>-ss-boss-type &lt;Id&gt;</c> (KAN-109): every boss Chamber gets that boss (roster id, e.g. <c>PyreLord</c>, from the
    /// fight's boss list or its library of other bosses in the build) instead of the Realm's own.
    /// <c>-ss-shaman-cast orbs|hex|raise[,&lt;s&gt;]</c> (KAN-107, with <c>-ss-enemy HexboundShaman,...</c>): every Shaman starts
    /// that cast that many seconds after load (default 1.2; hex falls back to raise without zombies in reach).
    /// <c>-ss-hearts &lt;n&gt;</c> (KAN-60, boss Chamber): drops n healing hearts ahead of the Knight right after load (cap
    /// permitting); each shows its ground glow for 1 s, then floats.
    /// Test Mode (KAN-144): <c>-ss-test-mode on</c> turns it on before any scene wakes (grant flag reset first, so the
    /// Souls grant runs); without the arg it is off, so a Test Mode left on never leaks into other shots.
    /// <c>-ss-settings</c> opens the menu settings; <c>-ss-start-chamber &lt;n&gt;</c> steps the Realm sheet's Start Chamber.
    /// <c>-ss-store-sim</c> (KAN-151): the build acts as a store release build (Test Mode locked off); with
    /// <c>-ss-settings</c> the settings title also gets 2 x 7 taps, so the shot proves the row never shows.
    /// Main menu (KAN-20): <c>-ss-realms</c> opens the Realm selection page, <c>-ss-realm &lt;id&gt;</c> also opens that
    /// Realm's detail sheet. With either, the saved progress of Realms 1-3 is wiped first (repeatable shots); then
    /// <c>-ss-best &lt;realm&gt;:&lt;chamber&gt;</c> records a best Chamber and <c>-ss-cleared &lt;realm&gt;</c> marks a Realm completed.
    /// <c>-ss-wallet &lt;n&gt;</c> (KAN-96) sets the saved Souls wallet to n before the first scene wakes.
    /// <c>-ss-gems &lt;n&gt;</c> (KAN-97) sets the Ember Shards wallet to n and clears the granted bonuses (welcome toast shows).
    /// Soul tree (KAN-98): <c>-ss-tree-levels &lt;id&gt;:&lt;n&gt;[,id:n...]</c> sets those node levels (all others 0) after load;
    /// in the Game scene the Knight's permanent bonuses are applied again. <c>-ss-soul-tree</c> opens the main menu's tree
    /// page, <c>-ss-soul-node &lt;id&gt;</c> also opens that node's detail sheet.
    /// Store (KAN-99): <c>-ss-store</c> opens the main menu's store page, <c>-ss-store-tab items</c> shows the Items tab,
    /// <c>-ss-store-item &lt;id&gt;</c> opens a pack's (product id) or offer's sheet, <c>-ss-store-buy</c> presses its buy button
    /// (sandbox provider: the result toast shows).
    /// <c>-ss-enemy &lt;Id&gt;[+Id...][,count[,radius]]</c> (KAN-104; radius KAN-106) places count (default 3) enemies of each roster type (e.g.
    /// GraveyardLaborer, prefab <c>Enemy_&lt;Id&gt;</c>) in an arc ~5 m ahead of the Knight after load and turns the crossbow off,
    /// so they walk in and attack. Only types the scene's wave table or the spawner's extra types (KAN-105 ranged zombies)
    /// hold can be placed (others are not in the build).
    /// Used by tools/agents to verify visuals without a human. Inactive in release builds (Debug.isDebugBuild).
    /// </summary>
    public sealed class DevScreenshot : MonoBehaviour
    {
        private string _path;
        private float _delay;
        private string _scene;
        private string _move;
        private string _closeup;
        private bool _nearExit;
        private string _pos;
        private string _playerHp;
        private string _counters;
        private string _hitMarker;
        private float _release;
        private int _souls;
        private float _soulsPull = -1f;
        private float _soulsFreeze = -1f;
        private int _xp;
        private string _skills;
        private bool _powerCurve;
        private bool _volley;
        private string _upgrade;
        private int _chests;
        private string _openChest;
        private string _offer;
        private int _rerolls;
        private int _horde;
        private float _hordePower;
        private bool _hordeTiers;
        private string _enemy;
        private string _shamanCast;
        private int _hold;
        private bool _immortal;
        private bool _bossStay;
        private string _timer;
        private bool _portal;
        private int _soak;
        private bool _realms;
        private bool _soulTree;
        private string _soulNode;
        private bool _store;
        private string _storeTab;
        private string _storeItem;
        private bool _storeBuy;
        private string _treeLevels;
        private int _realmDetail;
        private string _best;
        private int _cleared;
        private string _boss;
        private string _bossAttack;
        private int _hearts;
        private bool _settings;
        private int _startChamber;

        private const float XpLead = 1.5f; // realtime seconds between the XP grant and the capture (cards fully in)

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (Application.isEditor || !Debug.isDebugBuild) return;
            string path = GetArg("-ss-screenshot");
            if (string.IsNullOrEmpty(path)) return;

            Application.runInBackground = true; // keep frames ticking when the window is not focused
            ApplyQualityOverride(GetArg("-ss-quality"));

            var go = new GameObject(nameof(DevScreenshot));
            DontDestroyOnLoad(go);
            var shot = go.AddComponent<DevScreenshot>();
            shot._path = path;
            shot._delay = float.TryParse(GetArg("-ss-delay"), NumberStyles.Float, CultureInfo.InvariantCulture, out float d) ? d : 3f;
            shot._scene = GetArg("-ss-scene");
            shot._move = GetArg("-ss-move");
            shot._closeup = GetArg("-ss-closeup");
            shot._nearExit = Array.IndexOf(Environment.GetCommandLineArgs(), "-ss-near-exit") >= 0;
            shot._pos = GetArg("-ss-pos");
            shot._playerHp = GetArg("-ss-player-hp");
            shot._counters = GetArg("-ss-counters");
            shot._hitMarker = GetArg("-ss-hit-marker");
            shot._release = float.TryParse(GetArg("-ss-release"), NumberStyles.Float, CultureInfo.InvariantCulture, out float r) ? r : 0f;
            shot._souls = int.TryParse(GetArg("-ss-souls"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 0;
            string[] pull = (GetArg("-ss-souls-pull") ?? "").Split(',');
            shot._soulsPull = float.TryParse(pull[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float sp) ? sp : -1f;
            shot._soulsFreeze = pull.Length > 1 && float.TryParse(pull[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float sf) ? sf : -1f;
            shot._skills = GetArg("-ss-skills");
            shot._powerCurve = Array.IndexOf(Environment.GetCommandLineArgs(), "-ss-power-curve") >= 0;
            shot._volley = Array.IndexOf(Environment.GetCommandLineArgs(), "-ss-volley") >= 0;
            shot._upgrade = GetArg("-ss-upgrade");
            shot._chests = int.TryParse(GetArg("-ss-chests"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int ch) ? ch : 0;
            shot._offer = GetArg("-ss-offer");
            shot._rerolls = int.TryParse(GetArg("-ss-reroll"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int rr) ? rr : 0;
            shot._openChest = GetArg("-ss-open-chest");
            shot._xp = int.TryParse(GetArg("-ss-xp"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int x) ? x : 0;
            shot._horde = int.TryParse(GetArg("-ss-horde"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int h) ? h : 0;
            shot._hordePower = float.TryParse(GetArg("-ss-horde-power"), NumberStyles.Float, CultureInfo.InvariantCulture, out float hp) ? hp : 0f;
            shot._hordeTiers = Array.IndexOf(Environment.GetCommandLineArgs(), "-ss-horde-tiers") >= 0;
            shot._enemy = GetArg("-ss-enemy");
            shot._shamanCast = GetArg("-ss-shaman-cast");
            shot._hold = int.TryParse(GetArg("-ss-hold"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int hold) ? hold : 0;
            shot._immortal = Array.IndexOf(Environment.GetCommandLineArgs(), "-ss-immortal") >= 0;
            shot._timer = GetArg("-ss-timer");
            shot._portal = Array.IndexOf(Environment.GetCommandLineArgs(), "-ss-portal") >= 0;
            shot._soak = int.TryParse(GetArg("-ss-portal-soak"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int sk) ? sk : 0;
            shot._realmDetail = int.TryParse(GetArg("-ss-realm"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int rd) ? rd : 0;
            shot._realms = shot._realmDetail > 0 || Array.IndexOf(Environment.GetCommandLineArgs(), "-ss-realms") >= 0;
            shot._best = GetArg("-ss-best");
            shot._soulNode = GetArg("-ss-soul-node");
            shot._soulTree = !string.IsNullOrEmpty(shot._soulNode) || Array.IndexOf(Environment.GetCommandLineArgs(), "-ss-soul-tree") >= 0;
            shot._treeLevels = GetArg("-ss-tree-levels");
            shot._storeTab = GetArg("-ss-store-tab");
            shot._storeItem = GetArg("-ss-store-item");
            shot._storeBuy = Array.IndexOf(Environment.GetCommandLineArgs(), "-ss-store-buy") >= 0;
            shot._store = shot._storeBuy || !string.IsNullOrEmpty(shot._storeTab) || !string.IsNullOrEmpty(shot._storeItem)
                || Array.IndexOf(Environment.GetCommandLineArgs(), "-ss-store") >= 0;
            shot._boss = GetArg("-ss-boss");
            shot._bossAttack = GetArg("-ss-boss-attack");
            shot._bossStay = Array.IndexOf(Environment.GetCommandLineArgs(), "-ss-boss-stay") >= 0;
            // KAN-109: which boss the boss Chambers get (before the Game scene wakes: BossFight picks in Start).
            SurvivorSouls.Gameplay.Boss.BossFight.ForcedBoss = GetArg("-ss-boss-type");
            shot._hearts = int.TryParse(GetArg("-ss-hearts"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int hs) ? hs : 0;
            shot._cleared = int.TryParse(GetArg("-ss-cleared"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int cl) ? cl : 0;
            shot._settings = Array.IndexOf(Environment.GetCommandLineArgs(), "-ss-settings") >= 0;
            shot._startChamber = int.TryParse(GetArg("-ss-start-chamber"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int sc) ? sc : 0;
            // KAN-151: simulate a store build before Test Mode is read anywhere.
            if (Array.IndexOf(Environment.GetCommandLineArgs(), "-ss-store-sim") >= 0)
            {
                SurvivorSouls.Core.TestMode.SimulateStoreBuild = true;
                Debug.Log("[DevScreenshot] Store build simulated: Test Mode locked off");
            }
            // Test Mode (KAN-144) before any scene wakes: menus and the HUD read it on enable.
            if (GetArg("-ss-test-mode") == "on")
            {
                SurvivorSouls.Core.TestMode.ResetAll();
                int added = SurvivorSouls.Core.TestMode.SetOn(true);
                Debug.Log($"[DevScreenshot] Test Mode on, +{added} Souls, balance {SurvivorSouls.Core.SoulWallet.Balance}");
            }
            else SurvivorSouls.Core.GameSettings.TestMode = false;
            // Souls wallet (KAN-96) set before any scene wakes, so the menu and the end screens show a known balance.
            if (int.TryParse(GetArg("-ss-wallet"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int wallet))
            {
                SurvivorSouls.Core.SoulWallet.Clear();
                SurvivorSouls.Core.SoulWallet.Earn(wallet);
            }
            // Pending souls (KAN-101) after the wallet arg (its Clear deletes them): the menu banks them at start + toast.
            if (int.TryParse(GetArg("-ss-pending-souls"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int pending))
                SurvivorSouls.Core.SoulWallet.SavePending(pending);
            // Ember Shards (KAN-97): balance n and no bonus granted yet, so the menu shows the welcome toast once.
            if (int.TryParse(GetArg("-ss-gems"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int gems))
            {
                SurvivorSouls.Core.GemWallet.ResetAll();
                SurvivorSouls.Core.GemWallet.Earn(gems);
            }

            // Before any scene wakes: ChamberSetup reads the run in Awake (KAN-46).
            int runSeed = int.TryParse(GetArg("-ss-run-seed"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int rs) ? rs : 0;
            int chamber = int.TryParse(GetArg("-ss-chamber"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int c) ? c : 0;
            if (runSeed != 0 || chamber > 0)
            {
                SurvivorSouls.Gameplay.Chamber.RunState.NewRun(1, runSeed);
                if (chamber > 0) SurvivorSouls.Gameplay.Chamber.RunState.JumpToChamber(chamber);
            }
        }

        private IEnumerator Start()
        {
            if (!string.IsNullOrEmpty(_scene))
            {
                SceneManager.LoadScene(_scene);
                yield return null;
                yield return null;
            }
            if (_nearExit) MoveNearExit();
            if (!string.IsNullOrEmpty(_pos))
            {
                string[] xz = _pos.Split(',');
                Teleport(new Vector3(float.Parse(xz[0], CultureInfo.InvariantCulture), 0f, float.Parse(xz[1], CultureInfo.InvariantCulture)));
            }
            if (!string.IsNullOrEmpty(_counters)) SetCounters(_counters);
            if (!string.IsNullOrEmpty(_playerHp)) SetPlayerHealth(float.Parse(_playerHp, CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(_move))
            {
                string[] xy = _move.Split(',');
                FloatingJoystick.SimulatedValue = new Vector2(
                    float.Parse(xy[0], CultureInfo.InvariantCulture), float.Parse(xy[1], CultureInfo.InvariantCulture));
            }

            if (_souls > 0) DropSouls(_souls);
            if (_soulsPull >= 0f) StartCoroutine(PullSouls(_soulsPull, _soulsFreeze));
            if (_immortal)
            {
                foreach (var c in FindObjectsByType<ContactDamageReceiver>()) c.enabled = false;
                // KAN-44: melee clip hits, specials, projectiles and hazards (KAN-104+) bypass contact damage.
                foreach (var h in FindObjectsByType<PlayerHealth>()) h.Invulnerable = true;
            }
            if (_hordePower > 0f) SetHordePower(_hordePower);
            if (_horde > 0) PlaceHorde(_horde);
            if (_hordeTiers) PlaceTierLineup();
            if (!string.IsNullOrEmpty(_enemy)) PlaceEnemyType(_enemy);
            if (!string.IsNullOrEmpty(_shamanCast)) StartCoroutine(ShamanCast(_shamanCast));
            if (_hold > 0) StartCoroutine(HoldHorde(_hold));
            if (!string.IsNullOrEmpty(_skills)) GrantSkills(_skills);
            if (_powerCurve) PrintPowerCurve();
            if (_volley) PrintVolley();
            if (_chests > 0) DropChests(_chests);
            if (!string.IsNullOrEmpty(_offer)) ForceOffer(_offer);
            if (!string.IsNullOrEmpty(_openChest)) DropChestUnderfoot(_openChest == "rare");
            if (!string.IsNullOrEmpty(_timer))
            {
                var timer = SurvivorSouls.Gameplay.Chamber.SurvivalTimer.Active;
                if (timer == null) Debug.LogWarning("[DevScreenshot] -ss-timer: no survival timer in the scene");
                else timer.SetRemaining(float.Parse(_timer, CultureInfo.InvariantCulture));
            }

            if (!string.IsNullOrEmpty(_boss)) StartCoroutine(SetupBoss(float.Parse(_boss, CultureInfo.InvariantCulture), _bossAttack, _bossStay));
            if (_hearts > 0) DropHearts(_hearts);
            if (_portal) EnterPortal();
            if (_soak > 0) yield return PortalSoak(_soak);
            if (_realms) OpenRealms();
            if (_settings) OpenSettings();
            if (_treeLevels != null) SetTreeLevels(_treeLevels);
            if (_soulTree) OpenSoulTree();
            if (_store) OpenStore();

            float release = Mathf.Clamp(_release, 0f, _delay);
            float xpLead = _xp > 0 ? Mathf.Min(XpLead, _delay - release) : 0f;
            float wait = _delay - release - xpLead;
            yield return new WaitForSecondsRealtime(Mathf.Max(0f, wait - 1f));
            yield return LogStats(Mathf.Min(1f, wait));
            var spawner = FindAnyObjectByType<SurvivorSouls.Gameplay.Combat.EnemySpawner>();
            if (spawner != null) Debug.Log($"[DevScreenshot] Spawner t={spawner.RunTime:F0}s inUse={spawner.InUse} {spawner.Stats}");
            if (_xp > 0)
            {
                FindAnyObjectByType<SurvivorSouls.Gameplay.Progression.XpManager>()?.AddXp(_xp);
                yield return new WaitForSecondsRealtime(xpLead);
                if (_rerolls > 0) yield return Reroll(_rerolls);
            }
            if (release > 0f)
            {
                FloatingJoystick.SimulatedValue = null; // finger up: stick stops and glides back to idle
                yield return new WaitForSecondsRealtime(release);
            }
            if (!string.IsNullOrEmpty(_closeup)) FrameCloseup(float.Parse(_closeup, CultureInfo.InvariantCulture));
            if (!string.IsNullOrEmpty(_upgrade))
            {
                UpgradeSkills(_upgrade);
                yield return new WaitForSecondsRealtime(0.3f); // glow near its peak, new pips mid-blink
            }
            if (!string.IsNullOrEmpty(_hitMarker)) ShowHitMarker(_hitMarker == "kill");
            yield return null;
            yield return new WaitForEndOfFrame();
            ScreenCapture.CaptureScreenshot(_path);
            yield return new WaitForSecondsRealtime(1f); // capture writes asynchronously
            if (_portal) Debug.Log($"[DevScreenshot] Chamber {SurvivorSouls.Gameplay.Chamber.RunState.Chamber} (Realm {SurvivorSouls.Gameplay.Chamber.RunState.Realm})");
            Debug.Log($"[DevScreenshot] Saved {_path}");
            Application.Quit();
        }

        /// <summary>Main menu: wipes Realm progress, applies -ss-best / -ss-cleared, opens the Realm page (and a sheet).</summary>
        private void OpenRealms()
        {
            for (int realm = 1; realm <= 3; realm++) SurvivorSouls.Gameplay.Chamber.RunProgress.Clear(realm);
            if (!string.IsNullOrEmpty(_best))
            {
                string[] rc = _best.Split(':');
                SurvivorSouls.Gameplay.Chamber.RunProgress.RecordChamberStart(
                    int.Parse(rc[0], CultureInfo.InvariantCulture), int.Parse(rc[1], CultureInfo.InvariantCulture));
            }
            if (_cleared > 0) SurvivorSouls.Gameplay.Chamber.RunProgress.MarkCompleted(_cleared);

            var menu = FindAnyObjectByType<SurvivorSouls.UI.MainMenu.MainMenuController>();
            if (menu == null || menu.RealmSelect == null)
            {
                Debug.LogWarning("[DevScreenshot] -ss-realms: no main menu with a Realm page in the scene");
                return;
            }
            menu.OpenRealms();
            if (_realmDetail > 0 && !menu.RealmSelect.OpenDetail(_realmDetail))
                Debug.LogWarning($"[DevScreenshot] -ss-realm: no Realm {_realmDetail}");
            for (int i = 1; i < _startChamber; i++) menu.RealmSelect.StepChamber(1);
            if (_startChamber > 0)
                Debug.Log($"[DevScreenshot] Start Chamber {menu.RealmSelect.StartChamber} stepper={menu.RealmSelect.StepperVisible}");
            Debug.Log($"[DevScreenshot] Realm page open, detail={menu.RealmSelect.Selected?.DisplayName ?? "none"} canStart={menu.RealmSelect.CanStart}");
        }

        /// <summary>Soul tree levels from "id:n,id:n" (others 0) on the scene's tree; Game scene: bonuses applied again.</summary>
        private static void SetTreeLevels(string spec)
        {
            var bonuses = FindAnyObjectByType<SurvivorSouls.Gameplay.Meta.SoulTreeBonuses>();
            var menu = FindAnyObjectByType<SurvivorSouls.UI.MainMenu.MainMenuController>();
            var tree = bonuses != null ? bonuses.Tree : menu != null && menu.SoulTreePage != null ? menu.SoulTreePage.Tree : null;
            if (tree == null)
            {
                Debug.LogWarning("[DevScreenshot] -ss-tree-levels: no soul tree in the scene");
                return;
            }
            SurvivorSouls.Gameplay.Meta.SoulTreeProgress.ResetAll(tree);
            foreach (string part in spec.Split(','))
            {
                string[] kv = part.Split(':');
                var node = kv.Length == 2 ? tree.Find(kv[0].Trim()) : null;
                if (node == null) Debug.LogWarning($"[DevScreenshot] -ss-tree-levels: unknown node '{part}'");
                else SurvivorSouls.Gameplay.Meta.SoulTreeProgress.SetLevel(node, int.Parse(kv[1], CultureInfo.InvariantCulture));
            }
            if (bonuses != null) bonuses.Apply();
            menu?.SoulTreePage?.Refresh();
            Debug.Log($"[DevScreenshot] Soul tree levels set ({spec}), {tree.TotalSpent()} of {tree.FullCost()} Souls spent");
        }

        /// <summary>Main menu: opens the soul tree page (and a node's sheet with -ss-soul-node).</summary>
        private static void OpenSettings()
        {
            var menu = FindAnyObjectByType<SurvivorSouls.UI.MainMenu.MainMenuController>();
            if (menu == null)
            {
                Debug.LogWarning("[DevScreenshot] -ss-settings: no main menu in the scene");
                return;
            }
            menu.OpenSettings();
            if (SurvivorSouls.Core.TestMode.Locked)
                for (int i = 0; i < SurvivorSouls.Core.TestMode.RevealTaps * 2; i++) menu.TapSettingsTitle();
            Debug.Log($"[DevScreenshot] Settings open, testRow={menu.TestModeRowVisible} testTag={menu.TestModeTagVisible} testMode={SurvivorSouls.Core.TestMode.IsOn}");
        }

        private void OpenSoulTree()
        {
            var menu = FindAnyObjectByType<SurvivorSouls.UI.MainMenu.MainMenuController>();
            if (menu == null || menu.SoulTreePage == null)
            {
                Debug.LogWarning("[DevScreenshot] -ss-soul-tree: no main menu with a soul tree page in the scene");
                return;
            }
            menu.OpenSoulTree();
            if (!string.IsNullOrEmpty(_soulNode) && !menu.SoulTreePage.OpenDetail(_soulNode))
                Debug.LogWarning($"[DevScreenshot] -ss-soul-node: no node '{_soulNode}'");
            Debug.Log($"[DevScreenshot] Soul tree open, detail={menu.SoulTreePage.Selected?.DisplayName ?? "none"} canBuy={menu.SoulTreePage.CanBuy}");
        }

        /// <summary>Main menu: opens the store page (tab, item sheet, buy press from the -ss-store-* args).</summary>
        private void OpenStore()
        {
            var menu = FindAnyObjectByType<SurvivorSouls.UI.MainMenu.MainMenuController>();
            if (menu == null || menu.StorePage == null)
            {
                Debug.LogWarning("[DevScreenshot] -ss-store: no main menu with a store page in the scene");
                return;
            }
            var page = menu.StorePage;
            menu.OpenStore();
            if (!string.IsNullOrEmpty(_storeTab)) page.ShowTab(_storeTab);
            if (!string.IsNullOrEmpty(_storeItem) && !page.OpenDetail(_storeItem))
                Debug.LogWarning($"[DevScreenshot] -ss-store-item: no pack or offer '{_storeItem}'");
            string bought = _storeBuy ? $" buy={page.BuySelected()} toast='{page.ToastText}'" : "";
            Debug.Log($"[DevScreenshot] Store open, tab={page.Tab} detail={page.SelectedPack?.Title ?? page.SelectedOffer?.Title ?? "none"} " +
                      $"canBuy={page.CanBuy}{bought} shards={SurvivorSouls.Core.GemWallet.Balance} souls={SurvivorSouls.Core.SoulWallet.Balance}");
        }

        /// <summary>Waits for the boss to rise, sets its health share and puts the Knight in slam range in front of it.</summary>
        private static IEnumerator SetupBoss(float health, string attack, bool stay)
        {
            var fight = SurvivorSouls.Gameplay.Boss.BossFight.Active;
            if (fight == null || fight.Current == SurvivorSouls.Gameplay.Boss.BossFight.Stage.Inactive)
            {
                Debug.LogWarning("[DevScreenshot] -ss-boss: no boss fight in this Chamber (use -ss-chamber 5)");
                yield break;
            }
            while (fight.Boss == null || !fight.Boss.isActiveAndEnabled) yield return null;
            var boss = fight.Boss;
            float target = Mathf.Clamp01(health) * boss.MaxHealth;
            if (target <= 0f) // kill: reward drops, the portal opens; the Knight waits in front of the exit (KAN-59 arena)
            {
                boss.TakeDamage(boss.Health + 1f, Vector3.zero, DamageKind.Burst);
                if (!stay) MoveNearExit(); // -ss-boss-stay: the Knight stays to watch the victory beat and the reward chest
                Debug.Log($"[DevScreenshot] Boss {fight.Data.DisplayName} killed, stage={fight.Current}");
                yield break;
            }
            if (boss.Health > target) boss.TakeDamage(boss.Health - target, Vector3.zero, DamageKind.Burst);
            var b = boss.transform;
            int index = BossAttackIndex(fight, attack);
            if (index < 0 && Enum.TryParse(attack, true, out SurvivorSouls.Gameplay.Boss.BossFight.Move move)
                && !int.TryParse(attack, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                index = fight.AddAttack(SurvivorSouls.Gameplay.Boss.BossAttack.Sample(move)); // KAN-116: try a type this boss lacks
            float distance = index >= 0 ? BossAttackDistance(fight.Attacks[index]) : 2.6f;
            Teleport(b.position + b.forward * distance);
            Debug.Log($"[DevScreenshot] Boss {fight.Data.DisplayName} hp={boss.Health:F0}/{boss.MaxHealth:F0} phase={fight.Phase} at {b.position}");
            if (index < 0) yield break;
            var entry = fight.Attacks[index];
            for (float t = 0f; t < 6f; t += Time.unscaledDeltaTime)
            {
                yield return null;
                if (fight.ForceAttack(index))
                {
                    Debug.Log($"[DevScreenshot] Boss attack {entry} ({entry.type}) started, wind-up {fight.WindUpTime:F2} s");
                    yield break;
                }
            }
            Debug.LogWarning($"[DevScreenshot] -ss-boss-attack: {entry} did not start");
        }

        /// <summary>-ss-boss-attack: a type name (first of that type, this phase first) or an index. -1 = none.</summary>
        private static int BossAttackIndex(SurvivorSouls.Gameplay.Boss.BossFight fight, string attack)
        {
            if (string.IsNullOrEmpty(attack)) return -1;
            var list = fight.Attacks;
            if (int.TryParse(attack, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)) return i >= 0 && i < list.Length ? i : -1;
            int any = -1;
            for (int k = 0; k < list.Length; k++)
            {
                if (!string.Equals(list[k].type.ToString(), attack, StringComparison.OrdinalIgnoreCase)) continue;
                if (list[k].InPhase(fight.Phase)) return k;
                if (any < 0) any = k;
            }
            return any;
        }

        /// <summary>Where the Knight stands for the attack's look: inside its range (close for slam / spin / chop).</summary>
        private static float BossAttackDistance(in SurvivorSouls.Gameplay.Boss.BossAttack a)
        {
            float want = a.type == SurvivorSouls.Gameplay.Boss.BossFight.Move.Slam || a.type == SurvivorSouls.Gameplay.Boss.BossFight.Move.Spin ? 2.6f
                : a.type == SurvivorSouls.Gameplay.Boss.BossFight.Move.Lane ? Mathf.Max(3.5f, (a.minRange + a.maxRange) * 0.5f)
                : a.type == SurvivorSouls.Gameplay.Boss.BossFight.Move.Nova || a.type == SurvivorSouls.Gameplay.Boss.BossFight.Move.Spiral ? 5f
                : a.type == SurvivorSouls.Gameplay.Boss.BossFight.Move.Rain ? 6f
                : a.type == SurvivorSouls.Gameplay.Boss.BossFight.Move.Shockwave ? 3f
                : a.type == SurvivorSouls.Gameplay.Boss.BossFight.Move.Beams ? 5f
                : a.type == SurvivorSouls.Gameplay.Boss.BossFight.Move.Barrage ? 6f // KAN-174: boss and fireballs in one frame
                : 8f;
            return Mathf.Clamp(want, a.minRange + 0.3f, Mathf.Max(a.minRange + 0.3f, a.maxRange - 0.3f));
        }

        /// <summary>Starts the portal exit as if the Knight walked into the arch 1.5 m ahead (KAN-40).</summary>
        private static void EnterPortal()
        {
            var transition = SurvivorSouls.Gameplay.Chamber.ChamberTransition.Active;
            var player = FindAnyObjectByType<PlayerHealth>();
            if (transition == null || player == null)
            {
                Debug.LogWarning("[DevScreenshot] -ss-portal: no ChamberTransition or player in the scene");
                return;
            }
            var t = player.transform;
            bool started = transition.Enter(t.position + t.forward * 1.5f);
            Debug.Log($"[DevScreenshot] Portal enter started={started}");
        }

        /// <summary>-ss-portal-soak: n portal transitions in a row with timing, object counts and memory per step (KAN-47).</summary>
        private static IEnumerator PortalSoak(int count)
        {
            for (int i = 1; i <= count; i++)
            {
                yield return new WaitForSecondsRealtime(1f);
                var transition = SurvivorSouls.Gameplay.Chamber.ChamberTransition.Active;
                var player = FindAnyObjectByType<PlayerHealth>();
                if (transition == null || player == null || player.IsDead)
                {
                    Debug.LogWarning($"[DevScreenshot] Soak {i}: no transition or living Knight, stop");
                    yield break;
                }
                int from = SurvivorSouls.Gameplay.Chamber.RunState.Chamber;
                float start = Time.realtimeSinceStartup;
                var t = player.transform;
                if (!transition.Enter(t.position + t.forward * 1.5f))
                {
                    Debug.LogWarning($"[DevScreenshot] Soak {i}: portal refused, stop");
                    yield break;
                }
                bool arriving = false, cleared = false;
                while (Time.realtimeSinceStartup - start < 20f)
                {
                    if (transition != null && transition.Current == SurvivorSouls.Gameplay.Chamber.ChamberTransition.Phase.Cleared)
                    {
                        cleared = true;
                        break;
                    }
                    var next = SurvivorSouls.Gameplay.Chamber.ChamberTransition.Active;
                    if (next != null && next != transition)
                    {
                        if (next.Current == SurvivorSouls.Gameplay.Chamber.ChamberTransition.Phase.Arriving) arriving = true;
                        else if (arriving && next.Current == SurvivorSouls.Gameplay.Chamber.ChamberTransition.Phase.Idle) break;
                    }
                    yield return null;
                }
                float took = Time.realtimeSinceStartup - start;
                yield return null;
                int sceneObjects = FindObjectsByType<Transform>(FindObjectsInactive.Include).Length;
                int allObjects = Resources.FindObjectsOfTypeAll<UnityEngine.Object>().Length;
                long total = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() >> 20;
                long mono = UnityEngine.Profiling.Profiler.GetMonoUsedSizeLong() >> 20;
                Debug.Log($"[DevScreenshot] Soak {i}: Chamber {from}->{SurvivorSouls.Gameplay.Chamber.RunState.Chamber}" +
                          $"{(cleared ? " REALM CLEARED" : "")} took={took:F2}s sceneObjects={sceneObjects} unityObjects={allObjects} " +
                          $"allocMB={total} monoMB={mono}");
                if (cleared) yield break;
            }
        }

        /// <summary>Averages render counters over <paramref name="seconds"/> (development players expose them).</summary>
        private static IEnumerator LogStats(float seconds)
        {
            using var draws = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Draw Calls Count");
            using var batches = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Batches Count");
            using var setPass = ProfilerRecorder.StartNew(ProfilerCategory.Render, "SetPass Calls Count");
            using var tris = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            using var main = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            long d = 0, b = 0, sp = 0, t = 0, m = 0, mMax = 0;
            int frames = 0, enemies = 0;
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < seconds)
            {
                yield return null;
                d += draws.LastValue; b += batches.LastValue; sp += setPass.LastValue; t += tris.LastValue;
                m += main.LastValue;
                mMax = Math.Max(mMax, main.LastValue);
                enemies += Enemy.Active.Count;
                frames++;
            }
            frames = Mathf.Max(1, frames);
            float ms = (Time.realtimeSinceStartup - start) * 1000f / frames;
            Debug.Log($"[DevScreenshot] Stats drawCalls={d / frames} batches={b / frames} setPass={sp / frames} tris={t / frames} frameMs={ms:F1} " +
                      $"mainThreadMs={m / frames * 1e-6f:F2} mainThreadMaxMs={mMax * 1e-6f:F2} enemies={enemies / frames} {QualityTag()}");
        }

        private static void MoveNearExit()
        {
            var arena = SurvivorSouls.Gameplay.World.BossArena.Active; // boss Chamber: the arena portal (KAN-59)
            var world = FindAnyObjectByType<SurvivorSouls.Gameplay.World.WorldStreamer>();
            if (arena != null) Teleport(arena.ExitPosition + new Vector3(0f, 0f, -5.5f));
            else if (world != null) Teleport(world.ExitPosition + new Vector3(0f, 0f, -5.5f));
        }

        private static void Teleport(Vector3 position)
        {
            var player = FindAnyObjectByType<PlayerMover>();
            if (player == null) return;
            var cc = player.GetComponent<CharacterController>();
            cc.enabled = false;
            player.transform.position = position;
            cc.enabled = true;
        }

        private static IEnumerator PullSouls(float after, float freeze)
        {
            yield return new WaitForSeconds(after);
            var field = FindAnyObjectByType<SurvivorSouls.Gameplay.Progression.SoulWispField>();
            if (field != null) field.PullAll();
            Debug.Log($"[DevScreenshot] Souls pulled: {(field != null ? field.Count : 0)} crystals at t={Time.time:F2}");
            if (freeze < 0f) yield break;
            yield return new WaitForSeconds(freeze);
            Time.timeScale = 0f;
        }

        private static void DropSouls(int count)
        {
            var field = FindAnyObjectByType<SurvivorSouls.Gameplay.Progression.SoulWispField>();
            var player = FindAnyObjectByType<PlayerMover>();
            if (field == null || player == null) return;
            for (int i = 0; i < count; i++)
            {
                float a = i * 2.39996f; // golden angle: an even scatter
                float d = 3.2f + (i % 5) * 0.9f;
                int xp = i == 0 ? 150 : i % 7 == 3 ? 40 : 10;
                field.Spawn(player.transform.position + new Vector3(Mathf.Cos(a) * d, 1f, Mathf.Sin(a) * d), xp);
            }
        }

        /// <summary>
        /// Puts <paramref name="count"/> enemies round the player, types picked like the last wave phase in this Chamber
        /// (KAN-112: Chamber gates and the shooter cap apply).
        /// </summary>
        private static void PlaceHorde(int count)
        {
            var spawner = FindAnyObjectByType<EnemySpawner>();
            var player = FindAnyObjectByType<PlayerMover>();
            var waves = spawner != null ? spawner.Waves : null;
            if (player == null || waves == null || waves.PhaseCount == 0) return;
            var phase = waves.GetPhase(waves.PhaseCount - 1);
            for (int i = 0; i < count; i++)
            {
                float a = i * 2.39996f;
                float r = 4f + 7f * Mathf.Sqrt((i + 0.5f) / count);
                var prefab = PickLike(spawner, waves, phase);
                if (prefab != null) spawner.SpawnAt(prefab, player.transform.position + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
        }

        /// <summary>KAN-92: horde scaling uses power index <paramref name="power"/> instead of the Knight's stats.</summary>
        private static void SetHordePower(float power)
        {
            var scaler = FindAnyObjectByType<SurvivorSouls.Gameplay.Chamber.HordeScaler>();
            if (scaler == null)
            {
                Debug.LogWarning("[DevScreenshot] -ss-horde-power: no HordeScaler in the scene");
                return;
            }
            scaler.PowerOverride = power;
            scaler.Refresh();
            var spawner = FindAnyObjectByType<EnemySpawner>();
            Debug.Log($"[DevScreenshot] horde power {power:0.##}: zombie health x{scaler.Multiplier:0.00} (spawn x{spawner?.SpawnHealthMultiplier:0.00}, step {spawner?.DangerStep})");
        }

        /// <summary>KAN-92: one Shambler per danger colour step in an arc ahead of the Knight (step 0 left, last step right).</summary>
        private static void PlaceTierLineup()
        {
            var spawner = FindAnyObjectByType<EnemySpawner>();
            var player = FindAnyObjectByType<PlayerMover>();
            var waves = spawner != null ? spawner.Waves : null;
            var scaling = spawner != null ? spawner.Scaling : null;
            if (player == null || waves == null || waves.PhaseCount == 0 || scaling == null)
            {
                Debug.LogWarning("[DevScreenshot] -ss-horde-tiers: no spawner, waves or horde scaling");
                return;
            }
            var prefab = waves.GetPhase(0).enemies[0].prefab;
            foreach (var shooter in FindObjectsByType<AutoShooter>()) shooter.enabled = false; // keep the lineup alive
            int steps = scaling.StepCount;
            for (int i = 0; i < steps; i++)
            {
                float t = steps > 1 ? i / (float)(steps - 1) : 0.5f;
                float a = Mathf.Lerp(150f, 30f, t) * Mathf.Deg2Rad;
                float r = i % 2 == 0 ? 4.2f : 6f;
                var enemy = spawner.SpawnAt(prefab, player.transform.position + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
                enemy.SetDangerStep(scaling, i);
                enemy.Halted = true;
            }
            Debug.Log($"[DevScreenshot] tier lineup: {steps} steps, {HordeTierMaterials.Count} tier materials");
        }

        /// <summary>KAN-104: <c>Id[+Id...][,count]</c> enemies of roster types in an arc ahead of the Knight; crossbow off.</summary>
        private static void PlaceEnemyType(string arg)
        {
            string[] parts = arg.Split(',');
            string[] ids = parts[0].Split('+');
            int count = parts.Length > 1 && int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int c) ? Mathf.Max(1, c) : 3;
            // Optional third part (KAN-106): spawn radius (m) round the Knight, e.g. "FrenziedGhoul,1,11" (default 4.5 / 5.5).
            float radius = parts.Length > 2 && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float rr) ? rr : 0f;
            var spawner = FindAnyObjectByType<EnemySpawner>();
            var player = FindAnyObjectByType<PlayerMover>();
            var waves = spawner != null ? spawner.Waves : null;
            if (player == null || waves == null)
            {
                Debug.LogWarning("[DevScreenshot] -ss-enemy: no player or wave table");
                return;
            }
            foreach (var shooter in FindObjectsByType<AutoShooter>()) shooter.enabled = false;
            int total = ids.Length * count, slot = 0;
            foreach (string id in ids)
            {
                var prefab = spawner.FindType("Enemy_" + id);
                if (prefab == null)
                {
                    Debug.LogWarning($"[DevScreenshot] -ss-enemy: no '{id}' in the wave table or the spawner's extra types");
                    slot += count;
                    continue;
                }
                for (int k = 0; k < count; k++, slot++)
                {
                    float t = total > 1 ? slot / (float)(total - 1) : 0.5f;
                    float a = Mathf.Lerp(150f, 30f, t) * Mathf.Deg2Rad;
                    float r = radius > 0f ? radius : slot % 2 == 0 ? 4.5f : 5.5f;
                    spawner.SpawnAt(prefab, player.transform.position + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
                }
                Debug.Log($"[DevScreenshot] enemy {prefab.name} x{count}: height {prefab.Height:F2} m, hit radius {prefab.HitRadius:F2} m");
            }
        }

        /// <summary>-ss-shaman-cast: waits, then every Hexbound Shaman starts that cast (KAN-107).</summary>
        private static IEnumerator ShamanCast(string arg)
        {
            string[] parts = arg.Split(',');
            float at = parts.Length > 1 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float a) ? a : 1.2f;
            var cast = parts[0] switch
            {
                "orbs" => HexShaman.Cast.Orbs,
                "hex" => HexShaman.Cast.Hex,
                "raise" => HexShaman.Cast.Raise,
                _ => HexShaman.Cast.None,
            };
            if (cast == HexShaman.Cast.None)
            {
                Debug.LogWarning($"[DevScreenshot] -ss-shaman-cast: unknown cast '{parts[0]}'");
                yield break;
            }
            yield return new WaitForSeconds(at);
            int started = 0;
            foreach (var s in HexShaman.Active)
                if (s.ForceCast(cast)) started++;
            Debug.Log($"[DevScreenshot] -ss-shaman-cast {cast}: {started} of {HexShaman.Active.Count} Shamans started");
        }

        /// <summary>Tops the field up to <paramref name="count"/> enemies once a second (off-screen spawn points).</summary>
        /// <summary>KAN-112: a wave pick as the spawner makes it (this Chamber's open entries, shooters under the cap).</summary>
        private static Enemy PickLike(EnemySpawner spawner, EnemyWaveTable waves, in EnemyWaveTable.Phase phase)
        {
            bool shooters = spawner.ShootersInUse < waves.MaxShootersOf(spawner.MaxAliveOf(phase));
            return EnemyWaveTable.PickEnemy(phase, UnityEngine.Random.value, spawner.Chamber, shooters);
        }

        private static IEnumerator HoldHorde(int count)
        {
            var spawner = FindAnyObjectByType<EnemySpawner>();
            var waves = spawner != null ? spawner.Waves : null;
            if (waves == null || waves.PhaseCount == 0) yield break;
            var phase = waves.GetPhase(waves.PhaseCount - 1);
            while (spawner != null)
            {
                for (int i = Enemy.Active.Count; i < count; i++)
                {
                    var prefab = PickLike(spawner, waves, phase);
                    if (prefab == null || !spawner.TryFindSpawnPoint(out var pos)) break;
                    spawner.SpawnAt(prefab, pos);
                }
                yield return new WaitForSecondsRealtime(1f);
            }
        }

        private static void GrantSkills(string ids)
        {
            var manager = FindAnyObjectByType<SurvivorSouls.Gameplay.Skills.SkillManager>();
            if (manager == null) return;
            foreach (string entry in ids.Split(','))
            {
                string id = entry.Trim();
                int colon = id.IndexOf(':');
                if (colon > 0 && int.TryParse(id.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int level))
                {
                    var skill = manager.FindSkill(id.Substring(0, colon));
                    if (skill == null || (!manager.SetLevel(skill, level) && manager.GetLevel(skill) != Mathf.Clamp(level, 0, skill.MaxLevel)))
                        Debug.LogWarning($"[DevScreenshot] -ss-skills: cannot set '{id}'");
                }
                else if (!manager.Grant(id)) Debug.LogWarning($"[DevScreenshot] -ss-skills: cannot grant '{id}'");
            }
        }

        /// <summary>-ss-power-curve: shipped table, base numbers of the live Knight.</summary>
        private static void PrintPowerCurve()
        {
            var manager = FindAnyObjectByType<SurvivorSouls.Gameplay.Skills.SkillManager>();
            if (manager == null || manager.Progression == null) return;
            var b = SurvivorSouls.Gameplay.Skills.PowerBase.Default;
            var shooter = FindAnyObjectByType<AutoShooter>();
            if (shooter != null)
            {
                b.Damage = shooter.BaseDamage;
                b.ShotsPerSecond = shooter.BaseShotsPerSecond;
                b.CritChance = shooter.BaseCritChance;
                b.CritMultiplier = shooter.BaseCritMultiplier;
                b.SpreadDamage = shooter.BaseSpreadDamage;
            }
            var health = FindAnyObjectByType<SurvivorSouls.Gameplay.Player.PlayerHealth>();
            if (health != null) b.MaxHealth = health.BaseMax;
            Debug.Log("[DevScreenshot] " + SurvivorSouls.Gameplay.Skills.SkillPowerCurve.Simulate(manager.Progression, b));
        }

        /// <summary>-ss-volley: the bolts the Knight fires now and the Double Shot stack odds of the shipped table.</summary>
        private static void PrintVolley()
        {
            var shooter = FindAnyObjectByType<AutoShooter>();
            if (shooter != null) Debug.Log("[DevScreenshot] " + shooter.DescribeVolley());
            var manager = FindAnyObjectByType<SurvivorSouls.Gameplay.Skills.SkillManager>();
            if (manager != null && manager.Progression != null)
                Debug.Log("[DevScreenshot] " + SurvivorSouls.Gameplay.Skills.SkillStackOdds.Simulate(manager.Progression, "double_shot"));
        }

        /// <summary>-ss-upgrade: +1 level on each owned skill listed (chest upgrade path, so the HUD glows).</summary>
        private static void UpgradeSkills(string ids)
        {
            var manager = FindAnyObjectByType<SurvivorSouls.Gameplay.Skills.SkillManager>();
            if (manager == null) return;
            foreach (string id in ids.Split(','))
            {
                if (!manager.Upgrade(id.Trim())) Debug.LogWarning($"[DevScreenshot] -ss-upgrade: cannot upgrade '{id}'");
            }
        }

        /// <summary>-ss-hearts n: healing hearts ahead of the Knight, left and right (KAN-60, cap permitting).</summary>
        private static void DropHearts(int count)
        {
            var hearts = FindAnyObjectByType<SurvivorSouls.Gameplay.Boss.BossHearts>();
            var player = FindAnyObjectByType<PlayerHealth>();
            if (hearts == null || player == null)
            {
                Debug.LogWarning("[DevScreenshot] -ss-hearts: no BossHearts or player in the scene");
                return;
            }
            var p = player.transform;
            for (int i = 0; i < count; i++)
            {
                float side = (i % 2 == 0 ? -1f : 1f) * (1.6f + 0.8f * (i / 2));
                var heart = hearts.DropNow(p.position + p.forward * (3f + 1.2f * (i / 2)) + p.right * side);
                Debug.Log(heart != null ? $"[DevScreenshot] Heart at {heart.transform.position}" : "[DevScreenshot] -ss-hearts: cap reached");
            }
        }

        /// <summary>-ss-chests n: chests on the ground ahead of the Knight, alternating Light / Rare (cap permitting).</summary>
        private static void DropChests(int count)
        {
            var dropper = FindAnyObjectByType<SurvivorSouls.Gameplay.Chests.ChestDropper>();
            var player = FindAnyObjectByType<PlayerHealth>();
            if (dropper == null || player == null) return;
            var p = player.transform;
            for (int i = 0; i < count; i++)
            {
                float side = (i % 2 == 0 ? -1f : 1f) * (1.3f + 0.6f * (i / 2));
                var at = p.position + p.forward * (2.4f + 0.8f * (i / 2)) + p.right * side;
                var tier = i % 2 == 0 ? SurvivorSouls.Gameplay.Chests.ChestTier.Light : SurvivorSouls.Gameplay.Chests.ChestTier.Rare;
                var chest = dropper.DropNow(at, tier);
                Debug.Log(chest != null
                    ? $"[DevScreenshot] Chest {tier} at {chest.transform.position} choices={chest.Choices} upgrades={chest.Upgrades}"
                    : "[DevScreenshot] -ss-chests: ground full");
            }
        }

        /// <summary>-ss-open-chest: a chest 0.2 m ahead of the Knight; the dropper's own pickup opens it once it has landed.</summary>
        private static void DropChestUnderfoot(bool rare)
        {
            var dropper = FindAnyObjectByType<SurvivorSouls.Gameplay.Chests.ChestDropper>();
            var player = FindAnyObjectByType<PlayerHealth>();
            if (dropper == null || player == null) return;
            var rewards = dropper.GetComponent<SurvivorSouls.Gameplay.Chests.ChestRewards>();
            if (rewards != null) rewards.GemsFound += (at, n) => Debug.Log($"[DevScreenshot] Chest gave {n} Ember Shards"); // KAN-97
            var tier = rare ? SurvivorSouls.Gameplay.Chests.ChestTier.Rare : SurvivorSouls.Gameplay.Chests.ChestTier.Light;
            var chest = dropper.Drop(player.transform.position + player.transform.forward * 0.2f, tier, dropper.Choices,
                rare ? dropper.Config.RareUpgrades : 1);
            Debug.Log(chest != null
                ? $"[DevScreenshot] Open chest {tier}: choices={chest.Choices} upgrades={chest.Upgrades}"
                : "[DevScreenshot] -ss-open-chest: ground full");
        }

        /// <summary>-ss-reroll n: taps the level-up re-roll n times (KAN-67), waiting for each flip to settle.</summary>
        private static IEnumerator Reroll(int count)
        {
            var screen = FindAnyObjectByType<SurvivorSouls.UI.Hud.SkillChoiceScreen>();
            if (screen == null || !screen.IsShowing)
            {
                Debug.LogWarning("[DevScreenshot] -ss-reroll: no level-up screen open (use -ss-xp)");
                yield break;
            }
            for (int i = 0; i < count; i++)
            {
                bool ok = screen.Reroll();
                yield return new WaitForSecondsRealtime(0.5f);
                var names = new System.Text.StringBuilder();
                foreach (var s in screen.Options) names.Append(s.Id).Append(' ');
                Debug.Log($"[DevScreenshot] Re-roll {i + 1}: {(ok ? "done" : "refused")}, left={screen.RerollsLeft}, " +
                          $"offer={names}button='{screen.RerollLabel} {screen.RerollCountLabel}' enabled={screen.RerollEnabled}");
            }
        }

        private static void ForceOffer(string ids)
        {
            var manager = FindAnyObjectByType<SurvivorSouls.Gameplay.Skills.SkillManager>();
            if (manager == null) return;
            var offer = new System.Collections.Generic.List<SurvivorSouls.Gameplay.Skills.SkillDefinition>();
            foreach (string id in ids.Split(','))
            {
                var skill = manager.FindSkill(id.Trim());
                if (skill == null) Debug.LogWarning($"[DevScreenshot] -ss-offer: no skill '{id}'");
                else offer.Add(skill);
            }
            manager.ForceNextOffer(offer.ToArray());
        }

        /// <summary>-ss-counters kills,souls: run counters as if carried from earlier Chambers (death screen shots, KAN-51).</summary>
        private static void SetCounters(string arg)
        {
            var counters = FindAnyObjectByType<SurvivorSouls.Gameplay.Chamber.RunCounters>();
            string[] ks = arg.Split(',');
            if (counters == null || ks.Length < 2) return;
            counters.Restore(int.Parse(ks[0], CultureInfo.InvariantCulture), int.Parse(ks[1], CultureInfo.InvariantCulture));
        }

        private static void SetPlayerHealth(float fraction)
        {
            var health = FindAnyObjectByType<PlayerHealth>();
            if (health != null) health.TakeDamage(health.Max * (1f - Mathf.Clamp01(fraction)));
        }

        private static void ShowHitMarker(bool kill)
        {
            var hud = FindAnyObjectByType<SurvivorSouls.UI.Hud.HitMarkerHud>();
            var player = FindAnyObjectByType<PlayerMover>();
            if (hud == null || player == null) return;
            Enemy nearest = null;
            float best = float.MaxValue;
            foreach (var e in Enemy.Active)
            {
                float d = (e.transform.position - player.transform.position).sqrMagnitude;
                if (d < best) { best = d; nearest = e; }
            }
            if (nearest != null) hud.Show(nearest, kill);
            else Debug.LogWarning("[DevScreenshot] -ss-hit-marker: no enemy alive");
        }

        /// <summary>Stops the follow camera and frames the player's upper body from <paramref name="yaw"/> degrees.</summary>
        private static void FrameCloseup(float yaw)
        {
            var player = FindAnyObjectByType<PlayerMover>();
            var cam = Camera.main;
            if (player == null || cam == null) return;
            var follow = cam.GetComponent<TopDownFollowCamera>();
            if (follow != null) follow.enabled = false;
            foreach (var doc in FindObjectsByType<UIDocument>()) doc.gameObject.SetActive(false); // hide HUD

            Transform t = player.transform;
            Vector3 dir = Quaternion.AngleAxis(yaw, Vector3.up) * t.forward;
            cam.nearClipPlane = 0.05f;
            cam.fieldOfView = 32f;
            cam.transform.position = t.position + dir * 3.6f + Vector3.up * 1.45f;
            cam.transform.LookAt(t.position + Vector3.up * 0.98f);
        }

        private static void ApplyQualityOverride(string qualityName)
        {
            if (string.IsNullOrEmpty(qualityName)) return;
            int index = Array.IndexOf(QualitySettings.names, qualityName);
            if (index >= 0) QualitySettings.SetQualityLevel(index, applyExpensiveChanges: true);
            else Debug.LogWarning($"[DevScreenshot] Unknown quality level '{qualityName}' (levels: {string.Join(", ", QualitySettings.names)})");
            Debug.Log($"[DevScreenshot] {QualityTag()}");
        }

        /// <summary>Active quality level and render pipeline asset, e.g. <c>quality=Mobile pipeline=Mobile_RPAsset</c> (KAN-52).</summary>
        private static string QualityTag()
        {
            var pipeline = QualitySettings.renderPipeline != null ? QualitySettings.renderPipeline : UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline;
            return $"quality={QualitySettings.names[QualitySettings.GetQualityLevel()]} pipeline={(pipeline != null ? pipeline.name : "none")}";
        }

        private static string GetArg(string name)
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == name) return args[i + 1];
            return null;
        }
    }
}
