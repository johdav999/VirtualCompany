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

$invokeSqlcmdCommand = Get-Command Invoke-Sqlcmd -ErrorAction SilentlyContinue
$sqlcmdCommand = Get-Command sqlcmd -ErrorAction SilentlyContinue
if ($null -eq $invokeSqlcmdCommand -and $null -eq $sqlcmdCommand)
{
    throw "Neither Invoke-Sqlcmd nor sqlcmd is available. Install SQL Server Management tools or the SqlServer PowerShell module."
}

function Invoke-SqlcmdCli
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$Query,
        [switch]$Delimited,
        [switch]$UnlimitedTimeout
    )

    $arguments = @("-S", $ServerInstance, "-b", "-W", "-h", "-1")
    if ($UseWindowsAuthentication)
    {
        $arguments += "-E"
    }
    else
    {
        $arguments += @("-U", $SqlUser, "-P", $SqlPassword)
    }

    if ($Delimited)
    {
        $arguments += @("-s", "|")
    }

    if ($UnlimitedTimeout)
    {
        $arguments += @("-t", "0")
    }

    $arguments += @("-Q", $Query)
    $output = & $sqlcmdCommand.Source @arguments
    if ($LASTEXITCODE -ne 0)
    {
        throw "sqlcmd failed with exit code $LASTEXITCODE."
    }

    return $output
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
$backupDirectoryQuery = "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(4000)) AS BackupPath;"
$backupDirectory = if ($null -ne $invokeSqlcmdCommand)
{
    Invoke-Sqlcmd @invokeSqlArgs -Query $backupDirectoryQuery | Select-Object -ExpandProperty BackupPath
}
else
{
    (Invoke-SqlcmdCli -Query $backupDirectoryQuery | Select-Object -Last 1).Trim()
}

if ([string]::IsNullOrWhiteSpace($backupDirectory))
{
    $backupDirectory = "C:\Program Files\Microsoft SQL Server\MSSQL17.SQLEXPRESS\MSSQL\Backup\"
}

New-Item -ItemType Directory -Force -Path $backupDirectory | Out-Null
$sqlReadableBackupPath = Join-Path $backupDirectory (Split-Path $backupFullPath -Leaf)

if ($backupFullPath -ne $sqlReadableBackupPath)
{
    Write-Host "Copying backup to SQL Server backup folder '$backupDirectory'..."
    try
    {
        Copy-Item -LiteralPath $backupFullPath -Destination $sqlReadableBackupPath -Force
    }
    catch [System.UnauthorizedAccessException]
    {
        # A non-elevated operator may be unable to write SQL Server's protected backup directory.
        # RESTORE can still use the original absolute path when the SQL Server service account has
        # read access to that one backup file. SQL Server will return an explicit access error if it does not.
        Write-Warning "Could not copy into the protected SQL Server backup folder. Using the original backup path; ensure the SQL Server service account has read access to this file."
        $sqlReadableBackupPath = $backupFullPath
    }
}

Write-Host "Reading backup metadata from '$sqlReadableBackupPath'..."
$fileListQuery = "RESTORE FILELISTONLY FROM DISK = N'$sqlReadableBackupPath';"
$fileList = if ($null -ne $invokeSqlcmdCommand)
{
    Invoke-Sqlcmd @invokeSqlArgs -Query $fileListQuery
}
else
{
    Invoke-SqlcmdCli -Query $fileListQuery -Delimited |
        Where-Object { $_ -match "\|" } |
        ForEach-Object {
            $columns = $_ -split "\|"
            [pscustomobject]@{
                LogicalName = $columns[0].Trim()
                Type = $columns[2].Trim()
            }
        }
}

$dataFile = $fileList | Where-Object { $_.Type -eq "D" } | Select-Object -First 1
$logFile = $fileList | Where-Object { $_.Type -eq "L" } | Select-Object -First 1

if ($null -eq $dataFile -or $null -eq $logFile)
{
    throw "Could not read data and log logical file names from the backup."
}

$dataDirectoryQuery = "SET NOCOUNT ON; SELECT CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(4000)) AS DataPath;"
$dataDirectory = if ($null -ne $invokeSqlcmdCommand)
{
    Invoke-Sqlcmd @invokeSqlArgs -Query $dataDirectoryQuery | Select-Object -ExpandProperty DataPath
}
else
{
    (Invoke-SqlcmdCli -Query $dataDirectoryQuery | Select-Object -Last 1).Trim()
}

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
if ($null -ne $invokeSqlcmdCommand)
{
    Invoke-Sqlcmd @invokeSqlArgs -Query $restoreQuery -QueryTimeout 0
}
else
{
    Invoke-SqlcmdCli -Query $restoreQuery -UnlimitedTimeout | Out-Host
}

Write-Host "Restored '$DatabaseName' on '$ServerInstance'."
Write-Host "Run .\server-local-sql.ps1 to apply pending EF Core migrations and start the API."
