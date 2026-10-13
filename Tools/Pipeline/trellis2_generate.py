"""
Generate one prop with TRELLIS.2 on the RTX 5080 laptop and drop it in the Drive inbox (KAN-27).

Run in the trellis2 environment (WSL2 Ubuntu, see Docs/ASSET_PIPELINE.md), from the TRELLIS.2 repo folder:
    python trellis2_generate.py --image concept.png --name SM_R1_Rock_Boulder_01 \
        --prompt "<prompt used for the concept image>" --seed 42 \
        --inbox "/mnt/g/My Drive/SurvivorSouls/Assets/Incoming"

Output: <inbox>/<name>/<name>.glb (PBR, PNG textures), preview.png (the concept image), meta.json.
The Mac side runs Tools/Blender/clean_prop.py --batch on the same inbox.
Not run on the laptop yet: check the pipeline API against the TRELLIS.2 README if it fails.
"""
import argparse
import datetime
import json
import os
import shutil

os.environ.setdefault("PYTORCH_CUDA_ALLOC_CONF", "expandable_segments:True")  # less VRAM fragmentation on 16 GB
os.environ.setdefault("ATTN_BACKEND", "xformers")                             # no flash-attn build on Blackwell

import torch  # noqa: E402
from PIL import Image  # noqa: E402

MODEL = "microsoft/TRELLIS.2-4B"
LICENCE = "TRELLIS.2 code + weights MIT (nvdiffrast / nvdiffrec own licences, not shipped). Concept image: FLUX.1 [schnell], Apache 2.0."


def main():
    p = argparse.ArgumentParser()
    p.add_argument("--image", required=True)
    p.add_argument("--name", required=True)
    p.add_argument("--inbox", required=True)
    p.add_argument("--prompt", default="")
    p.add_argument("--seed", type=int, default=42)
    p.add_argument("--faces", type=int, default=100000, help="decimation target before Blender clean-up")
    p.add_argument("--texture", type=int, default=2048)
    p.add_argument("--run-args", default="{}", help='extra pipeline.run kwargs as JSON, e.g. {"pipeline_type": "512"}')
    p.add_argument("--cleanup", default="{}", help='meta cleanup block as JSON, e.g. {"tris": 800, "height": 0.9}')
    a = p.parse_args()

    from trellis2.pipelines import Trellis2ImageTo3DPipeline
    import o_voxel

    out_dir = os.path.join(a.inbox, a.name)
    os.makedirs(out_dir, exist_ok=True)
    torch.manual_seed(a.seed)
    started = datetime.datetime.now()

    pipeline = Trellis2ImageTo3DPipeline.from_pretrained(MODEL)
    pipeline.cuda()
    mesh = pipeline.run(Image.open(a.image), **json.loads(a.run_args))[0]
    mesh.simplify(16777216)  # nvdiffrast limit
    glb = o_voxel.postprocess.to_glb(
        vertices=mesh.vertices, faces=mesh.faces, attr_volume=mesh.attrs, coords=mesh.coords,
        attr_layout=mesh.layout, voxel_size=mesh.voxel_size, aabb=[[-0.5, -0.5, -0.5], [0.5, 0.5, 0.5]],
        decimation_target=a.faces, texture_size=a.texture, remesh=True, remesh_band=1, remesh_project=0)
    glb.export(os.path.join(out_dir, f"{a.name}.glb"), extension_webp=False)
    shutil.copy(a.image, os.path.join(out_dir, "preview.png"))

    meta = dict(
        name=a.name, prompt=a.prompt, seed=a.seed, model=MODEL, model_version="TRELLIS.2 (git HEAD at run time)",
        date=started.date().isoformat(), licence=LICENCE, category="prop",
        gpu=torch.cuda.get_device_name(0), peak_vram_gb=round(torch.cuda.max_memory_allocated() / 2**30, 1),
        seconds=round((datetime.datetime.now() - started).total_seconds(), 1),
        run_args=json.loads(a.run_args), cleanup=json.loads(a.cleanup))
    with open(os.path.join(out_dir, "meta.json"), "w") as f:
        json.dump(meta, f, indent=2)
    print(f"[trellis2_generate] {a.name}: {meta['seconds']} s, peak VRAM {meta['peak_vram_gb']} GB -> {out_dir}")


main()
