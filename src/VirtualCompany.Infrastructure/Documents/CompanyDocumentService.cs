using System.Text;
using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Documents;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;
using Microsoft.Extensions.Logging;

namespace VirtualCompany.Infrastructure.Documents;

public sealed class CompanyDocumentService : ICompanyDocumentService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _companyMembershipContextResolver;
    private readonly ICompanyDocumentStorage _documentStorage;
    private readonly IDocumentIngestionOrchestrator _ingestionOrchestrator;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly ICorrelationContextAccessor _correlationContextAccessor;
    private readonly CompanyDocumentOptions _options;
    private readonly ILogger<CompanyDocumentService> _logger;

    public CompanyDocumentService(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver companyMembershipContextResolver,
        ICompanyDocumentStorage documentStorage,
        ICurrentUserAccessor currentUserAccessor,
        IAuditEventWriter auditEventWriter,
        ICorrelationContextAccessor correlationContextAccessor,
        IDocumentIngestionOrchestrator ingestionOrchestrator,
        IOptions<CompanyDocumentOptions> options,
        ILogger<CompanyDocumentService> logger)
    {
        _dbContext = dbContext;
        _companyMembershipContextResolver = companyMembershipContextResolver;
        _documentStorage = documentStorage;
        _currentUserAccessor = currentUserAccessor;
        _auditEventWriter = auditEventWriter;
        _correlationContextAccessor = correlationContextAccessor;
        _ingestionOrchestrator = ingestionOrchestrator;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CompanyKnowledgeDocumentDto> UploadAsync(Guid companyId, UploadCompanyDocumentCommand command, CancellationToken cancellationToken)
    {
        await EnsureManagementAccessAsync(companyId, cancellationToken);

        var validationErrors = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var documentType = ValidateCommand(companyId, command, validationErrors, out var normalizedAccessScope);
        if (validationErrors.Count > 0)
        {
            throw new CompanyDocumentValidationException(validationErrors);
        }

        var documentId = Guid.NewGuid();
        var normalizedFileName = Path.GetFileName(command.OriginalFileName.Trim());
        var fileExtension = Path.GetExtension(normalizedFileName).ToLowerInvariant();
        var normalizedContentType = NormalizeContentType(command.ContentType);
        var storageKey = BuildStorageKey(companyId, documentId, normalizedFileName);
        var checksumSha256 = await TryComputeSha256Async(command.Content, cancellationToken);
        var normalizedMetadata = NormalizeMetadata(
            command.Metadata,
            normalizedFileName,
            normalizedContentType,
            fileExtension,
            command.Length,
            checksumSha256);

        DocumentStorageWriteResult storageResult;
        try
        {
            storageResult = await _documentStorage.WriteAsync(
                new DocumentStorageWriteRequest(
                    companyId,
                    documentId,
                    storageKey,
                    normalizedFileName,
                    normalizedContentType,
                    command.Content),
                cancellationToken);
        }
        catch (Exception ex)
        {
            await EnqueueAuditEventAsync(
                companyId,
                documentId,
                AuditEventActions.CompanyDocumentUploadFailed,
                AuditEventOutcomes.Failed,
                "The document could not be stored. Retry the upload or contact support if the problem persists.",
                BuildAuditMetadata(
                    documentType.ToStorageValue(),
                    normalizedFileName,
                    CompanyKnowledgeDocumentIngestionStatusValues.Failed,
                    "storage_failed",
                    normalizedAccessScope!,
                    storageKey),
                cancellationToken);

            await TrySaveAuditEventAsync(cancellationToken);

            throw new CompanyDocumentOperationException(
                "Document upload failed",
                "The document could not be stored. Retry the upload or contact support if the problem persists.",
                503,
                ex);
        }

        var document = new CompanyKnowledgeDocument(
            documentId,
            companyId,
            command.Title,
            documentType,
            storageResult.StorageKey,
            storageResult.StorageUrl,
            normalizedFileName,
            normalizedContentType,
            fileExtension,
            command.Length,
            normalizedMetadata,
            normalizedAccessScope!);

        _dbContext.CompanyKnowledgeDocuments.Add(document);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _dbContext.CompanyKnowledgeDocuments.Remove(document);
            await TryDeleteStoredObjectAsync(storageResult.StorageKey, cancellationToken);

            throw new CompanyDocumentOperationException(
                "Document metadata update failed",
                "The document was stored, but the metadata record could not be saved. Retry the upload or contact support if the problem persists.",
                503,
                ex);
        }

        // Upload persistence stops at the virus-scan gate. Downstream processing starts only after scan clearance.
        await _ingestionOrchestrator.ProcessUploadedAsync(companyId, documentId, cancellationToken);
        await EnqueueAuditEventAsync(
            companyId,
            documentId,
            AuditEventActions.CompanyDocumentUploaded,
            AuditEventOutcomes.Succeeded,
            null,
            BuildAuditMetadata(document),
            cancellationToken);

        await TrySaveAuditEventAsync(cancellationToken);
        return MapDto(document);
    }

    public async Task<IReadOnlyList<CompanyKnowledgeDocumentDto>> ListAsync(Guid companyId, CancellationToken cancellationToken)
    {
        await EnsureReadAccessAsync(companyId, cancellationToken);

        var documents = await _dbContext.CompanyKnowledgeDocuments
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId)
            .OrderByDescending(x => x.CreatedUtc)
            .ThenByDescending(x => x.Id)
            .ToListAsync(cancellationToken);

        return documents.Select(MapDto).ToArray();
    }

    public async Task<CompanyKnowledgeDocumentDto?> GetAsync(Guid companyId, Guid documentId, CancellationToken cancellationToken)
    {
        await EnsureReadAccessAsync(companyId, cancellationToken);

        var document = await _dbContext.CompanyKnowledgeDocuments
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == documentId, cancellationToken);

        return document is null ? null : MapDto(document);
    }

    private async Task EnsureReadAccessAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await _companyMembershipContextResolver.ResolveAsync(companyId, cancellationToken);
        if (membership is null)
        {
            throw new UnauthorizedAccessException("The current user cannot access documents for this company.");
        }
    }

    private async Task EnsureManagementAccessAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var membership = await _companyMembershipContextResolver.ResolveAsync(companyId, cancellationToken);
        if (membership is null)
        {
            throw new UnauthorizedAccessException("The current user cannot manage documents for this company.");
        }

        if (membership.MembershipRole == CompanyMembershipRole.Employee)
        {
            throw new UnauthorizedAccessException("Only managers, admins, or owners can upload company documents.");
        }
    }

    private CompanyKnowledgeDocumentType ValidateCommand(
        Guid companyId,
        UploadCompanyDocumentCommand command,
        IDictionary<string, string[]> errors,
        out CompanyKnowledgeDocumentAccessScope? accessScope)
    {
        accessScope = null;
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        AddRequiredStringValidation(result, "Title", command.Title, 200);
        AddRequiredStringValidation(result, "DocumentType", command.DocumentType, 64);
        ValidateAccessScope(companyId, command.AccessScope, result, out accessScope);
        
        if (!CompanyKnowledgeDocumentTypeValues.TryParse(command.DocumentType, out var documentType))
        {
            AddError(result, "DocumentType", CompanyKnowledgeDocumentTypeValues.BuildValidationMessage(command.DocumentType));
        }

        if (command.Length <= 0)
        {
            AddError(result, "File", "The uploaded file cannot be empty.");
        }

        if (command.Length > _options.MaxUploadBytes)
        {
            AddError(result, "File", $"The uploaded file exceeds the {_options.MaxUploadBytes} byte limit.");
        }

        if (string.IsNullOrWhiteSpace(command.OriginalFileName))
        {
            AddError(result, "File", "The uploaded file must include a file name.");
        }
        else
        {
            ValidateSupportedFileType(Path.GetFileName(command.OriginalFileName.Trim()), command.ContentType, result);
        }

        foreach (var pair in result)
        {
            errors[pair.Key] = pair.Value.ToArray();
        }

        return documentType;
    }

    private static void ValidateSupportedFileType(string fileName, string? contentType, IDictionary<string, List<string>> errors)
    {
        if (!CompanyDocumentFileRules.TryValidate(fileName, contentType, out var failure))
        {
            AddError(errors, "File", CompanyDocumentFileRules.FormatValidationMessage(failure!));
            return;
        }
    }

    private static void AddRequiredStringValidation(IDictionary<string, List<string>> errors, string key, string? value, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddError(errors, key, $"{key} is required.");
            return;
        }

        if (value.Trim().Length > maxLength)
        {
            AddError(errors, key, $"{key} must be {maxLength} characters or fewer.");
        }
    }

    private static void AddError(IDictionary<string, List<string>> errors, string key, string message)
    {
        if (!errors.TryGetValue(key, out var messages))
        {
            messages = new List<string>();
            errors[key] = messages;
        }

        messages.Add(message);
    }

    private static void ValidateAccessScope(
        Guid companyId,
        Dictionary<string, JsonNode?>? accessScope,
        IDictionary<string, List<string>> errors,
        out CompanyKnowledgeDocumentAccessScope? normalizedAccessScope)
    {
        normalizedAccessScope = null;
        if (CompanyKnowledgeDocumentAccessScope.TryNormalizeForCompany(companyId, accessScope, out normalizedAccessScope, out var validationErrors))
        {
            return;
        }

        foreach (var validationError in validationErrors)
        {
            AddError(errors, "AccessScope", validationError);
        }
    }

    private static Dictionary<string, JsonNode?> NormalizeMetadata(
        Dictionary<string, JsonNode?>? metadata,
        string originalFileName,
        string? contentType,
        string fileExtension,
        long fileSizeBytes,
        string? checksumSha256)
    {
        var normalized = CloneDictionary(metadata);
        normalized["original_file_name"] = JsonValue.Create(originalFileName);
        normalized["file_extension"] = JsonValue.Create(fileExtension);
        normalized["file_size_bytes"] = JsonValue.Create(fileSizeBytes);

        if (string.IsNullOrWhiteSpace(contentType))
        {
            normalized.Remove("content_type");
        }
        else
        {
            normalized["content_type"] = JsonValue.Create(contentType);
        }

        if (!string.IsNullOrWhiteSpace(checksumSha256))
        {
            normalized["checksum_sha256"] = JsonValue.Create(checksumSha256);
        }

        return normalized;
    }

    private static async Task<string?> TryComputeSha256Async(Stream content, CancellationToken cancellationToken)
    {
        if (!content.CanSeek)
        {
            return null;
        }

        RewindStream(content);
        using var sha256 = SHA256.Create();
        var hash = await sha256.ComputeHashAsync(content, cancellationToken);
        RewindStream(content);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private Task EnqueueAuditEventAsync(
        Guid companyId,
        Guid documentId,
        string action,
        string outcome,
        string? rationaleSummary,
        IReadOnlyDictionary<string, string?> metadata,
        CancellationToken cancellationToken) =>
        _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                companyId,
                AuditActorTypes.User,
                _currentUserAccessor.UserId,
                action,
                AuditTargetTypes.CompanyDocument,
                documentId.ToString("D"),
                outcome,
                Truncate(rationaleSummary, 512),
                ["knowledge_documents", "object_storage"],
                metadata,
                _correlationContextAccessor.CorrelationId),
            cancellationToken);

    private static string BuildStorageKey(Guid companyId, Guid documentId, string originalFileName)
    {
        var extension = Path.GetExtension(originalFileName).ToLowerInvariant();
        var baseName = Path.GetFileNameWithoutExtension(originalFileName);
        var safeBaseName = SanitizePathSegment(baseName);
        var safeExtension = string.IsNullOrWhiteSpace(extension) ? string.Empty : extension;
        var fileName = $"{safeBaseName}{safeExtension}";

        return $"companies/{companyId:N}/knowledge/{documentId:N}/{fileName}";
    }

    private static string SanitizePathSegment(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var character in value)
        {
            builder.Append(char.IsLetterOrDigit(character) || character is '-' or '_' ? character : '-');
        }

        var normalized = builder.ToString().Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "document" : normalized;
    }

    private static void RewindStream(Stream stream)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }
    }

    private static string? NormalizeContentType(string? contentType) =>
        string.IsNullOrWhiteSpace(contentType) ? null : contentType.Trim();

    private static Dictionary<string, JsonNode?> CloneDictionary(Dictionary<string, JsonNode?>? value)
    {
        var clone = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase);
        if (value is null)
        {
            return clone;
        }

        foreach (var pair in value)
        {
            clone[pair.Key] = pair.Value?.DeepClone();
        }

        return clone;
    }

    private static IReadOnlyDictionary<string, string?> BuildAuditMetadata(CompanyKnowledgeDocument document)
    {
        return BuildAuditMetadata(
            document.DocumentType.ToStorageValue(),
            document.OriginalFileName,
            document.IngestionStatus.ToStorageValue(),
            document.FailureCode,
            document.AccessScope,
            document.StorageKey,
            document.StorageUrl);
    }

    private static IReadOnlyDictionary<string, string?> BuildAuditMetadata(
        string documentType,
        string fileName,
        string ingestionStatus,
        string? failureCode,
        CompanyKnowledgeDocumentAccessScope accessScope,
        string? storageKey,
        string? storageUrl = null)
    {
        var metadata = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["documentType"] = documentType,
            ["fileName"] = fileName,
            ["ingestionStatus"] = ingestionStatus,
            ["failureAction"] = null,
            ["canRetry"] = "false",
            ["failureCode"] = failureCode,
            ["storageKey"] = storageKey
        };

        if (!string.IsNullOrWhiteSpace(storageUrl))
        {
            metadata["storageUrl"] = storageUrl;
        }

        metadata["visibility"] = accessScope.Visibility;

        return metadata;
    }

    private async Task TrySaveAuditEventAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch
        {
        }
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Length <= maxLength
                ? value.Trim()
                : value.Trim()[..maxLength];

    private async Task TryDeleteStoredObjectAsync(string storageKey, CancellationToken cancellationToken)
    {
        try
        {
            await _documentStorage.DeleteAsync(storageKey, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Best-effort cleanup failed for stored document object {StorageKey}.", storageKey);
        }
    }

    private static CompanyKnowledgeDocumentDto MapDto(CompanyKnowledgeDocument document) =>
        new(
            document.Id,
            document.CompanyId,
            document.Title,
            document.DocumentType.ToStorageValue(),
            document.SourceType.ToStorageValue(),
            document.OriginalFileName,
            document.ContentType,
            document.FileExtension,
            document.FileSizeBytes,
            document.StorageKey,
            document.StorageUrl,
            CloneDictionary(document.Metadata),
            document.AccessScope.Clone(),
            document.IngestionStatus.ToStorageValue(),
            document.FailureCode,
            document.FailureMessage,
            document.FailureAction,
            document.CanRetry,
            document.CreatedUtc,
            document.UpdatedUtc,
            document.UploadedUtc,
            document.ProcessingStartedUtc,
            document.ProcessedUtc,
            document.FailedUtc);
}
