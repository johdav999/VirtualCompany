Get-Process dotnet -ErrorAction SilentlyContinue |
    Where-Object { $_.Path -like (Join-Path $PSScriptRoot '*') } |
    Stop-Process -Force -ErrorAction SilentlyContinue

$sqlPassword = if ([string]::IsNullOrWhiteSpace($env:VC_SQL_SA_PASSWORD)) { "YourStrong!Passw0rd" } else { $env:VC_SQL_SA_PASSWORD }
$serverInstance = if ([string]::IsNullOrWhiteSpace($env:VC_SQL_SERVER_INSTANCE)) { "localhost\SQLEXPRESS" } else { $env:VC_SQL_SERVER_INSTANCE }
$databaseName = if ([string]::IsNullOrWhiteSpace($env:VC_SQL_DATABASE)) { "virtualcompany" } else { $env:VC_SQL_DATABASE }
$useWindowsAuthentication = $env:VC_SQL_USE_WINDOWS_AUTH -in @("1", "true", "True", "TRUE", "yes", "Yes", "YES")

$projectDir = "src\VirtualCompany.Api"
$apiDll = "bin\Debug\net9.0\VirtualCompany.Api.dll"

dotnet build "$projectDir\VirtualCompany.Api.csproj" -v minimal
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://localhost:5301"
if ($useWindowsAuthentication)
{
    $env:ConnectionStrings__VirtualCompanyDb = "Server=$serverInstance;Database=$databaseName;Trusted_Connection=True;TrustServerCertificate=True;Encrypt=False;MultipleActiveResultSets=True"
}
else
{
    $env:ConnectionStrings__VirtualCompanyDb = "Server=$serverInstance;Database=$databaseName;User Id=sa;Password=$sqlPassword;TrustServerCertificate=True;Encrypt=False;MultipleActiveResultSets=True"
}

Push-Location $projectDir
try
{
    dotnet $apiDll
}
finally
{
    Pop-Location
}
