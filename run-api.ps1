param(
    [Parameter(Mandatory = $true)]
    [string]$ConnectionString,

    [int]$Port = 5301
)

$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "local-build-common.ps1")
$stateDirectory = Join-Path $PSScriptRoot ".codex-build"
$stateFile = Join-Path $stateDirectory "virtualcompany-api.json"
$projectPath = Join-Path $PSScriptRoot "src\VirtualCompany.Api\VirtualCompany.Api.csproj"
$projectDirectory = Split-Path -Parent $projectPath
$configuration = Get-VcLocalConfiguration
$buildOutputRoot = Join-Path $stateDirectory "build-output\api"
$buildDirectory = Join-Path $buildOutputRoot "net9.0"
$runId = [DateTime]::UtcNow.ToString("yyyyMMddHHmmssfff")
$runDirectory = Join-Path $stateDirectory "api-runs\$runId"
$apiDll = Join-Path $runDirectory "VirtualCompany.Api.dll"
$standardOutputLog = Join-Path $runDirectory "api.stdout.log"
$standardErrorLog = Join-Path $runDirectory "api.stderr.log"

function Stop-TrackedApiProcess
{
    if (-not (Test-Path -LiteralPath $stateFile))
    {
        return
    }

    try
    {
        $state = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
        $process = Get-Process -Id ([int]$state.ProcessId) -ErrorAction SilentlyContinue
        if ($null -ne $process -and
            ($process.ProcessName -eq "dotnet" -or $process.ProcessName -eq "VirtualCompany.Api") -and
            $process.StartTime.ToUniversalTime().ToString("O") -eq [string]$state.StartedUtc)
        {
            Stop-Process -Id $process.Id -Force -ErrorAction Stop
            $process.WaitForExit(5000) | Out-Null
        }
    }
    finally
    {
        Remove-Item -LiteralPath $stateFile -Force -ErrorAction SilentlyContinue
    }
}

function Stop-ApiPortListener
{
    $listeners = Get-NetTCPConnection -LocalPort $Port -State Listen -ErrorAction SilentlyContinue
    foreach ($processId in @($listeners | Select-Object -ExpandProperty OwningProcess -Unique))
    {
        $process = Get-Process -Id $processId -ErrorAction SilentlyContinue
        if ($null -ne $process -and
            ($process.ProcessName -eq "dotnet" -or $process.ProcessName -eq "VirtualCompany.Api"))
        {
            Stop-Process -Id $process.Id -Force -ErrorAction Stop
            $process.WaitForExit(5000) | Out-Null
        }
    }
}

function Stop-OrphanedRepoApiProcesses
{
    $normalizedRoot = [System.IO.Path]::GetFullPath($PSScriptRoot).TrimEnd('\')
    $apiDllPath = [System.IO.Path]::GetFullPath($apiDll)
    $apiExecutablePath = [System.IO.Path]::ChangeExtension($apiDllPath, ".exe")
    $normalizedRootLower = $normalizedRoot.ToLowerInvariant()
    $apiDllPathLower = $apiDllPath.ToLowerInvariant()
    $apiExecutablePathLower = $apiExecutablePath.ToLowerInvariant()
    $candidates = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            $commandLine = [string]$_.CommandLine
            $commandLineLower = $commandLine.ToLowerInvariant()
            $_.ProcessId -ne $PID -and
            ($_.Name -eq "dotnet.exe" -or $_.Name -eq "VirtualCompany.Api.exe") -and
            -not [string]::IsNullOrWhiteSpace($commandLine) -and
            ($commandLineLower.Contains($apiDllPathLower) -or
             $commandLineLower.Contains($apiExecutablePathLower) -or
             ($commandLineLower.Contains($normalizedRootLower) -and
              $commandLineLower.Contains("virtualcompany.api")))
        }

    foreach ($candidate in @($candidates))
    {
        $process = Get-Process -Id ([int]$candidate.ProcessId) -ErrorAction SilentlyContinue
        if ($null -eq $process)
        {
            continue
        }

        Write-Host "Stopping previous VirtualCompany API process $($process.Id)..."
        Stop-Process -Id $process.Id -Force -ErrorAction Stop
        $process.WaitForExit(10000) | Out-Null
    }
}

