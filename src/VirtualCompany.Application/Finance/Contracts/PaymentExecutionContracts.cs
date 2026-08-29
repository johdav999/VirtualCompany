namespace VirtualCompany.Application.Finance;

public static class PaymentExecutionReasonCodes
{
    public const string Ready = "payment_execution_ready";
    public const string NotFound = "payment_execution_not_found";
    public const string BatchNotApproved = "payment_execution_batch_not_approved";
    public const string ApprovalStale = "payment_execution_approval_stale";
    public const string AlreadyExists = "payment_execution_already_exists";
    public const string ProviderUnsupported = "payment_execution_provider_unsupported";
    public const string ProviderNotConfigured = "payment_execution_provider_not_configured";
    public const string RailUnsupported = "payment_execution_rail_unsupported";
    public const string ConnectionUnavailable = "payment_execution_connection_unavailable";
    public const string ConsentUnavailable = "payment_execution_consent_unavailable";
    public const string CapabilityMissing = "payment_execution_capability_missing";
    public const string AccountMappingMissing = "payment_execution_account_mapping_missing";
    public const string AccountOwnershipUnverified = "payment_execution_account_ownership_unverified";
    public const string AccountAuthorityMismatch = "payment_execution_account_authority_mismatch";
    public const string InsufficientCash = "payment_execution_insufficient_cash";
    public const string BeneficiaryChanged = "payment_execution_beneficiary_changed";
    public const string SubmissionAmbiguous = "payment_execution_submission_ambiguous";
    public const string ProviderRejected = "payment_execution_provider_rejected";
    public const string ProviderUnavailable = "payment_execution_provider_unavailable";
    public const string AuthorizationExpired = "payment_execution_authorization_expired";
    public const string CancellationUnsafe = "payment_execution_cancellation_unsafe";
    public const string StatusReconciliationRequired = "payment_execution_status_reconciliation_required";
    public const string SettlementEvidenceMissing = "payment_execution_settlement_evidence_missing";
    public const string SettlementMismatch = "payment_execution_settlement_mismatch";
    public const string WebhookInvalid = "payment_execution_webhook_invalid";
    public const string WebhookReplay = "payment_execution_webhook_replay";
    public const string VersionConflict = "payment_execution_version_conflict";
    public const string IdempotencyConflict = "payment_execution_idempotency_conflict";
    public const string InvalidLifecycle = "payment_execution_invalid_lifecycle";
    public const string RemittanceUnavailable = "payment_execution_remittance_unavailable";
}

public sealed class PaymentExecutionOptions
{
    public const string SectionName = "Finance:PaymentExecution";
    public string RedirectUri { get; set; } = string.Empty;
    public string WebhookUri { get; set; } = string.Empty;
    public int PollIntervalSeconds { get; set; } = 30;
    public int MaximumStatusPolls { get; set; } = 240;
    public int MaximumProviderAttempts { get; set; } = 5;
    public int AuthorizationExpiryMinutes { get; set; } = 30;
}

public sealed record PaymentInitiationProviderDescriptor(string ProviderKey, string DisplayName,
    bool IsConfigured, IReadOnlyCollection<string> SupportedRails);
public sealed record PaymentProviderInstruction(Guid InstructionId, Guid ObligationLinkId, int Sequence,
    DateOnly ExecutionDate, decimal Amount, string Currency, string PaymentReference,
    string BeneficiaryName, string Rail, string Destination, string ContentHash);
public sealed record PaymentProviderSubmissionRequest(Guid CompanyId, Guid ExecutionId,
    string BusinessIdempotencyKey, string InstitutionId, string ProviderConsentId,
    BankProviderCredentialBundle Credentials, Uri RedirectUri, Uri WebhookUri,
    IReadOnlyList<PaymentProviderInstruction> Instructions);
public sealed record PaymentProviderInstructionStatus(Guid InstructionId, string? ProviderTransactionId,
    string Status, string? ReasonCode, string? ReasonSummary, bool IsFinal);
public sealed record PaymentProviderSubmissionResult(string ProviderPaymentId, Uri? AuthorizationUri,
    string Status, bool IsFinal, bool UpdatesExpected, bool CanCancel, string? ProviderRequestId,
    string? ReasonCode, string? ReasonSummary,
    IReadOnlyList<PaymentProviderInstructionStatus> Instructions);
public sealed record PaymentProviderStatusResult(string ProviderPaymentId, string Status, bool IsFinal,
    bool UpdatesExpected, bool CanCancel, string? ReasonCode, string? ReasonSummary,
    string? DebtorAccountMasked, string? ProviderRequestId,
    IReadOnlyList<PaymentProviderInstructionStatus> Instructions);
public sealed record PaymentProviderCancelResult(string ProviderPaymentId, string Status,
    bool IsFinal, string? ProviderRequestId);
