param([int]$Samples=72,[int]$IntervalSeconds=5,[string]$Url='http://localhost:30080',[switch]$StartSpike)
$ErrorActionPreference='Stop';Set-Location (Split-Path $PSScriptRoot -Parent)
$password=(Get-Content .env | Where-Object {$_ -like 'ADMIN_PASSWORD=*'}) -replace '^ADMIN_PASSWORD=',''
$session=New-Object Microsoft.PowerShell.Commands.WebRequestSession
Invoke-RestMethod "$Url/api/v1/auth/login" -Method Post -Body (@{role='Admin';password=$password}|ConvertTo-Json) -ContentType application/json -WebSession $session | Out-Null
$runId=$null
if($StartSpike){$run=Invoke-RestMethod "$Url/api/v1/runs" -Method Post -Body '{"preset":"Month-End Spike","targetRps":100,"durationSeconds":120,"concurrency":100}' -ContentType application/json -WebSession $session;$runId=$run.id}
New-Item -ItemType Directory -Force artifacts | Out-Null
$filename=Join-Path $PWD ('artifacts/scaling-'+[DateTimeOffset]::UtcNow.ToString('yyyyMMdd-HHmmss')+'.jsonl')
for($i=0;$i -lt $Samples;$i++){
  try{
    $metrics=Invoke-RestMethod "$Url/api/v1/metrics" -WebSession $session -TimeoutSec 30
    $worker=@($metrics.cluster.deployments | Where-Object {$_.name -eq 'loan-worker'})[0]
    $receiver=@($metrics.cluster.deployments | Where-Object {$_.name -eq 'webhook-api'})[0]
    $sample=@{at=[DateTimeOffset]::UtcNow.ToString('O');runId=$runId;queueReady=$metrics.queue.ready;queueInFlight=$metrics.queue.unacknowledged;workerDesired=$worker.replicas;workerReady=$worker.status.readyReplicas;receiverDesired=$receiver.replicas;receiverReady=$receiver.status.readyReplicas}
    $sample|ConvertTo-Json -Compress|Add-Content -LiteralPath $filename
    $sample|ConvertTo-Json -Compress
  }catch{Write-Output ('Telemetry unavailable: '+$_.Exception.Message)}
  Start-Sleep -Seconds $IntervalSeconds
}
if($runId){Invoke-RestMethod "$Url/api/v1/runs/$runId" -WebSession $session|ConvertTo-Json -Depth 7|Set-Content -LiteralPath artifacts/spike-results.json}
Write-Output "Recorded $filename"
