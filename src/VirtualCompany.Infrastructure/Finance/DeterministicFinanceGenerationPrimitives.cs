using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using VirtualCompany.Application.Finance;

namespace VirtualCompany.Infrastructure.Finance;

internal sealed class Sha256FinanceDeterministicValueSource : IFinanceDeterministicValueSource
{
    public int GetCycleOffset(
        Guid companyId,
        int seed,
        DateTime startSimulatedUtc,
        string? deterministicConfigurationJson,
        string scope,
        int modulo)
    {
        if (modulo <= 1)
        {
            return 0;
        }

        return GetBoundedInt(
            FormattableString.Invariant(
                $"{companyId:N}|{seed}|{NormalizeUtc(startSimulatedUtc):yyyyMMdd}|{Canonicalize(deterministicConfigurationJson)}|cycle|{scope}"),
            modulo);
    }

    public int GetDayValue(
        Guid companyId,
        int seed,
        DateTime startSimulatedUtc,
        DateTime simulatedDateUtc,
        int dayIndex,
        string? deterministicConfigurationJson,
        string scope,
        int modulo)
    {
        if (modulo <= 1)
        {
            return 0;
        }

        return GetBoundedInt(
            FormattableString.Invariant(
                $"{companyId:N}|{seed}|{NormalizeUtc(startSimulatedUtc):yyyyMMdd}|{NormalizeUtc(simulatedDateUtc):yyyyMMdd}|{dayIndex}|{Canonicalize(deterministicConfigurationJson)}|day|{scope}"),
            modulo);
    }

    private static int GetBoundedInt(string value, int modulo)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        var numeric = BitConverter.ToUInt32(hash, 0);
        return (int)(numeric % (uint)modulo);
    }

    private static string Canonicalize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return string.Empty;
        }

        try
        {
            return CanonicalizeNode(JsonNode.Parse(json));
        }
        catch (JsonException)
        {
            return json.Trim();
        }
    }

    private static string CanonicalizeNode(JsonNode? node) =>
        node switch
        {
            null => "null",
            JsonValue value => value.ToJsonString(),
            JsonArray array => $"[{string.Join(",", array.Select(CanonicalizeNode))}]",
            JsonObject obj => $"{{{string.Join(",", obj.OrderBy(x => x.Key, StringComparer.Ordinal).Select(x => $"{JsonValue.Create(x.Key)!.ToJsonString()}:{CanonicalizeNode(x.Value)}"))}}}",
            _ => node.ToJsonString()
        };

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };
}

internal sealed class DefaultFinanceScenarioFactory : IFinanceScenarioFactory
{
    private readonly IFinanceDeterministicValueSource _valueSource;

    public DefaultFinanceScenarioFactory(IFinanceDeterministicValueSource valueSource)
    {
        _valueSource = valueSource;
    }

    public FinanceScenarioSelection Create(
        FinanceDeterministicGenerationContext context,
        int invoiceScenarioCount,
        int thresholdCaseCount,
        int customerCount,
        int supplierCount)
    {
        var invoiceOffset = _valueSource.GetCycleOffset(
            context.CompanyId,
            context.Seed,
            context.StartSimulatedUtc,
            context.DeterministicConfigurationJson,
            "invoice-scenario",
            invoiceScenarioCount);
        var thresholdOffset = _valueSource.GetCycleOffset(
            context.CompanyId,
            context.Seed,
            context.StartSimulatedUtc,
            context.DeterministicConfigurationJson,
            "threshold-case",
            thresholdCaseCount);
        var customerOffset = _valueSource.GetCycleOffset(
            context.CompanyId,
            context.Seed,
            context.StartSimulatedUtc,
            context.DeterministicConfigurationJson,
            "customer-rotation",
            customerCount);
        var supplierOffset = _valueSource.GetCycleOffset(
            context.CompanyId,
            context.Seed,
            context.StartSimulatedUtc,
            context.DeterministicConfigurationJson,
            "supplier-rotation",
            supplierCount);

        // Seed-specific offsets keep full scenario coverage across a deterministic run while changing which rule starts the cycle.
        return new FinanceScenarioSelection(
            Wrap(context.DayIndex + invoiceOffset, invoiceScenarioCount),
            Wrap(context.DayIndex + thresholdOffset, thresholdCaseCount),
            Wrap(context.DayIndex + customerOffset, customerCount),
            Wrap(context.DayIndex + supplierOffset, supplierCount));
    }

    private static int Wrap(int value, int modulo) =>
        modulo <= 0 ? 0 : ((value % modulo) + modulo) % modulo;
}

internal sealed class PeriodicFinanceAnomalyScheduleFactory : IFinanceAnomalyScheduleFactory
{
    private readonly IFinanceDeterministicValueSource _valueSource;

    public PeriodicFinanceAnomalyScheduleFactory(IFinanceDeterministicValueSource valueSource)
    {
        _valueSource = valueSource;
    }

    public FinanceAnomalySchedule Create(
        FinanceDeterministicGenerationContext context,
        int anomalyCount,
        int transactionCount,
        int anomalyCadenceDays,
        int anomalyOffsetDays)
    {
        var cadence = Math.Max(1, anomalyCadenceDays);
        var seedOffset = _valueSource.GetCycleOffset(
            context.CompanyId,
            context.Seed,
            context.StartSimulatedUtc,
            context.DeterministicConfigurationJson,
            "anomaly-cadence-offset",
            cadence);
        var effectiveOffset = (Math.Max(0, anomalyOffsetDays) + seedOffset) % cadence;
        var isAnomalyDay = ((context.DayIndex + effectiveOffset) % cadence) == 0;
        if (!isAnomalyDay || anomalyCount <= 0 || transactionCount <= 0)
        {
            return new FinanceAnomalySchedule(false, null, 0);
        }

        var rotationOffset = _valueSource.GetCycleOffset(
            context.CompanyId,
            context.Seed,
            context.StartSimulatedUtc,
            context.DeterministicConfigurationJson,
            "anomaly-rotation",
            anomalyCount);
        var anomalyOrdinal = (context.DayIndex + effectiveOffset) / cadence;
        var targetTransactionIndex = _valueSource.GetDayValue(
            context.CompanyId,
            context.Seed,
            context.StartSimulatedUtc,
            context.SimulatedDateUtc,
            context.DayIndex,
            context.DeterministicConfigurationJson,
            "anomaly-transaction-target",
            transactionCount);

        return new FinanceAnomalySchedule(true, (anomalyOrdinal + rotationOffset) % anomalyCount, targetTransactionIndex);
    }
}
