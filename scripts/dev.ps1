param([switch]$Migrate,[switch]$Stop)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot -Parent
Set-Location $root
$sdk=Join-Path $root '.tools/dotnet/dotnet.exe'
if(!(Test-Path $sdk)){$sdk='dotnet'}
$env:DOTNET_ROOT=Join-Path $root '.tools/dotnet'
$env:DOTNET_CLI_HOME=Join-Path $root '.tools/cli'
$env:NUGET_PACKAGES=Join-Path $root '.tools/packages'
$outputDir=Join-Path $root 'artifacts/dev'
New-Item -ItemType Directory -Force -Path $outputDir | Out-Null
$pidFile=Join-Path $outputDir 'processes.json'
if($Stop){
  if(Test-Path $pidFile){$recorded=Get-Content $pidFile -Raw | ConvertFrom-Json; foreach($entry in $recorded){ $process=Get-Process -Id $entry.pid -ErrorAction SilentlyContinue; if($process -and $process.StartTime.ToUniversalTime().ToString('O') -eq $entry.started){Stop-Process -Id $entry.pid} };Remove-Item -LiteralPath $pidFile}
  return
}
if(Test-Path $pidFile){throw 'A local dev session is recorded. Run scripts/dev.ps1 -Stop first.'}
if(!(Test-Path .env)){Copy-Item -LiteralPath .env.example -Destination .env}
Get-Content .env | ForEach-Object {if($_ -match '^([A-Z_]+)=(.*)$'){[Environment]::SetEnvironmentVariable($matches[1],$matches[2],'Process')}}
$env:Logging__LogLevel__Microsoft='Warning'
$env:RABBITMQ_URL="amqp://$($env:RABBITMQ_USER):$($env:RABBITMQ_PASSWORD)@localhost:5672"
$env:WEBHOOK_URL='http://localhost:5101';$env:LOAN_URL='http://localhost:5102';$env:NOTIFICATION_URL='http://localhost:5103';$env:SIMULATOR_URL='http://localhost:5104';$env:RABBITMQ_MANAGEMENT_URL='http://localhost:15672';$env:RUNTIME_MODE='Compose'
$services=@(@{name='WebhookApi';db='webhook';port=5101},@{name='LoanService';db='loan';port=5102},@{name='NotificationService';db='notification';port=5103},@{name='BankSimulator';db='simulator';port=5104},@{name='DemoApi';db='';port=5100})
$processes=@()
foreach($service in $services){
  $password=[Environment]::GetEnvironmentVariable($service.db.ToUpper()+'_DB_PASSWORD')
  $env:DATABASE_URL="Host=localhost;Database=$($service.db);Username=$($service.db);Password=$password;Maximum Pool Size=5"
  $env:ASPNETCORE_URLS="http://localhost:$($service.port)";$env:SERVICE_MODE='all'
  $dll=Join-Path $root "backend/$($service.name)/bin/Debug/net10.0/$($service.name).dll"
  if($Migrate -and $service.db){& $sdk $dll --migrate; if($LASTEXITCODE -ne 0){throw 'Migration failed'};if($service.name -eq 'LoanService'){& $sdk $dll --seed;if($LASTEXITCODE -ne 0){throw 'Seed failed'}}}
  $process=Start-Process -FilePath $sdk -ArgumentList @($dll) -WorkingDirectory $root -WindowStyle Hidden -RedirectStandardOutput (Join-Path $outputDir "$($service.name).log") -RedirectStandardError (Join-Path $outputDir "$($service.name).err") -PassThru
  $processes+=@{pid=$process.Id;name=$service.name;started=$process.StartTime.ToUniversalTime().ToString('O')}
}
$processes | ConvertTo-Json | Set-Content -LiteralPath $pidFile
Write-Output 'Services started. API: http://localhost:5100. Run npm.cmd run dev in frontend.'

