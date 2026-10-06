param([switch]$Observability,[switch]$ComposeOnly)
$ErrorActionPreference='Stop';Set-Location (Split-Path $PSScriptRoot -Parent)
if(-not $ComposeOnly){
  Get-Command kubectl -ErrorAction Stop | Out-Null
  kubectl --context docker-desktop get nodes
  if($LASTEXITCODE -ne 0){throw 'Docker Desktop Kubernetes must already be running.'}
}
if(!(Test-Path .env)){Copy-Item -LiteralPath .env.example -Destination .env}
$composeArgs=@('compose','--env-file','.env','-f','deploy/compose/compose.yaml')
if($Observability){$composeArgs+=@('--profile','observability')}
Write-Host 'Starting Docker Compose...'
# Docker build progress uses stderr. Check its exit code explicitly on PowerShell 5.1.
$previousPreference=$ErrorActionPreference
$ErrorActionPreference='Continue'
try{
  & docker @composeArgs up -d --build --wait 2>&1 | ForEach-Object {Write-Host $_.ToString()}
  $composeExitCode=$LASTEXITCODE
}finally{$ErrorActionPreference=$previousPreference}
if($composeExitCode -ne 0){throw 'Compose startup failed.'}
if(-not $ComposeOnly){
  Write-Host 'Starting Kubernetes pods...'
  & "$PSScriptRoot/k8s-start.ps1" -Observability:$Observability -Context docker-desktop
}
Write-Host 'Compose dashboard: http://localhost:8080'
if(-not $ComposeOnly){Write-Host 'Kubernetes dashboard: http://localhost:30080'}
if($Observability){Write-Host 'Grafana: http://localhost:3000';Write-Host 'Prometheus (Compose): http://localhost:9090';Write-Host 'Jaeger: http://localhost:16686'}
