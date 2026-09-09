[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $ApiPackagePath,
    [Parameter(Mandatory)] [ValidatePattern('^[A-Fa-f0-9]{64}$')] [string] $ApiPackageSha256,
    [Parameter(Mandatory)] [ValidatePattern('^[a-zA-Z0-9-]+$')] [string] $KeyVaultName,
    [Parameter(Mandatory)] [string] $CertificateSecretName,
    [Parameter(Mandatory)] [string] $ConfigurationSecretName,
    [Parameter(Mandatory)] [string] $ServiceFqdn,
    [Parameter(Mandatory)] [ValidateRange(1024,65535)] [int] $MediaInternalPort,
    [Parameter(Mandatory)] [ValidateRange(1024,65535)] [int] $MediaPublicPort,
    [Parameter(Mandatory)] [guid] $ManagedIdentityClientId
)
function Stop-TeamsMediaHostForUpgrade {
    param([string] $ServiceName)
    $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existing -and $existing.Status -ne 'Stopped') {
        $upgrade = Invoke-WebRequest -UseBasicParsing -TimeoutSec 10 -Uri 'http://127.0.0.1:8080/health/media-host/upgrade'
        if ($upgrade.StatusCode -ne 200) { throw 'Drain the existing host and wait for provider-confirmed termination before upgrading.' }
        Stop-Service -Name $ServiceName -ErrorAction Stop
        (Get-Service $ServiceName).WaitForStatus('Stopped',[TimeSpan]::FromSeconds(120))
    }
}
function Register-TeamsMediaService {
    param([string] $ServiceName, [string] $Binary, [bool] $Exists)
    if ($Exists) { & sc.exe config $ServiceName binPath= $Binary start= auto obj= LocalSystem | Out-Null }
    else { & sc.exe create $ServiceName binPath= $Binary start= auto obj= LocalSystem | Out-Null }
    if ($LASTEXITCODE -ne 0) { throw 'SCM registration failed.' }
    & sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/15000/restart/60000 | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'SCM recovery configuration failed.' }
}
function Get-UniformVmssInstanceId {
    param([Parameter(Mandatory)] $Compute)
    $pattern = '^/subscriptions/([^/]+)/resourceGroups/([^/]+)/providers/Microsoft.Compute/virtualMachineScaleSets/([^/]+)/virtualMachines/([0-9]+)$'
    if ([string]$Compute.resourceId -notmatch $pattern) { throw 'Uniform VMSS resource identity is unavailable.' }
    $instance = $Matches[4]
    if ($Matches[1] -ne $Compute.subscriptionId -or $Matches[2] -ne $Compute.resourceGroupName -or $Matches[3] -ne $Compute.vmScaleSetName) {
        throw 'VMSS metadata is inconsistent.'
    }
    $vmGuid = [guid]::Empty
    if (-not [guid]::TryParse([string]$Compute.vmId, [ref]$vmGuid) -or $vmGuid -eq [guid]::Empty) { throw 'VM GUID is unavailable.' }
    return $instance
}
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT -or -not [Environment]::Is64BitOperatingSystem) { throw 'A 64-bit Windows Server host is required.' }
if ($MediaInternalPort -ne $MediaPublicPort) { throw 'Instance public IP media ports must match without NAT.' }
$actualHash = (Get-FileHash -LiteralPath $ApiPackagePath -Algorithm SHA256).Hash
if ($actualHash -ne $ApiPackageSha256.ToUpperInvariant()) { throw 'API artifact SHA-256 verification failed.' }
$metadataHeaders = @{ Metadata = 'true' }
# Windows PowerShell 5.1 has no -NoProxy; IMDS must never use a proxy.
[System.Net.WebRequest]::DefaultWebProxy = $null
$compute = Invoke-RestMethod -TimeoutSec 10 -Headers $metadataHeaders -Uri 'http://169.254.169.254/metadata/instance/compute?api-version=2021-02-01'
$network = Invoke-RestMethod -TimeoutSec 10 -Headers $metadataHeaders -Uri 'http://169.254.169.254/metadata/instance/network/interface/0/ipv4/ipAddress/0?api-version=2021-02-01'
# resourceId identifies a Uniform scale-set VM; vmId is a GUID and is NOT its instanceId.
$instanceId = Get-UniformVmssInstanceId -Compute $compute
$publicIp = $null
if (-not [Net.IPAddress]::TryParse([string]$network.publicIpAddress, [ref]$publicIp) -or
    $publicIp.AddressFamily -ne [Net.Sockets.AddressFamily]::InterNetwork) { throw 'Instance public IPv4 is unavailable.' }
