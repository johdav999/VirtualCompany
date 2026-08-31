[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [Guid]$CompanyId,
    [Parameter(Mandatory)]
    [Guid]$FiscalPeriodId,
    [Parameter(Mandatory)]
    [string]$MatrixManifestPath,
    [Parameter(Mandatory)]
    [string]$BrowserEvidencePath,
    [Parameter(Mandatory)]
    [string]$RecoveryEvidencePath,
    [Parameter(Mandatory)]
    [string]$CapacityEvidencePath,
    [Parameter(Mandatory)]
    [string]$ProviderScopeEvidencePath,
    [Parameter(Mandatory)]
    [string]$ProfessionalReviewEvidencePath,
    [string]$ApiBaseUri = 'https://localhost:7183',
    [string]$ExpectedEvidenceHash,
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\artifacts\close-compliance-release\release-decision.json')
)

$ErrorActionPreference = 'Stop'
$token = $env:VC_CLOSE_COMPLIANCE_OPERATOR_TOKEN
if ([string]::IsNullOrWhiteSpace($token)) {
    throw 'Set VC_CLOSE_COMPLIANCE_OPERATOR_TOKEN to a short-lived AccountingAdmin bearer token.'
}

function Read-EvidenceRecord {
    param([string]$Path, [string]$ExpectedLane)

    $resolved = Resolve-Path -LiteralPath $Path
    $record = Get-Content -LiteralPath $resolved -Raw | ConvertFrom-Json
    if ($record.lane -ne $ExpectedLane) { throw "Evidence '$resolved' has lane '$($record.lane)'; expected '$ExpectedLane'." }
    if ($record.outcome -notin @('passed', 'approved')) { throw "Evidence '$resolved' is not passed or approved." }
    if ([Guid]$record.companyId -ne $CompanyId) { throw "Evidence '$resolved' belongs to another company." }
    if ([Guid]$record.fiscalPeriodId -ne $FiscalPeriodId) { throw "Evidence '$resolved' belongs to another fiscal period." }
    if ([string]::IsNullOrWhiteSpace($record.reviewer)) { throw "Evidence '$resolved' has no accountable reviewer." }
    if ([string]::IsNullOrWhiteSpace($record.generatedUtc)) { throw "Evidence '$resolved' has no generation timestamp." }
    return $record
}

$matrixPath = Resolve-Path -LiteralPath $MatrixManifestPath
$matrix = Get-Content -LiteralPath $matrixPath -Raw | ConvertFrom-Json
if ($matrix.schemaVersion -lt 2 -or $matrix.lane -ne 'close-compliance-proof') {
    throw 'The matrix manifest must be schema version 2 or later from the close-compliance-proof lane.'
}

$operatorCategories = @(
    'close-compliance-recovery',
    'close-compliance-browser',
    'close-compliance-capacity',
    'close-compliance-provider-scope',
    'close-compliance-professional-review'
)
$automatedStops = @($matrix.results | Where-Object {
    $_.outcome -ne 'passed' -and $_.category -notin $operatorCategories
})
if ($automatedStops.Count -gt 0) {
    throw "Automated release proof is incomplete: $($automatedStops.category -join ', ')."
}

$base = $ApiBaseUri.TrimEnd('/')
$route = "$base/api/companies/$($CompanyId.ToString('D'))/finance/close-compliance-release-readiness?fiscalPeriodId=$($FiscalPeriodId.ToString('D'))"
$readiness = Invoke-RestMethod -Method Get -Uri $route -Headers @{ Authorization = "Bearer $token" }
if ($readiness.companyId -ne $CompanyId.ToString('D') -or $readiness.fiscalPeriodId -ne $FiscalPeriodId.ToString('D')) {
    throw 'The readiness response company or fiscal period does not match the requested scope.'
}
if ($readiness.decision -ne 'ready' -or [int]$readiness.releaseStopCount -ne 0) {
    $blocked = @($readiness.signals | Where-Object { $_.status -eq 'release_stop' } | ForEach-Object { $_.key })
    throw "Backend readiness is no-go: $($blocked -join ', ')."
}
if (-not [string]::IsNullOrWhiteSpace($ExpectedEvidenceHash) -and
    -not [string]::Equals($ExpectedEvidenceHash, $readiness.evidenceHash, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Readiness evidence hash changed. Expected $ExpectedEvidenceHash but received $($readiness.evidenceHash)."
}

$browser = Read-EvidenceRecord $BrowserEvidencePath 'close-compliance-browser'
$recovery = Read-EvidenceRecord $RecoveryEvidencePath 'close-compliance-recovery'
$capacity = Read-EvidenceRecord $CapacityEvidencePath 'close-compliance-capacity'
$provider = Read-EvidenceRecord $ProviderScopeEvidencePath 'close-compliance-provider-scope'
$professional = Read-EvidenceRecord $ProfessionalReviewEvidencePath 'close-compliance-professional-review'

if (-not [string]::Equals($recovery.evidenceHash, $readiness.evidenceHash, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'Recovery evidence does not preserve the authoritative pre-recovery evidence hash.'
}
$missingSourceLinks = @($readiness.evidenceSourceLinks | Where-Object { $_ -notin @($recovery.sourceLinks) })
if ($missingSourceLinks.Count -gt 0) {
    throw "Recovery evidence does not preserve source links: $($missingSourceLinks -join ', ')."
}
if ($provider.capability -ne 'export_and_manual_evidence_only') {
    throw 'Provider-scope evidence must approve the implemented export_and_manual_evidence_only boundary.'
}

$evidenceFiles = @($matrixPath.Path, (Resolve-Path $BrowserEvidencePath).Path,
    (Resolve-Path $RecoveryEvidencePath).Path, (Resolve-Path $CapacityEvidencePath).Path,
    (Resolve-Path $ProviderScopeEvidencePath).Path, (Resolve-Path $ProfessionalReviewEvidencePath).Path)
$fileHashes = @($evidenceFiles | ForEach-Object {
    [pscustomobject]@{ path = $_; sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() }
})
$decision = [pscustomobject]@{
    schemaVersion = 1
    generatedUtc = [DateTime]::UtcNow.ToString('O')
    decision = 'go'
    companyId = $CompanyId.ToString('D')
    fiscalPeriodId = $FiscalPeriodId.ToString('D')
    readinessEvidenceHash = $readiness.evidenceHash
    readinessSourceLinks = @($readiness.evidenceSourceLinks)
    matrixEvidenceChecksum = $matrix.evidenceChecksum
    evidenceFiles = $fileHashes
    reviewers = @($browser.reviewer, $recovery.reviewer, $capacity.reviewer, $provider.reviewer, $professional.reviewer)
}

$resolvedOutput = [System.IO.Path]::GetFullPath($OutputPath, (Get-Location).Path)
$parent = Split-Path -Parent $resolvedOutput
if (-not [string]::IsNullOrWhiteSpace($parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
$decision | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resolvedOutput -Encoding utf8
$decision
