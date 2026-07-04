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

public sealed class SupplierInvoiceEnrichmentService : IFinanceSupplierInvoiceEnrichmentService
{
    private const string ApprovalType = "supplier_invoice_enrichment";
    private const string TaskType = "finance.supplier_invoice_enrichment";
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IApprovalRequestService _approvalRequestService;
    private readonly IReadOnlyDictionary<string, ISupplierInvoiceEnrichmentProvider> _providers;
    private readonly TimeProvider _timeProvider;
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly ILogger<SupplierInvoiceEnrichmentService>? _logger;

    public SupplierInvoiceEnrichmentService(
        VirtualCompanyDbContext dbContext,
        IApprovalRequestService approvalRequestService,
        TimeProvider timeProvider,
        ICompanyContextAccessor? companyContextAccessor = null,
        IEnumerable<ISupplierInvoiceEnrichmentProvider>? providers = null,
        ILogger<SupplierInvoiceEnrichmentService>? logger = null)
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

    public async Task<SupplierInvoiceEnrichmentActionDto> SuggestAsync(
        SuggestSupplierInvoiceEnrichmentCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        var bill = await LoadBillAsync(command.CompanyId, command.BillId, cancellationToken);
        ValidateSupplierInvoice(bill);
        var action = await LoadOrCreateActionAsync(command.CompanyId, command.BillId, cancellationToken);

        if (action.Status is SupplierInvoiceEnrichmentActionStatuses.AwaitingApproval or SupplierInvoiceEnrichmentActionStatuses.Approved or SupplierInvoiceEnrichmentActionStatuses.SyncRequested)
        {
            return MapAction(action);
        }

        var providerKey = NormalizeProviderKey(command.ProviderKey);
        var suggestion = await BuildSuggestionPayloadAsync(bill, providerKey, cancellationToken);
        var warnings = await BuildReconciliationWarningsAsync(bill, cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        action.MarkSuggested(
            suggestion,
            warnings,
            command.ActorUserId,
            warnings.Count == 0
                ? "Laura suggested supplier invoice enrichment changes and found no reconciliation warnings."
                : "Laura suggested supplier invoice enrichment changes and found reconciliation warnings.",
            now);

        var actorId = command.ActorUserId is { } userId && userId != Guid.Empty ? userId : command.CompanyId;
        var task = new WorkTask(
            Guid.NewGuid(),
            command.CompanyId,
            TaskType,
            $"Approve supplier invoice enrichment for {bill.Counterparty.Name}",
            $"Review Laura's supplier invoice coding, supplier data, and reconciliation suggestions for bill {bill.BillNumber}. Approval allows syncing supported changes to Fortnox.",
            WorkTaskPriority.Normal,
            assignedAgentId: null,
            parentTaskId: null,
            createdByActorType: "human",
            createdByActorId: actorId,
            inputPayload: BuildTaskInput(action, bill, suggestion, warnings),
            rationaleSummary: "Supplier invoice enrichment requires approval before Fortnox is updated.",
            correlationId: BuildCorrelationId(command.CompanyId, bill.Id),
            sourceType: WorkTaskSourceTypes.User,
            triggerSource: "finance_supplier_bill",
            creationReason: "Laura suggested supplier invoice enrichment and reconciliation changes.",
            triggerEventId: bill.Id.ToString("N"),
            status: WorkTaskStatus.AwaitingApproval);
        task.SetDueDate(bill.DueUtc);

        await ExecuteInTransactionAsync(async () =>
        {
            _dbContext.WorkTasks.Add(task);
            await _dbContext.SaveChangesAsync(cancellationToken);

            var approval = await _approvalRequestService.CreateAsync(
                command.CompanyId,
                new CreateApprovalRequestCommand(
                    ApprovalTargetEntityType.Task.ToStorageValue(),
                    task.Id,
                    "human",
                    actorId,
                    ApprovalType,
                    BuildApprovalContext(action, bill, suggestion, warnings),
                    RequiredRole: "finance_approver"),
                cancellationToken);

            action.AttachApprovalWorkflow(task.Id, approval.Id, now);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }, cancellationToken);

        _logger?.LogInformation(
            "Supplier invoice enrichment suggestion created. CompanyId: {CompanyId}. BillId: {BillId}. ActionId: {ActionId}. ApprovalRequestId: {ApprovalRequestId}. WarningCount: {WarningCount}.",
            command.CompanyId,
            bill.Id,
            action.Id,
            action.ApprovalRequestId,
            warnings.Count);

        return MapAction(action);
    }

    public async Task<SupplierInvoiceEnrichmentActionDto> SyncApprovedAsync(
        SyncSupplierInvoiceEnrichmentCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        var providerKey = NormalizeProviderKey(command.ProviderKey);
        var provider = ResolveProvider(providerKey);
        var bill = await LoadBillAsync(command.CompanyId, command.BillId, cancellationToken);
        ValidateSupplierInvoice(bill);
        var action = await LoadExistingActionAsync(command.CompanyId, command.BillId, cancellationToken);
        await EnsureApprovedAsync(action, command.ActorUserId, cancellationToken);
        var connection = await ResolveActiveConnectionAsync(command.CompanyId, providerKey, cancellationToken);

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        action.MarkProviderResult(
            SupplierInvoiceEnrichmentActionStatuses.SyncRequested,
            providerKey,
            connection.Id,
            command.ActorUserId,
            "Supplier invoice enrichment sync requested.",
            action.ProviderMetadata,
            now);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation(
            "Supplier invoice enrichment sync requested. CompanyId: {CompanyId}. BillId: {BillId}. ActionId: {ActionId}. ProviderKey: {ProviderKey}.",
            command.CompanyId,
            bill.Id,
            action.Id,
            providerKey);

        var request = await BuildProviderRequestAsync(action, bill, connection.Id, command.ActorUserId, providerKey, cancellationToken);
        var providerResult = await provider.SyncAsync(request, cancellationToken);
        return await RecordProviderResultAsync(command.CompanyId, action, providerResult, command.ActorUserId, cancellationToken);
    }

    public async Task<SupplierInvoiceEnrichmentActionDto> ReconcileAsync(
        ReconcileSupplierInvoiceCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        var bill = await LoadBillAsync(command.CompanyId, command.BillId, cancellationToken);
        ValidateSupplierInvoice(bill);
        var action = await LoadOrCreateActionAsync(command.CompanyId, command.BillId, cancellationToken);
        var warnings = await BuildReconciliationWarningsAsync(bill, cancellationToken);
        action.MarkReconciliation(warnings, _timeProvider.GetUtcNow().UtcDateTime);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapAction(action);
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

    private ISupplierInvoiceEnrichmentProvider ResolveProvider(string providerKey) =>
        _providers.TryGetValue(providerKey, out var provider)
            ? provider
            : throw new InvalidOperationException($"Supplier invoice enrichment provider '{providerKey}' is not available.");

    private async Task<FinanceBill> LoadBillAsync(Guid companyId, Guid billId, CancellationToken cancellationToken) =>
        await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .Include(x => x.Counterparty)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == billId, cancellationToken)
        ?? throw new KeyNotFoundException("Supplier bill not found.");

    private async Task<SupplierInvoiceEnrichmentAction> LoadExistingActionAsync(Guid companyId, Guid billId, CancellationToken cancellationToken) =>
        await _dbContext.SupplierInvoiceEnrichmentActions
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.BillId == billId, cancellationToken)
        ?? throw new InvalidOperationException("Ask Laura for supplier invoice enrichment suggestions before syncing to Fortnox.");

