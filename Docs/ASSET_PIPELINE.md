# Local 3D Asset Pipeline (KAN-27)

Status: investigation done, chain tested on the Mac with a stand-in input. Laptop generation not run yet.
Confluence copy: 02 Art & Audio > "Local 3D Asset Pipeline: Options and Decisions".

## 1. Decisions

| Topic | Decision |
|---|---|
| Primary 3D model | TRELLIS.2 (Microsoft), image-to-3D. MIT licence. PBR GLB output. |
| Fallback 3D model | TRELLIS (v1) image-large. MIT licence. Official minimum 16 GB VRAM. |
| Concept image model | FLUX.1 [schnell]. Apache 2.0. |
| Exchange format | GLB from the generator. FBX from Blender to Unity. No glTFast package. |
| Clean-up | `Tools/Blender/clean_prop.py`, headless Blender 5.2. |
| Unity import | `Editor/Props/GeneratedPropImporter.cs`, menu "Survivor Souls/Import Generated Props". |
| Names | KAN-26 proposed pattern `SM_<Realm>_<Category>_<Name>_<NN>`. Textures `T_<Body>_BC / _N / _MASK`. |
| File handoff | Google Drive for Desktop on the laptop and the Mac. Same folder tree on both. |
| Local vs Meshy | Local: static props and environment pieces. Meshy (paid plan): rigged characters, enemies, bosses, held weapons. |

## 2. Model comparison

| Model | Licence | Commercial use | VRAM | Output | Result |
|---|---|---|---|---|---|
| TRELLIS.2 4B | MIT | Yes | 24 GB official; 16 GB needs 512 res + `expandable_segments` (not tested) | GLB, PBR (base colour, roughness, metallic, opacity) | **Primary** |
| TRELLIS image-large | MIT | Yes | 16 GB official | GLB, baked colour texture | **Fallback** |
| Hunyuan3D 2.1 | Tencent Hunyuan 3D 2.1 Community | Not valid in EU, UK, South Korea. Licence request above 1M MAU | 10 GB shape, 29 GB shape + texture | Mesh + PBR | Rejected (licence territory, VRAM) |
| Stable Fast 3D / SPAR3D | Stability Community | Only below USD 1M yearly revenue; covers outputs | about 7 GB | GLB, UV texture, material values | Rejected (revenue cap) |
| TripoSR | MIT | Yes | low | Mesh, vertex colour | Rejected (quality, no PBR) |
| InstantMesh | not checked | - | about 16 GB | Mesh, vertex colour | Rejected (quality) |

## 3. Blackwell (RTX 5080, sm_120)

- PyTorch: use a CUDA 12.8 (or newer) build. TRELLIS.2 default env (PyTorch 2.6 + CUDA 12.4) has no sm_120 kernels.
- Do not use `setup.sh --new-env`. Install PyTorch first, then the extensions.
- Build extensions with `TORCH_CUDA_ARCH_LIST="12.0"`: cumesh, o-voxel, flexgemm, nvdiffrast, nvdiffrec.
- Skip flash-attn. Use `ATTN_BACKEND=xformers` (xformers has sm_120 wheels).
- OS: TRELLIS.2 is tested on Linux only. Use WSL2 Ubuntu on the laptop.
- Fallback TRELLIS v1 also needs a CUDA 12.8 rebuild of its extensions (default env is CUDA 11.8).
- Write the working versions in this file after the first laptop run.

## 4. Laptop setup (not run yet)

```bash
# Windows PowerShell (admin), once
wsl --install -d Ubuntu-24.04
```

```bash
# Ubuntu (WSL2). Driver comes from Windows; install only the CUDA 12.8 toolkit for WSL.
sudo apt update && sudo apt install -y build-essential git
conda create -n trellis2 python=3.10 -y && conda activate trellis2
pip install torch torchvision --index-url https://download.pytorch.org/whl/cu128
pip install xformers
git clone --recursive https://github.com/microsoft/TRELLIS.2 && cd TRELLIS.2
export TORCH_CUDA_ARCH_LIST="12.0" ATTN_BACKEND=xformers PYTORCH_CUDA_ALLOC_CONF=expandable_segments:True
. ./setup.sh --basic --cumesh --o-voxel --flexgemm --nvdiffrast --nvdiffrec
```

Generate one prop (copy `Tools/Pipeline/trellis2_generate.py` into the TRELLIS.2 folder):

```bash
python trellis2_generate.py --image concept.png --name SM_R1_Rock_Boulder_01 \
  --prompt "<concept prompt>" --seed 42 --run-args '{"pipeline_type": "512"}' \
  --cleanup '{"tris": 800, "height": 1.2}' \
  --inbox "/mnt/g/My Drive/SurvivorSouls/Assets/Incoming"
```

Out of memory: keep `pipeline_type` 512, close other GPU apps, then try the TRELLIS v1 fallback.

## 5. Concept image prompt template

