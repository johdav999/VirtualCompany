using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;
public sealed class FinancePolicyConfiguration : ICompanyOwnedEntity
{
    private FinancePolicyConfiguration()
    {
    }

    public FinancePolicyConfiguration(
        Guid id,
        Guid companyId,
        string approvalCurrency,
        decimal invoiceApprovalThreshold,
        decimal billApprovalThreshold,
        bool requireCounterpartyForTransactions,
        decimal anomalyDetectionLowerBound = -10000m,
        decimal anomalyDetectionUpperBound = 10000m,
        int cashRunwayWarningThresholdDays = 90,
        int cashRunwayCriticalThresholdDays = 30)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        ValidateControls(
            invoiceApprovalThreshold,
            billApprovalThreshold,
            anomalyDetectionLowerBound,
            anomalyDetectionUpperBound,
            cashRunwayWarningThresholdDays,
            cashRunwayCriticalThresholdDays);

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        ApprovalCurrency = NormalizeRequired(approvalCurrency, nameof(approvalCurrency), 3).ToUpperInvariant();
        InvoiceApprovalThreshold = invoiceApprovalThreshold;
        BillApprovalThreshold = billApprovalThreshold;
        RequireCounterpartyForTransactions = requireCounterpartyForTransactions;
        AnomalyDetectionLowerBound = anomalyDetectionLowerBound;
        AnomalyDetectionUpperBound = anomalyDetectionUpperBound;
        CashRunwayWarningThresholdDays = cashRunwayWarningThresholdDays;
        CashRunwayCriticalThresholdDays = cashRunwayCriticalThresholdDays;
        CreatedUtc = DateTime.UtcNow;
        UpdatedUtc = CreatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string ApprovalCurrency { get; private set; } = null!;
    public decimal InvoiceApprovalThreshold { get; private set; }
    public decimal BillApprovalThreshold { get; private set; }
    public bool RequireCounterpartyForTransactions { get; private set; }
    public decimal AnomalyDetectionLowerBound { get; private set; }
    public decimal AnomalyDetectionUpperBound { get; private set; }
    public int CashRunwayWarningThresholdDays { get; private set; }
    public int CashRunwayCriticalThresholdDays { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;

    public void Update(
        string approvalCurrency,
        decimal invoiceApprovalThreshold,
        decimal billApprovalThreshold,
        bool requireCounterpartyForTransactions,
        decimal anomalyDetectionLowerBound,
        decimal anomalyDetectionUpperBound,
        int cashRunwayWarningThresholdDays,
        int cashRunwayCriticalThresholdDays)
    {
        ValidateControls(
            invoiceApprovalThreshold,
            billApprovalThreshold,
            anomalyDetectionLowerBound,
            anomalyDetectionUpperBound,
            cashRunwayWarningThresholdDays,
            cashRunwayCriticalThresholdDays);

        ApprovalCurrency = NormalizeRequired(approvalCurrency, nameof(approvalCurrency), 3).ToUpperInvariant();
        InvoiceApprovalThreshold = invoiceApprovalThreshold;
        BillApprovalThreshold = billApprovalThreshold;
        RequireCounterpartyForTransactions = requireCounterpartyForTransactions;
        AnomalyDetectionLowerBound = anomalyDetectionLowerBound;
        AnomalyDetectionUpperBound = anomalyDetectionUpperBound;
        CashRunwayWarningThresholdDays = cashRunwayWarningThresholdDays;
        CashRunwayCriticalThresholdDays = cashRunwayCriticalThresholdDays;
        UpdatedUtc = DateTime.UtcNow;
    }

    private static void ValidateControls(
        decimal invoiceApprovalThreshold,
        decimal billApprovalThreshold,
        decimal anomalyDetectionLowerBound,
        decimal anomalyDetectionUpperBound,
        int cashRunwayWarningThresholdDays,
        int cashRunwayCriticalThresholdDays)
    {
        if (invoiceApprovalThreshold < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(invoiceApprovalThreshold), "Invoice approval threshold cannot be negative.");
        }

        if (billApprovalThreshold < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(billApprovalThreshold), "Bill approval threshold cannot be negative.");
        }

        if (anomalyDetectionLowerBound >= anomalyDetectionUpperBound)
        {
            throw new ArgumentException("Anomaly detection lower bound must be less than upper bound.", nameof(anomalyDetectionLowerBound));
        }

        if (cashRunwayCriticalThresholdDays <= 0 || cashRunwayWarningThresholdDays <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(cashRunwayWarningThresholdDays), "Cash runway thresholds must be positive.");
        }

        if (cashRunwayCriticalThresholdDays > cashRunwayWarningThresholdDays)
        {
            throw new ArgumentException("Cash runway critical threshold cannot exceed warning threshold.", nameof(cashRunwayCriticalThresholdDays));
        }
    }

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
}

internal static class EntityTimestampNormalizer
{
    public static DateTime NormalizeUtc(DateTime value, string name)
    {
        if (value == default)
        {
            throw new ArgumentException($"{name} is required.", name);
        }

        return value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
    }
}

