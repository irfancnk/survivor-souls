#!/usr/bin/env bash
# Fast preflight for the hourly ticket routine: one call prints what step 1 needs.
# Read-only, except `--prune`, which removes clean routine worktrees (.claude/worktrees/kan-*)
# whose HEAD is already on origin/main. App session worktrees are never removed.
#
# Usage: Tools/routine/preflight.sh [--prune]
set -u

PRUNE=0
[ "${1:-}" = "--prune" ] && PRUNE=1

TOP=$(git rev-parse --show-toplevel) || exit 1
cd "$TOP" || exit 1
COMMON=$(cd "$(git rev-parse --git-common-dir)" && pwd)
MAIN_CHECKOUT=$(dirname "$COMMON")
NOW=$(date +%s)
LIVE_SECS=$((3 * 3600))
ROUTINE_WT="$MAIN_CHECKOUT/.claude/worktrees/kan-"

age_h() { echo $(( (NOW - $1) / 3600 )); }

retry() {
  local i
  for i in 1 2 3 4 5 6; do
    "$@" && return 0
    sleep 10
  done
  return 1
}

retry git fetch origin --prune -q || echo "WARN: git fetch failed 6 times"

echo "== origin/main (last 15)"
git log --oneline -15 origin/main

echo
echo "== KAN keys named in origin/main commit subjects (a ticket here has work on main: check before you build it again)"
git log origin/main --format=%s | grep -oE 'KAN-[0-9]+' | sort -t- -k2 -n -u | tr '\n' ' '
echo