public sealed record PaymentProviderWebhookEvent(string ProviderPaymentId, string WebhookId,
    string Status, bool UpdatesExpected, string? AuthorizationStatus, DateTime TriggeredUtc,
    string PayloadHash);

public interface IPaymentInitiationProvider
{
    PaymentInitiationProviderDescriptor Descriptor { get; }
    Task<PaymentProviderSubmissionResult> SubmitAsync(PaymentProviderSubmissionRequest request,
        CancellationToken cancellationToken);
    Task<PaymentProviderStatusResult> GetStatusAsync(Guid companyId, string providerPaymentId,
        CancellationToken cancellationToken);
    Task<PaymentProviderCancelResult> CancelAsync(Guid companyId, string providerPaymentId,
        CancellationToken cancellationToken);
    Task<PaymentProviderWebhookEvent> ValidateWebhookAsync(string authorizationHeader,
        ReadOnlyMemory<byte> payload, CancellationToken cancellationToken);
}

public interface IPaymentInitiationProviderRegistry
{
    IReadOnlyList<PaymentInitiationProviderDescriptor> GetProviders();
    IPaymentInitiationProvider GetRequired(string providerKey);
}

public sealed class PaymentProviderOperationException : Exception
{
    public PaymentProviderOperationException(string reasonCode, string safeMessage, bool isRetryable,
        bool isAmbiguous = false, string? providerRequestId = null, string? providerPaymentId = null,
        Exception? innerException = null)
        : base(safeMessage, innerException)
    {
        ReasonCode = reasonCode; SafeMessage = safeMessage; IsRetryable = isRetryable;
        IsAmbiguous = isAmbiguous; ProviderRequestId = providerRequestId;
        ProviderPaymentId = providerPaymentId;
    }
    public string ReasonCode { get; }
    public string SafeMessage { get; }
    public bool IsRetryable { get; }
    public bool IsAmbiguous { get; }
    public string? ProviderRequestId { get; }
    public string? ProviderPaymentId { get; }
}

public sealed record QueuePaymentBatchExecutionCommand(Guid CompanyId, Guid BatchId, long ExpectedBatchVersion,
    Guid BankConnectionId, Guid CompanyBankAccountId, string IdempotencyKey, Guid ActorUserId,
    string? CorrelationId = null);
public sealed record CancelPaymentBatchExecutionCommand(Guid CompanyId, Guid ExecutionId,
    long ExpectedVersion, string Reason, string IdempotencyKey, Guid ActorUserId,
    string? CorrelationId = null);
public sealed record ReconcilePaymentBatchExecutionCommand(Guid CompanyId, Guid ExecutionId,
    long ExpectedVersion, string? ProviderPaymentId, string Reason, string IdempotencyKey,
    Guid ActorUserId, string? CorrelationId = null);
public sealed record SettlePaymentBatchExecutionCommand(Guid CompanyId, Guid ExecutionId,
    long ExpectedVersion, Guid BankTransactionId, long ExpectedBankTransactionSourceVersion,
    string IdempotencyKey, Guid ActorUserId, string? CorrelationId = null);
public sealed record RetryPaymentRemittanceCommand(Guid CompanyId, Guid ExecutionId,
    Guid RemittanceId, long ExpectedExecutionVersion, string IdempotencyKey, Guid ActorUserId,
    string? CorrelationId = null);
public sealed record GetPaymentBatchExecutionQuery(Guid CompanyId, Guid ExecutionId);
public sealed record GetPaymentBatchExecutionForBatchQuery(Guid CompanyId, Guid BatchId);
public sealed record PaymentWebhookIngestCommand(string ProviderKey, string AuthorizationHeader,
    ReadOnlyMemory<byte> Payload, string? CorrelationId = null);

public sealed record PaymentExecutionAttemptDto(Guid Id, int AttemptNumber, string Operation,
    string Outcome, string RequestHash, string? ProviderRequestId, string? ReasonCode,
    string? SafeSummary, string RetryClassification, DateTime StartedUtc, DateTime? CompletedUtc);
public sealed record PaymentAcknowledgementDto(Guid Id, string Source, string ProviderStatus,
    string NormalizedStatus, bool IsFinal, bool UpdatesExpected, string? ReasonCode,
    string? SafeSummary, string EvidenceHash, DateTime AcknowledgedUtc);
public sealed record PaymentExecutionInstructionDto(Guid Id, Guid PaymentInstructionId, int Sequence,
    decimal Amount, string Currency, string BeneficiaryName, string MaskedDestination,
    string? ProviderTransactionId, string Status, string? ReasonCode, Guid? PaymentId,
    Guid? PaymentAllocationId);
