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
    public string LifecycleStatus { get; private set; } = CampaignLifecycleStatuses.Draft;
    public string CampaignType { get; private set; } = CampaignTypes.LeadGeneration;
    public string? Description { get; private set; }
    public Guid? OwnerUserId { get; private set; }
    public Guid? OwnerAgentId { get; private set; }
    public string? PrimaryObjectiveType { get; private set; }
    public decimal? PrimaryObjectiveTarget { get; private set; }
    public string? PrimaryObjectiveUnit { get; private set; }
    public DateTime? PrimaryObjectiveTargetUtc { get; private set; }
    public DateTime? PlanningStartsUtc { get; private set; }
    public DateTime? ScheduledLaunchUtc { get; private set; }
    public DateTime? EndsUtc { get; private set; }
    public DateTime? ReviewDueUtc { get; private set; }
    public string TimeZoneId { get; private set; } = "UTC";
    public decimal? PlannedBudget { get; private set; }
    public string? BudgetCurrency { get; private set; }
    public bool LegacySetupRequired { get; private set; } = true;
    public long ConcurrencyVersion { get; private set; } = 1;
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
    public ICollection<SalesCampaignObjective> Objectives { get; } = new List<SalesCampaignObjective>();
    public ICollection<SalesCampaignOffer> Offers { get; } = new List<SalesCampaignOffer>();
    public ICollection<SalesCampaignActivity> Activities { get; } = new List<SalesCampaignActivity>();

    public void ConfigureInitiative(
        string campaignType,
        string? description,
        Guid ownerUserId,
        Guid? ownerAgentId,
        string objectiveType,
        decimal objectiveTarget,
        string objectiveUnit,
        DateTime objectiveTargetUtc,
        DateTime planningStartsUtc,
        DateTime scheduledLaunchUtc,
        DateTime endsUtc,
        string timeZoneId,
        decimal? plannedBudget,
        string? budgetCurrency,
        DateTime? reviewDueUtc = null)
    {
        if (ownerUserId == Guid.Empty)
        {
            throw new ArgumentException("A campaign owner is required.", nameof(ownerUserId));
        }

        if (objectiveTarget <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(objectiveTarget), "The objective target must be greater than zero.");
        }

        planningStartsUtc = SalesEntityText.NormalizeUtc(planningStartsUtc, nameof(planningStartsUtc));
        scheduledLaunchUtc = SalesEntityText.NormalizeUtc(scheduledLaunchUtc, nameof(scheduledLaunchUtc));
        endsUtc = SalesEntityText.NormalizeUtc(endsUtc, nameof(endsUtc));
        objectiveTargetUtc = SalesEntityText.NormalizeUtc(objectiveTargetUtc, nameof(objectiveTargetUtc));
        if (scheduledLaunchUtc < planningStartsUtc || endsUtc <= scheduledLaunchUtc)
        {
            throw new ArgumentException("Campaign dates must follow planning, launch, and end order.");
        }

        if (objectiveTargetUtc < scheduledLaunchUtc)
        {
            throw new ArgumentException("The objective target date cannot be before campaign launch.", nameof(objectiveTargetUtc));
        }

        if (plannedBudget is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(plannedBudget), "Planned budget cannot be negative.");
        }

        CampaignType = SalesEntityText.NormalizeRequired(campaignType, nameof(campaignType), 64).ToLowerInvariant();
        Description = SalesEntityText.NormalizeOptional(description, nameof(description), 2000);
        OwnerUserId = ownerUserId;
        OwnerAgentId = ownerAgentId is { } agentId && agentId != Guid.Empty ? agentId : null;
        PrimaryObjectiveType = SalesEntityText.NormalizeRequired(objectiveType, nameof(objectiveType), 64).ToLowerInvariant();
        PrimaryObjectiveTarget = objectiveTarget;
        PrimaryObjectiveUnit = SalesEntityText.NormalizeRequired(objectiveUnit, nameof(objectiveUnit), 40).ToLowerInvariant();
        PrimaryObjectiveTargetUtc = objectiveTargetUtc;
        PlanningStartsUtc = planningStartsUtc;
        ScheduledLaunchUtc = scheduledLaunchUtc;
        EndsUtc = endsUtc;
        ReviewDueUtc = reviewDueUtc.HasValue ? SalesEntityText.NormalizeUtc(reviewDueUtc.Value, nameof(reviewDueUtc)) : null;
        TimeZoneId = SalesEntityText.NormalizeRequired(timeZoneId, nameof(timeZoneId), 128);
        PlannedBudget = plannedBudget;
        BudgetCurrency = plannedBudget.HasValue
            ? SalesEntityText.NormalizeRequired(
                budgetCurrency ?? throw new ArgumentException("Budget currency is required when a planned budget is provided.", nameof(budgetCurrency)),
                nameof(budgetCurrency),
                3).ToUpperInvariant()
            : null;
        LegacySetupRequired = false;
        LifecycleStatus = CampaignLifecycleStatuses.Planning;
        Touch();
    }

    public IReadOnlyList<string> ReadinessGaps()
    {
        var gaps = new List<string>();
        if (LegacySetupRequired) gaps.Add("Complete the campaign objective and schedule.");
        if (OwnerUserId is null) gaps.Add("Choose a campaign owner.");
        if (string.IsNullOrWhiteSpace(PrimaryObjectiveType) || PrimaryObjectiveTarget is null) gaps.Add("Add a measurable campaign objective.");
        if (PlanningStartsUtc is null || ScheduledLaunchUtc is null || EndsUtc is null) gaps.Add("Set planning, launch, and end dates.");
        if (Offers.Count == 0) gaps.Add("Add an approved offer or document why no offer is required.");
        if (Activities.Count == 0) gaps.Add("Add at least one campaign activity.");
        if (Contacts.Count == 0) gaps.Add("Add or preview an eligible audience.");
        return gaps;
    }

    public void MarkReadyForApproval()
    {
        var gaps = ReadinessGaps();
        if (gaps.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", gaps));
        }

        LifecycleStatus = ApprovalRequired
            ? CampaignLifecycleStatuses.WaitingForApproval
            : CampaignLifecycleStatuses.Scheduled;
        ApprovalRequestedUtc ??= DateTime.UtcNow;
        Touch();
    }

    public bool IsDueToStart(DateTime utcNow) =>
        LifecycleStatus == CampaignLifecycleStatuses.Scheduled &&
        ScheduledLaunchUtc.HasValue &&
        ScheduledLaunchUtc.Value <= SalesEntityText.NormalizeUtc(utcNow, nameof(utcNow));

    public void Start(DateTime utcNow)
    {
        utcNow = SalesEntityText.NormalizeUtc(utcNow, nameof(utcNow));
        if (ScheduledLaunchUtc.HasValue && utcNow < ScheduledLaunchUtc.Value)
        {
            LifecycleStatus = CampaignLifecycleStatuses.Scheduled;
            Touch();
            return;
        }

        LifecycleStatus = CampaignLifecycleStatuses.Running;
        Status = SalesStatuses.Active;
        StartedUtc ??= utcNow;
        Touch();
    }

    public void MarkCompleted()
    {
        LifecycleStatus = CampaignLifecycleStatuses.Completed;
        Status = SalesStatuses.Completed;
        CompletedUtc = DateTime.UtcNow;
        Touch();
    }

    public void MarkReviewed()
    {
        if (LifecycleStatus != CampaignLifecycleStatuses.Completed)
        {
            throw new InvalidOperationException("Only a completed campaign can be reviewed.");
        }

        LifecycleStatus = CampaignLifecycleStatuses.Reviewed;
        Touch();
    }

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
        Touch();
    }

    public void SetCommunicationLanguage(string? language)
    {
        CommunicationLanguage = CommunicationLanguageTag.NormalizeOptional(language, nameof(language));
        Touch();
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
            LifecycleStatus = CampaignLifecycleStatuses.WaitingForApproval;
            ApprovalRequestedUtc ??= LaunchRequestedUtc;
            ApprovalStatus = SalesStatuses.WaitingForApproval;
        }
        else
        {
            if (ScheduledLaunchUtc.HasValue && ScheduledLaunchUtc.Value > LaunchRequestedUtc.Value)
            {
                Status = SalesStatuses.Draft;
                LifecycleStatus = CampaignLifecycleStatuses.Scheduled;
            }
            else
            {
                Status = SalesStatuses.Active;
                LifecycleStatus = CampaignLifecycleStatuses.Running;
                StartedUtc ??= LaunchRequestedUtc;
            }
            ApprovalStatus = SalesStatuses.Approved;
        }

        Touch(LaunchRequestedUtc.Value);
    }

    public void ApproveLaunch()
    {
        ApprovedUtc = DateTime.UtcNow;
        ApprovalStatus = SalesStatuses.Approved;
        if (ScheduledLaunchUtc.HasValue && ScheduledLaunchUtc.Value > ApprovedUtc.Value)
        {
            Status = SalesStatuses.Draft;
            LifecycleStatus = CampaignLifecycleStatuses.Scheduled;
        }
        else
        {
            Status = SalesStatuses.Active;
            LifecycleStatus = CampaignLifecycleStatuses.Running;
            StartedUtc ??= ApprovedUtc;
        }
        Touch(ApprovedUtc.Value);
    }

    public void Pause()
    {
        if (Status is SalesStatuses.Stopped or SalesStatuses.Completed)
        {
            return;
        }

        Status = SalesStatuses.Paused;
        LifecycleStatus = CampaignLifecycleStatuses.Paused;
        PausedUtc = DateTime.UtcNow;
        Touch(PausedUtc.Value);
    }

    public void Stop()
    {
        if (Status == SalesStatuses.Stopped)
        {
            return;
        }

        Status = SalesStatuses.Stopped;
        LifecycleStatus = CampaignLifecycleStatuses.Stopped;
        StoppedUtc = DateTime.UtcNow;
        Touch(StoppedUtc.Value);
    }

    private void Touch(DateTime? utcNow = null)
    {
        UpdatedUtc = SalesEntityText.NormalizeUtc(utcNow ?? DateTime.UtcNow, nameof(utcNow));
        ConcurrencyVersion++;
    }
}

public static class CampaignLifecycleStatuses
{
    public const string Draft = "draft";
    public const string Planning = "planning";
    public const string WaitingForApproval = "waiting_for_approval";
    public const string Scheduled = "scheduled";
    public const string Running = "running";
    public const string Paused = "paused";
    public const string Completed = "completed";
    public const string Reviewed = "reviewed";
    public const string Stopped = "stopped";
    public const string Cancelled = "cancelled";
}

public static class CampaignTypes
{
    public const string LeadGeneration = "lead_generation";
    public const string AccountBasedSales = "account_based_sales";
    public const string ProductLaunch = "product_launch";
    public const string Promotion = "promotion";
    public const string Nurture = "nurture";
    public const string Reengagement = "reengagement";
    public const string CrossSell = "cross_sell";
    public const string Renewal = "renewal";
    public const string Event = "event";
    public const string CustomerEducation = "customer_education";
}
