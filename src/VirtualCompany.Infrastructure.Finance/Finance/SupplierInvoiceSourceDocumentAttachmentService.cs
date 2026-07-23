using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class SupplierInvoiceSourceDocumentAttachmentService : IFinanceSupplierInvoiceSourceDocumentAttachmentService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyDocumentStorage _documentStorage;
    private readonly IReadOnlyDictionary<string, ISupplierInvoiceSourceDocumentAttachmentProvider> _providers;
    private readonly TimeProvider _timeProvider;
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly ILogger<SupplierInvoiceSourceDocumentAttachmentService>? _logger;

    public SupplierInvoiceSourceDocumentAttachmentService(
        VirtualCompanyDbContext dbContext,
        ICompanyDocumentStorage documentStorage,
        TimeProvider timeProvider,
        ICompanyContextAccessor? companyContextAccessor = null,
        IEnumerable<ISupplierInvoiceSourceDocumentAttachmentProvider>? providers = null,
        ILogger<SupplierInvoiceSourceDocumentAttachmentService>? logger = null)
    {
        _dbContext = dbContext;
        _documentStorage = documentStorage;
        _timeProvider = timeProvider;
        _companyContextAccessor = companyContextAccessor;
        _providers = (providers ?? [])
            .GroupBy(provider => provider.ProviderKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        _logger = logger;
    }

    public async Task<SupplierInvoiceSourceDocumentAttachmentDto> RequestAttachmentAsync(
        RequestSupplierInvoiceSourceDocumentAttachmentCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);

        var providerKey = string.IsNullOrWhiteSpace(command.ProviderKey)
            ? FinanceIntegrationProviderKeys.Fortnox
            : command.ProviderKey.Trim().ToLowerInvariant();
        if (!_providers.TryGetValue(providerKey, out var provider))
        {
            throw new InvalidOperationException($"Source document attachment provider '{providerKey}' is not available.");
        }

        var bill = await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .Include(x => x.Document)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.BillId, cancellationToken)
            ?? throw new KeyNotFoundException("Supplier bill not found.");

        if (!string.Equals(bill.DocumentKind, FinanceDocumentKinds.SupplierInvoice, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Only supplier invoices can receive source document attachments.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var attachment = await _dbContext.SupplierInvoiceSourceDocumentAttachments
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.BillId == command.BillId, cancellationToken);

        if (attachment is null)
        {
            attachment = new SupplierInvoiceSourceDocumentAttachment(Guid.NewGuid(), command.CompanyId, bill.Id, bill.DocumentId, now);
            _dbContext.SupplierInvoiceSourceDocumentAttachments.Add(attachment);
        }
        else
        {
            attachment.UpdateDocument(bill.DocumentId, now);
        }

        _logger?.LogInformation(
            "Supplier invoice source document attachment requested. CompanyId: {CompanyId}. BillId: {BillId}. AttachmentId: {AttachmentId}. CurrentStatus: {Status}. ProviderKey: {ProviderKey}. DocumentId: {DocumentId}.",
            command.CompanyId,
            bill.Id,
            attachment.Id,
            attachment.Status,
            providerKey,
            bill.DocumentId);

        if (attachment.Status is SupplierInvoiceSourceDocumentAttachmentStatuses.Attached or
            SupplierInvoiceSourceDocumentAttachmentStatuses.AttachmentRequested)
        {
            return MapAttachment(attachment);
        }

        if (bill.Document is null || bill.DocumentId is null)
        {
            attachment.Mark(
                SupplierInvoiceSourceDocumentAttachmentStatuses.NotAvailable,
                providerKey,
                null,
                command.ActorUserId,
                "No source document available.",
                new JsonObject
                {
                    ["reason"] = "bill_has_no_linked_source_document"
                },
                now);
            AddAuditEvent(command.CompanyId, null, providerKey, attachment, FinanceIntegrationAuditOutcomes.Skipped, "No source document available.", now);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return MapAttachment(attachment);
        }

        var connection = await ResolveActiveConnectionAsync(command.CompanyId, providerKey, cancellationToken);
        var sourceBillNumber = await ResolveProviderSupplierInvoiceNumberAsync(command.CompanyId, bill.Id, providerKey, cancellationToken);
        await using var content = await _documentStorage.OpenReadAsync(bill.Document.StorageKey, cancellationToken);

        var providerResult = await provider.AttachAsync(
            new SupplierInvoiceSourceDocumentAttachmentProviderRequest(
                command.CompanyId,
                attachment.Id,
                bill.Id,
                bill.DocumentId.Value,
                sourceBillNumber ?? bill.BillNumber,
                connection.Id,
                command.ActorUserId,
                bill.Document.OriginalFileName,
                bill.Document.ContentType,
                bill.Document.FileSizeBytes,
                content),
            cancellationToken);

        now = _timeProvider.GetUtcNow().UtcDateTime;
        attachment.Mark(
            providerResult.Status,
            providerResult.ProviderKey,
            providerResult.ConnectionId ?? connection.Id,
            command.ActorUserId,
            providerResult.ResponseSummary,
            providerResult.ProviderMetadata,
            now);

        AddAuditEvent(
            command.CompanyId,
            providerResult.ConnectionId ?? connection.Id,
            providerResult.ProviderKey,
            attachment,
            providerResult.Status == SupplierInvoiceSourceDocumentAttachmentStatuses.Failed
                ? FinanceIntegrationAuditOutcomes.Failed
                : FinanceIntegrationAuditOutcomes.Succeeded,
            providerResult.ResponseSummary,
            now);

        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger?.LogInformation(
            "Supplier invoice source document attachment state recorded. CompanyId: {CompanyId}. BillId: {BillId}. AttachmentId: {AttachmentId}. Status: {Status}. ProviderKey: {ProviderKey}. ConnectionId: {ConnectionId}.",
            command.CompanyId,
            bill.Id,
            attachment.Id,
            attachment.Status,
            attachment.ProviderKey,
            attachment.ConnectionId);

        return MapAttachment(attachment);
    }

    private void EnsureTenant(Guid companyId)
    {
        if (_companyContextAccessor?.CompanyId is { } currentCompanyId && currentCompanyId != companyId)
        {
            throw new UnauthorizedAccessException("The requested company does not match the active tenant context.");
        }
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
        ?? throw new InvalidOperationException("No connected finance integration is available for source document attachment.");

    private async Task<string?> ResolveProviderSupplierInvoiceNumberAsync(
        Guid companyId,
        Guid billId,
        string providerKey,
        CancellationToken cancellationToken)
    {
        var reference = await _dbContext.FinanceExternalReferences
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.ProviderKey == providerKey &&
                x.InternalRecordId == billId &&
                (x.EntityType == "supplier_invoice" || x.EntityType == "bill"))
            .OrderByDescending(x => x.UpdatedUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return string.IsNullOrWhiteSpace(reference?.ExternalNumber)
            ? reference?.ExternalId
            : reference.ExternalNumber;
    }

    private void AddAuditEvent(
        Guid companyId,
        Guid? connectionId,
        string providerKey,
        SupplierInvoiceSourceDocumentAttachment attachment,
        string outcome,
        string summary,
        DateTime occurredUtc)
    {
        var audit = new FinanceIntegrationAuditEvent(
            Guid.NewGuid(),
            companyId,
            connectionId,
            providerKey,
            "supplier_invoice_source_document_attachment",
            outcome,
            "supplier_invoice",
            attachment.BillId,
            null,
            attachment.Id.ToString("N"),
            summary,
            occurredUtc,
            createdCount: outcome == FinanceIntegrationAuditOutcomes.Succeeded ? 1 : 0,
            errorCount: outcome == FinanceIntegrationAuditOutcomes.Failed ? 1 : 0,
            skippedCount: outcome == FinanceIntegrationAuditOutcomes.Skipped ? 1 : 0);
        audit.Metadata["attachmentId"] = attachment.Id.ToString("D");
        audit.Metadata["documentId"] = attachment.DocumentId?.ToString("D");
        audit.Metadata["status"] = attachment.Status;
        _dbContext.FinanceIntegrationAuditEvents.Add(audit);
    }

    public static SupplierInvoiceSourceDocumentAttachmentDto MapAttachment(SupplierInvoiceSourceDocumentAttachment attachment) =>
        new(
            attachment.Id,
            attachment.BillId,
            attachment.DocumentId,
            attachment.Status,
            attachment.ProviderKey,
            attachment.ConnectionId,
            attachment.RequestedByUserId,
            attachment.RequestedUtc,
            attachment.AttachedUtc,
            attachment.ResponseSummary,
            attachment.CreatedUtc,
            attachment.UpdatedUtc);
}

