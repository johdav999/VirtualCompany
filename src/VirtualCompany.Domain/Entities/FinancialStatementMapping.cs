using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class FinancialStatementMapping : ICompanyOwnedEntity
{
    private FinancialStatementMapping()
    {
    }

    public FinancialStatementMapping(
        Guid id,
        Guid companyId,
        Guid financeAccountId,
        FinancialStatementType statementType,
        FinancialStatementReportSection reportSection,
        FinancialStatementLineClassification lineClassification,
        bool isActive = true,
        DateTime? createdUtc = null,
        DateTime? updatedUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (financeAccountId == Guid.Empty)
        {
            throw new ArgumentException("FinanceAccountId is required.", nameof(financeAccountId));
        }

        FinancialStatementTypeValues.EnsureSupported(statementType, nameof(statementType));
        FinancialStatementReportSectionValues.EnsureSupported(reportSection, nameof(reportSection));
        FinancialStatementLineClassificationValues.EnsureSupported(lineClassification, nameof(lineClassification));
        FinancialStatementMappingCompatibility.EnsureCompatible(statementType, reportSection, lineClassification);

        var normalizedCreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? DateTime.UtcNow, nameof(createdUtc));
        var normalizedUpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? normalizedCreatedUtc, nameof(updatedUtc));
        if (normalizedUpdatedUtc < normalizedCreatedUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(updatedUtc), "UpdatedUtc cannot be earlier than CreatedUtc.");
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        FinanceAccountId = financeAccountId;
        StatementType = statementType;
        ReportSection = reportSection;
        LineClassification = lineClassification;
        IsActive = isActive;
        CreatedUtc = normalizedCreatedUtc;
        UpdatedUtc = normalizedUpdatedUtc;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid FinanceAccountId { get; private set; }
    public FinancialStatementType StatementType { get; private set; }
    public FinancialStatementReportSection ReportSection { get; private set; }
    public FinancialStatementLineClassification LineClassification { get; private set; }
    public bool IsActive { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public FinanceAccount FinanceAccount { get; private set; } = null!;

    public void UpdateClassification(
        FinancialStatementReportSection reportSection,
        FinancialStatementLineClassification lineClassification,
        DateTime? updatedUtc = null)
    {
        FinancialStatementReportSectionValues.EnsureSupported(reportSection, nameof(reportSection));
        FinancialStatementLineClassificationValues.EnsureSupported(lineClassification, nameof(lineClassification));
        FinancialStatementMappingCompatibility.EnsureCompatible(StatementType, reportSection, lineClassification);

        ReportSection = reportSection;
        LineClassification = lineClassification;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? DateTime.UtcNow, nameof(updatedUtc));
    }

    public void ReassignStatement(
        FinancialStatementType statementType,
        FinancialStatementReportSection reportSection,
        FinancialStatementLineClassification lineClassification,
        DateTime? updatedUtc = null)
    {
        FinancialStatementTypeValues.EnsureSupported(statementType, nameof(statementType));
        FinancialStatementReportSectionValues.EnsureSupported(reportSection, nameof(reportSection));
        FinancialStatementLineClassificationValues.EnsureSupported(lineClassification, nameof(lineClassification));
        FinancialStatementMappingCompatibility.EnsureCompatible(statementType, reportSection, lineClassification);

        StatementType = statementType;
        ReportSection = reportSection;
        LineClassification = lineClassification;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? DateTime.UtcNow, nameof(updatedUtc));
    }

    public void SetActive(bool isActive, DateTime? updatedUtc = null)
    {
        IsActive = isActive;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? DateTime.UtcNow, nameof(updatedUtc));
    }

    public void ReassignAccount(Guid financeAccountId, DateTime? updatedUtc = null)
    {
        if (financeAccountId == Guid.Empty)
        {
            throw new ArgumentException("FinanceAccountId is required.", nameof(financeAccountId));
        }

        FinanceAccountId = financeAccountId;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? DateTime.UtcNow, nameof(updatedUtc));
    }
}
