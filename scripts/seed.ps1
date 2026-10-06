param([int]$Count=1000)
Set-Location (Split-Path $PSScriptRoot -Parent)
docker compose --env-file .env -f deploy/compose/compose.yaml run --rm -e "SEED_COUNT=$Count" seed
if($LASTEXITCODE -ne 0){throw 'Seeding failed'}
