using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;
public sealed class FinanceCounterparty : ICompanyOwnedEntity
{
    private FinanceCounterparty()
    {
    }

    public FinanceCounterparty(
        Guid id,
        Guid companyId,
        string name,
        string counterpartyType,
        string? email = null,
        string? paymentTerms = null,
        string? taxId = null,
        decimal? creditLimit = null,
        string? preferredPaymentMethod = null,
        string? defaultAccountMapping = null,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        CounterpartyType = NormalizeCounterpartyType(counterpartyType);
        Name = NormalizeRequired(name, nameof(name), 200);
        Email = NormalizeOptional(email, nameof(email), 256);
        PaymentTerms = NormalizeOptionalOrDefault(paymentTerms, nameof(paymentTerms), 64, ResolveDefaultPaymentTerms(CounterpartyType));
        TaxId = NormalizeOptional(taxId, nameof(taxId), 64);
        CreditLimit = NormalizeCreditLimit(creditLimit, nameof(creditLimit));
        PreferredPaymentMethod = NormalizeOptionalOrDefault(preferredPaymentMethod, nameof(preferredPaymentMethod), 64, DefaultPreferredPaymentMethod);
        DefaultAccountMapping = NormalizeOptionalOrDefault(defaultAccountMapping, nameof(defaultAccountMapping), 64, ResolveDefaultAccountMapping(CounterpartyType));
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? CreatedUtc, nameof(updatedUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Name { get; private set; } = null!;
    public string CounterpartyType { get; private set; } = null!;
    public string? Email { get; private set; }
    public string? PaymentTerms { get; private set; }
    public string? TaxId { get; private set; }
    public decimal? CreditLimit { get; private set; }
    public string? PreferredPaymentMethod { get; private set; }
    public string? DefaultAccountMapping { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public ICollection<FinanceTransaction> Transactions { get; } = new List<FinanceTransaction>();
    public ICollection<FinanceInvoice> Invoices { get; } = new List<FinanceInvoice>();
    public ICollection<FinanceBill> Bills { get; } = new List<FinanceBill>();

    public void UpdateMasterData(
        string name,
        string counterpartyType,
        string? email = null,
        string? paymentTerms = null,
        string? taxId = null,
        decimal? creditLimit = null,
        string? preferredPaymentMethod = null,
        string? defaultAccountMapping = null)
    {
        CounterpartyType = NormalizeCounterpartyType(counterpartyType);
        Name = NormalizeRequired(name, nameof(name), 200);
        Email = NormalizeOptional(email, nameof(email), 256);
        PaymentTerms = NormalizeOptionalOrDefault(paymentTerms, nameof(paymentTerms), 64, ResolveDefaultPaymentTerms(CounterpartyType));
        TaxId = NormalizeOptional(taxId, nameof(taxId), 64);
        CreditLimit = NormalizeCreditLimit(creditLimit, nameof(creditLimit));
        PreferredPaymentMethod = NormalizeOptionalOrDefault(preferredPaymentMethod, nameof(preferredPaymentMethod), 64, DefaultPreferredPaymentMethod);
        DefaultAccountMapping = NormalizeOptionalOrDefault(defaultAccountMapping, nameof(defaultAccountMapping), 64, ResolveDefaultAccountMapping(CounterpartyType));
        UpdatedUtc = DateTime.UtcNow;
    }

    private const string DefaultPreferredPaymentMethod = "bank_transfer";

    private static string NormalizeOptionalOrDefault(string? value, string name, int maxLength, string fallback)
    {
        var normalized = NormalizeOptional(value, name, maxLength);
        return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
    }

    private static string ResolveDefaultPaymentTerms(string counterpartyType) =>
        counterpartyType switch
        {
            "customer" => "Net30",
            "supplier" => "Net30",
            _ => "Net30"
        };

    private static string ResolveDefaultAccountMapping(string counterpartyType) =>
        counterpartyType switch
        {
            "customer" => "1100",
            "supplier" => "2000",
            _ => "2000"
        };

    public static string NormalizeCounterpartyKind(string value) =>
        NormalizeCounterpartyType(value) switch
        {
            "supplier" => "supplier",
            _ => "customer"
        };

    private static string NormalizeCounterpartyType(string value) =>
        NormalizeRequired(value, nameof(value), 64).ToLowerInvariant() switch
        {
            "vendor" => "supplier",
            var normalized => normalized
        };

    private static string NormalizeRequired(string value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }

    private static decimal NormalizeCreditLimit(decimal? value, string name)
    {
        var normalized = value ?? 0m;
        if (normalized < 0m)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} cannot be negative.");
        }

        return normalized;
    }

    private static string? NormalizeOptional(string? value, string name, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var trimmed = value.Trim();
        if (trimmed.Length > maxLength)
        {
            throw new ArgumentOutOfRangeException(name, $"{name} must be {maxLength} characters or fewer.");
        }

        return trimmed;
    }
}

