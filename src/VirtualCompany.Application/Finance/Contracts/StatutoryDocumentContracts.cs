namespace VirtualCompany.Application.Finance;

public static class StatutoryDocumentTypes
{
    public const string CustomerInvoice = "customer_invoice";
    public const string CustomerCredit = "customer_credit_note";
    public const string SupplierInvoice = "supplier_invoice";
    public const string SupplierCredit = "supplier_credit_note";
}

public static class StatutoryDocumentAuthorities
{
    public const string Native = "native";
    public const string Provider = "provider";
    public const string Imported = "imported";
}

public static class StatutoryDocumentReasonCodes
{
    public const string ConfigurationUnavailable = "statutory_document_configuration_unavailable";
    public const string NativeIssuanceUnavailable = "native_statutory_document_issuance_unavailable";
    public const string SeriesNotFound = "statutory_document_series_not_found";
    public const string SeriesConflict = "statutory_document_series_conflict";
    public const string VersionConflict = "statutory_document_version_conflict";
    public const string SourceNotFound = "statutory_document_source_not_found";
    public const string SourceAlreadyIssued = "statutory_document_source_already_issued";
    public const string RequiredFieldMissing = "statutory_document_required_field_missing";
    public const string DateInvalid = "statutory_document_date_invalid";
    public const string TotalsMismatch = "statutory_document_totals_mismatch";
    public const string CreditReferenceRequired = "statutory_document_credit_reference_required";
    public const string CorrectionRequired = "statutory_document_correction_required";
    public const string IdempotencyConflict = "statutory_document_idempotency_conflict";
}

public sealed record StatutoryDocumentLineInput(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal NetAmount,
    decimal VatRate,
    decimal VatAmount);

public sealed record StatutoryDocumentInput(
    string DocumentType,
    string Authority,
    Guid CounterpartyId,
    string CounterpartyLegalName,
    string CounterpartyAddressLine1,
    string CounterpartyPostalCode,
    string CounterpartyCity,
    string CounterpartyCountryCode,
    string? CounterpartyVatIdentifier,
    DateOnly IssueDate,
    DateOnly SupplyDate,
    DateOnly AccountingDate,
    DateOnly DueDate,
    string Currency,
    string PaymentTerms,
    string ExplanatoryText,
    decimal NetTotal,
    decimal VatTotal,
    decimal GrossTotal,
    IReadOnlyList<StatutoryDocumentLineInput> Lines,
    Guid? OriginalIssuedDocumentId = null,
    string? ProviderDocumentNumber = null,
    string? TaxFactsJson = null,
    IReadOnlyList<Guid>? ApprovalIds = null,
    long SourceVersion = 1);

public sealed record StatutoryDocumentPolicyIssueDto(string ReasonCode, string Explanation, string? Field = null);
public sealed record StatutoryDocumentPolicyDecisionDto(bool IsAllowed, IReadOnlyList<StatutoryDocumentPolicyIssueDto> Issues);

public sealed record StatutoryDocumentSeriesDto(
    Guid Id, string Code, string DocumentType, DateOnly FiscalYearStart, DateOnly FiscalYearEnd,
    string Prefix, int NumberWidth, long NextNumber, bool IsActive, long Version,
    DateTime CreatedUtc, DateTime UpdatedUtc);

public sealed record CreateStatutoryDocumentSeriesCommand(
    Guid CompanyId, string Code, string DocumentType, DateOnly FiscalYearStart, DateOnly FiscalYearEnd,
    string Prefix, int NumberWidth, long FirstNumber, Guid ActorUserId, string? CorrelationId = null);

public sealed record UpdateStatutoryDocumentSeriesCommand(
    Guid CompanyId, Guid SeriesId, long ExpectedVersion, string Prefix, int NumberWidth,
    bool IsActive, Guid ActorUserId, string? CorrelationId = null);

public sealed record StatutoryDocumentAllocationDto(
    Guid Id, Guid SeriesId, string SeriesCode, string FiscalYearKey, long Number,
    string FormattedNumber, string Status, string? GapReason, string BusinessKey,
    long SourceVersion, Guid? IssuedDocumentId, Guid ActorUserId, DateTime AllocatedUtc);

