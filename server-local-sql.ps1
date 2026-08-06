$serverInstance = if ([string]::IsNullOrWhiteSpace($env:VC_SQL_SERVER_INSTANCE)) { "lpc:.\SQLEXPRESS" } else { $env:VC_SQL_SERVER_INSTANCE }
$databaseName = if ([string]::IsNullOrWhiteSpace($env:VC_SQL_DATABASE)) { "virtualcompany" } else { $env:VC_SQL_DATABASE }
$useSqlAuthentication = $env:VC_SQL_USE_SQL_AUTH -in @("1", "true", "True", "TRUE", "yes", "Yes", "YES")

if (-not $useSqlAuthentication)
{
    $connectionString = "Server=$serverInstance;Database=$databaseName;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;MultipleActiveResultSets=False"
}
else
{
    if ([string]::IsNullOrWhiteSpace($env:VC_SQL_SA_PASSWORD))
    {
        throw "VC_SQL_SA_PASSWORD must be set when VC_SQL_USE_SQL_AUTH is enabled."
    }

    $sqlPassword = $env:VC_SQL_SA_PASSWORD
    $connectionString = "Server=$serverInstance;Database=$databaseName;User Id=sa;Password=$sqlPassword;TrustServerCertificate=True;Encrypt=False;MultipleActiveResultSets=False"
}

& (Join-Path $PSScriptRoot "run-api.ps1") -ConnectionString $connectionString
exit $LASTEXITCODE
