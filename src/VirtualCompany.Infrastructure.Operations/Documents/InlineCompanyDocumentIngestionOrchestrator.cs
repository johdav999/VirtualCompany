using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Documents;

public sealed class InlineCompanyDocumentIngestionOrchestrator : IDocumentIngestionOrchestrator
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyDocumentIngestionStatusService _ingestionStatusService;
    private readonly ICompanyDocumentVirusScanner _virusScanner;
    private readonly ILogger<InlineCompanyDocumentIngestionOrchestrator> _logger;

    public InlineCompanyDocumentIngestionOrchestrator(
        VirtualCompanyDbContext dbContext,
        ICompanyDocumentIngestionStatusService ingestionStatusService,
        ICompanyDocumentVirusScanner virusScanner,
        ILogger<InlineCompanyDocumentIngestionOrchestrator> logger)
    {
        _dbContext = dbContext;
        _ingestionStatusService = ingestionStatusService;
        _virusScanner = virusScanner;
        _logger = logger;
    }

    public async Task ProcessUploadedAsync(Guid companyId, Guid documentId, CancellationToken cancellationToken)
    {
        await _ingestionStatusService.MarkPendingScanAsync(companyId, documentId, cancellationToken);

        var document = await GetDocumentAsync(companyId, documentId, cancellationToken);
        CompanyDocumentVirusScanResult scanResult;

        try
        {
            scanResult = await _virusScanner.ScanAsync(
                new CompanyDocumentVirusScanRequest(
                    document.CompanyId,
                    document.Id,
                    document.StorageKey,
                    document.StorageUrl,
                    document.OriginalFileName,
                    document.ContentType,
                    document.FileSizeBytes,
                    document.Metadata),
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Virus scan step failed unexpectedly. CompanyId: {CompanyId}, DocumentId: {DocumentId}.",
                companyId,
                documentId);

            await _ingestionStatusService.MarkFailedAsync(
                companyId,
                documentId,
                new CompanyDocumentIngestionFailure(
                    "virus_scan_error",
                    "The document could not be cleared by the virus scanning step.",
                    "Retry the scan or upload the document again when the scanning service is available.",
                    ex.Message,
                    true),
                cancellationToken);
            return;
        }

        switch (scanResult.Outcome)
        {
            case CompanyDocumentVirusScanOutcome.Clean:
                await _ingestionStatusService.MarkScanCleanAsync(companyId, documentId, scanResult, cancellationToken);
                break;
            case CompanyDocumentVirusScanOutcome.Blocked:
                await _ingestionStatusService.MarkBlockedAsync(companyId, documentId, scanResult, cancellationToken);
                break;
            case CompanyDocumentVirusScanOutcome.Error:
                await _ingestionStatusService.MarkFailedAsync(
                    companyId,
                    documentId,
                    new CompanyDocumentIngestionFailure(
                        scanResult.FailureCode ?? "virus_scan_error",
                        scanResult.Message ?? "The document could not be cleared by the virus scanning step.",
                        "Retry the scan or upload the document again when the scanning service is available.",
                        CanRetry: true),
                    cancellationToken);
                break;
            default:
                throw new InvalidOperationException($"Unsupported virus scan outcome '{scanResult.Outcome}'.");
        }
    }

    private async Task<CompanyKnowledgeDocument> GetDocumentAsync(Guid companyId, Guid documentId, CancellationToken cancellationToken)
    {
        return await _dbContext.CompanyKnowledgeDocuments
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId && x.Id == documentId, cancellationToken);
    }
}

public sealed class NoOpCompanyDocumentVirusScanner : ICompanyDocumentVirusScanner
{
    public Task<CompanyDocumentVirusScanResult> ScanAsync(CompanyDocumentVirusScanRequest request, CancellationToken cancellationToken)
    {
        return Task.FromResult(CompanyDocumentVirusScanResult.CleanPlaceholder());
    }
}
