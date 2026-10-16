#!/usr/bin/env bash
# Merge origin/main into the current feature branch (KAN-228).
# - Builder output (Tools/routine/generated_paths.txt) is never merged by hand: on a conflict main's file is taken.
# - Prints what came in from main and what you must do next: REAL CONFLICTS / REBUILD SCENES / RETEST.
# Exit 0 = merged (maybe with follow-up steps), 1 = real conflicts are left (resolve them, see ROUTINE_PROMPT 5.4), 2 = refused.
set -u

TOP=$(git rev-parse --show-toplevel) || exit 2
cd "$TOP" || exit 2
NOISE='^( M Assets/Settings/[A-Za-z_]*(RPAsset|DefaultVolumeProfile|UniversalRenderPipelineGlobalSettings)\.asset| M ProjectSettings/GraphicsSettings\.asset|\?\? \.agents/)$'
dirty=$(git status --porcelain | grep -Ev "$NOISE" || true)
if [ -n "$dirty" ]; then
  echo "REFUSED: commit your work first (merge needs a clean tree):"; echo "$dirty" | head; exit 2
fi
git checkout -q -- Assets/Settings ProjectSettings/GraphicsSettings.asset 2>/dev/null # build noise only

for i in 1 2 3 4 5 6; do git fetch -q origin && break; sleep 10; done
before=$(git rev-parse HEAD)
if git merge-base --is-ancestor origin/main HEAD; then echo "UP TO DATE with origin/main"; exit 0; fi

git merge --no-edit origin/main >/dev/null 2>&1
GENERATED=$(grep -Ev '^\s*(#|$)' Tools/routine/generated_paths.txt)
rebuild=0
for f in $(git diff --name-only --diff-filter=U); do
  for g in $GENERATED; do
    # shellcheck disable=SC2053
    if [[ $f == $g ]]; then git checkout -q --theirs -- "$f" && git add -- "$f"; echo "generated, took main's: $f"; rebuild=1; break; fi
  done
done

incoming=$(git diff --name-only "$before" origin/main)
echo "== incoming from main: $(echo "$incoming" | grep -c .) files, commits:"
git log --oneline "$before..origin/main" --no-merges | head -20

left=$(git diff --name-only --diff-filter=U)
if [ -n "$left" ]; then
  echo "== REAL CONFLICTS (resolve so BOTH works survive, then commit):"
  for f in $left; do echo "  $f  <- $(git log --oneline --merge -- "$f" | head -3 | tr '\n' ';')"; done
  [ $rebuild = 1 ] && echo "== then REBUILD SCENES (generated files were taken from main)"
  exit 1
fi
git rev-parse -q --verify MERGE_HEAD >/dev/null && git commit -q --no-edit
echo "$incoming" | grep -qE '^Assets/_Project/(Editor|Scripts)/|\.(uxml|uss)$' && rebuild=1
echo "== MERGED $(git rev-parse --short HEAD)"
[ $rebuild = 1 ] && echo "== REBUILD SCENES: Tools/routine/unity.sh <RUN_ID> scene -executeMethod SurvivorSouls.Editor.GameSceneBuilder.BuildFromCommandLine (+ MainMenuSceneBuilder), then commit the output"
echo "== RETEST: compile + EditMode + PlayMode on this merge before you push main"
exit 0
