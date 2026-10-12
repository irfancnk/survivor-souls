# Survivor Souls — Initial Game Plan (v0.1)

Date: 2026-10-11 · Jira: KAN-5 · Owner: Irem

## 1. Vision

Survivor Souls is a portrait, one-thumb action roguelite for iOS and Android.
**Design language = Archero / Archero 2.** Same menus, same loop, same meta (chests, daily/weekly quests, talents, gear merge).
**Look = Dark Souls.** Grounded, realistic, gothic. Worn steel, stone, fog, bonfires, embers. Dark but not dismal: warm fire light, readable silhouettes, saturated VFX for skills and loot.

One line pitch: *"Archero's loop, in a Lordran-style world. Run, stop, strike. Rest at the bonfire. Get stronger."*

### Pillars
1. **One thumb, ten seconds to fun.** Move = dodge, stop = attack (Archero rule). Readable on a 6" screen.
2. **Every run is a new build.** Pick 1 of 3 skills on level up. Stack, combo, break the game.
3. **Weight and grit.** Heavy hits, hit-stop, blood/spark VFX, boss telegraphs, a "YOU DIED" screen. Souls tone, mobile pace.
4. **Always a reason to come back.** Energy, daily/weekly quests, chests, events, battle pass.
5. **Performance first.** 60 FPS on mid-tier phones (e.g. Galaxy A54, iPhone 11). 30 FPS floor on low tier.

## 2. Archero → Survivor Souls mapping

| Archero thing | Survivor Souls name / look |
|---|---|
| Hero (archer) | **Ashen Knight** (and other unlockable "Undead" heroes) |
| Chapter (50 rooms) | **Realm** (e.g. Undead Burg, Blighted Marsh, Cathedral of Ash) |
| Room / stage | **Chamber**; exit = fog gate |
| Level-up skill pick (1 of 3) | **Ember Boon** pick, shown as 3 rune cards |
| Angel room | **Fire Keeper** shrine (heal or boon) |
| Devil room | **Covenant** altar (trade max HP for a strong boon) |
| Boss every 5/10 rooms | **Lord / Mini-boss** behind a fog gate |
| Energy | **Estus** (5 per run, refills over time) |
| Gold / Gems | **Souls** (soft) / **Ember Shards** (hard) |
| Golden / Obsidian chest | **Iron Reliquary** / **Obsidian Reliquary** |
| Talents (random lottery) | **Bonfire Kindling** (random stat upgrade, souls cost) |
| Equipment + merge (Common→Mythic) | Same slots, same merge chain, souls-style gear names |
| Pet / Locket / Book | **Spirit summon** / **Talisman** / **Tome** |
| Daily / weekly quests + activity chests | **Oaths** (daily) / **Pilgrimage** (weekly) with activity track chests |
| Battle pass | **Path of the Unkindled** |
| Death screen | "YOU DIED" + revive (ad or shards) |

## 3. Core loop (in run)

1. Enter Realm, spend 5 Estus.
2. Chamber: enemies spawn. **Move to dodge, stop to auto-attack** nearest enemy.
3. Clear chamber → souls + XP orbs fly to hero. Fog gate opens.
4. Level up → pick 1 of 3 boons (multishot, ricochet, fire blade, frost aura, orbiting swords, lifesteal, etc.).
5. Every 5 chambers: shrine or covenant event. Every 10: Lord boss.
6. Die or clear Realm → results screen: souls, gear drops, XP. Revive offer once.

**Archero 2 extras (phase 2):** endless "Survival" mode (Vampire Survivors style waves on open arena), Sky Tower style daily challenge, co-op later.

### Combat feel (what makes it "Souls")
- Enemy wind-up telegraphs (red ground decals), boss phases, stagger bar on bosses.
- Hit-stop 40–60 ms, camera shake small, blood/spark decals pooled.
- Melee + ranged heroes. First hero: sword-thrower knight (ranged auto-attack keeps Archero feel; melee hero later).
- No stamina in v1 (too complex for one thumb). Maybe as "dodge roll" skill later.

