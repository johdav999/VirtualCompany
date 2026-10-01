#requires -Version 7.0
# Contract checks only; do not start/stop application processes or contact NuGet.
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent

function Assert-True([bool]$Value, [string]$Message)
{
    if (-not $Value) { throw $Message }
}

foreach ($launcher in @('client.ps1', 'run-api.ps1'))
{
    $tokens = $null
    $parseErrors = $null
    $ast = [Management.Automation.Language.Parser]::ParseFile(
        (Join-Path $repoRoot $launcher), [ref]$tokens, [ref]$parseErrors)
    Assert-True ($parseErrors.Count -eq 0) "$launcher has invalid syntax."
    $builds = @($ast.FindAll({
        param($node)
        $node -is [Management.Automation.Language.CommandAst] -and
        $node.GetCommandName() -eq 'dotnet' -and
        $node.CommandElements[1].Extent.Text -eq 'build'
    }, $true))
    Assert-True ($builds.Count -eq 1) "$launcher must have one startup build."
    $build = $builds[0]
    $arguments = @($build.CommandElements | ForEach-Object { $_.Extent.Text })
    Assert-True ($arguments -notcontains '--no-restore') "$launcher skips repair of stale or missing NuGet assets."
    Assert-True ($arguments -notcontains '--no-dependencies') "$launcher skips referenced-project dependencies."

    $buildScope = $build.Parent
    while ($null -ne $buildScope -and $buildScope -isnot [Management.Automation.Language.TryStatementAst])
    {
        $buildScope = $buildScope.Parent
    }
    Assert-True ($null -ne $buildScope) "$launcher build is outside the lock's try/finally scope."
    Assert-True ($buildScope.Finally.Extent.Text -match '\$buildLock\.Dispose\(\)') "$launcher does not release its build lock on failure."
    $lock = $ast.Find({
        param($node)
        $node -is [Management.Automation.Language.CommandAst] -and
        $node.GetCommandName() -eq 'Enter-VcBuildLock'
    }, $true)
    Assert-True ($null -ne $lock -and $lock.Extent.StartOffset -lt $buildScope.Extent.StartOffset) "$launcher must acquire the shared lock before restore/build."
    Assert-True ($buildScope.Body.Extent.Text -match 'if \(\$buildExitCode -ne 0\)\s*\{\s*exit \$buildExitCode') "$launcher must stop on restore/build failure."
}

Write-Output 'PASS: both launchers restore dependencies under the shared lock, release it on failure, and stop on build errors.'
