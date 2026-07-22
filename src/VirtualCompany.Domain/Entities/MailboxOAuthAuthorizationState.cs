using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class MailboxOAuthAuthorizationState : ICompanyOwnedEntity
{
    private MailboxOAuthAuthorizationState()
    {
    }

    public MailboxOAuthAuthorizationState(
        Guid id,
        Guid companyId,
        Guid userId,
        MailboxPurpose purpose,
        MailboxProvider provider,
        string nonceHash,
        DateTime createdUtc,
        DateTime expiresUtc)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("CompanyId is required.", nameof(companyId));
        if (userId == Guid.Empty) throw new ArgumentException("UserId is required.", nameof(userId));
        MailboxPurposeValues.EnsureSupported(purpose, nameof(purpose));
        MailboxProviderValues.EnsureSupported(provider, nameof(provider));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        UserId = userId;
        Purpose = purpose;
        Provider = provider;
        NonceHash = NormalizeRequired(nonceHash, nameof(nonceHash), 64).ToLowerInvariant();
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        ExpiresUtc = EntityTimestampNormalizer.NormalizeUtc(expiresUtc, nameof(expiresUtc));
        if (ExpiresUtc <= CreatedUtc)
        {
            throw new ArgumentException("OAuth state expiry must be after creation.", nameof(expiresUtc));
        }
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid UserId { get; private set; }
    public MailboxPurpose Purpose { get; private set; }
    public MailboxProvider Provider { get; private set; }
    public string NonceHash { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public DateTime ExpiresUtc { get; private set; }
    public DateTime? ConsumedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public User User { get; private set; } = null!;

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"{name} is required.", name);
        var normalized = value.Trim();
        if (normalized.Length > maxLength) throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        return normalized;
    }
}
