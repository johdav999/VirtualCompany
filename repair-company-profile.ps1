param(
    [string]$ServerInstance = "localhost\SQLEXPRESS",
    [string]$Database = "virtualcompany",
    [string]$Username = "sa",
    [string]$Password = $env:VC_SQL_SA_PASSWORD,
    [switch]$UseWindowsAuthentication,
    [Guid]$CompanyId,
    [string]$CompanyName = "VC",
    [string]$Language = "en",
    [string]$ComplianceRegion = "EU",
    [switch]$WhatIf
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Get-Command Invoke-Sqlcmd -ErrorAction SilentlyContinue)) {
    throw "Invoke-Sqlcmd is required. Install the SqlServer PowerShell module or SQL Server Management Studio tools."
}

function Escape-SqlLiteral {
    param([Parameter(Mandatory = $true)][string]$Value)
    return $Value.Replace("'", "''")
}

function Invoke-VcSql {
    param([Parameter(Mandatory = $true)][string]$Query)

    $arguments = @{
        ServerInstance = $ServerInstance
        Database = $Database
        Query = $Query
    }
    if ((Get-Command Invoke-Sqlcmd).Parameters.ContainsKey("TrustServerCertificate")) {
        $arguments.TrustServerCertificate = $true
    }

    if ($UseWindowsAuthentication) {
        return Invoke-Sqlcmd @arguments
    }

    if ([string]::IsNullOrWhiteSpace($Password)) {
        throw "SQL password is required unless -UseWindowsAuthentication is supplied. Set VC_SQL_SA_PASSWORD or pass -Password."
    }

    return Invoke-Sqlcmd @arguments -Username $Username -Password $Password
}

$selector = if ($CompanyId -ne [Guid]::Empty) {
    "[Id] = '$($CompanyId.ToString("D"))'"
} else {
    "[Name] = N'$(Escape-SqlLiteral $CompanyName)'"
}
$languageValue = Escape-SqlLiteral $Language
$regionValue = Escape-SqlLiteral $ComplianceRegion

$companies = @(Invoke-VcSql -Query @"
SELECT [Id], [Name], [Industry], [BusinessType], [Timezone], [Currency], [Language], [ComplianceRegion]
FROM [companies]
WHERE $selector;
"@)

if ($companies.Count -eq 0) {
    throw "No company matched the supplied CompanyId or CompanyName."
}
if ($companies.Count -gt 1) {
    throw "More than one company matched CompanyName '$CompanyName'. Run again with -CompanyId."
}

Write-Host "Current company profile:"
$companies | Format-Table -AutoSize

if ($WhatIf) {
    Write-Host "WhatIf: no changes were made."
    exit 0
}

Invoke-VcSql -Query @"
UPDATE [companies]
SET [Language] = N'$languageValue',
    [ComplianceRegion] = N'$regionValue',
    [OnboardingStateJson] = CASE
        WHEN ISJSON([OnboardingStateJson]) = 1 THEN
            JSON_MODIFY(JSON_MODIFY([OnboardingStateJson], '$.language', N'$languageValue'), '$.complianceRegion', N'$regionValue')
        ELSE [OnboardingStateJson]
    END,
    [settings_json] = JSON_MODIFY(
        JSON_MODIFY(
            CASE WHEN ISJSON([settings_json]) = 1 THEN [settings_json] ELSE N'{}' END,
            '$.onboarding.language',
            N'$languageValue'),
        '$.onboarding.complianceRegion',
        N'$regionValue'),
    [UpdatedUtc] = SYSUTCDATETIME()
WHERE $selector;

SELECT [Id], [Name], [Industry], [BusinessType], [Timezone], [Currency], [Language], [ComplianceRegion]
FROM [companies]
WHERE $selector;
"@ | Format-Table -AutoSize

Write-Host "Company language and compliance region were updated in '$Database' on '$ServerInstance'."