$token = Invoke-RestMethod -TimeoutSec 10 -Headers $metadataHeaders -Uri "http://169.254.169.254/metadata/identity/oauth2/token?api-version=2018-02-01&resource=https%3A%2F%2Fvault.azure.net&client_id=$ManagedIdentityClientId"
$vaultHeaders = @{ Authorization = "Bearer $($token.access_token)" }
$certificateSecret = Invoke-RestMethod -TimeoutSec 30 -Headers $vaultHeaders -Uri "https://$KeyVaultName.vault.azure.net/secrets/$CertificateSecretName?api-version=7.4"
$configurationSecret = Invoke-RestMethod -TimeoutSec 30 -Headers $vaultHeaders -Uri "https://$KeyVaultName.vault.azure.net/secrets/$ConfigurationSecretName?api-version=7.4"
$configuration = [string]$configurationSecret.value | ConvertFrom-Json
foreach ($name in @('BotApplicationId','TeamsAppId','WebApplicationId','AllowedTenantId','KeyRingPath','ObjectStorageRootPath')) {
    if (-not $configuration.PSObject.Properties[$name] -or [string]::IsNullOrWhiteSpace([string]$configuration.$name)) { throw "Missing host configuration: $name." }
}
# UNC durable storage is a deployment prerequisite; never default to the replaceable VM OS disk.
foreach ($name in @('KeyRingPath','ObjectStorageRootPath')) {
    if (-not ([string]$configuration.$name).StartsWith('\\')) { throw "$name must identify durable shared UNC storage." }
}
$pfxBytes = [Convert]::FromBase64String([string]$certificateSecret.value)
try {
    $certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new($pfxBytes, $null,
        [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::MachineKeySet -bor
        [Security.Cryptography.X509Certificates.X509KeyStorageFlags]::PersistKeySet)
    if (-not $certificate.HasPrivateKey -or $certificate.NotAfter.ToUniversalTime() -le [datetime]::UtcNow.AddDays(7)) { throw 'TLS certificate is missing its key or expires within seven days.' }
    if ($certificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::DnsName,$false) -ne $ServiceFqdn) { throw 'TLS certificate DNS name must match ServiceFqdn.' }
    if (-not $certificate.Verify()) { throw 'TLS certificate trust-chain validation failed.' }
    $store = [Security.Cryptography.X509Certificates.X509Store]::new('My','LocalMachine')
    try { $store.Open('ReadWrite'); $store.Add($certificate) } finally { $store.Dispose() }
} finally { [Array]::Clear($pfxBytes,0,$pfxBytes.Length) }

