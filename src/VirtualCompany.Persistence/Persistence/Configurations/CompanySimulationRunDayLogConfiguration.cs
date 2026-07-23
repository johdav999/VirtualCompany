using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class CompanySimulationRunDayLogConfiguration : IEntityTypeConfiguration<CompanySimulationRunDayLog>
{
    public void Configure(EntityTypeBuilder<CompanySimulationRunDayLog> builder)
    {
        builder.ToTable("company_simulation_run_day_logs");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.RunHistoryId).HasColumnName("run_history_id").IsRequired();
        builder.Property(x => x.SessionId).HasColumnName("session_id").IsRequired();
        builder.Property(x => x.SimulatedDateUtc).HasColumnName("simulated_date_at").IsRequired();
        builder.Property(x => x.TransactionsGenerated).HasColumnName("transactions_generated").IsRequired();
        builder.Property(x => x.InvoicesGenerated).HasColumnName("invoices_generated").IsRequired();
        builder.Property(x => x.AssetPurchasesGenerated).HasColumnName("asset_purchases_generated").IsRequired();
        builder.Property(x => x.BillsGenerated).HasColumnName("bills_generated").IsRequired();
        builder.Property(x => x.RecurringExpenseInstancesGenerated).HasColumnName("recurring_expense_instances_generated").IsRequired();
        builder.Property(x => x.AlertsGenerated).HasColumnName("alerts_generated").IsRequired();
        builder.Property(x => x.InjectedAnomalies).HasColumnName("injected_anomalies_json").HasJsonConversion<List<string>>().HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonArrayDefault).IsRequired();
        builder.Property(x => x.Warnings).HasColumnName("warnings_json").HasJsonConversion<List<string>>().HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonArrayDefault).IsRequired();
        builder.Property(x => x.Errors).HasColumnName("errors_json").HasJsonConversion<List<string>>().HasDefaultValueSql(CompanyJsonColumnConfiguration.JsonArrayDefault).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.SessionId, x.SimulatedDateUtc }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.CreatedUtc });
    }
}

internal static class ActiveProviderConstraint
{
    // EF model configuration is provider-agnostic here; the expression is valid for SQL Server and SQLite.
    // The migration supplies a PostgreSQL-specific expression at apply time.
    public const string WindowEndAfterStart = "window_end_at > window_start_at";
}

