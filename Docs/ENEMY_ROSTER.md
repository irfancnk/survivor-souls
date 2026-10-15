# Enemy and Boss Roster (Meshy batch 2026-10-15)

15 rigged and animated characters were made: 11 zombies and 4 bosses. KAN-128 (2026-10-15, user decision) cut 3 zombies: Fallen Militiaman, Gravebound Knight and Bone Archer. 12 stay in the game: 8 zombies and 4 bosses. Row and section numbers below are kept as they were (2, 4 and 6 are gone), so references like `ENEMY_ROSTER.md 3.15` in the code stay valid. Jira tickets carry the implementation (KAN-103).

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
| 3 | Rusted Watchman | zombie | 1.8 m | 4024 | Attack (219), Death (184), Hit (179), Running (rig), Shamble (123), Walking (rig) | `Assets/_Project/Art/Characters/Enemies/RustedWatchman` |
| 5 | Plague Bloater | zombie | 1.8 m | 4037 | Burst (386), Death (189), Hit (178), Running (rig), Walk (119), Walking (rig) | `Assets/_Project/Art/Characters/Enemies/PlagueBloater` |
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

### 3. Rusted Watchman (`RustedWatchman`)

- Role: Melee tier 2 (armoured). Takes the wave shares of the cut Militiaman and Gravebound Knight (KAN-128); the elite melee role is the Grave Brute's.
- Attack: Melee overhead swing. Armour: takes 25 % less damage from the front cone (90 deg).
- Stats: HP 200, speed 1.6 m/s, 16 dps, XP 8.
- Pose sheet: `Docs/enemies/RustedWatchman.png`. Meshy tasks: generate `01a13caf-4547-7267-8476-df33009f9839`, remesh `01a13cb4-b965-702d-9483-1b994d806bdd`, rigging `01a13cb7-1e42-76fb-8817-b0591ebf1471`.

### 5. Plague Bloater (`PlagueBloater`)

- Role: Suicide bomber / area denial.
- Attack: Walks to the Knight. In 2 m range: 1.0 s swell + green ground circle (r 2.8 m), then it bursts: 25 % max HP damage in the circle and a toxic cloud (r 2.2 m, 6 s, 15 dps). A kill by bolts bursts it too, so other zombies can be caught.
- Stats: HP 180, speed 1.2 m/s, XP 8.
- Pose sheet: `Docs/enemies/PlagueBloater.png`. Meshy tasks: preview `01a13cb4-b1b8-76e8-809e-926ec2048849`, refine `01a13cb7-72b7-76a1-89bc-ec438b22c35e`, remesh `01a13cb8-8401-70c8-baef-0d1d3997542c`, rigging `01a13cba-3a18-747a-94ac-9f77bb5fde15`.

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
- Status: in the game since KAN-108 (Realm 1 boss of Chamber 5 since KAN-112, rotation `Assets/_Project/Data/Boss/BossRotation_Realm1.asset`; numbers in `Assets/_Project/Data/Boss/Boss_Realm1_Butcher.asset`, attack list per phase, see STATE.md).
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
- Status: in the game since KAN-110 (Realm 1 boss of Chamber 10 since KAN-112, rotation `BossRotation_Realm1.asset`; also `-ss-boss-type RottingColossus`; 8200 HP for the balance rule, still the most HP of all bosses; grab drags the Knight in to the body; numbers in `Assets/_Project/Data/Boss/Boss_RottingColossus.asset`, see STATE.md).
- Pose sheet: `Docs/enemies/RottingColossus.png`. Meshy tasks: preview `01a13cb4-631c-73d2-8ec1-3425de328878`, refine `01a13cb5-b0db-7582-a82f-875043db4831`, remesh `01a13cb7-0eab-7466-9dea-ea9e361ad67b`, rigging `01a13cb8-bf0c-773e-87c6-e9e06d0893cc`.

### 15. The Bone Matriarch (`BoneMatriarch`)

