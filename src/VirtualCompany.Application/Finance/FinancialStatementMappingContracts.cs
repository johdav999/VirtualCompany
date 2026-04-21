using System.Collections.ObjectModel;

namespace VirtualCompany.Application.Finance;

public sealed record ListFinancialStatementMappingsQuery(Guid CompanyId);

public sealed record CreateFinancialStatementMappingCommand(
    Guid CompanyId,
    Guid AccountId,
    string StatementType,
    string ReportSection,
    string LineClassification,
    bool IsActive = true);

public sealed record UpdateFinancialStatementMappingCommand(
    Guid CompanyId,
    Guid MappingId,
    Guid AccountId,
    string StatementType,
    string ReportSection,
    string LineClassification,
    bool IsActive = true);

public sealed record ValidateFinancialStatementMappingsQuery(Guid CompanyId);

public sealed record FinancialStatementMappingDto(
    Guid Id,
    Guid CompanyId,
    Guid AccountId,
    string AccountCode,
    string AccountName,
    string StatementType,
    string ReportSection,
    string LineClassification,
    bool IsActive,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record FinancialStatementMappingValidationIssueDto(
    string Code,
    string Message,
    Guid AccountId,
    string AccountCode,
    string AccountName,
    Guid? MappingId,
    string? StatementType);

public sealed record FinancialStatementMappingValidationResultDto(
    Guid CompanyId,
    int AccountCount,
    int MappingCount,
    int IssueCount,
    int UnmappedCount,
    int ConflictCount,
    IReadOnlyList<FinancialStatementMappingValidationIssueDto> Issues);

public sealed record FinancialStatementMappingCommandErrorDto(
    string Code,
    string Field,
    string Message);

public interface IFinancialStatementMappingService
{
    Task<IReadOnlyList<FinancialStatementMappingDto>> ListAsync(ListFinancialStatementMappingsQuery query, CancellationToken cancellationToken);
    Task<FinancialStatementMappingDto> CreateAsync(CreateFinancialStatementMappingCommand command, CancellationToken cancellationToken);
    Task<FinancialStatementMappingDto> UpdateAsync(UpdateFinancialStatementMappingCommand command, CancellationToken cancellationToken);
    Task<FinancialStatementMappingValidationResultDto> ValidateAsync(ValidateFinancialStatementMappingsQuery query, CancellationToken cancellationToken);
}

public sealed class FinancialStatementMappingCommandException : Exception
{
    public FinancialStatementMappingCommandException(int statusCode, string code, string title, string message, IReadOnlyList<FinancialStatementMappingCommandErrorDto> errors)
        : base(message)
    {
        StatusCode = statusCode;
        Code = code;
        Title = title;
        Errors = new ReadOnlyCollection<FinancialStatementMappingCommandErrorDto>(errors.ToList());
    }

    public int StatusCode { get; }
    public string Code { get; }
    public string Title { get; }
    public IReadOnlyList<FinancialStatementMappingCommandErrorDto> Errors { get; }
}