    private async Task<SupplierInvoiceEnrichmentAction> LoadOrCreateActionAsync(Guid companyId, Guid billId, CancellationToken cancellationToken)
    {
        var action = await _dbContext.SupplierInvoiceEnrichmentActions
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.BillId == billId, cancellationToken);
        if (action is not null)
        {
            return action;
        }

        action = new SupplierInvoiceEnrichmentAction(Guid.NewGuid(), companyId, billId, _timeProvider.GetUtcNow().UtcDateTime);
        _dbContext.SupplierInvoiceEnrichmentActions.Add(action);
        return action;
    }

    private static void ValidateSupplierInvoice(FinanceBill bill)
    {
        if (!string.Equals(bill.DocumentKind, FinanceDocumentKinds.SupplierInvoice, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only supplier invoices can be enriched and reconciled.");
        }
    }

    private async Task EnsureApprovedAsync(SupplierInvoiceEnrichmentAction action, Guid? actorUserId, CancellationToken cancellationToken)
    {
        if (action.Status is SupplierInvoiceEnrichmentActionStatuses.Approved or SupplierInvoiceEnrichmentActionStatuses.Failed)
        {
            return;
        }

        if (action.ApprovalRequestId is null)
        {
            throw new InvalidOperationException("Supplier invoice enrichment must be approved before it can be synced.");
        }

        var approval = await _dbContext.ApprovalRequests
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == action.CompanyId && x.Id == action.ApprovalRequestId, cancellationToken)
            ?? throw new InvalidOperationException("Supplier invoice enrichment approval could not be found.");
        if (approval.Status != ApprovalRequestStatus.Approved)
        {
            throw new InvalidOperationException("Supplier invoice enrichment must be approved before it can be synced.");
        }

        action.MarkApproved(actorUserId, _timeProvider.GetUtcNow().UtcDateTime);
    }

    private async Task<JsonObject> BuildSuggestionPayloadAsync(FinanceBill bill, string providerKey, CancellationToken cancellationToken)
    {
        var latestSimilarBill = await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == bill.CompanyId && x.CounterpartyId == bill.CounterpartyId && x.Id != bill.Id)
            .OrderByDescending(x => x.ReceivedUtc)
            .FirstOrDefaultAsync(cancellationToken);
        var invoiceReference = await ResolveProviderSupplierInvoiceReferenceAsync(bill.CompanyId, bill.Id, providerKey, cancellationToken);
        var supplierReference = await ResolveProviderSupplierReferenceAsync(bill.CompanyId, bill.CounterpartyId, providerKey, cancellationToken);
        var metadata = invoiceReference?.Metadata;
        var accountCode = FirstUseful(
            bill.Counterparty.DefaultAccountMapping,
            ReadString(metadata, "accountCode", "Account", "account"),
            "4010");
        var costCenter = ReadString(metadata, "costCenter", "CostCenter", "cost_center");
        var project = ReadString(metadata, "project", "Project");

        return new JsonObject
        {
            ["coding"] = new JsonObject
            {
                ["ledgerAccount"] = accountCode,
                ["costCenter"] = costCenter,
                ["project"] = project,
                ["basis"] = latestSimilarBill is null ? "Supplier default account and invoice metadata." : $"Based on supplier defaults and previous bill {latestSimilarBill.BillNumber}."
            },
            ["supplier"] = new JsonObject
            {
                ["supplierNumber"] = supplierReference?.ExternalNumber ?? supplierReference?.ExternalId,
                ["name"] = bill.Counterparty.Name,
                ["email"] = bill.Counterparty.Email,
                ["vatOrTaxId"] = bill.Counterparty.TaxId,
                ["paymentTerms"] = bill.Counterparty.PaymentTerms,
                ["preferredPaymentMethod"] = bill.Counterparty.PreferredPaymentMethod
            },
            ["comment"] = $"Laura reviewed supplier bill {bill.BillNumber}. Suggested account {accountCode}; verify supplier details before syncing.",
            ["provider"] = providerKey
        };
    }

    private async Task<JsonArray> BuildReconciliationWarningsAsync(FinanceBill bill, CancellationToken cancellationToken)
    {
        var warnings = new JsonArray();
        var paymentTransactions = await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == bill.CompanyId && x.BillId == bill.Id)
            .ToListAsync(cancellationToken);
        var paymentTransactionCount = paymentTransactions.Count(x => IsPaymentTransaction(x.TransactionType));
        var exportedProposals = await _dbContext.SupplierInvoicePaymentProposals
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == bill.CompanyId && x.BillId == bill.Id && x.ExportStatus == SupplierInvoicePaymentExportStatuses.Exported)
            .ToListAsync(cancellationToken);
        var paidAmount = decimal.Round(Math.Abs(bill.PaidAmount), 2, MidpointRounding.AwayFromZero);
        var totalAmount = decimal.Round(Math.Abs(bill.Amount), 2, MidpointRounding.AwayFromZero);
        var approvedExportAmount = exportedProposals.Sum(x => Math.Abs(x.Amount));

        if (paidAmount > totalAmount)
        {
            warnings.Add(CreateWarning("wrong_amount", "Paid amount is higher than the supplier invoice total.", "critical"));
        }

        if (paymentTransactionCount > 1)
        {
            warnings.Add(CreateWarning("duplicate_payment", "More than one supplier payment is linked to this bill.", "warning"));
        }

        if (paidAmount > 0m && approvedExportAmount == 0m)
        {
            warnings.Add(CreateWarning("paid_without_approval", "A payment is registered, but no approved payment proposal export is linked.", "critical"));
        }

        if (paidAmount > approvedExportAmount && approvedExportAmount > 0m)
        {
            warnings.Add(CreateWarning("wrong_amount", "Registered payment amount is higher than approved payment exports.", "warning"));
        }

        if (!string.Equals(bill.SettlementStatus, FinanceSettlementStatuses.Paid, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(bill.SettlementStatus, FinanceSettlementStatuses.Credited, StringComparison.OrdinalIgnoreCase) &&
            bill.DueUtc.Date < _timeProvider.GetUtcNow().UtcDateTime.Date)
        {
            warnings.Add(CreateWarning("unpaid_after_due_date", "Supplier invoice is still unpaid after the due date.", "warning"));
        }

        if (string.IsNullOrWhiteSpace(bill.Counterparty.TaxId))
        {
            warnings.Add(CreateWarning("supplier_data_mismatch", "Supplier VAT or organisation number is missing in the app.", "info"));
        }

        return warnings;
    }

    private static JsonObject CreateWarning(string code, string message, string severity) =>
        new()
        {
            ["code"] = code,
            ["message"] = message,
            ["severity"] = severity
        };

    private static bool IsPaymentTransaction(string transactionType)
    {
        var normalized = transactionType.Trim().Replace(' ', '_').Replace('-', '_').ToLowerInvariant();
        return normalized is "supplier_payment" or "payment";
    }

    private async Task<SupplierInvoiceEnrichmentProviderRequest> BuildProviderRequestAsync(
        SupplierInvoiceEnrichmentAction action,
        FinanceBill bill,
        Guid connectionId,
        Guid? actorUserId,
        string providerKey,
        CancellationToken cancellationToken)
    {
        var invoiceReference = await ResolveProviderSupplierInvoiceReferenceAsync(bill.CompanyId, bill.Id, providerKey, cancellationToken);
        var supplierReference = await ResolveProviderSupplierReferenceAsync(bill.CompanyId, bill.CounterpartyId, providerKey, cancellationToken);
        var coding = action.SuggestionPayload["coding"] as JsonObject;
        var supplier = action.SuggestionPayload["supplier"] as JsonObject;
        return new SupplierInvoiceEnrichmentProviderRequest(
            bill.CompanyId,
            action.Id,
            bill.Id,
            invoiceReference?.ExternalNumber ?? invoiceReference?.ExternalId ?? bill.BillNumber,
            bill.CounterpartyId,
            bill.Counterparty.Name,
            ReadString(supplier, "supplierNumber") ?? supplierReference?.ExternalNumber ?? supplierReference?.ExternalId,
            ReadString(supplier, "email") ?? bill.Counterparty.Email,
            ReadString(supplier, "vatOrTaxId") ?? bill.Counterparty.TaxId,
            ReadString(supplier, "paymentTerms") ?? bill.Counterparty.PaymentTerms,
            ReadString(supplier, "preferredPaymentMethod") ?? bill.Counterparty.PreferredPaymentMethod,
            ReadString(coding, "ledgerAccount") ?? bill.Counterparty.DefaultAccountMapping,
            ReadString(coding, "costCenter"),
            ReadString(coding, "project"),
            ReadString(action.SuggestionPayload, "comment"),
            connectionId,
            actorUserId,
            CloneObject(action.SuggestionPayload));
    }

    private async Task<SupplierInvoiceEnrichmentActionDto> RecordProviderResultAsync(
        Guid companyId,
        SupplierInvoiceEnrichmentAction action,
        SupplierInvoiceEnrichmentProviderResult providerResult,
        Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        action.MarkProviderResult(
            providerResult.Status,
            providerResult.ProviderKey,
            providerResult.ConnectionId,
            actorUserId,
            providerResult.ResponseSummary,
            providerResult.ProviderMetadata,
            _timeProvider.GetUtcNow().UtcDateTime);

        AddAuditEvent(
            companyId,
            providerResult.ConnectionId,
            providerResult.ProviderKey,
            providerResult.Status == SupplierInvoiceEnrichmentActionStatuses.Failed
                ? FinanceIntegrationAuditOutcomes.Failed
                : FinanceIntegrationAuditOutcomes.Succeeded,
            action,
            providerResult.ResponseSummary);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapAction(action);
    }

    private void AddAuditEvent(
        Guid companyId,
        Guid? connectionId,
        string providerKey,
        string outcome,
        SupplierInvoiceEnrichmentAction action,
        string summary)
    {
        var audit = new FinanceIntegrationAuditEvent(
            Guid.NewGuid(),
            companyId,
            connectionId,
            providerKey,
            "supplier_invoice_enrichment_sync",
            outcome,
            "supplier_invoice",
            action.BillId,
            null,
            action.Id.ToString("N"),
            summary,
            _timeProvider.GetUtcNow().UtcDateTime,
            updatedCount: outcome == FinanceIntegrationAuditOutcomes.Succeeded ? 1 : 0,
            errorCount: outcome == FinanceIntegrationAuditOutcomes.Failed ? 1 : 0);
        audit.Metadata["enrichmentActionId"] = action.Id.ToString("D");
        audit.Metadata["status"] = action.Status;
        _dbContext.FinanceIntegrationAuditEvents.Add(audit);
    }

    private async Task<FinanceIntegrationConnection> ResolveActiveConnectionAsync(
        Guid companyId,
        string providerKey,
        CancellationToken cancellationToken) =>
        await _dbContext.FinanceIntegrationConnections
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.ProviderKey == providerKey &&
                x.Status == FinanceIntegrationConnectionStatuses.Connected)
            .OrderByDescending(x => x.ConnectedUtc ?? x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new InvalidOperationException("No connected finance integration is available for supplier invoice enrichment sync.");

    private async Task<FinanceExternalReference?> ResolveProviderSupplierInvoiceReferenceAsync(
        Guid companyId,
        Guid billId,
        string providerKey,
        CancellationToken cancellationToken) =>
        await _dbContext.FinanceExternalReferences
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.ProviderKey == providerKey &&
                x.InternalRecordId == billId &&
                x.EntityType == "supplier_invoice")
            .OrderByDescending(x => x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<FinanceExternalReference?> ResolveProviderSupplierReferenceAsync(
        Guid companyId,
        Guid supplierId,
        string providerKey,
        CancellationToken cancellationToken) =>
        await _dbContext.FinanceExternalReferences
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.ProviderKey == providerKey &&
                x.InternalRecordId == supplierId &&
                x.EntityType == "supplier")
            .OrderByDescending(x => x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

    private static Dictionary<string, JsonNode?> BuildTaskInput(
        SupplierInvoiceEnrichmentAction action,
        FinanceBill bill,
        JsonObject suggestion,
        JsonArray warnings) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["enrichmentActionId"] = action.Id.ToString("D"),
            ["billId"] = bill.Id.ToString("D"),
            ["supplierName"] = bill.Counterparty.Name,
            ["billNumber"] = bill.BillNumber,
            ["suggestion"] = CloneObject(suggestion),
            ["warningCount"] = warnings.Count,
            ["doesNotInitiatePayment"] = true
        };

    private static Dictionary<string, JsonNode?> BuildApprovalContext(
        SupplierInvoiceEnrichmentAction action,
        FinanceBill bill,
        JsonObject suggestion,
        JsonArray warnings) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["enrichmentActionId"] = action.Id.ToString("D"),
            ["billId"] = bill.Id.ToString("D"),
            ["supplier"] = bill.Counterparty.Name,
            ["billNumber"] = bill.BillNumber,
            ["suggestion"] = CloneObject(suggestion),
            ["warningCount"] = warnings.Count,
            ["summary"] = $"Approve Laura's supplier invoice enrichment suggestions for bill {bill.BillNumber}. Syncing will update supported Fortnox fields only."
        };

    private static string BuildCorrelationId(Guid companyId, Guid billId) =>
        $"supplier-invoice-enrichment:{companyId:N}:{billId:N}";

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

    private static string? FirstUseful(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();

    private static string? ReadString(JsonObject? metadata, params string[] names)
    {
        if (metadata is null)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (metadata.TryGetPropertyValue(name, out var node) && node is not null && !string.IsNullOrWhiteSpace(node.ToString()))
            {
                return node.ToString().Trim();
            }
        }

        return null;
    }

    private static JsonObject CloneObject(JsonObject source) =>
        JsonNode.Parse(source.ToJsonString())?.AsObject() ?? [];

    private static JsonArray CloneArray(JsonArray source) =>
        JsonNode.Parse(source.ToJsonString())?.AsArray() ?? [];

    public static SupplierInvoiceEnrichmentActionDto MapAction(SupplierInvoiceEnrichmentAction action) =>
        new(
            action.Id,
            action.BillId,
            action.Status,
            action.ProviderKey,
            action.ConnectionId,
            action.RequestedByUserId,
            action.ApprovedByUserId,
            action.TaskId,
            action.ApprovalRequestId,
            action.RequestedUtc,
            action.ApprovedUtc,
            action.SyncedUtc,
            action.ResponseSummary,
            CloneObject(action.SuggestionPayload),
            CloneArray(action.ReconciliationWarnings),
            action.CreatedUtc,
            action.UpdatedUtc);
}

