namespace VirtualCompany.Application.Finance;

public sealed record CopyReportSystemTemplateCommand(Guid CompanyId, string TemplateKey, string Code, string Name,
    Guid ActorUserId, string IdempotencyKey);

public sealed record UpdateReportDefinitionVersionCommand(Guid CompanyId, Guid VersionId, string Name,
    int ExpectedRevision, Guid ActorUserId, string IdempotencyKey, IReadOnlyList<ReportDefinitionSectionInput> Sections,
    ReportDefinitionComparisonDto Comparison);

public sealed record ValidateReportDefinitionCommand(Guid CompanyId, Guid VersionId, int ExpectedRevision,
    Guid ActorUserId, string IdempotencyKey);

public sealed record PreviewReportDefinitionQuery(Guid CompanyId, Guid VersionId, Guid FiscalPeriodId,
    Guid? ComparisonFiscalPeriodId = null, int Page = 1, int PageSize = 200);

public sealed record SubmitReportDefinitionCommand(Guid CompanyId, Guid VersionId, int ExpectedRevision,
    Guid ActorUserId, string IdempotencyKey);

public sealed record DecideReportDefinitionCommand(Guid CompanyId, Guid VersionId, int ExpectedRevision,
    Guid ActorUserId, bool Approve, string? DecisionNote, string IdempotencyKey);

public sealed record ActivateReportDefinitionCommand(Guid CompanyId, Guid VersionId, int ExpectedRevision,
    Guid ActorUserId, DateOnly EffectiveFrom, string IdempotencyKey);

public sealed record RetireReportDefinitionCommand(Guid CompanyId, Guid VersionId, int ExpectedRevision,
    Guid ActorUserId, DateOnly EffectiveTo, string IdempotencyKey);

public sealed record CreateReportDefinitionVersionCommand(Guid CompanyId, Guid DefinitionId, Guid SourceVersionId,
    Guid ActorUserId, string IdempotencyKey);

public interface IReportDefinitionService
{
    Task<IReadOnlyList<ReportSystemTemplateDto>> ListSystemTemplatesAsync(Guid companyId, CancellationToken cancellationToken);
    Task<IReadOnlyList<ReportDefinitionSummaryDto>> ListAsync(Guid companyId, CancellationToken cancellationToken);
    Task<ReportDefinitionVersionDto> GetAsync(Guid companyId, Guid versionId, CancellationToken cancellationToken);
    Task<ReportDefinitionVersionDto> CopySystemTemplateAsync(CopyReportSystemTemplateCommand command, CancellationToken cancellationToken);
    Task<ReportDefinitionVersionDto> CreateVersionAsync(CreateReportDefinitionVersionCommand command, CancellationToken cancellationToken);
    Task<ReportDefinitionVersionDto> UpdateAsync(UpdateReportDefinitionVersionCommand command, CancellationToken cancellationToken);
    Task<ReportDefinitionVersionDto> ValidateAsync(ValidateReportDefinitionCommand command, CancellationToken cancellationToken);
    Task<CompleteFinancialReportDto> PreviewAsync(PreviewReportDefinitionQuery query, CancellationToken cancellationToken);
    Task<ReportDefinitionVersionDto> SubmitAsync(SubmitReportDefinitionCommand command, CancellationToken cancellationToken);
    Task<ReportDefinitionVersionDto> DecideAsync(DecideReportDefinitionCommand command, CancellationToken cancellationToken);
    Task<ReportDefinitionVersionDto> ActivateAsync(ActivateReportDefinitionCommand command, CancellationToken cancellationToken);
    Task<ReportDefinitionVersionDto> RetireAsync(RetireReportDefinitionCommand command, CancellationToken cancellationToken);
}

public sealed class ReportDefinitionException(string reasonCode, string message, bool isConflict = false)
    : Exception(message)
{
    public string ReasonCode { get; } = reasonCode;
    public bool IsConflict { get; } = isConflict;
}
