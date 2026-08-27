using Microsoft.Extensions.Diagnostics.HealthChecks;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

/// <summary>
/// Release metadata is deliberately registered in code beside the immutable pack catalog. A reviewed
/// pack must be introduced as a new version and its exact evidence record must be added here; operators
/// cannot turn a candidate into a validated pack with an environment-variable switch.
/// </summary>
public static class AccountingPolicyPackValidationEvidenceCatalog
{
    public static IReadOnlyList<AccountingPolicyPackValidationEvidence> All { get; } =
        Array.Empty<AccountingPolicyPackValidationEvidence>();
}

public sealed class AccountingPolicyPackValidationRegistry : IAccountingPolicyPackValidationRegistry
{
    private const int MaximumTextLength = 2_000;
    private const int MaximumListItems = 100;
    private readonly IReadOnlyList<AccountingPolicyPackValidationEvidence> _evidence;

    public AccountingPolicyPackValidationRegistry(IEnumerable<AccountingPolicyPackValidationEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        var records = evidence.ToArray();
        foreach (var record in records) Validate(record);

        var duplicate = records.GroupBy(x => (Normalize(x.PackKey), Normalize(x.PackVersion)))
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
            throw new InvalidOperationException(
                $"Duplicate accounting policy-pack validation evidence for key '{duplicate.Key.Item1}' and version '{duplicate.Key.Item2}'.");

        _evidence = records;
    }

    public AccountingPolicyPackValidationDecision Evaluate(IAccountingPolicyPack pack, DateOnly evaluationDate)
    {
        ArgumentNullException.ThrowIfNull(pack);
        var definition = pack.Definition;
        if (definition.IsCountryNeutral)
            return new(AccountingPolicyPackValidationStates.NotApplicable, false,
                "The selected policy pack is country-neutral and makes no statutory compliance claim.", null);

        var evidence = _evidence.SingleOrDefault(x =>
            string.Equals(x.PackKey, definition.PackKey, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.PackVersion, definition.Version, StringComparison.OrdinalIgnoreCase));
        if (evidence is null)
            return new(AccountingPolicyPackValidationStates.MissingEvidence, false,
                "Qualified reviewer evidence is not registered for this exact policy-pack version.", null);

        if (!string.Equals(evidence.DefinitionHash, pack.DefinitionHash, StringComparison.OrdinalIgnoreCase))
            return new(AccountingPolicyPackValidationStates.DefinitionHashMismatch, false,
                "The policy-pack definition hash does not match its qualified reviewer evidence.", evidence);

        if (evidence.ExpiresOn is { } expiresOn && expiresOn < evaluationDate)
            return new(AccountingPolicyPackValidationStates.EvidenceExpired, false,
                "The qualified reviewer evidence has expired and revalidation is required.", evidence);

        if (!definition.IsStatutoryComplianceValidated)
            return new(AccountingPolicyPackValidationStates.MissingEvidence, false,
                "Evidence exists, but this immutable policy-pack version does not declare statutory validation. Introduce a new reviewed version instead of changing it in place.", evidence);

        return new(AccountingPolicyPackValidationStates.Validated, true,
            "The immutable policy-pack definition matches current qualified reviewer evidence.", evidence);
    }

    public IReadOnlyList<AccountingPolicyPackValidationEvidence> GetAll() => _evidence;

    private static void Validate(AccountingPolicyPackValidationEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        Required(evidence.PackKey, nameof(evidence.PackKey));
        Required(evidence.PackVersion, nameof(evidence.PackVersion));
        Hash(evidence.DefinitionHash, nameof(evidence.DefinitionHash));
        Required(evidence.ReviewerDisplayName, nameof(evidence.ReviewerDisplayName));
        Required(evidence.ReviewerReference, nameof(evidence.ReviewerReference));
        Required(evidence.ReviewScope, nameof(evidence.ReviewScope));
        Required(evidence.EvidenceDocumentReference, nameof(evidence.EvidenceDocumentReference));
        Hash(evidence.EvidenceDocumentHash, nameof(evidence.EvidenceDocumentHash));
        BoundedList(evidence.ApprovedFixtureIds, nameof(evidence.ApprovedFixtureIds), requireOne: true);
        BoundedList(evidence.Limitations, nameof(evidence.Limitations), requireOne: false);
        BoundedList(evidence.RevalidationTriggers, nameof(evidence.RevalidationTriggers), requireOne: true);
        if (evidence.ExpiresOn < evidence.ReviewedOn)
            throw new InvalidOperationException("Accounting policy-pack validation evidence cannot expire before its review date.");
    }

    private static void Required(string value, string name)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length > MaximumTextLength)
            throw new InvalidOperationException($"Accounting policy-pack validation evidence field '{name}' is required and must be bounded.");
    }

    private static void Hash(string value, string name)
    {
        if (value?.Length != 64 || value.Any(character => !Uri.IsHexDigit(character)))
            throw new InvalidOperationException($"Accounting policy-pack validation evidence field '{name}' must be a SHA-256 hash.");
    }

    private static void BoundedList(IReadOnlyList<string> values, string name, bool requireOne)
    {
        if (values is null || values.Count > MaximumListItems || requireOne && values.Count == 0 ||
            values.Any(value => string.IsNullOrWhiteSpace(value) || value.Trim().Length > MaximumTextLength))
            throw new InvalidOperationException($"Accounting policy-pack validation evidence field '{name}' is invalid or exceeds its bound.");
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}

public sealed class SwedishAccountingValidationHealthCheck(
    IAccountingPolicyPackResolver resolver,
    IAccountingPolicyPackValidationRegistry validationRegistry,
    TimeProvider timeProvider) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var packs = resolver.GetAll()
            .Where(pack => string.Equals(pack.Definition.CountryOrRegion, "SE", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (packs.Length == 0)
            return Task.FromResult(HealthCheckResult.Healthy("No Swedish statutory policy pack is registered."));

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);
        var decisions = packs.Select(pack => new
        {
            pack.Definition.PackKey,
            pack.Definition.Version,
            pack.DefinitionHash,
            Decision = validationRegistry.Evaluate(pack, today)
        }).ToArray();
        var validated = decisions.Where(x => x.Decision.IsValidated).ToArray();
        var data = new Dictionary<string, object>
        {
            ["registeredSwedishPackCount"] = packs.Length,
            ["validatedSwedishPackCount"] = validated.Length,
            ["packs"] = decisions.Select(x => new
            {
                x.PackKey,
                x.Version,
                x.DefinitionHash,
                x.Decision.State
            }).ToArray()
        };

        return Task.FromResult(HealthCheckResult.Healthy(
            validated.Length > 0
                ? "At least one exact Swedish policy-pack version has current qualified reviewer evidence."
                : "The validation gate is operational; Swedish statutory release remains blocked because no exact registered pack version has current qualified reviewer evidence.",
            data));
    }
}