public sealed class FortnoxSupplierInvoiceEnrichmentProvider : ISupplierInvoiceEnrichmentProvider
{
    private readonly IFortnoxApiClient? _apiClient;
    private readonly ILogger<FortnoxSupplierInvoiceEnrichmentProvider>? _logger;

    public FortnoxSupplierInvoiceEnrichmentProvider(
        IFortnoxApiClient? apiClient = null,
        ILogger<FortnoxSupplierInvoiceEnrichmentProvider>? logger = null)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    public string ProviderKey => FinanceIntegrationProviderKeys.Fortnox;

    public async Task<SupplierInvoiceEnrichmentProviderResult> SyncAsync(
        SupplierInvoiceEnrichmentProviderRequest request,
        CancellationToken cancellationToken)
    {
        var metadata = BuildBaseMetadata(request);
        if (_apiClient is null)
        {
            metadata["manualSyncRequired"] = true;
            return new SupplierInvoiceEnrichmentProviderResult(
                ProviderKey,
                request.ConnectionId,
                SupplierInvoiceEnrichmentActionStatuses.SyncRequested,
                "Manual Fortnox enrichment update required. No Fortnox API client is available.",
                metadata);
        }

        try
        {
            var invoicePayload = BuildSupplierInvoicePayload(request);
            _logger?.LogInformation(
                "Syncing supplier invoice enrichment to Fortnox. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. ActionId: {ActionId}. FortnoxInvoiceNumber: {InvoiceNumber}.",
                request.CompanyId,
                request.ConnectionId,
                request.ActionId,
                request.SourceBillNumber);
            var invoiceResponse = await _apiClient.PutDirectAsync<JsonObject, JsonObject?>(
                BuildContext(request, "supplier-invoice-enrichment"),
                $"supplierinvoices/{Uri.EscapeDataString(request.SourceBillNumber)}",
                invoicePayload,
                cancellationToken);

            metadata["supplierInvoicePayload"] = JsonNode.Parse(invoicePayload.ToJsonString())?.AsObject() ?? new JsonObject();
            metadata["supplierInvoiceResponse"] = invoiceResponse is null ? new JsonObject() : JsonNode.Parse(invoiceResponse.ToJsonString())?.AsObject() ?? new JsonObject();

            if (!string.IsNullOrWhiteSpace(request.SupplierNumber))
            {
                var supplierPayload = BuildSupplierPayload(request);
                var supplierResponse = await _apiClient.PutDirectAsync<JsonObject, JsonObject?>(
                    BuildContext(request, "supplier-master-data-enrichment"),
                    $"suppliers/{Uri.EscapeDataString(request.SupplierNumber)}",
                    supplierPayload,
                    cancellationToken);
                metadata["supplierPayload"] = JsonNode.Parse(supplierPayload.ToJsonString())?.AsObject() ?? new JsonObject();
                metadata["supplierResponse"] = supplierResponse is null ? new JsonObject() : JsonNode.Parse(supplierResponse.ToJsonString())?.AsObject() ?? new JsonObject();
            }
            else
            {
                metadata["supplierUpdateSkipped"] = "No Fortnox supplier number is linked to this supplier.";
            }

            return new SupplierInvoiceEnrichmentProviderResult(
                ProviderKey,
                request.ConnectionId,
                SupplierInvoiceEnrichmentActionStatuses.Synced,
                "Fortnox supplier invoice enrichment was synced for supported fields.",
                metadata);
        }
        catch (Exception exception) when (exception is FortnoxApiException or HttpRequestException or TaskCanceledException)
        {
            var safeSummary = exception is FortnoxApiException apiException
                ? apiException.SafeMessage
                : "Fortnox enrichment sync failed. Review the record details and try again.";
            _logger?.LogWarning(
                exception,
                "Fortnox supplier invoice enrichment sync failed. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. ActionId: {ActionId}. FortnoxInvoiceNumber: {InvoiceNumber}. SafeSummary: {SafeSummary}.",
                request.CompanyId,
                request.ConnectionId,
                request.ActionId,
                request.SourceBillNumber,
                safeSummary);
            metadata["failureCategory"] = exception is FortnoxApiException api ? api.Category : "transport";
            metadata["failureMessage"] = safeSummary;
            return new SupplierInvoiceEnrichmentProviderResult(
                ProviderKey,
                request.ConnectionId,
                SupplierInvoiceEnrichmentActionStatuses.Failed,
                safeSummary,
                metadata);
        }
    }

