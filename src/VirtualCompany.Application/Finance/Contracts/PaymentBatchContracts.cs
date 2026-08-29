namespace VirtualCompany.Application.Finance;

public static class PaymentBatchReasonCodes
{
    public const string Ready = "payment_batch_ready";
    public const string BatchNotFound = "payment_batch_not_found";
    public const string ObligationNotFound = "payment_batch_obligation_not_found";
    public const string ObligationIneligible = "payment_batch_obligation_ineligible";
    public const string ObligationHeld = "payment_batch_obligation_held";
    public const string ObligationDisputed = "payment_batch_obligation_disputed";
    public const string ObligationSettled = "payment_batch_obligation_settled";
    public const string ObligationDuplicate = "payment_batch_obligation_duplicate";
    public const string SourceChanged = "payment_batch_source_changed";
    public const string BeneficiaryMissing = "payment_batch_beneficiary_missing";
    public const string BeneficiaryUnverified = "payment_batch_beneficiary_unverified";
    public const string BeneficiaryChanged = "payment_batch_beneficiary_changed";
    public const string UnsupportedRail = "payment_batch_rail_unsupported";
    public const string UnsupportedCurrency = "payment_batch_currency_unsupported";
    public const string CashAvailabilityUnknown = "payment_batch_cash_availability_unknown";
    public const string InsufficientCash = "payment_batch_insufficient_cash";
    public const string InvalidExecutionDate = "payment_batch_execution_date_invalid";
    public const string ValidationRequired = "payment_batch_validation_required";
    public const string ApprovalPending = "payment_batch_approval_pending";
    public const string ApprovalStale = "payment_batch_approval_stale";
    public const string SegregationOfDuties = "payment_batch_segregation_of_duties";
    public const string VersionConflict = "payment_batch_version_conflict";
    public const string IdempotencyConflict = "payment_batch_idempotency_conflict";
    public const string InvalidLifecycle = "payment_batch_invalid_lifecycle";
    public const string NoObligations = "payment_batch_no_obligations";
}

public sealed class PaymentBatchPolicyOptions
{
    public const string SectionName = "Finance:PaymentBatches";
    public int CutOffHourEuropeStockholm { get; set; } = 14;
    public decimal DualApprovalThreshold { get; set; } = 1_000_000m;
    public string ApprovalRole { get; set; } = "owner";
    public string[] SupportedCurrencies { get; set; } = ["SEK", "EUR"];
    public string[] HolidayDates { get; set; } = [];
}

public sealed record PaymentBatchEligibilityInput(
    string ObligationType,
    decimal Amount,
    string Currency,
    DateOnly DueDate,
    DateOnly? DiscountDate,
    bool IsHeld,
    bool IsDisputed,
    bool IsSettled,
    bool IsDuplicate,
    bool IsBeneficiaryVerified,
    bool IsSourceCurrent,
    string Rail,
    decimal? AvailableCash,
    DateOnly RequestedExecutionDate,
    DateTime UtcNow);

public sealed record PaymentBatchEligibilityDecision(
    bool IsEligible,
    string ReasonCode,
    string Explanation,
    DateOnly RecommendedExecutionDate,
    bool UsesEarlyPaymentDiscount,
    IReadOnlyList<string> Evidence);

public interface IPaymentBatchEligibilityPolicy
{
    PaymentBatchEligibilityDecision Evaluate(PaymentBatchEligibilityInput input);
}

public sealed record RegisterPaymentBeneficiaryCommand(Guid CompanyId, string PartyType, Guid PartyId,
    string DisplayName, string Rail, string Destination, string MaskedDestination, string Currency,
    string VerificationEvidenceReference, string VerificationEvidenceHash, Guid ActorUserId,
    string? CorrelationId = null);

