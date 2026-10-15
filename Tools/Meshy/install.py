"""Export every complete Meshy job into the Unity project + render a pose sheet.
python3 install.py <repo_root> [slug ...]"""
import json, os, glob, subprocess, sys
HERE = os.path.dirname(os.path.abspath(__file__))
PIL_PY = "/Users/irfancankaleli/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/bin/python3"
repo = sys.argv[1]
only = set(sys.argv[2:])
roster = {j["slug"]: j for j in json.load(open(os.path.join(HERE, "roster.json")))["jobs"]}
summary = {}
for sp in glob.glob(os.path.expanduser("~/survivor-souls-assets/meshy_jobs/*/job_state.json")):
    st = json.load(open(sp))
    slug = st.get("slug")
    if slug not in roster or not st.get("complete") or (only and slug not in only):
        continue
    j = roster[slug]
    name = j["name"]
    sub = {"boss": "Bosses", "pet": "Pets", "hero": "Heroes"}.get(j["kind"], "Enemies")
    out = os.path.join(repo, "Assets/_Project/Art/Characters", sub, name)
    os.makedirs(out, exist_ok=True)
    proj = st["project_dir"]
    r = subprocess.run(["blender", "-b", "--python", os.path.join(HERE, "to_unity.py"), "--", proj, out, name],
                       capture_output=True, text=True)
    info = None
    for line in r.stdout.splitlines():
        if line.startswith("EXPORT_INFO"):
            info = json.loads(line[len("EXPORT_INFO "):])
    if not info:
        print("EXPORT FAILED", slug, r.stdout[-1500:], r.stderr[-1500:])
        continue
    if not os.environ.get("NO_SHEETS"):
        pre = os.path.join(HERE, "check", name)
        subprocess.run(["blender", "-b", "--python", os.path.join(HERE, "render_check.py"), "--", proj, pre],
                       capture_output=True, text=True)
        sheet = os.path.join(repo, "Docs/enemies", f"{name}.png")
        os.makedirs(os.path.dirname(sheet), exist_ok=True)
        subprocess.run([PIL_PY, "-I", os.path.join(HERE, "sheet.py"), pre, sheet], capture_output=True, text=True)
    info.update(kind=j["kind"], slug=slug, folder=os.path.relpath(out, repo), tasks=st["tasks"],
                credits=sum(v or 0 for v in st.get("credits", {}).values()), project_dir=proj,
                anim_actions=j["anims"])
    summary[slug] = info
    print("OK", slug, info["height_m"], info["tris"], sorted(info["clips"]))
prev = os.path.join(HERE, "install_summary.json")
old = json.load(open(prev)) if os.path.exists(prev) else {}
old.update(summary)
json.dump(old, open(prev, "w"), indent=1)
