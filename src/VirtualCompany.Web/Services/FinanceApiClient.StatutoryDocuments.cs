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

public sealed class StatutoryDocumentApiRequest
{
    public string DocumentType { get; set; } = string.Empty;
    public string Authority { get; set; } = string.Empty;
    public Guid CounterpartyId { get; set; }
    public string CounterpartyLegalName { get; set; } = string.Empty;
    public string CounterpartyAddressLine1 { get; set; } = string.Empty;
    public string CounterpartyPostalCode { get; set; } = string.Empty;
    public string CounterpartyCity { get; set; } = string.Empty;
    public string CounterpartyCountryCode { get; set; } = string.Empty;
    public string? CounterpartyVatIdentifier { get; set; }
    public DateOnly IssueDate { get; set; }
    public DateOnly SupplyDate { get; set; }
    public DateOnly AccountingDate { get; set; }
    public DateOnly DueDate { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string PaymentTerms { get; set; } = string.Empty;
    public string ExplanatoryText { get; set; } = string.Empty;
    public decimal NetTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrossTotal { get; set; }
    public List<StatutoryDocumentLineApiRequest> Lines { get; set; } = [];
    public Guid? OriginalIssuedDocumentId { get; set; }
    public string? ProviderDocumentNumber { get; set; }
    public string? TaxFactsJson { get; set; }
    public List<Guid> ApprovalIds { get; set; } = [];
    public long SourceVersion { get; set; } = 1;
}

public sealed class StatutoryDocumentLineApiRequest
{
    public string Description { get; set; } = string.Empty;
    public decimal Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatRate { get; set; }
    public decimal VatAmount { get; set; }
}

public sealed class CreateStatutoryDocumentSeriesApiRequest
{
    public string Code { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public DateOnly FiscalYearStart { get; set; }
    public DateOnly FiscalYearEnd { get; set; }
    public string Prefix { get; set; } = string.Empty;
    public int NumberWidth { get; set; } = 6;
    public long FirstNumber { get; set; } = 1;
}

public sealed class UpdateStatutoryDocumentSeriesApiRequest
{
    public long ExpectedVersion { get; set; }
    public string Prefix { get; set; } = string.Empty;
    public int NumberWidth { get; set; } = 6;
    public bool IsActive { get; set; } = true;
}

public sealed class RecordStatutoryDocumentGapApiRequest
{
    public string BusinessKey { get; set; } = string.Empty;
    public long SourceVersion { get; set; } = 1;
    public string Reason { get; set; } = string.Empty;
}

public sealed class IssueNativeStatutoryDocumentApiRequest
{
    public Guid SeriesId { get; set; }
    public string BusinessKey { get; set; } = string.Empty;
    public StatutoryDocumentApiRequest Document { get; set; } = new();
}

public sealed class RegisterImportedStatutoryDocumentApiRequest
{
    public Guid SourceRecordId { get; set; }
    public string BusinessKey { get; set; } = string.Empty;
    public StatutoryDocumentApiRequest Document { get; set; } = new();
}

public sealed class AttachStatutoryDocumentEvidenceApiRequest
{
    public long ExpectedEvidenceVersion { get; set; }
    public string? RenderedEvidenceReference { get; set; }
    public string? DeliveryEvidenceReference { get; set; }
}

public sealed class StatutoryDocumentPolicyDecisionResponse
{
    public bool IsAllowed { get; set; }
    public List<StatutoryDocumentPolicyIssueResponse> Issues { get; set; } = [];
}

public sealed class StatutoryDocumentPolicyIssueResponse
{
    public string ReasonCode { get; set; } = string.Empty;
    public string Explanation { get; set; } = string.Empty;
    public string? Field { get; set; }
}

public sealed class StatutoryDocumentSeriesResponse
{
    public Guid Id { get; set; }
    public string Code { get; set; } = string.Empty;
    public string DocumentType { get; set; } = string.Empty;
    public DateOnly FiscalYearStart { get; set; }
    public DateOnly FiscalYearEnd { get; set; }
    public string Prefix { get; set; } = string.Empty;
    public int NumberWidth { get; set; }
    public long NextNumber { get; set; }
    public bool IsActive { get; set; }
    public long Version { get; set; }
    public DateTime CreatedUtc { get; set; }
    public DateTime UpdatedUtc { get; set; }
}

public sealed class StatutoryDocumentAllocationResponse
{
    public Guid Id { get; set; }
    public Guid SeriesId { get; set; }
    public string SeriesCode { get; set; } = string.Empty;
    public string FiscalYearKey { get; set; } = string.Empty;
    public long Number { get; set; }
    public string FormattedNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? GapReason { get; set; }
    public string BusinessKey { get; set; } = string.Empty;
    public long SourceVersion { get; set; }
    public Guid? IssuedDocumentId { get; set; }
    public Guid ActorUserId { get; set; }
    public DateTime AllocatedUtc { get; set; }
}

public sealed class StatutoryIssuedDocumentResponse
{
    public Guid Id { get; set; }
    public string DocumentType { get; set; } = string.Empty;
    public string Authority { get; set; } = string.Empty;
    public string DocumentNumber { get; set; } = string.Empty;
    public Guid SourceRecordId { get; set; }
    public long SourceVersion { get; set; }
    public Guid? SeriesId { get; set; }
    public string? FiscalYearKey { get; set; }
    public long? SequenceNumber { get; set; }
    public Guid StatutoryProfileId { get; set; }
    public long StatutoryProfileVersion { get; set; }
    public string PolicyPackKey { get; set; } = string.Empty;
    public string PolicyPackVersion { get; set; } = string.Empty;
    public string PolicyPackDefinitionHash { get; set; } = string.Empty;
    public string SnapshotHash { get; set; } = string.Empty;
    public Guid? OriginalIssuedDocumentId { get; set; }
    public DateTime IssuedUtc { get; set; }
    public bool IsImmutable { get; set; }
    public List<Guid> ApprovalIds { get; set; } = [];
    public string? RenderedEvidenceReference { get; set; }
    public string? DeliveryEvidenceReference { get; set; }
    public long EvidenceVersion { get; set; }
}
