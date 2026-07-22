param(
    [ValidateSet("Api", "Web", "All")]
    [string]$Project = "All",

    [ValidateRange(1, 10)]
    [int]$Iterations = 2,

    [switch]$Rebuild,

    [switch]$BinaryLog
)

$ErrorActionPreference = "Stop"
$repoRoot = $PSScriptRoot
. (Join-Path $repoRoot "local-build-common.ps1")
$configuration = Get-VcLocalConfiguration
$stateDirectory = Join-Path $repoRoot ".codex-build"
$measurementDirectory = Join-Path $stateDirectory "measurements"
$measurementId = [DateTime]::UtcNow.ToString("yyyyMMddHHmmssfff")
New-Item -ItemType Directory -Path $measurementDirectory -Force | Out-Null

$projects = switch ($Project)
{
    "Api" { @{ Name = "Api"; Path = "src\VirtualCompany.Api\VirtualCompany.Api.csproj" } }
    "Web" { @{ Name = "Web"; Path = "src\VirtualCompany.Web\VirtualCompany.Web.csproj" } }
    default {
        @{ Name = "Api"; Path = "src\VirtualCompany.Api\VirtualCompany.Api.csproj" }
        @{ Name = "Web"; Path = "src\VirtualCompany.Web\VirtualCompany.Web.csproj" }
    }
}

$results = @()
foreach ($projectDefinition in $projects)
{
    for ($iteration = 1; $iteration -le $Iterations; $iteration++)
    {
        $projectPath = Join-Path $repoRoot $projectDefinition.Path
        $arguments = @(
            "build",
            $projectPath,
            "-c", $configuration,
            "--no-restore",
            "--disable-build-servers",
            "-p:BuildInParallel=false",
            "-p:VCStartupProject=VirtualCompany.$($projectDefinition.Name)",
            "-p:VCStartupOutputPath=$(Join-Path $stateDirectory "build-output\$($projectDefinition.Name.ToLowerInvariant())")",
            "-v", "minimal"
        )
        if ($Rebuild)
        {
            $arguments += "-t:Rebuild"
        }
        if ($BinaryLog)
        {
            $binlogPath = Join-Path $measurementDirectory "$measurementId-$($projectDefinition.Name)-$iteration.binlog"
            $arguments += "-bl:$binlogPath"
        }

        $buildLock = Enter-VcBuildLock -StateDirectory $stateDirectory -Operation "Build measurement"
        try
        {
            Write-Host "Measuring $($projectDefinition.Name) build $iteration of $Iterations..."
            $env:MSBuildEnableWorkloadResolver = "false"
            $timer = [System.Diagnostics.Stopwatch]::StartNew()
            & dotnet @arguments
            $exitCode = $LASTEXITCODE
            $timer.Stop()
        }
        finally
        {
            $buildLock.Dispose()
        }

        $result = [pscustomobject]@{
            Project = $projectDefinition.Name
            Configuration = $configuration
            Kind = if ($Rebuild) { "Rebuild" } else { "Incremental" }
            Iteration = $iteration
            ElapsedSeconds = [Math]::Round($timer.Elapsed.TotalSeconds, 3)
            ExitCode = $exitCode
            RecordedUtc = [DateTime]::UtcNow.ToString("O")
        }
        $results += $result
        $result | Format-Table -AutoSize

        if ($exitCode -ne 0)
        {
            throw "$($projectDefinition.Name) build failed with exit code $exitCode."
        }
    }
}

$resultPath = Join-Path $measurementDirectory "$measurementId.json"
$results | ConvertTo-Json | Set-Content -LiteralPath $resultPath
Write-Host "Measurements saved to '$resultPath'."
