# Enemy and Boss Roster (Meshy batch 2026-10-15)

15 new rigged and animated characters: 11 zombies and 4 bosses. Models only: no game code uses them yet. Jira tickets carry the implementation (epic "New zombie and boss roster").

## 1. Asset facts (all characters)

- Source: Meshy API (text-to-3d preview + refine, or multi-image-to-3d from `~/survivor-souls-assets/character-references/<name>/v001` front/left/back), then remesh (triangles), auto-rig, Meshy animation library. Raw outputs + task JSON: `~/survivor-souls-assets/meshy_jobs/*enemy-<slug>*` (not in git).
- Unity files per character: `SK_<Name>.fbx` (skinned mesh + rig, no animation), `A_<Name>_<Clip>.fbx` (rig only, one take named `<Clip>`), `T_<Name>_BaseColor.jpg` (2048), `T_<Name>_Normal.png` (1024), `T_<Name>_Emission.jpg` (1024, only when the texture glows). No material yet: make a URP Simple Lit / Lit material per character (same as `M_Zombie`; the FBX has no embedded texture). Set the Normal texture type to Normal map.
- Rig: Meshy 24-bone skeleton (`Hips`, `Spine02`, `Spine01`, `Spine`, `neck`, `Head`, arms, legs; same names on all 15). Root armature node scale 0.01 (FBX cm). Import as **Generic** (checked with a Unity 6000.6.5f1 batch import: all files load, 0 import errors). Clips bind by transform path, so every `A_<Name>_*` clip plays on its own `SK_<Name>`. Do not share clips between characters (bone lengths differ).
- Rest pose: A-pose (Bloater / Brute: relaxed). Height = Meshy rig height. Unity skinned bounds read ~10-15 % taller (bounds padding). Knight = 1.75 m, so bosses are 2.3-2.9x the Knight.
- `Walking` / `Running` come with every rig (Meshy default human walk / run). The other clips come from the Meshy animation library (action id in the table). Several clips are long (5-8 s): set loop / trim ranges in the import or play them faster.
- Clips can carry root motion on `Hips` (X/Z). Freeze it (bake into pose, or zero Hips X/Z like `A_Zombie_Run`).
- Tri budget (STATE decisions, KAN-27): enemy 1.5-3k, boss 8-12k. Zombies are ~4k (Brute 6k), bosses ~12.4k after remesh. Add Unity Mesh LODs (as for `SK_Zombie`) before big hordes.
- Licence: Meshy output on the account plan. Free plan output is CC BY 4.0 (credit needed); check the plan before ship.
- Cost: 838 Meshy credits for the batch (includes 3 previews rejected because they did not match the brief). Balance after: 176.

## 2. Roster

