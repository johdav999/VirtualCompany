using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class SupplierInvoiceCorrectionService : IFinanceSupplierInvoiceCorrectionService
{
    private const string CancellationApprovalType = "supplier_invoice_cancellation";
    private const string CreditNoteApprovalType = "supplier_invoice_credit_note";
    private const string CancellationTaskType = "finance.supplier_invoice_cancellation";
    private const string CreditNoteTaskType = "finance.supplier_invoice_credit_note";
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IApprovalRequestService _approvalRequestService;
    private readonly IReadOnlyDictionary<string, ISupplierInvoiceCorrectionProvider> _providers;
    private readonly TimeProvider _timeProvider;
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly ILogger<SupplierInvoiceCorrectionService>? _logger;

    public SupplierInvoiceCorrectionService(
        VirtualCompanyDbContext dbContext,
        IApprovalRequestService approvalRequestService,
        TimeProvider timeProvider,
        ICompanyContextAccessor? companyContextAccessor = null,
        IEnumerable<ISupplierInvoiceCorrectionProvider>? providers = null,
        ILogger<SupplierInvoiceCorrectionService>? logger = null)
    {
        _dbContext = dbContext;
        _approvalRequestService = approvalRequestService;
        _timeProvider = timeProvider;
        _companyContextAccessor = companyContextAccessor;
        _providers = (providers ?? [])
            .GroupBy(provider => provider.ProviderKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public async Task<SupplierInvoiceCorrectionActionDto> RequestCancellationAsync(
        RequestSupplierInvoiceCancellationCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        var providerKey = NormalizeProviderKey(command.ProviderKey);
        var provider = ResolveProvider(providerKey);
        var bill = await LoadBillAsync(command.CompanyId, command.BillId, cancellationToken);
        ValidateCanCancel(bill);
        var action = await LoadOrCreateActionAsync(
            command.CompanyId,
            bill.Id,
            SupplierInvoiceCorrectionActionTypes.Cancellation,
            SupplierInvoiceCorrectionActionStatuses.CancellationRequested,
            cancellationToken);

        if (action.Status == SupplierInvoiceCorrectionActionStatuses.Cancelled)
        {
            return MapAction(action);
        }

        if (!await EnsureApprovedOrRequestApprovalAsync(
                action,
                bill,
                providerKey,
                command.ActorUserId,
                CancellationTaskType,
                CancellationApprovalType,
                "Approve supplier invoice cancellation",
                $"Review cancellation for supplier bill {bill.BillNumber}. Approval allows cancelling the supplier invoice in Fortnox.",
                "Supplier invoice cancellation requires approval before Fortnox is updated.",
                cancellationToken))
        {
            return MapAction(action);
        }

        var connection = await ResolveActiveConnectionAsync(command.CompanyId, providerKey, cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        action.Mark(
            SupplierInvoiceCorrectionActionStatuses.CancellationRequested,
            providerKey,
            connection.Id,
            command.ActorUserId,
            "Supplier invoice cancellation requested.",
            new JsonObject { ["phase"] = "cancellation_requested" },
            now);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation(
            "Supplier invoice cancellation requested. CompanyId: {CompanyId}. BillId: {BillId}. ActionId: {ActionId}. ProviderKey: {ProviderKey}.",
            command.CompanyId,
            bill.Id,
            action.Id,
            providerKey);

        var request = await BuildProviderRequestAsync(action, bill, connection.Id, command.ActorUserId, providerKey, null, cancellationToken);
        var providerResult = await provider.CancelAsync(request, cancellationToken);
        var result = await RecordProviderResultAsync(command.CompanyId, action, providerResult, "supplier_invoice_cancellation", cancellationToken);
        if (result.Status == SupplierInvoiceCorrectionActionStatuses.Cancelled)
        {
            bill.ApplySyncedSnapshot(
                bill.CounterpartyId,
                bill.ReceivedUtc,
                bill.DueUtc,
                bill.Amount,
                bill.Currency,
                "cancelled",
                bill.SettlementStatus,
                postingStatus: FinanceDocumentPostingStatuses.Cancelled,
                documentKind: bill.DocumentKind,
                providerStatus: bill.ProviderStatus,
                processingStatus: bill.ProcessingStatus,
                paidAmount: bill.PaidAmount);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    public async Task<SupplierInvoiceCorrectionActionDto> RequestCreditNoteAsync(
        RequestSupplierInvoiceCreditNoteCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        var providerKey = NormalizeProviderKey(command.ProviderKey);
        var provider = ResolveProvider(providerKey);
        var bill = await LoadBillAsync(command.CompanyId, command.BillId, cancellationToken);
        ValidateCanCreateCreditNote(bill);
        var action = await LoadOrCreateActionAsync(
            command.CompanyId,
            bill.Id,
            SupplierInvoiceCorrectionActionTypes.CreditNote,
            SupplierInvoiceCorrectionActionStatuses.CreditNoteRequested,
            cancellationToken);

        if (action.Status == SupplierInvoiceCorrectionActionStatuses.CreditNoteCreated)
        {
            return MapAction(action);
        }

        if (!await EnsureApprovedOrRequestApprovalAsync(
                action,
                bill,
                providerKey,
                command.ActorUserId,
                CreditNoteTaskType,
                CreditNoteApprovalType,
                "Approve supplier credit note",
                $"Review credit note creation for supplier bill {bill.BillNumber}. Approval allows creating a linked supplier credit note in Fortnox.",
                "Supplier credit note creation requires approval before Fortnox is updated.",
                cancellationToken,
                command.Reason))
        {
            return MapAction(action);
        }

        var connection = await ResolveActiveConnectionAsync(command.CompanyId, providerKey, cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        action.Mark(
            SupplierInvoiceCorrectionActionStatuses.CreditNoteRequested,
            providerKey,
            connection.Id,
            command.ActorUserId,
            "Supplier credit note requested.",
            new JsonObject { ["phase"] = "credit_note_requested", ["reason"] = command.Reason },
            now);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation(
            "Supplier credit note requested. CompanyId: {CompanyId}. BillId: {BillId}. ActionId: {ActionId}. ProviderKey: {ProviderKey}.",
            command.CompanyId,
            bill.Id,
            action.Id,
            providerKey);

        var request = await BuildProviderRequestAsync(action, bill, connection.Id, command.ActorUserId, providerKey, command.Reason, cancellationToken);
        var providerResult = await provider.CreateCreditNoteAsync(request, cancellationToken);
        return await RecordProviderResultAsync(command.CompanyId, action, providerResult, "supplier_invoice_credit_note", cancellationToken);
    }

    private void EnsureTenant(Guid companyId)
    {
        if (_companyContextAccessor?.CompanyId is { } currentCompanyId && currentCompanyId != companyId)
        {
            throw new UnauthorizedAccessException("The requested company does not match the active tenant context.");
        }
    }

    private static string NormalizeProviderKey(string? providerKey) =>
        string.IsNullOrWhiteSpace(providerKey)
            ? FinanceIntegrationProviderKeys.Fortnox
            : providerKey.Trim().ToLowerInvariant();

    private ISupplierInvoiceCorrectionProvider ResolveProvider(string providerKey) =>
        _providers.TryGetValue(providerKey, out var provider)
            ? provider
            : throw new InvalidOperationException($"Supplier invoice correction provider '{providerKey}' is not available.");

    private async Task<FinanceBill> LoadBillAsync(Guid companyId, Guid billId, CancellationToken cancellationToken) =>
        await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .Include(x => x.Counterparty)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == billId, cancellationToken)
        ?? throw new KeyNotFoundException("Supplier bill not found.");

    private async Task<SupplierInvoiceCorrectionAction> LoadOrCreateActionAsync(
        Guid companyId,
        Guid billId,
        string actionType,
        string initialStatus,
        CancellationToken cancellationToken)
    {
        var action = await _dbContext.SupplierInvoiceCorrectionActions
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.BillId == billId && x.ActionType == actionType, cancellationToken);
        if (action is not null)
        {
            return action;
        }

        action = new SupplierInvoiceCorrectionAction(Guid.NewGuid(), companyId, billId, actionType, initialStatus, _timeProvider.GetUtcNow().UtcDateTime);
        _dbContext.SupplierInvoiceCorrectionActions.Add(action);
        return action;
    }

    private async Task<bool> EnsureApprovedOrRequestApprovalAsync(
        SupplierInvoiceCorrectionAction action,
        FinanceBill bill,
        string providerKey,
        Guid? actorUserId,
        string taskType,
        string approvalType,
        string taskTitlePrefix,
        string taskDescription,
        string rationaleSummary,
        CancellationToken cancellationToken,
        string? reason = null)
    {
        if (action.ApprovalRequestId is Guid approvalRequestId)
        {
            var approval = await _dbContext.ApprovalRequests
                .IgnoreQueryFilters()
                .AsNoTracking()
                .SingleOrDefaultAsync(x => x.CompanyId == action.CompanyId && x.Id == approvalRequestId, cancellationToken)
                ?? throw new InvalidOperationException("Supplier invoice correction approval could not be found.");
            if (approval.Status != ApprovalRequestStatus.Approved)
            {
                return false;
            }

            if (action.ApprovedUtc is null)
            {
                action.MarkApproved(actorUserId, _timeProvider.GetUtcNow().UtcDateTime);
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            return true;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var connection = await ResolveActiveConnectionAsync(action.CompanyId, providerKey, cancellationToken);
        action.Mark(
            action.Status,
            providerKey,
            connection.Id,
            actorUserId,
            "Approval requested. No Fortnox change has been made yet.",
            new JsonObject
            {
                ["phase"] = "approval_requested",
                ["provider"] = providerKey,
                ["reason"] = reason
            },
            now);

        var actorId = actorUserId is { } userId && userId != Guid.Empty ? userId : action.CompanyId;
        var task = new WorkTask(
            Guid.NewGuid(),
            action.CompanyId,
            taskType,
            $"{taskTitlePrefix} for {bill.Counterparty.Name}",
            taskDescription,
            WorkTaskPriority.Normal,
            assignedAgentId: null,
            parentTaskId: null,
            createdByActorType: "human",
            createdByActorId: actorId,
            inputPayload: BuildApprovalTaskInput(action, bill, reason),
            rationaleSummary: rationaleSummary,
            correlationId: BuildCorrelationId(action.CompanyId, action.BillId, action.ActionType),
            sourceType: WorkTaskSourceTypes.User,
            triggerSource: "finance_supplier_bill",
            creationReason: rationaleSummary,
            triggerEventId: action.BillId.ToString("N"),
            status: WorkTaskStatus.AwaitingApproval);
        task.SetDueDate(bill.DueUtc);

        await ExecuteInTransactionAsync(async () =>
        {
            _dbContext.WorkTasks.Add(task);
            await _dbContext.SaveChangesAsync(cancellationToken);
            var approval = await _approvalRequestService.CreateAsync(
                action.CompanyId,
                new CreateApprovalRequestCommand(
                    ApprovalTargetEntityType.Task.ToStorageValue(),
                    task.Id,
                    "human",
                    actorId,
                    approvalType,
                    BuildApprovalContext(action, bill, reason),
                    RequiredRole: "finance_approver"),
                cancellationToken);
            action.AttachApprovalWorkflow(task.Id, approval.Id, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        _logger?.LogInformation(
            "Supplier invoice correction approval requested. CompanyId: {CompanyId}. BillId: {BillId}. ActionId: {ActionId}. ActionType: {ActionType}. ApprovalRequestId: {ApprovalRequestId}.",
            action.CompanyId,
            action.BillId,
            action.Id,
            action.ActionType,
            action.ApprovalRequestId);

        return false;
    }

    private async Task<FinanceIntegrationConnection> ResolveActiveConnectionAsync(Guid companyId, string providerKey, CancellationToken cancellationToken) =>
        await _dbContext.FinanceIntegrationConnections
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.ProviderKey == providerKey && x.Status == FinanceIntegrationConnectionStatuses.Connected)
            .OrderByDescending(x => x.ConnectedUtc ?? x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new InvalidOperationException("No connected finance integration is available for supplier invoice corrections.");

    private static void ValidateCanCancel(FinanceBill bill)
    {
        ValidateSupplierInvoice(bill);
        if (string.Equals(bill.PostingStatus, FinanceDocumentPostingStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("This supplier invoice is already cancelled.");
        }

        if (bill.SettlementStatus is FinanceSettlementStatuses.Paid or FinanceSettlementStatuses.Credited)
        {
            throw new InvalidOperationException("Paid or credited supplier invoices cannot be cancelled safely.");
        }
    }

    private static void ValidateCanCreateCreditNote(FinanceBill bill)
    {
        ValidateSupplierInvoice(bill);
        if (string.Equals(bill.PostingStatus, FinanceDocumentPostingStatuses.Cancelled, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Cancelled supplier invoices cannot receive a credit note.");
        }

        if (bill.SettlementStatus == FinanceSettlementStatuses.Credited)
        {
            throw new InvalidOperationException("This supplier invoice is already credited.");
        }
    }

    private static void ValidateSupplierInvoice(FinanceBill bill)
    {
        if (!string.Equals(bill.DocumentKind, FinanceDocumentKinds.SupplierInvoice, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only supplier invoices support this correction action.");
        }
    }

    private async Task<SupplierInvoiceCorrectionProviderRequest> BuildProviderRequestAsync(
        SupplierInvoiceCorrectionAction action,
        FinanceBill bill,
        Guid connectionId,
        Guid? actorUserId,
        string providerKey,
        string? reason,
        CancellationToken cancellationToken)
    {
        var invoiceReference = await ResolveProviderReferenceAsync(bill.CompanyId, bill.Id, providerKey, "supplier_invoice", cancellationToken);
        var supplierReference = await ResolveProviderReferenceAsync(bill.CompanyId, bill.CounterpartyId, providerKey, "supplier", cancellationToken);
        return new SupplierInvoiceCorrectionProviderRequest(
            bill.CompanyId,
            action.Id,
            bill.Id,
            invoiceReference?.ExternalNumber ?? invoiceReference?.ExternalId ?? bill.BillNumber,
            bill.CounterpartyId,
            bill.Counterparty.Name,
            supplierReference?.ExternalNumber ?? supplierReference?.ExternalId,
            bill.Amount,
            bill.Currency,
            bill.ReceivedUtc,
            bill.DueUtc,
            bill.BillNumber,
            null,
            connectionId,
            actorUserId,
            reason);
    }

    private async Task<FinanceExternalReference?> ResolveProviderReferenceAsync(
        Guid companyId,
        Guid recordId,
        string providerKey,
        string entityType,
        CancellationToken cancellationToken) =>
        await _dbContext.FinanceExternalReferences
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.ProviderKey == providerKey && x.InternalRecordId == recordId && x.EntityType == entityType)
            .OrderByDescending(x => x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<SupplierInvoiceCorrectionActionDto> RecordProviderResultAsync(
        Guid companyId,
        SupplierInvoiceCorrectionAction action,
        SupplierInvoiceCorrectionProviderResult providerResult,
        string eventType,
        CancellationToken cancellationToken)
    {
        var creditNoteBillId = await ResolveCreditNoteBillIdAsync(companyId, providerResult.ProviderKey, providerResult.ProviderCreditNoteNumber, cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        action.Mark(
            providerResult.Status,
            providerResult.ProviderKey,
            providerResult.ConnectionId,
            action.RequestedByUserId,
            providerResult.ResponseSummary,
            providerResult.ProviderMetadata,
            now,
            creditNoteBillId,
            providerResult.ProviderCreditNoteNumber);

        AddAuditEvent(
            companyId,
            providerResult.ConnectionId,
            providerResult.ProviderKey,
            eventType,
            providerResult.Status is SupplierInvoiceCorrectionActionStatuses.CancellationFailed or SupplierInvoiceCorrectionActionStatuses.CreditNoteFailed
                ? FinanceIntegrationAuditOutcomes.Failed
                : FinanceIntegrationAuditOutcomes.Succeeded,
            action,
            providerResult.ResponseSummary,
            now);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapAction(action);
    }

    private async Task<Guid?> ResolveCreditNoteBillIdAsync(Guid companyId, string providerKey, string? providerCreditNoteNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(providerCreditNoteNumber))
        {
            return null;
        }

        var reference = await _dbContext.FinanceExternalReferences
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.ProviderKey == providerKey &&
                x.EntityType == "supplier_invoice" &&
                (x.ExternalNumber == providerCreditNoteNumber || x.ExternalId == providerCreditNoteNumber))
            .OrderByDescending(x => x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return reference?.InternalRecordId;
    }

    private void AddAuditEvent(
        Guid companyId,
        Guid? connectionId,
        string providerKey,
        string eventType,
        string outcome,
        SupplierInvoiceCorrectionAction action,
        string summary,
        DateTime occurredUtc)
    {
        var audit = new FinanceIntegrationAuditEvent(
            Guid.NewGuid(),
            companyId,
            connectionId,
            providerKey,
            eventType,
            outcome,
            "supplier_invoice",
            action.BillId,
            null,
            action.Id.ToString("N"),
            summary,
            occurredUtc,
            updatedCount: outcome == FinanceIntegrationAuditOutcomes.Succeeded ? 1 : 0,
            errorCount: outcome == FinanceIntegrationAuditOutcomes.Failed ? 1 : 0);
        audit.Metadata["correctionActionId"] = action.Id.ToString("D");
        audit.Metadata["actionType"] = action.ActionType;
        audit.Metadata["status"] = action.Status;
        _dbContext.FinanceIntegrationAuditEvents.Add(audit);
    }

    private static Dictionary<string, JsonNode?> BuildApprovalTaskInput(
        SupplierInvoiceCorrectionAction action,
        FinanceBill bill,
        string? reason) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["correctionActionId"] = action.Id.ToString("D"),
            ["billId"] = bill.Id.ToString("D"),
            ["actionType"] = action.ActionType,
            ["supplierName"] = bill.Counterparty.Name,
            ["billNumber"] = bill.BillNumber,
            ["amount"] = bill.Amount,
            ["currency"] = bill.Currency,
            ["reason"] = reason,
            ["doesNotInitiatePayment"] = true
        };

    private static Dictionary<string, JsonNode?> BuildApprovalContext(
        SupplierInvoiceCorrectionAction action,
        FinanceBill bill,
        string? reason) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["correctionActionId"] = action.Id.ToString("D"),
            ["billId"] = bill.Id.ToString("D"),
            ["actionType"] = action.ActionType,
            ["supplier"] = bill.Counterparty.Name,
            ["billNumber"] = bill.BillNumber,
            ["amount"] = bill.Amount,
            ["currency"] = bill.Currency,
            ["reason"] = reason,
            ["summary"] = action.ActionType == SupplierInvoiceCorrectionActionTypes.Cancellation
                ? $"Approve cancellation of supplier invoice {bill.BillNumber}. No payment is initiated."
                : $"Approve creation of a supplier credit note for invoice {bill.BillNumber}. No payment is initiated."
        };

    private static string BuildCorrelationId(Guid companyId, Guid billId, string actionType) =>
        $"supplier-invoice-correction:{companyId:N}:{billId:N}:{actionType}";

    private async Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken)
    {
        if (!_dbContext.Database.IsRelational() || _dbContext.Database.CurrentTransaction is not null)
        {
            await action();
            return;
        }

        var strategy = _dbContext.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            await action();
            await transaction.CommitAsync(cancellationToken);
        });
    }

    public static SupplierInvoiceCorrectionActionDto MapAction(SupplierInvoiceCorrectionAction action) =>
        new(
            action.Id,
            action.BillId,
            action.ActionType,
            action.Status,
            action.ProviderKey,
            action.ConnectionId,
            action.RequestedByUserId,
            action.ApprovedByUserId,
            action.TaskId,
            action.ApprovalRequestId,
            action.RequestedUtc,
            action.ApprovedUtc,
            action.CompletedUtc,
            action.CreditNoteBillId,
            action.ProviderCreditNoteNumber,
            action.ResponseSummary,
            action.CreatedUtc,
            action.UpdatedUtc);
}

public sealed class FortnoxSupplierInvoiceCorrectionProvider : ISupplierInvoiceCorrectionProvider
{
    private readonly IFortnoxApiClient? _apiClient;
    private readonly ILogger<FortnoxSupplierInvoiceCorrectionProvider>? _logger;

    public FortnoxSupplierInvoiceCorrectionProvider(
        IFortnoxApiClient? apiClient = null,
        ILogger<FortnoxSupplierInvoiceCorrectionProvider>? logger = null)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    public string ProviderKey => FinanceIntegrationProviderKeys.Fortnox;

    public async Task<SupplierInvoiceCorrectionProviderResult> CancelAsync(
        SupplierInvoiceCorrectionProviderRequest request,
        CancellationToken cancellationToken)
    {
        var metadata = BuildBaseMetadata(request);
        if (_apiClient is null)
        {
            metadata["manualCancellationRequired"] = true;
            return new SupplierInvoiceCorrectionProviderResult(
                ProviderKey,
                request.ConnectionId,
                SupplierInvoiceCorrectionActionStatuses.CancellationRequested,
                "Manual Fortnox cancellation required. No Fortnox API client is available.",
                metadata);
        }

        try
        {
            _logger?.LogInformation(
                "Cancelling supplier invoice in Fortnox. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. ActionId: {ActionId}. FortnoxInvoiceNumber: {InvoiceNumber}.",
                request.CompanyId,
                request.ConnectionId,
                request.ActionId,
                request.SourceBillNumber);
            var response = await _apiClient.PutDirectAsync<JsonObject, JsonObject?>(
                BuildContext(request, "supplier-invoice-cancel"),
                $"supplierinvoices/{Uri.EscapeDataString(request.SourceBillNumber)}/cancel",
                new JsonObject(),
                cancellationToken);
            metadata["fortnoxResponse"] = response is null ? new JsonObject() : JsonNode.Parse(response.ToJsonString())?.AsObject() ?? new JsonObject();
            return new SupplierInvoiceCorrectionProviderResult(
                ProviderKey,
                request.ConnectionId,
                SupplierInvoiceCorrectionActionStatuses.Cancelled,
                $"Fortnox cancelled supplier invoice {request.SourceBillNumber}.",
                metadata);
        }
        catch (Exception exception) when (exception is FortnoxApiException or HttpRequestException or TaskCanceledException)
        {
            return CreateFailure(request, metadata, exception, SupplierInvoiceCorrectionActionStatuses.CancellationFailed, "Fortnox could not cancel this supplier invoice.");
        }
    }

    public async Task<SupplierInvoiceCorrectionProviderResult> CreateCreditNoteAsync(
        SupplierInvoiceCorrectionProviderRequest request,
        CancellationToken cancellationToken)
    {
        var metadata = BuildBaseMetadata(request);
        if (_apiClient is null)
        {
            metadata["manualCreditNoteRequired"] = true;
            return new SupplierInvoiceCorrectionProviderResult(
                ProviderKey,
                request.ConnectionId,
                SupplierInvoiceCorrectionActionStatuses.CreditNoteRequested,
                "Manual Fortnox credit note required. No Fortnox API client is available.",
                metadata);
        }

        try
        {
            _logger?.LogInformation(
                "Creating supplier credit note in Fortnox. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. ActionId: {ActionId}. FortnoxInvoiceNumber: {InvoiceNumber}.",
                request.CompanyId,
                request.ConnectionId,
                request.ActionId,
                request.SourceBillNumber);
            var response = await _apiClient.PutDirectAsync<JsonObject, JsonObject?>(
                BuildContext(request, "supplier-invoice-credit-note"),
                $"supplierinvoices/{Uri.EscapeDataString(request.SourceBillNumber)}/credit",
                new JsonObject(),
                cancellationToken);
            metadata["fortnoxResponse"] = response is null ? new JsonObject() : JsonNode.Parse(response.ToJsonString())?.AsObject() ?? new JsonObject();
            var creditNoteNumber = ExtractCreditNoteNumber(response);
            return new SupplierInvoiceCorrectionProviderResult(
                ProviderKey,
                request.ConnectionId,
                SupplierInvoiceCorrectionActionStatuses.CreditNoteCreated,
                string.IsNullOrWhiteSpace(creditNoteNumber)
                    ? $"Fortnox created a supplier credit note for invoice {request.SourceBillNumber}."
                    : $"Fortnox created supplier credit note {creditNoteNumber} for invoice {request.SourceBillNumber}.",
                metadata,
                creditNoteNumber);
        }
        catch (Exception exception) when (exception is FortnoxApiException or HttpRequestException or TaskCanceledException)
        {
            return CreateFailure(request, metadata, exception, SupplierInvoiceCorrectionActionStatuses.CreditNoteFailed, "Fortnox could not create this supplier credit note.");
        }
    }

    private static FortnoxRequestContext BuildContext(SupplierInvoiceCorrectionProviderRequest request, string prefix) =>
        new(
            request.CompanyId,
            request.ConnectionId,
            $"{prefix}:{request.ActionId:N}",
            ActorUserId: request.ActorUserId,
            WriteRequestId: request.ActionId);

    private static JsonObject BuildBaseMetadata(SupplierInvoiceCorrectionProviderRequest request) =>
        new()
        {
            ["provider"] = FinanceIntegrationProviderKeys.Fortnox,
            ["connectionId"] = request.ConnectionId.ToString("D"),
            ["actionId"] = request.ActionId.ToString("D"),
            ["billId"] = request.BillId.ToString("D"),
            ["fortnoxInvoiceNumber"] = request.SourceBillNumber,
            ["supplierNumber"] = request.SupplierNumber,
            ["amount"] = request.Amount,
            ["currency"] = request.Currency,
            ["reason"] = request.Reason
        };

    private static string? ExtractCreditNoteNumber(JsonObject? response)
    {
        if (response is null)
        {
            return null;
        }

        var invoice = response["SupplierInvoice"];
        return invoice?["GivenNumber"]?.ToString()
            ?? invoice?["DocumentNumber"]?.ToString()
            ?? invoice?["Number"]?.ToString()
            ?? response["GivenNumber"]?.ToString();
    }

    private SupplierInvoiceCorrectionProviderResult CreateFailure(
        SupplierInvoiceCorrectionProviderRequest request,
        JsonObject metadata,
        Exception exception,
        string status,
        string fallback)
    {
        var safeSummary = exception is FortnoxApiException apiException
            ? apiException.SafeMessage
            : fallback;
        _logger?.LogWarning(
            exception,
            "Fortnox supplier invoice correction failed. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. ActionId: {ActionId}. FortnoxInvoiceNumber: {InvoiceNumber}. SafeSummary: {SafeSummary}.",
            request.CompanyId,
            request.ConnectionId,
            request.ActionId,
            request.SourceBillNumber,
            safeSummary);
        metadata["failureCategory"] = exception is FortnoxApiException api ? api.Category : "transport";
        metadata["failureMessage"] = safeSummary;
        return new SupplierInvoiceCorrectionProviderResult(
            ProviderKey,
            request.ConnectionId,
            status,
            safeSummary,
            metadata);
    }
}
