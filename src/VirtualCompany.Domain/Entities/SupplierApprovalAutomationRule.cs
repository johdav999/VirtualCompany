namespace VirtualCompany.Domain.Entities;

public static class SupplierApprovalAutomationStages
{
    public const string SupplierCreation = "supplier_creation";
    public const string InvoiceRegistration = "invoice_registration";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        SupplierCreation,
        InvoiceRegistration
    };

    public static string Normalize(string value)
    {
        var normalized = value?.Trim().Replace('-', '_').ToLowerInvariant()
            ?? throw new ArgumentNullException(nameof(value));
        return All.Contains(normalized)
            ? normalized
            : throw new ArgumentOutOfRangeException(nameof(value), "Unsupported supplier approval automation stage.");
    }
}

public sealed class SupplierApprovalAutomationRule : ICompanyOwnedEntity
{
    private SupplierApprovalAutomationRule()
    {
    }

    public SupplierApprovalAutomationRule(
        Guid id,
        Guid companyId,
        string supplierKey,
        string supplierName,
        string supplierOrgNumber,
        string stage,
        Guid agentId,
        string agentDisplayName,
        Guid? grantedByUserId,
        string grantedByDisplayName,
        DateTime nowUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        SupplierKey = Required(supplierKey, nameof(supplierKey), 160);
        SupplierName = Required(supplierName, nameof(supplierName), 300);
        SupplierOrgNumber = Required(supplierOrgNumber, nameof(supplierOrgNumber), 100);
        Stage = SupplierApprovalAutomationStages.Normalize(stage);
        AgentId = agentId == Guid.Empty ? throw new ArgumentException("AgentId is required.", nameof(agentId)) : agentId;
        AgentDisplayName = Required(agentDisplayName, nameof(agentDisplayName), 200);
        GrantedByUserId = grantedByUserId == Guid.Empty ? null : grantedByUserId;
        GrantedByDisplayName = Required(grantedByDisplayName, nameof(grantedByDisplayName), 200);
        IsActive = true;
        CreatedUtc = UpdatedUtc = NormalizeUtc(nowUtc);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string SupplierKey { get; private set; } = null!;
    public string SupplierName { get; private set; } = null!;
    public string SupplierOrgNumber { get; private set; } = null!;
    public string Stage { get; private set; } = null!;
    public Guid AgentId { get; private set; }
    public string AgentDisplayName { get; private set; } = null!;
    public Guid? GrantedByUserId { get; private set; }
    public string GrantedByDisplayName { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? RevokedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public Agent Agent { get; private set; } = null!;

    public void Enable(Guid agentId, string agentDisplayName, Guid? grantedByUserId, string grantedByDisplayName, DateTime nowUtc)
    {
        AgentId = agentId == Guid.Empty ? throw new ArgumentException("AgentId is required.", nameof(agentId)) : agentId;
        AgentDisplayName = Required(agentDisplayName, nameof(agentDisplayName), 200);
        GrantedByUserId = grantedByUserId == Guid.Empty ? null : grantedByUserId;
        GrantedByDisplayName = Required(grantedByDisplayName, nameof(grantedByDisplayName), 200);
        IsActive = true;
        RevokedUtc = null;
        UpdatedUtc = NormalizeUtc(nowUtc);
    }

    public void Revoke(DateTime nowUtc)
    {
        IsActive = false;
        RevokedUtc = UpdatedUtc = NormalizeUtc(nowUtc);
    }

    private static string Required(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var normalized = value.Trim();
        return normalized.Length <= maxLength
            ? normalized
            : throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();
}
