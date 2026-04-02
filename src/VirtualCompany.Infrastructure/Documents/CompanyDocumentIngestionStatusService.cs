using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using Microsoft.Extensions.Logging;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Documents;

public sealed class CompanyDocumentIngestionStatusService : ICompanyDocumentIngestionStatusService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly ICorrelationContextAccessor _correlationContextAccessor;
    private readonly ILogger<CompanyDocumentIngestionStatusService> _logger;

    public CompanyDocumentIngestionStatusService(
        VirtualCompanyDbContext dbContext,
        IAuditEventWriter auditEventWriter,
        ICorrelationContextAccessor correlationContextAccessor,
        ILogger<CompanyDocumentIngestionStatusService> logger)
    {
        _dbContext = dbContext;
        _auditEventWriter = auditEventWriter;
        _correlationContextAccessor = correlationContextAccessor;
        _logger = logger;
    }

    public async Task MarkPendingScanAsync(Guid companyId, Guid documentId, CancellationToken cancellationToken)
    {
        var document = await GetDocumentAsync(companyId, documentId, cancellationToken);
        if (!document.MarkPendingScan())
        {
            return;
        }

        _logger.LogInformation(
            "Company document queued for virus scan. CompanyId: {CompanyId}, DocumentId: {DocumentId}, CorrelationId: {CorrelationId}.",
            companyId,
            documentId,
            _correlationContextAccessor.CorrelationId);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkScanCleanAsync(
        Guid companyId,
        Guid documentId,
        CompanyDocumentVirusScanResult result,
        CancellationToken cancellationToken)
    {
        var document = await GetDocumentAsync(companyId, documentId, cancellationToken);
        if (!document.MarkScanClean())
        {
            return;
        }

        ApplyVirusScanMetadata(document, result);

        _logger.LogInformation(
            "Company document cleared by virus scan. CompanyId: {CompanyId}, DocumentId: {DocumentId}, Scanner: {ScannerName}, CorrelationId: {CorrelationId}.",
            companyId,
            documentId,
            result.ScannerName,
            _correlationContextAccessor.CorrelationId);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkBlockedAsync(
        Guid companyId,
        Guid documentId,
        CompanyDocumentVirusScanResult result,
        CancellationToken cancellationToken)
    {
        var document = await GetDocumentAsync(companyId, documentId, cancellationToken);
        if (!document.MarkBlocked(
            result.FailureCode ?? "virus_scan_blocked",
            result.Message ?? "The document was blocked by the virus scanning policy.",
            "Remove the unsafe content and upload a clean file.",
            canRetry: false))
        {
            return;
        }

        ApplyVirusScanMetadata(document, result);

        await WriteDocumentAuditEventAsync(
            companyId,
            documentId,
            AuditEventActions.CompanyDocumentFailed,
            AuditEventOutcomes.Denied,
            document.FailureMessage,
            document,
            cancellationToken);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkProcessingAsync(Guid companyId, Guid documentId, CancellationToken cancellationToken)
    {
        var document = await GetDocumentAsync(companyId, documentId, cancellationToken);
        if (!document.MarkProcessing())
        {
            return;
        }

        _logger.LogInformation(
            "Company document ingestion started. CompanyId: {CompanyId}, DocumentId: {DocumentId}, CorrelationId: {CorrelationId}.",
            companyId,
            documentId,
            _correlationContextAccessor.CorrelationId);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkProcessedAsync(Guid companyId, Guid documentId, CancellationToken cancellationToken)
    {
        var document = await GetDocumentAsync(companyId, documentId, cancellationToken);
        if (!document.MarkProcessed())
        {
            return;
        }

        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                AuditActorTypes.System,
                null,
                AuditEventActions.CompanyDocumentProcessed,
                AuditTargetTypes.CompanyDocument,
                documentId.ToString("D"),
                AuditEventOutcomes.Succeeded,
                null,
                ["knowledge_documents"],
                BuildAuditMetadata(document),
                _correlationContextAccessor.CorrelationId),
            cancellationToken);
        _logger.LogInformation(
            "Company document ingestion completed. CompanyId: {CompanyId}, DocumentId: {DocumentId}, CorrelationId: {CorrelationId}.",
            companyId,
            documentId,
            _correlationContextAccessor.CorrelationId);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkFailedAsync(Guid companyId, Guid documentId, CompanyDocumentIngestionFailure failure, CancellationToken cancellationToken)
    {
        var document = await GetDocumentAsync(companyId, documentId, cancellationToken);
        if (!document.MarkFailed(
            failure.Code,
            failure.Message,
            failure.Action,
            failure.TechnicalDetail,
            failure.CanRetry))
        {
            return;
        }

        await WriteDocumentAuditEventAsync(
            companyId,
            documentId,
            AuditEventActions.CompanyDocumentFailed,
            AuditEventOutcomes.Failed,
            document.FailureMessage,
            document,
            cancellationToken);

        _logger.LogWarning(
            "Company document ingestion failed. CompanyId: {CompanyId}, DocumentId: {DocumentId}, CorrelationId: {CorrelationId}, FailureCode: {FailureCode}, Retryable: {CanRetry}, TechnicalDetail: {TechnicalDetail}.",
            companyId,
            documentId,
            _correlationContextAccessor.CorrelationId,
            failure.Code,
            failure.CanRetry,
            failure.TechnicalDetail);

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<CompanyKnowledgeDocument> GetDocumentAsync(Guid companyId, Guid documentId, CancellationToken cancellationToken)
    {
        var document = await _dbContext.CompanyKnowledgeDocuments
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId && x.Id == documentId,
                cancellationToken);

        return document ?? throw new KeyNotFoundException(
            $"Company knowledge document '{documentId:D}' was not found for company '{companyId:D}'.");
    }

    private async Task WriteDocumentAuditEventAsync(
        Guid companyId,
        Guid documentId,
        string action,
        string outcome,
        string? rationaleSummary,
        CompanyKnowledgeDocument document,
        CancellationToken cancellationToken)
    {
        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                AuditActorTypes.System,
                null,
                action,
                AuditTargetTypes.CompanyDocument,
                documentId.ToString("D"),
                outcome,
                rationaleSummary,
                ["knowledge_documents"],
                BuildAuditMetadata(document),
                _correlationContextAccessor.CorrelationId),
            cancellationToken);
    }

    private static void ApplyVirusScanMetadata(CompanyKnowledgeDocument document, CompanyDocumentVirusScanResult result)
    {
        document.SetMetadataValue("virus_scan", BuildVirusScanMetadata(result));
    }

    private static IReadOnlyDictionary<string, string?> BuildAuditMetadata(CompanyKnowledgeDocument document)
    {
        var metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["documentType"] = document.DocumentType.ToStorageValue(),
            ["fileName"] = document.OriginalFileName,
            ["ingestionStatus"] = document.IngestionStatus.ToStorageValue(),
            ["failureAction"] = document.FailureAction,
            ["canRetry"] = document.CanRetry ? "true" : "false",
            ["failureCode"] = document.FailureCode,
            ["indexingStatus"] = document.IndexingStatus.ToStorageValue(),
            ["storageKey"] = document.StorageKey
        };

        if (!string.IsNullOrWhiteSpace(document.StorageUrl))
        {
            metadata["storageUrl"] = document.StorageUrl;
        }

        if (TryGetVirusScanStringValue(document.Metadata, "outcome", out var scanOutcome))
        {
            metadata["scanOutcome"] = scanOutcome;
            metadata["virusScanner"] = TryGetVirusScanStringValue(document.Metadata, "scanner_name", out var scannerName) ? scannerName : null;
        }

        metadata["visibility"] = document.AccessScope.Visibility;

        return metadata;
    }

    private static JsonObject BuildVirusScanMetadata(CompanyDocumentVirusScanResult result)
    {
        var metadata = new JsonObject
        {
            ["outcome"] = result.Outcome.ToStorageValue(),
            ["scanner_name"] = result.ScannerName,
            ["scanner_version"] = result.ScannerVersion,
            ["scanned_utc"] = result.ScannedUtc
        };

        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            metadata["message"] = result.Message;
        }

        if (!string.IsNullOrWhiteSpace(result.FailureCode))
        {
            metadata["failure_code"] = result.FailureCode;
        }

        return metadata;
    }

    private static bool TryGetVirusScanStringValue(
        IReadOnlyDictionary<string, JsonNode?> metadata,
        string key,
        out string value)
    {
        value = string.Empty;
        return metadata.TryGetValue("virus_scan", out var node) &&
               node is JsonObject virusScan &&
               TryGetStringValue(virusScan, key, out value);
    }

    private static bool TryGetStringValue(
        JsonObject values,
        string key,
        out string value)
    {
        value = string.Empty;
        if (!values.TryGetPropertyValue(key, out var node) || node is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var rawValue))
        {
            return false;
        }

        value = rawValue?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryGetStringValue(
        IReadOnlyDictionary<string, JsonNode?> values,
        string key,
        out string value)
    {
        value = string.Empty;
        if (!values.TryGetValue(key, out var node) || node is not JsonValue jsonValue || !jsonValue.TryGetValue<string>(out var rawValue))
        {
            return false;
        }

        value = rawValue?.Trim() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(value);
    }
}
