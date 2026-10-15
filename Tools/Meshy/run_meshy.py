#!/usr/bin/env python3
"""Drive Meshy CLI for the enemy roster. Resumable: every step stores its task id in
<project>/job_state.json, create calls carry a fixed --operation-id (local journal), so a
rerun never pays twice for a step that was already submitted."""
import json, os, subprocess, sys, threading, time
from concurrent.futures import ThreadPoolExecutor

HERE = os.path.dirname(os.path.abspath(__file__))
WORKSPACE = os.path.expanduser("~/survivor-souls-assets")
ROOT = os.path.join(WORKSPACE, "meshy_jobs")
REFS = os.path.join(WORKSPACE, "character-references")
RUNNER = ["npm", "exec", "--yes", "--package=meshy-cli@0.4.0", "--", "meshy"]
COMMON = ["--output-schema", "v1", "--format", "json", "--no-update-check"]
TASK_SLOTS = threading.Semaphore(int(os.environ.get("SLOTS", "10")))
LOCK = threading.Lock()
STOP = threading.Event()
roster = json.load(open(os.path.join(HERE, "roster.json")))
only = set(sys.argv[1:])


def log(slug, msg):
    line = f"{time.strftime('%H:%M:%S')} [{slug}] {msg}"
    with LOCK:
        print(line, flush=True)
        with open(os.path.join(HERE, "run.log"), "a") as f:
            f.write(line + "\n")


def cli(args, timeout=1800):
    p = subprocess.run(RUNNER + args + COMMON, capture_output=True, text=True, timeout=timeout)
    try:
        out = json.loads(p.stdout)
    except Exception:
        out = {"ok": False, "error": {"raw": p.stdout[-800:], "stderr": p.stderr[-800:]}}
    return p.returncode, out