public sealed record StatutoryIssuedDocumentDto(
    Guid Id, string DocumentType, string Authority, string DocumentNumber, Guid SourceRecordId,
    long SourceVersion, Guid? SeriesId, string? FiscalYearKey, long? SequenceNumber,
    Guid StatutoryProfileId, long StatutoryProfileVersion, string PolicyPackKey,
    string PolicyPackVersion, string PolicyPackDefinitionHash, string SnapshotHash,
    Guid? OriginalIssuedDocumentId, DateTime IssuedUtc, bool IsImmutable,
    IReadOnlyList<Guid> ApprovalIds, string? RenderedEvidenceReference, string? DeliveryEvidenceReference,
    long EvidenceVersion);

public sealed record PreviewStatutoryDocumentQuery(Guid CompanyId, StatutoryDocumentInput Document);
public sealed record IssueNativeCustomerDocumentCommand(
    Guid CompanyId, Guid SeriesId, string BusinessKey, StatutoryDocumentInput Document,
    Guid ActorUserId, string? CorrelationId = null);
public sealed record RegisterImportedStatutoryDocumentCommand(
    Guid CompanyId, Guid SourceRecordId, string BusinessKey, StatutoryDocumentInput Document,
    Guid ActorUserId, string? CorrelationId = null);
public sealed record RecordStatutoryDocumentGapCommand(
    Guid CompanyId, Guid SeriesId, string BusinessKey, long SourceVersion, string Reason,
    Guid ActorUserId, string? CorrelationId = null);
public sealed record AttachStatutoryDocumentEvidenceCommand(
    Guid CompanyId, Guid IssuedDocumentId, long ExpectedEvidenceVersion,
    string? RenderedEvidenceReference, string? DeliveryEvidenceReference,
    Guid ActorUserId, string? CorrelationId = null);

public interface IStatutoryDocumentPolicy
{
    Task<StatutoryDocumentPolicyDecisionDto> EvaluateAsync(PreviewStatutoryDocumentQuery query, CancellationToken cancellationToken);
}

public interface IStatutoryDocumentService
{
    Task<StatutoryDocumentPolicyDecisionDto> PreviewAsync(PreviewStatutoryDocumentQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<StatutoryDocumentSeriesDto>> ListSeriesAsync(Guid companyId, CancellationToken cancellationToken);
    Task<StatutoryDocumentSeriesDto> CreateSeriesAsync(CreateStatutoryDocumentSeriesCommand command, CancellationToken cancellationToken);
    Task<StatutoryDocumentSeriesDto> UpdateSeriesAsync(UpdateStatutoryDocumentSeriesCommand command, CancellationToken cancellationToken);
    Task<IReadOnlyList<StatutoryDocumentAllocationDto>> ListAllocationsAsync(Guid companyId, Guid? seriesId, CancellationToken cancellationToken);
    Task<StatutoryDocumentAllocationDto> RecordGapAsync(RecordStatutoryDocumentGapCommand command, CancellationToken cancellationToken);
    Task<StatutoryIssuedDocumentDto> IssueNativeCustomerAsync(IssueNativeCustomerDocumentCommand command, CancellationToken cancellationToken);
    Task<StatutoryIssuedDocumentDto> RegisterImportedAsync(RegisterImportedStatutoryDocumentCommand command, CancellationToken cancellationToken);
    Task<StatutoryIssuedDocumentDto> AttachEvidenceAsync(AttachStatutoryDocumentEvidenceCommand command, CancellationToken cancellationToken);
    Task<StatutoryIssuedDocumentDto> GetIssuedAsync(Guid companyId, Guid issuedDocumentId, CancellationToken cancellationToken);
}

public sealed class StatutoryDocumentException : Exception
{
    public StatutoryDocumentException(string reasonCode, string message, bool isConflict = false) : base(message)
    {
        ReasonCode = reasonCode;
        IsConflict = isConflict;
    }

    public string ReasonCode { get; }
    public bool IsConflict { get; }
}
