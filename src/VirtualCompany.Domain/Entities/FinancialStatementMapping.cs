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
        DateTime? updatedUtc = null,
        long versionNumber = 1,
        DateOnly? effectiveFrom = null,
        DateOnly? effectiveTo = null,
        Guid? supersedesMappingId = null)
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
        if (versionNumber < 1) throw new ArgumentOutOfRangeException(nameof(versionNumber));
        if (effectiveTo.HasValue && effectiveTo.Value < (effectiveFrom ?? DateOnly.MinValue))
            throw new ArgumentOutOfRangeException(nameof(effectiveTo), "EffectiveTo cannot be earlier than EffectiveFrom.");
        if (supersedesMappingId == Guid.Empty) throw new ArgumentException("SupersedesMappingId cannot be empty.", nameof(supersedesMappingId));

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        FinanceAccountId = financeAccountId;
        StatementType = statementType;
        ReportSection = reportSection;
        LineClassification = lineClassification;
        IsActive = isActive;
        VersionNumber = versionNumber;
        EffectiveFrom = effectiveFrom ?? DateOnly.MinValue;
        EffectiveTo = effectiveTo;
        SupersedesMappingId = supersedesMappingId;
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
    public long VersionNumber { get; private set; }
    public DateOnly EffectiveFrom { get; private set; }
    public DateOnly? EffectiveTo { get; private set; }
    public Guid? SupersedesMappingId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public FinanceAccount FinanceAccount { get; private set; } = null!;

    public bool IsEffectiveOn(DateOnly date) => EffectiveFrom <= date && (!EffectiveTo.HasValue || date < EffectiveTo.Value);

    public void Retire(DateOnly effectiveTo, DateTime? updatedUtc = null)
    {
        if (effectiveTo < EffectiveFrom) throw new ArgumentOutOfRangeException(nameof(effectiveTo));
        EffectiveTo = effectiveTo;
        IsActive = false;
        UpdatedUtc = EntityTimestampNormalizer.NormalizeUtc(updatedUtc ?? DateTime.UtcNow, nameof(updatedUtc));
    }

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
