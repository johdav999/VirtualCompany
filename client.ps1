param([int]$Port = 5062)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $repoRoot "local-build-common.ps1")
$projectPath = Join-Path $repoRoot "src\VirtualCompany.Web\VirtualCompany.Web.csproj"
$projectDirectory = Split-Path -Parent $projectPath
$configuration = Get-VcLocalConfiguration
$stateDirectory = Join-Path $repoRoot ".codex-build"
$buildOutputRoot = Join-Path $stateDirectory "build-output\web"
$buildDirectory = Join-Path $buildOutputRoot "net9.0"
$stateFile = Join-Path $stateDirectory "virtualcompany-web.json"
$runId = [DateTime]::UtcNow.ToString("yyyyMMddHHmmssfff")
$runDirectory = Join-Path $stateDirectory "web-runs\$runId"
$webDll = Join-Path $runDirectory "VirtualCompany.Web.dll"

New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
$buildLock = Enter-VcBuildLock -StateDirectory $stateDirectory -Operation "Web client build"
try
{
    Stop-VcProcessesRunningFromBuildDirectory `
        -BuildDirectory $buildDirectory `
        -EntryAssembly "VirtualCompany.Web.dll"

    $env:MSBuildEnableWorkloadResolver = "false"
    $buildTimer = [System.Diagnostics.Stopwatch]::StartNew()
    dotnet build $projectPath -c $configuration --no-restore --disable-build-servers `
        "-p:BuildInParallel=false" `
        "-p:VCStartupProject=VirtualCompany.Web" `
        "-p:VCStartupOutputPath=$buildOutputRoot" `
        -v minimal
    $buildExitCode = $LASTEXITCODE
    $buildTimer.Stop()
    Write-Host ("Web build completed in {0:N1} seconds." -f $buildTimer.Elapsed.TotalSeconds)
    if ($buildExitCode -ne 0)
    {
        exit $buildExitCode
    }

    Copy-VcBuildSnapshot `
        -BuildDirectory $buildDirectory `
        -RunDirectory $runDirectory `
        -EntryAssembly "VirtualCompany.Web.dll"
}
finally
{
    if ($null -ne $buildLock)
    {
        $buildLock.Dispose()
    }
}

if (Test-Path -LiteralPath $stateFile)
{
    try
    {
        $state = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
        $tracked = Get-Process -Id ([int]$state.ProcessId) -ErrorAction SilentlyContinue
        if ($null -ne $tracked -and
            $tracked.ProcessName -in @("dotnet", "VirtualCompany.Web") -and
            $tracked.StartTime.ToUniversalTime().ToString("O") -eq [string]$state.StartedUtc)
        {
            Stop-Process -Id $tracked.Id -Force -ErrorAction Stop
            $tracked.WaitForExit(5000) | Out-Null
        }
    }
    finally
    {
        Remove-Item -LiteralPath $stateFile -Force -ErrorAction SilentlyContinue
    }
}

foreach ($processId in @(Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue |
    Select-Object -ExpandProperty OwningProcess -Unique))
{
    $listener = Get-Process -Id $processId -ErrorAction SilentlyContinue
    if ($null -ne $listener -and $listener.ProcessName -in @("dotnet", "VirtualCompany.Web"))
    {
        Stop-Process -Id $listener.Id -Force -ErrorAction Stop
        $listener.WaitForExit(5000) | Out-Null
    }
}

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://localhost:$Port"
$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = "dotnet"
$startInfo.Arguments = "`"$webDll`""
$startInfo.WorkingDirectory = $projectDirectory
$startInfo.UseShellExecute = $false
$webProcess = [System.Diagnostics.Process]::new()
$webProcess.StartInfo = $startInfo
if (-not $webProcess.Start()) { throw "The Virtual Company Web process could not be started." }

Remove-VcOldRunSnapshots `
    -RunsDirectory (Join-Path $stateDirectory "web-runs") `
    -ProtectedDirectory $runDirectory

@{ ProcessId = $webProcess.Id; StartedUtc = $webProcess.StartTime.ToUniversalTime().ToString("O") } |
    ConvertTo-Json | Set-Content -LiteralPath $stateFile

try
{
    $webProcess.WaitForExit()
    exit $webProcess.ExitCode
}
finally
{
    $webProcess.Refresh()
    if (-not $webProcess.HasExited)
    {
        Stop-Process -Id $webProcess.Id -Force -ErrorAction SilentlyContinue
        $webProcess.WaitForExit(5000) | Out-Null
    }
    Remove-Item -LiteralPath $stateFile -Force -ErrorAction SilentlyContinue
}
