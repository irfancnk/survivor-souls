# Survivor Souls balance sheet (repo copy)

Source of numbers: the KAN-75 power curve (`SkillPowerCurve`, 200 simulated runs, one random card of each level-up offer, no chest upgrades) with the shipped table `Data/Skills/SkillProgression_Run.asset`.

Print it again:

- Editor menu: `Survivor Souls/Debug/Print Balance Report`
- Batch: `Unity -batchmode -nographics -projectPath . -executeMethod SurvivorSouls.Editor.Skills.BalanceReport.PrintFromCommandLine -logFile <log>`
- Dev build: `-ss-power-curve`

The Confluence sheet (SS > 01 Product & Game Design) is the design copy. Keep the two in sync.

## KAN-86 single-target pass (2026-10-16)

### Decision

| Metric (growth over the bare Knight) | Target | Before | After |
| --- | --- | --- | --- |
| 1-target DPS, level 10 | at least 2.5x | 2.32x | 2.54x |
| 1-target DPS, level 20 | 4-6x | 3.80x | 4.36x |
| Crowd DPS, level 20 | (no hard cap) | 37.3x | 25.6x |
| Crowd / 1-target gap, level 20 | at most 6.5 | 9.8 | 5.9 |

When the ticket was written, the numbers were 2.1x for 1-target and 16.5x for crowd at level 20. That was before KAN-83 (Double Shot and Spread Shot). Double Shot then raised 1-target DPS to 3.8x. Spread Shot raised crowd DPS to 37x.

We did not use the ticket's example crowd target of 8-10x. Since KAN-92, horde health scales with a power index that includes crowd DPS, so more crowd power already makes zombies tougher. To reach 8-10x, we would have to cut Spread Shot almost completely. The real risk was the gap between crowd and 1-target damage, because boss health does not follow the power index. The `SingleTargetBalanceTests` test guards these targets.

### Changes

- Pierce: each enemy pierced lowers the damage by 33 %. Ricochet: each bounce lowers the damage by 30 %. These are the Archero rules. Implemented in `BoltShot.PierceFalloff` / `BounceFalloff` and the `AutoShooter` fields `pierceFalloff` / `bounceFalloff`. The power curve uses `PowerSnapshot.HitFactor`.
- Sharpened Bolts: +20 / 38 / 58 / 80 / 102 / 125 % damage (was 20 / 36 / 52 / 66 / 80 / 95). Level 1 stays the same (KAN-71 rule). The late levels grow the most.
- Quick Draw: +15 / 30 / 46 / 62 / 76 / 90 % attack speed (was 15 / 28 / 40 / 51 / 62 / 72).
- Keen Eye: crit damage +25 / 45 / 60 / 80 / 100 % (was 25 / 40 / 55 / 70 / 90). Crit chance stays the same.
- Spread Shot: side bolts deal 50 / 55 / 60 % damage (was 50 / 65 / 80).
- Guarantees: level 2 always offers Sharpened Bolts, and level 8 always offers Quick Draw. The existing guarantees stay: level 3 Double Shot, level 4 Ember Bolts, level 6 Spread Shot.

### Power curve after the change

| Lv | skills | bolt dmg | shots/s | crit | bolts | chain | pierce | bounce | burn | 1-target DPS | crowd DPS | crowd x |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 0.0 | 100 | 1.00 | 0 % | 1.00 | 0.00 | 0.00 | 0.00 | 0 | 100 | 100 | 1.0x |
| 5 | 3.5 | 114 | 1.08 | 2 % | 1.00 | 0.37 | 0.19 | 0.11 | 8 | 181 | 226 | 2.3x |
| 10 | 6.6 | 124 | 1.19 | 6 % | 2.30 | 0.55 | 0.51 | 0.54 | 10 | 254 | 735 | 7.3x |
| 15 | 8.5 | 135 | 1.26 | 9 % | 3.07 | 0.69 | 0.86 | 0.95 | 13 | 334 | 1473 | 14.7x |
| 20 | 10.1 | 145 | 1.35 | 12 % | 3.81 | 0.83 | 1.17 | 1.26 | 15 | 436 | 2562 | 25.6x |

Horde scaling (KAN-92, unchanged asset) at level 20: power index 8.11, zombie health x3.51 at the Chamber start and x4.56 at the end. The Knight's edge is 1.24x on 1-target and 7.30x on crowd. All `HordeScalingTests` shipped-balance checks still pass. The first boss with the typical 10-pick build (`BossIntroTests` balance check) stays inside the KAN-22 band of 60-90 s.

### Open points

- Phone feel check for these numbers: KAN-53 (Chamber balance pass) and KAN-85 (boss playtest).
- No boss-damage perk yet. If bosses still feel long on the phone, add one (follow-up ticket).

