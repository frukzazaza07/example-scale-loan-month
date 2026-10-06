#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet build MonthEndLms.slnx -p:RestoreLockedMode=true
(cd frontend && npm ci && npm run lint && npm run build)