public sealed record PaymentRemittanceDto(Guid Id, Guid PaymentInstructionId, string BeneficiaryName,
    string? RecipientEmail, string Status, string ContentHash, string? ProviderReference,
    string? ReasonCode, string? SafeSummary, int AttemptCount, DateTime CreatedUtc,
    DateTime? AcceptedUtc);
public sealed record PaymentSettlementDto(Guid Id, Guid BankTransactionId, string BankReference,
    decimal Amount, string Currency, int PaymentCount, int AllocationCount,
    IReadOnlyList<Guid> LedgerEntryIds, DateTime SettledUtc);
public sealed record PaymentExecutionAllowedActionsDto(bool CanOpenBankAuthorization,
    bool CanCancel, bool CanRefreshStatus, bool CanAttachProviderReference,
    bool CanSettle, bool CanRetryRemittance, string? BlockingReasonCode, string Explanation);
public sealed record PaymentBatchExecutionDto(Guid Id, Guid BatchId, string BatchReference,
    int InstructionSetVersion, long Version, string ProviderKey, string ProviderDisplayName,
    Guid BankConnectionId, string InstitutionName, Guid CompanyBankAccountId,
    string BankAccountName, string MaskedBankAccount, string Status, string? ProviderPaymentId,
    Uri? AuthorizationUri, string? ProviderStatus, string RequestHash, string BusinessIdempotencyKey,
    bool UpdatesExpected, bool CanCancelAtProvider, string? ReasonCode, string? SafeSummary,
    DateTime CreatedUtc, DateTime UpdatedUtc, DateTime? ProviderAcceptedUtc,
    DateTime? ProviderCompletedUtc, DateTime? SettledUtc,
    IReadOnlyList<PaymentExecutionAttemptDto> Attempts,
    IReadOnlyList<PaymentAcknowledgementDto> Acknowledgements,
    IReadOnlyList<PaymentExecutionInstructionDto> Instructions,
    IReadOnlyList<PaymentRemittanceDto> Remittances,
    PaymentSettlementDto? Settlement, PaymentExecutionAllowedActionsDto AllowedActions,
    bool IsIdempotentReplay = false);

public interface IPaymentBatchExecutionService
{
    Task<PaymentBatchExecutionDto?> GetAsync(GetPaymentBatchExecutionQuery query, CancellationToken cancellationToken);
    Task<PaymentBatchExecutionDto?> GetForBatchAsync(GetPaymentBatchExecutionForBatchQuery query, CancellationToken cancellationToken);
    Task<PaymentBatchExecutionDto> QueueAsync(QueuePaymentBatchExecutionCommand command, CancellationToken cancellationToken);
    Task<PaymentBatchExecutionDto> CancelAsync(CancelPaymentBatchExecutionCommand command, CancellationToken cancellationToken);
    Task<PaymentBatchExecutionDto> ReconcileAsync(ReconcilePaymentBatchExecutionCommand command, CancellationToken cancellationToken);
    Task<PaymentBatchExecutionDto> SettleAsync(SettlePaymentBatchExecutionCommand command, CancellationToken cancellationToken);
    Task<PaymentBatchExecutionDto> RetryRemittanceAsync(RetryPaymentRemittanceCommand command, CancellationToken cancellationToken);
    Task IngestWebhookAsync(PaymentWebhookIngestCommand command, CancellationToken cancellationToken);
}

public sealed record PaymentBatchSubmissionRequestedMessage(Guid CompanyId, Guid ExecutionId, string? CorrelationId);
public sealed record PaymentBatchStatusPollRequestedMessage(Guid CompanyId, Guid ExecutionId, string? CorrelationId);
public sealed record PaymentBatchCancellationRequestedMessage(Guid CompanyId, Guid ExecutionId, string? CorrelationId);
public sealed record PaymentRemittanceDeliveryRequestedMessage(Guid CompanyId, Guid RemittanceId, string? CorrelationId);

public interface IPaymentBatchExecutionDispatcher
{
    Task DispatchSubmissionAsync(PaymentBatchSubmissionRequestedMessage message, CancellationToken cancellationToken);
    Task DispatchStatusPollAsync(PaymentBatchStatusPollRequestedMessage message, CancellationToken cancellationToken);
    Task DispatchCancellationAsync(PaymentBatchCancellationRequestedMessage message, CancellationToken cancellationToken);
    Task DispatchRemittanceAsync(PaymentRemittanceDeliveryRequestedMessage message, CancellationToken cancellationToken);
}

public sealed class PaymentExecutionException : Exception
{
    public PaymentExecutionException(string reasonCode, string message, bool isConflict = false,
        long? currentVersion = null) : base(message)
    { ReasonCode = reasonCode; IsConflict = isConflict; CurrentVersion = currentVersion; }
    public string ReasonCode { get; }
    public bool IsConflict { get; }
    public long? CurrentVersion { get; }
}
