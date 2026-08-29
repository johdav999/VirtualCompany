[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [Guid]$CompanyId,
    [string]$ApiBaseUri = 'https://localhost:7183',
    [ValidateSet('small', 'medium')]
    [string]$Profile = 'small',
    [switch]$VerifyObjectContent,
    [switch]$RequireReady,
    [string]$ExpectedChecksum,
    [string]$WriteChecksumPath
)

$ErrorActionPreference = 'Stop'
$token = $env:VC_CONNECTED_BANKING_OPERATOR_TOKEN
if ([string]::IsNullOrWhiteSpace($token)) {
    throw 'Set VC_CONNECTED_BANKING_OPERATOR_TOKEN to a short-lived AccountingAdmin bearer token.'
}

$base = $ApiBaseUri.TrimEnd('/')
$route = "$base/api/companies/$($CompanyId.ToString('D'))/finance/connected-banking-readiness"
$headers = @{ Authorization = "Bearer $token" }

if ($RequireReady) {
    $readiness = Invoke-RestMethod -Method Get -Uri "${route}?profile=$Profile" -Headers $headers
    if (-not $readiness.isReady) {
        $blocking = @($readiness.checks | Where-Object { $_.status -in @('blocked', 'not_measured') })
        throw "Connected-banking readiness is not green. Blocking/not-measured checks: $($blocking.key -join ', ')."
    }
}

$body = @{
    verifyObjectContent = [bool]$VerifyObjectContent
    correlationId = "connected-banking-recovery-$([Guid]::NewGuid().ToString('N'))"
} | ConvertTo-Json
$result = Invoke-RestMethod -Method Post -Uri "$route/recovery-verification" -Headers $headers `
    -ContentType 'application/json' -Body $body

if (-not $result.isValid) {
    $reasonCodes = @($result.issues | ForEach-Object { $_.reasonCode }) -join ', '
    throw "Connected-banking recovery verification failed: $reasonCodes"
}

if (-not [string]::IsNullOrWhiteSpace($ExpectedChecksum) -and
    -not [string]::Equals($ExpectedChecksum, $result.evidenceChecksum,
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Recovery checksum mismatch. Expected $ExpectedChecksum but received $($result.evidenceChecksum)."
}

if (-not [string]::IsNullOrWhiteSpace($WriteChecksumPath)) {
    $resolved = [System.IO.Path]::GetFullPath($WriteChecksumPath, (Get-Location).Path)
    $parent = Split-Path -Parent $resolved
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $resolved -Encoding utf8
}

$result
