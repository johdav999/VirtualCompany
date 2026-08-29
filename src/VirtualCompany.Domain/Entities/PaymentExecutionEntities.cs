using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;

public sealed class PaymentBatchExecution : ICompanyOwnedEntity
{
    private PaymentBatchExecution() { }

    public PaymentBatchExecution(Guid id, Guid companyId, Guid batchId, int instructionSetVersion,
        Guid approvalBindingId, Guid bankConnectionId, Guid companyBankAccountId, string providerKey,
        string requestHash, string businessIdempotencyKey, Guid requestedByUserId,
        string? correlationId, DateTime createdUtc)
    {
        Id = PaymentBatchEntityValues.Id(id);
        CompanyId = PaymentBatchEntityValues.Required(companyId, nameof(companyId));
        BatchId = PaymentBatchEntityValues.Required(batchId, nameof(batchId));
        InstructionSetVersion = instructionSetVersion > 0 ? instructionSetVersion : throw new ArgumentOutOfRangeException(nameof(instructionSetVersion));
        ApprovalBindingId = PaymentBatchEntityValues.Required(approvalBindingId, nameof(approvalBindingId));
        BankConnectionId = PaymentBatchEntityValues.Required(bankConnectionId, nameof(bankConnectionId));
        CompanyBankAccountId = PaymentBatchEntityValues.Required(companyBankAccountId, nameof(companyBankAccountId));
        ProviderKey = PaymentBatchEntityValues.Text(providerKey, nameof(providerKey), 64).ToLowerInvariant();
        RequestHash = PaymentBatchEntityValues.Hash(requestHash, nameof(requestHash));
        BusinessIdempotencyKey = PaymentBatchEntityValues.Text(businessIdempotencyKey, nameof(businessIdempotencyKey), 200);
        RequestedByUserId = PaymentBatchEntityValues.Required(requestedByUserId, nameof(requestedByUserId));
        CorrelationId = PaymentBatchEntityValues.Optional(correlationId, 128);
        Status = PaymentExecutionStatuses.Queued; UpdatesExpected = true; CanCancelAtProvider = false;
        Version = 1; CreatedUtc = UpdatedUtc = PaymentBatchEntityValues.Utc(createdUtc, nameof(createdUtc));
        RowVersion = PaymentBatchEntityValues.ConcurrencyToken();
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid BatchId { get; private set; }
    public int InstructionSetVersion { get; private set; }
    public Guid ApprovalBindingId { get; private set; }
    public Guid BankConnectionId { get; private set; }
    public Guid CompanyBankAccountId { get; private set; }
    public string ProviderKey { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string? ProviderPaymentId { get; private set; }
    public string? ProviderAuthorizationUri { get; private set; }
    public string? ProviderStatus { get; private set; }
    public string RequestHash { get; private set; } = null!;
    public string BusinessIdempotencyKey { get; private set; } = null!;
    public bool UpdatesExpected { get; private set; }
    public bool CanCancelAtProvider { get; private set; }
    public int StatusPollCount { get; private set; }
    public string? ReasonCode { get; private set; }
    public string? SafeSummary { get; private set; }
    public Guid RequestedByUserId { get; private set; }
    public string? CorrelationId { get; private set; }
    public long Version { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? SubmittedUtc { get; private set; }
    public DateTime? ProviderAcceptedUtc { get; private set; }
    public DateTime? ProviderCompletedUtc { get; private set; }
    public DateTime? SettledUtc { get; private set; }
    public DateTime? CancelledUtc { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void BeginSubmission(DateTime utcNow)
    {
        if (Status != PaymentExecutionStatuses.Queued) throw new InvalidOperationException("Only a queued execution can start provider submission.");
        Status = PaymentExecutionStatuses.Submitting; Touch(utcNow);
    }

    public void RecordSubmission(string providerPaymentId, Uri? authorizationUri, string providerStatus,
        bool isFinal, bool updatesExpected, bool canCancel, DateTime utcNow)
    {
        ProviderPaymentId = PaymentBatchEntityValues.Text(providerPaymentId, nameof(providerPaymentId), 256);
        ProviderAuthorizationUri = authorizationUri is null
            ? null
            : PaymentBatchEntityValues.Text(authorizationUri.ToString(), nameof(authorizationUri), 1000);
        SubmittedUtc ??= PaymentBatchEntityValues.Utc(utcNow, nameof(utcNow));
        ApplyProviderStatus(providerStatus, isFinal, updatesExpected, canCancel, null, null, utcNow);
    }

    public void AttachProviderReference(string providerPaymentId, DateTime utcNow)
    {
        if (Status != PaymentExecutionStatuses.ReconciliationRequired || !string.IsNullOrWhiteSpace(ProviderPaymentId))
            throw new InvalidOperationException("A provider reference can only be attached to an unresolved ambiguous submission.");
        ProviderPaymentId = PaymentBatchEntityValues.Text(providerPaymentId, nameof(providerPaymentId), 256);
        Status = PaymentExecutionStatuses.Processing; UpdatesExpected = true; CanCancelAtProvider = false;
        ReasonCode = null; SafeSummary = null; Touch(utcNow);
    }

    public void ApplyProviderStatus(string providerStatus, bool isFinal, bool updatesExpected,
        bool canCancel, string? reasonCode, string? safeSummary, DateTime utcNow)
    {
        ProviderStatus = PaymentBatchEntityValues.Text(providerStatus, nameof(providerStatus), 40).ToUpperInvariant();
        UpdatesExpected = updatesExpected && !isFinal; CanCancelAtProvider = canCancel && !isFinal;
        ReasonCode = PaymentBatchEntityValues.Optional(reasonCode, 100);
        SafeSummary = PaymentBatchEntityValues.Optional(safeSummary, 1000);
        StatusPollCount++;
        Status = NormalizeProviderStatus(ProviderStatus, isFinal);
        if (Status == PaymentExecutionStatuses.ProviderAccepted) ProviderAcceptedUtc ??= PaymentBatchEntityValues.Utc(utcNow, nameof(utcNow));
        if (Status == PaymentExecutionStatuses.ProviderCompleted) ProviderCompletedUtc ??= PaymentBatchEntityValues.Utc(utcNow, nameof(utcNow));
        if (Status == PaymentExecutionStatuses.Cancelled) CancelledUtc ??= PaymentBatchEntityValues.Utc(utcNow, nameof(utcNow));
        Touch(utcNow);
    }

    public void RequireReconciliation(string reasonCode, string safeSummary, DateTime utcNow)
    {
        Status = PaymentExecutionStatuses.ReconciliationRequired;
        ReasonCode = PaymentBatchEntityValues.Text(reasonCode, nameof(reasonCode), 100);
        SafeSummary = PaymentBatchEntityValues.Text(safeSummary, nameof(safeSummary), 1000);
        UpdatesExpected = false; CanCancelAtProvider = false; Touch(utcNow);
    }

    public void RequireProviderReconciliation(string providerPaymentId, string reasonCode,
        string safeSummary, DateTime utcNow)
    {
        var normalizedReference = PaymentBatchEntityValues.Text(providerPaymentId,
            nameof(providerPaymentId), 256);
        if (!string.IsNullOrWhiteSpace(ProviderPaymentId) &&
            !string.Equals(ProviderPaymentId, normalizedReference, StringComparison.Ordinal))
            throw new InvalidOperationException("A different provider payment reference is already retained.");
        ProviderPaymentId = normalizedReference;
        RequireReconciliation(reasonCode, safeSummary, utcNow);
    }

    public void ScheduleSubmissionRetry(string reasonCode, string safeSummary, DateTime utcNow)
    {
        if (Status != PaymentExecutionStatuses.Submitting)
            throw new InvalidOperationException("Only an in-progress provider submission can be safely rescheduled.");
        Status = PaymentExecutionStatuses.Queued;
        ReasonCode = PaymentBatchEntityValues.Text(reasonCode, nameof(reasonCode), 100);
        SafeSummary = PaymentBatchEntityValues.Text(safeSummary, nameof(safeSummary), 1000);
        UpdatesExpected = true; CanCancelAtProvider = false; Touch(utcNow);
    }

    public void Reject(string reasonCode, string safeSummary, DateTime utcNow)
    {
        Status = PaymentExecutionStatuses.Rejected;
        ReasonCode = PaymentBatchEntityValues.Text(reasonCode, nameof(reasonCode), 100);
        SafeSummary = PaymentBatchEntityValues.Text(safeSummary, nameof(safeSummary), 1000);
        UpdatesExpected = false; CanCancelAtProvider = false; Touch(utcNow);
    }

    public void CancelLocally(string reason, DateTime utcNow)
    {
        if (Status is not (PaymentExecutionStatuses.Queued or PaymentExecutionStatuses.Submitting))
            throw new InvalidOperationException("The provider cancellation boundary has already been crossed.");
        Status = PaymentExecutionStatuses.Cancelled; ReasonCode = "cancelled_before_provider_submission";
        SafeSummary = PaymentBatchEntityValues.Text(reason, nameof(reason), 1000);
        UpdatesExpected = false; CanCancelAtProvider = false; CancelledUtc = PaymentBatchEntityValues.Utc(utcNow, nameof(utcNow));
        Touch(utcNow);
    }

    public void MarkSettled(DateTime utcNow)
    {
        if (Status != PaymentExecutionStatuses.ProviderCompleted)
            throw new InvalidOperationException("Only a provider-completed execution can be matched to final bank settlement evidence.");
        Status = PaymentExecutionStatuses.Settled; UpdatesExpected = false; CanCancelAtProvider = false;
        ReasonCode = null; SafeSummary = null; SettledUtc = PaymentBatchEntityValues.Utc(utcNow, nameof(utcNow)); Touch(utcNow);
    }

    public void EnsureVersion(long expectedVersion)
    { if (Version != expectedVersion) throw new InvalidOperationException("The payment execution changed after it was opened."); }

    private static string NormalizeProviderStatus(string status, bool isFinal)
    {
        if (status is "RJCT" or "FAILED") return PaymentExecutionStatuses.Rejected;
        if (status is "CANC" or "CANCELLED") return PaymentExecutionStatuses.Cancelled;
        if (isFinal && status is ("ACSC" or "ACCC" or "ACWC" or "COMPLETED")) return PaymentExecutionStatuses.ProviderCompleted;
        if (status is "RCVD" or "AUTH" or "PENDING_AUTHORIZATION") return PaymentExecutionStatuses.AwaitingAuthorization;
        if (status is "ACTC" or "ACCP" or "ACSP") return PaymentExecutionStatuses.ProviderAccepted;
        return PaymentExecutionStatuses.Processing;
    }

    private void Touch(DateTime utcNow)
    {
        UpdatedUtc = PaymentBatchEntityValues.Utc(utcNow, nameof(utcNow)); Version++;
        RowVersion = PaymentBatchEntityValues.ConcurrencyToken();
    }
}

public sealed class PaymentExecutionAttempt : ICompanyOwnedEntity
{
    private PaymentExecutionAttempt() { }
    public PaymentExecutionAttempt(Guid id, Guid companyId, Guid executionId, int attemptNumber,
        string operation, string requestHash, DateTime startedUtc)
    {
        Id = PaymentBatchEntityValues.Id(id); CompanyId = PaymentBatchEntityValues.Required(companyId, nameof(companyId));
        ExecutionId = PaymentBatchEntityValues.Required(executionId, nameof(executionId));
        AttemptNumber = attemptNumber > 0 ? attemptNumber : throw new ArgumentOutOfRangeException(nameof(attemptNumber));
        Operation = PaymentBatchEntityValues.Text(operation, nameof(operation), 32);
        RequestHash = PaymentBatchEntityValues.Hash(requestHash, nameof(requestHash));
        Outcome = PaymentExecutionAttemptOutcomes.Started; RetryClassification = "none";
        StartedUtc = PaymentBatchEntityValues.Utc(startedUtc, nameof(startedUtc));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ExecutionId { get; private set; }
    public int AttemptNumber { get; private set; } public string Operation { get; private set; } = null!;
    public string Outcome { get; private set; } = null!; public string RequestHash { get; private set; } = null!;
    public string? ProviderRequestId { get; private set; } public string? ReasonCode { get; private set; }
    public string? SafeSummary { get; private set; } public string RetryClassification { get; private set; } = null!;
    public DateTime StartedUtc { get; private set; } public DateTime? CompletedUtc { get; private set; }
    public void Complete(string outcome, string retryClassification, string? providerRequestId,
        string? reasonCode, string? safeSummary, DateTime utcNow)
    {
        if (Outcome != PaymentExecutionAttemptOutcomes.Started) return;
        Outcome = PaymentBatchEntityValues.Text(outcome, nameof(outcome), 32);
        RetryClassification = PaymentBatchEntityValues.Text(retryClassification, nameof(retryClassification), 32);
        ProviderRequestId = PaymentBatchEntityValues.Optional(providerRequestId, 256);
        ReasonCode = PaymentBatchEntityValues.Optional(reasonCode, 100);
        SafeSummary = PaymentBatchEntityValues.Optional(safeSummary, 1000);
        CompletedUtc = PaymentBatchEntityValues.Utc(utcNow, nameof(utcNow));
    }
}

public sealed class PaymentProviderAcknowledgement : ICompanyOwnedEntity
{
    private PaymentProviderAcknowledgement() { }
    public PaymentProviderAcknowledgement(Guid id, Guid companyId, Guid executionId, string eventIdentity,
        string source, string providerStatus, string normalizedStatus, bool isFinal, bool updatesExpected,
        string? reasonCode, string? safeSummary, string evidenceHash, DateTime acknowledgedUtc)
    {
        Id = PaymentBatchEntityValues.Id(id); CompanyId = PaymentBatchEntityValues.Required(companyId, nameof(companyId));
        ExecutionId = PaymentBatchEntityValues.Required(executionId, nameof(executionId));
        EventIdentity = PaymentBatchEntityValues.Text(eventIdentity, nameof(eventIdentity), 256);
        Source = PaymentBatchEntityValues.Text(source, nameof(source), 32); ProviderStatus = PaymentBatchEntityValues.Text(providerStatus, nameof(providerStatus), 40).ToUpperInvariant();
        NormalizedStatus = PaymentBatchEntityValues.Text(normalizedStatus, nameof(normalizedStatus), 40);
        IsFinal = isFinal; UpdatesExpected = updatesExpected; ReasonCode = PaymentBatchEntityValues.Optional(reasonCode, 100);
        SafeSummary = PaymentBatchEntityValues.Optional(safeSummary, 1000); EvidenceHash = PaymentBatchEntityValues.Hash(evidenceHash, nameof(evidenceHash));
        AcknowledgedUtc = PaymentBatchEntityValues.Utc(acknowledgedUtc, nameof(acknowledgedUtc));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ExecutionId { get; private set; }
    public string EventIdentity { get; private set; } = null!; public string Source { get; private set; } = null!;
    public string ProviderStatus { get; private set; } = null!; public string NormalizedStatus { get; private set; } = null!;
    public bool IsFinal { get; private set; } public bool UpdatesExpected { get; private set; }
    public string? ReasonCode { get; private set; } public string? SafeSummary { get; private set; }
    public string EvidenceHash { get; private set; } = null!; public DateTime AcknowledgedUtc { get; private set; }
}

public sealed class PaymentExecutionInstruction : ICompanyOwnedEntity
{
    private PaymentExecutionInstruction() { }
    public PaymentExecutionInstruction(Guid id, Guid companyId, Guid executionId, Guid paymentInstructionId,
        Guid obligationLinkId, int sequence, decimal amount, string currency, string beneficiaryName,
        string maskedDestination, DateTime createdUtc)
    {
        Id = PaymentBatchEntityValues.Id(id); CompanyId = PaymentBatchEntityValues.Required(companyId, nameof(companyId));
        ExecutionId = PaymentBatchEntityValues.Required(executionId, nameof(executionId));
        PaymentInstructionId = PaymentBatchEntityValues.Required(paymentInstructionId, nameof(paymentInstructionId));
        ObligationLinkId = PaymentBatchEntityValues.Required(obligationLinkId, nameof(obligationLinkId));
        Sequence = sequence > 0 ? sequence : throw new ArgumentOutOfRangeException(nameof(sequence));
        Amount = PaymentBatchEntityValues.Positive(amount, nameof(amount)); Currency = PaymentBatchEntityValues.Currency(currency);
        BeneficiaryName = PaymentBatchEntityValues.Text(beneficiaryName, nameof(beneficiaryName), 200);
        MaskedDestination = PaymentBatchEntityValues.Text(maskedDestination, nameof(maskedDestination), 100);
        Status = "queued"; CreatedUtc = UpdatedUtc = PaymentBatchEntityValues.Utc(createdUtc, nameof(createdUtc));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ExecutionId { get; private set; }
    public Guid PaymentInstructionId { get; private set; } public Guid ObligationLinkId { get; private set; }
    public int Sequence { get; private set; } public decimal Amount { get; private set; }
    public string Currency { get; private set; } = null!; public string BeneficiaryName { get; private set; } = null!;
    public string MaskedDestination { get; private set; } = null!; public string? ProviderTransactionId { get; private set; }
    public string Status { get; private set; } = null!; public string? ReasonCode { get; private set; }
    public Guid? PaymentId { get; private set; } public Guid? PaymentAllocationId { get; private set; }
    public DateTime CreatedUtc { get; private set; } public DateTime UpdatedUtc { get; private set; }
    public void RecordStatus(string? providerTransactionId, string status, string? reasonCode, DateTime utcNow)
    {
        ProviderTransactionId = PaymentBatchEntityValues.Optional(providerTransactionId, 256) ?? ProviderTransactionId;
        Status = PaymentBatchEntityValues.Text(status, nameof(status), 40).ToUpperInvariant();
        ReasonCode = PaymentBatchEntityValues.Optional(reasonCode, 100); UpdatedUtc = PaymentBatchEntityValues.Utc(utcNow, nameof(utcNow));
    }
    public void Materialize(Guid paymentId, Guid allocationId, DateTime utcNow)
    {
        if (PaymentId.HasValue && (PaymentId != paymentId || PaymentAllocationId != allocationId))
            throw new InvalidOperationException("A provider instruction already points to different payment evidence.");
        PaymentId = PaymentBatchEntityValues.Required(paymentId, nameof(paymentId));
        PaymentAllocationId = PaymentBatchEntityValues.Required(allocationId, nameof(allocationId));
        UpdatedUtc = PaymentBatchEntityValues.Utc(utcNow, nameof(utcNow));
    }
}

public sealed class PaymentProviderWebhookReceipt : ICompanyOwnedEntity
{
    private PaymentProviderWebhookReceipt() { }
    public PaymentProviderWebhookReceipt(Guid id, Guid companyId, Guid executionId, string providerKey,
        string webhookId, string providerPaymentId, string providerStatus, string payloadHash,
        DateTime triggeredUtc, DateTime receivedUtc)
    {
        Id = PaymentBatchEntityValues.Id(id); CompanyId = PaymentBatchEntityValues.Required(companyId, nameof(companyId));
        ExecutionId = PaymentBatchEntityValues.Required(executionId, nameof(executionId));
        ProviderKey = PaymentBatchEntityValues.Text(providerKey, nameof(providerKey), 64).ToLowerInvariant();
        WebhookId = PaymentBatchEntityValues.Text(webhookId, nameof(webhookId), 256);
        ProviderPaymentId = PaymentBatchEntityValues.Text(providerPaymentId, nameof(providerPaymentId), 256);
        ProviderStatus = PaymentBatchEntityValues.Text(providerStatus, nameof(providerStatus), 40).ToUpperInvariant();
        PayloadHash = PaymentBatchEntityValues.Hash(payloadHash, nameof(payloadHash));
        TriggeredUtc = PaymentBatchEntityValues.Utc(triggeredUtc, nameof(triggeredUtc));
        ReceivedUtc = PaymentBatchEntityValues.Utc(receivedUtc, nameof(receivedUtc));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ExecutionId { get; private set; }
    public string ProviderKey { get; private set; } = null!; public string WebhookId { get; private set; } = null!;
    public string ProviderPaymentId { get; private set; } = null!; public string ProviderStatus { get; private set; } = null!;
    public string PayloadHash { get; private set; } = null!; public DateTime TriggeredUtc { get; private set; }
    public DateTime ReceivedUtc { get; private set; }
}

public sealed class PaymentBatchSettlement : ICompanyOwnedEntity
{
    private PaymentBatchSettlement() { }
    public PaymentBatchSettlement(Guid id, Guid companyId, Guid executionId, Guid bankTransactionId,
        string bankReference, decimal amount, string currency, int paymentCount, int allocationCount,
        string ledgerEntryIdsJson, Guid settledByUserId, DateTime settledUtc)
    {
        Id = PaymentBatchEntityValues.Id(id); CompanyId = PaymentBatchEntityValues.Required(companyId, nameof(companyId));
        ExecutionId = PaymentBatchEntityValues.Required(executionId, nameof(executionId));
        BankTransactionId = PaymentBatchEntityValues.Required(bankTransactionId, nameof(bankTransactionId));
        BankReference = PaymentBatchEntityValues.Text(bankReference, nameof(bankReference), 240);
        Amount = PaymentBatchEntityValues.Positive(amount, nameof(amount)); Currency = PaymentBatchEntityValues.Currency(currency);
        PaymentCount = paymentCount >= 0 ? paymentCount : throw new ArgumentOutOfRangeException(nameof(paymentCount));
        AllocationCount = allocationCount >= 0 ? allocationCount : throw new ArgumentOutOfRangeException(nameof(allocationCount));
        LedgerEntryIdsJson = PaymentBatchEntityValues.Text(ledgerEntryIdsJson, nameof(ledgerEntryIdsJson), 8000);
        SettledByUserId = PaymentBatchEntityValues.Required(settledByUserId, nameof(settledByUserId));
        SettledUtc = PaymentBatchEntityValues.Utc(settledUtc, nameof(settledUtc));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ExecutionId { get; private set; }
    public Guid BankTransactionId { get; private set; } public string BankReference { get; private set; } = null!;
    public decimal Amount { get; private set; } public string Currency { get; private set; } = null!;
    public int PaymentCount { get; private set; } public int AllocationCount { get; private set; }
    public string LedgerEntryIdsJson { get; private set; } = null!; public Guid SettledByUserId { get; private set; }
    public DateTime SettledUtc { get; private set; }
}

public sealed class PaymentRemittance : ICompanyOwnedEntity
{
    private PaymentRemittance() { }
    public PaymentRemittance(Guid id, Guid companyId, Guid executionId, Guid paymentInstructionId,
        string beneficiaryName, string? recipientEmail, string subject, string content,
        string contentHash, DateTime createdUtc)
    {
        Id = PaymentBatchEntityValues.Id(id); CompanyId = PaymentBatchEntityValues.Required(companyId, nameof(companyId));
        ExecutionId = PaymentBatchEntityValues.Required(executionId, nameof(executionId));
        PaymentInstructionId = PaymentBatchEntityValues.Required(paymentInstructionId, nameof(paymentInstructionId));
        BeneficiaryName = PaymentBatchEntityValues.Text(beneficiaryName, nameof(beneficiaryName), 200);
        RecipientEmail = PaymentBatchEntityValues.Optional(recipientEmail, 320);
        Subject = PaymentBatchEntityValues.Text(subject, nameof(subject), 300);
        Content = PaymentBatchEntityValues.Text(content, nameof(content), 20_000);
        ContentHash = PaymentBatchEntityValues.Hash(contentHash, nameof(contentHash));
        Status = string.IsNullOrWhiteSpace(RecipientEmail) ? PaymentRemittanceStatuses.Blocked : PaymentRemittanceStatuses.Ready;
        ReasonCode = string.IsNullOrWhiteSpace(RecipientEmail) ? "remittance_recipient_missing" : null;
        SafeSummary = string.IsNullOrWhiteSpace(RecipientEmail) ? "Add a supplier email address before delivering this remittance." : null;
        CreatedUtc = UpdatedUtc = PaymentBatchEntityValues.Utc(createdUtc, nameof(createdUtc));
    }
    public Guid Id { get; private set; } public Guid CompanyId { get; private set; } public Guid ExecutionId { get; private set; }
    public Guid PaymentInstructionId { get; private set; } public string BeneficiaryName { get; private set; } = null!;
    public string? RecipientEmail { get; private set; } public string Subject { get; private set; } = null!;
    public string Content { get; private set; } = null!; public string ContentHash { get; private set; } = null!;
    public string Status { get; private set; } = null!; public string? ProviderReference { get; private set; }
    public string? ReasonCode { get; private set; } public string? SafeSummary { get; private set; }
    public int AttemptCount { get; private set; } public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; } public DateTime? AcceptedUtc { get; private set; }
    public void Begin(DateTime utcNow) { if (Status is PaymentRemittanceStatuses.Accepted or PaymentRemittanceStatuses.ReconciliationRequired) return; Status = PaymentRemittanceStatuses.Sending; AttemptCount++; UpdatedUtc = PaymentBatchEntityValues.Utc(utcNow, nameof(utcNow)); }
    public void Accept(string providerReference, DateTime utcNow) { Status = PaymentRemittanceStatuses.Accepted; ProviderReference = PaymentBatchEntityValues.Text(providerReference, nameof(providerReference), 500); ReasonCode = null; SafeSummary = null; AcceptedUtc = UpdatedUtc = PaymentBatchEntityValues.Utc(utcNow, nameof(utcNow)); }
    public void Fail(string reasonCode, string summary, bool ambiguous, DateTime utcNow) { Status = ambiguous ? PaymentRemittanceStatuses.ReconciliationRequired : PaymentRemittanceStatuses.Failed; ReasonCode = PaymentBatchEntityValues.Text(reasonCode, nameof(reasonCode), 100); SafeSummary = PaymentBatchEntityValues.Text(summary, nameof(summary), 1000); UpdatedUtc = PaymentBatchEntityValues.Utc(utcNow, nameof(utcNow)); }
    public void Retry(DateTime utcNow) { if (Status != PaymentRemittanceStatuses.Failed) throw new InvalidOperationException("Only a failed remittance can be retried."); Status = PaymentRemittanceStatuses.Ready; ReasonCode = null; SafeSummary = null; UpdatedUtc = PaymentBatchEntityValues.Utc(utcNow, nameof(utcNow)); }
}
