using VirtualCompany.Domain.ValueObjects;

namespace VirtualCompany.Domain.Entities;
public sealed class SalesCampaign : ICompanyOwnedEntity
{
    private SalesCampaign()
    {
    }

    public SalesCampaign(
        Guid id,
        Guid companyId,
        Guid salesSequenceId,
        string name,
        string audienceType,
        string status = SalesStatuses.Draft,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null,
        string? communicationLanguage = null)
    {
        SalesEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        SalesSequenceId = salesSequenceId == Guid.Empty ? throw new ArgumentException("SalesSequenceId is required.", nameof(salesSequenceId)) : salesSequenceId;
        Name = SalesEntityText.NormalizeRequired(name, nameof(name), 160);
        AudienceType = SalesEntityText.NormalizeRequired(audienceType, nameof(audienceType), 64).ToLowerInvariant();
        Status = SalesEntityText.NormalizeRequired(status, nameof(status), 32).ToLowerInvariant();
        CommunicationLanguage = CommunicationLanguageTag.NormalizeOptional(communicationLanguage, nameof(communicationLanguage));
        CreatedUtc = SalesEntityText.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = SalesEntityText.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid SalesSequenceId { get; private set; }
    public string Name { get; private set; } = null!;
    public string AudienceType { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string? CommunicationLanguage { get; private set; }
    public bool OutboundEnabled { get; private set; } = true;
    public int MaxEmailsPerDay { get; private set; } = 50;
    public bool ApprovalRequired { get; private set; }
    public DateTime? ApprovalRequestedUtc { get; private set; }
    public DateTime? ApprovedUtc { get; private set; }
    public string? ApprovalStatus { get; private set; }
    public DateTime? LaunchRequestedUtc { get; private set; }
    public DateTime? StartedUtc { get; private set; }
    public DateTime? PausedUtc { get; private set; }
    public DateTime? StoppedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public SalesSequence SalesSequence { get; private set; } = null!;
    public ICollection<SalesCampaignContact> Contacts { get; } = new List<SalesCampaignContact>();

    public void SetPolicy(bool outboundEnabled, int maxEmailsPerDay, bool approvalRequired)
    {
        if (maxEmailsPerDay <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxEmailsPerDay), "Max emails per day must be greater than zero.");
        }

        OutboundEnabled = outboundEnabled;
        MaxEmailsPerDay = maxEmailsPerDay;
        ApprovalRequired = approvalRequired;
        ApprovalStatus = approvalRequired ? SalesStatuses.WaitingForApproval : SalesStatuses.Approved;
        UpdatedUtc = DateTime.UtcNow;
    }

    public void SetCommunicationLanguage(string? language)
    {
        CommunicationLanguage = CommunicationLanguageTag.NormalizeOptional(language, nameof(language));
        UpdatedUtc = DateTime.UtcNow;
    }


    public void RequestLaunch()
    {
        if (!OutboundEnabled)
        {
            throw new InvalidOperationException("Outbound email is disabled for this company.");
        }

        LaunchRequestedUtc = DateTime.UtcNow;
        if (ApprovalRequired && ApprovedUtc is null)
        {
            Status = SalesStatuses.WaitingForApproval;
            ApprovalRequestedUtc ??= LaunchRequestedUtc;
            ApprovalStatus = SalesStatuses.WaitingForApproval;
        }
        else
        {
            Status = SalesStatuses.Active;
            StartedUtc ??= LaunchRequestedUtc;
            ApprovalStatus = SalesStatuses.Approved;
        }

        UpdatedUtc = LaunchRequestedUtc.Value;
    }

    public void ApproveLaunch()
    {
        ApprovedUtc = DateTime.UtcNow;
        ApprovalStatus = SalesStatuses.Approved;
        Status = SalesStatuses.Active;
        StartedUtc ??= ApprovedUtc;
        UpdatedUtc = ApprovedUtc.Value;
    }

    public void Pause()
    {
        if (Status is SalesStatuses.Stopped or SalesStatuses.Completed)
        {
            return;
        }

        Status = SalesStatuses.Paused;
        PausedUtc = DateTime.UtcNow;
        UpdatedUtc = PausedUtc.Value;
    }

    public void Stop()
    {
        if (Status == SalesStatuses.Stopped)
        {
            return;
        }

        Status = SalesStatuses.Stopped;
        StoppedUtc = DateTime.UtcNow;
        UpdatedUtc = StoppedUtc.Value;
    }
}
