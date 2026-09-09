namespace VirtualCompany.Infrastructure.Sales;

public sealed class TeamsPresenterOptions
{
    public const string SectionName = "TeamsPresenter";

    public bool Enabled { get; set; }
    public string TeamsAppId { get; set; } = string.Empty;
    public string BotApplicationId { get; set; } = string.Empty;
    public string WebApplicationId { get; set; } = string.Empty;
    public string WebApplicationResource { get; set; } = string.Empty;
    public string TenantMode { get; set; } = "single_tenant";
    public string[] AllowedTenantIds { get; set; } = [];
    public string PublicApiOrigin { get; set; } = string.Empty;
    public string BotNotificationUrl { get; set; } = string.Empty;
    public string BotCallingCallbackUrl { get; set; } = string.Empty;
    public string WebOrigin { get; set; } = string.Empty;
    public string ConfigurationUrl { get; set; } = string.Empty;
    public string SidePanelUrl { get; set; } = string.Empty;
    public string StageUrl { get; set; } = string.Empty;
    public string MediaRoute { get; set; } = "disabled";
    public bool MediaRouteApproved { get; set; }
    public bool UseManagedIdentity { get; set; }
    public string CredentialMode { get; set; } = string.Empty;
    public string ManagedIdentityClientId { get; set; } = string.Empty;
    public string WorkloadIdentityTokenFile { get; set; } = string.Empty;
    public string CertificateReference { get; set; } = string.Empty;
    public string CertificatePasswordReference { get; set; } = string.Empty;
    public string ClientSecretReference { get; set; } = string.Empty;
    public string AdminConsentRedirectUrl { get; set; } = string.Empty;
    public int ConsentStateLifetimeMinutes { get; set; } = 10;
    public int TokenRefreshSkewMinutes { get; set; } = 5;
    public string CallbackOpenIdConfigurationUrl { get; set; } = "https://api.aps.skype.com/v1/.well-known/OpenIdConfiguration";
    public string CallbackIssuer { get; set; } = "https://api.botframework.com";
    public string PackageVersion { get; set; } = "1.0.0";
    public bool CallControlEnabled { get; set; }
    public bool AudioEnabled { get; set; }
    public bool SharedStageEnabled { get; set; }
    public bool VisualMediaEnabled { get; set; }
    public string CallControlProvider { get; set; } = "microsoft_graph";
    public string CallControlHostId { get; set; } = "unassigned";
    public int MaxActiveCallsPerCompany { get; set; } = 3;
    public int MaxActiveCallsPerHost { get; set; } = 10;
    public int ProviderRequestTimeoutSeconds { get; set; } = 15;
    public int CallbackMaxBytes { get; set; } = 262144;
    public int ReconciliationDelaySeconds { get; set; } = 5;
    public int MediaBufferFrames { get; set; } = 100;
    public int MaximumMediaFrameBytes { get; set; } = 640;
    public int MaximumMediaReconnects { get; set; } = 2;
    public int StageRenderTimeoutMilliseconds { get; set; } = 3000;
    public int StageAccessMinutes { get; set; } = 120;
    public int MaximumConsecutiveSlideTransitions { get; set; } = 8;
    public int MinimumSlideDwellSeconds { get; set; } = 2;
    public int MaximumSlideDwellSeconds { get; set; } = 900;
    public int MaximumMediaMinutes { get; set; } = 120;
    public string MediaHostPublicIp { get; set; } = string.Empty;
    public string MediaHostServiceFqdn { get; set; } = string.Empty;
    public int MediaHostInternalPort { get; set; } = 8445;
    public int MediaHostPublicPort { get; set; } = 8445;
    public string MediaCertificateThumbprint { get; set; } = string.Empty;
    public bool MediaHostDeploymentApproved { get; set; }
    public bool MediaHostPublicReachabilityApproved { get; set; }
    public string MediaHostTopology { get; set; } = "azure_vmss_windows";
    public int MediaSdkMaximumAgeDays { get; set; } = 92;
    public int MediaHostDrainMinutes { get; set; } = 15;
    public int MediaHostReconciliationSeconds { get; set; } = 2;
    public bool AzureScheduledEventsEnabled { get; set; }
    public bool VmssInstanceProtectionEnabled { get; set; }
    public string VmssSubscriptionId { get; set; } = string.Empty;
    public string VmssResourceGroup { get; set; } = string.Empty;
    public string VmssName { get; set; } = string.Empty;
    public string VmssInstanceId { get; set; } = string.Empty;
    public bool ProductionEnabled { get; set; }
    public bool PilotEnabled { get; set; }
    public bool FirstUatEnabled { get; set; }
    public bool EmergencyDisabled { get; set; }
    public string[] PilotCompanyIds { get; set; } = [];
    public string[] PilotUserIds { get; set; } = [];
    public int MaxActiveCallsGlobal { get; set; } = 25;
    public decimal MonthlyCostUsed { get; set; }
    public decimal MonthlyCostLimit { get; set; }
    public string CostCurrency { get; set; } = "USD";
    public string MinimumPackageVersion { get; set; } = "1.0.0";
    public string InstalledPackageVersion { get; set; } = string.Empty;
    public bool AppInstallationAttested { get; set; }
    public bool AutomatedEvidenceApproved { get; set; }
    public bool LiveUatApproved { get; set; }
    public string LiveUatOwner { get; set; } = string.Empty;
    public DateTime? LiveUatCompletedUtc { get; set; }
    public string LiveUatEvidenceReference { get; set; } = string.Empty;
    public DateTime? MediaCertificateExpiresUtc { get; set; }
    public string InstallUrl { get; set; } = string.Empty;
}
