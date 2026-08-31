namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    private static string DefinitionPath(Guid companyId) =>
        $"internal/companies/{companyId}/finance/accounting/report-definitions";

    public Task<IReadOnlyList<ReportSystemTemplateResponse>> GetReportSystemTemplatesAsync(Guid companyId,
        CancellationToken cancellationToken = default) =>
        GetListAsync<ReportSystemTemplateResponse>(companyId, $"{DefinitionPath(companyId)}/templates", cancellationToken);

    public Task<IReadOnlyList<ReportDefinitionSummaryResponse>> GetReportDefinitionsAsync(Guid companyId,
        CancellationToken cancellationToken = default) =>
        GetListAsync<ReportDefinitionSummaryResponse>(companyId, DefinitionPath(companyId), cancellationToken);

    public Task<ReportDefinitionVersionResponse?> GetReportDefinitionVersionAsync(Guid companyId, Guid versionId,
        CancellationToken cancellationToken = default) => GetAsync<ReportDefinitionVersionResponse>(companyId,
            $"{DefinitionPath(companyId)}/versions/{versionId:D}", false, cancellationToken);

    public Task<ReportDefinitionVersionResponse> CopyReportSystemTemplateAsync(Guid companyId,
        CopyReportSystemTemplateRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<CopyReportSystemTemplateRequest, ReportDefinitionVersionResponse>(companyId,
            HttpMethod.Post, $"{DefinitionPath(companyId)}/copy-template", request, cancellationToken);
    }

    public Task<ReportDefinitionVersionResponse> CreateReportDefinitionVersionAsync(Guid companyId, Guid definitionId,
        Guid sourceVersionId, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, ReportDefinitionVersionResponse>(companyId, HttpMethod.Post,
            $"{DefinitionPath(companyId)}/{definitionId:D}/versions",
            new { sourceVersionId, idempotencyKey = $"report-version:{definitionId:N}:{Guid.NewGuid():N}" }, cancellationToken);
    }

    public Task<ReportDefinitionVersionResponse> UpdateReportDefinitionVersionAsync(Guid companyId, Guid versionId,
        UpdateReportDefinitionRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<UpdateReportDefinitionRequest, ReportDefinitionVersionResponse>(companyId,
            HttpMethod.Put, $"{DefinitionPath(companyId)}/versions/{versionId:D}", request, cancellationToken);
    }

    public Task<ReportDefinitionVersionResponse> ValidateReportDefinitionVersionAsync(Guid companyId, Guid versionId,
        int revision, CancellationToken cancellationToken = default) =>
        SendDefinitionRevisionAsync(companyId, versionId, "validate", revision, cancellationToken);

    public Task<ReportDefinitionVersionResponse> SubmitReportDefinitionVersionAsync(Guid companyId, Guid versionId,
        int revision, CancellationToken cancellationToken = default) =>
        SendDefinitionRevisionAsync(companyId, versionId, "submit", revision, cancellationToken);

    public Task<ReportDefinitionVersionResponse> DecideReportDefinitionVersionAsync(Guid companyId, Guid versionId,
        int revision, bool approve, string? decisionNote, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, ReportDefinitionVersionResponse>(companyId, HttpMethod.Post,
            $"{DefinitionPath(companyId)}/versions/{versionId:D}/decision",
            new { expectedRevision = revision, approve, decisionNote,
                idempotencyKey = $"report-decision:{versionId:N}:{revision}:{approve}:{Guid.NewGuid():N}" }, cancellationToken);
    }

    public Task<ReportDefinitionVersionResponse> ActivateReportDefinitionVersionAsync(Guid companyId, Guid versionId,
        int revision, DateOnly effectiveFrom, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, ReportDefinitionVersionResponse>(companyId, HttpMethod.Post,
            $"{DefinitionPath(companyId)}/versions/{versionId:D}/activate",
            new { expectedRevision = revision, effectiveFrom,
                idempotencyKey = $"report-activate:{versionId:N}:{revision}:{effectiveFrom:yyyyMMdd}" }, cancellationToken);
    }

    public Task<ReportDefinitionVersionResponse> RetireReportDefinitionVersionAsync(Guid companyId, Guid versionId,
        int revision, DateOnly effectiveTo, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, ReportDefinitionVersionResponse>(companyId, HttpMethod.Post,
            $"{DefinitionPath(companyId)}/versions/{versionId:D}/retire",
            new { expectedRevision = revision, effectiveTo,
                idempotencyKey = $"report-retire:{versionId:N}:{revision}:{effectiveTo:yyyyMMdd}" }, cancellationToken);
    }

    public Task<CompleteFinancialReportResponse?> PreviewReportDefinitionVersionAsync(Guid companyId, Guid versionId,
        Guid periodId, Guid? comparisonPeriodId = null, CancellationToken cancellationToken = default) =>
        GetAsync<CompleteFinancialReportResponse>(companyId,
            $"{DefinitionPath(companyId)}/versions/{versionId:D}/preview?fiscalPeriodId={periodId:D}" +
            (comparisonPeriodId.HasValue ? $"&comparisonFiscalPeriodId={comparisonPeriodId:D}" : string.Empty),
            false, cancellationToken);

    private Task<ReportDefinitionVersionResponse> SendDefinitionRevisionAsync(Guid companyId, Guid versionId,
        string action, int revision, CancellationToken cancellationToken)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<object, ReportDefinitionVersionResponse>(companyId, HttpMethod.Post,
            $"{DefinitionPath(companyId)}/versions/{versionId:D}/{action}",
            new { expectedRevision = revision,
                idempotencyKey = $"report-{action}:{versionId:N}:{revision}:{Guid.NewGuid():N}" }, cancellationToken);
    }
}

