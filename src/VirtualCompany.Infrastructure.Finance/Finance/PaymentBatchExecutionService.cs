using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

internal sealed class PaymentBatchExecutionService(
    VirtualCompanyDbContext db,
    PaymentExecutionAuthorityValidator authority,
    IPaymentInitiationProviderRegistry providers,
    ICompanyOutboxEnqueuer outbox,
    IBankTransactionCommandService bankTransactions,
    IAuditEventWriter audit,
    PaymentExecutionTelemetry telemetry,
    ICompanyContextAccessor? companyContext,
    TimeProvider time) : IPaymentBatchExecutionService
{
    public Task<PaymentBatchExecutionDto?> GetAsync(GetPaymentBatchExecutionQuery query,
        CancellationToken cancellationToken)
    { EnsureTenant(query.CompanyId); return MapAsync(query.CompanyId, query.ExecutionId, false, cancellationToken); }

    public async Task<PaymentBatchExecutionDto?> GetForBatchAsync(GetPaymentBatchExecutionForBatchQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        var executionId = await db.PaymentBatchExecutions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == query.CompanyId && x.BatchId == query.BatchId)
            .OrderByDescending(x => x.CreatedUtc).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);
        return executionId.HasValue ? await MapAsync(query.CompanyId, executionId.Value, false, cancellationToken) : null;
    }

    public async Task<PaymentBatchExecutionDto> QueueAsync(QueuePaymentBatchExecutionCommand command,
        CancellationToken cancellationToken)
    {
        EnsureCommand(command.CompanyId, command.ActorUserId);
        var requestHash = Hash($"{command.CompanyId:N}|{command.BatchId:N}|{command.ExpectedBatchVersion}|{command.BankConnectionId:N}|{command.CompanyBankAccountId:N}");
        var replay = await db.PaymentBatchExecutions.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId &&
                x.BusinessIdempotencyKey == command.IdempotencyKey, cancellationToken);
        if (replay is not null)
        {
            if (replay.RequestHash != requestHash)
                throw Error(PaymentExecutionReasonCodes.IdempotencyConflict,
                    "The idempotency key was already used for a different payment execution request.", true);
            return (await MapAsync(command.CompanyId, replay.Id, true, cancellationToken))!;
        }
        var snapshot = await authority.ValidateAsync(command.CompanyId, command.BatchId,
            command.ExpectedBatchVersion, command.BankConnectionId, command.CompanyBankAccountId,
            cancellationToken);
        var existing = await db.PaymentBatchExecutions.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.BatchId == command.BatchId &&
                x.InstructionSetVersion == snapshot.Batch.InstructionSetVersion, cancellationToken);
        if (existing is not null)
        {
            if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
                throw Error(PaymentExecutionReasonCodes.AlreadyExists,
                    "This approved instruction version already has a payment execution for a different bank connection or debit account.", true,
                    existing.Version);
            return (await MapAsync(command.CompanyId, existing.Id, true, cancellationToken))!;
        }

        var now = Now();
        var execution = new PaymentBatchExecution(Guid.NewGuid(), command.CompanyId, command.BatchId,
            snapshot.Batch.InstructionSetVersion, snapshot.Approval.Id, snapshot.Connection.Id,
            snapshot.BankAccount.Id, snapshot.Connection.ProviderKey, requestHash, command.IdempotencyKey,
            command.ActorUserId, command.CorrelationId, now);
        db.PaymentBatchExecutions.Add(execution);
        foreach (var instruction in snapshot.Instructions)
            db.PaymentExecutionInstructions.Add(new PaymentExecutionInstruction(Guid.NewGuid(), command.CompanyId,
                execution.Id, instruction.Id, instruction.ObligationLinkId, instruction.Sequence,
                instruction.Amount, instruction.Currency, instruction.BeneficiaryName,
                instruction.MaskedDestination, now));
        outbox.Enqueue(command.CompanyId, CompanyOutboxTopics.PaymentBatchSubmissionRequested,
            new PaymentBatchSubmissionRequestedMessage(command.CompanyId, execution.Id, command.CorrelationId),
            command.CorrelationId, idempotencyKey: $"payment-submit:{command.CompanyId:N}:{execution.Id:N}",
            causationId: command.BatchId.ToString("N"));
        await WriteAuditAsync(command.CompanyId, execution.Id, AuditEventActions.PaymentBatchExecutionQueued,
            AuditEventOutcomes.Requested,
            "The approved instruction version was queued for durable provider submission. No bank outcome is claimed.",
            command.ActorUserId, command.CorrelationId,
            new Dictionary<string, string?> { ["batchId"] = command.BatchId.ToString("D"), ["providerKey"] = execution.ProviderKey, ["instructionSetVersion"] = execution.InstructionSetVersion.ToString() }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        telemetry.Queued(execution.ProviderKey);
        return (await MapAsync(command.CompanyId, execution.Id, false, cancellationToken))!;
    }

    public async Task<PaymentBatchExecutionDto> CancelAsync(CancelPaymentBatchExecutionCommand command,
        CancellationToken cancellationToken)
    {
        EnsureCommand(command.CompanyId, command.ActorUserId);
        var execution = await LoadAsync(command.CompanyId, command.ExecutionId, cancellationToken);
        if (execution.Status == PaymentExecutionStatuses.Cancelled)
            return (await MapAsync(command.CompanyId, execution.Id, true, cancellationToken))!;
        execution.EnsureVersion(command.ExpectedVersion);
        if (execution.Status is (PaymentExecutionStatuses.Queued or PaymentExecutionStatuses.Submitting) &&
            string.IsNullOrWhiteSpace(execution.ProviderPaymentId))
        {
            execution.CancelLocally(command.Reason, Now());
        }
        else if (execution.CanCancelAtProvider && !string.IsNullOrWhiteSpace(execution.ProviderPaymentId))
        {
            outbox.Enqueue(command.CompanyId, CompanyOutboxTopics.PaymentBatchCancellationRequested,
                new PaymentBatchCancellationRequestedMessage(command.CompanyId, execution.Id, command.CorrelationId),
                command.CorrelationId, idempotencyKey: $"payment-cancel:{command.CompanyId:N}:{execution.Id:N}:{execution.Version}",
                causationId: execution.Id.ToString("N"));
        }
        else
        {
            throw Error(PaymentExecutionReasonCodes.CancellationUnsafe,
                "The provider's safe cancellation boundary has passed. Reconcile the bank status instead of issuing a blind cancellation.");
        }
        await WriteAuditAsync(command.CompanyId, execution.Id, AuditEventActions.PaymentBatchExecutionCancelled,
            execution.Status == PaymentExecutionStatuses.Cancelled ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Requested,
            execution.Status == PaymentExecutionStatuses.Cancelled
                ? "The execution was cancelled before any provider submission existed."
                : "A provider cancellation was queued after the provider-specific boundary was rechecked.",
            command.ActorUserId, command.CorrelationId, null, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return (await MapAsync(command.CompanyId, execution.Id, false, cancellationToken))!;
    }

    public async Task<PaymentBatchExecutionDto> ReconcileAsync(ReconcilePaymentBatchExecutionCommand command,
        CancellationToken cancellationToken)
    {
        EnsureCommand(command.CompanyId, command.ActorUserId);
        var execution = await LoadAsync(command.CompanyId, command.ExecutionId, cancellationToken);
        execution.EnsureVersion(command.ExpectedVersion);
        if (!string.IsNullOrWhiteSpace(command.ProviderPaymentId))
        {
            if (string.IsNullOrWhiteSpace(execution.ProviderPaymentId))
                execution.AttachProviderReference(command.ProviderPaymentId, Now());
            else if (!string.Equals(execution.ProviderPaymentId, command.ProviderPaymentId, StringComparison.Ordinal))
                throw Error(PaymentExecutionReasonCodes.IdempotencyConflict,
                    "The retained execution already has a different provider payment reference.", true,
                    execution.Version);
        }
        if (execution.Status is PaymentExecutionStatuses.Rejected or PaymentExecutionStatuses.Cancelled or PaymentExecutionStatuses.Settled)
            throw Error(PaymentExecutionReasonCodes.InvalidLifecycle,
                "A terminal payment execution cannot be queued for another provider status read.");
        if (string.IsNullOrWhiteSpace(execution.ProviderPaymentId))
            throw Error(PaymentExecutionReasonCodes.SubmissionAmbiguous,
                "Record the provider payment reference before status reconciliation can continue.");
        outbox.Enqueue(command.CompanyId, CompanyOutboxTopics.PaymentBatchStatusPollRequested,
            new PaymentBatchStatusPollRequestedMessage(command.CompanyId, execution.Id, command.CorrelationId),
            command.CorrelationId, idempotencyKey: $"payment-status-reconcile:{command.CompanyId:N}:{execution.Id:N}:{command.IdempotencyKey}",
            causationId: execution.Id.ToString("N"));
        await WriteAuditAsync(command.CompanyId, execution.Id,
            AuditEventActions.PaymentBatchExecutionReconciliationRequired, AuditEventOutcomes.Requested,
            "An authorized operator requested a provider status reconciliation without resubmitting money movement.",
            command.ActorUserId, command.CorrelationId,
            new Dictionary<string, string?> { ["reason"] = command.Reason }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        return (await MapAsync(command.CompanyId, execution.Id, false, cancellationToken))!;
    }

    public async Task<PaymentBatchExecutionDto> SettleAsync(SettlePaymentBatchExecutionCommand command,
        CancellationToken cancellationToken)
    {
        EnsureCommand(command.CompanyId, command.ActorUserId);
        var execution = await LoadAsync(command.CompanyId, command.ExecutionId, cancellationToken);
        var priorSettlement = await db.PaymentBatchSettlements.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.ExecutionId == execution.Id,
                cancellationToken);
        if (priorSettlement is not null)
        {
            if (priorSettlement.BankTransactionId != command.BankTransactionId)
                throw Error(PaymentExecutionReasonCodes.IdempotencyConflict,
                    "This execution is already settled against a different retained bank row.", true,
                    execution.Version);
            return (await MapAsync(command.CompanyId, execution.Id, true, cancellationToken))!;
        }
        execution.EnsureVersion(command.ExpectedVersion);
        if (execution.Status != PaymentExecutionStatuses.ProviderCompleted)
            throw Error(PaymentExecutionReasonCodes.SettlementEvidenceMissing,
                "Wait for an authoritative provider-completed status before matching final bank settlement evidence.");
        var instructionRecords = await db.PaymentExecutionInstructions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.ExecutionId == execution.Id)
            .OrderBy(x => x.Sequence).ToListAsync(cancellationToken);
        if (instructionRecords.Any(x => !x.PaymentId.HasValue || !x.PaymentAllocationId.HasValue))
            throw Error(PaymentExecutionReasonCodes.SettlementEvidenceMissing,
                "Provider-completed instructions have not yet materialized payment and allocation evidence.");
        var bank = await db.BankTransactions.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.BankAccount).SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId &&
                x.Id == command.BankTransactionId, cancellationToken)
            ?? throw Error(PaymentExecutionReasonCodes.SettlementEvidenceMissing,
                "The bank settlement row was not found in the active company.");
        var total = instructionRecords.Sum(x => x.Amount);
        if (bank.Amount >= 0 || bank.AbsoluteAmount != total ||
            !string.Equals(bank.Currency, instructionRecords[0].Currency, StringComparison.OrdinalIgnoreCase) ||
            bank.BankAccountId != execution.CompanyBankAccountId)
            throw Error(PaymentExecutionReasonCodes.SettlementMismatch,
                "The bank row direction, amount, currency, or debit account does not agree with the executed batch.");

        await bankTransactions.ReconcileAsync(new(command.CompanyId, bank.Id,
            instructionRecords.Select(x => new BankTransactionPaymentMatchDto(x.PaymentId!.Value, x.Amount)).ToArray(),
            command.ActorUserId, command.ExpectedBankTransactionSourceVersion,
            BankReconciliationHandlingModes.Payment, IdempotencyKey: command.IdempotencyKey,
            CorrelationId: command.CorrelationId), cancellationToken);
        var ledgerIds = await db.BankTransactionCashLedgerLinks.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.BankTransactionId == bank.Id)
            .Select(x => x.LedgerEntryId).Distinct().ToListAsync(cancellationToken);
        var now = Now();
        db.PaymentBatchSettlements.Add(new PaymentBatchSettlement(Guid.NewGuid(), command.CompanyId,
            execution.Id, bank.Id, bank.ReferenceText, bank.AbsoluteAmount, bank.Currency,
            instructionRecords.Select(x => x.PaymentId).Distinct().Count(),
            instructionRecords.Select(x => x.PaymentAllocationId).Distinct().Count(),
            JsonSerializer.Serialize(ledgerIds), command.ActorUserId, now));
        execution.MarkSettled(now);
        await WriteAuditAsync(command.CompanyId, execution.Id, AuditEventActions.PaymentBatchExecutionSettled,
            AuditEventOutcomes.Succeeded,
            "Provider-completed payments were matched to the exact booked bank row and posted through the native accounting boundary.",
            command.ActorUserId, command.CorrelationId,
            new Dictionary<string, string?> { ["bankTransactionId"] = bank.Id.ToString("D"), ["paymentCount"] = instructionRecords.Count.ToString(), ["ledgerEntryCount"] = ledgerIds.Count.ToString() }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        telemetry.Settled(execution.ProviderKey);
        return (await MapAsync(command.CompanyId, execution.Id, false, cancellationToken))!;
    }

    public async Task<PaymentBatchExecutionDto> RetryRemittanceAsync(RetryPaymentRemittanceCommand command,
        CancellationToken cancellationToken)
    {
        EnsureCommand(command.CompanyId, command.ActorUserId);
        var execution = await LoadAsync(command.CompanyId, command.ExecutionId, cancellationToken);
        execution.EnsureVersion(command.ExpectedExecutionVersion);
        var remittance = await db.PaymentRemittances.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.ExecutionId == execution.Id &&
                x.Id == command.RemittanceId, cancellationToken)
            ?? throw Error(PaymentExecutionReasonCodes.RemittanceUnavailable,
                "The remittance was not found in the active company.");
        if (remittance.Status is PaymentRemittanceStatuses.Ready or PaymentRemittanceStatuses.Sending or PaymentRemittanceStatuses.Accepted)
            return (await MapAsync(command.CompanyId, execution.Id, true, cancellationToken))!;
        remittance.Retry(Now());
        outbox.Enqueue(command.CompanyId, CompanyOutboxTopics.PaymentRemittanceDeliveryRequested,
            new PaymentRemittanceDeliveryRequestedMessage(command.CompanyId, remittance.Id, command.CorrelationId),
            command.CorrelationId, idempotencyKey: $"payment-remittance:{command.CompanyId:N}:{remittance.Id:N}:retry:{remittance.AttemptCount + 1}",
            causationId: execution.Id.ToString("N"));
        await db.SaveChangesAsync(cancellationToken);
        return (await MapAsync(command.CompanyId, execution.Id, false, cancellationToken))!;
    }

    public async Task IngestWebhookAsync(PaymentWebhookIngestCommand command,
        CancellationToken cancellationToken)
    {
        var provider = providers.GetRequired(command.ProviderKey);
        var webhook = await provider.ValidateWebhookAsync(command.AuthorizationHeader, command.Payload,
            cancellationToken);
        var execution = await db.PaymentBatchExecutions.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.ProviderKey == command.ProviderKey &&
                x.ProviderPaymentId == webhook.ProviderPaymentId, cancellationToken);
        if (execution is null) return;
        var prior = await db.PaymentProviderWebhookReceipts.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.ProviderKey == command.ProviderKey && x.WebhookId == webhook.WebhookId,
                cancellationToken);
        if (prior is not null)
        {
            if (prior.PayloadHash != webhook.PayloadHash)
                throw Error(PaymentExecutionReasonCodes.WebhookReplay,
                    "A replayed payment webhook carried different evidence.", true);
            return;
        }
        var now = Now();
        db.PaymentProviderWebhookReceipts.Add(new PaymentProviderWebhookReceipt(Guid.NewGuid(), execution.CompanyId,
            execution.Id, command.ProviderKey, webhook.WebhookId, webhook.ProviderPaymentId,
            webhook.Status, webhook.PayloadHash, webhook.TriggeredUtc, now));
        execution.ApplyProviderStatus(webhook.Status, !webhook.UpdatesExpected, webhook.UpdatesExpected,
            false, null, null, now);
        db.PaymentProviderAcknowledgements.Add(new PaymentProviderAcknowledgement(Guid.NewGuid(), execution.CompanyId,
            execution.Id, $"webhook:{webhook.WebhookId}", "webhook", webhook.Status, execution.Status,
            !webhook.UpdatesExpected, webhook.UpdatesExpected, null, null, webhook.PayloadHash, webhook.TriggeredUtc));
        outbox.Enqueue(execution.CompanyId, CompanyOutboxTopics.PaymentBatchStatusPollRequested,
            new PaymentBatchStatusPollRequestedMessage(execution.CompanyId, execution.Id, command.CorrelationId),
            command.CorrelationId, idempotencyKey: $"payment-status-webhook:{execution.CompanyId:N}:{webhook.WebhookId}",
            causationId: execution.Id.ToString("N"));
        await WriteAuditAsync(execution.CompanyId, execution.Id, AuditEventActions.PaymentBatchAcknowledged,
            AuditEventOutcomes.Succeeded,
            "A signed provider status webhook was verified, replay-protected, retained, and queued for authoritative detail retrieval.",
            null, command.CorrelationId,
            new Dictionary<string, string?> { ["providerStatus"] = webhook.Status, ["webhookId"] = webhook.WebhookId }, cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<PaymentBatchExecution> LoadAsync(Guid companyId, Guid executionId,
        CancellationToken cancellationToken) => await db.PaymentBatchExecutions.IgnoreQueryFilters()
        .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == executionId, cancellationToken)
        ?? throw Error(PaymentExecutionReasonCodes.NotFound,
            "The payment execution was not found in the active company.");

    private async Task<PaymentBatchExecutionDto?> MapAsync(Guid companyId, Guid executionId, bool replay,
        CancellationToken cancellationToken)
    {
        var execution = await db.PaymentBatchExecutions.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == executionId, cancellationToken);
        if (execution is null) return null;
        var batch = await db.PaymentBatches.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId && x.Id == execution.BatchId, cancellationToken);
        var connection = await db.BankConnections.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId && x.Id == execution.BankConnectionId, cancellationToken);
        var account = await db.CompanyBankAccounts.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId && x.Id == execution.CompanyBankAccountId, cancellationToken);
        var attempts = await db.PaymentExecutionAttempts.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.ExecutionId == executionId).OrderByDescending(x => x.StartedUtc)
            .Select(x => new PaymentExecutionAttemptDto(x.Id, x.AttemptNumber, x.Operation, x.Outcome,
                x.RequestHash, x.ProviderRequestId, x.ReasonCode, x.SafeSummary, x.RetryClassification,
                x.StartedUtc, x.CompletedUtc)).ToListAsync(cancellationToken);
        var acknowledgements = await db.PaymentProviderAcknowledgements.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.ExecutionId == executionId).OrderBy(x => x.AcknowledgedUtc)
            .Select(x => new PaymentAcknowledgementDto(x.Id, x.Source, x.ProviderStatus,
                x.NormalizedStatus, x.IsFinal, x.UpdatesExpected, x.ReasonCode, x.SafeSummary,
                x.EvidenceHash, x.AcknowledgedUtc)).ToListAsync(cancellationToken);
        var instructions = await db.PaymentExecutionInstructions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.ExecutionId == executionId).OrderBy(x => x.Sequence)
            .Select(x => new PaymentExecutionInstructionDto(x.Id, x.PaymentInstructionId, x.Sequence,
                x.Amount, x.Currency, x.BeneficiaryName, x.MaskedDestination, x.ProviderTransactionId,
                x.Status, x.ReasonCode, x.PaymentId, x.PaymentAllocationId)).ToListAsync(cancellationToken);
        var remittances = await db.PaymentRemittances.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.ExecutionId == executionId).OrderBy(x => x.BeneficiaryName)
            .Select(x => new PaymentRemittanceDto(x.Id, x.PaymentInstructionId, x.BeneficiaryName,
                x.RecipientEmail, x.Status, x.ContentHash, x.ProviderReference, x.ReasonCode,
                x.SafeSummary, x.AttemptCount, x.CreatedUtc, x.AcceptedUtc)).ToListAsync(cancellationToken);
        var settlementRow = await db.PaymentBatchSettlements.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.ExecutionId == executionId, cancellationToken);
        PaymentSettlementDto? settlement = null;
        if (settlementRow is not null)
            settlement = new(settlementRow.Id, settlementRow.BankTransactionId, settlementRow.BankReference,
                settlementRow.Amount, settlementRow.Currency, settlementRow.PaymentCount,
                settlementRow.AllocationCount, ParseLedgerIds(settlementRow.LedgerEntryIdsJson), settlementRow.SettledUtc);
        var providerName = providers.GetProviders().SingleOrDefault(x => x.ProviderKey == execution.ProviderKey)?.DisplayName
            ?? execution.ProviderKey;
        return new(execution.Id, execution.BatchId, batch.Reference, execution.InstructionSetVersion,
            execution.Version, execution.ProviderKey, providerName, execution.BankConnectionId,
            connection.InstitutionName, execution.CompanyBankAccountId, account.DisplayName,
            account.MaskedAccountNumber, execution.Status, execution.ProviderPaymentId,
            Uri.TryCreate(execution.ProviderAuthorizationUri, UriKind.Absolute, out var authorizationUri) ? authorizationUri : null,
            execution.ProviderStatus, execution.RequestHash, execution.BusinessIdempotencyKey,
            execution.UpdatesExpected, execution.CanCancelAtProvider, execution.ReasonCode,
            execution.SafeSummary, execution.CreatedUtc, execution.UpdatedUtc,
            execution.ProviderAcceptedUtc, execution.ProviderCompletedUtc, execution.SettledUtc,
            attempts, acknowledgements, instructions, remittances, settlement,
            Allowed(execution, remittances), replay);
    }

    private static PaymentExecutionAllowedActionsDto Allowed(PaymentBatchExecution execution,
        IReadOnlyCollection<PaymentRemittanceDto> remittances)
    {
        var canOpen = execution.Status == PaymentExecutionStatuses.AwaitingAuthorization &&
            !string.IsNullOrWhiteSpace(execution.ProviderAuthorizationUri);
        var canCancel = execution.Status == PaymentExecutionStatuses.Queued || execution.CanCancelAtProvider;
        var canRefresh = !string.IsNullOrWhiteSpace(execution.ProviderPaymentId) &&
            execution.Status is not (PaymentExecutionStatuses.Rejected or PaymentExecutionStatuses.Cancelled or PaymentExecutionStatuses.Settled);
        var canAttach = execution.Status == PaymentExecutionStatuses.ReconciliationRequired &&
            string.IsNullOrWhiteSpace(execution.ProviderPaymentId);
        var canSettle = execution.Status == PaymentExecutionStatuses.ProviderCompleted;
        var canRetryRemittance = remittances.Any(x => x.Status == PaymentRemittanceStatuses.Failed);
        var reason = execution.Status == PaymentExecutionStatuses.ReconciliationRequired
            ? execution.ReasonCode : execution.Status == PaymentExecutionStatuses.ProviderCompleted
                ? PaymentExecutionReasonCodes.SettlementEvidenceMissing : null;
        var explanation = execution.Status switch
        {
            PaymentExecutionStatuses.Queued => "Approved instructions are queued. The worker will recheck authority before contacting the bank.",
            PaymentExecutionStatuses.AwaitingAuthorization => "Open the bank authorization flow. Provider receipt is not final settlement.",
            PaymentExecutionStatuses.ProviderAccepted or PaymentExecutionStatuses.Processing => "The bank is processing the instructions. Continue status reconciliation; do not create a duplicate batch.",
            PaymentExecutionStatuses.ReconciliationRequired => execution.SafeSummary ?? "The outcome needs an operator reconciliation before any further money movement.",
            PaymentExecutionStatuses.ProviderCompleted => "The provider reports completion. Match the exact booked bank row before final settlement posting.",
            PaymentExecutionStatuses.Settled => "Provider, payment, allocation, bank-row, and journal evidence are reconciled.",
            PaymentExecutionStatuses.Rejected => execution.SafeSummary ?? "The provider rejected the payment instructions.",
            PaymentExecutionStatuses.Cancelled => "The execution was cancelled and retained as evidence.",
            _ => "Payment execution is in progress."
        };
        return new(canOpen, canCancel, canRefresh, canAttach, canSettle, canRetryRemittance,
            reason, explanation);
    }

    private async Task WriteAuditAsync(Guid companyId, Guid executionId, string action, string outcome,
        string rationale, Guid? actor, string? correlationId,
        IReadOnlyDictionary<string, string?>? metadata, CancellationToken cancellationToken) =>
        await audit.WriteAsync(new(companyId, actor.HasValue ? AuditActorTypes.User : AuditActorTypes.System,
            actor, action, AuditTargetTypes.PaymentBatchExecution, executionId.ToString("D"), outcome,
            rationale, [$"payment_execution:{executionId:N}"], metadata, correlationId, Now()), cancellationToken);

    private static IReadOnlyList<Guid> ParseLedgerIds(string json)
    {
        try { return JsonSerializer.Deserialize<Guid[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
    private static PaymentExecutionException Error(string code, string message, bool conflict = false,
        long? version = null) => new(code, message, conflict, version);
    private void EnsureTenant(Guid companyId)
    {
        if (companyId == Guid.Empty) throw new ArgumentException("Company id is required.", nameof(companyId));
        if (companyContext?.CompanyId is Guid active && active != companyId)
            throw new UnauthorizedAccessException("Payment executions are scoped to the active company context.");
    }
    private void EnsureCommand(Guid companyId, Guid actorUserId)
    { EnsureTenant(companyId); if (actorUserId == Guid.Empty) throw new UnauthorizedAccessException("A resolved company user is required."); }
    private DateTime Now() => time.GetUtcNow().UtcDateTime;
}