$root = 'C:\VirtualCompany'
$releasePath = Join-Path $root "releases\$($actualHash.Substring(0,16).ToLowerInvariant())-$([guid]::NewGuid().ToString('N').Substring(0,8))"
$serviceName = 'VirtualCompanyTeamsMedia'
$service = Get-Service -Name $serviceName -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $releasePath -Force | Out-Null
& icacls.exe $root /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Failed to protect deployment directory.' }
Expand-Archive -LiteralPath $ApiPackagePath -DestinationPath $releasePath -Force
foreach ($file in @('VirtualCompany.Api.exe','VirtualCompany.Api.dll','coreclr.dll','hostfxr.dll')) {
    if (-not (Test-Path -LiteralPath (Join-Path $releasePath $file))) { throw 'A self-contained win-x64 API publish is required.' }
}
$hostSettings = @{
    AzureKeyVault = @{ Uri = "https://$KeyVaultName.vault.azure.net/"; ManagedIdentityClientId = "$ManagedIdentityClientId" }
    PlatformSecrets = @{ ManagedIdentityClientId = "$ManagedIdentityClientId" }
    DataProtection = @{ KeyRingPath = [string]$configuration.KeyRingPath }
    CompanyDocuments = @{ Storage = @{ RootPath = [string]$configuration.ObjectStorageRootPath } }
    TeamsPresenter = @{
        CallControlHostId = "$($compute.vmScaleSetName)-$($compute.vmId)"
        MediaHostPublicIp = "$publicIp"; MediaHostServiceFqdn = $ServiceFqdn
        MediaHostInternalPort = $MediaInternalPort; MediaHostPublicPort = $MediaPublicPort
        MediaCertificateThumbprint = $certificate.Thumbprint; MediaHostDeploymentApproved = $true
        AzureScheduledEventsEnabled = $true; VmssInstanceProtectionEnabled = $true
        VmssSubscriptionId = [string]$compute.subscriptionId; VmssResourceGroup = [string]$compute.resourceGroupName
        VmssName = [string]$compute.vmScaleSetName; VmssInstanceId = $instanceId
        ManagedIdentityClientId = "$ManagedIdentityClientId"
        AllowedTenantIds = @([string]$configuration.AllowedTenantId)
    }
    Kestrel = @{
        Endpoints = @{ Https = @{ Url = 'https://+:8443' }; Operator = @{ Url = 'http://127.0.0.1:8080' } }
        Certificates = @{ Default = @{ Subject = $certificate.Subject; Store = 'My'; Location = 'LocalMachine' } }
    }
}
foreach ($name in @('Enabled','CallControlEnabled','AudioEnabled','SharedStageEnabled','MediaRouteApproved','MediaHostPublicReachabilityApproved','MediaRoute','BotApplicationId','TeamsAppId','WebApplicationId')) {
    if ($configuration.PSObject.Properties[$name]) { $hostSettings.TeamsPresenter[$name] = $configuration.$name }
}
$hostSettings | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $releasePath 'appsettings.MediaHost.json') -Encoding UTF8
foreach ($rule in @(@{Name='Virtual Company Teams callback';Protocol='TCP';Port=8443},
    @{Name='Virtual Company Teams media TCP';Protocol='TCP';Port=$MediaInternalPort},
    @{Name='Virtual Company Teams media UDP';Protocol='UDP';Port=$MediaInternalPort})) {
    if (-not (Get-NetFirewallRule -DisplayName $rule.Name -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName $rule.Name -Direction Inbound -Action Allow -Protocol $rule.Protocol -LocalPort $rule.Port | Out-Null
    }
}
$binary = '"' + (Join-Path $releasePath 'VirtualCompany.Api.exe') + '" --environment Production --contentRoot "' + $releasePath + '"'
# LocalSystem can read the machine certificate key. Restrict host access and grant its
# computer identity access to the shared storage; do not embed storage credentials.
Stop-TeamsMediaHostForUpgrade -ServiceName $serviceName
Register-TeamsMediaService -ServiceName $serviceName -Binary $binary -Exists ([bool]$service)
Start-Service $serviceName -ErrorAction Stop
(Get-Service $serviceName).WaitForStatus('Running',[TimeSpan]::FromSeconds(30))
$deadline = [datetime]::UtcNow.AddSeconds(30)
$healthy = $false
do {
    try { $healthy = (Invoke-WebRequest -UseBasicParsing -TimeoutSec 2 -Uri 'http://127.0.0.1:8080/health/media-host/live').StatusCode -eq 200 } catch { }
    if (-not $healthy) { Start-Sleep -Seconds 1 }
} while (-not $healthy -and [datetime]::UtcNow -lt $deadline)
if (-not $healthy) { throw 'Service did not reach signaling liveness; inspect service logs before retrying.' }
Write-Output 'Installed self-contained Teams API service. Media admission requires separate readiness verification.'
