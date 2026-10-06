#!/usr/bin/env bash
set -euo pipefail
if ! kubectl get apiservice v1beta1.metrics.k8s.io >/dev/null 2>&1; then
  kubectl apply -f https://github.com/kubernetes-sigs/metrics-server/releases/download/v0.8.0/components.yaml
  if [[ ${1:-} == --docker-desktop ]]; then
    kubectl -n kube-system patch deployment metrics-server --type=json -p '[{"op":"add","path":"/spec/template/spec/containers/0/args/-","value":"--kubelet-insecure-tls"}]'
  fi
fi
if ! kubectl get crd scaledobjects.keda.sh >/dev/null 2>&1; then
  kubectl apply --server-side -f https://github.com/kedacore/keda/releases/download/v2.18.1/keda-2.18.1.yaml
fi
if [[ -z $(kubectl get ingressclass -o name) ]]; then
  kubectl apply -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/controller-v1.13.3/deploy/static/provider/cloud/deploy.yaml
fi
