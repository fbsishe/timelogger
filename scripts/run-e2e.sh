#!/usr/bin/env bash
# Runs the Playwright E2E suite (TL-62/TL-63) end to end:
# SQL Server via docker compose → build → chromium install (by the fixture) → tests.
#
#   ./scripts/run-e2e.sh            # run and leave SQL running for fast re-runs
#   ./scripts/run-e2e.sh --clean    # also stop SQL and delete its volume afterwards
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

echo "── Starting SQL Server (docker compose) ──"
docker compose -f docker-compose.e2e.yml up -d --wait

cleanup() {
  if [[ "${1:-}" == "--clean" ]]; then
    echo "── Stopping SQL Server and removing volume ──"
    docker compose -f docker-compose.e2e.yml down -v
  fi
}
trap 'cleanup "${1:-}"' EXIT

echo "── Building app and E2E project ──"
dotnet build TimeLogger.sln -v quiet
dotnet build tests/TimeLogger.E2E -v quiet

echo "── Running E2E tests ──"
dotnet test tests/TimeLogger.E2E --no-build