- Role: Boss (bullet-hell sorceress).
- Attack: Floats, never melee. (a) homing skull orbs, 3 at a time, slow turn rate; (b) ring of 24 teal orbs with 3 gaps; (c) summon 4 Graveyard Laborers (the doc first said 3 Bone Archers; cut, KAN-128). Phase 2: (d) cross beams, 4 rotating lanes (line telegraphs) for 4 s; (e) teleport to a random arena point every 8 s. Low HP, hard to reach.
- Stats: HP 7000, height 4.0 m.
- Status: in the game since KAN-111 (fight library, not in Realm 1's rotation: `-ss-boss-type BoneMatriarch`; summons 4 Graveyard Laborers instead of the cut Bone Archers (KAN-128); never melees, holds 6-9 m, hovers; teleport every 8 s in phase 2; numbers in `Assets/_Project/Data/Boss/Boss_BoneMatriarch.asset`, see STATE.md).
- Pose sheet: `Docs/enemies/BoneMatriarch.png`. Meshy tasks: preview `01a13cb4-6919-763c-8ed7-914f787963ef`, refine `01a13cb6-1405-7211-9c12-7f5fc0416fcf`, remesh `01a13cb7-6925-73f9-aea7-ac1b08c860ce`, rigging `01a13cb9-14f4-70c5-9d1b-34ab390ab543`.

## Spawn tables (KAN-112)

Realm 1 wave table `Assets/_Project/Data/Combat/EnemyWaves_Realm1.asset`, entry field `fromChamber`. Chamber 1: Graveyard Laborer, Rusted Watchman, Runner, Grave Brute, Frenzied Ghoul (melee only). Chamber 2: + Pyre Thrower, Bile Spitter. Chamber 3: + Plague Bloater. Chamber 4: + Hexbound Shaman. The first 45 s of every Chamber are melee only. Shooters (types with a hold band) are capped to 20 % of the phase cap, at most 6 alive. Bosses: Butcher (Chamber 5), Rotting Colossus (Chamber 10); Pyre Lord and Bone Matriarch wait for later Realms (KAN-57).

## 4. Known asset issues

- Hooded look repeats (Pyre Thrower, Hexbound Shaman, Butcher). Textures differ (bone / ember / violet / black), but add a colour rim or emissive accent so they read apart at the gameplay camera.
- The Butcher came out as a hooded skeleton executioner with a cleaver-axe, not a fat butcher. The Rotting Colossus came out as a horned demon brute, not a corpse golem. Both read as big bosses; names kept.
- Plague Bloater arms clip into the belly in `Burst`.
- Graveyard Laborer and Rusted Watchman are two of the four reference sheets (common-enemy tiers) made 3D. The other two (Fallen Militiaman, Gravebound Knight) and the Bone Archer were cut (KAN-128): art, prefabs, pose sheets and wave slots are gone; the raw Meshy jobs stay in `~/survivor-souls-assets/meshy_jobs` (outside git).

## 5. Rebuild

Scripts in `Tools/Meshy/`: `roster.json` (prompts, heights, tris, action ids), `design.json` (section 3 text), `run_meshy.py` (resumable Meshy CLI driver; `UNTIL=preview` stops for a review), `to_unity.py` (Blender: GLB -> SK / A FBX + textures), `render_check.py` + `sheet.py` (pose sheets), `install.py` (export all into the repo), `make_doc.py` (this file). Needs a Meshy CLI 0.4.0 login (`npm exec --package=meshy-cli@0.4.0 -- meshy auth status`) and Blender 5.2. Scripts read `~/survivor-souls-assets` (Mac paths).

## 6. Pets (Meshy batch 2026-10-15, assets only)

Three flying familiars that fight for the Knight. Winged humanoid bodies on purpose: the Meshy auto-rig only rigs humanoids, so a dragon or bird would have no animation. The wings move with the body but do not flap (no wing bones). Show them floating at about shoulder height (0.95 m) with a small bob, as in the Bestiary viewer. No game code uses them yet.

| Rarity | Pet | Height | Tris | Clips (Meshy action id) | Folder |
|---|---|---|---|---|---|
| epic | Cinder Seraph | 0.9 m | 5197 | Cast (129), Hover (243), Running (rig), Walking (rig) | `Assets/_Project/Art/Characters/Pets/CinderSeraph` |
| rare | Gargoyle Familiar | 0.8 m | 4154 | Cast (125), Hover (244), Running (rig), Walking (rig) | `Assets/_Project/Art/Characters/Pets/GargoyleFamiliar` |
| normal | Lantern Sprite | 0.6 m | 3087 | Cast (130), Hover (245), Running (rig), Walking (rig) | `Assets/_Project/Art/Characters/Pets/LanternSprite` |

### Cinder Seraph (epic)

- Role: Epic pet (fire, homing).
- Attack: Floats over the Knight's right shoulder. Every 1.2 s fires 2 homing ember bolts at the nearest enemy (turn rate capped, 9 m/s). Every 6 s a burst of 8 embers in a ring around the Knight that sets enemies on fire (uses the Burn status of the Ember Bolts skill). Fires while the Knight moves.
- Stats: Bolt 45 % of the Knight's bolt damage, ring 30 % + burn 2 s, range 10 m.
- Pose sheet: `Docs/enemies/CinderSeraph.png`. Meshy tasks: preview `01a13f32-7b5b-714e-b2ec-30120e34fc16`, refine `01a13f33-67f9-7537-ac12-985a648a429e`, remesh `01a13f34-c520-735a-88b9-e18b6577cef4`, rigging `01a13f36-6dbb-735a-9c76-5fea3b1fedc6`.

### Gargoyle Familiar (rare)

- Role: Rare pet (stone, pierce).
- Attack: Hovers behind the Knight. Every 1.5 s throws a stone shard that pierces up to 2 enemies. Every 10 s it dives on the densest group in 6 m and slams (small stun 0.5 s, knockback), then flies back.
- Stats: Shard 40 % of the Knight's bolt damage, pierce 2, slam 60 % + stun 0.5 s, range 9 m.
- Pose sheet: `Docs/enemies/GargoyleFamiliar.png`. Meshy tasks: preview `01a13f33-6859-72e3-b881-6bc32825b65c`, refine `01a13f34-7331-7738-8834-33fc8d7966f0`, remesh `01a13f35-8180-76cb-b393-f0f0efb05d80`, rigging `01a13f37-2df9-735c-848f-1a20a88f13c5`.

### Lantern Sprite (normal)

- Role: Normal pet (light, support).
- Attack: Bobs beside the Knight. Every 1.8 s shoots a slow light mote at the nearest enemy. Its lantern also pulls in XP wisps from 1.5 m further away.
- Stats: Mote 30 % of the Knight's bolt damage, range 8 m, +1.5 m pickup radius.
- Pose sheet: `Docs/enemies/LanternSprite.png`. Meshy tasks: preview `01a13f33-6bc2-7589-943f-294c14bf7e08`, refine `01a13f34-743b-72cf-9f39-9a012912a421`, remesh `01a13f35-817f-714f-8f71-0e41123ab538`, rigging `01a13f37-2cfc-7635-85c9-0d7162961b90`.

Pet cost: 138 Meshy credits (balance after: 38). Files follow section 1 (SK / A / T naming, Generic rig, 24 bones, root scale 0.01). Not yet imported in a Unity batch run: the first import makes the .meta files.

## 7. Hero: The Shadow Ranger (Meshy 2026-10-15 + Blender rebuild, assets only)

New main hero model: a black hooded bow ranger with a long cape (Lord of the Rings ranger look). Replaces the crossbow Knight look when KAN-153 lands. No game code uses it yet.

**Build:** `Tools/Meshy/build_shadow_ranger.py` (headless Blender) turns the raw Meshy rig (`rigged_meshy.glb` in the job folder) into the final asset. It replaces the first fix (`fix_ranger_weights.py`, deleted). Run:
`blender -b --python Tools/Meshy/build_shadow_ranger.py -- <job_dir> --stage B --export`, then `Tools/Meshy/install.py` (Unity FBX) and `to_web.py` (viewer). Work file for hand edits: `~/survivor-souls-assets/models/shadow_ranger/ShadowRanger.blend` (everything packed; actions Shoot / Idle / Walking / Running).

**What the build does**
- Merges the remesh triangle soup (26.8k -> 9.9k verts) and smooths normals (hard edges > 40 deg kept). Removes the faceted look.
- Cuts the fused Meshy bow, string and arrow (the "drawn bow" preview pose left a string from both tips to the chest and an arrow across the body: the tangled strings). Region fit from vertex plots.
- Quiver arrows bound 100 % to `Spine`.
- Face: a hood shadow painted into the base colour (smooth height ramp, skin texels only), plus a dark ranger cowl `ShadowRanger_Mask` (raycast onto the face, smoothed, 11 mm off the skin, drapes from the chin into the collar), skinned to `Head`.
- New black recurve bow `ShadowRanger_Bow` (1.24 m, grip wrap, nocks), built around the hand in the aim pose and mapped to rest: 100 % `LeftHand`.
- New string `ShadowRanger_String`: two tubes from the tips to a new bone `BowString` (child of `LeftHand`); the middle follows the draw hand.
- New arrow `ShadowRanger_Arrow` on a new bone `Arrow` (child of `Spine`): at rest it sits in the quiver; in Shoot it rides the string and goes back to the quiver on release (game spawns the flying arrow then).
- Right hand curled into a fist (no finger bones: fingers past the knuckle line bent towards the palm in the mesh). The left hand was already closed round the grip; old grip / arrow flakes inside it are cut.
- Clips, hand-keyed with two-bone IK and baked: `Shoot` (1.0 s loop, 30 fps: ready 0-4, draw 4-14, hold 14-19, **release frame 19 = 0.63 s**, follow-through 19-25, next arrow 27-30). Stance after the user's Legolas reference: hips / feet turned 82 deg off the shot line (feet perpendicular), chest opened back to 46 deg, head fully to the target; bow canted 18 deg (top limb to the archer's right); raise-and-draw (bow arm and string hand start 16 cm low at the set, rise into line during the draw); compact chin anchor (user review 4): the build searches a grid round the chin and keeps the point closest to "just under and beside the chin" whose full draw and follow-through put no arm vertex inside head / hood / cowl (found 2.5 cm from the ideal); elbow tucked, short follow-through past the jaw. The draw hand, forearm and follow-through never enter the head: `Tools/Meshy/check_ranger_clipping.py` reports at most 2 shoulder vertices under the hood hem per frame (was 36 arm vertices up to 5.9 cm deep) and `Idle` (2 s, bow carried upright, breathing). `Walking` / `Running`: Meshy rig clips rebaked: Meshy legs, torso, head and right arm (read from the source F-curves), plus a hand-keyed bow-carry left arm (bow low at the hip, held by the grip, upright and leaning forward 26 / 34 deg, string facing back, small swing in time with the stride). The raw Meshy run pumped the left arm to the face, so the bow swung flat. Rest keys for `Arrow` / `BowString`. Carry hand 16 cm out from the shoulder line, so the limbs clear the thigh / coat (`Tools/Meshy/check_ranger_bow.py`: worst contact 0.4-0.7 cm cloth graze per clip; the grip inside the fist is ignored).
- Preview film: `Tools/Meshy/film_shadow_ranger.py` (EEVEE, 1280x720, clips back to back with labels) -> `~/survivor-souls-assets/models/shadow_ranger/ShadowRanger_preview.mp4` (not in git). Clips export one NLA track each (exporting the active action baked a frozen pose).

