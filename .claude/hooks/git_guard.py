#!/usr/bin/env python3
"""PreToolUse guard for Bash (KAN-228): stops commands that can erase the work of a parallel session.

Up to 5 ticket routines and the user's own sessions share one .git, one stash stack and one origin.
Rules in a prompt cannot stop a bad command; this hook can. Exit 2 = blocked, the reason goes to Claude.

Blocked:
- git push with --force / -f / +refspec / --mirror; --force-with-lease and ref deletes only for claim/* and lock/* refs
- git reset --hard / --merge / --keep, git clean -f, git rebase (except --abort), git filter-branch / filter-repo
- git checkout / restore of a broad path (., *, :/, Assets, ProjectSettings, Packages, ...) or with --force
- git stash (bare), stash pop / drop / clear / save (shared stack: use a WIP commit)
- git branch -D / -M / --force, git update-ref -d (except claim/* and lock/*), git reflog expire, git gc --prune
- git worktree remove of another worktree, or with --force
- rm -r outside the session worktree and the temp folders

Override: start Claude with SS_GIT_GUARD=off in the environment (an inline `SS_GIT_GUARD=off cmd` does not work).
Tests: Tools/routine/test_git_guard.py
"""
import json
import os
import re
import shlex
import subprocess
import sys

BROAD_PATHS = {".", "./", "*", ":/", ":(top)", ":/*", "Assets", "Assets/_Project", "ProjectSettings", "Packages",
               "Docs", "Tools", "ArtSource", "Assets/_Project/Scripts", "Assets/_Project/Editor"}
SHARED_REF = re.compile(r"^(refs/heads/)?(claim|lock)/")
TEMP_ROOTS = ("/tmp/", "/private/tmp/", "/var/folders/", "/private/var/folders/")
SEPARATORS = re.compile(r"\|\||&&|;|\||\n|\$\(|`|\(|\)")


def toplevel(cwd):
    try:
        out = subprocess.run(["git", "-C", cwd, "rev-parse", "--show-toplevel"], capture_output=True, text=True, timeout=5)
        return os.path.realpath(out.stdout.strip()) if out.returncode == 0 and out.stdout.strip() else None
    except Exception:
        return None


def words(segment):
    try:
        w = shlex.split(segment, comments=True)
    except ValueError:
        w = segment.split()
    # drop env assignments and wrappers: FOO=1 env sudo command nice time
    while w and (re.match(r"^[A-Za-z_][A-Za-z0-9_]*=", w[0]) or w[0] in ("env", "sudo", "command", "nice", "time", "exec")):
        w = w[1:]
    return w


def git_parts(w):
    """['git', '-C', 'x', 'push', ...] -> ('push', [...args]) or (None, [])"""
    if not w or os.path.basename(w[0]) != "git":
        return None, []
    i = 1
    while i < len(w) and w[i].startswith("-"):
        i += 2 if w[i] in ("-C", "-c", "--git-dir", "--work-tree", "--namespace") else 1
    if i >= len(w):
        return None, []
    return w[i], w[i + 1:]


def short_flag(args, letter):
    return any(re.match(r"^-[A-Za-z]*" + letter, a) and not a.startswith("--") for a in args)


def pathspecs(args):
    if "--" in args:
        return args[args.index("--") + 1:]
    return [a for a in args if not a.startswith("-")]


def is_broad(p):
    p = p.rstrip("/") if p not in ("./", ":/") else p
    return p in BROAD_PATHS or p.endswith("/*") or p == ""


