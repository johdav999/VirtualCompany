#requires -Version 7.0
# Deterministic boundaries only: this test never invokes real Azure CLI, SCM, IMDS, or Key Vault.
$ErrorActionPreference = 'Stop'
$root = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$installer = Join-Path $root 'scripts/Install-TeamsMediaHost.ps1'
$deploy = Join-Path $root 'scripts/Deploy-TeamsMediaHost.ps1'
function Assert-True([bool] $Value, [string] $Message) { if (-not $Value) { throw $Message } }
function Assert-Throws([scriptblock] $Action, [string] $Message) {
    $threw = $false
    try { & $Action | Out-Null } catch { $threw = $true }
    Assert-True $threw $Message
}
$tokens = $null; $errors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($installer, [ref]$tokens, [ref]$errors)
Assert-True ($errors.Count -eq 0) 'Installer syntax failed.'
[void][Management.Automation.Language.Parser]::ParseFile($deploy, [ref]$tokens, [ref]$errors)
Assert-True ($errors.Count -eq 0) 'Deployment syntax failed.'
foreach ($name in @('Get-UniformVmssInstanceId','Stop-TeamsMediaHostForUpgrade','Register-TeamsMediaService')) {
    $function = $ast.Find({param($n) $n -is [Management.Automation.Language.FunctionDefinitionAst] -and $n.Name -eq $name}, $true)
    Assert-True ($null -ne $function) "Missing bootstrap boundary $name."
    . ([scriptblock]::Create($function.Extent.Text))
}
$metadata = [pscustomobject]@{
    resourceId='/subscriptions/sub/resourceGroups/group/providers/Microsoft.Compute/virtualMachineScaleSets/scale/virtualMachines/27'
    subscriptionId='sub'; resourceGroupName='group'; vmScaleSetName='scale'; vmId='aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa'
}
Assert-True ((Get-UniformVmssInstanceId $metadata) -eq '27') 'VM GUID was confused with VMSS instance ID.'
$metadata.vmScaleSetName='wrong'
Assert-Throws { Get-UniformVmssInstanceId $metadata } 'Inconsistent IMDS was accepted.'
$metadata.vmScaleSetName='scale'; $metadata.resourceId='/subscriptions/sub/resourceGroups/group/providers/Microsoft.Compute/virtualMachines/name'
Assert-Throws { Get-UniformVmssInstanceId $metadata } 'Standalone VM identity was accepted.'

$global:vcTestscCalls = [Collections.Generic.List[string]]::new()
$global:vcTestfailSc = ''
function sc.exe { $global:vcTestscCalls.Add([string]$args[0]); $global:LASTEXITCODE = if ($args[0] -eq $global:vcTestfailSc) { 5 } else { 0 } }
Register-TeamsMediaService 'TestService' '"C:\test\VirtualCompany.Api.exe"' $false
Register-TeamsMediaService 'TestService' '"C:\test\VirtualCompany.Api.exe"' $true
Assert-True (($global:vcTestscCalls -join ',') -eq 'create,failure,config,failure') 'Repeat install created duplicate SCM services.'
$global:vcTestfailSc='config'
Assert-Throws { Register-TeamsMediaService 'TestService' 'test.exe' $true } 'SCM failure was ignored.'
$global:vcTestfailSc='failure'
Assert-Throws { Register-TeamsMediaService 'TestService' 'test.exe' $false } 'Recovery failure was ignored.'

$global:vcTeststops=0; $global:vcTestupgradeStatus=503; $global:vcTestserviceStopped=$false
function Get-Service {
    $service = [pscustomobject]@{ Status = $(if($global:vcTestserviceStopped){'Stopped'}else{'Running'}) }
    $service | Add-Member ScriptMethod WaitForStatus { param($state,$timeout) if (-not $global:vcTestserviceStopped) { throw 'Did not stop' } }
    return $service
}
function Invoke-WebRequest { [pscustomobject]@{StatusCode=$global:vcTestupgradeStatus} }
function Stop-Service { $global:vcTeststops++; $global:vcTestserviceStopped=$true }
Assert-Throws { Stop-TeamsMediaHostForUpgrade 'TestService' } 'Upgrade interrupted a call before provider termination.'
Assert-True ($global:vcTeststops -eq 0) 'Stop-Service was called before drain.'
$global:vcTestupgradeStatus=200
Stop-TeamsMediaHostForUpgrade 'TestService'
Stop-TeamsMediaHostForUpgrade 'TestService'
Assert-True ($global:vcTeststops -eq 1) 'Drained/stopped install was not idempotent.'

$global:vcTestazCalls=[Collections.Generic.List[string]]::new(); $global:vcTestazFailure=''; $global:vcTesttempPath=''
function az {
    $op = if($args[0] -eq 'bicep'){'build'}else{[string]$args[2]}
    $global:vcTestazCalls.Add($op); $global:LASTEXITCODE=0
    if($op -eq 'build') {
        $outIndex = [Array]::IndexOf($args,'--outfile')
        $global:vcTesttempPath=Split-Path $args[$outIndex+1]
        if($global:vcTestazFailure -ne 'build'){ '{}' | Set-Content -LiteralPath $args[$outIndex+1] }
    }
    if($op -eq $global:vcTestazFailure){ $global:LASTEXITCODE=1 }
}
$fixture=Join-Path $root "artifacts/teams-script-parameters-$([guid]::NewGuid().ToString('N')).json"
try {
    @{parameters=@{ adminPassword=@{value='unit-test-only'}; apiPackageUri=@{value='https://example.test/VirtualCompany.Api.zip'};
        bootstrapScriptUri=@{value='https://example.test/Install-TeamsMediaHost.ps1'} }} | ConvertTo-Json -Depth 6 | Set-Content $fixture
    & $deploy -Location test -ParameterFile $fixture -WhatIf
    Assert-True ($global:vcTestazCalls.Count -eq 0) 'PowerShell WhatIf contacted Azure.'
    & $deploy -Location test -ParameterFile $fixture -Preview
    Assert-True (($global:vcTestazCalls -join ',') -eq 'build,what-if') 'Azure preview did not execute only what-if.'
    Assert-True (-not (Test-Path -LiteralPath $global:vcTesttempPath)) 'Preview retained temporary secret files.'
    $global:vcTestazCalls.Clear()
    & $deploy -Location test -ParameterFile $fixture -Confirm:$false
    Assert-True (($global:vcTestazCalls -join ',') -eq 'build,create') 'Normal deployment did not execute create.'
    foreach($failure in @('build','what-if')) {
        $global:vcTestazFailure=$failure
        Assert-Throws { & $deploy -Location test -ParameterFile $fixture -Preview } "Azure $failure failure was swallowed."
        Assert-True (-not (Test-Path -LiteralPath $global:vcTesttempPath)) 'Failed deployment retained temporary files.'
    }
} finally { Remove-Item -LiteralPath $fixture -Force }
Write-Output 'PASS: syntax, Uniform VMSS identity, SCM failure/idempotency, drain safety, WhatIf, Azure preview/create, failure cleanup.'
