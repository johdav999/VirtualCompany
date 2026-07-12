using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CustomerInvoiceFortnoxActionService : IFinanceCustomerInvoiceFortnoxActionService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IFinanceIntegrationWriteCommandService _writeCommands;
    private readonly IFortnoxOutboundActionExecutor _outboundActionExecutor;
    private readonly TimeProvider _timeProvider;
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly IFortnoxSyncService? _syncService;
    private readonly ILogger<CustomerInvoiceFortnoxActionService>? _logger;

    public CustomerInvoiceFortnoxActionService(
        VirtualCompanyDbContext dbContext,
        IFinanceIntegrationWriteCommandService writeCommands,
        IFortnoxOutboundActionExecutor outboundActionExecutor,
        TimeProvider timeProvider,
        ICompanyContextAccessor? companyContextAccessor = null,
        IFortnoxSyncService? syncService = null,
        ILogger<CustomerInvoiceFortnoxActionService>? logger = null)
    {
        _dbContext = dbContext;
        _writeCommands = writeCommands;
        _outboundActionExecutor = outboundActionExecutor;
        _timeProvider = timeProvider;
        _companyContextAccessor = companyContextAccessor;
        _syncService = syncService;
        _logger = logger;
    }

    public async Task<CustomerInvoiceFortnoxActionDto> RequestExportAsync(
        RequestCustomerInvoiceFortnoxExportCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        EnsureFortnox(command.ProviderKey);
        var invoice = await LoadInvoiceAsync(command.CompanyId, command.InvoiceId, cancellationToken);
        ValidateCanCreate(invoice);
        var connection = await ResolveActiveConnectionAsync(command.CompanyId, cancellationToken);
        var existingReference = await ResolveInvoiceReferenceAsync(command.CompanyId, invoice.Id, cancellationToken);
        if (existingReference is not null)
        {
            return await BuildStateAsync(command.CompanyId, invoice.Id, "This customer invoice already exists in Fortnox.", cancellationToken);
        }

        var customerReference = await ResolveCustomerReferenceAsync(command.CompanyId, invoice.CounterpartyId, cancellationToken)
            ?? throw new InvalidOperationException("Sync this customer from Fortnox before creating a Fortnox invoice.");
        var payload = BuildCreatePayload(invoice, customerReference);
        var writeRequestId = CreateWriteRequestId("create", invoice.Id, null);

        var result = await _writeCommands.RequestApprovalAsync(
            new FinanceIntegrationWriteCommand(
                FinanceIntegrationProviderKeys.Fortnox,
                command.CompanyId,
                connection.Id,
                command.ActorUserId,
                FinanceIntegrationWriteCommandTypes.InvoiceExport,
                "POST",
                "invoices",
                invoice.Counterparty.Name,
                FortnoxWritePayloadSanitizer.CreateSummary(payload),
                FortnoxWritePayloadSanitizer.CreatePayloadHash(payload),
                new FinanceIntegrationWritePayload(FortnoxWritePayloadSanitizer.CreateSanitizedJson(payload), "CustomerInvoiceCreate"),
                writeRequestId,
                $"customer-invoice:{invoice.Id:N}:fortnox-create"),
            cancellationToken);

        _logger?.LogInformation(
            "Customer invoice Fortnox create approval requested. CompanyId: {CompanyId}. InvoiceId: {InvoiceId}. WriteRequestId: {WriteRequestId}. ApprovalId: {ApprovalId}. CustomerNumber: {CustomerNumber}.",
            command.CompanyId,
            invoice.Id,
            result.WriteRequestId,
            result.ApprovalId,
            customerReference.ExternalNumber ?? customerReference.ExternalId);

        return await BuildStateAsync(command.CompanyId, invoice.Id, result.Message, cancellationToken);
    }

    public async Task<CustomerInvoiceFortnoxActionDto> ExecuteExportAsync(
        ExecuteCustomerInvoiceFortnoxExportCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        EnsureFortnox(command.ProviderKey);
        var invoice = await LoadInvoiceAsync(command.CompanyId, command.InvoiceId, cancellationToken);
        var writeRequestId = CreateWriteRequestId("create", invoice.Id, null);
        var result = await _outboundActionExecutor.ExecuteApprovedAsync(command.CompanyId, writeRequestId, cancellationToken);
        if (result.Executed)
        {
            var writeCommand = await LoadWriteCommandAsync(command.CompanyId, writeRequestId, cancellationToken);
            await EnsureInvoiceReferenceFromWriteCommandAsync(invoice, writeCommand, cancellationToken);
            await RunSyncBestEffortAsync(command.CompanyId, writeCommand.ConnectionId, command.ActorUserId, $"customer-invoice:{invoice.Id:N}:fortnox-create-sync", cancellationToken);
        }

        return await BuildStateAsync(command.CompanyId, invoice.Id, result.Summary, cancellationToken);
    }

    public async Task<CustomerInvoiceFortnoxActionDto> RequestBookkeepAsync(
        RequestCustomerInvoiceFortnoxBookkeepCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        EnsureFortnox(command.ProviderKey);
        var invoice = await LoadInvoiceAsync(command.CompanyId, command.InvoiceId, cancellationToken);
        ValidateCanBookkeep(invoice);
        var reference = await ResolveInvoiceReferenceAsync(command.CompanyId, invoice.Id, cancellationToken)
            ?? throw new InvalidOperationException("Create and sync the Fortnox customer invoice before bookkeeping it.");
        var documentNumber = ResolveDocumentNumber(reference);
        var writeRequestId = CreateWriteRequestId("bookkeep", invoice.Id, documentNumber);
        var payload = new JsonObject();

        var result = await _writeCommands.RequestApprovalAsync(
            new FinanceIntegrationWriteCommand(
                FinanceIntegrationProviderKeys.Fortnox,
                command.CompanyId,
                reference.ConnectionId,
                command.ActorUserId,
                FinanceIntegrationWriteCommandTypes.InvoiceExport,
                "PUT",
                $"invoices/{Uri.EscapeDataString(documentNumber)}/bookkeep",
                invoice.Counterparty.Name,
                $"Bookkeep Fortnox customer invoice {documentNumber}.",
                FortnoxWritePayloadSanitizer.CreatePayloadHash(payload),
                new FinanceIntegrationWritePayload(FortnoxWritePayloadSanitizer.CreateSanitizedJson(payload), "CustomerInvoiceBookkeep"),
                writeRequestId,
                $"customer-invoice:{invoice.Id:N}:fortnox-bookkeep"),
            cancellationToken);

        _logger?.LogInformation(
            "Customer invoice Fortnox bookkeep approval requested. CompanyId: {CompanyId}. InvoiceId: {InvoiceId}. WriteRequestId: {WriteRequestId}. ApprovalId: {ApprovalId}. FortnoxInvoiceNumber: {FortnoxInvoiceNumber}.",
            command.CompanyId,
            invoice.Id,
            result.WriteRequestId,
            result.ApprovalId,
            documentNumber);

        return await BuildStateAsync(command.CompanyId, invoice.Id, result.Message, cancellationToken);
    }

    public async Task<CustomerInvoiceFortnoxActionDto> ExecuteBookkeepAsync(
        ExecuteCustomerInvoiceFortnoxBookkeepCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);
        EnsureFortnox(command.ProviderKey);
        var invoice = await LoadInvoiceAsync(command.CompanyId, command.InvoiceId, cancellationToken);
        var reference = await ResolveInvoiceReferenceAsync(command.CompanyId, invoice.Id, cancellationToken)
            ?? throw new InvalidOperationException("Create and sync the Fortnox customer invoice before bookkeeping it.");
        var documentNumber = ResolveDocumentNumber(reference);
        var writeRequestId = CreateWriteRequestId("bookkeep", invoice.Id, documentNumber);
        var result = await _outboundActionExecutor.ExecuteApprovedAsync(command.CompanyId, writeRequestId, cancellationToken);
        if (result.Executed)
        {
            var writeCommand = await LoadWriteCommandAsync(command.CompanyId, writeRequestId, cancellationToken);
            reference.ReplaceMetadata(MergeMetadata(reference.Metadata, new JsonObject
            {
                ["bookkeepWriteRequestId"] = writeCommand.Id.ToString("D"),
                ["bookkeepStatus"] = FinanceIntegrationWriteCommandRecordStatuses.Executed,
                ["bookkeepExecutedUtc"] = _timeProvider.GetUtcNow().UtcDateTime.ToString("O", CultureInfo.InvariantCulture)
            }), _timeProvider.GetUtcNow().UtcDateTime);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await RunSyncBestEffortAsync(command.CompanyId, writeCommand.ConnectionId, command.ActorUserId, $"customer-invoice:{invoice.Id:N}:fortnox-bookkeep-sync", cancellationToken);
        }

        return await BuildStateAsync(command.CompanyId, invoice.Id, result.Summary, cancellationToken);
    }

    private void EnsureTenant(Guid companyId)
    {
        if (_companyContextAccessor?.CompanyId is { } currentCompanyId && currentCompanyId != companyId)
        {
            throw new UnauthorizedAccessException("The requested company does not match the active tenant context.");
        }
    }

    private static void EnsureFortnox(string providerKey)
    {
        if (!string.Equals(providerKey, FinanceIntegrationProviderKeys.Fortnox, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only Fortnox customer invoice actions are supported.");
        }
    }

    private async Task<FinanceInvoice> LoadInvoiceAsync(Guid companyId, Guid invoiceId, CancellationToken cancellationToken) =>
        await _dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .Include(x => x.Counterparty)
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == invoiceId, cancellationToken)
        ?? throw new KeyNotFoundException("Customer invoice was not found.");

    private static void ValidateCanCreate(FinanceInvoice invoice)
    {
        var isInvoice = string.Equals(invoice.DocumentKind, FinanceDocumentKinds.Invoice, StringComparison.OrdinalIgnoreCase) && invoice.Amount > 0m;
        var isCreditNote = string.Equals(invoice.DocumentKind, FinanceDocumentKinds.CreditNote, StringComparison.OrdinalIgnoreCase) && invoice.Amount < 0m;
        if (!isInvoice && !isCreditNote)
        {
            throw new InvalidOperationException("Only valid customer invoices or customer credit notes can be created in Fortnox.");
        }

        if (string.Equals(invoice.SettlementStatus, FinanceSettlementStatuses.Paid, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invoice.SettlementStatus, FinanceSettlementStatuses.Credited, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Paid or credited customer invoices cannot be created as new Fortnox drafts.");
        }
    }

    private static void ValidateCanBookkeep(FinanceInvoice invoice)
    {
        if (!string.Equals(invoice.DocumentKind, FinanceDocumentKinds.Invoice, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only customer invoices can be bookkept in Fortnox.");
        }

    }

    private async Task<FinanceIntegrationConnection> ResolveActiveConnectionAsync(Guid companyId, CancellationToken cancellationToken) =>
        await _dbContext.FinanceIntegrationConnections
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                x.Status == FinanceIntegrationConnectionStatuses.Connected)
            .OrderByDescending(x => x.ConnectedUtc ?? x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken)
        ?? throw new InvalidOperationException("Connect Fortnox before creating customer invoices.");

    private async Task<FinanceExternalReference?> ResolveCustomerReferenceAsync(Guid companyId, Guid customerId, CancellationToken cancellationToken) =>
        await _dbContext.FinanceExternalReferences
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                x.InternalRecordId == customerId &&
                x.EntityType == "customer")
            .OrderByDescending(x => x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<FinanceExternalReference?> ResolveInvoiceReferenceAsync(Guid companyId, Guid invoiceId, CancellationToken cancellationToken) =>
        await _dbContext.FinanceExternalReferences
            .IgnoreQueryFilters()
            .Where(x =>
                x.CompanyId == companyId &&
                x.ProviderKey == FinanceIntegrationProviderKeys.Fortnox &&
                x.InternalRecordId == invoiceId &&
                x.EntityType == "invoice")
            .OrderByDescending(x => x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

    private async Task<FinanceIntegrationWriteCommandRecord> LoadWriteCommandAsync(Guid companyId, Guid writeRequestId, CancellationToken cancellationToken) =>
        await _dbContext.FinanceIntegrationWriteCommands
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId && x.Id == writeRequestId, cancellationToken);

    private async Task EnsureInvoiceReferenceFromWriteCommandAsync(
        FinanceInvoice invoice,
        FinanceIntegrationWriteCommandRecord writeCommand,
        CancellationToken cancellationToken)
    {
        var documentNumber = ExtractDocumentNumber(writeCommand);
        if (string.IsNullOrWhiteSpace(documentNumber))
        {
            _logger?.LogWarning(
                "Fortnox accepted customer invoice create but no document number was returned. CompanyId: {CompanyId}. InvoiceId: {InvoiceId}. WriteRequestId: {WriteRequestId}.",
                invoice.CompanyId,
                invoice.Id,
                writeCommand.Id);
            return;
        }

        var existing = await ResolveInvoiceReferenceAsync(invoice.CompanyId, invoice.Id, cancellationToken);
        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var metadata = new JsonObject
        {
            ["createWriteRequestId"] = writeCommand.Id.ToString("D"),
            ["createdFromLocalInvoiceNumber"] = invoice.InvoiceNumber
        };

        if (existing is null)
        {
            _dbContext.FinanceExternalReferences.Add(new FinanceExternalReference(
                Guid.NewGuid(),
                invoice.CompanyId,
                writeCommand.ConnectionId ?? await ResolveActiveConnectionIdAsync(invoice.CompanyId, cancellationToken),
                FinanceIntegrationProviderKeys.Fortnox,
                "invoice",
                invoice.Id,
                documentNumber,
                documentNumber,
                null,
                now));
            await _dbContext.SaveChangesAsync(cancellationToken);
            existing = await ResolveInvoiceReferenceAsync(invoice.CompanyId, invoice.Id, cancellationToken);
        }
        else
        {
            existing.Refresh(documentNumber, null, now);
        }

        if (existing is not null)
        {
            existing.ReplaceMetadata(MergeMetadata(existing.Metadata, metadata), now);
        }

        _dbContext.FinanceIntegrationAuditEvents.Add(new FinanceIntegrationAuditEvent(
            Guid.NewGuid(),
            invoice.CompanyId,
            writeCommand.ConnectionId,
            FinanceIntegrationProviderKeys.Fortnox,
            "customer_invoice_reference_linked",
            FinanceIntegrationAuditOutcomes.Succeeded,
            "invoice",
            invoice.Id,
            documentNumber,
            writeCommand.Id.ToString("N"),
            $"Fortnox customer invoice {documentNumber} linked to local invoice {invoice.InvoiceNumber}.",
            now,
            updatedCount: 1));
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<Guid> ResolveActiveConnectionIdAsync(Guid companyId, CancellationToken cancellationToken) =>
        await _dbContext.FinanceIntegrationConnections
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.ProviderKey == FinanceIntegrationProviderKeys.Fortnox && x.Status == FinanceIntegrationConnectionStatuses.Connected)
            .OrderByDescending(x => x.ConnectedUtc ?? x.UpdatedUtc)
            .Select(x => x.Id)
            .FirstAsync(cancellationToken);

    private async Task RunSyncBestEffortAsync(Guid companyId, Guid? connectionId, Guid? actorUserId, string correlationId, CancellationToken cancellationToken)
    {
        if (_syncService is null || connectionId is null)
        {
            return;
        }

        try
        {
            await _syncService.SyncAsync(new RunFortnoxSyncCommand(companyId, connectionId, correlationId, actorUserId), cancellationToken);
        }
        catch (Exception exception) when (exception is FortnoxApiException or InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            _logger?.LogWarning(
                exception,
                "Customer invoice Fortnox follow-up sync failed. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. CorrelationId: {CorrelationId}.",
                companyId,
                connectionId,
                correlationId);
        }
    }

    private async Task<CustomerInvoiceFortnoxActionDto> BuildStateAsync(
        Guid companyId,
        Guid invoiceId,
        string message,
        CancellationToken cancellationToken)
    {
        var invoice = await LoadInvoiceAsync(companyId, invoiceId, cancellationToken);
        var reference = await ResolveInvoiceReferenceAsync(companyId, invoiceId, cancellationToken);
        var documentNumber = reference is null ? null : ResolveDocumentNumber(reference);
        var createId = CreateWriteRequestId("create", invoiceId, null);
        var createCommand = await TryLoadWriteCommandAsync(companyId, createId, cancellationToken);
        FinanceIntegrationWriteCommandRecord? bookkeepCommand = null;
        if (!string.IsNullOrWhiteSpace(documentNumber))
        {
            bookkeepCommand = await TryLoadWriteCommandAsync(companyId, CreateWriteRequestId("bookkeep", invoiceId, documentNumber), cancellationToken);
        }

        return new CustomerInvoiceFortnoxActionDto(
            invoiceId,
            createCommand?.Id,
            createCommand?.ApprovalId,
            createCommand?.Status ?? (reference is null ? "not_requested" : FinanceIntegrationWriteCommandRecordStatuses.Executed),
            bookkeepCommand?.Id,
            bookkeepCommand?.ApprovalId,
            bookkeepCommand?.Status,
            message,
            reference is null && CanRequest(createCommand?.Status),
            CanExecute(createCommand),
            reference is not null && CanRequest(bookkeepCommand?.Status),
            CanExecute(bookkeepCommand),
            documentNumber,
            reference?.UpdatedUtc);
    }

    private async Task<FinanceIntegrationWriteCommandRecord?> TryLoadWriteCommandAsync(Guid companyId, Guid writeRequestId, CancellationToken cancellationToken) =>
        await _dbContext.FinanceIntegrationWriteCommands
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == writeRequestId, cancellationToken);

    private static bool CanRequest(string? status) =>
        status is null or FinanceIntegrationWriteCommandRecordStatuses.Failed;

    private static bool CanExecute(FinanceIntegrationWriteCommandRecord? command) =>
        command is { Status: FinanceIntegrationWriteCommandRecordStatuses.Approved, ApprovalId: not null };

    private static JsonObject BuildCreatePayload(FinanceInvoice invoice, FinanceExternalReference customerReference)
    {
        var customerNumber = customerReference.ExternalNumber ?? customerReference.ExternalId;
        var line = new JsonObject
        {
            ["Description"] = invoice.DocumentKind == FinanceDocumentKinds.CreditNote
                ? $"Credit note {invoice.InvoiceNumber}"
                : $"Invoice {invoice.InvoiceNumber}",
            ["DeliveredQuantity"] = 1,
            ["Price"] = invoice.Amount,
            ["VAT"] = 0
        };

        var invoicePayload = new JsonObject
        {
            ["CustomerNumber"] = customerNumber,
            ["InvoiceDate"] = FormatDate(invoice.IssuedUtc),
            ["DueDate"] = FormatDate(invoice.DueUtc),
            ["Currency"] = invoice.Currency,
            ["YourReference"] = invoice.Counterparty.Name,
            ["Remarks"] = invoice.DocumentKind == FinanceDocumentKinds.CreditNote
                ? $"Created from approved Virtual Company customer credit {invoice.InvoiceNumber}."
                : $"Created from Virtual Company invoice {invoice.InvoiceNumber}.",
            ["InvoiceRows"] = new JsonArray(line)
        };

        return new JsonObject { ["Invoice"] = invoicePayload };
    }

    private static string FormatDate(DateTime value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private static string ResolveDocumentNumber(FinanceExternalReference reference) =>
        string.IsNullOrWhiteSpace(reference.ExternalNumber)
            ? reference.ExternalId
            : reference.ExternalNumber;

    private static string? ExtractDocumentNumber(FinanceIntegrationWriteCommandRecord command)
    {
        if (!string.IsNullOrWhiteSpace(command.ExternalId))
        {
            return command.ExternalId.Trim();
        }

        if (string.IsNullOrWhiteSpace(command.SafeResponseSummary))
        {
            return null;
        }

        try
        {
            var node = JsonNode.Parse(command.SafeResponseSummary);
            return ReadFirstString(node, "DocumentNumber", "InvoiceNumber", "Number", "id");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ReadFirstString(JsonNode? node, params string[] names)
    {
        if (node is JsonObject obj)
        {
            foreach (var name in names)
            {
                if (obj.TryGetPropertyValue(name, out var value) && value is not null && !string.IsNullOrWhiteSpace(value.ToString()))
                {
                    return value.ToString().Trim();
                }
            }

            foreach (var property in obj)
            {
                var nested = ReadFirstString(property.Value, names);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                var nested = ReadFirstString(item, names);
                if (!string.IsNullOrWhiteSpace(nested))
                {
                    return nested;
                }
            }
        }

        return null;
    }

    private static JsonObject MergeMetadata(JsonObject existing, JsonObject additions)
    {
        var merged = JsonNode.Parse(existing.ToJsonString())?.AsObject() ?? [];
        foreach (var property in additions)
        {
            merged[property.Key] = property.Value?.DeepClone();
        }

        return merged;
    }

    internal static Guid CreateWriteRequestId(string action, Guid invoiceId, string? documentNumber)
    {
        var seed = string.IsNullOrWhiteSpace(documentNumber)
            ? $"fortnox-customer-invoice:{action}:{invoiceId:N}"
            : $"fortnox-customer-invoice:{action}:{invoiceId:N}:{documentNumber.Trim()}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(seed));
        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guidBytes);
        return new Guid(guidBytes);
    }
}