## 4. Meta systems (out of run)

### 4.1 Home / main menu (Archero bottom tab bar)
Tabs, left→right: **Shop · Gear · Realms (home) · Talents · Settings/Events**.
Top bar: player level, Estus, Souls, Shards. Home: big Realm card + PLAY. Side bubbles: quests, mail, events, battle pass, ads chest.
Current UI Toolkit main menu becomes the "Realms" tab.

### 4.2 Gear
Slots: Weapon, Armor, Ring ×2, Spirit ×2, Talisman, Tome.
Rarity merge: Common → Great → Rare → Epic → Epic+ → Legendary → Legendary+ → Mythic (3 same = 1 higher).
Upgrade with souls + scrolls (Archero style). Weapon decides attack pattern.

### 4.3 Heroes
Unlock with shards or events. Each hero: base stats + passive + skin. 3 at launch (Ashen Knight, Pyromancer, Sellsword).

### 4.4 Chests (Reliquaries)
- Iron Reliquary: souls key, drops common–rare gear.
- Obsidian Reliquary: shards, drops rare+ gear, pity counter (epic guaranteed every 10).
- Free chest by ad every few hours.
- **Note:** show drop rates in-game (Apple + Google rule, also some markets law).

### 4.5 Quests
- Daily Oaths: 5–6 tasks (clear 3 chambers, open 1 chest, upgrade gear…). Points fill an activity bar with 4–5 chest milestones. Reset 00:00 UTC.
- Weekly Pilgrimage: bigger tasks, bigger chests.
- Achievements: one-time, long term.

### 4.6 Other features (backlog, by phase)
- Energy (Estus) refill timer, buy with shards / ad.
- Battle pass (free + paid track), season 30 days.
- Events: login calendar (7-day), limited Realms, boss rush.
- Mail inbox, rewards from server.
- Leaderboard (furthest chamber, boss rush time).
- Guild / co-op: phase 3.
- Cosmetic skins.
- Rewarded ads + IAP.

## 5. Content for first playable / MVP

| Item | Prototype | MVP (soft launch) |
|---|---|---|
| Heroes | 1 | 3 |
| Realms | 1 (10 chambers) | 5 (×20 chambers) |
| Enemies | 4 | 15 + 5 elites |
| Bosses | 1 | 5 |
| Boons | 15 | 50 |
| Weapons | 1 | 6 |
| Meta | none | gear, merge, chests, talents, quests, energy, shop |

## 6. Phased roadmap

1. **P0 — Core prototype:** `Game` scene, joystick, move/stop-attack, 4 enemies, chamber clear, fog gate, boon pick, 1 boss, death/result. Placeholder + first Meshy models. Perf baseline on Android.
2. **P1 — Vertical slice:** 1 full Realm, real art for hero/enemies/boss, VFX, audio, results → souls.
3. **P2 — Meta:** save system, gear + merge, chests, talents, energy, daily/weekly quests, bottom tab menu.
4. **P3 — Backend + live:** accounts, cloud save, server time for quests/energy, remote config, IAP, ads, analytics.
5. **P4 — Soft launch:** 5 Realms, battle pass, events, tune economy.

## 7. Tech plan (short)

- Unity 6000.6.5f1, URP mobile, portrait. Fixed top-down-ish camera (~55° pitch), like Archero.
- Architecture: data in ScriptableObjects (heroes, enemies, boons, gear, drop tables). Systems small and decoupled (event bus). Easy for AI to edit.
- Pooling for enemies, projectiles, VFX, damage numbers. No runtime Instantiate in combat.
- Combat: no physics engine for projectiles (simple distance/raycast checks). Enemy count target 40–60 on screen.
- Lighting: baked lightmaps + light probes per chamber, 1 realtime light (bonfire/hero), fog. Shadows: 1 cascade or blob shadows on low tier.
- Addressables for Realm content (download size).
- Backend (P3): choose later (e.g. Unity Gaming Services, PlayFab, or own Node + Postgres). Server-authoritative for chests and currency.

## 8. Art direction