public sealed class ReportSystemTemplateResponse { public string Key { get; set; } = ""; public string Name { get; set; } = ""; public string ReportKind { get; set; } = ""; public string Description { get; set; } = ""; public bool IsStatutoryCandidate { get; set; } }
public sealed class ReportDefinitionSummaryResponse { public Guid Id { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string ReportKind { get; set; } = ""; public string SourceTemplateKey { get; set; } = ""; public int LatestVersionNumber { get; set; } public string LatestStatus { get; set; } = ""; public Guid LatestVersionId { get; set; } public DateOnly? EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; } public int Revision { get; set; } }
public sealed class ReportDefinitionVersionResponse
{
    public Guid DefinitionId { get; set; } public Guid VersionId { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string ReportKind { get; set; } = ""; public string SourceTemplateKey { get; set; } = "";
    public int VersionNumber { get; set; } public string Status { get; set; } = ""; public DateOnly? EffectiveFrom { get; set; } public DateOnly? EffectiveTo { get; set; } public string? DefinitionHash { get; set; } public int Revision { get; set; }
    public DateTime CreatedUtc { get; set; } public DateTime UpdatedUtc { get; set; } public List<ReportDefinitionSectionResponse> Sections { get; set; } = []; public ReportDefinitionComparisonResponse Comparison { get; set; } = new();
    public ReportDefinitionValidationResponse? LatestValidation { get; set; } public ReportDefinitionApprovalResponse? LatestApproval { get; set; }
    public bool CanEdit { get; set; } public bool CanSubmit { get; set; } public bool CanApprove { get; set; } public bool CanActivate { get; set; } public bool CanRetire { get; set; }
}
public sealed class ReportDefinitionSectionResponse { public Guid Id { get; set; } public string Code { get; set; } = ""; public string Label { get; set; } = ""; public int DisplayOrder { get; set; } public List<ReportDefinitionLineResponse> Lines { get; set; } = []; }
public sealed class ReportDefinitionLineResponse { public Guid Id { get; set; } public string Code { get; set; } = ""; public string Label { get; set; } = ""; public string LineType { get; set; } = "detail"; public int DisplayOrder { get; set; } public string? Formula { get; set; } public string SignRule { get; set; } = "normal"; public int Scale { get; set; } = 1; public int Decimals { get; set; } = 2; public bool SuppressZero { get; set; } public string CurrencyMode { get; set; } = "functional"; public Guid? DimensionTypeId { get; set; } public Guid? DimensionMemberId { get; set; } public List<ReportDefinitionAccountGroupResponse> AccountGroups { get; set; } = []; }
public sealed class ReportDefinitionAccountGroupResponse { public Guid Id { get; set; } public string Code { get; set; } = ""; public string Name { get; set; } = ""; public List<Guid> FinanceAccountIds { get; set; } = []; }
public sealed class ReportDefinitionComparisonResponse { public string Mode { get; set; } = "none"; public int PeriodCount { get; set; } = 1; public bool ShowVariance { get; set; } public bool ShowVariancePercent { get; set; } }
public sealed class ReportDefinitionValidationResponse { public Guid Id { get; set; } public bool IsValid { get; set; } public string DefinitionHash { get; set; } = ""; public Guid ValidatedByUserId { get; set; } public DateTime ValidatedUtc { get; set; } public List<ReportDefinitionValidationIssueResponse> Issues { get; set; } = []; }
public sealed class ReportDefinitionValidationIssueResponse { public string Code { get; set; } = ""; public string Severity { get; set; } = ""; public string Explanation { get; set; } = ""; public Guid? LineId { get; set; } public Guid? AccountId { get; set; } }
public sealed class ReportDefinitionApprovalResponse { public Guid Id { get; set; } public string Status { get; set; } = ""; public Guid SubmittedByUserId { get; set; } public DateTime SubmittedUtc { get; set; } public Guid? DecidedByUserId { get; set; } public DateTime? DecidedUtc { get; set; } public string? DecisionNote { get; set; } }
public sealed class CopyReportSystemTemplateRequest { public string TemplateKey { get; set; } = ""; public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string IdempotencyKey { get; set; } = ""; }
public sealed class UpdateReportDefinitionRequest { public string Name { get; set; } = ""; public int ExpectedRevision { get; set; } public string IdempotencyKey { get; set; } = ""; public List<ReportDefinitionSectionInputRequest> Sections { get; set; } = []; public ReportDefinitionComparisonResponse Comparison { get; set; } = new(); }
public sealed class ReportDefinitionSectionInputRequest { public string Code { get; set; } = ""; public string Label { get; set; } = ""; public int DisplayOrder { get; set; } public List<ReportDefinitionLineInputRequest> Lines { get; set; } = []; }
public sealed class ReportDefinitionLineInputRequest { public string Code { get; set; } = ""; public string Label { get; set; } = ""; public string LineType { get; set; } = "detail"; public int DisplayOrder { get; set; } public string? Formula { get; set; } public string SignRule { get; set; } = "normal"; public int Scale { get; set; } = 1; public int Decimals { get; set; } = 2; public bool SuppressZero { get; set; } public string CurrencyMode { get; set; } = "functional"; public Guid? DimensionTypeId { get; set; } public Guid? DimensionMemberId { get; set; } public List<ReportDefinitionAccountGroupInputRequest> AccountGroups { get; set; } = []; }
public sealed class ReportDefinitionAccountGroupInputRequest { public string Code { get; set; } = ""; public string Name { get; set; } = ""; public List<Guid> FinanceAccountIds { get; set; } = []; }
