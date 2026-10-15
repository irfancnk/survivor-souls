#!/usr/bin/env bash
# Version bump (KAN-142). VERSION.json is the one version source; this script is the only writer.
#
#   Tools/bump-version.sh patch|minor|major ["KAN-N Title" ...]
#       PATCH: bug fix, polish, follow-up, tooling. MINOR: new feature. MAJOR: store release / milestone.
#       The build number goes +1 on every bump and never resets. Adds the CHANGELOG.md entry (newest on top)
#       and prints the git tag to put on the merge commit, e.g. v0.3.0+14.
#   Tools/bump-version.sh --check [ref]
#       Fails when the build in VERSION.json is not higher than in <ref> (default origin/main):
#       another session merged first, so rebase and bump again (never reuse a build number).
#   Tools/bump-version.sh --show
#       Prints the current version, build and tag.
#
# Env (tests): SS_VERSION_ROOT = folder with VERSION.json + CHANGELOG.md, SS_VERSION_DATE = entry date (YYYY-MM-DD).
set -euo pipefail
ROOT="${SS_VERSION_ROOT:-$(cd "$(dirname "$0")/.." && pwd)}"

if [[ "${1:-}" == "--check" ]]; then
    REF="${2:-origin/main}"
    OTHER="$(git -C "$ROOT" show "$REF:VERSION.json" 2>/dev/null || echo '{"build": 0}')"
    exec python3 - "$ROOT/VERSION.json" "$OTHER" "$REF" <<'PY'
import json, sys
mine = json.load(open(sys.argv[1]))["build"]
theirs = json.loads(sys.argv[2]).get("build", 0)
if mine <= theirs:
    print(f"build {mine} is not higher than build {theirs} on {sys.argv[3]}: rebase and bump again", file=sys.stderr)
    sys.exit(1)
print(f"ok: build {mine} > {theirs} ({sys.argv[3]})")
PY
fi

exec python3 - "$ROOT" "${SS_VERSION_DATE:-$(date +%Y-%m-%d)}" "$@" <<'PY'
import json, os, re, sys

root, date, args = sys.argv[1], sys.argv[2], sys.argv[3:]
version_path = os.path.join(root, "VERSION.json")
changelog_path = os.path.join(root, "CHANGELOG.md")
data = json.load(open(version_path))
m = re.fullmatch(r"(\d+)\.(\d+)\.(\d+)", data["version"])
if not m:
    sys.exit(f"bad version in VERSION.json: {data['version']}")
major, minor, patch = map(int, m.groups())
build = int(data["build"])

if not args or args[0] == "--show":
    print(f"{data['version']} (build {build}) v{data['version']}+{build}")
    sys.exit(0)

kind, lines = args[0], args[1:]
if kind == "patch":
    patch += 1
elif kind == "minor":
    minor, patch = minor + 1, 0
elif kind == "major":
    major, minor, patch = major + 1, 0, 0
else:
    sys.exit("usage: Tools/bump-version.sh patch|minor|major [\"KAN-N Title\" ...] | --check [ref] | --show")

version, build = f"{major}.{minor}.{patch}", build + 1
with open(version_path, "w") as f:
    json.dump({"version": version, "build": build}, f, indent=2)
    f.write("\n")

entry = [f"## {version} (build {build}) - {date}"] + [f"- {l.strip()}" for l in lines if l.strip()]
if len(entry) == 1:
    entry.append("- No ticket listed")
text = open(changelog_path).read() if os.path.exists(changelog_path) else "# Changelog\n"
at = text.find("\n## ")
head, rest = (text, "") if at < 0 else (text[:at + 1], text[at + 1:])
with open(changelog_path, "w") as f:
    f.write(head.rstrip("\n") + "\n\n" + "\n".join(entry) + "\n" + ("\n" + rest if rest else ""))
print(f"v{version}+{build}")
PY
