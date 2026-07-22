param(
    [string]$BackupPath = (Join-Path $PSScriptRoot "virtualcompany.bak"),
    [string]$DatabaseName = "virtualcompany",
    [string]$SqlPassword = $(if ([string]::IsNullOrWhiteSpace($env:VC_SQL_SA_PASSWORD)) { "YourStrong!Passw0rd" } else { $env:VC_SQL_SA_PASSWORD })
)

$ErrorActionPreference = "Stop"

$docker = "docker"
if (-not (Get-Command $docker -ErrorAction SilentlyContinue))
{
    $defaultDockerPath = "C:\Program Files\Docker\Docker\resources\bin\docker.exe"
    if (Test-Path $defaultDockerPath)
    {
        $docker = $defaultDockerPath
    }
    else
    {
        throw "Docker CLI is not installed or not available on PATH."
    }
}

if (-not (Test-Path $BackupPath))
{
    throw "Backup file was not found: $BackupPath"
}

$env:VC_SQL_SA_PASSWORD = $SqlPassword
$composeFile = Join-Path $PSScriptRoot "docker-compose.yml"
$containerName = "virtualcompany-sqlserver"
$containerBackupPath = "/var/opt/mssql/backup/virtualcompany.bak"

& $docker info *> $null
if ($LASTEXITCODE -ne 0)
{
    throw "Docker Desktop is not running or this user cannot access the Docker engine. Start Docker Desktop, then sign out/in or restart Windows if access is denied."
}

& $docker compose -f $composeFile up -d sqlserver
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Waiting for SQL Server to accept connections..."
$sqlcmd = "/opt/mssql-tools18/bin/sqlcmd"
$ready = $false
for ($attempt = 1; $attempt -le 60; $attempt++)
{
    & $docker exec $containerName test -x $sqlcmd *> $null
    if ($LASTEXITCODE -ne 0)
    {
        $sqlcmd = "/opt/mssql-tools/bin/sqlcmd"
    }

    & $docker exec $containerName $sqlcmd -S localhost -U sa -P $SqlPassword -C -Q "SELECT 1" *> $null
    if ($LASTEXITCODE -eq 0)
    {
        $ready = $true
        break
    }

    Start-Sleep -Seconds 2
}

if (-not $ready)
{
    throw "SQL Server did not become ready within 120 seconds."
}

& $docker exec $containerName mkdir -p /var/opt/mssql/backup
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Copying backup into the SQL Server container..."
& $docker cp $BackupPath "${containerName}:$containerBackupPath"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$fileListQuery = "RESTORE FILELISTONLY FROM DISK = N'$containerBackupPath';"
$fileList = & $docker exec $containerName $sqlcmd -S localhost -U sa -P $SqlPassword -C -W -h -1 -s "|" -Q $fileListQuery
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$logicalFiles = $fileList |
    Where-Object { $_ -match "\|" } |
    ForEach-Object {
        $columns = $_ -split "\|"
        [pscustomobject]@{
            LogicalName = $columns[0].Trim()
            Type = $columns[2].Trim()
        }
    }

$dataFile = $logicalFiles | Where-Object { $_.Type -eq "D" } | Select-Object -First 1
$logFile = $logicalFiles | Where-Object { $_.Type -eq "L" } | Select-Object -First 1

if ($null -eq $dataFile -or $null -eq $logFile)
{
    throw "Could not read data and log logical file names from the backup."
}

$restoreQuery = @"
IF DB_ID(N'$DatabaseName') IS NOT NULL
BEGIN
    ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
END;

RESTORE DATABASE [$DatabaseName]
FROM DISK = N'$containerBackupPath'
WITH REPLACE,
    MOVE N'$($dataFile.LogicalName)' TO N'/var/opt/mssql/data/$DatabaseName.mdf',
    MOVE N'$($logFile.LogicalName)' TO N'/var/opt/mssql/data/${DatabaseName}_log.ldf',
    RECOVERY,
    STATS = 5;

ALTER DATABASE [$DatabaseName] SET MULTI_USER;
"@

Write-Host "Restoring database '$DatabaseName'..."
& $docker exec $containerName $sqlcmd -S localhost -U sa -P $SqlPassword -C -b -Q $restoreQuery
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Restored '$DatabaseName' in container '$containerName'."
Write-Host "Run .\server.ps1 to apply pending EF Core migrations and start the API."
