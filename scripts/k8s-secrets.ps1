param([string]$Context='docker-desktop')
$ErrorActionPreference='Stop'
Set-Location (Split-Path $PSScriptRoot -Parent)
if(!(Test-Path .env)){throw 'Copy .env.example to .env first.'}
$values=@{}
Get-Content .env | ForEach-Object {if($_ -match '^([A-Z_]+)=(.*)$'){$values[$matches[1]]=$matches[2]}}
$values['RABBITMQ_URL']='amqp://'+[uri]::EscapeDataString($values['RABBITMQ_USER'])+':'+[uri]::EscapeDataString($values['RABBITMQ_PASSWORD'])+'@rabbitmq.lms-demo.svc.cluster.local:5672'
foreach($name in @('webhook','loan','notification','simulator')){$values['DB_URL_'+$name.ToUpper()]="Host=postgres;Database=$name;Username=$name;Password=$($values[$name.ToUpper()+'_DB_PASSWORD']);Maximum Pool Size=5"}
$values['SEED_CONNECTION']='postgresql://loan:'+ [uri]::EscapeDataString($values['LOAN_DB_PASSWORD'])+'@postgres:5432/loan'
$secret=@{apiVersion='v1';kind='Secret';metadata=@{name='lms-secrets';namespace='lms-demo'};type='Opaque';stringData=$values}
$secret | ConvertTo-Json -Depth 5 | kubectl --context $Context apply -f -
if($LASTEXITCODE -ne 0){throw 'Kubernetes secret creation failed.'}


