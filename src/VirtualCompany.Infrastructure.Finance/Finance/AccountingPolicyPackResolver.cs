using Microsoft.Extensions.Hosting;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingPolicyPackResolver : IAccountingPolicyPackResolver
{
    private readonly IReadOnlyDictionary<(string Key, string Version), IAccountingPolicyPack> _packs;

    public AccountingPolicyPackResolver(IEnumerable<IAccountingPolicyPack> packs)
    {
        ArgumentNullException.ThrowIfNull(packs);
        var resolved = new Dictionary<(string Key, string Version), IAccountingPolicyPack>(PolicyPackIdentityComparer.Instance);
        foreach (var pack in packs)
        {
            ArgumentNullException.ThrowIfNull(pack);
            var identity = NormalizeIdentity(pack.Definition.PackKey, pack.Definition.Version);
            if (!resolved.TryAdd(identity, pack))
            {
                throw new InvalidOperationException(
                    $"Duplicate accounting policy-pack registration for key '{identity.Key}' and version '{identity.Version}'.");
            }
        }

        if (resolved.Count == 0)
        {
            throw new InvalidOperationException("At least one accounting policy pack must be registered.");
        }

        _packs = resolved;
    }

    public IAccountingPolicyPack Resolve(string packKey, string version)
    {
        var identity = NormalizeIdentity(packKey, version);
        return _packs.TryGetValue(identity, out var pack)
            ? pack
            : throw new AccountingConfigurationException(
                AccountingConfigurationReasonCodes.UnsupportedPackVersion,
                $"Accounting policy pack '{identity.Key}' version '{identity.Version}' is not available.");
    }

    public bool TryResolve(string packKey, string version, out IAccountingPolicyPack? pack)
    {
        var identity = NormalizeIdentity(packKey, version);
        return _packs.TryGetValue(identity, out pack);
    }

    public IReadOnlyList<IAccountingPolicyPack> GetAll() =>
        _packs.Values
            .OrderBy(pack => pack.Definition.PackKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(pack => pack.Definition.Version, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static (string Key, string Version) NormalizeIdentity(string packKey, string version)
    {
        if (string.IsNullOrWhiteSpace(packKey))
        {
            throw new ArgumentException("Policy pack key is required.", nameof(packKey));
        }

        if (string.IsNullOrWhiteSpace(version))
        {
            throw new ArgumentException("Policy pack version is required.", nameof(version));
        }

        return (packKey.Trim().ToLowerInvariant(), version.Trim());
    }

    private sealed class PolicyPackIdentityComparer : IEqualityComparer<(string Key, string Version)>
    {
        public static PolicyPackIdentityComparer Instance { get; } = new();

        public bool Equals((string Key, string Version) x, (string Key, string Version) y) =>
            string.Equals(x.Key, y.Key, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Version, y.Version, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string Key, string Version) obj) =>
            HashCode.Combine(
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Key),
                StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Version));
    }
}

public sealed class AccountingPolicyPackCatalogStartupValidator(IAccountingPolicyPackResolver resolver) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        _ = resolver.GetAll();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
