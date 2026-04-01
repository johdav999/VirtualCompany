using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Documents;

namespace VirtualCompany.Infrastructure.Documents;

public sealed class LocalCompanyDocumentStorage : ICompanyDocumentStorage
{
    private readonly IHostEnvironment _hostEnvironment;
    private readonly CompanyDocumentOptions _options;

    public LocalCompanyDocumentStorage(IHostEnvironment hostEnvironment, IOptions<CompanyDocumentOptions> options)
    {
        _hostEnvironment = hostEnvironment;
        _options = options.Value;
    }

    public async Task<DocumentStorageWriteResult> WriteAsync(DocumentStorageWriteRequest request, CancellationToken cancellationToken)
    {
        if (request.Content.CanSeek)
        {
            request.Content.Position = 0;
        }

        var fullPath = ResolveFullPath(request.StorageKey);
        var directoryPath = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directoryPath))
        {
            Directory.CreateDirectory(directoryPath);
        }

        await using var output = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await request.Content.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);

        return new DocumentStorageWriteResult(request.StorageKey, BuildStorageUrl(request.StorageKey));
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var fullPath = ResolveFullPath(storageKey);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
        }

        return Task.CompletedTask;
    }

    private string ResolveFullPath(string storageKey)
    {
        var rootPath = ResolveRootPath();
        var fullRootPath = Path.GetFullPath(rootPath);
        var segments = storageKey
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(SanitizePathSegment)
            .ToArray();

        var fullPath = Path.GetFullPath(Path.Combine(fullRootPath, Path.Combine(segments)));
        if (!fullPath.StartsWith(fullRootPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The resolved storage path is outside the configured document storage root.");
        }

        return fullPath;
    }

    private string? BuildStorageUrl(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(_options.Storage.BaseUri))
        {
            return null;
        }

        var normalizedBaseUri = _options.Storage.BaseUri.TrimEnd('/') + "/";
        var relativePath = string.Join(
            '/',
            storageKey
                .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(Uri.EscapeDataString));

        return new Uri(new Uri(normalizedBaseUri, UriKind.Absolute), relativePath).ToString();
    }

    private string ResolveRootPath()
    {
        var configuredPath = string.IsNullOrWhiteSpace(_options.Storage.RootPath)
            ? "App_Data/object-storage"
            : _options.Storage.RootPath;

        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(_hostEnvironment.ContentRootPath, configuredPath));
    }

    private static string SanitizePathSegment(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitizedCharacters = value
            .Select(character => invalidCharacters.Contains(character) ? '-' : character)
            .ToArray();

        var sanitized = new string(sanitizedCharacters)
            .Replace("..", "-", StringComparison.Ordinal)
            .Trim();

        return string.IsNullOrWhiteSpace(sanitized) ? "document" : sanitized;
    }
}