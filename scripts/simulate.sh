#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
source .env
url=${1:-http://localhost:8080}
cookie=$(mktemp); trap 'rm -f "$cookie"' EXIT
curl --fail --silent --cookie-jar "$cookie" -H 'Content-Type: application/json' -d "{\"role\":\"Admin\",\"password\":\"$ADMIN_PASSWORD\"}" "$url/api/v1/auth/login" >/dev/null
curl --fail --silent --cookie "$cookie" -H 'Content-Type: application/json' -d '{"preset":"Normal","targetRps":5,"durationSeconds":10,"concurrency":20}' "$url/api/v1/runs"
