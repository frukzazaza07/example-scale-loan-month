#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
test -f .env || cp .env.example .env
args=(compose --env-file .env -f deploy/compose/compose.yaml)
if [[ ${1:-} == --observability ]]; then args+=(--profile observability); fi
docker "${args[@]}" up -d --build
printf '%s\n' 'Dashboard: http://localhost:8080'