def check_git(sub, args, own, cwd="."):
    if sub == "push":
        if "--mirror" in args or "--force" in args or short_flag(args, "f"):
            return "git push --force / -f is blocked (it can drop commits another session pushed). Merge origin/main and push again."
        refspecs = [a for a in args if not a.startswith("-")][1:]  # first non-flag word is the remote
        lease = any(a.startswith("--force-with-lease") for a in args)
        deletes = "--delete" in args or short_flag(args, "d")
        for r in refspecs:
            dst = r.split(":", 1)[1] if ":" in r else r
            if r.startswith("+"):
                return "push with a +refspec is a force push and is blocked."
            if (r.startswith(":") or deletes) and not SHARED_REF.match(dst.lstrip(":")):
                return f"deleting the remote ref '{dst}' is blocked. Only claim/* and lock/* refs may be deleted (with --force-with-lease)."
            if lease and not SHARED_REF.match(dst.lstrip(":")):
                return f"--force-with-lease is allowed only for claim/* and lock/* refs, not '{dst}'."
        if (deletes or any(r.startswith(":") for r in refspecs)) and not lease:
            return "deleting a claim/lock ref needs --force-with-lease=<ref>:<your sha> (never remove another run's claim)."
        return None
    if sub == "reset" and any(a in ("--hard", "--merge", "--keep") for a in args):
        return "git reset --hard is blocked (it erases uncommitted work). Commit a WIP, or `git checkout -- <file>` for single files."
    if sub == "clean" and ("--force" in args or short_flag(args, "f")):
        return "git clean -f is blocked. Delete only the files you made, by name."
    if sub == "rebase" and not any(a in ("--abort", "--quit") for a in args):
        return "git rebase is blocked (pushed branches must not be rewritten). Use `git merge origin/main`."
    if sub in ("filter-branch", "filter-repo"):
        return f"git {sub} is blocked."
    if sub == "reflog" and args[:1] == ["expire"]:
        return "git reflog expire is blocked (the reflog is the last backup)."
    if sub == "gc" and any(a.startswith("--prune") for a in args):
        return "git gc --prune is blocked."
    if sub == "checkout":
        if "--force" in args or short_flag(args, "f"):
            return "git checkout --force is blocked (it drops uncommitted changes)."
        if "--" in args or "." in args:
            broad = [p for p in pathspecs(args) if is_broad(p)]
            if broad:
                return f"git checkout of a broad path {broad} is blocked (it erased a parallel change on 2026-10-13). Name the files: `git checkout -- Assets/Settings ProjectSettings/GraphicsSettings.asset` is allowed."
        return None
    if sub == "restore":
        staged_only = ("--staged" in args or "-S" in args) and not ("--worktree" in args or "-W" in args)
        if staged_only:
            return None
        broad = [p for p in pathspecs([a for a in args if a not in ("--source",)]) if is_broad(p)]
        if broad:
            return f"git restore of a broad path {broad} is blocked. Name the files."
        return None
    if sub == "stash":
        action = next((a for a in args if not a.startswith("-")), None)
        if action in (None, "pop", "drop", "clear", "save"):
            return "git stash / stash pop / drop / clear is blocked: the stash stack is shared by every session. Commit a WIP instead."
        return None
    if sub == "branch":
        if any(a in ("-D", "-M", "-C", "--force") for a in args) or short_flag(args, "f"):
            return "git branch -D / -M / --force is blocked. `git branch -d` (merged only) is allowed."
        return None
    if sub == "update-ref" and ("-d" in args or "--delete" in args):
        refs = [a for a in args if not a.startswith("-")]
        if not refs or not SHARED_REF.match(refs[0]):
            return "git update-ref -d is blocked except for claim/* and lock/* refs."
        return None
    if sub == "worktree" and args[:1] == ["remove"]:
        rest = args[1:]
        if "--force" in rest or short_flag(rest, "f"):
            return "git worktree remove --force is blocked. Tools/routine/preflight.sh --prune removes clean, merged routine worktrees."
        targets = [a for a in rest if not a.startswith("-")]
        for t in targets:
            if own is None or os.path.realpath(os.path.join(cwd, t)) != own:
                return f"removing another worktree ('{t}') is blocked. Use Tools/routine/preflight.sh --prune."
        return None
    return None


def check_rm(w, cwd, own):
    args = w[1:]
    recursive = any(a in ("-r", "-R", "--recursive") or (re.match(r"^-[A-Za-z]*[rR]", a) and not a.startswith("--")) for a in args)
    if not recursive:
        return None
    targets = [a for a in args if not a.startswith("-")]
    for t in targets:
        e = os.path.expandvars(os.path.expanduser(t))
        if "$" in e or "`" in e:
            return f"rm -r of '{t}' is blocked: the hook cannot see shell variables. Write the literal path."
        p = os.path.realpath(os.path.join(cwd, e))
        if p.startswith(TEMP_ROOTS) or (p + "/").startswith(TEMP_ROOTS):
            continue
        if own and p != own and p.startswith(own + "/") and os.path.basename(p) != ".git":
            continue
        return f"rm -r of '{t}' is blocked: only folders inside your own worktree ({own}) or the temp folders may be removed."
    return None


def check(command, cwd):
    own = toplevel(cwd)
    for segment in SEPARATORS.split(command):
        w = words(segment.strip())
        if not w:
            continue
        if w[0] == "cd" and len(w) > 1:  # follow `cd x && ...` so relative paths resolve right
            cwd = os.path.realpath(os.path.join(cwd, os.path.expanduser(w[1])))
            continue
        sub, args = git_parts(w)
        reason = check_git(sub, args, own, cwd) if sub else None
        if not reason and os.path.basename(w[0]) == "rm":
            reason = check_rm(w, cwd, own)
        if reason:
            return reason
    return None


def main():
    if os.environ.get("SS_GIT_GUARD") == "off":
        return 0
    try:
        data = json.load(sys.stdin)
    except Exception:
        return 0
    if data.get("tool_name") != "Bash":
        return 0
    command = (data.get("tool_input") or {}).get("command") or ""
    reason = check(command, data.get("cwd") or os.getcwd())
    if reason:
        print(f"[git_guard KAN-228] BLOCKED: {reason}", file=sys.stderr)
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
