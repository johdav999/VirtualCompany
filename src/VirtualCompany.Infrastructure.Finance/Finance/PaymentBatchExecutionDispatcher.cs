using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Security;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.BackgroundJobs;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

internal sealed class PaymentBatchExecutionDispatcher(
    VirtualCompanyDbContext db,
    PaymentExecutionAuthorityValidator authority,
    IPaymentInitiationProviderRegistry providers,
    ICompanyOutboxEnqueuer outbox,
    IMailboxTransportRegistry mailboxTransports,
    IFieldEncryptionService encryption,
    IAuditEventWriter audit,
    IOptions<PaymentExecutionOptions> options,
    PaymentExecutionTelemetry telemetry,
    TimeProvider time,
    ILogger<PaymentBatchExecutionDispatcher> logger) : IPaymentBatchExecutionDispatcher
{
    public async Task DispatchSubmissionAsync(PaymentBatchSubmissionRequestedMessage message,
        CancellationToken cancellationToken)
    {
        var execution = await LoadAsync(message.CompanyId, message.ExecutionId, cancellationToken);
        if (execution.Status == PaymentExecutionStatuses.Cancelled ||
            !string.IsNullOrWhiteSpace(execution.ProviderPaymentId)) return;
        if (execution.Status == PaymentExecutionStatuses.Submitting)
        {
            var interrupted = await db.PaymentExecutionAttempts.IgnoreQueryFilters()
                .OrderByDescending(x => x.AttemptNumber).FirstOrDefaultAsync(x => x.CompanyId == message.CompanyId &&
                    x.ExecutionId == execution.Id && x.Operation == PaymentExecutionAttemptOperations.Submit &&
                    x.Outcome == PaymentExecutionAttemptOutcomes.Started, cancellationToken);
            if (interrupted is not null)
            {
                var now = Now();
                interrupted.Complete(PaymentExecutionAttemptOutcomes.Ambiguous, "manual_reconciliation", null,
                    PaymentExecutionReasonCodes.SubmissionAmbiguous,
                    "The worker stopped after provider submission began. Automatic resubmission is blocked.", now);
                execution.RequireReconciliation(PaymentExecutionReasonCodes.SubmissionAmbiguous,
                    "The prior provider submission may have moved money. Locate the provider payment reference before continuing.", now);
                await WriteAuditAsync(execution, AuditEventActions.PaymentBatchExecutionReconciliationRequired,
                    AuditEventOutcomes.Failed,
                    "An interrupted provider write was frozen for operator reconciliation instead of being replayed.",
                    message.CorrelationId, null, cancellationToken);
                await db.SaveChangesAsync(cancellationToken); return;
            }
        }
        if (execution.Status != PaymentExecutionStatuses.Queued) return;

        PaymentExecutionAuthoritySnapshot snapshot;
        try
        {
            snapshot = await authority.ValidateAsync(message.CompanyId, execution.BatchId, null,
                execution.BankConnectionId, execution.CompanyBankAccountId, cancellationToken);
            if (snapshot.Batch.InstructionSetVersion != execution.InstructionSetVersion ||
                snapshot.Approval.Id != execution.ApprovalBindingId)
                throw new PaymentExecutionException(PaymentExecutionReasonCodes.ApprovalStale,
                    "The approved instruction or approval binding changed before provider submission.");
        }
        catch (PaymentExecutionException exception)
        {
            execution.Reject(exception.ReasonCode, exception.Message, Now());
            await WriteAuditAsync(execution, AuditEventActions.PaymentBatchExecutionRejected,
                AuditEventOutcomes.Failed,
                "Provider submission was blocked by the final backend authority recheck.", message.CorrelationId,
                new Dictionary<string, string?> { ["reasonCode"] = exception.ReasonCode }, cancellationToken);
            await db.SaveChangesAsync(cancellationToken); return;
        }

        var attempt = await BeginAttemptAsync(execution, PaymentExecutionAttemptOperations.Submit,
            execution.RequestHash, cancellationToken);
        execution.BeginSubmission(Now());
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            var redirectUri = new Uri(options.Value.RedirectUri, UriKind.Absolute);
            var webhookUri = new Uri(options.Value.WebhookUri, UriKind.Absolute);
            var providerRequest = new PaymentProviderSubmissionRequest(execution.CompanyId, execution.Id,
                execution.BusinessIdempotencyKey, snapshot.Connection.InstitutionId,
                snapshot.Consent.ProviderConsentId, snapshot.Credentials, redirectUri, webhookUri,
                snapshot.Instructions.Select(x => new PaymentProviderInstruction(x.Id, x.ObligationLinkId,
                    x.Sequence, x.ExecutionDate, x.Amount, x.Currency, x.PaymentReference,
                    x.BeneficiaryName, x.Rail, x.Destination, x.ContentHash)).ToArray());
            var result = await snapshot.Provider.SubmitAsync(providerRequest, cancellationToken);
            var now = Now();
            execution.RecordSubmission(result.ProviderPaymentId, result.AuthorizationUri, result.Status,
                result.IsFinal, result.UpdatesExpected, result.CanCancel, now);
            var missingAuthorization = !result.IsFinal && result.AuthorizationUri is null;
            if (missingAuthorization)
                execution.RequireProviderReconciliation(result.ProviderPaymentId,
                    result.ReasonCode ?? PaymentExecutionReasonCodes.StatusReconciliationRequired,
                    result.ReasonSummary ?? "The provider created a payment reference without a usable bank-authorization address.",
                    now);
            attempt.Complete(PaymentExecutionAttemptOutcomes.Succeeded, "none", result.ProviderRequestId,
                result.ReasonCode, result.ReasonSummary, now);
            await ApplyInstructionStatusesAsync(execution, result.Instructions, now, cancellationToken);
            await HandleFinalProviderOutcomeAsync(execution, result.IsFinal, message.CorrelationId,
                now, cancellationToken);
            await AddAcknowledgementAsync(execution, "submission", result.Status, result.IsFinal,
                result.UpdatesExpected, result.ReasonCode, result.ReasonSummary,
                Hash(JsonSerializer.Serialize(result)), now, cancellationToken);
            if (!result.IsFinal || result.UpdatesExpected)
                QueueStatusPoll(execution, message.CorrelationId, "submission", now);
            await WriteAuditAsync(execution, AuditEventActions.PaymentBatchSubmittedToProvider,
                missingAuthorization ? AuditEventOutcomes.Failed : AuditEventOutcomes.Succeeded,
                missingAuthorization
                    ? "The provider returned a payment reference without a safe authorization address. Status reconciliation was queued without resubmitting money movement."
                    : "The provider returned a payment reference and, where required, an authorization address. This is not bank acceptance or settlement.",
                message.CorrelationId,
                new Dictionary<string, string?> { ["providerStatus"] = result.Status, ["providerRequestId"] = result.ProviderRequestId }, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            telemetry.ProviderOperation(execution.ProviderKey, PaymentExecutionAttemptOperations.Submit,
                PaymentExecutionAttemptOutcomes.Succeeded);
        }
        catch (PaymentProviderOperationException exception)
        {
            var now = Now();
            var retryExhausted = exception.IsRetryable &&
                attempt.AttemptNumber >= options.Value.MaximumProviderAttempts;
            if (exception.IsAmbiguous || retryExhausted)
            {
                attempt.Complete(exception.IsAmbiguous
                        ? PaymentExecutionAttemptOutcomes.Ambiguous
                        : PaymentExecutionAttemptOutcomes.RetryableFailure,
                    retryExhausted ? "retry_exhausted" : "manual_reconciliation",
                    exception.ProviderRequestId,
                    retryExhausted ? PaymentExecutionReasonCodes.ProviderUnavailable : exception.ReasonCode,
                    retryExhausted
                        ? "Provider submission remained unavailable after bounded safe retries. Prove that no provider payment exists before creating another approved batch."
                        : exception.SafeMessage,
                    now);
                if (!string.IsNullOrWhiteSpace(exception.ProviderPaymentId))
                {
                    execution.RequireProviderReconciliation(exception.ProviderPaymentId,
                        retryExhausted ? PaymentExecutionReasonCodes.ProviderUnavailable : exception.ReasonCode,
                        retryExhausted
                            ? "Provider submission remained unavailable after bounded safe retries. Prove that no duplicate provider payment exists before continuing."
                            : exception.SafeMessage,
                        now);
                    QueueStatusPoll(execution, message.CorrelationId, "ambiguous_response", now);
                }
                else
                    execution.RequireReconciliation(
                        retryExhausted ? PaymentExecutionReasonCodes.ProviderUnavailable : exception.ReasonCode,
                        retryExhausted
                            ? "Provider submission remained unavailable after bounded safe retries. Prove that no provider payment exists before creating another approved batch."
                            : exception.SafeMessage,
                        now);
            }
            else if (exception.IsRetryable)
            {
                attempt.Complete(PaymentExecutionAttemptOutcomes.RetryableFailure, "bounded_retry",
                    exception.ProviderRequestId, exception.ReasonCode, exception.SafeMessage, now);
                execution.ScheduleSubmissionRetry(exception.ReasonCode, exception.SafeMessage, now);
            }
            else
            {
                attempt.Complete(PaymentExecutionAttemptOutcomes.PermanentFailure, "do_not_retry",
                    exception.ProviderRequestId, exception.ReasonCode, exception.SafeMessage, now);
                execution.Reject(exception.ReasonCode, exception.SafeMessage, now);
            }
            await WriteAuditAsync(execution,
                exception.IsAmbiguous || retryExhausted
                    ? AuditEventActions.PaymentBatchExecutionReconciliationRequired
                    : AuditEventActions.PaymentBatchExecutionRejected,
                AuditEventOutcomes.Failed,
                exception.IsAmbiguous
                    ? "The provider-write outcome is ambiguous and automatic replay was blocked."
                    : retryExhausted
                        ? "Bounded safe submission retries were exhausted and the execution now requires operator evidence before any new money movement."
                    : exception.IsRetryable ? "A provider rate limit safely deferred submission within the bounded outbox retry policy."
                    : "The provider permanently rejected payment submission.",
                message.CorrelationId, new Dictionary<string, string?> { ["reasonCode"] = exception.ReasonCode }, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            telemetry.ProviderOperation(execution.ProviderKey, PaymentExecutionAttemptOperations.Submit,
                exception.IsAmbiguous ? PaymentExecutionAttemptOutcomes.Ambiguous :
                exception.IsRetryable ? PaymentExecutionAttemptOutcomes.RetryableFailure :
                PaymentExecutionAttemptOutcomes.PermanentFailure);
            if (exception.IsAmbiguous || retryExhausted)
                telemetry.Ambiguous(execution.ProviderKey, PaymentExecutionAttemptOperations.Submit);
            if (exception.IsRetryable && !retryExhausted)
                throw new InvalidOperationException(exception.SafeMessage, exception);
        }
    }

    public async Task DispatchStatusPollAsync(PaymentBatchStatusPollRequestedMessage message,
        CancellationToken cancellationToken)
    {
        var execution = await LoadAsync(message.CompanyId, message.ExecutionId, cancellationToken);
        if (string.IsNullOrWhiteSpace(execution.ProviderPaymentId) ||
            execution.Status is PaymentExecutionStatuses.Cancelled or PaymentExecutionStatuses.Rejected or PaymentExecutionStatuses.Settled)
            return;
        var utcNow = Now();
        if (execution.Status == PaymentExecutionStatuses.AwaitingAuthorization &&
            execution.SubmittedUtc is DateTime submittedUtc &&
            submittedUtc.AddMinutes(options.Value.AuthorizationExpiryMinutes) <= utcNow)
        {
            execution.RequireReconciliation(PaymentExecutionReasonCodes.AuthorizationExpired,
                "The unattended bank-authorization window expired. Confirm the provider status before attempting any new payment.", utcNow);
            await WriteAuditAsync(execution, AuditEventActions.PaymentBatchExecutionReconciliationRequired,
                AuditEventOutcomes.Failed,
                "The configured authorization window expired without authoritative completion evidence.",
                message.CorrelationId, new Dictionary<string, string?>
                {
                    ["reasonCode"] = PaymentExecutionReasonCodes.AuthorizationExpired
                }, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            return;
        }
        if (execution.StatusPollCount >= options.Value.MaximumStatusPolls)
        {
            execution.RequireReconciliation(PaymentExecutionReasonCodes.StatusReconciliationRequired,
                "Automatic status polling reached its bounded limit. Reconcile the payment with the provider and bank feed.", Now());
            await db.SaveChangesAsync(cancellationToken); return;
        }
        var requestHash = Hash($"{execution.ProviderKey}|{execution.ProviderPaymentId}|status|{execution.StatusPollCount + 1}");
        var attempt = await BeginAttemptAsync(execution, PaymentExecutionAttemptOperations.Status,
            requestHash, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            var result = await providers.GetRequired(execution.ProviderKey)
                .GetStatusAsync(execution.CompanyId, execution.ProviderPaymentId, cancellationToken);
            var now = Now();
            if (!string.Equals(result.ProviderPaymentId, execution.ProviderPaymentId, StringComparison.Ordinal))
                throw new PaymentProviderOperationException(PaymentExecutionReasonCodes.StatusReconciliationRequired,
                    "The provider status response referenced a different payment.", false, true);
            var account = await db.CompanyBankAccounts.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(x => x.CompanyId == execution.CompanyId && x.Id == execution.CompanyBankAccountId,
                    cancellationToken);
            if (!string.IsNullOrWhiteSpace(result.DebtorAccountMasked) &&
                !SameMaskedAccount(account.MaskedAccountNumber, result.DebtorAccountMasked))
            {
                execution.RequireReconciliation(PaymentExecutionReasonCodes.AccountAuthorityMismatch,
                    "The provider-reported debit account does not match the authorized company account.", now);
                attempt.Complete(PaymentExecutionAttemptOutcomes.Ambiguous, "manual_reconciliation",
                    result.ProviderRequestId, PaymentExecutionReasonCodes.AccountAuthorityMismatch,
                    execution.SafeSummary, now);
                await db.SaveChangesAsync(cancellationToken); return;
            }
            execution.ApplyProviderStatus(result.Status, result.IsFinal, result.UpdatesExpected,
                result.CanCancel, result.ReasonCode, result.ReasonSummary, now);
            await ApplyInstructionStatusesAsync(execution, result.Instructions, now, cancellationToken);
            attempt.Complete(PaymentExecutionAttemptOutcomes.Succeeded, "none", result.ProviderRequestId,
                result.ReasonCode, result.ReasonSummary, now);
            await AddAcknowledgementAsync(execution, "poll", result.Status, result.IsFinal,
                result.UpdatesExpected, result.ReasonCode, result.ReasonSummary,
                Hash(JsonSerializer.Serialize(result)), now, cancellationToken);
            await HandleFinalProviderOutcomeAsync(execution, result.IsFinal, message.CorrelationId,
                now, cancellationToken);
            if (execution.UpdatesExpected && !PaymentExecutionStatuses.IsTerminal(execution.Status))
                QueueStatusPoll(execution, message.CorrelationId, "poll", now);
            await WriteAuditAsync(execution, AuditEventActions.PaymentBatchAcknowledged,
                AuditEventOutcomes.Succeeded,
                execution.Status == PaymentExecutionStatuses.ProviderCompleted
                    ? "The provider reported a supported final status. Bank settlement remains a separate required control."
                    : "The latest provider status was retained without claiming final settlement.",
                message.CorrelationId,
                new Dictionary<string, string?> { ["providerStatus"] = result.Status, ["normalizedStatus"] = execution.Status }, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            telemetry.ProviderOperation(execution.ProviderKey, PaymentExecutionAttemptOperations.Status,
                PaymentExecutionAttemptOutcomes.Succeeded);
        }
        catch (PaymentProviderOperationException exception)
        {
            var now = Now();
            var retryExhausted = exception.IsRetryable &&
                attempt.AttemptNumber >= options.Value.MaximumProviderAttempts;
            attempt.Complete(exception.IsRetryable ? PaymentExecutionAttemptOutcomes.RetryableFailure :
                    exception.IsAmbiguous ? PaymentExecutionAttemptOutcomes.Ambiguous : PaymentExecutionAttemptOutcomes.PermanentFailure,
                retryExhausted ? "retry_exhausted" : exception.IsRetryable ? "bounded_retry" : "manual_reconciliation",
                exception.ProviderRequestId,
                retryExhausted ? PaymentExecutionReasonCodes.ProviderUnavailable : exception.ReasonCode,
                retryExhausted
                    ? "Provider status remained unavailable after bounded retries. Reconcile against provider and bank evidence."
                    : exception.SafeMessage,
                now);
            if (!exception.IsRetryable || retryExhausted)
                execution.RequireReconciliation(
                    retryExhausted ? PaymentExecutionReasonCodes.ProviderUnavailable : exception.ReasonCode,
                    retryExhausted
                        ? "Provider status remained unavailable after bounded retries. Reconcile against provider and bank evidence."
                        : exception.SafeMessage,
                    now);
            await db.SaveChangesAsync(cancellationToken);
            telemetry.ProviderOperation(execution.ProviderKey, PaymentExecutionAttemptOperations.Status,
                exception.IsRetryable ? PaymentExecutionAttemptOutcomes.RetryableFailure :
                exception.IsAmbiguous ? PaymentExecutionAttemptOutcomes.Ambiguous :
                PaymentExecutionAttemptOutcomes.PermanentFailure);
            if (exception.IsAmbiguous || retryExhausted)
                telemetry.Ambiguous(execution.ProviderKey, PaymentExecutionAttemptOperations.Status);
            if (exception.IsRetryable && !retryExhausted)
                throw new InvalidOperationException(exception.SafeMessage, exception);
        }
    }

    public async Task DispatchCancellationAsync(PaymentBatchCancellationRequestedMessage message,
        CancellationToken cancellationToken)
    {
        var execution = await LoadAsync(message.CompanyId, message.ExecutionId, cancellationToken);
        if (string.IsNullOrWhiteSpace(execution.ProviderPaymentId) || !execution.CanCancelAtProvider)
            throw new PermanentBackgroundJobException("The provider cancellation boundary is not available.");
        var attempt = await BeginAttemptAsync(execution, PaymentExecutionAttemptOperations.Cancel,
            Hash($"{execution.ProviderKey}|{execution.ProviderPaymentId}|cancel"), cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        try
        {
            var result = await providers.GetRequired(execution.ProviderKey)
                .CancelAsync(execution.CompanyId, execution.ProviderPaymentId, cancellationToken);
            var now = Now();
            execution.ApplyProviderStatus(result.Status, result.IsFinal, false, false, null, null, now);
            attempt.Complete(PaymentExecutionAttemptOutcomes.Succeeded, "none", result.ProviderRequestId,
                null, null, now);
            await AddAcknowledgementAsync(execution, "cancellation", result.Status, result.IsFinal,
                false, null, null, Hash(JsonSerializer.Serialize(result)), now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (PaymentProviderOperationException exception)
        {
            var now = Now();
            var retryExhausted = exception.IsRetryable &&
                attempt.AttemptNumber >= options.Value.MaximumProviderAttempts;
            attempt.Complete(exception.IsRetryable ? PaymentExecutionAttemptOutcomes.RetryableFailure :
                PaymentExecutionAttemptOutcomes.PermanentFailure,
                retryExhausted ? "retry_exhausted" : exception.IsRetryable ? "bounded_retry" : "do_not_retry",
                exception.ProviderRequestId,
                retryExhausted ? PaymentExecutionReasonCodes.ProviderUnavailable : exception.ReasonCode,
                retryExhausted
                    ? "Provider cancellation remained unavailable after bounded retries. Confirm the bank state before any further action."
                    : exception.SafeMessage,
                now);
            if (!exception.IsRetryable || retryExhausted)
                execution.RequireReconciliation(
                    retryExhausted ? PaymentExecutionReasonCodes.ProviderUnavailable : exception.ReasonCode,
                    retryExhausted
                        ? "Provider cancellation remained unavailable after bounded retries. Confirm the bank state before any further action."
                        : exception.SafeMessage,
                    now);
            await db.SaveChangesAsync(cancellationToken);
            if (exception.IsRetryable && !retryExhausted)
                throw new InvalidOperationException(exception.SafeMessage, exception);
        }
    }

    public async Task DispatchRemittanceAsync(PaymentRemittanceDeliveryRequestedMessage message,
        CancellationToken cancellationToken)
    {
        var remittance = await db.PaymentRemittances.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == message.CompanyId && x.Id == message.RemittanceId,
                cancellationToken) ?? throw new PermanentBackgroundJobException("The remittance no longer exists.");
        if (remittance.Status is PaymentRemittanceStatuses.Accepted or PaymentRemittanceStatuses.ReconciliationRequired)
            return;
        if (string.IsNullOrWhiteSpace(remittance.RecipientEmail))
            throw new PermanentBackgroundJobException("The remittance recipient email is missing.");
        var execution = await LoadAsync(message.CompanyId, remittance.ExecutionId, cancellationToken);
        var attempt = await BeginAttemptAsync(execution, PaymentExecutionAttemptOperations.Remittance,
            remittance.ContentHash, cancellationToken);
        remittance.Begin(Now()); await db.SaveChangesAsync(cancellationToken);
        try
        {
            var connection = await db.MailboxConnections.IgnoreQueryFilters().Where(x =>
                    x.CompanyId == message.CompanyId && x.Purpose == MailboxPurpose.Finance &&
                    x.Status == MailboxConnectionStatus.Active &&
                    x.CapabilityFlags.HasFlag(MailboxCapability.SendMessages))
                .OrderByDescending(x => x.UpdatedUtc).FirstOrDefaultAsync(cancellationToken)
                ?? throw new PermanentBackgroundJobException("Connect a finance mailbox before sending remittance advice.");
            if (connection.Provider != MailboxProvider.StandardEmail)
                throw new PermanentBackgroundJobException("The finance mailbox cannot send remittance advice. Connect a standard SMTP mailbox.");
            var context = StandardMailboxSessionCodec.Decode(StandardMailboxSessionCodec.Create(connection, encryption));
            var outbound = new MailboxOutboundMessage($"<remittance-{remittance.Id:N}@virtualcompany.local>",
                connection.EmailAddress, [remittance.RecipientEmail], [], [], remittance.Subject,
                remittance.Content, null, null, [], []);
            var result = await mailboxTransports.Resolve("mailkit").SendAsync(context, outbound, cancellationToken);
            var now = Now();
            if (result.Outcome == MailboxSubmissionOutcome.Accepted)
            {
                remittance.Accept(result.ProviderReference ?? $"accepted:{remittance.Id:N}", now);
                attempt.Complete(PaymentExecutionAttemptOutcomes.Succeeded, "none", result.ProviderReference,
                    null, null, now);
                await WriteAuditAsync(execution, AuditEventActions.PaymentRemittanceAccepted,
                    AuditEventOutcomes.Succeeded,
                    "The finance mailbox accepted remittance advice. Recipient delivery is not asserted.",
                    message.CorrelationId, new Dictionary<string, string?> { ["remittanceId"] = remittance.Id.ToString("D") }, cancellationToken);
                telemetry.Remittance("accepted");
            }
            else if (result.Outcome == MailboxSubmissionOutcome.Ambiguous)
            {
                remittance.Fail(result.SafeFailureCode ?? "remittance_delivery_ambiguous",
                    "The mailbox outcome is ambiguous. Reconcile the Sent folder before retrying.", true, now);
                attempt.Complete(PaymentExecutionAttemptOutcomes.Ambiguous, "manual_reconciliation",
                    result.ProviderReference, result.SafeFailureCode, result.SafeFailureMessage, now);
                telemetry.Remittance("ambiguous");
            }
            else
            {
                remittance.Fail(result.SafeFailureCode ?? "remittance_delivery_failed",
                    result.SafeFailureMessage ?? "The mailbox did not accept remittance advice.", false, now);
                var permanent = result.Outcome is MailboxSubmissionOutcome.PermanentFailure or MailboxSubmissionOutcome.AuthenticationRequired;
                attempt.Complete(permanent ? PaymentExecutionAttemptOutcomes.PermanentFailure : PaymentExecutionAttemptOutcomes.RetryableFailure,
                    permanent ? "do_not_retry" : "bounded_retry", result.ProviderReference,
                    result.SafeFailureCode, result.SafeFailureMessage, now);
                telemetry.Remittance(permanent ? "permanent_failure" : "retryable_failure");
                await db.SaveChangesAsync(cancellationToken);
                if (permanent) throw new PermanentBackgroundJobException(remittance.SafeSummary ?? "Remittance delivery failed permanently.");
                throw new InvalidOperationException(remittance.SafeSummary ?? "Remittance delivery will retry.");
            }
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (PermanentBackgroundJobException exception)
        {
            if (attempt.Outcome == PaymentExecutionAttemptOutcomes.Started)
            {
                var now = Now();
                remittance.Fail("remittance_configuration_required", exception.Message, false, now);
                attempt.Complete(PaymentExecutionAttemptOutcomes.PermanentFailure, "do_not_retry", null,
                    "remittance_configuration_required", exception.Message, now);
                telemetry.Remittance("permanent_failure");
                await db.SaveChangesAsync(cancellationToken);
            }
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Remittance delivery {RemittanceId} failed for company {CompanyId}.",
                remittance.Id, remittance.CompanyId);
            throw;
        }
    }

    private async Task MaterializeCompletedInstructionsAsync(PaymentBatchExecution execution,
        IReadOnlyList<PaymentExecutionInstruction> records, string? correlationId, DateTime now,
        CancellationToken cancellationToken)
    {
        var batch = await db.PaymentBatches.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.CompanyId == execution.CompanyId && x.Id == execution.BatchId, cancellationToken);
        foreach (var record in records)
        {
            if (record.PaymentId.HasValue) continue;
            var instruction = await db.PaymentInstructions.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(x => x.CompanyId == execution.CompanyId && x.Id == record.PaymentInstructionId,
                    cancellationToken);
            var obligation = await db.PaymentBatchObligations.IgnoreQueryFilters().AsNoTracking()
                .SingleAsync(x => x.CompanyId == execution.CompanyId && x.Id == record.ObligationLinkId,
                    cancellationToken);
            Guid? invoiceId = null; Guid? billId = null; Guid counterpartyId;
            if (obligation.ObligationType == PaymentBatchObligationTypes.SupplierPaymentProposal)
            {
                var proposal = await db.SupplierInvoicePaymentProposals.IgnoreQueryFilters().AsNoTracking()
                    .SingleAsync(x => x.CompanyId == execution.CompanyId && x.Id == obligation.SourceId,
                        cancellationToken);
                billId = proposal.BillId; counterpartyId = proposal.SupplierId;
            }
            else
            {
                var correction = await db.CustomerInvoiceCorrections.IgnoreQueryFilters().AsNoTracking()
                    .Include(x => x.Invoice).SingleAsync(x => x.CompanyId == execution.CompanyId &&
                        x.Id == obligation.SourceId, cancellationToken);
                invoiceId = correction.InvoiceId; counterpartyId = correction.Invoice.CounterpartyId;
            }
            var paymentId = DeterministicGuid($"payment:{execution.CompanyId:N}:{execution.Id:N}:{instruction.Id:N}");
            var allocationId = DeterministicGuid($"allocation:{execution.CompanyId:N}:{execution.Id:N}:{instruction.Id:N}");
            if (!await db.Payments.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == execution.CompanyId && x.Id == paymentId, cancellationToken))
                db.Payments.Add(new Payment(paymentId, execution.CompanyId, PaymentTypes.Outgoing,
                    instruction.Amount, instruction.Currency, now, PaymentMethods.BankTransfer,
                    PaymentStatuses.Completed, $"{batch.Reference} · {instruction.PaymentReference}", now, now));
            if (!await db.PaymentAllocations.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == execution.CompanyId && x.Id == allocationId, cancellationToken))
                db.PaymentAllocations.Add(new PaymentAllocation(allocationId, execution.CompanyId,
                    paymentId, invoiceId, billId, instruction.Amount, instruction.Currency, now, now,
                    idempotencyKey: $"payment-execution:{execution.Id:N}:instruction:{instruction.Id:N}"));
            record.Materialize(paymentId, allocationId, now);
            if (!await db.PaymentRemittances.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == execution.CompanyId &&
                x.ExecutionId == execution.Id && x.PaymentInstructionId == instruction.Id, cancellationToken))
            {
                var recipient = await db.FinanceCounterparties.IgnoreQueryFilters().AsNoTracking()
                    .Where(x => x.CompanyId == execution.CompanyId && x.Id == counterpartyId)
                    .Select(x => x.Email).SingleOrDefaultAsync(cancellationToken);
                var content = $"Payment advice for {instruction.BeneficiaryName}\n\nBatch: {batch.Reference}\nReference: {instruction.PaymentReference}\nAmount: {instruction.Amount:0.00} {instruction.Currency}\nExecution date: {instruction.ExecutionDate:yyyy-MM-dd}\nProvider status: {record.Status}\n\nThis advice records the bank-reported payment status. Receipt by the beneficiary is not asserted.";
                var remittance = new PaymentRemittance(Guid.NewGuid(), execution.CompanyId, execution.Id,
                    instruction.Id, instruction.BeneficiaryName, recipient,
                    $"Payment advice {batch.Reference} / {instruction.PaymentReference}", content,
                    Hash(content), now);
                db.PaymentRemittances.Add(remittance);
                if (remittance.Status == PaymentRemittanceStatuses.Ready)
                    outbox.Enqueue(execution.CompanyId, CompanyOutboxTopics.PaymentRemittanceDeliveryRequested,
                        new PaymentRemittanceDeliveryRequestedMessage(execution.CompanyId, remittance.Id, correlationId),
                        correlationId, idempotencyKey: $"payment-remittance:{execution.CompanyId:N}:{remittance.Id:N}:1",
                        causationId: execution.Id.ToString("N"));
            }
        }
    }

    private async Task HandleFinalProviderOutcomeAsync(PaymentBatchExecution execution, bool isFinal,
        string? correlationId, DateTime now, CancellationToken cancellationToken)
    {
        if (!isFinal) return;
        var records = await db.PaymentExecutionInstructions.IgnoreQueryFilters()
            .Where(x => x.CompanyId == execution.CompanyId && x.ExecutionId == execution.Id)
            .OrderBy(x => x.Sequence).ToListAsync(cancellationToken);
        var completed = records.Where(x => ProviderInstructionCompleted(x.Status)).ToArray();
        var allCompleted = records.Count > 0 && completed.Length == records.Count;

        if (completed.Length > 0)
            await MaterializeCompletedInstructionsAsync(execution, completed, correlationId, now,
                cancellationToken);
        if (execution.Status == PaymentExecutionStatuses.ProviderCompleted && allCompleted)
            return;
        if ((execution.Status is PaymentExecutionStatuses.Rejected or PaymentExecutionStatuses.Cancelled) &&
            completed.Length == 0)
            return;

        execution.RequireReconciliation(PaymentExecutionReasonCodes.StatusReconciliationRequired,
            completed.Length > 0
                ? "The provider returned a partial or internally inconsistent final batch outcome. Completed instructions were retained, but each bank settlement must be reconciled before closure."
                : "The provider marked the payment status final without a supported completed or rejected instruction outcome. Review retained provider evidence before any further money movement.",
            now);
    }

    private async Task ApplyInstructionStatusesAsync(PaymentBatchExecution execution,
        IReadOnlyList<PaymentProviderInstructionStatus> statuses, DateTime now,
        CancellationToken cancellationToken)
    {
        var records = await db.PaymentExecutionInstructions.IgnoreQueryFilters()
            .Where(x => x.CompanyId == execution.CompanyId && x.ExecutionId == execution.Id)
            .ToDictionaryAsync(x => x.PaymentInstructionId, cancellationToken);
        foreach (var status in statuses)
            if (records.TryGetValue(status.InstructionId, out var record))
                record.RecordStatus(status.ProviderTransactionId, status.Status, status.ReasonCode, now);
    }

    private async Task AddAcknowledgementAsync(PaymentBatchExecution execution, string source,
        string providerStatus, bool isFinal, bool updatesExpected, string? reasonCode,
        string? safeSummary, string evidenceHash, DateTime now, CancellationToken cancellationToken)
    {
        var identity = $"{source}:{providerStatus}:{evidenceHash}";
        if (await db.PaymentProviderAcknowledgements.IgnoreQueryFilters().AsNoTracking()
            .AnyAsync(x => x.CompanyId == execution.CompanyId && x.ExecutionId == execution.Id &&
                x.EventIdentity == identity, cancellationToken)) return;
        db.PaymentProviderAcknowledgements.Add(new PaymentProviderAcknowledgement(Guid.NewGuid(),
            execution.CompanyId, execution.Id, identity, source, providerStatus, execution.Status,
            isFinal, updatesExpected, reasonCode, safeSummary, evidenceHash, now));
    }

    private async Task<PaymentExecutionAttempt> BeginAttemptAsync(PaymentBatchExecution execution,
        string operation, string requestHash, CancellationToken cancellationToken)
    {
        var number = await db.PaymentExecutionAttempts.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(x => x.CompanyId == execution.CompanyId && x.ExecutionId == execution.Id &&
                x.Operation == operation, cancellationToken) + 1;
        var attempt = new PaymentExecutionAttempt(Guid.NewGuid(), execution.CompanyId, execution.Id,
            number, operation, requestHash, Now());
        db.PaymentExecutionAttempts.Add(attempt); return attempt;
    }

    private void QueueStatusPoll(PaymentBatchExecution execution, string? correlationId, string source,
        DateTime now)
    {
        var available = now.AddSeconds(Math.Clamp(options.Value.PollIntervalSeconds, 10, 3600));
        outbox.Enqueue(execution.CompanyId, CompanyOutboxTopics.PaymentBatchStatusPollRequested,
            new PaymentBatchStatusPollRequestedMessage(execution.CompanyId, execution.Id, correlationId),
            correlationId, available,
            idempotencyKey: $"payment-status:{execution.CompanyId:N}:{execution.Id:N}:{source}:{execution.StatusPollCount}",
            causationId: execution.Id.ToString("N"));
    }

    private async Task<PaymentBatchExecution> LoadAsync(Guid companyId, Guid executionId,
        CancellationToken cancellationToken) => await db.PaymentBatchExecutions.IgnoreQueryFilters()
        .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == executionId, cancellationToken)
        ?? throw new PermanentBackgroundJobException("The company payment execution no longer exists.");

    private async Task WriteAuditAsync(PaymentBatchExecution execution, string action, string outcome,
        string rationale, string? correlationId, IReadOnlyDictionary<string, string?>? metadata,
        CancellationToken cancellationToken) => await audit.WriteAsync(new(execution.CompanyId,
            AuditActorTypes.System, null, action, AuditTargetTypes.PaymentBatchExecution,
            execution.Id.ToString("D"), outcome, rationale, [$"payment_execution:{execution.Id:N}"],
            metadata, correlationId ?? execution.CorrelationId, Now()), cancellationToken);

    private static bool ProviderInstructionCompleted(string status) => status is "ACSC" or "ACCC" or "ACWC" or "COMPLETED";
    private static bool SameMaskedAccount(string left, string right)
    {
        static string LastFour(string value)
        {
            var compact = new string((value ?? string.Empty).Where(char.IsLetterOrDigit).ToArray());
            return compact.Length <= 4 ? compact : compact[^4..];
        }
        var a = LastFour(left); var b = LastFour(right);
        return a.Length == 4 && b.Length == 4 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }
    private static Guid DeterministicGuid(string value) => new(SHA256.HashData(Encoding.UTF8.GetBytes(value))[..16]);
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
