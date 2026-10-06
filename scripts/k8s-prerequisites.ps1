param([switch]$DockerDesktop)
$ErrorActionPreference='Stop'
# Check first: existing operators/controllers are left to their owner.
$metrics=kubectl get apiservice v1beta1.metrics.k8s.io --ignore-not-found -o name
if(-not $metrics){
  kubectl apply -f https://github.com/kubernetes-sigs/metrics-server/releases/download/v0.8.0/components.yaml
  if($LASTEXITCODE -ne 0){throw 'Metrics Server install failed'}
}
if($DockerDesktop){
  $deployment=kubectl -n kube-system get deployment metrics-server -o json | ConvertFrom-Json
  if($deployment.spec.template.spec.containers[0].args -notcontains '--kubelet-insecure-tls'){
    $patchFile=Join-Path ([IO.Path]::GetTempPath()) ('lms-metrics-'+[guid]::NewGuid().ToString('N')+'.json')
    '[{"op":"add","path":"/spec/template/spec/containers/0/args/-","value":"--kubelet-insecure-tls"}]' | Set-Content -LiteralPath $patchFile -Encoding ASCII
    try{kubectl -n kube-system patch deployment metrics-server --type=json --patch-file $patchFile;if($LASTEXITCODE -ne 0){throw 'Local Metrics Server TLS patch failed'}}finally{Remove-Item -LiteralPath $patchFile}
  }
}
$keda=kubectl get crd scaledobjects.keda.sh --ignore-not-found -o name
if(-not $keda){kubectl apply --server-side -f https://github.com/kedacore/keda/releases/download/v2.18.1/keda-2.18.1.yaml;if($LASTEXITCODE -ne 0){throw 'KEDA install failed'}}
$classes=kubectl get ingressclass -o json | ConvertFrom-Json
if($classes.items.Count -eq 0){kubectl apply -f https://raw.githubusercontent.com/kubernetes/ingress-nginx/controller-v1.13.3/deploy/static/provider/cloud/deploy.yaml;if($LASTEXITCODE -ne 0){throw 'Ingress install failed'}}
Write-Output 'Prerequisites applied where absent. Check readiness with kubectl get pods -A.'
