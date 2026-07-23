using System.Text.Json.Nodes;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Finance;

internal static class FinanceSeedingMetadata
{
    private const string ExtensionKey = "financeSeeding";
    private const string RecordChecksKey = "recordChecks";

    public static FinanceSeedingMetadataSnapshot? Read(Company company)
    {
        ArgumentNullException.ThrowIfNull(company);

        var extensionSnapshot = Read(company.Settings);
        var hasPersistedLifecycleMetadata = company.FinanceSeedStatus != FinanceSeedingState.NotSeeded || company.FinanceSeededUtc.HasValue;
        if (!hasPersistedLifecycleMetadata && extensionSnapshot is null)
        {
            return null;
        }

        return new FinanceSeedingMetadataSnapshot(
            extensionSnapshot is not null,
            company.FinanceSeedStatus,
            extensionSnapshot?.Accounts,
            extensionSnapshot?.Counterparties,
            extensionSnapshot?.Transactions,
            extensionSnapshot?.Balances,
            extensionSnapshot?.PolicyConfiguration,
            extensionSnapshot?.Invoices,
            extensionSnapshot?.Bills,
            extensionSnapshot?.SeedVersion,
            company.FinanceSeededUtc ?? extensionSnapshot?.SeededAtUtc,
            company.FinanceSeedStatusUpdatedUtc);
    }

    public static FinanceSeedingMetadataSnapshot? Read(CompanySettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (!settings.Extensions.TryGetValue(ExtensionKey, out var node) || node is not JsonObject metadata)
        {
            return null;
        }

        if (!FinanceSeedingStateValues.TryParse(ReadString(metadata, "state"), out var state))
        {
            return null;
        }

        var recordChecks = metadata[RecordChecksKey] as JsonObject;
        return new FinanceSeedingMetadataSnapshot(
            true,
            state,
            ReadBool(recordChecks, "accounts"),
            ReadBool(recordChecks, "counterparties"),
            ReadBool(recordChecks, "transactions"),
            ReadBool(recordChecks, "balances"),
            ReadBool(recordChecks, "policyConfiguration"),
            ReadBool(recordChecks, "invoices"),
            ReadBool(recordChecks, "bills"),
            ReadString(metadata, "seedVersion"),
            ReadDateTime(metadata, "seededAtUtc"),
            null);
    }

    public static void MarkNotSeeded(Company company, DateTime? updatedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(company);

        company.SetFinanceSeedStatus(FinanceSeedingState.NotSeeded, updatedAtUtc);
        company.Settings.Extensions.Remove(ExtensionKey);
    }

    public static void MarkSeeding(
        Company company,
        DateTime? updatedAtUtc = null,
        DateTime? requestedAtUtc = null,
        DateTime? startedAtUtc = null,
        string? triggerSource = null,
        Guid? jobId = null,
        string? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(company);

        var normalizedUpdatedAtUtc = NormalizeUtc(updatedAtUtc ?? DateTime.UtcNow);
        company.SetFinanceSeedStatus(FinanceSeedingState.Seeding, normalizedUpdatedAtUtc);

        var metadata = GetOrCreateMetadata(company);
        metadata["state"] = JsonValue.Create(FinanceSeedingState.Seeding.ToStorageValue());
        WriteOptionalDateTime(metadata, "seedRequestedAtUtc", requestedAtUtc);
        WriteOptionalDateTime(metadata, "seedStartedAtUtc", startedAtUtc);
        WriteOptionalString(metadata, "triggerSource", triggerSource);
        WriteOptionalGuid(metadata, "seedJobId", jobId);
        WriteOptionalString(metadata, "seedCorrelationId", correlationId);
        metadata.Remove("seededAtUtc");
        metadata.Remove("seedFailedAtUtc");
        metadata.Remove("failureCode");
        metadata.Remove("failureMessage");
        metadata[RecordChecksKey] ??= new JsonObject();
    }

    public static void MarkFailed(
        Company company,
        string? failureCode,
        string? failureMessage,
        DateTime? failedAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(company);

        var normalizedFailedAtUtc = NormalizeUtc(failedAtUtc ?? DateTime.UtcNow);
        company.SetFinanceSeedStatus(FinanceSeedingState.Failed, normalizedFailedAtUtc);

        var metadata = GetOrCreateMetadata(company);
        metadata["state"] = JsonValue.Create(FinanceSeedingState.Failed.ToStorageValue());
        metadata["seedFailedAtUtc"] = JsonValue.Create(normalizedFailedAtUtc);
        WriteOptionalString(metadata, "failureCode", failureCode);
        WriteOptionalString(metadata, "failureMessage", failureMessage);
        metadata.Remove("seededAtUtc");
        metadata[RecordChecksKey] ??= new JsonObject();
    }

