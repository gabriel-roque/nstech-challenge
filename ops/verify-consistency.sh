#!/usr/bin/env sh
set -eu

SCRIPT_DIR=$(CDPATH= cd -- "$(dirname -- "$0")" && pwd)
REPOSITORY_DIR=$(CDPATH= cd -- "$SCRIPT_DIR/.." && pwd)
GATLING_DIR="$REPOSITORY_DIR/tests/gatling"

if ! docker compose --project-directory "$REPOSITORY_DIR" --file "$REPOSITORY_DIR/compose.yml" ps --status running postgres api-1 api-2 nginx >/dev/null; then
  echo "The Compose stack is not running. Start it first:" >&2
  echo "  docker compose up --build --detach" >&2
  exit 1
fi

run_with_installed_node=false
if command -v node >/dev/null 2>&1 && command -v npm >/dev/null 2>&1; then
  node_major=$(node -p "Number(process.versions.node.split('.')[0])")
  npm_major=$(npm --version | cut -d. -f1)
  if [ "$node_major" -ge 24 ] && [ "$npm_major" -ge 11 ]; then
    run_with_installed_node=true
  fi
fi

cd "$GATLING_DIR"

if [ "${CONSISTENCY_SKIP_INSTALL:-0}" != "1" ]; then
  if [ "$run_with_installed_node" = true ]; then
    npm ci
  else
    npx --yes --package node@24 --package npm@11 npm ci
  fi
fi

if [ "$run_with_installed_node" = true ]; then
  exec npm run consistency
fi

exec npx --yes --package node@24 --package npm@11 npm run consistency
