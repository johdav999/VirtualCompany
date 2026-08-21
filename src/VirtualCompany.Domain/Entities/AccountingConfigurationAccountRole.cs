namespace VirtualCompany.Domain.Entities;

public sealed class AccountingConfigurationAccountRole : ICompanyOwnedEntity
{
    private AccountingConfigurationAccountRole()
    {
    }

    public AccountingConfigurationAccountRole(
        Guid id,
        Guid companyId,
        Guid accountingConfigurationId,
        string roleKey,
        Guid financeAccountId,
        DateTime createdUtc)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (accountingConfigurationId == Guid.Empty)
        {
            throw new ArgumentException("AccountingConfigurationId is required.", nameof(accountingConfigurationId));
        }

        if (financeAccountId == Guid.Empty)
        {
            throw new ArgumentException("FinanceAccountId is required.", nameof(financeAccountId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        AccountingConfigurationId = accountingConfigurationId;
        RoleKey = NormalizeRoleKey(roleKey);
        FinanceAccountId = financeAccountId;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid AccountingConfigurationId { get; private set; }
    public string RoleKey { get; private set; } = null!;
    public Guid FinanceAccountId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public AccountingConfiguration AccountingConfiguration { get; private set; } = null!;
    public FinanceAccount FinanceAccount { get; private set; } = null!;

    public void Reassign(Guid financeAccountId, DateTime updatedUtc)
    {
        if (financeAccountId == Guid.Empty)
        {
            throw new ArgumentException("FinanceAccountId is required.", nameof(financeAccountId));
        }

        FinanceAccountId = financeAccountId;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc, nameof(updatedUtc));
    }

    private static string NormalizeRoleKey(string roleKey)
    {
        if (string.IsNullOrWhiteSpace(roleKey))
        {
            throw new ArgumentException("RoleKey is required.", nameof(roleKey));
        }

        var normalized = roleKey.Trim().Replace('-', '_').ToLowerInvariant();
        if (normalized.Length > 96)
        {
            throw new ArgumentOutOfRangeException(nameof(roleKey), "RoleKey must be 96 characters or fewer.");
        }

        return normalized;
    }
}