| # | Character | Kind | Height | Tris | Clips (Meshy action id) | Folder |
|---|---|---|---|---|---|---|
| 1 | Graveyard Laborer | zombie | 1.72 m | 4007 | Attack (97), Death (189), Hit (178), Running (rig), Shamble (123), Walking (rig) | `Assets/_Project/Art/Characters/Enemies/GraveyardLaborer` |
| 2 | Fallen Militiaman | zombie | 1.78 m | 4063 | Attack (198), Death (187), Hit (178), Running (rig), Shamble (113), Walking (rig) | `Assets/_Project/Art/Characters/Enemies/FallenMilitiaman` |
| 3 | Rusted Watchman | zombie | 1.8 m | 4024 | Attack (219), Death (184), Hit (179), Running (rig), Shamble (123), Walking (rig) | `Assets/_Project/Art/Characters/Enemies/RustedWatchman` |
| 4 | Gravebound Knight | zombie | 1.85 m | 4069 | Attack (92), Death (190), Hit (178), Running (rig), Walk (119), Walking (rig) | `Assets/_Project/Art/Characters/Enemies/GraveboundKnight` |
| 5 | Plague Bloater | zombie | 1.8 m | 4037 | Burst (386), Death (189), Hit (178), Running (rig), Walk (119), Walking (rig) | `Assets/_Project/Art/Characters/Enemies/PlagueBloater` |
| 6 | Bone Archer | zombie | 1.78 m | 3944 | Death (183), Hit (178), Running (rig), Shoot (224), Walking (rig) | `Assets/_Project/Art/Characters/Enemies/BoneArcher` |
| 7 | Pyre Thrower | zombie | 1.75 m | 4046 | Death (189), Hit (178), Running (rig), Throw (239), Walk (123), Walking (rig) | `Assets/_Project/Art/Characters/Enemies/PyreThrower` |
| 8 | Bile Spitter | zombie | 1.7 m | 4157 | Death (187), Hit (178), Running (rig), Spit (386), Walk (123), Walking (rig) | `Assets/_Project/Art/Characters/Enemies/BileSpitter` |
| 9 | Frenzied Ghoul | zombie | 1.7 m | 4138 | Death (184), Hit (178), Leap (86), Running (rig), Sprint (16), Walking (rig) | `Assets/_Project/Art/Characters/Enemies/FrenziedGhoul` |
| 10 | Grave Brute | zombie | 2.3 m | 6177 | Death (190), Hit (179), Running (rig), Slam (127), Walk (119), Walking (rig) | `Assets/_Project/Art/Characters/Enemies/GraveBrute` |
| 11 | Hexbound Shaman | zombie | 1.75 m | 4059 | Cast (129), Death (189), Hit (178), Running (rig), Summon (126), Walking (rig) | `Assets/_Project/Art/Characters/Enemies/HexboundShaman` |
| 12 | The Butcher of Ashgrave | boss | 4.2 m | 12388 | Charge (16), Chop (237), Death (190), Roar (255), Running (rig), Spin (238), Walk (119), Walking (rig) | `Assets/_Project/Art/Characters/Bosses/AshgraveButcher` |
| 13 | The Pyre Lord | boss | 4.5 m | 12495 | CastFan (125), CastNova (130), Death (189), Roar (255), Running (rig), Slam (127), Walk (119), Walking (rig) | `Assets/_Project/Art/Characters/Bosses/PyreLord` |
| 14 | The Rotting Colossus | boss | 5.0 m | 12575 | Death (187), Grab (389), Roar (255), Running (rig), Slam (127), Throw (398), Walk (119), Walking (rig) | `Assets/_Project/Art/Characters/Bosses/RottingColossus` |
| 15 | The Bone Matriarch | boss | 4.0 m | 12359 | CastOrbs (131), CastRing (132), Death (185), Roar (386), Running (rig), Summon (126), Walking (rig) | `Assets/_Project/Art/Characters/Bosses/BoneMatriarch` |

Pose sheets (rest + one frame of every clip, Blender workbench render): `Docs/enemies/<Name>.png`.

## 3. Design per character

Numbers are first guesses in Chamber 1 terms (same units as `CombatBuilder.ZombieType` and `BossData`). Damage given as a share of the Knight max HP follows the boss rule (`maxHitShare`). Every telegraph >= 0.6 s (`BossData.MinTelegraph`). Every ranged shot stops on pillars / walls (`Enemy.Walkability`), like `BossProjectiles`.

### 1. Graveyard Laborer (`GraveyardLaborer`)

- Role: Melee tier 1 (replaces the base Shambler look).
- Attack: Melee claw swipe on contact. Same AI as today's Shambler.
- Stats: HP 100, speed 1.75 m/s, 12 dps, XP 5.
- Pose sheet: `Docs/enemies/GraveyardLaborer.png`. Meshy tasks: generate `01a13caf-39af-7627-9e9e-63e48f685d80`, remesh `01a13cb4-aec0-770c-9d4f-66221e113fb2`, rigging `01a13cb6-a5e2-74a8-97aa-545fd64f3aa8`.

### 2. Fallen Militiaman (`FallenMilitiaman`)

- Role: Melee tier 2.
- Attack: Melee punch combo on contact. A bit tougher than the Laborer.
- Stats: HP 140, speed 1.8 m/s, 14 dps, XP 6.
- Pose sheet: `Docs/enemies/FallenMilitiaman.png`. Meshy tasks: generate `01a13caf-3fb4-7589-b033-eaaff6cff6c5`, remesh `01a13cb4-b3cb-708a-a40b-e0e47ad94625`, rigging `01a13cb6-ae24-727f-b046-2ac3db471941`.

### 3. Rusted Watchman (`RustedWatchman`)

- Role: Melee tier 3 (armoured).
- Attack: Melee overhead swing. Armour: takes 25 % less damage from the front cone (90 deg).
- Stats: HP 200, speed 1.6 m/s, 16 dps, XP 8.
- Pose sheet: `Docs/enemies/RustedWatchman.png`. Meshy tasks: generate `01a13caf-4547-7267-8476-df33009f9839`, remesh `01a13cb4-b965-702d-9483-1b994d806bdd`, rigging `01a13cb7-1e42-76fb-8817-b0591ebf1471`.