class Job:
    def __init__(self, spec):
        self.s = spec
        self.slug = spec["slug"]
        self.state_path = None
        self.st = {}
        for d in os.listdir(ROOT) if os.path.isdir(ROOT) else []:
            sp = os.path.join(ROOT, d, "job_state.json")
            if os.path.exists(sp) and json.load(open(sp)).get("slug") == self.slug:
                self.state_path = sp
                self.st = json.load(open(sp))

    def save(self):
        with open(self.state_path, "w") as f:
            json.dump(self.st, f, indent=1)

    @property
    def proj(self):
        return self.st["project_dir"]

    def create(self, key, args):
        """Submit once. Returns task id."""
        if key in self.st.get("tasks", {}):
            return self.st["tasks"][key]
        if STOP.is_set():
            raise RuntimeError("stopped")
        opid = f"ss-enemy-{self.slug}-{key}-v{self.s.get('ver', 1)}"
        extra = []
        if "project_dir" in self.st:
            extra = ["--project", self.proj, "--stage", key, "--workspace", WORKSPACE]
        for attempt in range(90):
            op = opid if attempt == 0 else f"{opid}-r{attempt}"  # a rejected 429 is journaled; retry needs a new id
            rc, out = cli(args + ["--async", "--operation-id", op] + extra, timeout=600)
            if rc == 6 and "NoMorePendingTasks" in json.dumps(out.get("error")):
                time.sleep(20)  # queue full: wait for a slot
                continue
            if rc == 2 and "already rejected" in json.dumps(out.get("error")):
                continue  # journaled rejection from an earlier run: try the next id
            break
        if rc == 9:
            STOP.set()
            raise RuntimeError(f"insufficient credit at {key}")
        if not out.get("ok"):
            raise RuntimeError(f"create {key} failed rc={rc}: {json.dumps(out.get('error'))[:600]}")
        tid = out["result"]["submission"]["task_id"]
        log(self.slug, f"submitted {key} -> {tid}")
        if "project_dir" not in self.st:
            rc, init = cli(["project", "init", "--root", ROOT, "--name", f"enemy-{self.slug}",
                            "--task-id", tid, "--task-type", args[0], "--workspace", WORKSPACE])
            if not init.get("ok"):
                raise RuntimeError(f"project init failed: {init.get('error')}")
            self.st["project_dir"] = init["result"]["project_dir"]
            self.st["slug"] = self.slug
            self.state_path = os.path.join(self.proj, "job_state.json")
        self.st.setdefault("tasks", {})[key] = tid
        self.st.setdefault("resources", {})[key] = args[0]
        self.save()
        return tid

    def wait(self, key):
        tid = self.st["tasks"][key]
        res = self.st["resources"][key]
        if self.st.get("done", {}).get(key):
            return
        with TASK_SLOTS:
            for attempt in range(6):
                rc, out = cli([res, "wait", tid, "--timeout", "1200", "--project", self.proj,
                               "--stage", key, "--workspace", WORKSPACE], timeout=1500)
                if rc in (7, 8):
                    log(self.slug, f"{key} wait rc={rc}, resuming")
                    time.sleep(10)
                    continue
                break
        if not out.get("ok"):
            raise RuntimeError(f"wait {key} failed rc={rc}: {json.dumps(out.get('error'))[:600]}")
        task = out["result"].get("task", out["result"])
        credits = task.get("consumed_credits")
        status = task.get("status")
        if status and status != "SUCCEEDED":
            raise RuntimeError(f"{key} ended {status}: {task.get('task_error')}")
        self.st.setdefault("done", {})[key] = True
        self.st.setdefault("credits", {})[key] = credits
        self.save()
        log(self.slug, f"{key} SUCCEEDED credits={credits}")

    def download(self, key, out_name, asset=None, model_format=None):
        dst = os.path.join(self.proj, out_name)
        if os.path.exists(dst):
            return dst
        tj = os.path.join(self.proj, f"task_{self.st['tasks'][key]}.json")
        a = ["download", "--task-json", tj, "--output", dst, "--project", self.proj,
             "--stage", f"{key}-download", "--workspace", WORKSPACE]
        a += ["--asset", asset] if asset else ["--model-format", model_format]
        rc, out = cli(a)
        if not out.get("ok"):
            # snapshot URL may be stale: refresh from the server
            a2 = ["download", "--resource", self.st["resources"][key], "--task-id",
                  self.st["tasks"][key], "--output", dst, "--project", self.proj,
                  "--stage", f"{key}-download", "--workspace", WORKSPACE]
            a2 += ["--asset", asset] if asset else ["--model-format", model_format]
            rc, out = cli(a2)
            if not out.get("ok"):
                raise RuntimeError(f"download {key} {out_name}: {json.dumps(out.get('error'))[:400]}")
        return dst

    def run(self):
        s = self.s
        style = roster["style"]
        try:
            if "ref" in s:
                r = os.path.join(REFS, s["ref"], "v001")
                imgs = ",".join(os.path.join(r, v + ".png") for v in ("front", "left", "back"))
                tex_key = "generate"
                self.create(tex_key, ["multi-image-to-3d", "create", "--image-urls", imgs,
                                      "--should-texture", "true", "--enable-pbr", "true",
                                      "--texture-resolution", "2k", "--texture-prompt", s["texture"],
                                      "--target-formats", "glb"])
                self.wait(tex_key)
            else:
                prompt = (s["prompt"] + " " + style)[:600]
                self.create("preview", ["text-to-3d", "create", "--mode", "preview", "--prompt", prompt,
                                        "--pose-mode", s.get("pose", "a-pose"), "--target-formats", "glb"]
                            + (["--data", json.dumps(s["preview_data"])] if s.get("preview_data") else []))
                self.wait("preview")
                self.download("preview", "preview_mesh.png", asset="thumbnail.primary")
                if os.environ.get("UNTIL") == "preview":
                    log(self.slug, "PAUSED after preview")
                    return
                tex_key = "refine"
                self.create(tex_key, ["text-to-3d", "create", "--mode", "refine", "--preview-task-id",
                                      self.st["tasks"]["preview"], "--enable-pbr", "true",
                                      "--texture-resolution", "2k", "--texture-prompt", s["texture"][:600],
                                      "--target-formats", "glb"])
                self.wait(tex_key)
            self.download(tex_key, "preview.png", asset="thumbnail.primary")
            if os.environ.get("UNTIL") in ("preview", "texture"):
                log(self.slug, "PAUSED after texture")
                return
            if s.get("skip_remesh"):
                # preview already remeshed (preview_data should_remesh): rig the textured task directly
                self.download(tex_key, "textured.glb", model_format="glb")
                rig_src = tex_key
            else:
                self.create("remesh", ["remesh", "create", "--input-task-id", self.st["tasks"][tex_key],
                                       "--topology", "triangle", "--target-polycount", str(s["tris"]),
                                       "--target-formats", "glb"])
                self.wait("remesh")
                self.download("remesh", "textured.glb", model_format="glb")
                rig_src = "remesh"
            self.create("rigging", ["rigging", "create", "--input-task-id", self.st["tasks"][rig_src],
                                    "--height-meters", str(s["height"])])
            self.wait("rigging")
            self.download("rigging", "rigged.glb", asset="result.rigged_character_glb_url")
            self.download("rigging", "anim_Walking.glb", asset="result.basic_animations.walking_glb_url")
            self.download("rigging", "anim_Running.glb", asset="result.basic_animations.running_glb_url")
            pending = []
            for clip, aid in s["anims"].items():
                if aid in (-2, -1):
                    continue  # bundled with rigging
                key = f"anim-{clip}"
                self.create(key, ["animate", "create", "--rig-task-id", self.st["tasks"]["rigging"],
                                  "--action-id", str(aid)])
                pending.append((key, clip))
            for key, clip in pending:
                self.wait(key)
                self.download(key, f"anim_{clip}.glb", asset="result.animation_glb_url")
            self.st["complete"] = True
            self.save()
            log(self.slug, "COMPLETE")
        except Exception as e:
            log(self.slug, f"ERROR {e}")


jobs = [Job(j) for j in roster["jobs"] if not only or j["slug"] in only]
with ThreadPoolExecutor(max_workers=len(jobs)) as ex:
    for j in jobs:
        ex.submit(j.run)
        time.sleep(1.5)
print("ALL DONE", flush=True)
