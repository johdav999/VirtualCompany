namespace VirtualCompany.Domain.Entities;

public sealed class FinancialReportSuiteSnapshot : ICompanyOwnedEntity
{
    private FinancialReportSuiteSnapshot() { }

    public FinancialReportSuiteSnapshot(Guid id, Guid companyId, Guid fiscalPeriodId, string reportKind,
        string calculationVersion, string mappingVersion, string parametersHash, string checksum,
        string reportJson, Guid createdByUserId, string idempotencyKey, DateTime createdUtc,
        Guid? reportDefinitionVersionId = null, int? reportDefinitionVersionNumber = null,
        string? reportDefinitionHash = null)
    {
        Id = Required(id, nameof(id));
        CompanyId = Required(companyId, nameof(companyId));
        FiscalPeriodId = Required(fiscalPeriodId, nameof(fiscalPeriodId));
        ReportKind = Text(reportKind, nameof(reportKind), 64);
        CalculationVersion = Text(calculationVersion, nameof(calculationVersion), 64);
        MappingVersion = Text(mappingVersion, nameof(mappingVersion), 128);
        ParametersHash = Hash(parametersHash, nameof(parametersHash));
        Checksum = Hash(checksum, nameof(checksum));
        ReportJson = Text(reportJson, nameof(reportJson), 2_000_000);
        CreatedByUserId = Required(createdByUserId, nameof(createdByUserId));
        IdempotencyKey = Text(idempotencyKey, nameof(idempotencyKey), 200);
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
        ReportDefinitionVersionId = reportDefinitionVersionId;
        ReportDefinitionVersionNumber = reportDefinitionVersionNumber;
        ReportDefinitionHash = string.IsNullOrWhiteSpace(reportDefinitionHash) ? null : Hash(reportDefinitionHash, nameof(reportDefinitionHash));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid FiscalPeriodId { get; private set; }
    public string ReportKind { get; private set; } = null!;
    public string CalculationVersion { get; private set; } = null!;
    public string MappingVersion { get; private set; } = null!;
    public string ParametersHash { get; private set; } = null!;
    public string Checksum { get; private set; } = null!;
    public string ReportJson { get; private set; } = null!;
    public Guid CreatedByUserId { get; private set; }
    public string IdempotencyKey { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public Guid? ReportDefinitionVersionId { get; private set; }
    public int? ReportDefinitionVersionNumber { get; private set; }
    public string? ReportDefinitionHash { get; private set; }
    public Company Company { get; private set; } = null!;
    public FiscalPeriod FiscalPeriod { get; private set; } = null!;
    public ReportDefinitionVersion? ReportDefinitionVersion { get; private set; }

    private static Guid Required(Guid value, string name) => value == Guid.Empty
        ? throw new ArgumentException($"{name} is required.", name) : value;
    private static string Text(string value, string name, int max) => string.IsNullOrWhiteSpace(value)
        ? throw new ArgumentException($"{name} is required.", name)
        : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);
    private static string Hash(string value, string name)
    {
        var normalized = Text(value, name, 64).ToLowerInvariant();
        return normalized.Length == 64 && normalized.All(Uri.IsHexDigit)
            ? normalized : throw new ArgumentException($"{name} must be a SHA-256 value.", name);
    }
}
