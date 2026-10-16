#!/usr/bin/env bash
# Unity batch run with a machine-wide slot limit (KAN-228). Every routine runs Unity ONLY through this script.
# The Mac has 10 cores / 16 GB: more than 2 Unity processes at once thrash it (25 min shader compiles, KAN-23).
#
# Usage:
#   Tools/routine/unity.sh <RUN_ID> <step> [unity args...]   run Unity on THIS worktree (waits for a free slot)
#   Tools/routine/unity.sh status                           show the slots
# Examples:
#   Tools/routine/unity.sh r202610161400-a3f9c1 scene -executeMethod SurvivorSouls.Editor.GameSceneBuilder.BuildFromCommandLine
#   Tools/routine/unity.sh r202610161400-a3f9c1 playmode -runTests -testPlatform PlayMode -testResults /private/tmp/.../pm.xml
# Adds -batchmode -projectPath <this worktree> -logFile and (except for -runTests) -quit.
# A run longer than ~9 min must start in the background (Bash run_in_background), the slot stays held until Unity exits.
# Env: SS_UNITY_SLOTS (default 2), SS_UNITY_WAIT minutes to wait for a slot (default 90), SS_LOG_DIR (default /private/tmp/ss-unity).
set -u

TOP=$(git rev-parse --show-toplevel) || exit 1
COMMON=$(cd "$(git rev-parse --git-common-dir)" && pwd)
SLOTS=${SS_UNITY_SLOTS:-2}
STALE_SECS=$((4 * 3600))

slot_owner_alive() { # $1 = slot dir; true when its owner process still runs and the slot is not older than 4 h
  local pid ts
  pid=$(sed -n 's/^pid=//p' "$1/owner" 2>/dev/null)
  ts=$(stat -f %m "$1" 2>/dev/null || echo 0)
  [ -n "$pid" ] && kill -0 "$pid" 2>/dev/null && [ $(( $(date +%s) - ts )) -lt $STALE_SECS ]
}

if [ "${1:-}" = "status" ]; then
  for i in $(seq 1 "$SLOTS"); do
    d="$COMMON/ss-unity-slot-$i"
    if [ -d "$d" ]; then
      echo "slot $i: $(tr '\n' ' ' < "$d/owner" 2>/dev/null) $(slot_owner_alive "$d" && echo ALIVE || echo STALE)"
    else
      echo "slot $i: free"
    fi
  done
  exit 0
fi

RUN_ID=${1:?run id}
STEP=${2:?step name}
shift 2
VERSION=$(sed -n 's/^m_EditorVersion: //p' "$TOP/ProjectSettings/ProjectVersion.txt")
UNITY="/Applications/Unity/Hub/Editor/$VERSION/Unity.app/Contents/MacOS/Unity"
[ -x "$UNITY" ] || { echo "ERROR: no Unity $VERSION at $UNITY"; exit 1; }
LOG_DIR=${SS_LOG_DIR:-/private/tmp/ss-unity}
mkdir -p "$LOG_DIR"
LOG="$LOG_DIR/unity-$RUN_ID-$STEP.log"

if pgrep -fl "^/Applications/Unity/Hub/Editor/.*/Unity " | grep -F -- "-projectPath $TOP" >/dev/null; then
  echo "ERROR: Unity already runs on $TOP (one Unity per worktree). Wait for it."
  exit 1
fi

SLOT=""
deadline=$(( $(date +%s) + ${SS_UNITY_WAIT:-90} * 60 ))
said=0
while [ -z "$SLOT" ]; do
  for i in $(seq 1 "$SLOTS"); do
    d="$COMMON/ss-unity-slot-$i"
    if [ -d "$d" ] && ! slot_owner_alive "$d"; then
      echo "slot $i stale ($(tr '\n' ' ' < "$d/owner" 2>/dev/null)): freeing it"
      rm -rf "$d"
    fi
    if mkdir "$d" 2>/dev/null; then
      printf 'run=%s\nstep=%s\npid=%s\nproject=%s\nstart=%s\n' "$RUN_ID" "$STEP" "$$" "$TOP" "$(date -u +%FT%TZ)" > "$d/owner"
      SLOT=$d
      break
    fi
  done
  [ -n "$SLOT" ] && break
  if [ "$(date +%s)" -ge "$deadline" ]; then
    echo "ERROR: no Unity slot after ${SS_UNITY_WAIT:-90} min"; "$0" status; exit 3
  fi
  if [ $((said % 10)) = 0 ]; then echo "waiting for a Unity slot ($(date +%T)):"; "$0" status; fi
  said=$((said + 1))
  sleep 30
done

release() { [ -n "$SLOT" ] && [ "$(sed -n 's/^pid=//p' "$SLOT/owner" 2>/dev/null)" = "$$" ] && rm -rf "$SLOT"; }
trap release EXIT
trap 'release; exit 130' INT TERM

ARGS=(-batchmode -projectPath "$TOP" -logFile "$LOG")
case " $* " in *" -runTests "*) ;; *) ARGS+=(-quit) ;; esac
echo "unity: slot $(basename "$SLOT") step=$STEP log=$LOG start=$(date +%T)"
"$UNITY" "${ARGS[@]}" "$@"
CODE=$?
echo "unity: exit=$CODE step=$STEP end=$(date +%T) log=$LOG"
grep -E 'error CS[0-9]+|Scripts have compiler errors|Exception:' "$LOG" 2>/dev/null | sort -u | head -15
exit $CODE
