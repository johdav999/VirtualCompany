using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class SupplierInvoiceDraftActionService : IFinanceSupplierInvoiceDraftActionService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IReadOnlyDictionary<string, ISupplierInvoiceDraftActionProvider> _providers;
    private readonly TimeProvider _timeProvider;
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly ILogger<SupplierInvoiceDraftActionService>? _logger;

    public SupplierInvoiceDraftActionService(
        VirtualCompanyDbContext dbContext,
        TimeProvider timeProvider,
        ICompanyContextAccessor? companyContextAccessor = null,
        IEnumerable<ISupplierInvoiceDraftActionProvider>? providers = null,
        ILogger<SupplierInvoiceDraftActionService>? logger = null)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _companyContextAccessor = companyContextAccessor;
        _providers = (providers ?? [])
            .GroupBy(provider => provider.ProviderKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public async Task<SupplierInvoiceDraftActionDto> UpdateDraftAsync(
        UpdateSupplierInvoiceDraftCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        var providerKey = NormalizeProviderKey(command.ProviderKey);
        var provider = ResolveProvider(providerKey);
        var bill = await LoadBillAsync(command.CompanyId, command.BillId, cancellationToken);
        ValidateCanUpdateDraft(bill);
        var connection = await ResolveActiveConnectionAsync(command.CompanyId, providerKey, cancellationToken);
        var action = await LoadOrCreateActionAsync(command.CompanyId, bill.Id, cancellationToken);

        if (action.Status is SupplierInvoiceDraftActionStatuses.Updated or SupplierInvoiceDraftActionStatuses.BookkeepingRequested)
        {
            return MapAction(action);
        }

        if (action.Status == SupplierInvoiceDraftActionStatuses.Booked)
        {
            throw new InvalidOperationException("Booked supplier invoices cannot be updated.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        action.Mark(
            SupplierInvoiceDraftActionStatuses.UpdatePending,
            providerKey,
            connection.Id,
            command.ActorUserId,
            "Fortnox draft update requested.",
            new JsonObject { ["phase"] = "update_requested" },
            now);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation(
            "Supplier invoice draft update requested. CompanyId: {CompanyId}. BillId: {BillId}. ActionId: {ActionId}. ProviderKey: {ProviderKey}.",
            command.CompanyId,
            bill.Id,
            action.Id,
            providerKey);

        var request = await BuildProviderRequestAsync(action, bill, connection.Id, command.ActorUserId, providerKey, cancellationToken);
        var providerResult = await provider.UpdateDraftAsync(request, cancellationToken);
        return await RecordProviderResultAsync(command.CompanyId, action, providerResult, "supplier_invoice_draft_update", cancellationToken);
    }

    public async Task<SupplierInvoiceDraftActionDto> BookkeepAsync(
        BookkeepSupplierInvoiceDraftCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        var providerKey = NormalizeProviderKey(command.ProviderKey);
        var provider = ResolveProvider(providerKey);
        var bill = await LoadBillAsync(command.CompanyId, command.BillId, cancellationToken);
        ValidateCanBookkeep(bill);
        var connection = await ResolveActiveConnectionAsync(command.CompanyId, providerKey, cancellationToken);
        var action = await LoadOrCreateActionAsync(command.CompanyId, bill.Id, cancellationToken);

        if (action.Status == SupplierInvoiceDraftActionStatuses.Booked)
        {
            return MapAction(action);
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        action.Mark(
            SupplierInvoiceDraftActionStatuses.BookkeepingRequested,
            providerKey,
            connection.Id,
            command.ActorUserId,
            "Fortnox bookkeeping requested.",
            new JsonObject { ["phase"] = "bookkeeping_requested" },
            now);
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger?.LogInformation(
            "Supplier invoice bookkeeping requested. CompanyId: {CompanyId}. BillId: {BillId}. ActionId: {ActionId}. ProviderKey: {ProviderKey}.",
            command.CompanyId,
            bill.Id,
            action.Id,
            providerKey);

        var request = await BuildProviderRequestAsync(action, bill, connection.Id, command.ActorUserId, providerKey, cancellationToken);
        var providerResult = await provider.BookkeepAsync(request, cancellationToken);
        return await RecordProviderResultAsync(command.CompanyId, action, providerResult, "supplier_invoice_bookkeeping", cancellationToken);
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

    private ISupplierInvoiceDraftActionProvider ResolveProvider(string providerKey) =>
        _providers.TryGetValue(providerKey, out var provider)
            ? provider
            : throw new InvalidOperationException($"Supplier invoice draft action provider '{providerKey}' is not available.");

    private async Task<FinanceBill> LoadBillAsync(Guid companyId, Guid billId, CancellationToken cancellationToken) =>
        await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .Include(x => x.Counterparty)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == billId, cancellationToken)
        ?? throw new KeyNotFoundException("Supplier bill not found.");

    private async Task<SupplierInvoiceDraftAction> LoadOrCreateActionAsync(Guid companyId, Guid billId, CancellationToken cancellationToken)
    {
        var action = await _dbContext.SupplierInvoiceDraftActions
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.BillId == billId, cancellationToken);
        if (action is not null)
        {
            return action;
        }

        action = new SupplierInvoiceDraftAction(Guid.NewGuid(), companyId, billId, _timeProvider.GetUtcNow().UtcDateTime);
        _dbContext.SupplierInvoiceDraftActions.Add(action);
        return action;
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
        ?? throw new InvalidOperationException("No connected finance integration is available for supplier invoice draft actions.");

    private static void ValidateCanUpdateDraft(FinanceBill bill)
    {
        if (!string.Equals(bill.DocumentKind, FinanceDocumentKinds.SupplierInvoice, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only supplier invoices can be updated in Fortnox.");
        }

        if (!string.Equals(bill.PostingStatus, FinanceDocumentPostingStatuses.Draft, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only draft supplier invoices can be updated.");
        }

        if (IsClosedForDraftAction(bill))
        {
            throw new InvalidOperationException("Booked, cancelled, credited, or paid supplier invoices cannot be updated.");
        }
    }

    private static void ValidateCanBookkeep(FinanceBill bill)
    {
        if (!string.Equals(bill.DocumentKind, FinanceDocumentKinds.SupplierInvoice, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only supplier invoices can be bookkept in Fortnox.");
        }

        if (string.Equals(bill.PostingStatus, FinanceDocumentPostingStatuses.Booked, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("This supplier invoice is already booked.");
        }

        if (IsClosedForDraftAction(bill))
        {
            throw new InvalidOperationException("Cancelled, credited, or paid supplier invoices cannot be bookkept.");
        }
    }

    private static bool IsClosedForDraftAction(FinanceBill bill) =>
        string.Equals(bill.PostingStatus, FinanceDocumentPostingStatuses.Cancelled, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(bill.SettlementStatus, FinanceSettlementStatuses.Paid, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(bill.SettlementStatus, FinanceSettlementStatuses.Credited, StringComparison.OrdinalIgnoreCase);

    private async Task<SupplierInvoiceDraftActionProviderRequest> BuildProviderRequestAsync(
        SupplierInvoiceDraftAction action,
        FinanceBill bill,
        Guid connectionId,
        Guid? actorUserId,
        string providerKey,
        CancellationToken cancellationToken)
    {
        var invoiceReference = await ResolveProviderSupplierInvoiceReferenceAsync(bill.CompanyId, bill.Id, providerKey, cancellationToken);
        var supplierReference = await ResolveProviderSupplierReferenceAsync(bill.CompanyId, bill.CounterpartyId, providerKey, cancellationToken);
        var paymentReference = await ResolveLatestPaymentReferenceAsync(bill.CompanyId, bill.Id, cancellationToken);
        var metadata = invoiceReference?.Metadata;
        return new SupplierInvoiceDraftActionProviderRequest(
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
            paymentReference,
            ReadDecimal(metadata, "vatAmount", "rawVatAmount", "VAT"),
            bill.Counterparty.DefaultAccountMapping,
            ReadString(metadata, "costCenter", "CostCenter", "cost_center"),
            ReadString(metadata, "project", "Project"),
            connectionId,
            actorUserId);
    }

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

    private async Task<string?> ResolveLatestPaymentReferenceAsync(Guid companyId, Guid billId, CancellationToken cancellationToken)
    {
        var proposal = await _dbContext.SupplierInvoicePaymentProposals
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.BillId == billId)
            .OrderByDescending(x => x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken);
        return proposal?.PaymentReference;
    }

    private async Task<SupplierInvoiceDraftActionDto> RecordProviderResultAsync(
        Guid companyId,
        SupplierInvoiceDraftAction action,
        SupplierInvoiceDraftActionProviderResult providerResult,
        string eventType,
        CancellationToken cancellationToken)
    {
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        action.Mark(
            providerResult.Status,
            providerResult.ProviderKey,
            providerResult.ConnectionId,
            action.RequestedByUserId,
            providerResult.ResponseSummary,
            providerResult.ProviderMetadata,
            now);

        AddAuditEvent(
            companyId,
            providerResult.ConnectionId,
            providerResult.ProviderKey,
            eventType,
            providerResult.Status == SupplierInvoiceDraftActionStatuses.Failed
                ? FinanceIntegrationAuditOutcomes.Failed
                : FinanceIntegrationAuditOutcomes.Succeeded,
            action,
            providerResult.ResponseSummary,
            now);

        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapAction(action);
    }

    private void AddAuditEvent(
        Guid companyId,
        Guid? connectionId,
        string providerKey,
        string eventType,
        string outcome,
        SupplierInvoiceDraftAction action,
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
        audit.Metadata["draftActionId"] = action.Id.ToString("D");
        audit.Metadata["status"] = action.Status;
        _dbContext.FinanceIntegrationAuditEvents.Add(audit);
    }

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

    private static decimal? ReadDecimal(JsonObject? metadata, params string[] names)
    {
        var text = ReadString(metadata, names);
        return decimal.TryParse(text, System.Globalization.NumberStyles.Number, System.Globalization.CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    public static SupplierInvoiceDraftActionDto MapAction(SupplierInvoiceDraftAction action) =>
        new(
            action.Id,
            action.BillId,
            action.Status,
            action.ProviderKey,
            action.ConnectionId,
            action.RequestedByUserId,
            action.RequestedUtc,
            action.UpdatedInProviderUtc,
            action.BookedUtc,
            action.ResponseSummary,
            action.CreatedUtc,
            action.UpdatedUtc);
}

public sealed class FortnoxSupplierInvoiceDraftActionProvider : ISupplierInvoiceDraftActionProvider
{
    private readonly IFortnoxApiClient? _apiClient;
    private readonly ILogger<FortnoxSupplierInvoiceDraftActionProvider>? _logger;

    public FortnoxSupplierInvoiceDraftActionProvider(
        IFortnoxApiClient? apiClient = null,
        ILogger<FortnoxSupplierInvoiceDraftActionProvider>? logger = null)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    public string ProviderKey => FinanceIntegrationProviderKeys.Fortnox;

    public async Task<SupplierInvoiceDraftActionProviderResult> UpdateDraftAsync(
        SupplierInvoiceDraftActionProviderRequest request,
        CancellationToken cancellationToken)
    {
        var metadata = BuildBaseMetadata(request);
        if (_apiClient is null)
        {
            metadata["manualUpdateRequired"] = true;
            return new SupplierInvoiceDraftActionProviderResult(
                ProviderKey,
                request.ConnectionId,
                SupplierInvoiceDraftActionStatuses.UpdatePending,
                "Manual Fortnox draft update required. No Fortnox API client is available.",
                metadata);
        }

        try
        {
            var payload = BuildSupplierInvoicePayload(request);
            _logger?.LogInformation(
                "Updating supplier invoice draft in Fortnox. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. ActionId: {ActionId}. FortnoxInvoiceNumber: {InvoiceNumber}.",
                request.CompanyId,
                request.ConnectionId,
                request.ActionId,
                request.SourceBillNumber);
            var response = await _apiClient.PutDirectAsync<JsonObject, JsonObject?>(
                BuildContext(request, "supplier-invoice-draft-update"),
                $"supplierinvoices/{Uri.EscapeDataString(request.SourceBillNumber)}",
                payload,
                cancellationToken);
            metadata["requestPayload"] = JsonNode.Parse(payload.ToJsonString())?.AsObject() ?? new JsonObject();
            metadata["fortnoxResponse"] = response is null ? new JsonObject() : JsonNode.Parse(response.ToJsonString())?.AsObject() ?? new JsonObject();
            return new SupplierInvoiceDraftActionProviderResult(
                ProviderKey,
                request.ConnectionId,
                SupplierInvoiceDraftActionStatuses.Updated,
                $"Fortnox updated supplier invoice {request.SourceBillNumber}.",
                metadata);
        }
        catch (Exception exception) when (exception is FortnoxApiException or HttpRequestException or TaskCanceledException)
        {
            return CreateFailure(request, metadata, exception, "Fortnox could not update this supplier invoice draft.");
        }
    }

    public async Task<SupplierInvoiceDraftActionProviderResult> BookkeepAsync(
        SupplierInvoiceDraftActionProviderRequest request,
        CancellationToken cancellationToken)
    {
        var metadata = BuildBaseMetadata(request);
        if (_apiClient is null)
        {
            metadata["manualBookkeepingRequired"] = true;
            return new SupplierInvoiceDraftActionProviderResult(
                ProviderKey,
                request.ConnectionId,
                SupplierInvoiceDraftActionStatuses.BookkeepingRequested,
                "Manual Fortnox bookkeeping required. No Fortnox API client is available.",
                metadata);
        }

        try
        {
            _logger?.LogInformation(
                "Bookkeeping supplier invoice in Fortnox. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. ActionId: {ActionId}. FortnoxInvoiceNumber: {InvoiceNumber}.",
                request.CompanyId,
                request.ConnectionId,
                request.ActionId,
                request.SourceBillNumber);
            var response = await _apiClient.PutDirectAsync<JsonObject, JsonObject?>(
                BuildContext(request, "supplier-invoice-bookkeep"),
                $"supplierinvoices/{Uri.EscapeDataString(request.SourceBillNumber)}/bookkeep",
                new JsonObject(),
                cancellationToken);
            metadata["fortnoxResponse"] = response is null ? new JsonObject() : JsonNode.Parse(response.ToJsonString())?.AsObject() ?? new JsonObject();
            return new SupplierInvoiceDraftActionProviderResult(
                ProviderKey,
                request.ConnectionId,
                SupplierInvoiceDraftActionStatuses.Booked,
                $"Fortnox booked supplier invoice {request.SourceBillNumber}.",
                metadata);
        }
        catch (Exception exception) when (exception is FortnoxApiException or HttpRequestException or TaskCanceledException)
        {
            return CreateFailure(request, metadata, exception, "Fortnox could not bookkeep this supplier invoice.");
        }
    }

    private static FortnoxRequestContext BuildContext(SupplierInvoiceDraftActionProviderRequest request, string prefix) =>
        new(
            request.CompanyId,
            request.ConnectionId,
            $"{prefix}:{request.ActionId:N}",
            ActorUserId: request.ActorUserId,
            WriteRequestId: request.ActionId);

    private static JsonObject BuildSupplierInvoicePayload(SupplierInvoiceDraftActionProviderRequest request)
    {
        var supplierInvoice = new JsonObject
        {
            ["SupplierNumber"] = request.SupplierNumber,
            ["InvoiceNumber"] = request.InvoiceNumber,
            ["InvoiceDate"] = request.ReceivedUtc.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["DueDate"] = request.DueUtc.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
            ["Total"] = request.Amount
        };

        if (request.VatAmount.HasValue)
        {
            supplierInvoice["VAT"] = request.VatAmount.Value;
        }

        if (!string.IsNullOrWhiteSpace(request.Currency))
        {
            supplierInvoice["Currency"] = request.Currency;
        }

        if (!string.IsNullOrWhiteSpace(request.PaymentReference))
        {
            supplierInvoice["OCR"] = request.PaymentReference;
        }

        if (!string.IsNullOrWhiteSpace(request.AccountCode))
        {
            supplierInvoice["SupplierInvoiceRows"] = new JsonArray
            {
                new JsonObject
                {
                    ["Account"] = request.AccountCode,
                    ["CostCenter"] = request.CostCenter,
                    ["Project"] = request.Project,
                    ["Debit"] = request.Amount
                }
            };
        }

        return new JsonObject { ["SupplierInvoice"] = supplierInvoice };
    }

    private static JsonObject BuildBaseMetadata(SupplierInvoiceDraftActionProviderRequest request) =>
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
            ["accountCode"] = request.AccountCode,
            ["costCenter"] = request.CostCenter,
            ["project"] = request.Project
        };

    private SupplierInvoiceDraftActionProviderResult CreateFailure(
        SupplierInvoiceDraftActionProviderRequest request,
        JsonObject metadata,
        Exception exception,
        string fallback)
    {
        var safeSummary = exception is FortnoxApiException apiException
            ? apiException.SafeMessage
            : fallback;
        _logger?.LogWarning(
            exception,
            "Fortnox supplier invoice draft action failed. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. ActionId: {ActionId}. FortnoxInvoiceNumber: {InvoiceNumber}. SafeSummary: {SafeSummary}.",
            request.CompanyId,
            request.ConnectionId,
            request.ActionId,
            request.SourceBillNumber,
            safeSummary);
        metadata["failureCategory"] = exception is FortnoxApiException api ? api.Category : "transport";
        metadata["failureMessage"] = safeSummary;
        return new SupplierInvoiceDraftActionProviderResult(
            ProviderKey,
            request.ConnectionId,
            SupplierInvoiceDraftActionStatuses.Failed,
            safeSummary,
            metadata);
    }
}
