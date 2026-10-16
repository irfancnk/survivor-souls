#!/usr/bin/env bash
# Main merge lock (KAN-228): only ONE routine merges origin/main, re-tests and pushes main at a time.
# The lock is the ref refs/heads/lock/main-merge on origin, taken and released with compare-and-swap.
#
# Usage:
#   Tools/routine/main_lock.sh acquire <RUN_ID> <KAN-N> [wait-min]  take it (waits, default 8 min: fits in one tool call)
#   Tools/routine/main_lock.sh refresh                              new timestamp (after each long step: the lock is STALE at 75 min)
#   Tools/routine/main_lock.sh release                              free it (only your own lock)
#   Tools/routine/main_lock.sh status
# acquire prints LOCK_SHA=<sha> and keeps it in <worktree git dir>/ss-main-lock for refresh / release.
# Exit 0 = you hold the lock, 3 = busy (do other work, try again), 1 = error.
set -u

REF=refs/heads/lock/main-merge
STALE_SECS=$((75 * 60))
MINE="$(git rev-parse --absolute-git-dir)/ss-main-lock"

retry() { local i; for i in 1 2 3 4 5 6; do "$@" && return 0; sleep 10; done; return 1; }
remote_sha() { git ls-remote origin "$REF" | cut -f1; }
lock_age() { # $1 = sha -> seconds since the lock commit
  git fetch -q origin "$REF" 2>/dev/null
  echo $(( $(date +%s) - $(git log -1 --format=%ct "$1" 2>/dev/null || echo 0) ))
}
new_commit() {
  retry git fetch -q origin main || return 1
  git commit-tree "origin/main^{tree}" -p origin/main -m "lock main-merge run=$1 ticket=$2 worktree=$(git rev-parse --show-toplevel) $(date -u +%FT%TZ)"
}

case "${1:-}" in
status)
  sha=$(remote_sha)
  if [ -z "$sha" ]; then echo "main-merge lock: free"; exit 0; fi
  age=$(lock_age "$sha")
  echo "main-merge lock: HELD sha=$sha age=$((age / 60))min $([ "$age" -lt $STALE_SECS ] && echo LIVE || echo STALE)"
  echo "  $(git log -1 --format=%s "$sha" 2>/dev/null)"
  ;;
acquire)
  RUN_ID=${2:?run id}; KEY=${3:?KAN-N}; WAIT=${4:-8}
  deadline=$(( $(date +%s) + WAIT * 60 ))
  while :; do
    old=$(remote_sha)
    C=$(new_commit "$RUN_ID" "$KEY") || { echo "ERROR: fetch failed"; exit 1; }
    if [ -z "$old" ]; then
      lease="--force-with-lease=$REF:"
    elif [ "$(lock_age "$old")" -ge $STALE_SECS ]; then
      echo "taking over STALE lock $old: $(git log -1 --format=%s "$old" 2>/dev/null)"
      lease="--force-with-lease=$REF:$old"
    else
      lease=""
    fi
    if [ -n "$lease" ] && git push -q "$lease" origin "${C}:$REF" 2>/dev/null && [ "$(remote_sha)" = "$C" ]; then
      echo "$C" > "$MINE"
      echo "LOCK_SHA=$C"
      exit 0
    fi
    if [ "$(date +%s)" -ge "$deadline" ]; then
      echo "BUSY: $("$0" status | tr '\n' ' ')"
      exit 3
    fi
    sleep 30
  done
  ;;
refresh)
  [ -f "$MINE" ] || { echo "ERROR: this worktree holds no lock"; exit 1; }
  old=$(cat "$MINE")
  msg=$(git log -1 --format=%s "$old" 2>/dev/null)
  run=$(echo "$msg" | sed -n 's/.*run=\([^ ]*\).*/\1/p'); key=$(echo "$msg" | sed -n 's/.*ticket=\([^ ]*\).*/\1/p')
  C=$(new_commit "${run:-unknown}" "${key:-unknown}") || exit 1
  if git push -q "--force-with-lease=$REF:$old" origin "${C}:$REF"; then echo "$C" > "$MINE"; echo "LOCK_SHA=$C (refreshed)"; else
    echo "ERROR: the lock is not yours any more (taken over as stale?). Do NOT push main; acquire again."; rm -f "$MINE"; exit 1; fi
  ;;
release)
  [ -f "$MINE" ] || { echo "nothing to release (no lock file in this worktree)"; exit 0; }
  sha=$(cat "$MINE")
  if retry git push -q "--force-with-lease=$REF:$sha" origin ":$REF"; then echo "released $sha"; else
    echo "WARN: release refused: the lock is not $sha any more (someone took it over). Nothing removed."; fi
  rm -f "$MINE"
  ;;
*)
  sed -n '2,12p' "$0"; exit 1 ;;
esac
