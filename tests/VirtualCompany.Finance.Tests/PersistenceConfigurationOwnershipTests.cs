using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Finance.Tests;

public sealed class PersistenceConfigurationOwnershipTests
{
    [Fact]
    public void Every_entity_has_at_most_one_discovered_configuration_owner()
    {
        var duplicates = typeof(VirtualCompanyDbContext).Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && !type.ContainsGenericParameters)
            .SelectMany(type => type.GetInterfaces()
                .Where(contract => contract.IsGenericType &&
                    contract.GetGenericTypeDefinition() == typeof(IEntityTypeConfiguration<>))
                .Select(contract => new { Entity = contract.GenericTypeArguments[0], Owner = type }))
            .GroupBy(mapping => mapping.Entity)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key.Name}: {string.Join(", ", group.Select(mapping => mapping.Owner.Name).Order())}")
            .Order()
            .ToArray();

        Assert.True(duplicates.Length == 0, string.Join(Environment.NewLine, duplicates));
    }

    [Theory]
    [InlineData(typeof(FinanceAccount))]
    [InlineData(typeof(FinanceCounterparty))]
    [InlineData(typeof(FinanceInvoice))]
    [InlineData(typeof(FinanceBill))]
    [InlineData(typeof(FinanceTransaction))]
    [InlineData(typeof(FinanceBalance))]
    [InlineData(typeof(Payment))]
    [InlineData(typeof(CompanyBankAccount))]
    [InlineData(typeof(BankTransaction))]
    [InlineData(typeof(FinanceAsset))]
    public void Finance_source_provenance_keeps_columns_defaults_filtered_index_and_restrict_delete(Type entityType)
    {
        using var db = CreateContext();
        var entity = db.GetService<IDesignTimeModel>().Model.FindEntityType(entityType)!;
        var source = entity.FindProperty("SourceType")!;
        Assert.Equal("source_type", source.GetColumnName());
        Assert.Equal(32, source.GetMaxLength());
        Assert.Equal(FinanceRecordSourceTypes.Manual, source.GetDefaultValue());
        Assert.False(source.IsNullable);
        Assert.Equal("provider_key", entity.FindProperty("ProviderKey")!.GetColumnName());
        Assert.Equal(64, entity.FindProperty("ProviderKey")!.GetMaxLength());
        Assert.Equal("provider_external_id", entity.FindProperty("ProviderExternalId")!.GetColumnName());
        Assert.Equal(256, entity.FindProperty("ProviderExternalId")!.GetMaxLength());
        Assert.Equal("finance_external_reference_id", entity.FindProperty("FinanceExternalReferenceId")!.GetColumnName());
        var index = Assert.Single(entity.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual(new[] { "CompanyId", "ProviderKey", "ProviderExternalId" }));
        Assert.Equal("provider_key IS NOT NULL AND provider_external_id IS NOT NULL", index.GetFilter());
        var foreignKey = Assert.Single(entity.GetForeignKeys(), key =>
            key.Properties.Select(property => property.Name).SequenceEqual(new[] { "FinanceExternalReferenceId" }));
        Assert.Equal(typeof(FinanceExternalReference), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.NotNull(entity.GetQueryFilter());
        var constraintName = entityType == typeof(Payment) ? "CK_Payments_source_type" :
            entityType == typeof(FinanceTransaction) ? "CK_FinanceTransactions_source_type" :
            $"CK_{entity.GetTableName()}_source_type";
        var constraint = Assert.Single(entity.GetCheckConstraints(), check => check.Name == constraintName);
        Assert.Equal(FinanceRecordSourceTypes.BuildCheckConstraintSql("source_type"), constraint.Sql);
    }

    [Theory]
    [InlineData(typeof(Payment), "SourceSimulationEventRecordId", typeof(SimulationEventRecord))]
    [InlineData(typeof(FinanceInvoice), "SourceSimulationEventRecordId", typeof(SimulationEventRecord))]
    [InlineData(typeof(FinanceBill), "SourceSimulationEventRecordId", typeof(SimulationEventRecord))]
    [InlineData(typeof(FinanceTransaction), "SourceSimulationEventRecordId", typeof(SimulationEventRecord))]
    [InlineData(typeof(FinanceBalance), "SourceSimulationEventRecordId", typeof(SimulationEventRecord))]
    [InlineData(typeof(FinanceAsset), "SourceSimulationEventRecordId", typeof(SimulationEventRecord))]
    [InlineData(typeof(BankTransaction), "SourceSimulationEventRecordId", typeof(SimulationEventRecord))]
    [InlineData(typeof(PaymentAllocation), "SourceSimulationEventRecordId", typeof(SimulationEventRecord))]
    [InlineData(typeof(PaymentAllocation), "PaymentSourceSimulationEventRecordId", typeof(SimulationEventRecord))]
    [InlineData(typeof(PaymentAllocation), "TargetSourceSimulationEventRecordId", typeof(SimulationEventRecord))]
    [InlineData(typeof(FinanceInvoice), "DocumentId", typeof(CompanyKnowledgeDocument))]
    [InlineData(typeof(FinanceBill), "DocumentId", typeof(CompanyKnowledgeDocument))]
    [InlineData(typeof(FinanceTransaction), "DocumentId", typeof(CompanyKnowledgeDocument))]
    [InlineData(typeof(FinanceBalance), "AccountId", typeof(FinanceAccount))]
    public void Consolidated_links_keep_company_scoped_keys_indexes_and_restrict_delete(
        Type entityType, string linkedId, Type principalType)
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(entityType)!;
        var properties = new[] { "CompanyId", linkedId };
        var foreignKey = Assert.Single(entity.GetForeignKeys(), key =>
            key.Properties.Select(property => property.Name).SequenceEqual(properties));
        Assert.Equal(principalType, foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(new[] { "CompanyId", "Id" }, foreignKey.PrincipalKey.Properties.Select(property => property.Name));
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.Contains(entity.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).Take(2).SequenceEqual(properties));
    }

    private static VirtualCompanyDbContext CreateContext() => new(
        new DbContextOptionsBuilder<VirtualCompanyDbContext>().UseSqlite("Data Source=:memory:").Options);
}