### 4. Gravebound Knight (`GraveboundKnight`)

- Role: Melee tier 4 / elite, mini boss candidate (KAN-23).
- Attack: Two-hit combo with a lunge (1.5 m dash, 0.6 s ground warning). Knockback x0.3.
- Stats: HP 420, speed 1.5 m/s, 22 dps, XP 20.
- Pose sheet: `Docs/enemies/GraveboundKnight.png`. Meshy tasks: generate `01a13caf-4a11-75c9-beee-9293365aadc8`, remesh `01a13cb5-1275-70ef-8356-0d58bc601fad`, rigging `01a13cb7-5dc2-70d7-87b4-e1f900b3f0c3`.

### 5. Plague Bloater (`PlagueBloater`)

- Role: Suicide bomber / area denial.
- Attack: Walks to the Knight. In 2 m range: 1.0 s swell + green ground circle (r 2.8 m), then it bursts: 25 % max HP damage in the circle and a toxic cloud (r 2.2 m, 6 s, 15 dps). A kill by bolts bursts it too, so other zombies can be caught.
- Stats: HP 180, speed 1.2 m/s, XP 8.
- Pose sheet: `Docs/enemies/PlagueBloater.png`. Meshy tasks: preview `01a13cb4-b1b8-76e8-809e-926ec2048849`, refine `01a13cb7-72b7-76a1-89bc-ec438b22c35e`, remesh `01a13cb8-8401-70c8-baef-0d1d3997542c`, rigging `01a13cba-3a18-747a-94ac-9f77bb5fde15`.

### 6. Bone Archer (`BoneArcher`)

- Role: Ranged sniper.
- Attack: Keeps 7-9 m distance (backs off when the Knight comes near). Aim 0.8 s with a thin red line, then one fast arrow (14 m/s, 10 % max HP). Every 3rd volley: 3-arrow fan (30 deg). Arrows stop on pillars/walls. No bow in the mesh: attach a simple bow prop to the left hand.
- Stats: HP 80, speed 2.0 m/s, XP 7.
- Pose sheet: `Docs/enemies/BoneArcher.png`. Meshy tasks: preview `01a13caf-56e9-766c-8b34-4d4698bd87d1`, refine `01a13cb5-1daf-7721-97e2-b6e09263140e`, remesh `01a13cb6-750c-7613-b69d-c97d1f667869`, rigging `01a13cb8-7a7e-75cc-99c7-dd8776b2bb30`.

### 7. Pyre Thrower (`PyreThrower`)

- Role: Ranged artillery (lobbed fireballs).
- Attack: At 6-10 m: throws a fire pot in an arc (1.1 s flight) to where the Knight will be (lead aim). Landing circle (r 1.6 m) shows on the ground during the flight. Impact 12 % max HP + burning ground patch 3 s (8 dps). Pattern: single, single, then a 3-pot spread.
- Stats: HP 110, speed 1.6 m/s, XP 8.
- Pose sheet: `Docs/enemies/PyreThrower.png`. Meshy tasks: preview `01a13caf-5e40-766a-9f19-84782deff500`, refine `01a13cb5-2386-7535-ba82-a358e20fe7ff`, remesh `01a13cb6-cd61-7286-aff9-b4078ec62cd5`, rigging `01a13cb8-8298-76f9-8906-689efd09d8be`.

### 8. Bile Spitter (`BileSpitter`)

- Role: Ranged area denial.
- Attack: At 5-8 m: 0.7 s wind-up (throat glows), spits a slow acid glob (7 m/s) that leaves an acid puddle (r 1.4 m, 5 s, 12 dps, slows the Knight 30 %). Max 3 puddles per spitter.
- Stats: HP 90, speed 1.7 m/s, XP 7.
- Pose sheet: `Docs/enemies/BileSpitter.png`. Meshy tasks: preview `01a13cac-cfd5-72e3-ac7a-25d781a9d4f8`, refine `01a13cad-4a98-70ba-be68-6e893a27b50b`, remesh `01a13caf-37a0-7798-9466-6b42d13286f5`, rigging `01a13cb0-8df2-7296-a459-b96da1aab093`.

### 9. Frenzied Ghoul (`FrenziedGhoul`)