BUSY_AREAS=""
LIVE_BRANCHES=""
echo
echo "== claims / locks on origin (git side only: also check the last Jira comment before you call one STALE)"
found=0
while read -r ref sha ts; do
  [ -z "$ref" ] && continue
  found=1
  name=${ref#refs/remotes/origin/}
  newest=$ts
  branch="-"
  if [[ $name == claim/kan-* ]]; then
    key=${name#claim/}
    b=$(git for-each-ref --sort=-committerdate --count=1 --format='%(refname:strip=3) %(committerdate:unix)' "refs/remotes/origin/feat/$key-*")
    if [ -n "$b" ]; then
      branch=${b% *}
      bts=${b##* }
      [ "$bts" -gt "$newest" ] && newest=$bts
    fi
  fi
  if [[ $name == lock/main-merge ]]; then
    state=$([ $((NOW - ts)) -lt $((75 * 60)) ] && echo LIVE || echo STALE)
  else
    state=$([ $((NOW - newest)) -lt $LIVE_SECS ] && echo LIVE || echo STALE)
  fi
  echo "$state $name sha=$sha ref-age=$(age_h "$ts")h newest-activity=$(age_h "$newest")h branch=$branch"
  if [ "$state" = LIVE ] && [[ $name == claim/* ]]; then
    areas=$(git log -1 --format=%s "$sha" | sed -n 's/.*areas=\([^ ]*\).*/\1/p')
    BUSY_AREAS="$BUSY_AREAS ${areas//,/ }"
    [ "$branch" != "-" ] && LIVE_BRANCHES="$LIVE_BRANCHES origin/$branch"
  fi
  echo "      $(git log -1 --format=%s "$sha")"
  if [ "$branch" != "-" ]; then
    git diff --name-only "origin/main...origin/$branch" | sed 's/^/      file: /'
  fi
done < <(git for-each-ref --format='%(refname) %(objectname:short) %(committerdate:unix)' refs/remotes/origin/claim refs/remotes/origin/lock)
[ $found = 0 ] && echo "(none)"

echo
echo "== BUSY AREAS (KAN-228: do not pick a ticket whose areas meet one of these)"
echo "$BUSY_AREAS" | tr ' ' '\n' | grep . | sort | uniq -c | awk '{printf "%s(%s) ", $2, $1} END {print ""}'

echo
echo "== OVERLAP: files changed on 2+ unmerged feat branches (expect a merge conflict; the later merger resolves it)"
for b in $(git for-each-ref --format='%(refname:strip=2)' refs/remotes/origin/feat); do
  [ "$(git rev-list --count "origin/main..$b")" = 0 ] && continue
  git diff --name-only "origin/main...$b" | sed "s|$| ${b#origin/}|"
done | sort | awk '{n[$1]++; l[$1]=l[$1]" "$2} END {for (f in n) if (n[f]>1) print f " <-" l[f]}' | head -30

echo
echo "== origin feat/* branches with commits not on main (claude/* app branches are skipped)"
found=0
while read -r ref ts; do
  [ -z "$ref" ] && continue
  name=${ref#refs/remotes/origin/}
  ahead=$(git rev-list --count "origin/main..$ref")
  [ "$ahead" = 0 ] && continue
  found=1
  echo "$name ahead=$ahead last-commit=$(age_h "$ts")h :: $(git log -1 --format=%s "$ref" | cut -c1-120)"
done < <(git for-each-ref --format='%(refname) %(committerdate:unix)' refs/remotes/origin/feat)
[ $found = 0 ] && echo "(none)"

echo
echo "== worktrees"
UNITY=$(pgrep -fl Unity 2>/dev/null | grep -i -- '-projectPath' || true)
best_lib="" best_behind=999999
dirty=0 clean=0 noisy=0 pruned=0 prunable=0
# Changes that are not work: Unity build rewrites (STATE.md KNOWN ISSUES) and the synced .agents/ skill copy.
NOISE='^( M Assets/Settings/[A-Za-z_]*(RPAsset|DefaultVolumeProfile|UniversalRenderPipelineGlobalSettings)\.asset| M ProjectSettings/GraphicsSettings\.asset|\?\? \.agents/)$'
while read -r line; do
  case $line in
    "worktree "*) wt=${line#worktree } ;;
  esac
  [[ $line != "worktree "* ]] && continue
  [ "$wt" = "$MAIN_CHECKOUT" ] && continue
  [ -d "$wt" ] || continue
  busy=$(echo "$UNITY" | grep -F -- "$wt" || true)
  all=$(git -C "$wt" status --porcelain 2>/dev/null)
  st=$(echo "$all" | grep -Ev "$NOISE" || true)
  br=$(git -C "$wt" rev-parse --abbrev-ref HEAD 2>/dev/null)
  if [ -n "$st" ]; then
    dirty=$((dirty + 1))
    newest=0
    while read -r f; do
      m=$(stat -f %m "$wt/$f" 2>/dev/null || echo 0)
      [ "$m" -gt "$newest" ] && newest=$m
    done < <(echo "$st" | cut -c4- | sed 's/.* -> //')
    mins=$(( (NOW - newest) / 60 ))
    echo "DIRTY $wt branch=$br files=$(echo "$st" | wc -l | tr -d ' ') newest-change=${mins}min unity=$([ -n "$busy" ] && echo RUNNING || echo no)"
    continue
  fi
  clean=$((clean + 1))
  [ -n "$all" ] && noisy=$((noisy + 1))
  if [ -d "$wt/Library" ] && [ -z "$busy" ]; then
    behind=$(cd "$wt" && git rev-list --count HEAD..origin/main 2>/dev/null || echo 999999)
    if [ "$behind" -lt "$best_behind" ]; then best_behind=$behind; best_lib=$wt; fi
  fi
  if [[ $wt == "$ROUTINE_WT"* ]] && [ -z "$busy" ] && [ "$wt" != "$TOP" ] \
     && git -C "$wt" merge-base --is-ancestor HEAD origin/main 2>/dev/null; then
    hts=$(git -C "$wt" log -1 --format=%ct HEAD)
    if [ $((NOW - hts)) -ge $LIVE_SECS ]; then
      prunable=$((prunable + 1))
      if [ $PRUNE = 1 ]; then
        # --force only discards build noise / .agents: real changes made the worktree DIRTY above.
        if retry git worktree remove --force "$wt" 2>/dev/null; then pruned=$((pruned + 1)); else echo "WARN: could not remove $wt"; fi
      fi
    fi
  fi
done < <(git worktree list --porcelain)
echo "dirty=$dirty clean=$clean (of which build-noise-only=$noisy) prunable-routine-worktrees=$prunable pruned=$pruned"
[ $PRUNE = 1 ] && git worktree prune
echo "Library clone source: ${best_lib:-none} (behind origin/main by $best_behind commits)"
echo "Unity batch processes: $(echo "$UNITY" | grep -c . || true)"

echo
echo "== Unity slots (Tools/routine/unity.sh, max ${SS_UNITY_SLOTS:-2} at once)"
"$TOP/Tools/routine/unity.sh" status 2>/dev/null || echo "(unity.sh missing on this branch)"

echo
echo "== disk: $(df -h /System/Volumes/Data | awk 'NR==2 {print $4 " free (" $5 " used)"}')"
free_gb=$(df -g /System/Volumes/Data | awk 'NR==2 {print $4}')
[ "${free_gb:-0}" -lt 50 ] && echo "WARN: less than 50 GB free: no Mac / iOS build until old scratch builds are deleted (KAN-207)"

echo
echo "== iPhone lock"
LOCK="$COMMON/ss-iphone.lock"
if [ -d "$LOCK" ]; then
  echo "HELD, age $(( (NOW - $(stat -f %m "$LOCK")) / 60 ))min"
else
  echo "free"
fi
