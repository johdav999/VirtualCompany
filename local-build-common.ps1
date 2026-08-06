$script:VcLocalConfiguration = "LocalRun"
$script:VcSnapshotRetentionCount = 5

function Get-VcLocalConfiguration
{
    return $script:VcLocalConfiguration
}

function Enter-VcBuildLock
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$StateDirectory,

        [string]$Operation = "local build",

        [int]$TimeoutSeconds = 300
    )

    New-Item -ItemType Directory -Path $StateDirectory -Force | Out-Null
    $lockPath = Join-Path $StateDirectory "local-build.lock"
    $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
    $waitStartedUtc = [DateTime]::UtcNow
    $reportedWait = $false
    $nextProgressUtc = $waitStartedUtc

    while ([DateTime]::UtcNow -lt $deadline)
    {
        try
        {
            $stream = $null
            $stream = [System.IO.File]::Open(
                $lockPath,
                [System.IO.FileMode]::OpenOrCreate,
                [System.IO.FileAccess]::ReadWrite,
                [System.IO.FileShare]::Read)
            $stream.SetLength(0)
            $writer = [System.IO.StreamWriter]::new($stream, [System.Text.Encoding]::UTF8, 1024, $true)
            $metadata = "PID={0};AcquiredUtc={1};Operation={2}" -f $PID, [DateTime]::UtcNow.ToString("O"), $Operation
            $writer.Write($metadata)
            $writer.Flush()
            $writer.Dispose()
            return $stream
        }
        catch [System.IO.IOException]
        {
            $nowUtc = [DateTime]::UtcNow
            if (-not $reportedWait -or $nowUtc -ge $nextProgressUtc)
            {
                $owner = $null
                try
                {
                    $owner = Get-Content -LiteralPath $lockPath -Raw -ErrorAction Stop
                }
                catch
                {
                    # Older script versions opened the lock without read sharing.
                }

                $elapsedSeconds = [Math]::Floor(($nowUtc - $waitStartedUtc).TotalSeconds)
                if ([string]::IsNullOrWhiteSpace($owner))
                {
                    Write-Host "Another Virtual Company build is running. Waiting for the shared build lock ($elapsedSeconds seconds elapsed)..."
                }
                else
                {
                    Write-Host "Another Virtual Company build is running [$owner]. Waiting for the shared build lock ($elapsedSeconds seconds elapsed)..."
                }
                $reportedWait = $true
                $nextProgressUtc = $nowUtc.AddSeconds(15)
            }
            Start-Sleep -Milliseconds 500
        }
        catch
        {
            if ($null -ne $stream)
            {
                $stream.Dispose()
            }
            throw
        }
    }

    throw "Timed out after $TimeoutSeconds seconds waiting for '$lockPath'. Stop the other local build or wait for it to finish."
}

function Stop-VcProcessesRunningFromBuildDirectory
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$BuildDirectory,

        [Parameter(Mandatory = $true)]
        [string]$EntryAssembly
    )

    $resolvedBuildDirectory = [System.IO.Path]::GetFullPath($BuildDirectory).TrimEnd('\')
    $entryAssemblyPath = Join-Path $resolvedBuildDirectory $EntryAssembly
    $entryExecutablePath = [System.IO.Path]::ChangeExtension($entryAssemblyPath, ".exe")
    $entryAssemblyPathLower = $entryAssemblyPath.ToLowerInvariant()
    $entryExecutablePathLower = $entryExecutablePath.ToLowerInvariant()

    $candidates = Get-CimInstance Win32_Process -ErrorAction SilentlyContinue |
        Where-Object {
            $commandLine = [string]$_.CommandLine
            $commandLineLower = $commandLine.ToLowerInvariant()
            $_.ProcessId -ne $PID -and
            ($_.Name -eq "dotnet.exe" -or $_.Name -eq ([System.IO.Path]::GetFileName($entryExecutablePath))) -and
            -not [string]::IsNullOrWhiteSpace($commandLine) -and
            ($commandLineLower.Contains($entryAssemblyPathLower) -or
             $commandLineLower.Contains($entryExecutablePathLower))
        }

    foreach ($candidate in @($candidates))
    {
        $process = Get-Process -Id ([int]$candidate.ProcessId) -ErrorAction SilentlyContinue
        if ($null -eq $process)
        {
            continue
        }

        Write-Host "Stopping process $($process.Id) running from the stable build directory..."
        Stop-Process -Id $process.Id -Force -ErrorAction Stop
        $process.WaitForExit(10000) | Out-Null
    }
}

function Copy-VcBuildSnapshot
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$BuildDirectory,

        [Parameter(Mandatory = $true)]
        [string]$RunDirectory,

        [Parameter(Mandatory = $true)]
        [string]$EntryAssembly
    )

    $entryAssemblyPath = Join-Path $BuildDirectory $EntryAssembly
    if (-not (Test-Path -LiteralPath $entryAssemblyPath))
    {
        throw "The build completed without producing '$entryAssemblyPath'."
    }

    New-Item -ItemType Directory -Path $RunDirectory -Force | Out-Null
    Copy-Item -Path (Join-Path $BuildDirectory "*") -Destination $RunDirectory -Recurse -Force
}

function Remove-VcOldRunSnapshots
{
    param(
        [Parameter(Mandatory = $true)]
        [string]$RunsDirectory,

        [string]$ProtectedDirectory,

        [int]$Keep = $script:VcSnapshotRetentionCount
    )

    if (-not (Test-Path -LiteralPath $RunsDirectory))
    {
        return
    }

    $resolvedRunsDirectory = [System.IO.Path]::GetFullPath($RunsDirectory).TrimEnd('\')
    $resolvedProtectedDirectory = if ([string]::IsNullOrWhiteSpace($ProtectedDirectory)) {
        $null
    } else {
        [System.IO.Path]::GetFullPath($ProtectedDirectory).TrimEnd('\')
    }
    $directories = @(Get-ChildItem -LiteralPath $resolvedRunsDirectory -Directory |
        Sort-Object LastWriteTimeUtc -Descending)
    $retained = 0

    foreach ($directory in $directories)
    {
        $resolvedCandidate = [System.IO.Path]::GetFullPath($directory.FullName).TrimEnd('\')
        $isChild = $resolvedCandidate.StartsWith(
            "$resolvedRunsDirectory\",
            [System.StringComparison]::OrdinalIgnoreCase)
        if (-not $isChild)
        {
            throw "Refusing to remove snapshot outside '$resolvedRunsDirectory': '$resolvedCandidate'."
        }

        if ($resolvedCandidate -eq $resolvedProtectedDirectory -or $retained -lt $Keep)
        {
            $retained++
            continue
        }

        try
        {
            Remove-Item -LiteralPath $resolvedCandidate -Recurse -Force -ErrorAction Stop
        }
        catch [System.UnauthorizedAccessException]
        {
            Write-Warning "Could not remove old runtime snapshot '$resolvedCandidate' because one or more files are still in use. The snapshot was retained and startup will continue."
        }
        catch [System.IO.IOException]
        {
            Write-Warning "Could not remove old runtime snapshot '$resolvedCandidate' because one or more files are still in use. The snapshot was retained and startup will continue."
        }
    }
}