- Role: Fast flanker / leaper.
- Attack: Sprints (4.2 m/s, faster than the horde, slower than the Knight 5 m/s). At 4-6 m: crouch 0.5 s + red lane, then leaps 5 m (hit 10 % max HP). After the leap it is stunned 0.8 s (punish window).
- Stats: HP 60, speed 4.2 m/s, XP 6.
- Pose sheet: `Docs/enemies/FrenziedGhoul.png`. Meshy tasks: preview `01a13caf-6379-7641-a460-30c9c16d98ea`, refine `01a13cb5-29b5-72f2-a844-e2092400823b`, remesh `01a13cb6-2f76-712e-a179-f9a6a48611ba`, rigging `01a13cb7-ea0d-771b-82f3-9e0747f2f902`.

### 10. Grave Brute (`GraveBrute`)

- Role: Heavy elite (replaces the scaled-up Brute look).
- Attack: Ground slam: 0.9 s wind-up, then an expanding shockwave ring (inner r 0 to outer r 4.5 m over 0.6 s, ring width 0.8 m) the Knight must dash through or outrun. 18 % max HP. Knockback immune.
- Stats: HP 520, speed 1.25 m/s, 26 dps contact, XP 25 (elite).
- Pose sheet: `Docs/enemies/GraveBrute.png`. Meshy tasks: preview `01a13cb4-b7ca-7786-9d71-e339f07d566b`, refine `01a13cb6-8316-72a4-a986-e96e105a1a53`, remesh `01a13cb8-8f5a-777d-97a1-1f3f448f49c7`, rigging `01a13cba-45c0-729d-8817-8fe6fc21d288`.

### 11. Hexbound Shaman (`HexboundShaman`)

- Role: Support caster + bullet pattern.
- Attack: Keeps 8-11 m. Cast A (every 5 s): ring of 8 violet soul orbs fired outwards (slow, 5 m/s, gaps between orbs). Cast B (every 12 s): buffs zombies in 6 m (+30 % speed, violet tint, 5 s) or raises 2 Laborers. Teleports 6 m away when the Knight gets within 3 m (cooldown 6 s).
- Stats: HP 130, speed 1.5 m/s, XP 12.
- Pose sheet: `Docs/enemies/HexboundShaman.png`. Meshy tasks: preview `01a13cb4-5a26-718a-a549-a1a5ec4f95db`, refine `01a13cb5-a64d-7561-ab9d-de7dcd4dd0fa`, remesh `01a13cb7-0217-7432-80d0-d744b1953192`, rigging `01a13cb8-b464-7307-bd32-a204ee7faafa`.

### 12. The Butcher of Ashgrave (`AshgraveButcher`)

- Role: Boss (aggressive melee + charges).
- Attack: Phase 1: (a) cleaver chop, long lane 6 m x 1.6 m; (b) charge dash (reuse KAN-61 charge) with a 1 s stun on a pillar hit; (c) cleaver spin, 2.5 s whirl that follows the Knight slowly (r 3 m). Phase 2: (d) chain hook throw, lane 10 m, pulls the Knight 4 m; (e) triple charge. Uses the existing BossFight slam / fan / charge code most directly: good first boss to port.
- Stats: HP 8000, height 4.2 m.
- Status: in the game since KAN-108 (Realm 1 boss of every boss Chamber; numbers in `Assets/_Project/Data/Boss/Boss_Realm1_Butcher.asset`, attack list per phase, see STATE.md).
- Pose sheet: `Docs/enemies/AshgraveButcher.png`. Meshy tasks: preview `01a13cb4-6f2f-77f6-906e-4e4916688d19`, refine `01a13cb5-bcc2-75bd-a997-3f7f375e4788`, remesh `01a13cb7-174a-71a2-a472-9b58e5b900f5`, rigging `01a13cb8-c83d-712f-9a4f-6afa554645c3`.

### 13. The Pyre Lord (`PyreLord`)