public sealed class FortnoxSupplierInvoiceSourceDocumentAttachmentProvider : ISupplierInvoiceSourceDocumentAttachmentProvider
{
    private readonly IFortnoxApiClient? _apiClient;
    private readonly ILogger<FortnoxSupplierInvoiceSourceDocumentAttachmentProvider>? _logger;

    public FortnoxSupplierInvoiceSourceDocumentAttachmentProvider(
        IFortnoxApiClient? apiClient = null,
        ILogger<FortnoxSupplierInvoiceSourceDocumentAttachmentProvider>? logger = null)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    public string ProviderKey => FinanceIntegrationProviderKeys.Fortnox;

    public async Task<SupplierInvoiceSourceDocumentAttachmentProviderResult> AttachAsync(
        SupplierInvoiceSourceDocumentAttachmentProviderRequest request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var metadata = BuildBaseMetadata(request);
        if (_apiClient is null)
        {
            metadata["manualAttachmentRequired"] = true;
            metadata["reason"] = "Fortnox source document attachment is recorded for manual handling because no Fortnox API client is available.";
            return new SupplierInvoiceSourceDocumentAttachmentProviderResult(
                ProviderKey,
                request.ConnectionId,
                SupplierInvoiceSourceDocumentAttachmentStatuses.AttachmentRequested,
                "Manual source document attachment required. The document exists, but no Fortnox API client is available.",
                metadata);
        }

        _logger?.LogInformation(
            "Uploading supplier invoice source document to Fortnox archive. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. AttachmentId: {AttachmentId}. BillId: {BillId}. FortnoxInvoiceNumber: {InvoiceNumber}. FileName: {FileName}. ContentType: {ContentType}. SizeBytes: {SizeBytes}.",
            request.CompanyId,
            request.ConnectionId,
            request.AttachmentId,
            request.BillId,
            request.SourceBillNumber,
            request.OriginalFileName,
            request.ContentType,
            request.FileSizeBytes);

        try
        {
            var context = new FortnoxRequestContext(
                request.CompanyId,
                request.ConnectionId,
                $"supplier-invoice-source-document:{request.AttachmentId:N}",
                ActorUserId: request.ActorUserId,
                WriteRequestId: request.AttachmentId);
            var archiveResponse = await _apiClient.PostMultipartFileDirectAsync<JsonObject?>(
                context,
                "archive",
                "file",
                request.OriginalFileName,
                request.ContentType,
                request.Content,
                cancellationToken);
            var archiveMetadata = archiveResponse is null
                ? new JsonObject()
                : JsonNode.Parse(archiveResponse.ToJsonString())?.AsObject() ?? new JsonObject();
            var file = archiveResponse?["File"] as JsonObject ?? archiveResponse?["ArchiveFile"] as JsonObject;
            var fileId = ReadString(file, "Id") ?? ReadString(file, "ArchiveFileId");
            if (string.IsNullOrWhiteSpace(fileId))
            {
                metadata["fortnoxArchiveResponse"] = archiveMetadata;
                return new SupplierInvoiceSourceDocumentAttachmentProviderResult(
                    ProviderKey,
                    request.ConnectionId,
                    SupplierInvoiceSourceDocumentAttachmentStatuses.Failed,
                    "Fortnox accepted the file upload but did not return a file id.",
                    metadata);
            }

            var connectionPayload = new JsonObject
            {
                ["SupplierInvoiceFileConnection"] = new JsonObject
                {
                    ["FileId"] = fileId,
                    ["SupplierInvoiceNumber"] = request.SourceBillNumber
                }
            };

            _logger?.LogInformation(
                "Connecting Fortnox archive file to supplier invoice. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. AttachmentId: {AttachmentId}. FileId: {FileId}. FortnoxInvoiceNumber: {InvoiceNumber}.",
                request.CompanyId,
                request.ConnectionId,
                request.AttachmentId,
                fileId,
                request.SourceBillNumber);

            var connectionResponse = await _apiClient.PostDirectAsync<JsonObject, JsonObject?>(
                context,
                "supplierinvoicefileconnections",
                connectionPayload,
                cancellationToken);
            var connectionMetadata = connectionResponse is null
                ? new JsonObject()
                : JsonNode.Parse(connectionResponse.ToJsonString())?.AsObject() ?? new JsonObject();

            metadata["fortnoxArchiveFileId"] = fileId;
            metadata["fortnoxArchiveResponse"] = archiveMetadata;
            metadata["fortnoxFileConnectionResponse"] = connectionMetadata;
            metadata["connectionPayload"] = JsonNode.Parse(connectionPayload.ToJsonString())?.AsObject() ?? new JsonObject();

            return new SupplierInvoiceSourceDocumentAttachmentProviderResult(
                ProviderKey,
                request.ConnectionId,
                SupplierInvoiceSourceDocumentAttachmentStatuses.Attached,
                $"Fortnox attached source document {request.OriginalFileName} to supplier invoice {request.SourceBillNumber}.",
                metadata);
        }
        catch (Exception exception) when (exception is FortnoxApiException or HttpRequestException or TaskCanceledException or IOException)
        {
            var safeSummary = exception is FortnoxApiException apiException
                ? apiException.SafeMessage
                : "Fortnox could not attach this source document.";
            _logger?.LogWarning(
                exception,
                "Fortnox supplier invoice source document attachment failed. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. AttachmentId: {AttachmentId}. BillId: {BillId}. FortnoxInvoiceNumber: {InvoiceNumber}. SafeSummary: {SafeSummary}.",
                request.CompanyId,
                request.ConnectionId,
                request.AttachmentId,
                request.BillId,
                request.SourceBillNumber,
                safeSummary);

            metadata["failureCategory"] = exception is FortnoxApiException api ? api.Category : "transport";
            metadata["failureMessage"] = safeSummary;
            return new SupplierInvoiceSourceDocumentAttachmentProviderResult(
                ProviderKey,
                request.ConnectionId,
                SupplierInvoiceSourceDocumentAttachmentStatuses.Failed,
                safeSummary,
                metadata);
        }
    }

    private JsonObject BuildBaseMetadata(SupplierInvoiceSourceDocumentAttachmentProviderRequest request) =>
        new()
        {
            ["provider"] = ProviderKey,
            ["connectionId"] = request.ConnectionId.ToString("D"),
            ["attachmentId"] = request.AttachmentId.ToString("D"),
            ["billId"] = request.BillId.ToString("D"),
            ["documentId"] = request.DocumentId.ToString("D"),
            ["fortnoxInvoiceNumber"] = request.SourceBillNumber,
            ["fileName"] = request.OriginalFileName,
            ["contentType"] = request.ContentType,
            ["fileSizeBytes"] = request.FileSizeBytes
        };

    private static string? ReadString(JsonObject? source, string propertyName) =>
        source is not null && source.TryGetPropertyValue(propertyName, out var node) && node is not null
            ? node.ToString()
            : null;
}
