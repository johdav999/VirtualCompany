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
public sealed class CopyReportSystemTemplateRequest { public string TemplateKey { get; set; } = ""; public string Code { get; set; } = ""; public string Name { get; set; } = ""; public string IdempotencyKey { get; set; } = ""; }
