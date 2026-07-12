$ErrorActionPreference = "Stop"

$sqlPassword = if ([string]::IsNullOrWhiteSpace($env:VC_SQL_SA_PASSWORD)) { "YourStrong!Passw0rd" } else { $env:VC_SQL_SA_PASSWORD }
$currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent().Name
$installerUrl = "https://download.microsoft.com/download/5/1/4/5145fe04-4d30-4b85-b0d1-39533663a2f1/SQL2022-SSEI-Expr.exe"
$downloadRoot = Join-Path $PSScriptRoot ".sqlserver-install"
$bootstrapper = Join-Path $downloadRoot "SQL2022-SSEI-Expr.exe"
$mediaRoot = Join-Path $downloadRoot "media"
$existingSetup = "C:\SQL2022\Developer_ENU\setup.exe"
$setup = if (Test-Path $existingSetup) { $existingSetup } else { Join-Path $mediaRoot "setup.exe" }

if (-not (Test-Path $setup))
{
    New-Item -ItemType Directory -Force -Path $downloadRoot, $mediaRoot | Out-Null

    if (-not (Test-Path $bootstrapper))
    {
        Write-Host "Downloading SQL Server 2022 Express bootstrapper..."
        Invoke-WebRequest -Uri $installerUrl -OutFile $bootstrapper
    }

    Write-Host "Downloading SQL Server 2022 Express installation media..."
    & $bootstrapper /ACTION=Download /MEDIAPATH="$mediaRoot" /MEDIATYPE=Express /QUIET
    if ($LASTEXITCODE -ne 0)
    {
        throw "SQL Server Express media download failed before setup.exe was available. Exit code: $LASTEXITCODE"
    }

    $downloadedSetup = Get-ChildItem $mediaRoot -Recurse -Filter setup.exe | Select-Object -First 1 -ExpandProperty FullName
    if (-not [string]::IsNullOrWhiteSpace($downloadedSetup))
    {
        $setup = $downloadedSetup
    }
}

if (-not (Test-Path $setup))
{
    throw "Could not find setup.exe in downloaded SQL Server media: $mediaRoot"
}

Write-Host "Using SQL Server setup media: $setup"
Write-Host "Installing SQL Server instance localhost\VCLOCAL..."
& $setup `
    /Q `
    /ACTION=Install `
    /FEATURES=SQLENGINE `
    /INSTANCENAME=VCLOCAL `
    /SECURITYMODE=SQL `
    /SAPWD="$sqlPassword" `
    /SQLSYSADMINACCOUNTS="$currentUser" `
    /TCPENABLED=1 `
    /IACCEPTSQLSERVERLICENSETERMS

if ($LASTEXITCODE -ne 0)
{
    throw "SQL Server Express setup failed. Exit code: $LASTEXITCODE"
}

$service = Get-Service -Name 'MSSQL$VCLOCAL' -ErrorAction SilentlyContinue
if ($null -eq $service)
{
    throw "SQL Server installer completed, but service 'MSSQL`$VCLOCAL' was not found."
}

if ($service.Status -ne "Running")
{
    Start-Service -Name 'MSSQL$VCLOCAL'
}

Write-Host "Installed SQL Server Express instance localhost\VCLOCAL."
Write-Host "Next run: .\restore-local-sql-db.ps1"
