namespace VirtualCompany.Application.Security;

public sealed record PlatformSecretValue(
    string Value,
    string Version,
    DateTime UpdatedUtc);

public sealed record PlatformSecretWriteResult(
    string Version,
    DateTime UpdatedUtc);

public interface IPlatformSecretStore
{
    string BackendName { get; }
    bool SupportsWrites { get; }

    Task<PlatformSecretValue?> GetAsync(
        string name,
        string? version,
        CancellationToken cancellationToken);

    Task<PlatformSecretWriteResult> SetAsync(
        string name,
        string value,
        CancellationToken cancellationToken);
}
