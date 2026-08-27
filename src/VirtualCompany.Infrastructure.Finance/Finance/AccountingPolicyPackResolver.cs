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
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pack in packs)
        {
            ArgumentNullException.ThrowIfNull(pack);
            var identity = NormalizeIdentity(pack.Definition.PackKey, pack.Definition.Version);
            if (!resolved.TryAdd(identity, pack))
            {
                throw new InvalidOperationException(
                    $"Duplicate accounting policy-pack registration for key '{identity.Key}' and version '{identity.Version}'.");
            }

            if (pack.DefinitionHash.Length != 64 || pack.DefinitionHash.Any(character => !Uri.IsHexDigit(character)))
            {
                throw new InvalidOperationException(
                    $"Accounting policy pack '{identity.Key}' version '{identity.Version}' has an invalid SHA-256 definition hash.");
            }

            if (!hashes.Add(pack.DefinitionHash))
            {
                throw new InvalidOperationException(
                    $"Duplicate accounting policy-pack definition hash for key '{identity.Key}' and version '{identity.Version}'. Each catalog entry must identify a distinct immutable definition.");
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

public sealed class AccountingPolicyPackCatalogStartupValidator(
    IAccountingPolicyPackResolver resolver,
    IAccountingTaxDecisionPolicy taxDecisionPolicy,
    IAccountingPolicyPackValidationRegistry validationRegistry,
    TimeProvider timeProvider) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        foreach (var pack in resolver.GetAll())
        {
            var issues = taxDecisionPolicy.Validate(pack).Where(x => x.IsBlocking).ToArray();
            if (issues.Length > 0)
                throw new InvalidOperationException(
                    $"Accounting policy pack '{pack.Definition.PackKey}' version '{pack.Definition.Version}' has invalid tax configuration: " +
                    string.Join(" ", issues.Select(x => x.Explanation)));

            var validation = validationRegistry.Evaluate(pack, today);
            if (pack.Definition.IsStatutoryComplianceValidated && !validation.IsValidated)
                throw new InvalidOperationException(
                    $"Accounting policy pack '{pack.Definition.PackKey}' version '{pack.Definition.Version}' declares statutory validation but exact qualified reviewer evidence is not current: {validation.State}.");
        }

        foreach (var evidence in validationRegistry.GetAll())
        {
            if (!resolver.TryResolve(evidence.PackKey, evidence.PackVersion, out var reviewedPack) || reviewedPack is null)
                throw new InvalidOperationException(
                    $"Accounting policy-pack validation evidence references unavailable pack '{evidence.PackKey}' version '{evidence.PackVersion}'.");
            if (!reviewedPack.Definition.IsStatutoryComplianceValidated)
                throw new InvalidOperationException(
                    $"Accounting policy-pack validation evidence references unvalidated historical pack '{evidence.PackKey}' version '{evidence.PackVersion}'. Introduce a new reviewed version instead.");
            var validation = validationRegistry.Evaluate(reviewedPack, today);
            if (!validation.IsValidated)
                throw new InvalidOperationException(
                    $"Accounting policy-pack validation evidence for '{evidence.PackKey}' version '{evidence.PackVersion}' is not current: {validation.State}.");
        }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
