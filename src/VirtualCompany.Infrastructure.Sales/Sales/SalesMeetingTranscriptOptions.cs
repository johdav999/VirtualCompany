namespace VirtualCompany.Infrastructure.Sales;

public sealed class SalesMeetingTranscriptOptions
{
    public const string SectionName = "SalesMeetingTranscripts";
    public bool Enabled { get; set; }
    public string NotificationUrl { get; set; } = string.Empty;
    public string LifecycleNotificationUrl { get; set; } = string.Empty;
    public string ClientState { get; set; } = string.Empty;
    public int SubscriptionLifetimeHours { get; set; } = 71;
    public int RenewalLeadMinutes { get; set; } = 360;
    public int RenewalPollMinutes { get; set; } = 30;
    public int MaximumNotificationsPerRequest { get; set; } = 100;
    public int MaximumWebhookBytes { get; set; } = 256_000;
}
