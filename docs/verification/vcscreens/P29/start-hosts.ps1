$ErrorActionPreference='Stop'
$p29Root=(Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
foreach($p29Port in 5348,5108) {
    if(Get-NetTCPConnection -LocalPort $p29Port -State Listen -ErrorAction SilentlyContinue) {throw "Port $p29Port already has a listener; inspect its ownership before starting."}
}
$env:DOTNET_PROCESSOR_COUNT='1'
$env:DOTNET_TieredCompilation='0'
$env:ASPNETCORE_ENVIRONMENT='Development'
$env:ApiBaseUrl='http://localhost:5348/'
$env:DevelopmentAuth__Subject='p19-owner'
$env:DevelopmentAuth__Email='p19-owner@example.test'
$env:DevelopmentAuth__Provider='dev-header'
$p29ApiDll=Join-Path $p29Root 'tests/VirtualCompany.Workspace.Uat/bin/Debug/net9.0/VirtualCompany.Workspace.Uat.dll'
$p29WebDll=Join-Path $p29Root 'src/VirtualCompany.Web/bin/Debug/net9.0/VirtualCompany.Web.dll'
$p29Hosts=@{startedUtc=[DateTime]::UtcNow.ToString('o')}
$p29Api=Start-Process dotnet -ArgumentList @('"'+$p29ApiDll+'"','--urls','http://localhost:5348') -WorkingDirectory $p29Root -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $PSScriptRoot 'api-host-final.log') -RedirectStandardError (Join-Path $PSScriptRoot 'api-host-final-error.log')
$p29Hosts.api=@{pid=$p29Api.Id;dll=$p29ApiDll;port=5348}
$p29Hosts|ConvertTo-Json -Depth 4|Set-Content (Join-Path $PSScriptRoot 'owned-hosts.json')
$p29Web=Start-Process dotnet -ArgumentList @('"'+$p29WebDll+'"','--urls','http://localhost:5108') -WorkingDirectory (Join-Path $p29Root 'src/VirtualCompany.Web') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $PSScriptRoot 'web-host-final.log') -RedirectStandardError (Join-Path $PSScriptRoot 'web-host-final-error.log')
$p29Hosts.web=@{pid=$p29Web.Id;dll=$p29WebDll;port=5108}
$p29Hosts|ConvertTo-Json -Depth 4|Set-Content (Join-Path $PSScriptRoot 'owned-hosts.json')
$p29Hosts|ConvertTo-Json -Depth 4

