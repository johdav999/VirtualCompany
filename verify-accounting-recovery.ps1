[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [Guid]$CompanyId,
    [Uri]$ApiBaseUri = "http://localhost:5301/",
    [Nullable[Guid]]$FiscalPeriodId = $null,
    [switch]$VerifyObjectContent,
    [switch]$RequireReady,
    [string]$ExpectedChecksum,
    [string]$WriteChecksumPath,
    [string]$AccessToken = $env:VC_ACCOUNTING_OPERATOR_TOKEN
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($AccessToken))
{
    throw "Set VC_ACCOUNTING_OPERATOR_TOKEN or pass -AccessToken for an authorized accounting administrator."
}

$correlationId = "accounting-recovery-$([Guid]::NewGuid().ToString('N'))"
$headers = @{
    Authorization = "Bearer $AccessToken"
    "X-Correlation-ID" = $correlationId
}
$baseUri = $ApiBaseUri.AbsoluteUri.TrimEnd('/')
$operationsUri = "$baseUri/internal/companies/$($CompanyId.ToString('D'))/finance/accounting/operations"

if ($RequireReady)
{
    $operations = Invoke-RestMethod -Method Get -Uri $operationsUri -Headers $headers
    if ($operations.readiness.status -ne "ready")
    {
        $blockingSignals = $operations.readiness.signals |
            Where-Object { $_.status -ne "ready" } |
            ForEach-Object { "$($_.key)=$($_.status) ($($_.count))" }
        throw "Accounting readiness is '$($operations.readiness.status)': $($blockingSignals -join '; ')"
    }
}

$body = @{
    fiscalPeriodId = if ($FiscalPeriodId.HasValue) { $FiscalPeriodId.Value } else { $null }
    verifyObjectContent = [bool]$VerifyObjectContent
} | ConvertTo-Json

$result = Invoke-RestMethod `
    -Method Post `
    -Uri "$operationsUri/recovery-verification" `
    -Headers $headers `
    -ContentType "application/json" `
    -Body $body

if (-not $result.isValid)
{
    $issues = $result.issues | ForEach-Object {
        "$($_.reasonCode): $($_.entityType) $($_.entityId) - $($_.explanation)"
    }
    throw "Accounting recovery verification failed: $($issues -join '; ')"
}

if (-not [string]::IsNullOrWhiteSpace($ExpectedChecksum) -and
    -not [string]::Equals($ExpectedChecksum.Trim(), $result.evidenceChecksum, [StringComparison]::OrdinalIgnoreCase))
{
    throw "The restored accounting evidence checksum does not match the pre-backup checksum."
}

if (-not [string]::IsNullOrWhiteSpace($WriteChecksumPath))
{
    $parent = Split-Path -Parent $WriteChecksumPath
    if (-not [string]::IsNullOrWhiteSpace($parent))
    {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    Set-Content -LiteralPath $WriteChecksumPath -Value $result.evidenceChecksum -Encoding utf8NoBOM
}

Write-Host "Accounting recovery verification succeeded."
Write-Host "Company: $($CompanyId.ToString('D'))"
Write-Host "Journals: $($result.journalCount); Lines: $($result.lineCount); Evidence links: $($result.evidenceLinkCount)"
Write-Host "Object content verified: $($result.objectContentVerified)"
Write-Host "Evidence checksum: $($result.evidenceChecksum)"
