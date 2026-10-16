# Parallel ticket routines (KAN-228)

Up to 5 Claude routines work on KAN tickets at the same time. Each one runs `ROUTINE_PROMPT.md` with its own lane.

## Start the routines
Make one scheduled task per lane in the Claude desktop app (Code tab, project `survivor-souls`, new worktree per run).
Prompt of each task: `LANE=<n>` on the first line, then the full text of `Tools/routine/ROUTINE_PROMPT.md`.
Give the starts a 12-minute offset, so preflights and Unity compiles do not all start at once:

| Task | Lane | Schedule | Model suggestion |
|---|---|---|---|
| ss-lane-1 | 1 gameplay | hourly at :00 | Opus |
| ss-lane-2 | 2 UI + meta | hourly at :12 | Opus |
| ss-lane-3 | 3 art | hourly at :24 | Opus |
| ss-lane-4 | 4 audio, feel, perf, bugs | hourly at :36 | Sonnet (small tickets) |
| ss-lane-5 | 5 device, tooling, splitting | hourly at :48 | Sonnet |

A run can be longer than 1 hour. The next run of the same lane then takes another ticket (claims stop doubles).
Start with 2 lanes for one day. Check the waits in the run reports (Unity slots, merge lock) and the conflict count, then add lanes.

## Usage budget
One routine used about 20 % of the plan in 5 hours. 5 routines use about 100 %, with no room for your own sessions.
Start with 4 lanes. Add the 5th lane when the reports show tokens left at the end of the window.

## What keeps the routines safe
| Risk | Guard |
|---|---|
| Two routines take one ticket | claim ref `claim/kan-N` on origin, compare-and-swap (`--force-with-lease`) |
| Two areas edited at once | claim names `areas=`; preflight prints BUSY AREAS; step 2 skips tickets that meet them |
| A merge drops other work | main merge lock `Tools/routine/main_lock.sh` (one merger at a time) + re-test on the merge + fast-forward push only |
| Hotspot files conflict | new work in new files: `Editor/SceneSteps/*Step.cs`, `Scripts/Dev/*ShotArgs.cs`, `UI/Hud/Features/*.uss`, `Docs/state/done/*.md` |
| Builder output merged into broken YAML | `.gitattributes` merge=binary + `merge_main.sh` takes main's file, then the builders run again |
| A destructive command | `.claude/hooks/git_guard.py` (PreToolUse on Bash). Tests: `python3 -I Tools/routine/test_git_guard.py` |
| The Mac thrashes | `Tools/routine/unity.sh`: at most 2 Unity processes (`SS_UNITY_SLOTS`) |
| The phone is used twice | `ss-iphone.lock` folder in the shared `.git` |

## Manual step for Irfan (GitHub)
Protect `main` on GitHub: Settings > Branches > add a rule for `main`: block force pushes, block deletion.
This is the last guard if a hook is off. Do not require pull requests: the routines push main directly.

## Override
Start Claude with `SS_GIT_GUARD=off` in the environment to turn the guard off for one session (for cleanup work such as KAN-207).
