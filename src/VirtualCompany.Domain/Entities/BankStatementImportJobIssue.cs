namespace VirtualCompany.Domain.Entities;

public sealed class BankStatementImportJobIssue : ICompanyOwnedEntity
{
    private BankStatementImportJobIssue() { }
    public BankStatementImportJobIssue(Guid id, Guid companyId, Guid jobId, string code, string severity,
        string message, int? rowNumber, DateTime createdUtc)
    {
        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId == Guid.Empty ? throw new ArgumentException("CompanyId is required.", nameof(companyId)) : companyId;
        JobId = jobId == Guid.Empty ? throw new ArgumentException("JobId is required.", nameof(jobId)) : jobId;
        Code = Required(code, nameof(code), 64);
        Severity = Required(severity, nameof(severity), 16);
        Message = Required(message, nameof(message), 500);
        RowNumber = rowNumber;
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc, nameof(createdUtc));
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid JobId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Severity { get; private set; } = null!;
    public string Message { get; private set; } = null!;
    public int? RowNumber { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public BankStatementImportJob Job { get; private set; } = null!;
    private static string Required(string value, string name, int max) => string.IsNullOrWhiteSpace(value) ?
        throw new ArgumentException($"{name} is required.", name) : value.Trim().Length <= max ? value.Trim() : throw new ArgumentOutOfRangeException(name);
}
