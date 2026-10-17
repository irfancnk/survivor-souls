# Chamber concept generator (KAN-245)

Blender scripts that built the island Chamber concepts the user picked for KAN-245.
They are the reference for layout numbers, obstacle positions, colours and light. They are not game code.

| File | What it holds |
|---|---|
| `kit.py` | Materials and mesh helpers (stone, floor, cliff, rock, ashlar wall, arch, pillar, characters, world, sun) |
| `chamber_v2.py` | Shared island Chamber: 36 x 60 m room with NE / SW chamfers, walls, NE gate + light beam, bridges, island foundation, sea shader (`mat_sea2`), camera, compositor |
| `chamber_v3.py` | Ponds / pools, maze wall runs, set dressing, in-game camera shot, and the themes (`THEMES3`) |

Picked themes: `01_drowned_cathedral`, `02_frost_bastion`, `03_wreck_cove`, `05_burning_hamlet`, `16_sunken_bathhouse`,
`23_bloodmoon_circle`, `27_lantern_market`, `30_abyssal_watergate`. Renders: `Docs/screenshots/kan245-concepts/`.

Run (Blender 5.2, from this folder):

```
blender -b --factory-startup -P chamber_v3.py -- <theme> <out.png> [knight.glb|-] [zombie.fbx|-] [width height]
```

It writes `<out.png>` (whole map) and `<out>_game.png` (in-game camera, 16 m view width).
Coordinates: Blender X = east, Y = north (= Unity Z), Z = up; room centre at the origin.
