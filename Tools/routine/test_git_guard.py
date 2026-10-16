#!/usr/bin/env python3
"""Tests for .claude/hooks/git_guard.py (KAN-228). Run: python3 -I Tools/routine/test_git_guard.py"""
import json
import os
import subprocess
import sys

ROOT = os.path.realpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
HOOK = os.path.join(ROOT, ".claude", "hooks", "git_guard.py")
OTHER_WT = os.path.join(os.path.dirname(ROOT), "kan-1-r0-other")

BLOCK = [
    "git push --force origin HEAD:main",
    "git push -f origin feat/kan-1-x",
    "git push origin +HEAD:main",
    "git push origin :refs/heads/feat/kan-1-x",
    "git push origin --delete feat/kan-1-x",
    "git push --force-with-lease origin HEAD:main",
    "git push origin :refs/heads/claim/kan-1",  # delete without lease
    "git reset --hard origin/main",
    "cd /tmp && git -C " + ROOT + " reset --hard",
    "git clean -fdx",
    "git checkout -- .",
    "git checkout .",
    "git checkout -- ProjectSettings",
    "git checkout -f main",
    "git restore .",
    "git restore --worktree --staged Assets",
    "git stash",
    "git stash pop",
    "git stash drop stash@{0}",
    "git rebase origin/main",
    "git branch -D feat/kan-1-x",
    "git update-ref -d refs/heads/main",
    "git worktree remove " + OTHER_WT,
    "git worktree remove --force " + ROOT,
    "rm -rf " + OTHER_WT,
    "rm -rf ../kan-1-r0-other/Library",
    "rm -rf /Users/irfancankaleli/survivor-souls/.git",
    "rm -rf .git",
    'rm -rf "$UNKNOWN_DIR/x"',
    "git fetch origin && git reset --hard origin/main",
    "git reflog expire --expire=now --all",
]

ALLOW = [
    "git status --short",
    "git fetch origin --prune",
    "git push -u origin feat/kan-1-x",
    "git push origin HEAD:refs/heads/feat/kan-1-x",
    "git push origin HEAD:main",
    'git push origin "${C}:refs/heads/claim/kan-1"',
    "git push --force-with-lease=refs/heads/claim/kan-1:abc123 origin :refs/heads/claim/kan-1",
    "git push --force-with-lease=refs/heads/lock/main-merge: origin abc:refs/heads/lock/main-merge",
    "git push --force-with-lease=refs/heads/lock/main-merge:abc origin :refs/heads/lock/main-merge",
    "git checkout -- Assets/Settings ProjectSettings/GraphicsSettings.asset",
    "git checkout --theirs Assets/_Project/Scenes/Game.unity",
    "git checkout -b feat/kan-1-x origin/main",
    "git checkout feat/kan-1-x",
    "git restore --staged STATE.md",
    "git reset HEAD~1",
    "git reset --soft HEAD~1",
    "git merge origin/main",
    "git merge --abort",
    "git rebase --abort",
    "git stash list",
    'git stash push -u -m "kan-1-tag"',
    "git stash apply 1234abcd",
    "git branch -d feat/kan-1-x",
    "git worktree add ../kan-1-r1 -b feat/kan-1-x origin/main",
    "git worktree prune",
    "git clean -n",
    "git commit -m 'docs: never run git reset --hard; use a WIP commit'",
    "rm -rf /private/tmp/claude-501/x/scratchpad/build-r1",
    "rm -rf Library/Bee",
    "rm -rf " + ROOT + "/Temp",
    "rm STATE.md.orig",
    "Tools/routine/preflight.sh --prune",
    "ls -la",
]


def run(cmd):
    payload = json.dumps({"tool_name": "Bash", "tool_input": {"command": cmd}, "cwd": ROOT})
    env = {k: v for k, v in os.environ.items() if k != "SS_GIT_GUARD"}
    p = subprocess.run([sys.executable, "-I", HOOK], input=payload, capture_output=True, text=True, env=env)
    return p.returncode


def main():
    bad = 0
    for c in BLOCK:
        if run(c) != 2:
            print("NOT BLOCKED:", c)
            bad += 1
    for c in ALLOW:
        if run(c) != 0:
            print("WRONGLY BLOCKED:", c)
            bad += 1
    print(f"{len(BLOCK) + len(ALLOW) - bad}/{len(BLOCK) + len(ALLOW)} pass")
    return 1 if bad else 0


if __name__ == "__main__":
    sys.exit(main())
