$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$webProject = Join-Path $repoRoot "src\VirtualCompany.Web\VirtualCompany.Web.csproj"

dotnet run --project $webProject --launch-profile http
