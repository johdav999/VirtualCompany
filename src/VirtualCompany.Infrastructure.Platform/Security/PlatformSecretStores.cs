using System.Text.Json;
using Azure;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Security;

namespace VirtualCompany.Infrastructure.Security;

public sealed class PlatformSecretStoreOptions
{
    public const string SectionName = "PlatformSecrets";

    public string Provider { get; set; } = "auto";
    public string? KeyVaultUri { get; set; }
    public string? LocalEncryptedFilePath { get; set; }
}

public sealed class AzureKeyVaultPlatformSecretStore : IPlatformSecretStore
{
    private readonly SecretClient _client;
    private readonly TimeProvider _timeProvider;

    public AzureKeyVaultPlatformSecretStore(Uri vaultUri, TimeProvider timeProvider)
    {
        _client = new SecretClient(vaultUri, new DefaultAzureCredential());
        _timeProvider = timeProvider;
    }

    public string BackendName => "Azure Key Vault";
    public bool SupportsWrites => true;

    public async Task<PlatformSecretValue?> GetAsync(
        string name,
        string? version,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await _client.GetSecretAsync(NormalizeName(name), version, cancellationToken);
            var secret = response.Value;
            return new PlatformSecretValue(
                secret.Value,
                secret.Properties.Version,
                secret.Properties.UpdatedOn?.UtcDateTime
                    ?? secret.Properties.CreatedOn?.UtcDateTime
                    ?? _timeProvider.GetUtcNow().UtcDateTime);
        }
        catch (RequestFailedException exception) when (exception.Status == 404)
        {
            return null;
        }
    }

    public async Task<PlatformSecretWriteResult> SetAsync(
        string name,
        string value,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Secret value is required.", nameof(value));
        }

        var response = await _client.SetSecretAsync(NormalizeName(name), value, cancellationToken);
        return new PlatformSecretWriteResult(
            response.Value.Properties.Version,
            response.Value.Properties.UpdatedOn?.UtcDateTime
                ?? response.Value.Properties.CreatedOn?.UtcDateTime
                ?? _timeProvider.GetUtcNow().UtcDateTime);
    }

    private static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Secret name is required.", nameof(name));
        }

        var normalized = new string(name
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) || character == '-' ? character : '-')
            .ToArray());
        if (normalized.Length is < 1 or > 127)
        {
            throw new ArgumentOutOfRangeException(nameof(name), "Secret name must contain between 1 and 127 characters.");
        }

        return normalized;
    }
}

