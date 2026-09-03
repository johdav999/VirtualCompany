using Microsoft.AspNetCore.DataProtection;

namespace VirtualCompany.Api;

public static class DataProtectionKeyRingConfiguration
{
    public const string ConfigurationKey = "DataProtection:KeyRingPath";
    public const string ApplicationName = "VirtualCompany.Api";

    public static DirectoryInfo Configure(
        IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var directory = ResolveDirectory(configuration, environment);
        EnsureAccessible(directory);

        services
            .AddDataProtection()
            .SetApplicationName(ApplicationName)
            .PersistKeysToFileSystem(directory);

        return directory;
    }

    public static DirectoryInfo ResolveDirectory(
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var configuredPath = configuration[ConfigurationKey]?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredPath))
        {
            if (!Path.IsPathFullyQualified(configuredPath))
            {
                throw new InvalidOperationException(
                    $"{ConfigurationKey} must be an absolute path outside the replaceable application deployment directory.");
            }

            return new DirectoryInfo(Path.GetFullPath(configuredPath));
        }

        if (!environment.IsDevelopment() && !environment.IsEnvironment("Testing"))
        {
            throw new InvalidOperationException(
                $"{ConfigurationKey} is required outside Development. Configure an absolute path on durable, access-controlled storage shared by every API instance.");
        }

        var localApplicationData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localApplicationData))
        {
            throw new InvalidOperationException(
                $"A stable Data Protection key-ring location could not be resolved. Configure {ConfigurationKey}.");
        }

        return new DirectoryInfo(Path.Combine(localApplicationData, "VirtualCompany", "DataProtection-Keys"));
    }

    private static void EnsureAccessible(DirectoryInfo directory)
    {
        try
        {
            directory.Create();
            var probePath = Path.Combine(directory.FullName, $".virtualcompany-write-probe-{Guid.NewGuid():N}");
            using var probe = new FileStream(
                probePath,
                FileMode.CreateNew,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.DeleteOnClose);
            probe.WriteByte(0x1);
            probe.Flush(flushToDisk: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                $"The Data Protection key-ring directory '{directory.FullName}' is not durable and writable. Correct {ConfigurationKey} or its storage permissions before starting the API.",
                ex);
        }
    }
}
