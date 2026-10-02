[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [string]$BackupFile
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($env:VANTA_RESTORE_DATABASE_URL)) {
    throw "VANTA_RESTORE_DATABASE_URL is required and must target an isolated restore database."
}

if (-not (Test-Path -LiteralPath $BackupFile -PathType Leaf)) {
    throw "Backup file does not exist: $BackupFile"
}

if ($PSCmdlet.ShouldProcess($env:VANTA_RESTORE_DATABASE_URL, "restore PostgreSQL backup into isolated database")) {
    pg_restore --clean --if-exists --no-owner --no-acl --dbname=$env:VANTA_RESTORE_DATABASE_URL $BackupFile
    if ($LASTEXITCODE -ne 0) {
        throw "pg_restore failed with exit code $LASTEXITCODE."
    }
}
