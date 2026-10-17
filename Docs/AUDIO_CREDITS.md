# Audio credits (KAN-236)

Every sound in Survivor Souls is **procedural**: it is synthesised by our own editor code and written as a WAV into
`Assets/_Project/Audio/Sfx/` on a scene rebuild. No recorded samples, no sound packs, no third-party audio, no licence
to credit (decision 2026-10-12: "Sounds: procedural (SfxBuilder). No paid packs."). There is no music track yet.

If a recorded or licensed sound is ever added, add a row to "Third-party sounds" below with the source, author,
licence and the file it became, before it is merged.

`EditMode ShotChannelSfxTests.AudioCredits_ListEveryClipFamily` fails when a clip family in the Sfx folder is missing
from this file.

## Procedural families

Family = file name without the take number (`sfx_zombie_death_03.wav` -> `sfx_zombie_death`). Source = the generator.

| Family | What | Source | Ticket |
|---|---|---|---|
| `sfx_crossbow_shot` | crossbow shot, 3 takes (boss fan volley, fallback) | `SfxBuilder.CrossbowShot` | KAN-231 |
| `sfx_crossbow_shot_var` | crossbow shot, 8 takes for the shot channel | `SfxBuilder.Shots.cs` | KAN-236 |
| `sfx_crossbow_volley_layer` | Spread Shot volley layer | `SfxBuilder.Shots.cs` | KAN-236 |
| `sfx_crossbow_seat` | bolt seated on the rail (reload) | `CrossbowCycleBuilder` | KAN-217 |
| `sfx_bow_twang` | Shadow Ranger bow shot | `SfxBuilder.BowTwang` | KAN-227, KAN-231 |
| `sfx_ember_hot_shot` | Ember Bolts fire layer on the shot | `SfxBuilder` | KAN-201 |
| `sfx_ember_ignite` | Ember Bolts ignite | `SfxBuilder` | KAN-201 |
| `sfx_ember_crackle_loop` | burning foe crackle loop | `SfxBuilder` | KAN-201 |
| `sfx_bolt_impact` | bolt hits a body | `SfxBuilder.BoltImpact` | KAN-13 |
| `sfx_hitmarker_tick` | hit marker tick | `SfxBuilder.HitMarkerTick` | KAN-13 |
| `sfx_killmarker` | kill marker | `SfxBuilder` | KAN-13 |
| `sfx_hollow_death` | old enemy groan (now player death + boss groans, KAN-58) | `SfxBuilder` | early prototype |
| `sfx_zombie_death` | zombie death voices | `SfxBuilder.ZombieDeaths.cs` | KAN-234 |
| `sfx_zombie_fall` | body-fall thumps | `SfxBuilder.ZombieDeaths.cs` | KAN-234 |
| `sfx_zombie_mass_death` | many kills at once | `SfxBuilder.ZombieDeaths.cs` | KAN-234 |
| `sfx_step_snow` | Knight footsteps on snow | `SfxBuilder.Footsteps.cs` | KAN-235 |
| `sfx_step_shingle` | Knight footsteps on shingle | `SfxBuilder.Footsteps.cs` | KAN-235 |
| `sfx_step_wood` | Knight footsteps on wood | `SfxBuilder.Footsteps.cs` | KAN-235 |
| `sfx_step_stone` | Knight footsteps on stone | `SfxBuilder.Footsteps.cs` | KAN-235 |
| `sfx_knight_grunt` | Knight hurt grunt | `SfxBuilder` | KAN-187 |
| `sfx_heartbeat` | low health heartbeat | `SfxBuilder` | KAN-187 |
| `sfx_soul_chime` | soul wisp absorbed | `SfxBuilder` | KAN-21 |
| `sfx_level_up` | level-up swell | `SfxBuilder` | KAN-21 |
| `sfx_boon_pick` | skill picked | `SfxBuilder` | KAN-21 |
| `sfx_skill_upgrade` | HUD skill slot glow | `SfxBuilder` | KAN-74 |
| `sfx_card_reroll` | level-up re-roll | `SfxBuilder` | KAN-67 |
| `sfx_reel_tick` | skill slot-machine tick | `SfxBuilder` | KAN-159 |
| `sfx_reel_stop` | skill slot-machine stop | `SfxBuilder` | KAN-159 |
| `sfx_reveal_common` | card reveal, Common | `SfxBuilder` | KAN-37 |
| `sfx_reveal_rare` | card reveal, Rare | `SfxBuilder` | KAN-37 |
| `sfx_reveal_epic` | card reveal, Epic | `SfxBuilder` | KAN-37 |
| `sfx_reveal_legendary` | card reveal, Legendary | `SfxBuilder` | KAN-37 |
| `sfx_chest_drop` | Reliquary lands | `SfxBuilder` | KAN-73 |
| `sfx_chest_open` | Reliquary opens | `SfxBuilder` | KAN-73 |
| `sfx_heart_heal` | healing heart taken | `SfxBuilder` | KAN-60 |
| `sfx_portal_open` | portal horn | `SfxBuilder` | KAN-39 |
| `sfx_portal_enter` | portal rush | `SfxBuilder` | KAN-40 |
| `sfx_portal_sealed` | sealed portal thud | `SfxBuilder` | KAN-49 |
| `sfx_chamber_arrive` | Chamber toll | `SfxBuilder` | KAN-40 |
| `sfx_ward_up` | Hallowed Shield rises | `SfxBuilder` | KAN-93 |
| `sfx_ward_block` | Hallowed Shield blocks | `SfxBuilder` | KAN-93 |
| `sfx_ward_down` | Hallowed Shield ends | `SfxBuilder` | KAN-93 |
| `sfx_soul_burst` | Soul Burst blast | `SfxBuilder` | KAN-66 |
| `sfx_frost_freeze` | Freeze Arrow | `SfxBuilder` | KAN-91 |
| `sfx_tome_thump` | Holy Tomes hit | `SfxBuilder` | KAN-123 |
| `sfx_tome_whoosh_loop` | Holy Tomes orbit loop | `SfxBuilder` | KAN-123 |
| `sfx_charge_scrape` | boss charge wind-up | `SfxBuilder` | KAN-168 |
| `sfx_charge_dash` | boss charge dash | `SfxBuilder` | KAN-168 |
| `sfx_charge_stop` | boss charge stop | `SfxBuilder` | KAN-168 |
| `sfx_charge_crash` | boss charge hits a wall | `SfxBuilder` | KAN-168 |
| `sfx_bloater_swell` | Plague Bloater swell | `SfxBuilder` | KAN-132 |
| `sfx_bloater_burst` | Plague Bloater burst | `SfxBuilder` | KAN-132 |
| `sfx_toxic_cloud_hiss_loop` | toxic cloud loop | `SfxBuilder` | KAN-132 |
| `sfx_ghoul_screech` | Frenzied Ghoul leap screech | `SfxBuilder` | KAN-132 |
| `sfx_ghoul_whoosh` | Frenzied Ghoul leap | `SfxBuilder` | KAN-132 |
| `sfx_ghoul_thud` | Frenzied Ghoul landing | `SfxBuilder` | KAN-132 |
| `sfx_brute_grunt` | Grave Brute wind-up | `SfxBuilder` | KAN-132 |
| `sfx_brute_slam` | Grave Brute ring slam | `SfxBuilder` | KAN-132 |
| `sfx_shaman_hum` | Hexbound Shaman cast hum | `SfxBuilder` | KAN-138 |
| `sfx_shaman_orb_whoosh` | Hexbound Shaman orbs | `SfxBuilder` | KAN-138 |
| `sfx_shaman_hex_chime` | Hexbound Shaman hex | `SfxBuilder` | KAN-138 |
| `sfx_shaman_raise_crack` | Hexbound Shaman raise | `SfxBuilder` | KAN-138 |
| `sfx_shaman_blink_pop` | Hexbound Shaman blink | `SfxBuilder` | KAN-138 |
| `amb_shore_wind_surf_loop` | shore ambience (wind, surf, gulls) | `SfxBuilder.ShoreAmbience` | KAN-25 |
| `amb_portal_hum_loop` | portal hum (3D) | `SfxBuilder.PortalHum` | KAN-39 |
| `amb_charge_rumble_loop` | boss charge rumble loop | `SfxBuilder.ChargeRumble` | KAN-168 |

## Third-party sounds

None.

## Mix bus (KAN-236)

`Assets/_Project/Audio/SurvivorSouls.mixer`: Master > SFX > (Shots, Footsteps, Deaths), Master > Music, all 0 dB.
`AudioMixerRouting` routes the scene's voices at Start; `MasterLimiter` on the AudioListener limits the final mix at
-1 dBFS (instant attack, 0.12 s release). Dev sound board: Unity menu `Survivor Souls/Audio/Sound Board`.
