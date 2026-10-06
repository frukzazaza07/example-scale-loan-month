param([string]$Url='http://localhost:8080',[string]$Preset='Normal',[int]$Rps=5,[int]$Seconds=10,[string]$LoanReference='')
$ErrorActionPreference='Stop';Set-Location (Split-Path $PSScriptRoot -Parent)
$password=(Get-Content .env | Where-Object {$_ -like 'ADMIN_PASSWORD=*'}) -replace '^ADMIN_PASSWORD=',''
$session=New-Object Microsoft.PowerShell.Commands.WebRequestSession
Invoke-RestMethod "$Url/api/v1/auth/login" -Method Post -Body (@{role='Admin';password=$password}|ConvertTo-Json) -ContentType application/json -WebSession $session | Out-Null
$config=@{preset=$Preset;targetRps=$Rps;durationSeconds=$Seconds;concurrency=100;amountMinor='900000'}
if($LoanReference){$config.loanReference=$LoanReference}
$run=Invoke-RestMethod "$Url/api/v1/runs" -Method Post -Body ($config|ConvertTo-Json) -ContentType application/json -WebSession $session
$run | ConvertTo-Json
