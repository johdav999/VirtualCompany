$ErrorActionPreference='Stop'
$p30Root=(Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
foreach($p30Port in 5348,5108) {
    if(Get-NetTCPConnection -LocalPort $p30Port -State Listen -ErrorAction SilentlyContinue) {throw "Port $p30Port already has a listener; inspect its ownership before starting."}
}
$env:DOTNET_PROCESSOR_COUNT='1'
$env:DOTNET_TieredCompilation='0'
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ApiBaseUrl='http://localhost:5348/'
$env:DevelopmentAuth__Subject='p19-owner'
$env:DevelopmentAuth__Email='p19-owner@example.test'
$env:DevelopmentAuth__Provider='dev-header'
$p30ApiDll=Join-Path $p30Root 'tests/VirtualCompany.Workspace.Uat/bin/Debug/net9.0/VirtualCompany.Workspace.Uat.dll'
$p30WebDll=Join-Path $p30Root 'src/VirtualCompany.Web/bin/Debug/net9.0/VirtualCompany.Web.dll'
$p30Hosts=@{startedUtc=[DateTime]::UtcNow.ToString('o')}
$p30Api=Start-Process dotnet -ArgumentList @('"'+$p30ApiDll+'"','--urls','http://localhost:5348') -WorkingDirectory $p30Root -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $PSScriptRoot 'api-host-final.log') -RedirectStandardError (Join-Path $PSScriptRoot 'api-host-final-error.log')
$p30Hosts.api=@{pid=$p30Api.Id;dll=$p30ApiDll;port=5348}
$p30Hosts|ConvertTo-Json -Depth 4|Set-Content (Join-Path $PSScriptRoot 'owned-hosts.json')
$p30Web=Start-Process dotnet -ArgumentList @('"'+$p30WebDll+'"','--urls','http://localhost:5108') -WorkingDirectory (Join-Path $p30Root 'src/VirtualCompany.Web') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $PSScriptRoot 'web-host-final.log') -RedirectStandardError (Join-Path $PSScriptRoot 'web-host-final-error.log')
$p30Hosts.web=@{pid=$p30Web.Id;dll=$p30WebDll;port=5108}
$p30Hosts|ConvertTo-Json -Depth 4|Set-Content (Join-Path $PSScriptRoot 'owned-hosts.json')
$p30Hosts|ConvertTo-Json -Depth 4


