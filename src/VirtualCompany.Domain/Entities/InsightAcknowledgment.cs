namespace VirtualCompany.Domain.Entities;

public sealed class InsightAcknowledgment : ICompanyOwnedEntity
{
    private const int InsightKeyMaxLength = 200;

    private InsightAcknowledgment()
    {
    }

    public InsightAcknowledgment(Guid id, Guid companyId, Guid userId, string insightKey, DateTime acknowledgedUtc)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (userId == Guid.Empty)
        {
            throw new ArgumentException("UserId is required.", nameof(userId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        UserId = userId;
        InsightKey = NormalizeInsightKey(insightKey);
        AcknowledgedUtc = NormalizeUtc(acknowledgedUtc);
        CreatedUtc = AcknowledgedUtc;
        UpdatedUtc = AcknowledgedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid UserId { get; private set; }
    public string InsightKey { get; private set; } = null!;
    public DateTime AcknowledgedUtc { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public User User { get; private set; } = null!;

    public void MarkAcknowledged(DateTime acknowledgedUtc)
    {
        AcknowledgedUtc = NormalizeUtc(acknowledgedUtc);
        UpdatedUtc = AcknowledgedUtc;
    }

    private static string NormalizeInsightKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("InsightKey is required.", nameof(value));
        }

        var trimmed = value.Trim().ToLowerInvariant();
        if (trimmed.Length > InsightKeyMaxLength)
        {
            throw new ArgumentOutOfRangeException(nameof(value), $"InsightKey must be {InsightKeyMaxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}