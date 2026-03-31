Get-Process dotnet -ErrorAction SilentlyContinue | Stop-Process -Force
dotnet run --project src\VirtualCompany.Api --launch-profile https
