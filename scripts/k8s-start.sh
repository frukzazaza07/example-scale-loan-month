#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
kubectl apply -f deploy/k8s/namespace.yaml
bash scripts/k8s-secrets.sh
kubectl apply -k deploy/k8s
printf '%s\n' 'Dashboard: http://localhost:30080 or http://lms.localhost'
