using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class FixedAssetClassConfiguration : IEntityTypeConfiguration<FixedAssetClass>
{
    public void Configure(EntityTypeBuilder<FixedAssetClass> b)
    {
        b.ToTable("fixed_asset_classes"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.Code).HasColumnName("code").HasMaxLength(64); b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200);
        b.Property(x => x.BookMethod).HasColumnName("book_method").HasMaxLength(32); b.Property(x => x.UsefulLifeMonths).HasColumnName("useful_life_months");
        b.Property(x => x.DefaultResidualPercent).HasColumnName("default_residual_percent").HasPrecision(9, 4);
        b.Property(x => x.CostAccountId).HasColumnName("cost_account_id"); b.Property(x => x.AccumulatedDepreciationAccountId).HasColumnName("accumulated_depreciation_account_id");
        b.Property(x => x.DepreciationExpenseAccountId).HasColumnName("depreciation_expense_account_id"); b.Property(x => x.AccumulatedImpairmentAccountId).HasColumnName("accumulated_impairment_account_id");
        b.Property(x => x.ImpairmentExpenseAccountId).HasColumnName("impairment_expense_account_id"); b.Property(x => x.DisposalGainAccountId).HasColumnName("disposal_gain_account_id");
        b.Property(x => x.DisposalLossAccountId).HasColumnName("disposal_loss_account_id"); b.Property(x => x.VoucherSeriesCode).HasColumnName("voucher_series_code").HasMaxLength(32);
        b.Property(x => x.RequiresApproval).HasColumnName("requires_approval"); b.Property(x => x.IsActive).HasColumnName("is_active");
        b.Property(x => x.DefinitionHash).HasColumnName("definition_hash").HasMaxLength(64).IsFixedLength(); b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id"); b.Property(x => x.CreatedUtc).HasColumnName("created_utc"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_utc");
        b.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.IsActive });
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<FinanceAccount>().WithMany().HasForeignKey(x => new { x.CompanyId, x.CostAccountId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FinanceAccount>().WithMany().HasForeignKey(x => new { x.CompanyId, x.AccumulatedDepreciationAccountId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FinanceAccount>().WithMany().HasForeignKey(x => new { x.CompanyId, x.DepreciationExpenseAccountId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FinanceAccount>().WithMany().HasForeignKey(x => new { x.CompanyId, x.AccumulatedImpairmentAccountId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FinanceAccount>().WithMany().HasForeignKey(x => new { x.CompanyId, x.ImpairmentExpenseAccountId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FinanceAccount>().WithMany().HasForeignKey(x => new { x.CompanyId, x.DisposalGainAccountId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FinanceAccount>().WithMany().HasForeignKey(x => new { x.CompanyId, x.DisposalLossAccountId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

public sealed class FixedAssetRegisterItemConfiguration : IEntityTypeConfiguration<FixedAssetRegisterItem>
{
    public void Configure(EntityTypeBuilder<FixedAssetRegisterItem> b)
    {
        b.ToTable("fixed_asset_register_items"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.AssetClassId).HasColumnName("asset_class_id");
        b.Property(x => x.AssetClassVersion).HasColumnName("asset_class_version"); b.Property(x => x.AssetClassHash).HasColumnName("asset_class_hash").HasMaxLength(64).IsFixedLength();
        b.Property(x => x.AssetNumber).HasColumnName("asset_number").HasMaxLength(64); b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200); b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
        Money(b, x => x.AcquisitionCost, "acquisition_cost"); Money(b, x => x.ImprovementCost, "improvement_cost"); Money(b, x => x.ResidualValue, "residual_value");
        Money(b, x => x.AccumulatedDepreciation, "accumulated_depreciation"); Money(b, x => x.AccumulatedImpairment, "accumulated_impairment");
        Money(b, x => x.DisposalProceeds, "disposal_proceeds"); Money(b, x => x.DisposalGainLoss, "disposal_gain_loss");
        b.Ignore(x => x.GrossBookValue); b.Ignore(x => x.NetBookValue); b.Property(x => x.UsefulLifeMonths).HasColumnName("useful_life_months"); b.Property(x => x.BookMethod).HasColumnName("book_method").HasMaxLength(32);
        b.Property(x => x.AcquisitionDate).HasColumnName("acquisition_date").HasColumnType("date"); b.Property(x => x.CapitalizationDate).HasColumnName("capitalization_date").HasColumnType("date");
        b.Property(x => x.PlacedInServiceDate).HasColumnName("placed_in_service_date").HasColumnType("date"); b.Property(x => x.LastDepreciationThrough).HasColumnName("last_depreciation_through").HasColumnType("date"); b.Property(x => x.DisposalDate).HasColumnName("disposal_date").HasColumnType("date");
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32); b.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(64); b.Property(x => x.SourceId).HasColumnName("source_id").HasMaxLength(200); b.Property(x => x.SourceVersion).HasColumnName("source_version").HasMaxLength(100);
        b.Property(x => x.SourceDocumentId).HasColumnName("source_document_id"); b.Property(x => x.LegacyFinanceAssetId).HasColumnName("legacy_finance_asset_id"); b.Property(x => x.Custodian).HasColumnName("custodian").HasMaxLength(160); b.Property(x => x.Location).HasColumnName("location").HasMaxLength(160);
        b.Property(x => x.DimensionSnapshotJson).HasColumnName("dimension_snapshot_json").HasMaxLength(8000); b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id"); b.Property(x => x.CreatedUtc).HasColumnName("created_utc"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_utc"); b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        b.HasIndex(x => new { x.CompanyId, x.AssetNumber }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.SourceType, x.SourceId, x.SourceVersion }).IsUnique();
        b.HasIndex(x => new { x.CompanyId, x.Status, x.AssetClassId }); b.HasIndex(x => new { x.CompanyId, x.LegacyFinanceAssetId }).IsUnique().HasFilter("legacy_finance_asset_id IS NOT NULL");
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.AssetClass).WithMany(x => x.Assets).HasForeignKey(x => new { x.CompanyId, x.AssetClassId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<CompanyKnowledgeDocument>().WithMany().HasForeignKey(x => new { x.CompanyId, x.SourceDocumentId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<FinanceAsset>().WithMany().HasForeignKey(x => new { x.CompanyId, x.LegacyFinanceAssetId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
    private static void Money(EntityTypeBuilder<FixedAssetRegisterItem> b, System.Linq.Expressions.Expression<Func<FixedAssetRegisterItem, decimal>> p, string name) => b.Property(p).HasColumnName(name).HasPrecision(19, 4);
}

public sealed class FixedAssetComponentConfiguration : IEntityTypeConfiguration<FixedAssetComponent>
{
    public void Configure(EntityTypeBuilder<FixedAssetComponent> b) { b.ToTable("fixed_asset_components"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id }); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.AssetId).HasColumnName("asset_id"); b.Property(x => x.Code).HasColumnName("code").HasMaxLength(64); b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200); b.Property(x => x.Cost).HasColumnName("cost").HasPrecision(19, 4); b.Property(x => x.ResidualValue).HasColumnName("residual_value").HasPrecision(19, 4); b.Property(x => x.AccumulatedDepreciation).HasColumnName("accumulated_depreciation").HasPrecision(19, 4); b.Property(x => x.UsefulLifeMonths).HasColumnName("useful_life_months"); b.Property(x => x.PlacedInServiceDate).HasColumnName("placed_in_service_date").HasColumnType("date"); b.HasIndex(x => new { x.CompanyId, x.AssetId, x.Code }).IsUnique(); b.HasOne(x => x.Asset).WithMany(x => x.Components).HasForeignKey(x => new { x.CompanyId, x.AssetId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade); }
}

public sealed class FixedAssetBookEventConfiguration : IEntityTypeConfiguration<FixedAssetBookEvent>
{
    public void Configure(EntityTypeBuilder<FixedAssetBookEvent> b) { b.ToTable("fixed_asset_book_events"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id }); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.AssetId).HasColumnName("asset_id"); b.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(32); b.Property(x => x.EffectiveDate).HasColumnName("effective_date").HasColumnType("date"); b.Property(x => x.Amount).HasColumnName("amount").HasPrecision(19,4); b.Property(x => x.CostMovement).HasColumnName("cost_movement").HasPrecision(19,4); b.Property(x => x.DepreciationMovement).HasColumnName("depreciation_movement").HasPrecision(19,4); b.Property(x => x.ImpairmentMovement).HasColumnName("impairment_movement").HasPrecision(19,4); b.Property(x => x.Proceeds).HasColumnName("proceeds").HasPrecision(19,4); b.Property(x => x.GainLoss).HasColumnName("gain_loss").HasPrecision(19,4); b.Property(x => x.SourceType).HasColumnName("source_type").HasMaxLength(64); b.Property(x => x.SourceId).HasColumnName("source_id").HasMaxLength(200); b.Property(x => x.SourceVersion).HasColumnName("source_version").HasMaxLength(100); b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200); b.Property(x => x.SnapshotHash).HasColumnName("snapshot_hash").HasMaxLength(64).IsFixedLength(); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(24); b.Property(x => x.FiscalPeriodId).HasColumnName("fiscal_period_id"); b.Property(x => x.LedgerEntryId).HasColumnName("ledger_entry_id"); b.Property(x => x.DepreciationRunId).HasColumnName("depreciation_run_id"); b.Property(x => x.OriginalEventId).HasColumnName("original_event_id"); b.Property(x => x.ComponentAllocationJson).HasColumnName("component_allocation_json").HasMaxLength(8000); b.Property(x => x.ActorUserId).HasColumnName("actor_user_id"); b.Property(x => x.CreatedUtc).HasColumnName("created_utc"); b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.AssetId, x.EffectiveDate }); b.HasIndex(x => new { x.CompanyId, x.LedgerEntryId }).IsUnique().HasFilter("ledger_entry_id IS NOT NULL"); b.HasOne(x => x.Asset).WithMany(x => x.Events).HasForeignKey(x => new { x.CompanyId, x.AssetId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne<FixedAssetBookEvent>().WithMany().HasForeignKey(x => new { x.CompanyId, x.OriginalEventId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict); }
}

public sealed class FixedAssetMigrationConflictConfiguration : IEntityTypeConfiguration<FixedAssetMigrationConflict>
{
    public void Configure(EntityTypeBuilder<FixedAssetMigrationConflict> b) { b.ToTable("fixed_asset_migration_conflicts"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.LegacyFinanceAssetId).HasColumnName("legacy_finance_asset_id"); b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(100); b.Property(x => x.Explanation).HasColumnName("explanation").HasMaxLength(1000); b.Property(x => x.LegacySnapshotJson).HasColumnName("legacy_snapshot_json").HasMaxLength(8000); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(24); b.Property(x => x.CreatedUtc).HasColumnName("created_utc"); b.Property(x => x.ResolvedUtc).HasColumnName("resolved_utc"); b.HasIndex(x => new { x.CompanyId, x.LegacyFinanceAssetId }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.Status }); b.HasOne(x => x.LegacyFinanceAsset).WithMany().HasForeignKey(x => new { x.CompanyId, x.LegacyFinanceAssetId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade); }
}

public sealed class FixedAssetDepreciationRunConfiguration : IEntityTypeConfiguration<FixedAssetDepreciationRun>
{
    public void Configure(EntityTypeBuilder<FixedAssetDepreciationRun> b) { b.ToTable("fixed_asset_depreciation_runs"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id }); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.FiscalPeriodId).HasColumnName("fiscal_period_id"); b.Property(x => x.PeriodStart).HasColumnName("period_start").HasColumnType("date"); b.Property(x => x.PeriodEnd).HasColumnName("period_end").HasColumnType("date"); b.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(200); b.Property(x => x.PopulationHash).HasColumnName("population_hash").HasMaxLength(64).IsFixedLength(); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32); b.Property(x => x.TotalAmount).HasColumnName("total_amount").HasPrecision(19,4); b.Property(x => x.PostedItemCount).HasColumnName("posted_item_count"); b.Property(x => x.ExceptionCount).HasColumnName("exception_count"); b.Property(x => x.ActorUserId).HasColumnName("actor_user_id"); b.Property(x => x.CreatedUtc).HasColumnName("created_utc"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_utc"); b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken(); b.HasIndex(x => new { x.CompanyId, x.IdempotencyKey }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.PeriodStart, x.PeriodEnd }); b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade); }
}

public sealed class FixedAssetDepreciationRunItemConfiguration : IEntityTypeConfiguration<FixedAssetDepreciationRunItem>
{
    public void Configure(EntityTypeBuilder<FixedAssetDepreciationRunItem> b) { b.ToTable("fixed_asset_depreciation_run_items"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.RunId).HasColumnName("run_id"); b.Property(x => x.AssetId).HasColumnName("asset_id"); b.Property(x => x.AssetVersion).HasColumnName("asset_version"); b.Property(x => x.AssetClassHash).HasColumnName("asset_class_hash").HasMaxLength(64).IsFixedLength(); b.Property(x => x.OpeningCost).HasColumnName("opening_cost").HasPrecision(19,4); b.Property(x => x.OpeningAccumulatedDepreciation).HasColumnName("opening_accumulated_depreciation").HasPrecision(19,4); b.Property(x => x.OpeningAccumulatedImpairment).HasColumnName("opening_accumulated_impairment").HasPrecision(19,4); b.Property(x => x.ResidualValue).HasColumnName("residual_value").HasPrecision(19,4); b.Property(x => x.Amount).HasColumnName("amount").HasPrecision(19,4); b.Property(x => x.CalculationExplanation).HasColumnName("calculation_explanation").HasMaxLength(1000); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(24); b.Property(x => x.LedgerEntryId).HasColumnName("ledger_entry_id"); b.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(100); b.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000); b.HasIndex(x => new { x.CompanyId, x.RunId, x.AssetId }).IsUnique(); b.HasOne(x => x.Run).WithMany(x => x.Items).HasForeignKey(x => new { x.CompanyId, x.RunId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade); b.HasOne(x => x.Asset).WithMany().HasForeignKey(x => new { x.CompanyId, x.AssetId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict); }
}
