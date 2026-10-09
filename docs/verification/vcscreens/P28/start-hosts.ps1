$ErrorActionPreference='Stop'
$p28Root=(Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
foreach($p28Port in 5348,5108) {
    if(Get-NetTCPConnection -LocalPort $p28Port -State Listen -ErrorAction SilentlyContinue) {throw "Port $p28Port already has a listener; inspect its ownership before starting."}
}
$env:DOTNET_PROCESSOR_COUNT='1'
$env:DOTNET_TieredCompilation='0'
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ApiBaseUrl='http://localhost:5348/'
$env:DevelopmentAuth__Subject='p19-owner'
$env:DevelopmentAuth__Email='p19-owner@example.test'
$env:DevelopmentAuth__Provider='dev-header'
$p28ApiDll=Join-Path $p28Root 'tests/VirtualCompany.Workspace.Uat/bin/Debug/net9.0/VirtualCompany.Workspace.Uat.dll'
$p28WebDll=Join-Path $p28Root 'src/VirtualCompany.Web/bin/Debug/net9.0/VirtualCompany.Web.dll'
$p28Hosts=@{startedUtc=[DateTime]::UtcNow.ToString('o')}
$p28Api=Start-Process dotnet -ArgumentList @('"'+$p28ApiDll+'"','--urls','http://localhost:5348') -WorkingDirectory $p28Root -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $PSScriptRoot 'api-host-final.log') -RedirectStandardError (Join-Path $PSScriptRoot 'api-host-final-error.log')
$p28Hosts.api=@{pid=$p28Api.Id;dll=$p28ApiDll;port=5348}
$p28Hosts|ConvertTo-Json -Depth 4|Set-Content (Join-Path $PSScriptRoot 'owned-hosts.json')
$p28Web=Start-Process dotnet -ArgumentList @('"'+$p28WebDll+'"','--urls','http://localhost:5108') -WorkingDirectory (Join-Path $p28Root 'src/VirtualCompany.Web') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $PSScriptRoot 'web-host-final.log') -RedirectStandardError (Join-Path $PSScriptRoot 'web-host-final-error.log')
$p28Hosts.web=@{pid=$p28Web.Id;dll=$p28WebDll;port=5108}
$p28Hosts|ConvertTo-Json -Depth 4|Set-Content (Join-Path $PSScriptRoot 'owned-hosts.json')
$p28Hosts|ConvertTo-Json -Depth 4