    private static FortnoxRequestContext BuildContext(SupplierInvoiceEnrichmentProviderRequest request, string prefix) =>
        new(
            request.CompanyId,
            request.ConnectionId,
            $"{prefix}:{request.ActionId:N}",
            ActorUserId: request.ActorUserId,
            WriteRequestId: request.ActionId);

    private static JsonObject BuildSupplierInvoicePayload(SupplierInvoiceEnrichmentProviderRequest request)
    {
        var supplierInvoice = new JsonObject();
        if (!string.IsNullOrWhiteSpace(request.ReviewComment))
        {
            supplierInvoice["Comments"] = request.ReviewComment;
        }

        if (!string.IsNullOrWhiteSpace(request.AccountCode))
        {
            supplierInvoice["SupplierInvoiceRows"] = new JsonArray
            {
                new JsonObject
                {
                    ["Account"] = request.AccountCode,
                    ["CostCenter"] = request.CostCenter,
                    ["Project"] = request.Project
                }
            };
        }

        return new JsonObject { ["SupplierInvoice"] = supplierInvoice };
    }

    private static JsonObject BuildSupplierPayload(SupplierInvoiceEnrichmentProviderRequest request)
    {
        var supplier = new JsonObject
        {
            ["SupplierNumber"] = request.SupplierNumber,
            ["Name"] = request.SupplierName,
            ["Email"] = request.SupplierEmail,
            ["OrganisationNumber"] = request.SupplierTaxId,
            ["TermsOfPayment"] = request.SupplierPaymentTerms
        };

        return new JsonObject { ["Supplier"] = supplier };
    }

    private static JsonObject BuildBaseMetadata(SupplierInvoiceEnrichmentProviderRequest request) =>
        new()
        {
            ["provider"] = FinanceIntegrationProviderKeys.Fortnox,
            ["connectionId"] = request.ConnectionId.ToString("D"),
            ["actionId"] = request.ActionId.ToString("D"),
            ["billId"] = request.BillId.ToString("D"),
            ["fortnoxInvoiceNumber"] = request.SourceBillNumber,
            ["supplierNumber"] = request.SupplierNumber,
            ["accountCode"] = request.AccountCode,
            ["costCenter"] = request.CostCenter,
            ["project"] = request.Project,
            ["suggestionPayload"] = JsonNode.Parse(request.SuggestionPayload.ToJsonString())?.AsObject() ?? new JsonObject()
        };
}
