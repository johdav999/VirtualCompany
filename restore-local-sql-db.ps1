param(
    [string]$BackupPath = (Join-Path $PSScriptRoot "virtualcompany.bak"),
    [string]$ServerInstance = "localhost\SQLEXPRESS",
    [string]$DatabaseName = "virtualcompany",
    [string]$SqlUser = "sa",
    [string]$SqlPassword = $(if ([string]::IsNullOrWhiteSpace($env:VC_SQL_SA_PASSWORD)) { "YourStrong!Passw0rd" } else { $env:VC_SQL_SA_PASSWORD }),
    [switch]$UseWindowsAuthentication
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $BackupPath))
{
    throw "Backup file was not found: $BackupPath"
}

if (-not (Get-Command Invoke-Sqlcmd -ErrorAction SilentlyContinue))
{
    throw "Invoke-Sqlcmd is not available. Install SQL Server Management tools or the SqlServer PowerShell module."
}

if ($ServerInstance -match "\\(?<instanceName>[^\\]+)$")
{
    $serviceName = "MSSQL`$$($Matches.instanceName)"
    $service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
    if ($null -eq $service)
    {
        throw "SQL Server instance '$ServerInstance' is not installed. Install SQL Server Express, or pass -ServerInstance for an existing SQL Server instance."
    }

    if ($service.Status -ne "Running")
    {
        Start-Service -Name $serviceName
    }
}

$invokeSqlArgs = @{
    ServerInstance = $ServerInstance
}

if (-not $UseWindowsAuthentication)
{
    $invokeSqlArgs.Username = $SqlUser
    $invokeSqlArgs.Password = $SqlPassword
}

$backupFullPath = (Resolve-Path $BackupPath).Path
$backupDirectory = Invoke-Sqlcmd `
    @invokeSqlArgs `
    -Query "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000)) AS BackupPath;" |
    Select-Object -ExpandProperty BackupPath

if ([string]::IsNullOrWhiteSpace($backupDirectory))
{
    $backupDirectory = "C:\Program Files\Microsoft SQL Server\MSSQL17.SQLEXPRESS\MSSQL\Backup\"
}

New-Item -ItemType Directory -Force -Path $backupDirectory | Out-Null
$sqlReadableBackupPath = Join-Path $backupDirectory (Split-Path $backupFullPath -Leaf)

if ($backupFullPath -ne $sqlReadableBackupPath)
{
    Write-Host "Copying backup to SQL Server backup folder '$backupDirectory'..."
    Copy-Item -LiteralPath $backupFullPath -Destination $sqlReadableBackupPath -Force
}

Write-Host "Reading backup metadata from '$sqlReadableBackupPath'..."
$fileList = Invoke-Sqlcmd `
    @invokeSqlArgs `
    -Query "RESTORE FILELISTONLY FROM DISK = N'$sqlReadableBackupPath';"

$dataFile = $fileList | Where-Object { $_.Type -eq "D" } | Select-Object -First 1
$logFile = $fileList | Where-Object { $_.Type -eq "L" } | Select-Object -First 1

if ($null -eq $dataFile -or $null -eq $logFile)
{
    throw "Could not read data and log logical file names from the backup."
}

$dataDirectory = Invoke-Sqlcmd `
    @invokeSqlArgs `
    -Query "SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000)) AS DataPath;" |
    Select-Object -ExpandProperty DataPath

if ([string]::IsNullOrWhiteSpace($dataDirectory))
{
    $dataDirectory = "C:\Program Files\Microsoft SQL Server\MSSQL17.SQLEXPRESS\MSSQL\DATA\"
}

$dataPath = Join-Path $dataDirectory "$DatabaseName.mdf"
$logPath = Join-Path $dataDirectory "${DatabaseName}_log.ldf"

$restoreQuery = @"
IF DB_ID(N'$DatabaseName') IS NOT NULL
BEGIN
    ALTER DATABASE [$DatabaseName] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
END;

RESTORE DATABASE [$DatabaseName]
FROM DISK = N'$sqlReadableBackupPath'
WITH REPLACE,
    MOVE N'$($dataFile.LogicalName)' TO N'$dataPath',
    MOVE N'$($logFile.LogicalName)' TO N'$logPath',
    RECOVERY,
    STATS = 5;

ALTER DATABASE [$DatabaseName] SET MULTI_USER;
"@

Write-Host "Restoring '$DatabaseName' on '$ServerInstance'..."
Invoke-Sqlcmd `
    @invokeSqlArgs `
    -Query $restoreQuery `
    -QueryTimeout 0

Write-Host "Restored '$DatabaseName' on '$ServerInstance'."