public sealed record CreatePaymentBatchCommand(Guid CompanyId, string Name, DateOnly PlannedExecutionDate,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record AddPaymentBatchObligationCommand(Guid CompanyId, Guid BatchId, string ObligationType,
    Guid SourceId, long ExpectedVersion, string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record RemovePaymentBatchObligationCommand(Guid CompanyId, Guid BatchId, Guid ObligationLinkId,
    long ExpectedVersion, string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record ValidatePaymentBatchCommand(Guid CompanyId, Guid BatchId, long ExpectedVersion,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record SubmitPaymentBatchCommand(Guid CompanyId, Guid BatchId, long ExpectedVersion,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record DecidePaymentBatchCommand(Guid CompanyId, Guid BatchId, long ExpectedVersion,
    string Comment, string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record CancelPaymentBatchCommand(Guid CompanyId, Guid BatchId, long ExpectedVersion,
    string Reason, string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record RegeneratePaymentBatchCommand(Guid CompanyId, Guid BatchId, long ExpectedVersion,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);

public sealed record ListPaymentBatchesQuery(Guid CompanyId, string? Status = null, int Limit = 100);
public sealed record GetPaymentBatchQuery(Guid CompanyId, Guid BatchId);
public sealed record ListEligiblePaymentObligationsQuery(Guid CompanyId, int Limit = 200);
public sealed record CheckPaymentBatchSendReadinessQuery(Guid CompanyId, Guid BatchId);

public sealed record PaymentBeneficiaryProfileDto(Guid Id, string PartyType, Guid PartyId,
    string DisplayName, string Rail, string MaskedDestination, string Currency, int Version,
    string Status, string VerificationEvidenceReference, DateTime VerifiedUtc);

public sealed record EligiblePaymentObligationDto(string ObligationType, Guid SourceId, string SourceReference,
    string BeneficiaryName, string? Rail, string? MaskedDestination, decimal Amount, string Currency,
    DateOnly DueDate, string PaymentReference, bool IsEligible, string ReasonCode, string Explanation,
    DateOnly RecommendedExecutionDate);

public sealed record PaymentBatchTotalDto(string Currency, decimal Amount, decimal? AvailableCash,
    bool HasSufficientCash);
public sealed record PaymentBatchValidationIssueDto(Guid Id, Guid? ObligationLinkId, string Severity,
    string ReasonCode, string Explanation);
public sealed record PaymentBatchValidationResultDto(Guid Id, long EvaluatedBatchVersion,
    int InstructionSetVersion, bool IsValid, string SourceSetHash,
    IReadOnlyList<PaymentBatchTotalDto> Totals, IReadOnlyList<PaymentBatchValidationIssueDto> Issues,
    DateTime CreatedUtc);

public sealed record PaymentBatchObligationDto(Guid Id, string ObligationType, Guid SourceId,
    string SourceReference, string SourceVersion, string SourceHash, decimal Amount, string Currency,
    DateOnly DueDate, string PaymentReference, string BeneficiaryName, string Rail,
    string MaskedDestination, int BeneficiaryVersion, string VerificationEvidenceReference,
    DateTime VerifiedUtc, DateTime CreatedUtc);

public sealed record PaymentInstructionDto(Guid Id, int InstructionSetVersion, int Sequence,
    DateOnly ExecutionDate, decimal Amount, string Currency, string PaymentReference,
    string BeneficiaryName, string Rail, string MaskedDestination, string SourceVersion,
    string ContentHash, string Status, bool IsCurrent, DateTime CreatedUtc);

public sealed record PaymentBatchApprovalDto(Guid BindingId, Guid ApprovalRequestId, string Status,
    int InstructionSetVersion, string SourceSetHash, Guid RequestedByUserId, Guid? DecidedByUserId,
    DateTime CreatedUtc, DateTime? DecidedUtc);

public sealed record PaymentBatchAllowedActionsDto(bool CanAddOrRemove, bool CanValidate,
    bool CanSubmit, bool CanApprove, bool CanReject, bool CanCancel, bool CanRegenerate,
    bool CanCheckSendReadiness, string? BlockingReasonCode, string Explanation);

public sealed record PaymentBatchSummaryDto(Guid Id, string Reference, string Name,
    DateOnly PlannedExecutionDate, string Status, long Version, int InstructionSetVersion,
    int ObligationCount, IReadOnlyList<PaymentBatchTotalDto> Totals, Guid CreatedByUserId,
    Guid? SubmittedByUserId, Guid? ApprovedByUserId, DateTime CreatedUtc, DateTime UpdatedUtc,
    bool IsIdempotentReplay = false);

public sealed record PaymentBatchDetailDto(PaymentBatchSummaryDto Summary,
    IReadOnlyList<PaymentBatchObligationDto> Obligations,
    IReadOnlyList<PaymentInstructionDto> Instructions,
    PaymentBatchValidationResultDto? Validation,
    PaymentBatchApprovalDto? Approval,
    PaymentBatchAllowedActionsDto AllowedActions,
    string InternalApprovalNotice,
    string? ExportArtifactHash = null);

public sealed record PaymentBatchListDto(IReadOnlyList<PaymentBatchSummaryDto> Items,
    int DraftCount, int NeedsValidationCount, int AwaitingApprovalCount,
    IReadOnlyList<PaymentBatchTotalDto> PlannedTotals);

public sealed record PaymentBatchPreviewDto(Guid BatchId, long Version,
    bool CanValidate, DateOnly RecommendedExecutionDate, IReadOnlyList<PaymentBatchTotalDto> Totals,
    IReadOnlyList<PaymentBatchValidationIssueDto> Issues,
    string InternalApprovalNotice);

public sealed record PaymentBatchSendReadinessDto(Guid BatchId, bool IsReady,
    string ReasonCode, string Explanation, int InstructionSetVersion,
    string? ApprovedSourceSetHash, string CurrentSourceSetHash,
    IReadOnlyList<PaymentBatchValidationIssueDto> Issues);

public interface IPaymentBatchService
{
    Task<PaymentBeneficiaryProfileDto> RegisterBeneficiaryAsync(RegisterPaymentBeneficiaryCommand command,
        CancellationToken cancellationToken);
    Task<PaymentBatchListDto> ListAsync(ListPaymentBatchesQuery query, CancellationToken cancellationToken);
    Task<PaymentBatchDetailDto?> GetAsync(GetPaymentBatchQuery query, CancellationToken cancellationToken);
    Task<IReadOnlyList<EligiblePaymentObligationDto>> ListEligibleObligationsAsync(
        ListEligiblePaymentObligationsQuery query, CancellationToken cancellationToken);
    Task<PaymentBatchDetailDto> CreateAsync(CreatePaymentBatchCommand command, CancellationToken cancellationToken);
    Task<PaymentBatchDetailDto> AddObligationAsync(AddPaymentBatchObligationCommand command, CancellationToken cancellationToken);
    Task<PaymentBatchDetailDto> RemoveObligationAsync(RemovePaymentBatchObligationCommand command, CancellationToken cancellationToken);
    Task<PaymentBatchPreviewDto> PreviewAsync(GetPaymentBatchQuery query, CancellationToken cancellationToken);
    Task<PaymentBatchDetailDto> ValidateAsync(ValidatePaymentBatchCommand command, CancellationToken cancellationToken);
    Task<PaymentBatchDetailDto> SubmitAsync(SubmitPaymentBatchCommand command, CancellationToken cancellationToken);
    Task<PaymentBatchDetailDto> ApproveAsync(DecidePaymentBatchCommand command, CancellationToken cancellationToken);
    Task<PaymentBatchDetailDto> RejectAsync(DecidePaymentBatchCommand command, CancellationToken cancellationToken);
    Task<PaymentBatchDetailDto> CancelAsync(CancelPaymentBatchCommand command, CancellationToken cancellationToken);
    Task<PaymentBatchDetailDto> RegenerateAsync(RegeneratePaymentBatchCommand command, CancellationToken cancellationToken);
    Task<PaymentBatchSendReadinessDto> CheckSendReadinessAsync(CheckPaymentBatchSendReadinessQuery query,
        CancellationToken cancellationToken);
}

public sealed class PaymentBatchException : Exception
{
    public PaymentBatchException(string reasonCode, string message, bool isConflict = false,
        long? currentVersion = null) : base(message)
    { ReasonCode = reasonCode; IsConflict = isConflict; CurrentVersion = currentVersion; }
    public string ReasonCode { get; } public bool IsConflict { get; } public long? CurrentVersion { get; }
}
