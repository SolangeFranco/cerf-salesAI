[CmdletBinding()]
param(
    [string]$OutputDirectory = ".backups"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($env:VANTA_DATABASE_URL)) {
    throw "VANTA_DATABASE_URL is required and must be supplied by the secret manager."
}

New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$timestamp = Get-Date -AsUTC -Format "yyyyMMdd-HHmmss"
$outputPath = Join-Path $OutputDirectory "vanta-$timestamp.dump"

pg_dump --format=custom --no-owner --no-acl --dbname=$env:VANTA_DATABASE_URL --file=$outputPath
if ($LASTEXITCODE -ne 0) {
    throw "pg_dump failed with exit code $LASTEXITCODE."
}

Write-Output $outputPath
