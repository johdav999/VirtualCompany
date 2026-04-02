using System.Collections.ObjectModel;
using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Application.Documents;

public sealed record UploadCompanyDocumentCommand(
    string Title,
    string DocumentType,
    Dictionary<string, JsonNode?>? AccessScope,
    Dictionary<string, JsonNode?>? Metadata,
    string OriginalFileName,
    string? ContentType,
    long Length,
    Stream Content);

public sealed record CompanyDocumentIngestionFailure(
    string Code,
    string Message,
    string Action,
    string? TechnicalDetail = null,
    bool CanRetry = false);

public sealed record CompanyKnowledgeDocumentDto(
    Guid Id,
    Guid CompanyId,
    string Title,
    string DocumentType,
    string SourceType,
    string OriginalFileName,
    string? ContentType,
    string FileExtension,
    long FileSizeBytes,
    string StorageKey,
    string? StorageUrl,
    IReadOnlyDictionary<string, JsonNode?> Metadata,
    CompanyKnowledgeDocumentAccessScope AccessScope,
    string IngestionStatus,
    string? FailureCode,
    string? FailureMessage,
    string? FailureAction,
    bool CanRetry,
    string IndexingStatus,
    string? IndexingFailureCode,
    string? IndexingFailureMessage,
    int CurrentChunkSetVersion,
    int ActiveChunkCount,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    DateTime? IndexedUtc,
    DateTime? UploadedUtc,
    DateTime? ProcessingStartedUtc,
    DateTime? ProcessedUtc,
    DateTime? FailedUtc);

public interface ICompanyDocumentService
{
    Task<IReadOnlyList<CompanyKnowledgeDocumentDto>> ListAsync(Guid companyId, CancellationToken cancellationToken);
    Task<CompanyKnowledgeDocumentDto> UploadAsync(Guid companyId, UploadCompanyDocumentCommand command, CancellationToken cancellationToken);
    Task<CompanyKnowledgeDocumentDto?> GetAsync(Guid companyId, Guid documentId, CancellationToken cancellationToken);
}

public interface ICompanyDocumentIngestionStatusService
{
    Task MarkPendingScanAsync(Guid companyId, Guid documentId, CancellationToken cancellationToken);
    Task MarkScanCleanAsync(Guid companyId, Guid documentId, CompanyDocumentVirusScanResult result, CancellationToken cancellationToken);
    Task MarkBlockedAsync(Guid companyId, Guid documentId, CompanyDocumentVirusScanResult result, CancellationToken cancellationToken);
    Task MarkProcessingAsync(Guid companyId, Guid documentId, CancellationToken cancellationToken);
    Task MarkProcessedAsync(Guid companyId, Guid documentId, CancellationToken cancellationToken);
    Task MarkFailedAsync(Guid companyId, Guid documentId, CompanyDocumentIngestionFailure failure, CancellationToken cancellationToken);
}

public sealed record DocumentStorageWriteRequest(
    Guid CompanyId,
    Guid DocumentId,
    string StorageKey,
    string OriginalFileName,
    string? ContentType,
    Stream Content);

public sealed record DocumentStorageWriteResult(string StorageKey, string? StorageUrl);

public interface ICompanyDocumentStorage
{
    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken);
    Task<DocumentStorageWriteResult> WriteAsync(DocumentStorageWriteRequest request, CancellationToken cancellationToken);
    Task DeleteAsync(string storageKey, CancellationToken cancellationToken);
}

public enum CompanyDocumentVirusScanOutcome
{
    Clean = 1,
    Blocked = 2,
    Error = 3
}

public static class CompanyDocumentVirusScanOutcomeValues
{
    public const string Clean = "clean";
    public const string Blocked = "blocked";
    public const string Error = "error";

    private static readonly IReadOnlyDictionary<CompanyDocumentVirusScanOutcome, string> Values =
        new Dictionary<CompanyDocumentVirusScanOutcome, string>
        {
            [CompanyDocumentVirusScanOutcome.Clean] = Clean,
            [CompanyDocumentVirusScanOutcome.Blocked] = Blocked,
            [CompanyDocumentVirusScanOutcome.Error] = Error
        };

    public static string ToStorageValue(this CompanyDocumentVirusScanOutcome outcome) =>
        Values.TryGetValue(outcome, out var value)
            ? value
            : throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unsupported virus scan outcome.");
}

public sealed record CompanyDocumentVirusScanRequest(
    Guid CompanyId,
    Guid DocumentId,
    string StorageKey,
    string? StorageUrl,
    string OriginalFileName,
    string? ContentType,
    long FileSizeBytes,
    IReadOnlyDictionary<string, JsonNode?> Metadata);

public sealed record CompanyDocumentVirusScanResult(
    CompanyDocumentVirusScanOutcome Outcome,
    string ScannerName,
    string? ScannerVersion,
    DateTime ScannedUtc,
    string? FailureCode = null,
    string? Message = null)
{
    public static CompanyDocumentVirusScanResult CleanPlaceholder(
        string scannerName = "no_op_placeholder",
        string? scannerVersion = "1.0",
        string? message = "Placeholder virus scan completed. Replace the registered scanner to enforce real malware inspection.") =>
        new(
            CompanyDocumentVirusScanOutcome.Clean,
            scannerName,
            scannerVersion,
            DateTime.UtcNow,
            null,
            message);

    public static CompanyDocumentVirusScanResult Blocked(
        string scannerName,
        string? scannerVersion,
        string failureCode,
        string message) =>
        new(CompanyDocumentVirusScanOutcome.Blocked, scannerName, scannerVersion, DateTime.UtcNow, failureCode, message);

    public static CompanyDocumentVirusScanResult Error(
        string scannerName,
        string? scannerVersion,
        string failureCode,
        string message) =>
        new(CompanyDocumentVirusScanOutcome.Error, scannerName, scannerVersion, DateTime.UtcNow, failureCode, message);
}

public interface ICompanyDocumentVirusScanner
{
    Task<CompanyDocumentVirusScanResult> ScanAsync(CompanyDocumentVirusScanRequest request, CancellationToken cancellationToken);
}

public interface IDocumentIngestionOrchestrator
{
    Task ProcessUploadedAsync(Guid companyId, Guid documentId, CancellationToken cancellationToken);
}

public sealed class CompanyDocumentValidationException : Exception
{
    public CompanyDocumentValidationException(IDictionary<string, string[]> errors)
        : base("Company document validation failed.")
    {
        Errors = new ReadOnlyDictionary<string, string[]>(new Dictionary<string, string[]>(errors, StringComparer.OrdinalIgnoreCase));
    }

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}

public sealed class CompanyDocumentOperationException : Exception
{
    public CompanyDocumentOperationException(string title, string detail, int statusCode = 503, Exception? innerException = null)
        : base(detail, innerException)
    {
        Title = title;
        Detail = detail;
        StatusCode = statusCode;
    }

    public string Title { get; }
    public string Detail { get; }
    public int StatusCode { get; }
}