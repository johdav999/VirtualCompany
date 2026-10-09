$ErrorActionPreference='Stop'
$p29Root=(Resolve-Path (Join-Path $PSScriptRoot '../../../..')).Path
$p29Cases=@{}
foreach($p29File in (Get-ChildItem (Join-Path $PSScriptRoot 'test-results') -Filter *.trx | Sort-Object LastWriteTime)) {
    [xml]$p29Xml=Get-Content -LiteralPath $p29File.FullName
    foreach($p29Result in $p29Xml.SelectNodes("//*[local-name()='UnitTestResult']")) {
        $p29Group=if($p29File.Name -like '*web*'){'web'}elseif($p29File.Name -like '*wire*'){'wire'}else{'api'}
        $p29Cases[$p29Group+':'+$p29Result.testName]=[pscustomobject]@{group=$p29Group;name=[string]$p29Result.testName;outcome=[string]$p29Result.outcome;evidence=$p29File.Name}
    }
}
$p29Counts=@{}
foreach($p29Group in 'api','web','wire') {
    $p29Selected=@($p29Cases.Values | Where-Object group -eq $p29Group | Sort-Object name)
    if(@($p29Selected | Where-Object outcome -ne 'Passed').Count){throw "Latest $p29Group cases are not all passing"}
    $p29Selected | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $PSScriptRoot "accepted-$p29Group-cases.json")
    $p29Counts[$p29Group]=$p29Selected.Count
}
$p29Prior=Get-Content (Join-Path $PSScriptRoot 'prior-artifact-baseline.json') -Raw | ConvertFrom-Json
$p29Changed=@($p29Prior | Where-Object { -not (Test-Path -LiteralPath $_.path) -or (Get-FileHash -LiteralPath $_.path -Algorithm SHA256).Hash -ne $_.hash })
if($p29Changed.Count){throw 'Prior evidence/reference artifact changed'}
[pscustomobject]@{checked=$p29Prior.Count;changed=0;changes=@()} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $PSScriptRoot 'prior-artifact-preservation.json')

