[CmdletBinding()]
param(
    [ValidateRange(1, 20)]
    [int]$Iterations = 3,

    [ValidateSet("All", "Clean", "Warm", "Edits")]
    [string]$ScenarioSet = "All",

    [string]$Configuration = "LocalRun",

    [string]$OutputDirectory = ".codex-build/build-performance",

    [string]$ReportPath = "docs/build-performance-baseline.md",

    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$dotnetCommand = Get-Command dotnet.exe -CommandType Application -ErrorAction SilentlyContinue |
    Select-Object -First 1
if ($null -eq $dotnetCommand) {
    $dotnetCommand = Get-Command dotnet -CommandType Application -ErrorAction Stop |
        Select-Object -First 1
}
$dotnet = $dotnetCommand.Path
$runId = [DateTime]::UtcNow.ToString("yyyyMMddHHmmssfff")
$artifactRoot = Join-Path $repositoryRoot (Join-Path $OutputDirectory $runId)
$reportFile = Join-Path $repositoryRoot $ReportPath
$results = [System.Collections.Generic.List[object]]::new()

$projects = [ordered]@{
    Domain = "src/VirtualCompany.Domain/VirtualCompany.Domain.csproj"
    Application = "src/VirtualCompany.Application/VirtualCompany.Application.csproj"
    Persistence = "src/VirtualCompany.Persistence/VirtualCompany.Persistence.csproj"
    "Persistence.Migrations" = "src/VirtualCompany.Persistence.Migrations/VirtualCompany.Persistence.Migrations.csproj"
    "Infrastructure.Platform" = "src/VirtualCompany.Infrastructure.Platform/VirtualCompany.Infrastructure.Platform.csproj"
    "Infrastructure.Mailbox" = "src/VirtualCompany.Infrastructure.Mailbox/VirtualCompany.Infrastructure.Mailbox.csproj"
    "Infrastructure.Finance" = "src/VirtualCompany.Infrastructure.Finance/VirtualCompany.Infrastructure.Finance.csproj"
    "Infrastructure.Sales" = "src/VirtualCompany.Infrastructure.Sales/VirtualCompany.Infrastructure.Sales.csproj"
    "Infrastructure.Support" = "src/VirtualCompany.Infrastructure.Support/VirtualCompany.Infrastructure.Support.csproj"
    "Infrastructure.Operations" = "src/VirtualCompany.Infrastructure.Operations/VirtualCompany.Infrastructure.Operations.csproj"
    Infrastructure = "src/VirtualCompany.Infrastructure/VirtualCompany.Infrastructure.csproj"
    Api = "src/VirtualCompany.Api/VirtualCompany.Api.csproj"
    Web = "src/VirtualCompany.Web/VirtualCompany.Web.csproj"
}

$editScenarios = [ordered]@{
    Finance = @{ Path = "src/VirtualCompany.Infrastructure.Finance/Finance/FinanceModuleRegistration.cs"; Project = $projects["Infrastructure.Finance"] }
    Sales = @{ Path = "src/VirtualCompany.Infrastructure.Sales/Sales/SalesModuleRegistration.cs"; Project = $projects["Infrastructure.Sales"] }
    Support = @{ Path = "src/VirtualCompany.Infrastructure.Support/Support/SupportModuleRegistration.cs"; Project = $projects["Infrastructure.Support"] }
    PersistenceConfiguration = @{ Path = "src/VirtualCompany.Persistence/Persistence/Configurations/WorkTaskConfiguration.cs"; Project = $projects.Persistence }
    Migration = @{ Path = "src/VirtualCompany.Persistence.Migrations/Persistence/Migrations/VirtualCompanyDbContextModelSnapshot.cs"; Project = $projects["Persistence.Migrations"] }
}

function Get-RelativePath {
    param([string]$BasePath, [string]$TargetPath)

    $base = [IO.Path]::GetFullPath($BasePath).TrimEnd([IO.Path]::DirectorySeparatorChar) +
        [IO.Path]::DirectorySeparatorChar
    $target = [IO.Path]::GetFullPath($TargetPath)
    $relativeUri = ([Uri]$base).MakeRelativeUri([Uri]$target)
    return [Uri]::UnescapeDataString($relativeUri.ToString()).Replace('/', [IO.Path]::DirectorySeparatorChar)
}

function Invoke-MeasuredBuild {
    param(
        [string]$Name,
        [string]$Project,
        [int]$Iteration,
        [switch]$Clean
    )

    $scenarioDirectory = Join-Path $artifactRoot "$Name-$Iteration"
    New-Item -ItemType Directory -Path $scenarioDirectory -Force | Out-Null
    $logPath = Join-Path $scenarioDirectory "build.log"
    $outputTimestamps = @{}
    foreach ($candidate in $projects.GetEnumerator()) {
        $assemblyPath = Join-Path $repositoryRoot "src/VirtualCompany.$($candidate.Key)/bin/$Configuration/net9.0/VirtualCompany.$($candidate.Key).dll"
        $outputTimestamps[$candidate.Key] = if (Test-Path -LiteralPath $assemblyPath) {
            (Get-Item -LiteralPath $assemblyPath).LastWriteTimeUtc
        }
        else {
            [DateTime]::MinValue
        }
    }
    $arguments = [System.Collections.Generic.List[string]]::new()
    $arguments.Add("build")
    $arguments.Add((Join-Path $repositoryRoot $Project))
    $arguments.Add("--configuration")
    $arguments.Add($Configuration)
    $arguments.Add("--nologo")
    $arguments.Add("--verbosity")
    $arguments.Add("minimal")
    $arguments.Add("--disable-build-servers")
    if ($NoRestore) { $arguments.Add("--no-restore") }
    if ($Clean) { $arguments.Add("--no-incremental") }
    $arguments.Add("-m:1")
    $arguments.Add("-p:BuildInParallel=false")
    $arguments.Add("-p:UseSharedCompilation=false")

    $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
    $output = & $dotnet @arguments 2>&1
    $exitCode = $LASTEXITCODE
    $stopwatch.Stop()
    $output | Set-Content -Path $logPath
    if ($exitCode -ne 0) {
        throw "Build scenario '$Name' iteration $Iteration failed. See $logPath."
    }

    $compiledProjects = @($projects.GetEnumerator() | Where-Object {
        $assemblyPath = Join-Path $repositoryRoot "src/VirtualCompany.$($_.Key)/bin/$Configuration/net9.0/VirtualCompany.$($_.Key).dll"
        (Test-Path -LiteralPath $assemblyPath) -and
            (Get-Item -LiteralPath $assemblyPath).LastWriteTimeUtc -gt $outputTimestamps[$_.Key]
    } | ForEach-Object { $_.Key })

    $results.Add([pscustomobject]@{
        Scenario = $Name
        Iteration = $Iteration
        Project = $Project
        ElapsedMilliseconds = [math]::Round($stopwatch.Elapsed.TotalMilliseconds, 0)
        CompiledProjects = $compiledProjects
        LogPath = Get-RelativePath -BasePath $repositoryRoot -TargetPath $logPath
    })
}

function Invoke-WithTouchedFile {
    param([string]$RelativePath, [scriptblock]$Action)

    $path = Join-Path $repositoryRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Edit scenario source file was not found: $RelativePath"
    }

    $originalTimestamp = (Get-Item -LiteralPath $path).LastWriteTimeUtc
    try {
        (Get-Item -LiteralPath $path).LastWriteTimeUtc = [DateTime]::UtcNow
        & $Action
    }
    finally {
        (Get-Item -LiteralPath $path).LastWriteTimeUtc = $originalTimestamp
    }
}