- Reference: Dark Souls 1/3, Elden Ring Limgrave mood, but higher contrast and brighter VFX for small screens.
- Palette: cold stone grey/blue world, warm orange fire light, gold for loot, red for danger, cyan for magic.
- Camera far → silhouettes must read: oversize weapons a bit, rim light on hero, outline on enemies optional.
- UI: dark parchment + iron frames, serif title font (e.g. Cinzel, OFL), Archero layout and icon sizes.

## 9. Meshy.ai → Unity: YES, possible

**Verdict:** Meshy is usable for this project. Best fit: props, environment pieces, weapons, gear icons (render), enemies. Hero and bosses: Meshy for base, then cleanup in Blender.

| Topic | Finding |
|---|---|
| Export formats | FBX, GLB, OBJ, USDZ, STL, BLEND. FBX = native in Unity, best for rigged/animated. GLB needs glTFast package. |
| Unity plugin | Official Meshy Unity plugin (Package Manager / .unitypackage). Unity 6 + URP supported. "Send to Unity" puts model + materials into Assets. |
| Textures | PBR metallic-roughness: base color, normal, metallic, roughness, emission. Maps to URP Lit. |
| Poly count | Smart Topology / remesh: target 100–15,000 polys, quad or triangle. |
| Rigging + anim | Auto-rig humanoid + quadruped, 600+ animation presets, FBX rig works with Unity Humanoid Avatar. |
| License | **Free plan = CC BY 4.0 (must credit Meshy), assets may be public.** **Paid plan = you own assets, commercial use OK, private.** Use paid plan for shipping assets. Do not upload copyrighted reference images (no real Dark Souls screenshots as input). |

### Mobile budget per asset (our rule)
| Asset | Tris | Texture |
|---|---|---|
| Hero | 5–8k | 1×1024 atlas |
| Enemy (normal) | 1.5–3k | 512 |
| Boss | 8–12k | 1024–2048 |
| Prop / wall piece | 200–1.5k | shared 1024 trim/atlas |
| Weapon | 500–1.5k | 256–512 |

### Import pipeline
1. Generate in Meshy (text/image → 3D). Style prompt fixed: "dark fantasy, gothic, worn steel, realistic PBR, game asset".
2. Remesh to budget above. Export **FBX**, 1024 textures.
3. Rig in Meshy (humanoid) if character. Pick base anims (idle, run, attack, hit, die).
4. Import to `Assets/_Project/Art/Meshy/<Category>/<Name>/`. Editor script (to build) sets: scale, Humanoid avatar, no read/write, mesh compression, ASTC textures, packs metallic+roughness into URP mask map, makes URP Lit (or Simple Lit) material.
5. Make LOD1 (50%) for enemies/bosses. Prefab + pool config.
6. Check in Frame Debugger + Android profiler.

### Risks
- AI meshes can have messy UVs/topology on complex shapes (armor, cloth). Plan Blender cleanup time for hero/bosses.
- Style drift between generations → keep one prompt template + reference image set; group-review per Realm.
- Animation presets are generic → bosses need custom anims (Mixamo, asset store, or animator).
- Repo size: add git-lfs before first Meshy import.

## 10. Open questions
1. First hero: ranged (sword thrower, Archero feel) or melee? (Recommend ranged for v1.)
2. Monetization: ads + IAP hybrid like Archero? (Recommend yes.)
3. Backend choice in P3.

Sources: [Meshy Unity page](https://www.meshy.ai/use-cases/free-game-assets/unity), [Meshy Unity plugin blog](https://www.meshy.ai/blog/ai-3d-models-for-unity-official-meshy-plugin-2026), [Meshy Unity tutorial](https://www.meshy.ai/tutorials/3d-model-for-unity-workflow), [Meshy glossary](https://docs.meshy.ai/en/webapp/glossary), [Meshy pricing docs](https://docs.meshy.ai/en/webapp/pricing), [Meshy commercial use FAQ](https://intercom.help/meshy/en/articles/16102098-can-i-use-meshy-assets-commercially).
