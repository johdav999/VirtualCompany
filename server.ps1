Get-Process dotnet -ErrorAction SilentlyContinue | Stop-Process -Force

$projectDir = "src\VirtualCompany.Api"
$apiDll = "bin\Debug\net9.0\VirtualCompany.Api.dll"

if (-not (Test-Path (Join-Path $projectDir $apiDll)))
{
    dotnet build "$projectDir\VirtualCompany.Api.csproj" -v minimal
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}

$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ASPNETCORE_URLS = "http://localhost:5301"

Push-Location $projectDir
try
{
    dotnet $apiDll
}
finally
{
    Pop-Location
}
