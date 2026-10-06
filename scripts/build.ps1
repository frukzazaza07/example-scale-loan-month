$ErrorActionPreference='Stop';Set-Location (Split-Path $PSScriptRoot -Parent)
$sdk=if(Test-Path .tools/dotnet/dotnet.exe){'.tools/dotnet/dotnet.exe'}else{'dotnet'}
& $sdk build MonthEndLms.slnx -p:RestoreLockedMode=true
if($LASTEXITCODE -ne 0){throw 'Backend build failed'}
Push-Location frontend
try{npm.cmd ci;npm.cmd run lint;npm.cmd run build;if($LASTEXITCODE -ne 0){throw 'Frontend build failed'}}finally{Pop-Location}

