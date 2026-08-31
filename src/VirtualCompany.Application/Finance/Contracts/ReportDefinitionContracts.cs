namespace VirtualCompany.Application.Finance;

public sealed record ReportSystemTemplateDto(string Key, string Name, string ReportKind, string Description, bool IsStatutoryCandidate);

public sealed record ReportDefinitionSummaryDto(Guid Id, string Code, string Name, string ReportKind,
    string SourceTemplateKey, int LatestVersionNumber, string LatestStatus, Guid LatestVersionId,
    DateOnly? EffectiveFrom, DateOnly? EffectiveTo, int Revision);

public sealed record ReportDefinitionAccountGroupDto(Guid Id, string Code, string Name,
    IReadOnlyList<Guid> FinanceAccountIds);

public sealed record ReportDefinitionLineDto(Guid Id, string Code, string Label, string LineType, int DisplayOrder,
    string? Formula, string SignRule, int Scale, int Decimals, bool SuppressZero, string CurrencyMode,
    Guid? DimensionTypeId, Guid? DimensionMemberId, IReadOnlyList<ReportDefinitionAccountGroupDto> AccountGroups);

public sealed record ReportDefinitionSectionDto(Guid Id, string Code, string Label, int DisplayOrder,
    IReadOnlyList<ReportDefinitionLineDto> Lines);

public sealed record ReportDefinitionComparisonDto(string Mode, int PeriodCount, bool ShowVariance, bool ShowVariancePercent);

public sealed record ReportDefinitionValidationIssueDto(string Code, string Severity, string Explanation,
    Guid? LineId = null, Guid? AccountId = null);

public sealed record ReportDefinitionValidationDto(Guid Id, bool IsValid, string DefinitionHash,
    Guid ValidatedByUserId, DateTime ValidatedUtc, IReadOnlyList<ReportDefinitionValidationIssueDto> Issues);

public sealed record ReportDefinitionApprovalDto(Guid Id, string Status, Guid SubmittedByUserId,
    DateTime SubmittedUtc, Guid? DecidedByUserId, DateTime? DecidedUtc, string? DecisionNote);

public sealed record ReportDefinitionVersionDto(Guid DefinitionId, Guid VersionId, string Code, string Name,
    string ReportKind, string SourceTemplateKey, int VersionNumber, string Status, DateOnly? EffectiveFrom,
    DateOnly? EffectiveTo, string? DefinitionHash, int Revision, DateTime CreatedUtc, DateTime UpdatedUtc,
    IReadOnlyList<ReportDefinitionSectionDto> Sections, ReportDefinitionComparisonDto Comparison,
    ReportDefinitionValidationDto? LatestValidation, ReportDefinitionApprovalDto? LatestApproval,
    bool CanEdit, bool CanSubmit, bool CanApprove, bool CanActivate, bool CanRetire);

public sealed record CopyReportSystemTemplateCommand(Guid CompanyId, string TemplateKey, string Code, string Name,
    Guid ActorUserId, string IdempotencyKey);

public sealed record ReportDefinitionAccountGroupInput(string Code, string Name, IReadOnlyList<Guid> FinanceAccountIds);

public sealed record ReportDefinitionLineInput(string Code, string Label, string LineType, int DisplayOrder,
    string? Formula, string SignRule, int Scale, int Decimals, bool SuppressZero, string CurrencyMode,
    Guid? DimensionTypeId, Guid? DimensionMemberId, IReadOnlyList<ReportDefinitionAccountGroupInput> AccountGroups);

public sealed record ReportDefinitionSectionInput(string Code, string Label, int DisplayOrder,
    IReadOnlyList<ReportDefinitionLineInput> Lines);

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
