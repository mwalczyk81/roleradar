#!/usr/bin/env bash
# Runs the web gate on Linux in a node:22 container, the way the Linux CI image does.
#
# Windows runs alone are not enough before a web phase boundary: the Linux image is stricter (case-sensitive paths, a
# newer Node than some local installs, a fresh Chromium and a frozen lockfile install).
#
# Run it from Git Bash, from anywhere:   bash scripts/linux-web-check.sh
# Needs Docker. It never starts the compose stack and never touches Mongo or any volume.
#
# What it does:
#   - mounts the repo READ-ONLY at /src (nothing in your working tree is written to);
#   - copies the project onto the container's own filesystem (excluding node_modules, web/build, web/coverage,
#     web/test-results, .git, src, tests, dist, TestResults, bin, obj, deploy/data, mongodump_bin), because vitest
#     workers time out when they run from the Windows bind mount;
#   - installs the pnpm version named by "packageManager" in the root package.json, then
#     pnpm install --frozen-lockfile, playwright install --with-deps chromium;
#   - runs typecheck, lint, test:coverage, build and test:e2e and prints the summary lines of each.
# The exit code is non-zero when any step failed.
#
# The docker command it runs for you (MSYS_NO_PATHCONV stops Git Bash rewriting the /src path):
#   MSYS_NO_PATHCONV=1 docker run --rm -v "<repo>:/src:ro" -e ROLERADAR_LINUX_WEB_CHECK_INNER=1 node:22 \
#     bash /src/scripts/linux-web-check.sh
set -uo pipefail

if [ -z "${ROLERADAR_LINUX_WEB_CHECK_INNER:-}" ]; then
  # ---- host side: start the container -----------------------------------------------------------------------------
  script_dir="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
  repo_root="$(cd "$script_dir/.." && pwd)"
  # Git Bash: pwd -W gives the Windows form (C:/...), which Docker Desktop needs for a bind mount.
  mount_root="$(cd "$repo_root" && (pwd -W 2>/dev/null || pwd))"
  exec env MSYS_NO_PATHCONV=1 docker run --rm \
    -v "${mount_root}:/src:ro" \
    -e ROLERADAR_LINUX_WEB_CHECK_INNER=1 \
    "${ROLERADAR_LINUX_WEB_CHECK_IMAGE:-node:22}" \
    bash /src/scripts/linux-web-check.sh
fi

# ---- container side ---------------------------------------------------------------------------------------------------
failed=0
log="$(mktemp)"

# step <name> <summary-filter> <command...>: run quietly, show only the matching summary lines, keep the tail on failure.
step() {
  local name="$1" filter="$2"
  shift 2
  echo "== $name"
  "$@" >"$log" 2>&1
  local code=$?
  grep -E "$filter" "$log" || true
  if [ "$code" -ne 0 ]; then
    echo "FAILED: $name (exit $code)"
    tail -n 40 "$log"
    failed=1
  fi
}

package_manager="$(node -p "require('/src/package.json').packageManager" 2>/dev/null || true)"
if ! printf '%s' "$package_manager" | grep -Eq '^pnpm@[0-9]+\.[0-9]+\.[0-9]+$'; then
  echo "Cannot read a pnpm@x.y.z version from the packageManager field of package.json (got: '$package_manager')."
  exit 2
fi

mkdir -p /work
(cd /src && tar cf - \
  --exclude=node_modules --exclude=./web/build --exclude=./web/coverage --exclude=./web/test-results \
  --exclude=./.git --exclude=./src --exclude=./tests --exclude=./dist --exclude=./TestResults \
  --exclude=bin --exclude=obj --exclude=./deploy/data --exclude=./mongodump_bin .) | tar xf - -C /work
cd /work || exit 2

# corepack has failed to verify pnpm signatures on this image; npm install is reliable.
step "install pnpm (${package_manager})" "added|changed|ERR" npm install -g "$package_manager"
step "pnpm install --frozen-lockfile" "Done in|ERR|error" pnpm install --frozen-lockfile
step "playwright install chromium" "ERR|error|Failed" pnpm --filter web exec playwright install --with-deps chromium

step "typecheck" "error TS|ERR" pnpm --filter web run typecheck
step "lint" "problem|error" pnpm --filter web run lint
step "test:coverage" "Test Files|Tests |All files|FAIL|×" pnpm --filter web run test:coverage
step "build" "built in|rror" pnpm --filter web run build
step "e2e" "passed|failed|skipped|flaky" pnpm --filter web run test:e2e

if [ "$failed" -eq 0 ]; then
  echo "LINUX WEB CHECK: PASS"
else
  echo "LINUX WEB CHECK: FAIL"
fi
exit "$failed"