Model: FLUX.1 [schnell]. One object, neutral grey background, three-quarter view, soft even light, no cast shadow.

```
<object>, single game prop, dark fantasy, realistic, weathered, <material words>,
snowy northern shore, frost on edges, muted cold colours,
three-quarter view, centred, full object in frame, plain neutral grey background,
soft overcast studio light, no shadow, no text, no people
```

Do not use real game screenshots or other copyrighted images as input.

## 6. Google Drive handoff

Folder tree (same on both machines):

```
SurvivorSouls/Assets/
  Incoming/<Name>/   <Name>.glb, preview.png, meta.json      (laptop writes)
  Approved/<Name>/   <Name>.fbx, T_*_BC/_N/_MASK.png, <Name>.json, <Name>_preview.png  (Mac writes)
  Rejected/<Name>/   moved by hand, with a note in meta.json
```

| Option | Result |
|---|---|
| Drive for Desktop sync on laptop + Mac | **Chosen.** Plain files, binary GLB/FBX OK, scripts read and write the folder. Needs install + sign-in on the Mac (`~/Library/CloudStorage/GoogleDrive-<account>/My Drive/`). |
| Google Drive connector for Claude | Second choice. Good for search and reading meta/preview. Not loaded in this session (status "pending"). Weak for binary files. |
| Cloudflare R2 | Rejected. Connector manages buckets only; laptop needs API keys (secret handling). |
| Git LFS | Rejected for raw files. git-lfs is not installed. Use later only for approved assets inside `Assets/`. |

Until Drive for Desktop is on the Mac, the scripts use the stand-in folder `~/survivor-souls-assets/drive/SurvivorSouls/Assets/`.
Set `SS_PROP_SOURCE` to the real `Approved` folder for the Unity importer.

## 7. Mac commands (tested)

```bash
# 1. Clean everything in the inbox -> Approved (skips props already done; --force to redo)
blender -b -P Tools/Blender/clean_prop.py -- --batch "<Drive>/Assets/Incoming" --out "<Drive>/Assets/Approved" --preview
```

```bash
# 2. Import Approved into Unity (or menu "Survivor Souls/Import Generated Props")
Unity -batchmode -nographics -projectPath . -executeMethod SurvivorSouls.Editor.Props.GeneratedPropImporter.ImportFromCommandLine -ss-props "<Drive>/Assets/Approved"
```

`clean_prop.py` steps: import GLB, join parts, merge close vertices, delete small floaters, pivot at bottom centre, optional scale to height, collapse decimate to budget, triangulate, recalc normals, smooth by angle, textures (reuse generator UVs and resize, or `--bake` new UVs + bake from the high mesh), LOD1 50 % and LOD2 25 % (a LOD that saves less than 15 % is dropped), FBX export (Y up, -Z forward, transforms applied), `<Name>.json`.

Importer steps: name check (KAN-26), copy into `Art/Environment/<R1_SnowyShore|Shared|Rn>/{Meshes,Textures,Materials,Prefabs}`, model settings (scale 1, no materials, no animation, read/write off, mesh compression medium), textures (ASTC 6x6 on Android + iOS, size from meta), URP Lit `M_*` (metallic map R metal, A smooth; no reflections), prefab `P_*` (no shadows, static, collider from meta: `box` default, `capsule`, `none`).

## 8. meta.json

Laptop writes: `name, prompt, seed, model, model_version, date, licence, category, gpu, peak_vram_gb, seconds, run_args, cleanup{tris,height,tex,lods,bake}, collider`.
Blender adds `cleanup_result{tris_in, parts_in, parts_removed, budget_ok, scale, tris_lod[], size_m[], texture_size, textures, seconds, blender, date}`.
Keep the JSON with the asset (copied into `Meshes/`). It is the licence audit trail.

## 9. Test result (Mac, stand-in input)

Input: Meshy crossbow GLB (780k tris, PBR), named `SM_Gen_Prop_Crossbow_01`.

| Step | Result |
|---|---|
| Blender clean-up (reuse textures) | 12.4 s. LOD0 4,999 tris, LOD1 2,499. 512 textures. |
| Blender clean-up (`--bake`) | 14.8 s. Same tris. New atlas, about 20 % darker (edge bleed). |
| Unity import | 36 s batch (incl. editor start). Prefab: rotation 0, scale 1, pivot at y 0, LODGroup 2 levels, URP Lit, BoxCollider, static. |
| Budget 1,500 tris | Not reached: collapse stops at about 2,100 on non-manifold edges and the shape breaks. Script warns (`budget_ok: false`). |

## 10. Open items

- Run TRELLIS.2 on the laptop. Record versions, VRAM, minutes per asset.
- Install Drive for Desktop on the Mac. Test a file in both directions.
- Proof of concept: boulder, barrel, driftwood log, broken rowboat, gravestone. Realism score 1-5 each.
- Install git-lfs before committing generated binaries.
- KAN-26 final names may change the validator pattern (`PropNaming`).