- Role: Boss (fire caster), Realm 2 candidate.
- Attack: Phase 1: (a) fireball fan, 7 bolts in a 70 deg cone; (b) fire nova, ring of 16 fireballs from the body; (c) slam with a flame circle (r 4 m). Phase 2 (50 % HP): (d) rotating spiral, 2 arms of fireballs for 3 s (bullet hell, gaps to walk through); (e) meteor rain, 6 ground circles near the Knight, 1.0 s warning each. Every telegraph >= 0.6 s (BossData.MinTelegraph).
- Stats: HP 9000 (Chamber 1 terms), height 4.5 m.
- Status: in the game since KAN-109 (fight library, not in Realm 1's rotation: `-ss-boss-type PyreLord`; 8000 HP for the balance rule; flame slam and meteors leave burning patches; numbers in `Assets/_Project/Data/Boss/Boss_PyreLord.asset`, see STATE.md).
- Pose sheet: `Docs/enemies/PyreLord.png`. Meshy tasks: preview `01a13cb4-5db0-73d1-b1cb-bb10b675bae6`, refine `01a13cb5-ac13-74b2-812f-07911b02a187`, remesh `01a13cb7-5d0c-7329-8714-9edfbc4ea52d`, rigging `01a13cb9-5a24-71e4-bc65-00130f9ef35e`.

### 14. The Rotting Colossus (`RottingColossus`)

- Role: Boss (heavy bruiser + thrower).
- Attack: Phase 1: (a) double ground slam, two shockwave rings; (b) corpse boulder throw, lobbed, landing circle r 2.5 m, leaves a rubble obstacle 8 s; (c) grab: 1.2 s reach lane, a hit pulls the Knight in and deals 20 %. Phase 2: (d) roar summons 4 Frenzied Ghouls; (e) 3 boulders in a row. Slowest boss, biggest body.
- Stats: HP 11000, height 5.0 m.
- Status: in the game since KAN-110 (fight library, not in Realm 1's rotation: `-ss-boss-type RottingColossus`; 8200 HP for the balance rule, still the most HP of all bosses; grab drags the Knight in to the body; numbers in `Assets/_Project/Data/Boss/Boss_RottingColossus.asset`, see STATE.md).
- Pose sheet: `Docs/enemies/RottingColossus.png`. Meshy tasks: preview `01a13cb4-631c-73d2-8ec1-3425de328878`, refine `01a13cb5-b0db-7582-a82f-875043db4831`, remesh `01a13cb7-0eab-7466-9dea-ea9e361ad67b`, rigging `01a13cb8-bf0c-773e-87c6-e9e06d0893cc`.

### 15. The Bone Matriarch (`BoneMatriarch`)

- Role: Boss (bullet-hell sorceress).
- Attack: Floats, never melee. (a) homing skull orbs, 3 at a time, slow turn rate; (b) ring of 24 teal orbs with 3 gaps; (c) summon 3 Bone Archers. Phase 2: (d) cross beams, 4 rotating lanes (line telegraphs) for 4 s; (e) teleport to a random arena point every 8 s. Low HP, hard to reach.
- Stats: HP 7000, height 4.0 m.
- Pose sheet: `Docs/enemies/BoneMatriarch.png`. Meshy tasks: preview `01a13cb4-6919-763c-8ed7-914f787963ef`, refine `01a13cb6-1405-7211-9c12-7f5fc0416fcf`, remesh `01a13cb7-6925-73f9-aea7-ac1b08c860ce`, rigging `01a13cb9-14f4-70c5-9d1b-34ab390ab543`.

## 4. Known asset issues

- Hooded look repeats (Bone Archer, Pyre Thrower, Hexbound Shaman, Butcher). Textures differ (bone / ember / violet / black), but add a colour rim or emissive accent so they read apart at the gameplay camera.
- Bone Archer has no bow in the mesh (hands empty). Attach a bow prop to `LeftHand`.
- The Butcher came out as a hooded skeleton executioner with a cleaver-axe, not a fat butcher. The Rotting Colossus came out as a horned demon brute, not a corpse golem. Both read as big bosses; names kept.
- A few triangles stretch from the hands in some clips (Militiaman / Knight Attack, Knight Walk): auto-rig weight noise. Fix in Blender, or hide it with attack VFX if it shows in game.
- Plague Bloater arms clip into the belly in `Burst`.
- Graveyard Laborer, Fallen Militiaman, Rusted Watchman and Gravebound Knight are the four reference sheets (common-enemy tiers) made 3D.

## 5. Rebuild

Scripts in `Tools/Meshy/`: `roster.json` (prompts, heights, tris, action ids), `design.json` (section 3 text), `run_meshy.py` (resumable Meshy CLI driver; `UNTIL=preview` stops for a review), `to_unity.py` (Blender: GLB -> SK / A FBX + textures), `render_check.py` + `sheet.py` (pose sheets), `install.py` (export all into the repo), `make_doc.py` (this file). Needs a Meshy CLI 0.4.0 login (`npm exec --package=meshy-cli@0.4.0 -- meshy auth status`) and Blender 5.2. Scripts read `~/survivor-souls-assets` (Mac paths).