**Unity files** (`Assets/_Project/Art/Characters/Heroes/ShadowRanger/`)
- `SK_ShadowRanger.fbx`: 26 bones (Meshy 24 + `BowString`, `Arrow`), meshes Body (19.9k tris, BaseColor 2048 + Normal 1024, painted face), Mask, Bow, String, Arrow. Materials without textures (make URP Lit): Bow wood RGB 0.030/0.026/0.024 rough 0.38; grip leather 0.11/0.065/0.035; string 0.55/0.52/0.45; cowl cloth 0.022/0.021/0.022 rough 0.85; arrow shaft 0.06/0.05/0.045; arrowhead steel 0.35/0.36/0.38 metal 0.9.
- `A_ShadowRanger_Shoot.fbx`, `_Idle.fbx`, `_Walking.fbx`, `_Running.fbx`. Fire the projectile on the release frame (0.63 s of the 1.0 s clip; scale the state speed to the fire interval). Spawn it at the arrow bone position on that frame, along the bone Y axis.
- Height 1.78 m (rig). Skinned bounds read taller because the bow tip rises above the head in the rest pose.

**Known limits:** no finger bones (the draw hand is an open hand, posed palm down); cape has no cloth sim; 21k tris above the hero budget (add Mesh LODs); no hit / death clips yet (Meshy credits 0: hand-key them with the same build script, or 3 credits each later); a small strip of cheek shows beside the cowl.

- Design: Same Archero rule as today: stands still to shoot the nearest enemy in range. Hand-keyed bow cycle (1 s, loops): side-on stance with the left shoulder to the target, bow arm locked out, string drawn to the jaw, release at 0.63 s, follow-through past the ear, next arrow from the quiver.
- Stats: Slightly stronger than the Knight: arrow damage 105 (Knight bolt 100), one shot every 0.9 s (Knight 1.0 s), range 11 m (Knight 10 m), arrow speed 30 m/s (bolt 26). About +17 % damage per second. HP 400 as today.
- Pose sheet: `Docs/enemies/ShadowRanger.png` (Shoot ready / full draw / follow-through, Idle, Walking, Running, Shoot side). Meshy tasks: preview `01a13f57-6aed-7656-b86a-478e3a999999`, refine `01a13f58-eca0-77e2-ba5e-008a14656854`, rigging `01a13f59-ce9a-755c-be1a-c740e239ad26`. Cost 38 credits.
