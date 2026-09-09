[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F-]{36}$')]
    [string]$TeamsAppId,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F-]{36}$')]
    [string]$BotApplicationId,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[0-9a-fA-F-]{36}$')]
    [string]$WebApplicationId,

    [Parameter(Mandatory = $true)]
    [string]$WebApplicationResource,

    [Parameter(Mandatory = $true)]
    [ValidateSet('single_tenant', 'multi_tenant')]
    [string]$TenantMode,

    [Parameter(Mandatory = $true)]
    [string[]]$AllowedTenantIds,

    [Parameter(Mandatory = $true)]
    [string]$PublicApiOrigin,

    [Parameter(Mandatory = $true)]
    [string]$WebOrigin,

    [Parameter(Mandatory = $true)]
    [string]$BotNotificationUrl,

    [Parameter(Mandatory = $true)]
    [string]$BotCallingCallbackUrl,

    [Parameter(Mandatory = $true)]
    [string]$AdminConsentRedirectUrl,

    [Parameter(Mandatory = $true)]
    [string]$ConfigurationUrl,

    [Parameter(Mandatory = $true)]
    [string]$SidePanelUrl,

    [Parameter(Mandatory = $true)]
    [string]$StageUrl,

    [ValidatePattern('^[0-9]+\.[0-9]+\.[0-9]+$')]
    [string]$PackageVersion = '1.0.0',

    [ValidateSet('disabled', 'teams_application_hosted', 'certified_provider')]
    [string]$MediaRoute = 'disabled',

    [switch]$MediaRouteApproved,
    [switch]$CallControlEnabled,
    [switch]$AudioEnabled,
    [switch]$SharedStageEnabled,
    [switch]$VisualMediaEnabled,
    [switch]$UseManagedIdentity,
    [string]$CertificateReference = '',
    [string]$OutputPath,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputPath))
{
    $OutputPath = Join-Path $repoRoot "artifacts\teams\alex-presenter-$PackageVersion.zip"
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputPath))
{
    $OutputPath = Join-Path $repoRoot $OutputPath
}

$settings = @{
    'TeamsPresenter__Enabled' = 'false'
    'TeamsPresenter__TeamsAppId' = $TeamsAppId
    'TeamsPresenter__BotApplicationId' = $BotApplicationId
    'TeamsPresenter__WebApplicationId' = $WebApplicationId
    'TeamsPresenter__WebApplicationResource' = $WebApplicationResource
    'TeamsPresenter__TenantMode' = $TenantMode
    'TeamsPresenter__PublicApiOrigin' = $PublicApiOrigin
    'TeamsPresenter__BotNotificationUrl' = $BotNotificationUrl
    'TeamsPresenter__BotCallingCallbackUrl' = $BotCallingCallbackUrl
    'TeamsPresenter__AdminConsentRedirectUrl' = $AdminConsentRedirectUrl
    'TeamsPresenter__WebOrigin' = $WebOrigin
    'TeamsPresenter__ConfigurationUrl' = $ConfigurationUrl
    'TeamsPresenter__SidePanelUrl' = $SidePanelUrl
    'TeamsPresenter__StageUrl' = $StageUrl
    'TeamsPresenter__MediaRoute' = $MediaRoute
    'TeamsPresenter__MediaRouteApproved' = $MediaRouteApproved.IsPresent.ToString().ToLowerInvariant()
    'TeamsPresenter__UseManagedIdentity' = $UseManagedIdentity.IsPresent.ToString().ToLowerInvariant()
    'TeamsPresenter__CertificateReference' = $CertificateReference
    'TeamsPresenter__PackageVersion' = $PackageVersion
    'TeamsPresenter__CallControlEnabled' = $CallControlEnabled.IsPresent.ToString().ToLowerInvariant()
    'TeamsPresenter__AudioEnabled' = $AudioEnabled.IsPresent.ToString().ToLowerInvariant()
    'TeamsPresenter__SharedStageEnabled' = $SharedStageEnabled.IsPresent.ToString().ToLowerInvariant()
    'TeamsPresenter__VisualMediaEnabled' = $VisualMediaEnabled.IsPresent.ToString().ToLowerInvariant()
}

foreach ($entry in $settings.GetEnumerator())
{
    [System.Environment]::SetEnvironmentVariable($entry.Key, [string]$entry.Value, [System.EnvironmentVariableTarget]::Process)
}
for ($index = 0; $index -lt $AllowedTenantIds.Count; $index++)
{
    [System.Environment]::SetEnvironmentVariable(
        "TeamsPresenter__AllowedTenantIds__$index",
        $AllowedTenantIds[$index],
        [System.EnvironmentVariableTarget]::Process)
}

$arguments = @(
    'run',
    '--project', (Join-Path $repoRoot 'src\VirtualCompany.Api\VirtualCompany.Api.csproj'),
    '--',
    'package-teams-presenter',
    '--output', $OutputPath
)
if ($Force)
{
    $arguments += '--force'
}

& dotnet @arguments
exit $LASTEXITCODE