Push-Location $repositoryRoot
try {
    New-Item -ItemType Directory -Path $artifactRoot -Force | Out-Null

    if ($ScenarioSet -in @("All", "Clean")) {
        foreach ($project in $projects.GetEnumerator()) {
            for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
                Invoke-MeasuredBuild -Name "Clean-$($project.Key)" -Project $project.Value -Iteration $iteration -Clean
            }
        }
    }

    if ($ScenarioSet -in @("All", "Warm")) {
        foreach ($project in $projects.GetEnumerator()) {
            Invoke-MeasuredBuild -Name "Warm-Primer-$($project.Key)" -Project $project.Value -Iteration 0
            for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
                Invoke-MeasuredBuild -Name "Warm-$($project.Key)" -Project $project.Value -Iteration $iteration
            }
        }
    }

    if ($ScenarioSet -in @("All", "Edits")) {
        Invoke-MeasuredBuild -Name "Edit-Primer-Infrastructure" -Project $projects.Infrastructure -Iteration 0
        foreach ($scenario in $editScenarios.GetEnumerator()) {
            for ($iteration = 1; $iteration -le $Iterations; $iteration++) {
                Invoke-WithTouchedFile -RelativePath $scenario.Value.Path -Action {
                    Invoke-MeasuredBuild -Name "Edit-$($scenario.Key)" -Project $scenario.Value.Project -Iteration $iteration
                }
            }
        }
    }
}
finally {
    Pop-Location
}

$sourceCounts = Get-ChildItem (Join-Path $repositoryRoot "src") -Recurse -Filter *.cs -File |
    Where-Object { $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } |
    Group-Object { $_.DirectoryName.Substring($repositoryRoot.Length + 1).Split([IO.Path]::DirectorySeparatorChar)[1] } |
    Sort-Object Name |
    ForEach-Object { [pscustomobject]@{ Project = $_.Name; CSharpFiles = $_.Count } }

