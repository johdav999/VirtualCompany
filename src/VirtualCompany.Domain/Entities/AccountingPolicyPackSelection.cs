namespace VirtualCompany.Domain.Entities;

public sealed class AccountingPolicyPackSelection : ICompanyOwnedEntity
{
    private AccountingPolicyPackSelection()
    {
    }

    public AccountingPolicyPackSelection(
        Guid id,
        Guid companyId,
        Guid accountingConfigurationId,
        string packKey,
        string packVersion,
        string definitionHash,
        bool isStatutoryComplianceValidated,
        DateOnly effectiveFrom,
        Guid selectedByUserId,
        DateTime selectedUtc)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (accountingConfigurationId == Guid.Empty)
        {
            throw new ArgumentException("AccountingConfigurationId is required.", nameof(accountingConfigurationId));
        }

        if (selectedByUserId == Guid.Empty)
        {
            throw new ArgumentException("SelectedByUserId is required.", nameof(selectedByUserId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        AccountingConfigurationId = accountingConfigurationId;
        PackKey = Normalize(packKey, nameof(packKey), 96).ToLowerInvariant();
        PackVersion = Normalize(packVersion, nameof(packVersion), 32);
        DefinitionHash = Normalize(definitionHash, nameof(definitionHash), 64).ToLowerInvariant();
        IsStatutoryComplianceValidated = isStatutoryComplianceValidated;
        EffectiveFrom = effectiveFrom;
        SelectedByUserId = selectedByUserId;
        SelectedUtc = EntityTimestampNormalizer.NormalizeUtc(selectedUtc, nameof(selectedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid AccountingConfigurationId { get; private set; }
    public string PackKey { get; private set; } = null!;
    public string PackVersion { get; private set; } = null!;
    public string DefinitionHash { get; private set; } = null!;
    public bool IsStatutoryComplianceValidated { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public Guid SelectedByUserId { get; private set; }
    public DateTime SelectedUtc { get; private set; }
    public AccountingConfiguration AccountingConfiguration { get; private set; } = null!;

    public void EndBefore(DateOnly nextEffectiveFrom)
    {
        if (nextEffectiveFrom <= EffectiveFrom)
        {
            throw new InvalidOperationException("A policy-pack upgrade must take effect after the current selection.");
        }

        EffectiveTo = nextEffectiveFrom.AddDays(-1);
    }

    private static string Normalize(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var normalized = value.Trim();
        if (normalized.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return normalized;
    }
}
