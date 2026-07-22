using System.Text.Json.Nodes;

namespace VirtualCompany.Domain.Entities;
public sealed class SupportSlaPolicy : ICompanyOwnedEntity
{
    private SupportSlaPolicy()
    {
    }

    public SupportSlaPolicy(Guid id, Guid companyId, string name, string category, string priority, int firstResponseMinutes, int resolutionMinutes, string? customerTier = null, bool isActive = true, string timeBasis = "elapsed", int riskThresholdMinutes = 240, string escalationRecipientRole = "support_supervisor")
    {
        SupportEntityText.EnsureCompany(companyId);
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        Name = SupportEntityText.NormalizeRequired(name, nameof(name), 160);
        Category = SupportCaseCategories.Normalize(category);
        Priority = SupportPriorities.Normalize(priority);
        CustomerTier = SupportEntityText.NormalizeOptional(customerTier, nameof(customerTier), 80);
        FirstResponseMinutes = firstResponseMinutes <= 0 ? throw new ArgumentOutOfRangeException(nameof(firstResponseMinutes)) : firstResponseMinutes;
        ResolutionMinutes = resolutionMinutes <= 0 ? throw new ArgumentOutOfRangeException(nameof(resolutionMinutes)) : resolutionMinutes;
        IsActive = isActive;
        TimeBasis = NormalizeTimeBasis(timeBasis);
        RiskThresholdMinutes = riskThresholdMinutes > 0 ? riskThresholdMinutes : throw new ArgumentOutOfRangeException(nameof(riskThresholdMinutes));
        EscalationRecipientRole = SupportEntityText.NormalizeRequired(escalationRecipientRole, nameof(escalationRecipientRole), 80);
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = null!;
    public string Category { get; private set; } = null!;
    public string Priority { get; private set; } = null!;
    public string? CustomerTier { get; private set; }
    public int FirstResponseMinutes { get; private set; }
    public int ResolutionMinutes { get; private set; }
    public bool IsActive { get; private set; }
    public string TimeBasis { get; private set; } = "elapsed";
    public int RiskThresholdMinutes { get; private set; } = 240;
    public string EscalationRecipientRole { get; private set; } = "support_supervisor";
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }

    public void Update(string name, string category, string priority, int firstResponseMinutes, int resolutionMinutes, string? customerTier, bool isActive, string timeBasis = "elapsed", int riskThresholdMinutes = 240, string escalationRecipientRole = "support_supervisor")
    {
        Name = SupportEntityText.NormalizeRequired(name, nameof(name), 160);
        Category = SupportCaseCategories.Normalize(category);
        Priority = SupportPriorities.Normalize(priority);
        CustomerTier = SupportEntityText.NormalizeOptional(customerTier, nameof(customerTier), 80);
        FirstResponseMinutes = firstResponseMinutes > 0 ? firstResponseMinutes : throw new ArgumentOutOfRangeException(nameof(firstResponseMinutes));
        ResolutionMinutes = resolutionMinutes > 0 ? resolutionMinutes : throw new ArgumentOutOfRangeException(nameof(resolutionMinutes));
        IsActive = isActive;
        TimeBasis = NormalizeTimeBasis(timeBasis);
        RiskThresholdMinutes = riskThresholdMinutes > 0 ? riskThresholdMinutes : throw new ArgumentOutOfRangeException(nameof(riskThresholdMinutes));
        EscalationRecipientRole = SupportEntityText.NormalizeRequired(escalationRecipientRole, nameof(escalationRecipientRole), 80);
        UpdatedUtc = DateTime.UtcNow;
    }

    public void Deactivate()
    {
        IsActive = false;
        UpdatedUtc = DateTime.UtcNow;
    }

    private static string NormalizeTimeBasis(string value) => value?.Trim().ToLowerInvariant() switch
    {
        "elapsed" => "elapsed",
        "business" => "business",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Time basis must be elapsed or business.")
    };
}

