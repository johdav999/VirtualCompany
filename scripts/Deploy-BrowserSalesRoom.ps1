[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)] [string] $ResourceGroup,
    [Parameter(Mandatory)] [string] $ParametersFile,
    [string] $DeploymentName = "browser-sales-room-$(Get-Date -Format yyyyMMddHHmmss)",
    [switch] $Apply
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$template = Join-Path $root 'infra/browser-sales-room/main.bicep'
$parameters = (Resolve-Path -LiteralPath $ParametersFile).Path

az bicep build --file $template | Out-Host
az deployment group what-if --resource-group $ResourceGroup --name $DeploymentName `
    --template-file $template --parameters "@$parameters" | Out-Host

if (-not $Apply) {
    Write-Host 'What-if complete. Re-run with -Apply after reviewing the disabled/draining control state.'
    return
}

if ($PSCmdlet.ShouldProcess($ResourceGroup, "Deploy $DeploymentName")) {
    az deployment group create --resource-group $ResourceGroup --name $DeploymentName `
        --template-file $template --parameters "@$parameters" | Out-Host
}
