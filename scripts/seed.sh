#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
docker compose --env-file .env -f deploy/compose/compose.yaml run --rm -e "SEED_COUNT=${1:-1000}" seed
