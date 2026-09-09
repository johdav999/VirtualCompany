#requires -Version 7.0
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [ValidateNotNullOrEmpty()] [string] $Location,
    [Parameter(Mandatory)] [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })] [string] $ParameterFile,
    [switch] $Preview
)

$ErrorActionPreference = 'Stop'
# PowerShell simulation never contacts Azure, writes secrets, or invokes tools.
if ($WhatIfPreference) {
    [void]$PSCmdlet.ShouldProcess("Azure subscription deployment in $Location", 'Deploy Teams media host')
    return
}
if (-not (Get-Command az -ErrorAction SilentlyContinue)) { throw 'Azure CLI is required.' }
& (Join-Path $PSScriptRoot 'Test-TeamsMediaSdkFreshness.ps1')
if (-not $Preview -and -not $PSCmdlet.ShouldProcess("Azure subscription deployment in $Location", 'create')) { return }

$template = Join-Path $PSScriptRoot '..\infra\teams-media\subscription.bicep'
$temporaryDirectory = Join-Path ([IO.Path]::GetTempPath()) "vc-teams-media-$([guid]::NewGuid().ToString('N'))"
$compiledTemplate = Join-Path $temporaryDirectory 'template.json'
$temporaryParameters = Join-Path $temporaryDirectory 'parameters.json'
try {
    [void](New-Item -ItemType Directory -Path $temporaryDirectory)
    if ($IsWindows) {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
        & icacls.exe $temporaryDirectory /inheritance:r /grant:r "*${identity}:(OI)(CI)F" '*S-1-5-18:(OI)(CI)F' | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not restrict deployment temporary directory access.' }
    } else {
        & chmod 700 $temporaryDirectory
        if ($LASTEXITCODE -ne 0) { throw 'Could not restrict deployment temporary directory access.' }
    }
    & az bicep build --file $template --outfile $compiledTemplate --only-show-errors
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $compiledTemplate)) { throw 'Bicep compilation failed.' }
    $parameters = Get-Content -LiteralPath $ParameterFile -Raw | ConvertFrom-Json -AsHashtable
    if ($parameters.parameters -isnot [System.Collections.IDictionary]) { throw 'Expected an Azure deployment parameters object.' }
    if (-not $parameters.parameters.ContainsKey('adminPassword')) {
        if ([string]::IsNullOrWhiteSpace($env:VC_TEAMS_MEDIA_ADMIN_PASSWORD)) { throw 'Set VC_TEAMS_MEDIA_ADMIN_PASSWORD for this deployment.' }
        $parameters.parameters.adminPassword = @{ value = $env:VC_TEAMS_MEDIA_ADMIN_PASSWORD }
    }
    if (-not $parameters.parameters.ContainsKey('apiPackageUri')) {
        if ([string]::IsNullOrWhiteSpace($env:VC_TEAMS_MEDIA_PACKAGE_URI)) { throw 'Set VC_TEAMS_MEDIA_PACKAGE_URI to a short-lived HTTPS artifact URI.' }
        $parameters.parameters.apiPackageUri = @{ value = $env:VC_TEAMS_MEDIA_PACKAGE_URI }
    }
    foreach ($name in @('apiPackageUri', 'bootstrapScriptUri')) {
        if ($parameters.parameters[$name] -isnot [System.Collections.IDictionary]) { throw "Provide the required $name artifact parameter." }
        if ($parameters.parameters[$name].ContainsKey('value')) {
            $uri = $null
            if (-not [uri]::TryCreate([string]$parameters.parameters[$name].value, [UriKind]::Absolute, [ref]$uri) -or $uri.Scheme -ne 'https') {
                throw "$name must be an absolute HTTPS artifact URI."
            }
        }
    }
    $parameters | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $temporaryParameters -Encoding UTF8
    $mode = if ($Preview) { 'what-if' } else { 'create' }
    # Secure template parameters are redacted by ARM. Suppress CLI error bodies which may contain input URLs.
    $result = & az deployment sub $mode --name "vc-teams-media-$([datetime]::UtcNow.ToString('yyyyMMddHHmmss'))" --location $Location --template-file $compiledTemplate --parameters "@$temporaryParameters" --only-show-errors 2>&1
    if ($LASTEXITCODE -ne 0) { throw "Azure deployment $mode failed. Review the deployment operation in Azure; secret-bearing CLI output has been suppressed." }
    if ($Preview) { $result } else { Write-Output 'Azure deployment completed. Validate host readiness before enabling calls.' }
} finally {
    foreach ($path in @($temporaryParameters, $compiledTemplate)) {
        if (Test-Path -LiteralPath $path) { Remove-Item -LiteralPath $path -Force }
    }
    if (Test-Path -LiteralPath $temporaryDirectory) { Remove-Item -LiteralPath $temporaryDirectory -Force }
}
