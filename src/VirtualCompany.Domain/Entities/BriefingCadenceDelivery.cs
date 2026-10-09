namespace VirtualCompany.Domain.Entities;

// Content is read through current recipient permissions; this ledger retains routing and delivery evidence only.
public sealed class BriefingCadenceDelivery : ICompanyOwnedEntity
{
    public Guid Id { get; set; }
    public Guid CompanyId { get; set; }
    public Guid OwnerUserId { get; set; }
    public Guid? RecipientUserId { get; set; }
    public string Cadence { get; set; } = "";
    public string SlotKey { get; set; } = "";
    public string SettingsHash { get; set; } = "";
    public string? ContentHash { get; set; }
    public DateTime ScheduledUtc { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
    public string Routing { get; set; } = "self";
    public string Status { get; set; } = BriefingCadenceDeliveryStates.Queued;
    public int Attempts { get; set; }
    public string? Reason { get; set; }
}
public static class BriefingCadenceDeliveryStates
{
    public const string Queued = "queued", Ready = "ready", Sent = "sent", Suppressed = "suppressed",
        Failed = "failed", Uncertain = "uncertain", Retry = "retry";
}