New-Item -ItemType Directory -Path $stateDirectory -Force | Out-Null
$buildLock = Enter-VcBuildLock -StateDirectory $stateDirectory -Operation "API server build"
try
{
    # Older launchers and manual commands may have run directly from the stable
    # build directory. Stop only those legacy processes before overwriting it;
    # the current timestamped runtime snapshot remains available during build.
    Stop-VcProcessesRunningFromBuildDirectory `
        -BuildDirectory $buildDirectory `
        -EntryAssembly "VirtualCompany.Api.dll"

    $env:MSBuildEnableWorkloadResolver = "false"
    $buildTimer = [System.Diagnostics.Stopwatch]::StartNew()
    dotnet build $projectPath -c $configuration --no-restore --disable-build-servers `
        "-p:BuildInParallel=false" `
        "-p:VCStartupProject=VirtualCompany.Api" `
        "-p:VCStartupOutputPath=$buildOutputRoot" `
        -v minimal
    $buildExitCode = $LASTEXITCODE
    $buildTimer.Stop()
    Write-Host ("API build completed in {0:N1} seconds." -f $buildTimer.Elapsed.TotalSeconds)
    if ($buildExitCode -ne 0)
    {
        exit $buildExitCode
    }

    # Run from a snapshot so later incremental builds never write to locked assemblies.
    Copy-VcBuildSnapshot `
        -BuildDirectory $buildDirectory `
        -RunDirectory $runDirectory `
        -EntryAssembly "VirtualCompany.Api.dll"
}
finally
{
    if ($null -ne $buildLock)
    {
        $buildLock.Dispose()
    }
}

# Keep the current API available while the replacement is compiling.
Stop-TrackedApiProcess
Stop-ApiPortListener
Stop-OrphanedRepoApiProcesses

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://localhost:$Port"
$env:ConnectionStrings__VirtualCompanyDb = $ConnectionString

# PowerShell terminals keep the environment they inherited when they were opened.
# Refresh the guided-dialogue development switches from the user's persisted
# environment so restarting this launcher does not silently revert voice to the
# disabled appsettings defaults.
foreach ($variableName in @(
    "GuidedDialogue__Enabled",
    "GuidedDialogue__RealtimeEnabled",
    "GuidedDialogue__RealtimeModel"
))
{
    $persistedValue = [Environment]::GetEnvironmentVariable($variableName, "User")
    if (-not [string]::IsNullOrWhiteSpace($persistedValue))
    {
        [Environment]::SetEnvironmentVariable($variableName, $persistedValue, "Process")
    }
}

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = "dotnet"
$startInfo.Arguments = "`"$apiDll`""
$startInfo.WorkingDirectory = $projectDirectory
$startInfo.UseShellExecute = $false
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
$apiProcess = [System.Diagnostics.Process]::new()
$apiProcess.StartInfo = $startInfo
if (-not $apiProcess.Start())
{
    throw "The Virtual Company API process could not be started."
}

# Keep detached development hosts diagnosable without routing their output
# through nested shell redirection. The launcher owns both streams for the
# complete lifetime of the tracked API process.
$standardOutputStream = [System.IO.FileStream]::new($standardOutputLog, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::ReadWrite)
$standardErrorStream = [System.IO.FileStream]::new($standardErrorLog, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::ReadWrite)
$standardOutputCopy = $apiProcess.StandardOutput.BaseStream.CopyToAsync($standardOutputStream)
$standardErrorCopy = $apiProcess.StandardError.BaseStream.CopyToAsync($standardErrorStream)

Remove-VcOldRunSnapshots `
    -RunsDirectory (Join-Path $stateDirectory "api-runs") `
    -ProtectedDirectory $runDirectory

@{
    ProcessId = $apiProcess.Id
    StartedUtc = $apiProcess.StartTime.ToUniversalTime().ToString("O")
} | ConvertTo-Json | Set-Content -LiteralPath $stateFile

Write-Host "Virtual Company API process $($apiProcess.Id) started."
Write-Host "Runtime output: $standardOutputLog"
Write-Host "Runtime errors: $standardErrorLog"
Write-Host "The launcher remains attached while the API is running."

try
{
    $apiProcess.WaitForExit()
    exit $apiProcess.ExitCode
}
finally
{
    $apiProcess.Refresh()
    if (-not $apiProcess.HasExited)
    {
        Stop-Process -Id $apiProcess.Id -Force -ErrorAction SilentlyContinue
        $apiProcess.WaitForExit(5000) | Out-Null
    }

    if (Test-Path -LiteralPath $stateFile)
    {
        $state = Get-Content -LiteralPath $stateFile -Raw | ConvertFrom-Json
        if ([int]$state.ProcessId -eq $apiProcess.Id)
        {
            Remove-Item -LiteralPath $stateFile -Force -ErrorAction SilentlyContinue
        }
    }

    try { $standardOutputCopy.GetAwaiter().GetResult() } catch { }
    try { $standardErrorCopy.GetAwaiter().GetResult() } catch { }
    $standardOutputStream.Dispose()
    $standardErrorStream.Dispose()
}
