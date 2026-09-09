[CmdletBinding()]
param(
    [string] $ProjectPath = (Join-Path $PSScriptRoot '..\src\VirtualCompany.Infrastructure.Sales\VirtualCompany.Infrastructure.Sales.csproj'),
    [string] $LockPath = (Join-Path $PSScriptRoot '..\infra\teams-media\media-sdk-lock.json'),
    [datetime] $AsOfUtc = [datetime]::UtcNow
)

$ErrorActionPreference = 'Stop'
$project = [xml](Get-Content -LiteralPath $ProjectPath -Raw)
$lock = Get-Content -LiteralPath $LockPath -Raw | ConvertFrom-Json
$reference = @($project.Project.ItemGroup.PackageReference) |
    Where-Object { $_.Include -eq $lock.package } |
    Select-Object -First 1
if ($null -eq $reference) { throw "The required Teams media SDK package is not referenced." }
if ([string]$reference.Version -ne [string]$lock.version) {
    throw "Teams media SDK version $($reference.Version) does not match the reviewed lock version $($lock.version)."
}
$publishedUtc = ([datetime]$lock.publishedUtc).ToUniversalTime()
$age = $AsOfUtc.ToUniversalTime() - $publishedUtc
if ($age.TotalDays -lt 0) { throw 'The Teams media SDK publication date is in the future.' }
if ($age.TotalDays -gt [int]$lock.maximumAgeDays) {
    throw "Teams media SDK $($lock.version) is $([math]::Floor($age.TotalDays)) days old; the maximum supported age is $($lock.maximumAgeDays) days. Revalidate and update before deployment."
}
Write-Output "Teams media SDK $($lock.version) passed the $($lock.maximumAgeDays)-day freshness gate ($([math]::Floor($age.TotalDays)) days old)."