$projectGraph = foreach ($project in $projects.GetEnumerator()) {
    [xml]$xml = Get-Content (Join-Path $repositoryRoot $project.Value)
    $references = @($xml.Project.ItemGroup.ProjectReference |
        ForEach-Object { [string]$_.Include } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { [IO.Path]::GetFileNameWithoutExtension($_) })
    [pscustomobject]@{ Project = $project.Key; References = [string[]]$references }
}

$sdkVersion = (& $dotnet --version).Trim()
$os = [System.Runtime.InteropServices.RuntimeInformation]::OSDescription
$processor = $env:PROCESSOR_IDENTIFIER
$memoryGb = try {
    [math]::Round((Get-CimInstance Win32_ComputerSystem -ErrorAction Stop).TotalPhysicalMemory / 1GB, 1)
}
catch {
    "Unavailable"
}
$measured = $results | Where-Object { $_.Iteration -gt 0 }
$summary = $measured | Group-Object Scenario | Sort-Object Name | ForEach-Object {
    $ordered = @($_.Group.ElapsedMilliseconds | Sort-Object)
    $middle = [int][math]::Floor($ordered.Count / 2)
    $median = if ($ordered.Count % 2 -eq 0) { ($ordered[$middle - 1] + $ordered[$middle]) / 2 } else { $ordered[$middle] }
    [pscustomobject]@{
        Scenario = $_.Name
        MedianMilliseconds = [math]::Round($median, 0)
        Runs = $ordered.Count
        CompiledProjects = @($_.Group.CompiledProjects | ForEach-Object { $_ } | Select-Object -Unique)
    }
}

$jsonPath = Join-Path $artifactRoot "results.json"
[pscustomobject]@{
    GeneratedUtc = [DateTime]::UtcNow
    SdkVersion = $sdkVersion
    Configuration = $Configuration
    Iterations = $Iterations
    Machine = [pscustomobject]@{ OperatingSystem = $os; Processor = $processor; MemoryGb = $memoryGb }
    SourceCounts = $sourceCounts
    ProjectGraph = $projectGraph
    Results = $results
    Summary = $summary
} | ConvertTo-Json -Depth 8 | Set-Content $jsonPath

$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add("# Build Performance Report")
$lines.Add("")
$lines.Add("Generated: $([DateTime]::UtcNow.ToString('yyyy-MM-dd HH:mm:ss')) UTC")
$lines.Add("")
$lines.Add("This report is comparative evidence, not a fixed performance gate. Re-run on the same machine, SDK, configuration, package cache, process count, and source state after each assembly-boundary change.")
$lines.Add("")
$lines.Add("## Environment")
$lines.Add("")
$lines.Add("- SDK: $sdkVersion")
$lines.Add("- Configuration: $Configuration")
$lines.Add("- OS: $os")
$lines.Add("- Processor: $processor")
$lines.Add("- Memory: $memoryGb GB")
$lines.Add("- Iterations per measured scenario: $Iterations")
$lines.Add("- Process isolation: build servers disabled, shared compilation disabled, parallel build disabled")
$lines.Add("")
$lines.Add("## Median Timings")
$lines.Add("")
$lines.Add("| Scenario | Median (ms) | Runs | Projects reported as compiled |")
$lines.Add("| --- | ---: | ---: | --- |")
foreach ($item in $summary) {
    $compiled = if ($item.CompiledProjects.Count -eq 0) { "None reported" } else { $item.CompiledProjects -join ", " }
    $lines.Add("| $($item.Scenario) | $($item.MedianMilliseconds) | $($item.Runs) | $compiled |")
}
$lines.Add("")
$lines.Add("## Source Counts")
$lines.Add("")
$lines.Add("| Project | C# files |")
$lines.Add("| --- | ---: |")
foreach ($item in $sourceCounts) { $lines.Add("| $($item.Project) | $($item.CSharpFiles) |") }
$lines.Add("")
$lines.Add("## Project Dependency Graph")
$lines.Add("")
foreach ($item in $projectGraph) {
    $references = if (@($item.References).Count -eq 0) { "None" } else { @($item.References) -join ", " }
    $lines.Add("- **$($item.Project)**: $references")
}
$lines.Add("")
$relativeArtifactRoot = Get-RelativePath -BasePath $repositoryRoot -TargetPath $artifactRoot
$lines.Add("Raw run data and logs: ``$relativeArtifactRoot`` (ignored build artifact).")

$reportDirectory = Split-Path -Parent $reportFile
New-Item -ItemType Directory -Path $reportDirectory -Force | Out-Null
$lines | Set-Content $reportFile
Write-Output "Build performance report: $reportFile"
Write-Output "Raw data: $jsonPath"
