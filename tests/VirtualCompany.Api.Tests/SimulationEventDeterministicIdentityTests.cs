using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Finance;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class SimulationEventDeterministicIdentityTests
{
    [Fact]
    public void Same_inputs_produce_the_same_event_id_and_key()
    {
        var companyId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var startUtc = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var simulationDateUtc = new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc);
        var sourceEntityId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var leftId = SimulationEventDeterministicIdentity.CreateEventId(companyId, 73, startUtc, simulationDateUtc, "finance.invoice.generated", "finance_invoice", sourceEntityId, 1, """{"b":2,"a":1}""");
        var rightId = SimulationEventDeterministicIdentity.CreateEventId(companyId, 73, startUtc, simulationDateUtc, "finance.invoice.generated", "finance_invoice", sourceEntityId, 1, """{"a":1,"b":2}""");
        var leftKey = SimulationEventDeterministicIdentity.CreateDeterministicKey(companyId, 73, startUtc, simulationDateUtc, "finance.invoice.generated", "finance_invoice", sourceEntityId, 1, """{"b":2,"a":1}""");
        var rightKey = SimulationEventDeterministicIdentity.CreateDeterministicKey(companyId, 73, startUtc, simulationDateUtc, "finance.invoice.generated", "finance_invoice", sourceEntityId, 1, """{"a":1,"b":2}""");

        Assert.Equal(leftId, rightId);
        Assert.Equal(leftKey, rightKey);
    }

    [Fact]
    public void Changing_seed_or_sequence_changes_the_event_identity()
    {
        var companyId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
        var startUtc = new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
        var simulationDateUtc = new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc);
        var sourceEntityId = Guid.Parse("11111111-2222-3333-4444-555555555555");

        var baseline = SimulationEventDeterministicIdentity.CreateEventId(companyId, 73, startUtc, simulationDateUtc, "finance.invoice.generated", "finance_invoice", sourceEntityId, 1);
        var changedSeed = SimulationEventDeterministicIdentity.CreateEventId(companyId, 74, startUtc, simulationDateUtc, "finance.invoice.generated", "finance_invoice", sourceEntityId, 1);
        var changedSequence = SimulationEventDeterministicIdentity.CreateEventId(companyId, 73, startUtc, simulationDateUtc, "finance.invoice.generated", "finance_invoice", sourceEntityId, 2);

        Assert.NotEqual(baseline, changedSeed);
        Assert.NotEqual(baseline, changedSequence);
    }

    [Fact]
    public void Deterministic_scoped_guid_generation_is_stable_for_replay_artifacts()
    {
        var companyId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

        var left = FinanceSimulationDeterministicIdentity.CreateCashDeltaRecordId(
            companyId,
            Guid.Parse("11111111-2222-3333-4444-555555555555"));
        var right = FinanceSimulationDeterministicIdentity.CreateCashDeltaRecordId(
            companyId,
            Guid.Parse("11111111-2222-3333-4444-555555555555"));

        Assert.Equal(left, right);
    }

    [Fact]
    public void Cash_snapshot_requires_before_delta_and_after_to_match()
    {
        Assert.Throws<ArgumentException>(() => new SimulationEventRecord(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 73, new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc), new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc), "finance.cash_movement.generated", "finance_payment", Guid.NewGuid(), "PAY-001", null, 1, "key", 100m, 10m, 109m));
    }
}