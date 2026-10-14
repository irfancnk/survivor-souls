import json, os, glob, sys
S = os.path.dirname(os.path.abspath(__file__))
repo = sys.argv[1]
summ = json.load(open(S + '/install_summary.json'))
des = json.load(open(S + '/design.json'))
tot = 0
for sp in glob.glob(os.path.expanduser('~/survivor-souls-assets/meshy_jobs/*enemy-*/job_state.json')):
    tot += sum(v or 0 for v in json.load(open(sp)).get('credits', {}).values())
order = ['graveyard-laborer', 'fallen-militiaman', 'rusted-watchman', 'gravebound-knight', 'plague-bloater',
         'bone-archer', 'pyre-thrower', 'bile-spitter', 'frenzied-ghoul', 'grave-brute', 'hexbound-shaman',
         'ashgrave-butcher', 'pyre-lord', 'rotting-colossus', 'bone-matriarch']
L = []
A = L.append
A('# Enemy and Boss Roster (Meshy batch 2026-10-15)')
A('')
A('15 new rigged and animated characters: 11 zombies and 4 bosses. Models only: no game code uses them yet. Jira tickets carry the implementation (epic "New zombie and boss roster").')
A('')
A('## 1. Asset facts (all characters)')
A('')
A('- Source: Meshy API (text-to-3d preview + refine, or multi-image-to-3d from `~/survivor-souls-assets/character-references/<name>/v001` front/left/back), then remesh (triangles), auto-rig, Meshy animation library. Raw outputs + task JSON: `~/survivor-souls-assets/meshy_jobs/*enemy-<slug>*` (not in git).')
A('- Unity files per character: `SK_<Name>.fbx` (skinned mesh + rig, no animation), `A_<Name>_<Clip>.fbx` (rig only, one take named `<Clip>`), `T_<Name>_BaseColor.jpg` (2048), `T_<Name>_Normal.png` (1024), `T_<Name>_Emission.jpg` (1024, only when the texture glows). No material yet: make a URP Simple Lit / Lit material per character (same as `M_Zombie`; the FBX has no embedded texture). Set the Normal texture type to Normal map.')
A('- Rig: Meshy 24-bone skeleton (`Hips`, `Spine02`, `Spine01`, `Spine`, `neck`, `Head`, arms, legs; same names on all 15). Root armature node scale 0.01 (FBX cm). Import as **Generic** (checked with a Unity 6000.6.5f1 batch import: all files load, 0 import errors). Clips bind by transform path, so every `A_<Name>_*` clip plays on its own `SK_<Name>`. Do not share clips between characters (bone lengths differ).')
A('- Rest pose: A-pose (Bloater / Brute: relaxed). Height = Meshy rig height. Unity skinned bounds read ~10-15 % taller (bounds padding). Knight = 1.75 m, so bosses are 2.3-2.9x the Knight.')
A('- `Walking` / `Running` come with every rig (Meshy default human walk / run). The other clips come from the Meshy animation library (action id in the table). Several clips are long (5-8 s): set loop / trim ranges in the import or play them faster.')
A('- Clips can carry root motion on `Hips` (X/Z). Freeze it (bake into pose, or zero Hips X/Z like `A_Zombie_Run`).')
A('- Tri budget (STATE decisions, KAN-27): enemy 1.5-3k, boss 8-12k. Zombies are ~4k (Brute 6k), bosses ~12.4k after remesh. Add Unity Mesh LODs (as for `SK_Zombie`) before big hordes.')
A('- Licence: Meshy output on the account plan. Free plan output is CC BY 4.0 (credit needed); check the plan before ship.')
A(f'- Cost: {tot} Meshy credits for the batch (includes 3 previews rejected because they did not match the brief). Balance after: 176.')
A('')
A('## 2. Roster')
A('')
A('| # | Character | Kind | Height | Tris | Clips (Meshy action id) | Folder |')
A('|---|---|---|---|---|---|---|')
for i, s in enumerate(order, 1):
    x = summ[s]; d = des[s]
    clips = ', '.join(f"{c} ({x['anim_actions'].get(c, 'rig')})" for c in sorted(x['clips']))
    A(f"| {i} | {d['title']} | {x['kind']} | {x['height_m']} m | {x['tris']} | {clips} | `{x['folder']}` |")
A('')
A('Pose sheets (rest + one frame of every clip, Blender workbench render): `Docs/enemies/<Name>.png`.')
A('')
A('## 3. Design per character')
A('')
A('Numbers are first guesses in Chamber 1 terms (same units as `CombatBuilder.ZombieType` and `BossData`). Damage given as a share of the Knight max HP follows the boss rule (`maxHitShare`). Every telegraph >= 0.6 s (`BossData.MinTelegraph`). Every ranged shot stops on pillars / walls (`Enemy.Walkability`), like `BossProjectiles`.')
A('')
for i, s in enumerate(order, 1):
    x = summ[s]; d = des[s]
    name = x['folder'].split('/')[-1]
    A(f"### {i}. {d['title']} (`{name}`)")
    A('')
    A(f"- Role: {d['role']}.")
    A(f"- Attack: {d['attack']}")
    A(f"- Stats: {d['stats']}.")
    A(f"- Pose sheet: `Docs/enemies/{name}.png`. Meshy tasks: " + ', '.join(
        f"{k} `{v}`" for k, v in x['tasks'].items() if k in ('preview', 'refine', 'generate', 'remesh', 'rigging')) + '.')
    A('')
A('## 4. Known asset issues')
A('')
A('- Hooded look repeats (Bone Archer, Pyre Thrower, Hexbound Shaman, Butcher). Textures differ (bone / ember / violet / black), but add a colour rim or emissive accent so they read apart at the gameplay camera.')
A('- Bone Archer has no bow in the mesh (hands empty). Attach a bow prop to `LeftHand`.')
A('- The Butcher came out as a hooded skeleton executioner with a cleaver-axe, not a fat butcher. The Rotting Colossus came out as a horned demon brute, not a corpse golem. Both read as big bosses; names kept.')
A('- A few triangles stretch from the hands in some clips (Militiaman / Knight Attack, Knight Walk): auto-rig weight noise. Fix in Blender, or hide it with attack VFX if it shows in game.')
A('- Plague Bloater arms clip into the belly in `Burst`.')
A('- Graveyard Laborer, Fallen Militiaman, Rusted Watchman and Gravebound Knight are the four reference sheets (common-enemy tiers) made 3D.')
A('')
A('## 5. Rebuild')
A('')
A('Scripts in `Tools/Meshy/`: `roster.json` (prompts, heights, tris, action ids), `design.json` (section 3 text), `run_meshy.py` (resumable Meshy CLI driver; `UNTIL=preview` stops for a review), `to_unity.py` (Blender: GLB -> SK / A FBX + textures), `render_check.py` + `sheet.py` (pose sheets), `install.py` (export all into the repo), `make_doc.py` (this file). Needs a Meshy CLI 0.4.0 login (`npm exec --package=meshy-cli@0.4.0 -- meshy auth status`) and Blender 5.2. Scripts read `~/survivor-souls-assets` (Mac paths).')
open(os.path.join(repo, 'Docs/ENEMY_ROSTER.md'), 'w').write('\n'.join(L) + '\n')
print(tot)