    public static void MarkSeeded(Company company, string seedVersion, int seedValue, DateTime? seededAtUtc = null)
    {
        ArgumentNullException.ThrowIfNull(company);

        var normalizedSeedVersion = string.IsNullOrWhiteSpace(seedVersion) ? "finance_seed_bootstrap:v1" : seedVersion.Trim();
        var normalizedSeededAtUtc = NormalizeUtc(seededAtUtc ?? DateTime.UtcNow);

        company.SetFinanceSeedStatus(FinanceSeedingState.Seeded, normalizedSeededAtUtc, normalizedSeededAtUtc);

        var metadata = GetOrCreateMetadata(company);
        metadata["state"] = JsonValue.Create(FinanceSeedingState.Seeded.ToStorageValue());
        metadata["seedVersion"] = JsonValue.Create(normalizedSeedVersion);
        metadata["seedValue"] = JsonValue.Create(seedValue);
        metadata["seededAtUtc"] = JsonValue.Create(normalizedSeededAtUtc);
        metadata.Remove("seedFailedAtUtc");
        metadata.Remove("failureCode");
        metadata.Remove("failureMessage");
        metadata[RecordChecksKey] = new JsonObject
        {
            ["accounts"] = JsonValue.Create(true),
            ["counterparties"] = JsonValue.Create(true),
            ["transactions"] = JsonValue.Create(true),
            ["balances"] = JsonValue.Create(true),
            ["policyConfiguration"] = JsonValue.Create(true),
            ["invoices"] = JsonValue.Create(true),
            ["bills"] = JsonValue.Create(true)
        };
    }

    public static void MarkFullySeeded(Company company, string seedVersion, int seedValue, DateTime? seededAtUtc = null) =>
        MarkSeeded(company, seedVersion, seedValue, seededAtUtc);

    private static JsonObject GetOrCreateMetadata(Company company)
    {
        if (company.Settings.Extensions.TryGetValue(ExtensionKey, out var node) && node is JsonObject existing)
        {
            return existing;
        }

        var created = new JsonObject();
        company.Settings.Extensions[ExtensionKey] = created;
        return created;
    }

    public static string? ReadTriggerSource(Company company)
    {
        ArgumentNullException.ThrowIfNull(company);

        if (!company.Settings.Extensions.TryGetValue(ExtensionKey, out var node) || node is not JsonObject metadata)
        {
            return null;
        }

        return ReadString(metadata, "triggerSource");
    }

    private static void WriteOptionalDateTime(JsonObject metadata, string key, DateTime? value)
    {
        if (value.HasValue)
        {
            metadata[key] = JsonValue.Create(NormalizeUtc(value.Value));
        }
        else
        {
            metadata.Remove(key);
        }
    }

    private static void WriteOptionalString(JsonObject metadata, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            metadata.Remove(key);
            return;
        }

        metadata[key] = JsonValue.Create(value.Trim());
    }

    private static void WriteOptionalGuid(JsonObject metadata, string key, Guid? value)
    {
        if (!value.HasValue || value.Value == Guid.Empty)
        {
            metadata.Remove(key);
            return;
        }

        metadata[key] = JsonValue.Create(value.Value);
    }


    private static string? ReadString(JsonObject? values, string key)
    {
        if (values is null || !values.TryGetPropertyValue(key, out var node))
        {
            return null;
        }

        return node?.GetValue<string>()?.Trim();
    }

    private static bool? ReadBool(JsonObject? values, string key)
    {
        if (values is null || !values.TryGetPropertyValue(key, out var node) || node is null)
        {
            return null;
        }

        if (node is JsonValue jsonValue && jsonValue.TryGetValue<bool>(out var value))
        {
            return value;
        }

        return bool.TryParse(node.ToString(), out value) ? value : null;
    }

    private static DateTime? ReadDateTime(JsonObject? values, string key)
    {
        if (values is null || !values.TryGetPropertyValue(key, out var node) || node is null)
        {
            return null;
        }

        if (node is JsonValue jsonValue && jsonValue.TryGetValue<DateTime>(out var value))
        {
            return NormalizeUtc(value);
        }

        return DateTime.TryParse(node.ToString(), out value)
            ? NormalizeUtc(value)
            : null;
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind == DateTimeKind.Utc
            ? value
            : value.ToUniversalTime();
}

internal sealed record FinanceSeedingMetadataSnapshot(
    bool HasExtensionMetadata,
    FinanceSeedingState State,
    bool? Accounts,
    bool? Counterparties,
    bool? Transactions,
    bool? Balances,
    bool? PolicyConfiguration,
    bool? Invoices,
    bool? Bills,
    string? SeedVersion,
    DateTime? SeededAtUtc,
    DateTime? StatusUpdatedAtUtc)
{
    public bool HasCompleteFoundationalChecks =>
        Accounts == true &&
        Counterparties == true &&
        Transactions == true &&
        Balances == true &&
        PolicyConfiguration == true;

    public bool HasAnyPositiveChecks =>
        Accounts == true || Counterparties == true || Transactions == true || Balances == true || PolicyConfiguration == true || Invoices == true || Bills == true;
}