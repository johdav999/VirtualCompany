using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class PaymentBatchPersistenceModelTests
{
    [Fact]
    public void Payment_batch_model_has_tenant_idempotency_current_artifact_and_concurrency_indexes()
    {
        using var db = new VirtualCompanyDbContext(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite("Data Source=:memory:").Options);

        var batch = Entity<PaymentBatch>(db);
        AssertIndex(batch, true, nameof(PaymentBatch.CompanyId), nameof(PaymentBatch.Reference));
        AssertIndex(batch, true, nameof(PaymentBatch.CompanyId), nameof(PaymentBatch.CreateIdempotencyKey));
        AssertIndex(batch, false, nameof(PaymentBatch.CompanyId), nameof(PaymentBatch.Status), nameof(PaymentBatch.UpdatedUtc));
        Assert.True(batch.FindProperty(nameof(PaymentBatch.Version))!.IsConcurrencyToken);
        Assert.True(batch.FindProperty(nameof(PaymentBatch.RowVersion))!.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.Never, batch.FindProperty(nameof(PaymentBatch.RowVersion))!.ValueGenerated);

        var operation = Entity<PaymentBatchOperation>(db);
        AssertIndex(operation, true, nameof(PaymentBatchOperation.CompanyId),
            nameof(PaymentBatchOperation.OperationType), nameof(PaymentBatchOperation.IdempotencyKey));

        var profile = Entity<PaymentBeneficiaryProfile>(db);
        AssertIndex(profile, true, nameof(PaymentBeneficiaryProfile.CompanyId),
            nameof(PaymentBeneficiaryProfile.PartyType), nameof(PaymentBeneficiaryProfile.PartyId),
            nameof(PaymentBeneficiaryProfile.Version));
        Assert.Contains(profile.GetIndexes(), x => x.IsUnique &&
            PropertyNames(x).SequenceEqual(new[]
            {
                nameof(PaymentBeneficiaryProfile.CompanyId), nameof(PaymentBeneficiaryProfile.PartyType),
                nameof(PaymentBeneficiaryProfile.PartyId), nameof(PaymentBeneficiaryProfile.IsCurrent)
            }) && !string.IsNullOrWhiteSpace(x.GetFilter()));

        var instruction = Entity<PaymentInstruction>(db);
        Assert.Contains(instruction.GetIndexes(), x => x.IsUnique &&
            PropertyNames(x).SequenceEqual(new[]
            {
                nameof(PaymentInstruction.CompanyId), nameof(PaymentInstruction.BatchId),
                nameof(PaymentInstruction.ObligationLinkId), nameof(PaymentInstruction.IsCurrent)
            }) && !string.IsNullOrWhiteSpace(x.GetFilter()));
    }

    private static IEntityType Entity<T>(VirtualCompanyDbContext db) =>
        db.Model.FindEntityType(typeof(T)) ?? throw new InvalidOperationException($"{typeof(T).Name} is missing from the persistence model.");

    private static void AssertIndex(IEntityType entity, bool unique, params string[] properties) =>
        Assert.Contains(entity.GetIndexes(), index => index.IsUnique == unique &&
            PropertyNames(index).SequenceEqual(properties));

    private static IEnumerable<string> PropertyNames(IReadOnlyIndex index) =>
        index.Properties.Select(property => property.Name);
}
