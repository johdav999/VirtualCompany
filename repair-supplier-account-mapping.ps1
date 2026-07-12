param(
    [string]$ServerInstance = "localhost\SQLEXPRESS",
    [string]$Database = "virtualcompany",
    [string]$Username = "sa",
    [string]$Password = $env:VC_SQL_SA_PASSWORD,
    [switch]$UseWindowsAuthentication,
    [string]$SupplierId,
    [string]$SupplierName,
    [string]$AccountCode,
    [switch]$ListOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not (Get-Command Invoke-Sqlcmd -ErrorAction SilentlyContinue)) {
    throw "Invoke-Sqlcmd is required. Install the SqlServer PowerShell module or SQL Server Management Studio tools."
}

function Invoke-VcSql {
    param([Parameter(Mandatory = $true)][string]$Query)

    $common = @{
        ServerInstance = $ServerInstance
        Database = $Database
        Query = $Query
    }

    if ((Get-Command Invoke-Sqlcmd).Parameters.ContainsKey("TrustServerCertificate")) {
        $common.TrustServerCertificate = $true
    }

    if ($UseWindowsAuthentication) {
        return Invoke-Sqlcmd @common
    }

    if ([string]::IsNullOrWhiteSpace($Password)) {
        throw "SQL password is required unless -UseWindowsAuthentication is supplied. Set VC_SQL_SA_PASSWORD or pass -Password."
    }

    return Invoke-Sqlcmd @common -Username $Username -Password $Password
}

function Escape-SqlLiteral {
    param([string]$Value)
    return $Value.Replace("'", "''")
}

Write-Host "Supplier account mappings in '$Database' on '$ServerInstance':"
Invoke-VcSql -Query @"
SELECT
    [id],
    [name],
    [counterparty_type],
    [default_account_mapping],
    CASE
        WHEN TRY_CONVERT(int, [default_account_mapping]) BETWEEN 4000 AND 8999 THEN 'expense'
        ELSE 'needs review'
    END AS [mapping_status]
FROM [finance_counterparties]
WHERE LOWER([counterparty_type]) IN ('supplier', 'vendor')
ORDER BY
    CASE WHEN TRY_CONVERT(int, [default_account_mapping]) BETWEEN 4000 AND 8999 THEN 1 ELSE 0 END,
    [name];
"@ | Format-Table -AutoSize

if ($ListOnly -or [string]::IsNullOrWhiteSpace($AccountCode)) {
    Write-Host "No changes made. Pass -SupplierId or -SupplierName with -AccountCode to update one supplier."
    exit 0
}

if ($AccountCode -notmatch '^\d+$' -or [int]$AccountCode -lt 4000 -or [int]$AccountCode -gt 8999) {
    throw "AccountCode must be a supplier expense account between 4000 and 8999."
}

if ([string]::IsNullOrWhiteSpace($SupplierId) -and [string]::IsNullOrWhiteSpace($SupplierName)) {
    throw "Pass either -SupplierId or -SupplierName before updating a supplier account mapping."
}

$where = if (-not [string]::IsNullOrWhiteSpace($SupplierId)) {
    "[id] = CONVERT(uniqueidentifier, '$(Escape-SqlLiteral $SupplierId)')"
} else {
    "[name] = N'$(Escape-SqlLiteral $SupplierName)'"
}

$preview = @(Invoke-VcSql -Query @"
SELECT [id], [name], [default_account_mapping]
FROM [finance_counterparties]
WHERE LOWER([counterparty_type]) IN ('supplier', 'vendor') AND $where;
"@)

if ($preview.Count -eq 0) {
    throw "No matching supplier was found."
}

if ($preview.Count -gt 1) {
    $preview | Format-Table -AutoSize
    throw "More than one supplier matched. Use -SupplierId for an exact update."
}

$preview | Format-Table -AutoSize
Invoke-VcSql -Query @"
UPDATE [finance_counterparties]
SET [default_account_mapping] = N'$(Escape-SqlLiteral $AccountCode)',
    [updated_utc] = SYSUTCDATETIME()
WHERE LOWER([counterparty_type]) IN ('supplier', 'vendor') AND $where;
"@ | Out-Null

Write-Host "Updated supplier default account mapping to $AccountCode."