public sealed class DataProtectionFilePlatformSecretStore : IPlatformSecretStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly IDataProtectionProvider _dataProtectionProvider;
    private readonly TimeProvider _timeProvider;
    private readonly string _filePath;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public DataProtectionFilePlatformSecretStore(
        IDataProtectionProvider dataProtectionProvider,
        TimeProvider timeProvider,
        string filePath)
    {
        _dataProtectionProvider = dataProtectionProvider;
        _timeProvider = timeProvider;
        _filePath = Path.GetFullPath(filePath);
    }

    public string BackendName => "Encrypted local secret store";
    public bool SupportsWrites => true;

    public async Task<PlatformSecretValue?> GetAsync(
        string name,
        string? version,
        CancellationToken cancellationToken)
    {
        var normalizedName = NormalizeName(name);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadAsync(cancellationToken);
            if (!document.Secrets.TryGetValue(normalizedName, out var versions) || versions.Count == 0)
            {
                return null;
            }

            var stored = string.IsNullOrWhiteSpace(version)
                ? versions.OrderByDescending(item => item.UpdatedUtc).First()
                : versions.SingleOrDefault(item => string.Equals(item.Version, version, StringComparison.Ordinal));
            if (stored is null)
            {
                return null;
            }

            var protector = CreateProtector(normalizedName, stored.Version);
            return new PlatformSecretValue(
                protector.Unprotect(stored.ProtectedValue),
                stored.Version,
                stored.UpdatedUtc);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<PlatformSecretWriteResult> SetAsync(
        string name,
        string value,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Secret value is required.", nameof(value));
        }

        var normalizedName = NormalizeName(name);
        var version = Guid.NewGuid().ToString("N");
        var updatedUtc = _timeProvider.GetUtcNow().UtcDateTime;
        var protectedValue = CreateProtector(normalizedName, version).Protect(value);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var document = await ReadAsync(cancellationToken);
            if (!document.Secrets.TryGetValue(normalizedName, out var versions))
            {
                versions = [];
                document.Secrets[normalizedName] = versions;
            }

            versions.Add(new StoredSecretVersion(version, protectedValue, updatedUtc));
            if (versions.Count > 10)
            {
                versions.RemoveRange(0, versions.Count - 10);
            }

            await WriteAsync(document, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }

        return new PlatformSecretWriteResult(version, updatedUtc);
    }

    private IDataProtector CreateProtector(string name, string version) =>
        _dataProtectionProvider.CreateProtector(
            "VirtualCompany.PlatformSecretStore",
            name,
            version);

    private async Task<SecretDocument> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_filePath))
        {
            return new SecretDocument();
        }

        await using var stream = File.OpenRead(_filePath);
        return await JsonSerializer.DeserializeAsync<SecretDocument>(stream, JsonOptions, cancellationToken)
            ?? new SecretDocument();
    }

    private async Task WriteAsync(SecretDocument document, CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_filePath)
            ?? throw new InvalidOperationException("A platform secret-store directory could not be resolved.");
        Directory.CreateDirectory(directory);
        var temporaryPath = $"{_filePath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             4096,
                             FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, document, JsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, _filePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private static string NormalizeName(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("Secret name is required.", nameof(name))
            : name.Trim().ToLowerInvariant();

    private sealed class SecretDocument
    {
        public Dictionary<string, List<StoredSecretVersion>> Secrets { get; init; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed record StoredSecretVersion(
        string Version,
        string ProtectedValue,
        DateTime UpdatedUtc);
}

public sealed class UnavailablePlatformSecretStore : IPlatformSecretStore
{
    public string BackendName => "Not configured";
    public bool SupportsWrites => false;

    public Task<PlatformSecretValue?> GetAsync(string name, string? version, CancellationToken cancellationToken) =>
        Task.FromResult<PlatformSecretValue?>(null);

    public Task<PlatformSecretWriteResult> SetAsync(string name, string value, CancellationToken cancellationToken) =>
        throw new InvalidOperationException(
            "A production secret store has not been configured. Configure PlatformSecrets:KeyVaultUri.");
}

public static class PlatformSecretStoreRegistration
{
    public static IServiceCollection AddPlatformSecretStore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<PlatformSecretStoreOptions>()
            .Bind(configuration.GetSection(PlatformSecretStoreOptions.SectionName));

        services.AddSingleton<IPlatformSecretStore>(serviceProvider =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<PlatformSecretStoreOptions>>().Value;
            var environment = serviceProvider.GetRequiredService<IHostEnvironment>();
            var timeProvider = serviceProvider.GetRequiredService<TimeProvider>();
            var configuredVaultUri =
                options.KeyVaultUri
                ?? configuration["AzureKeyVault:Uri"]
                ?? configuration["KeyVault:Uri"];
            var provider = string.IsNullOrWhiteSpace(options.Provider)
                ? "auto"
                : options.Provider.Trim().ToLowerInvariant();

            if (provider is "azure_key_vault" or "azure-key-vault" or "keyvault" ||
                provider == "auto" && !string.IsNullOrWhiteSpace(configuredVaultUri))
            {
                if (!Uri.TryCreate(configuredVaultUri, UriKind.Absolute, out var vaultUri))
                {
                    throw new InvalidOperationException("PlatformSecrets:KeyVaultUri must be an absolute URI.");
                }

                return new AzureKeyVaultPlatformSecretStore(vaultUri, timeProvider);
            }

            if (provider is "local_encrypted_file" or "local-encrypted-file" ||
                provider == "auto" && environment.IsDevelopment())
            {
                if (!environment.IsDevelopment())
                {
                    throw new InvalidOperationException(
                        "The encrypted local platform secret store is only supported in Development.");
                }

                var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var path = string.IsNullOrWhiteSpace(options.LocalEncryptedFilePath)
                    ? Path.Combine(localApplicationData, "VirtualCompany", "Secrets", "platform-secrets.json")
                    : options.LocalEncryptedFilePath;
                return new DataProtectionFilePlatformSecretStore(
                    serviceProvider.GetRequiredService<IDataProtectionProvider>(),
                    timeProvider,
                    path);
            }

            return new UnavailablePlatformSecretStore();
        });

        return services;
    }
}
