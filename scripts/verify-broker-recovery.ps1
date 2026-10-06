param([string]$Url='http://localhost:8080')
$ErrorActionPreference='Stop';Set-Location (Split-Path $PSScriptRoot -Parent)
Get-Content .env | ForEach-Object {if($_ -match '^([A-Z_]+)=(.*)$'){[Environment]::SetEnvironmentVariable($matches[1],$matches[2],'Process')}}
$session=New-Object Microsoft.PowerShell.Commands.WebRequestSession
Invoke-RestMethod "$Url/api/v1/auth/login" -Method Post -Body (@{role='Admin';password=$env:ADMIN_PASSWORD}|ConvertTo-Json) -ContentType application/json -WebSession $session | Out-Null
$id='broker-recovery-'+[guid]::NewGuid().ToString('N')
$body=@{eventId='evt-'+$id;bankTransactionId=$id;paymentReference='LOAN-1900';amountMinor='900000';currency='THB';status='SUCCEEDED';paidAt='2026-10-31T16:59:00+07:00'}|ConvertTo-Json -Compress
try{
  docker compose --env-file .env -f deploy/compose/compose.yaml stop rabbitmq
  if($LASTEXITCODE -ne 0){throw 'Could not stop the demo broker'}
  $stamp=[DateTimeOffset]::UtcNow.ToUnixTimeSeconds().ToString()
  $hmac=New-Object System.Security.Cryptography.HMACSHA256
  $hmac.Key=[Text.Encoding]::UTF8.GetBytes($env:BANK_SECRET)
  $signature=([BitConverter]::ToString($hmac.ComputeHash([Text.Encoding]::UTF8.GetBytes($stamp+'.'+$body)))).Replace('-','')
  $receipt=Invoke-RestMethod 'http://localhost:5101/webhooks/bank/payments' -Method Post -Body $body -ContentType application/json -Headers @{'X-Bank-Timestamp'=$stamp;'X-Bank-Signature'=$signature}
  if($receipt.status -ne 'Received'){throw 'Webhook was not durably accepted'}
  $pending=Invoke-RestMethod "$Url/api/v1/receipts?transactionId=$id" -WebSession $session
  if($pending.total -ne 1 -or $pending.items[0].outbox.publishedAt){throw 'Expected one receipt with a pending outbox during broker outage'}
}finally{docker compose --env-file .env -f deploy/compose/compose.yaml start rabbitmq}
$payment=$null
for($i=0;$i -lt 60;$i++){
  try{$payment=Invoke-RestMethod "$Url/api/v1/payments/$id" -WebSession $session}catch{$payment=$null}
  if($payment.payment.status -in @('Applied','AppliedWithCredit')){break}
  Start-Sleep -Seconds 1
}
if(!$payment -or $payment.journals.Count -ne 1){throw 'Broker recovery did not produce exactly one payment journal'}
$result=@{bankTransactionId=$id;durableReceiptDuringOutage=$true;publishedAfterRecovery=$true;applicationStatus=$payment.payment.status;paymentJournalCount=$payment.journals.Count;checkedAt=[DateTimeOffset]::UtcNow.ToString('O')}
New-Item -ItemType Directory -Force artifacts | Out-Null
$result|ConvertTo-Json|Set-Content -LiteralPath artifacts/broker-recovery.json
$result|ConvertTo-Json