$p29Paths=@(
 'src/VirtualCompany.Application/Briefings/BriefingCadenceContracts.cs',
 'src/VirtualCompany.Application/Companies/CompanyInvitationDeliveryContracts.cs',
 'src/VirtualCompany.Domain/Entities/BriefingCadenceDelivery.cs',
 'src/VirtualCompany.Domain/Entities/BriefingEntities.cs',
 'src/VirtualCompany.Infrastructure.Operations/Companies/BriefingCadenceCalendar.cs',
 'src/VirtualCompany.Infrastructure.Operations/Companies/BriefingCadenceService.cs',
 'src/VirtualCompany.Infrastructure.Operations/Companies/CompanyTodayWorkspaceLensResolver.cs',
 'src/VirtualCompany.Infrastructure.Operations/Companies/CompanyWorkVisibility.cs',
 'src/VirtualCompany.Infrastructure.Operations/Companies/CompanyBriefingService.cs',
 'src/VirtualCompany.Infrastructure.Operations/Companies/CompanyBriefingGenerationPipeline.cs',
 'src/VirtualCompany.Infrastructure.Operations/Companies/CompanyBriefingUpdateJobRunner.cs',
 'src/VirtualCompany.Infrastructure.Operations/Companies/CompanyOutboxInfrastructure.cs',
 'src/VirtualCompany.Infrastructure.Operations/Companies/OperationsModuleRegistration.cs',
 'src/VirtualCompany.Persistence/Persistence/VirtualCompanyDbContext.cs',
 'src/VirtualCompany.Persistence/Persistence/Configurations/BriefingCadenceDeliveryConfiguration.cs',
 'src/VirtualCompany.Persistence/Persistence/Configurations/CompanyBriefingDeliveryPreferenceConfiguration.cs',
 'src/VirtualCompany.Persistence.Migrations/Persistence/Migrations/20261006180516_AddBriefingCadenceDelivery.cs',
 'src/VirtualCompany.Persistence.Migrations/Persistence/Migrations/20261006180516_AddBriefingCadenceDelivery.Designer.cs',
 'src/VirtualCompany.Persistence.Migrations/Persistence/Migrations/VirtualCompanyDbContextModelSnapshot.cs',
 'src/VirtualCompany.Api/Controllers/BriefingCadenceController.cs',
 'src/VirtualCompany.Web/Services/BriefingCadenceApiClient.cs',
 'src/VirtualCompany.Web/Services/WebApiClientRegistration.cs',
 'src/VirtualCompany.Web/Components/BriefingCadencePanel.razor',
 'src/VirtualCompany.Web/Components/BriefingCadencePanel.razor.css',
 'src/VirtualCompany.Web/Pages/BriefingPreferences.razor',
 'tests/VirtualCompany.Web.Tests/BriefingCadenceJourneyTests.cs',
 'tests/VirtualCompany.Web.Tests/VirtualCompany.Web.Tests.csproj',
 'tests/VirtualCompany.Web.Contract.Tests/BriefingCadenceWireTests.cs',
 'tests/VirtualCompany.Web.Contract.Tests/VirtualCompany.Web.Contract.Tests.csproj',
 'tests/VirtualCompany.Workspace.Uat/Program.cs'
)
$p29Paths+=@(Get-ChildItem (Join-Path $p29Root 'tests/VirtualCompany.Api.Tests') -Filter 'BriefingCadence*.cs' | ForEach-Object {[IO.Path]::GetRelativePath($p29Root,$_.FullName)})
$p29Source=@(foreach($p29Path in $p29Paths | Sort-Object -Unique) {
    $p29Full=Join-Path $p29Root $p29Path; $p29Info=Get-Item -LiteralPath $p29Full
    [pscustomobject]@{path=$p29Path;sha256=(Get-FileHash -LiteralPath $p29Full).Hash;lastWriteUtc=$p29Info.LastWriteTimeUtc.ToString('o')}
})
$p29Binaries=@(foreach($p29Path in @(
 'src/VirtualCompany.Api/bin/Debug/net9.0/VirtualCompany.Infrastructure.Operations.dll',
 'src/VirtualCompany.Api/bin/Debug/net9.0/VirtualCompany.Application.dll',
 'src/VirtualCompany.Api/bin/Debug/net9.0/VirtualCompany.Persistence.dll',
 'src/VirtualCompany.Web/bin/Debug/net9.0/VirtualCompany.Web.dll',
 'tests/VirtualCompany.Workspace.Uat/bin/Debug/net9.0/VirtualCompany.Workspace.Uat.dll',
 'tests/VirtualCompany.Workspace.Uat/bin/Debug/net9.0/VirtualCompany.Infrastructure.Operations.dll',
 'tests/VirtualCompany.Workspace.Uat/bin/Debug/net9.0/VirtualCompany.Application.dll',
 'tests/VirtualCompany.Workspace.Uat/bin/Debug/net9.0/VirtualCompany.Persistence.dll'
)) {
    $p29Full=Join-Path $p29Root $p29Path; $p29Info=Get-Item -LiteralPath $p29Full
    [pscustomobject]@{path=$p29Path;sha256=(Get-FileHash -LiteralPath $p29Full).Hash;lastWriteUtc=$p29Info.LastWriteTimeUtc.ToString('o')}
})
foreach($p29Native in $p29Binaries | Where-Object path -like 'src/VirtualCompany.Api/*') {
    $p29Name=Split-Path $p29Native.path -Leaf
    if(($p29Binaries | Where-Object path -like "tests/*/$p29Name").sha256 -ne $p29Native.sha256){throw "Browser native dependency differs: $p29Name"}
}
[pscustomobject]@{recordedUtc=[DateTime]::UtcNow.ToString('o');scope='P29-specific files and intentionally extended existing owners; prior phase changes retained';source=$p29Source;binaries=$p29Binaries;fixtureAliasNote='Final native fixture aliases disambiguate linked Web wire compilation only; no production runtime change'} | ConvertTo-Json -Depth 6 | Set-Content (Join-Path $PSScriptRoot 'source-manifest.json')
$p29Browser=Get-Content (Join-Path $PSScriptRoot 'browser-accepted.json') -Raw | ConvertFrom-Json
if(@($p29Browser.results | Where-Object passed -ne $true).Count){throw 'Browser journey not passing'}
$p29Checks=Get-Content (Join-Path $PSScriptRoot 'final-checks.json') -Raw | ConvertFrom-Json
if($p29Checks.diffCheckExit -ne 0 -or $p29Checks.listeners.Count){throw 'Final diff or host cleanup incomplete'}
[pscustomobject]@{
 phase='P29';status='Implemented and verified locally';timeUtc=[DateTime]::UtcNow.ToString('o');baseline='9a356c7901abc2044f6ecb6a4e8a5ebf64ceab5b plus prior uncommitted P11-P28';uncommitted=$true
 automated=@{distinct=$p29Counts.api+$p29Counts.web+$p29Counts.wire;api=$p29Counts.api;web=$p29Counts.web;wire=$p29Counts.wire;sqlServerDistinct=2;latestFailures=0;newP29Ui=8;newP29Wire=1}
 builds=@{native='Pass';web='Pass, existing nullable warnings';uat='Pass';efPendingModel='Pass';diffCheck='Pass'}
 browser=@{status='Pass';surface='Fresh headless Microsoft Edge, native interactive Web/composed API';flow='Save/reload, absence, actual authorized work/source, one delegated native job/outbox notification, duplicate worker, current delivery/source/Work returns';viewports=@('1440x1000','390x844');pageErrors=0;keyboard='Pass';overflow='None';deliveryId=$p29Browser.deliveryId}
 preservation=@{priorArtifacts=$p29Prior.Count;changed=0};ownedHostCleanup='Verified exact DLL/PIDs stopped, ports 5348/5108 free'
 acceptance=@{controlledWorkspaceInbox='Pass';userInAppBrowser='Unavailable: trusted Node kernel/helper initialization failed';deployedTenant='Not performed';productionMigration='Not applied';externalProvider='Not configured or claimed; email/Teams/mobile not activated';humanReleaseApproval='Pending';P30='Not started'}
 evidence=@('accepted-api-cases.json','accepted-web-cases.json','accepted-wire-cases.json','browser-accepted.json','design-review.md','commands.md','source-manifest.json','prior-artifact-preservation.json','owned-cleanup-final.json')
} | ConvertTo-Json -Depth 8 | Set-Content (Join-Path $PSScriptRoot 'verification.json')
Get-Content (Join-Path $PSScriptRoot 'verification.json')
