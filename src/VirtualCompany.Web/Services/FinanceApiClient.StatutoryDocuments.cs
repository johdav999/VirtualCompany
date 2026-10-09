namespace VirtualCompany.Web.Services;

public sealed partial class FinanceApiClient
{
    private static string StatutoryDocumentsBase(Guid companyId) =>
        $"internal/companies/{companyId}/finance/accounting/statutory-documents";

    public Task<StatutoryDocumentPolicyDecisionResponse> PreviewStatutoryDocumentAsync(
        Guid companyId, StatutoryDocumentApiRequest request, CancellationToken cancellationToken = default) =>
        SendCompanyScopedAsync<StatutoryDocumentApiRequest, StatutoryDocumentPolicyDecisionResponse>(companyId,
            HttpMethod.Post, $"{StatutoryDocumentsBase(companyId)}/preview", request, cancellationToken);

    public Task<IReadOnlyList<StatutoryDocumentSeriesResponse>> GetStatutoryDocumentSeriesAsync(
        Guid companyId, CancellationToken cancellationToken = default) =>
        GetListAsync<StatutoryDocumentSeriesResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/statutory-document-series", cancellationToken);

    public Task<StatutoryDocumentSeriesResponse> CreateStatutoryDocumentSeriesAsync(
        Guid companyId, CreateStatutoryDocumentSeriesApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<CreateStatutoryDocumentSeriesApiRequest, StatutoryDocumentSeriesResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/statutory-document-series", request, cancellationToken);
    }

    public Task<StatutoryDocumentSeriesResponse> UpdateStatutoryDocumentSeriesAsync(
        Guid companyId, Guid seriesId, UpdateStatutoryDocumentSeriesApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<UpdateStatutoryDocumentSeriesApiRequest, StatutoryDocumentSeriesResponse>(companyId,
            HttpMethod.Put, $"internal/companies/{companyId}/finance/accounting/statutory-document-series/{seriesId:D}", request, cancellationToken);
    }

    public Task<IReadOnlyList<StatutoryDocumentAllocationResponse>> GetStatutoryDocumentAllocationsAsync(
        Guid companyId, Guid? seriesId = null, CancellationToken cancellationToken = default) =>
        GetListAsync<StatutoryDocumentAllocationResponse>(companyId,
            $"internal/companies/{companyId}/finance/accounting/statutory-document-allocations" +
            (seriesId.HasValue ? $"?seriesId={seriesId.Value:D}" : string.Empty), cancellationToken);

    public Task<StatutoryDocumentAllocationResponse> RecordStatutoryDocumentGapAsync(
        Guid companyId, Guid seriesId, RecordStatutoryDocumentGapApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<RecordStatutoryDocumentGapApiRequest, StatutoryDocumentAllocationResponse>(companyId,
            HttpMethod.Post, $"internal/companies/{companyId}/finance/accounting/statutory-document-series/{seriesId:D}/gaps", request, cancellationToken);
    }

    public Task<StatutoryIssuedDocumentResponse> IssueNativeStatutoryDocumentAsync(
        Guid companyId, IssueNativeStatutoryDocumentApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<IssueNativeStatutoryDocumentApiRequest, StatutoryIssuedDocumentResponse>(companyId,
            HttpMethod.Post, $"{StatutoryDocumentsBase(companyId)}/issue-native", request, cancellationToken);
    }

    public Task<StatutoryIssuedDocumentResponse> RegisterImportedStatutoryDocumentAsync(
        Guid companyId, RegisterImportedStatutoryDocumentApiRequest request, CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<RegisterImportedStatutoryDocumentApiRequest, StatutoryIssuedDocumentResponse>(companyId,
            HttpMethod.Post, $"{StatutoryDocumentsBase(companyId)}/register-imported", request, cancellationToken);
    }

    public Task<StatutoryIssuedDocumentResponse?> GetIssuedStatutoryDocumentAsync(
        Guid companyId, Guid issuedDocumentId, CancellationToken cancellationToken = default) =>
        GetAsync<StatutoryIssuedDocumentResponse>(companyId,
            $"{StatutoryDocumentsBase(companyId)}/{issuedDocumentId:D}", allowNotFound: false, cancellationToken);

    public Task<StatutoryIssuedDocumentResponse> AttachStatutoryDocumentEvidenceAsync(
        Guid companyId, Guid issuedDocumentId, AttachStatutoryDocumentEvidenceApiRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureOnlineMutation();
        return SendCompanyScopedAsync<AttachStatutoryDocumentEvidenceApiRequest, StatutoryIssuedDocumentResponse>(companyId,
            HttpMethod.Post, $"{StatutoryDocumentsBase(companyId)}/{issuedDocumentId:D}/evidence", request, cancellationToken);
    }
}
