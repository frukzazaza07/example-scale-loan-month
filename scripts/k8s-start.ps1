param([switch]$Observability,[string]$Context='docker-desktop')
$ErrorActionPreference='Stop';Set-Location (Split-Path $PSScriptRoot -Parent)
function Invoke-Kubectl {
  param([string[]]$Arguments)
  kubectl --context $Context @Arguments
  if($LASTEXITCODE -ne 0){throw "Kubernetes command failed: $($Arguments -join ' ')"}
}
Invoke-Kubectl -Arguments @('apply','-f','deploy/k8s/namespace.yaml')
& "$PSScriptRoot/k8s-secrets.ps1" -Context $Context
Invoke-Kubectl -Arguments @('apply','-k','deploy/k8s')
if($Observability){Invoke-Kubectl -Arguments @('-n','lms-demo','apply','-f','deploy/k8s/observability.yaml')}
# Existing completed jobs are kept; seeding and ordinary startup preserve financial data.
$jobs=Get-Content deploy/k8s/jobs.yaml -Raw | ConvertFrom-Json
foreach($job in $jobs.items){Invoke-Kubectl -Arguments @('-n','lms-demo','wait',"job/$($job.metadata.name)",'--for=condition=complete','--timeout=300s')}
$deploymentNames=@('postgres','rabbitmq','webhook-api','loan-api','loan-worker','notification','simulator','demo-api','frontend')
if($Observability){$deploymentNames+='prometheus'}
foreach($name in $deploymentNames){Invoke-Kubectl -Arguments @('-n','lms-demo','rollout','status',"deployment/$name",'--timeout=300s')}
Invoke-Kubectl -Arguments @('-n','lms-demo','wait','scaledobject/loan-worker','--for=condition=Ready','--timeout=120s')
Write-Host 'Kubernetes ready: http://localhost:30080'