## KAN-77 Ember Bolts level perks (2026-10-16)

| Lv | Burn | Length | Perk | Card text |
| --- | --- | --- | --- | --- |
| 1 | 25 /s | 3 s | - | Hits burn: 25 fire damage/s for 3 s |
| 2 | 40 /s | 3 s | - | Hits burn: 40 fire damage/s for 3 s |
| 3 | 55 /s | 4 s | Spread: `BurnSpread` 1 | Burn 55/s for 4 s, spreads to a foe |
| 4 | 75 /s | 5 s | Spread 1 + death blast: `BurnDeathBlast` 0.6 | Burn 75/s for 5 s + 60% death blast |

- Spread: every burn tick (0.5 s) of a bolt-lit enemy sets the nearest non-burning foe within 2.5 m on fire, with the same damage per second and length. One step only: a fire caught from a neighbour does not spread on. A bolt hit on a caught fire makes it a bolt fire again.
- Death blast: when a burning enemy dies (any cause except a Bloater's own burst), it deals 60 % of the current bolt damage to every foe within 2.2 m, one frame later (queued like Soul Burst). A blast kill of a burning foe blasts again.
- Tunables on the effect prefab `Prefabs/Skills/FX_Skill_EmberBolts.prefab` (`BurnOnHitEffect`): `spreadRadius` 2.5, `blastRadius` 2.2, `spreadEmbers` 8, `blastEmbers` 22.
- Not in the power curve yet: `SkillPowerCurve` models burn as a 1-target DPS add only. Spread and blast are crowd damage, so the KAN-92 horde scaling does not see them. Follow-up ticket if the phone run shows Ember L4 too strong.

## KAN-163 boss damage pass (2026-10-16)

### Decision

- Boss hits hurt a lot: 3 clean hits of each boss's biggest attack kill a fresh Knight (400 HP). Every damaging attack still warns at least 0.6 s (also after the phase 2 wind-up factor).
- All boss attack shares x1.8 (rounded), the Butcher numbers from the ticket table. Bone Matriarch Cross Beams 34 % (x2.1) so her biggest attack also kills in 3 hits.
- Max-hit cap 0.35 -> **0.5** (no one-shot, no two-shot from full health).
- Phase 2: every boss hit x**1.15** on top (`BossData.phase2Damage`), then the cap. Burning patches (Pyre Lord) are not scaled by phase.
- Contact damage (walking into the boss body): 30 -> **45** dps (11 % of a fresh Knight per second), flat, below every telegraphed big hit. The Bone Matriarch has none.
- Damage stays a share of max health (decision for now). Future gear armor (KAN-139) should subtract a `DamageTaken` stat after the share, so armor matters against bosses.
- Boss health and fight length unchanged (fight-time band 60-90 s, `BossIntroTests` balance check).

### Hits to kill a fresh Knight (400 HP)

Printed by `BossDamageBalanceTests.HitsToKill_Table` (EditMode). "-" = the attack is not in that phase.

| Boss | Attack | Share (was) | Phase 1 | Phase 2 |
| --- | --- | --- | --- | --- |
| Butcher of Ashgrave | Cleaver Chop | 40 % (22) | 3 | 3 |
| | Cleaver Spin (per tick, 0.6 s) | 14 % (8) | 8 | 7 |
| | Charge | 36 % (20) | 3 | - |
| | Chain Hook | 18 % (10) | - | 5 |
| | Triple Charge (each dash) | 30 % (18) | - | 3 |
| | Black Fireballs | 11 % (6) | 10 | 8 |
| Pyre Lord | Flame Slam (+ burn 9 %/s, was 5) | 36 % (20) | 3 | 3 |
| | Fireball Fan (volley hits once) | 22 % (12) | 5 | 4 |
| | Fire Nova (volley hits once) | 25 % (14) | 4 | 4 |
| | Fire Spiral | 29 % (16) | - | 3 |
| | Meteor Rain (+ burn 9 %/s, was 5) | 27 % (15) | - | 4 |
| Rotting Colossus | Double Slam (per ring) | 29 % (16) | 4 | 3 |
| | Grab | 36 % (20) | 3 | 3 |
| | Corpse Boulder | 36 % (20) | 3 | - |
| | Boulder Volley (each boulder) | 36 % (20) | - | 3 |
| Bone Matriarch | Homing Skulls (volley hits once) | 22 % (12) | 5 | 4 |
| | Soul Ring (volley hits once) | 25 % (14) | 4 | 4 |
| | Cross Beams (cast hits once) | 34 % (16) | - | 3 |

### Open points

- Phone playtest of the Butcher and one other boss (deaths from readable mistakes, fight time): human step, see KAN-163 follow-up.
- Hearts (KAN-60) and life steal unchanged; re-check after the playtest.
- The Confluence balance sheet sync (KAN-186) should also take this table.

## KAN-191 level 10 single-target back to 2.5x (2026-10-16)

### Problem

KAN-86 set the target "level 10 single-target DPS at least 2.5x the bare Knight". The 200-run power curve read 2.54x at seed 75, but that was seed luck. At 2000 runs the true mean on main was 2.42-2.45x (seeds 75 / 11 / 23). KAN-93 moved the test to 2000 runs and lowered the floor to 2.4x.

### Options measured (2000 runs, level 10 single-target at seeds 75 / 11 / 23)

| Change | L10 | L20 | Gap L20 |
| --- | --- | --- | --- |
| Main (before) | 2.45 / 2.42 / 2.43 | 4.13 | 5.9 |
| Sharpened Bolts +40-130 % | 2.46 / 2.44 / 2.45 | 4.19 | 5.9 |
| Sharpened Bolts +42-135 % | 2.48 / 2.46 / 2.46 | 4.25 | 5.9 |
| Quick Draw +33-94 % | 2.47 / 2.45 / 2.46 | 4.20 | 5.9 |
| Quick Draw guarantee level 8 -> 5 | 2.44 / 2.43 / 2.45 | 4.13 | 6.0 |
| Fleet Foot weight 0.7, Vigor 0.8 | 2.47 / 2.48 / 2.50 | 4.30 | 6.1 |
| Sharpened Bolts 42-135 % + Quick Draw 33-94 % + Fleet Foot 0.6 | 2.53 / 2.53 / 2.54 | 4.48 | 6.0-6.1 |
| **Sharpened Bolts 42-135 % + Quick Draw 30-94 % + Fleet Foot 0.5 (shipped)** | **2.52 / 2.53 / 2.55** | **4.49** | **6.1-6.2** |

Level steps alone move level 10 only a little: at level 10 most runs own Sharpened Bolts / Quick Draw at level 1-2. An earlier Quick Draw guarantee did not help (the random card of the offer is still 1 in 3). The offer mix moved it most: Fleet Foot (move speed, no damage, saturates at 33 %) crowded the early Common offers. Quick Draw level 2 stays at +30 %: with +33 % the 10-pick Bone Matriarch fight (`BossIntroTests` balance check, band 60-90 s) dropped to 59.2 s. With the shipped numbers it is about 60.5 s (Drowned Warden about 62 s).

### Decision

- Sharpened Bolts: +20 / **42 / 66 / 90 / 112 / 135** % damage (was 20 / 38 / 58 / 80 / 102 / 125). Level 1 stays (KAN-71 rule).
- Quick Draw: +15 / 30 / **50 / 66 / 80 / 94** % attack speed (was 15 / 30 / 46 / 62 / 76 / 90). Levels 1-2 stay.
- Fleet Foot: offer weight **0.5** (was 1). Same levels. Vigor stays at 1: survival matters more since the KAN-163 boss hits.
- `SingleTargetBalanceTests`: level 10 floor back to **2.5x** on 2000 runs. Level 20 stays inside 4-6x, gap under 6.5.
- The Confluence balance sheet sync is KAN-186.

### Power curve after the change (2000 runs, seed 75)

| Lv | skills | bolt dmg | shots/s | crit | bolts | chain | pierce | bounce | burn | 1-target DPS | crowd DPS | crowd x |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | 0.0 | 100 | 1.00 | 0 % | 1.00 | 0.00 | 0.00 | 0.00 | 0 | 100 | 100 | 1.0x |
| 5 | 3.5 | 115 | 1.08 | 2 % | 1.00 | 0.37 | 0.25 | 0.13 | 9 | 183 | 240 | 2.4x |
| 10 | 6.6 | 126 | 1.19 | 6 % | 2.27 | 0.50 | 0.59 | 0.52 | 11 | 252 | 735 | 7.3x |
| 15 | 8.9 | 139 | 1.27 | 9 % | 3.11 | 0.68 | 0.94 | 0.94 | 14 | 341 | 1535 | 15.4x |
| 20 | 10.5 | 152 | 1.36 | 11 % | 3.86 | 0.82 | 1.26 | 1.33 | 17 | 449 | 2723 | 27.2x |

Growth at level 20: 1-target 4.49x, crowd 27.2x, gap 6.1. All `HordeScalingTests` checks pass (horde health follows the higher crowd power). First boss with the 10-pick build: Drowned Warden 62 s, Butcher / Pyre Lord 69 s, Rotting Colossus 71 s, Bone Matriarch 61 s (band 60-90 s).
