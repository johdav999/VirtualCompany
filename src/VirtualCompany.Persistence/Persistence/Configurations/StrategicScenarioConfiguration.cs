using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class StrategicScenarioConfiguration : IEntityTypeConfiguration<StrategicScenarioVersion>
{
    public void Configure(EntityTypeBuilder<StrategicScenarioVersion> b)
    {
        b.ToTable("strategic_scenario_versions");b.HasKey(x=>x.Id);b.HasAlternateKey(x=>new{x.CompanyId,x.Id});
        b.HasIndex(x=>new{x.CompanyId,x.SeriesId,x.Revision}).IsUnique();b.HasIndex(x=>new{x.CompanyId,x.RequestId}).IsUnique();
        b.HasIndex(x=>new{x.CompanyId,x.PreviousId}).IsUnique().HasFilter("[PreviousId] IS NOT NULL");
        b.Property(x=>x.Name).HasMaxLength(64);b.Property(x=>x.OwnerName).HasMaxLength(200);b.Property(x=>x.Notes).HasMaxLength(2000);
        b.Property(x=>x.Currency).HasMaxLength(3);b.Property(x=>x.CalculationVersion).HasMaxLength(64);
        b.Property(x=>x.Checksum).HasMaxLength(64);b.Property(x=>x.CommandHash).HasMaxLength(64);
        b.Property(x=>x.SourceRevenue).HasPrecision(19,2);b.Property(x=>x.SourceExpense).HasPrecision(19,2);
        b.Property(x=>x.SavedUtc).HasConversion(v=>v,v=>DateTime.SpecifyKind(v,DateTimeKind.Utc));
        b.OwnsOne(x=>x.Drivers,d=>{d.Property(x=>x.CapacityUnit).HasMaxLength(32);foreach(var p in d.OwnedEntityType.GetProperties().Where(p=>p.ClrType==typeof(decimal)))p.SetPrecision(25);foreach(var p in d.OwnedEntityType.GetProperties().Where(p=>p.ClrType==typeof(decimal)))p.SetScale(10);});
        b.HasOne<Company>().WithMany().HasForeignKey(x=>x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<AnnualPlanVersion>().WithMany().HasForeignKey(x=>new{x.CompanyId,Id=x.AnnualPlanId}).HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<FinanceForecastRevision>().WithMany().HasForeignKey(x=>new{x.CompanyId,Id=x.ForecastRevisionId}).HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<StrategicScenarioVersion>().WithMany().HasForeignKey(x=>new{x.CompanyId,Id=x.PreviousId}).HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.NoAction);
        b.HasOne<StrategicScenarioVersion>().WithMany().HasForeignKey(x=>new{x.CompanyId,Id=x.DerivedFromId}).HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.NoAction);
        b.HasMany(x=>x.Cash).WithOne().HasForeignKey(x=>new{x.CompanyId,x.ScenarioId}).HasPrincipalKey(x=>new{x.CompanyId,x.Id});
        b.HasMany(x=>x.Outputs).WithOne().HasForeignKey(x=>new{x.CompanyId,x.ScenarioId}).HasPrincipalKey(x=>new{x.CompanyId,x.Id});
        b.HasMany(x=>x.Checkpoints).WithOne().HasForeignKey(x=>new{x.CompanyId,x.ScenarioId}).HasPrincipalKey(x=>new{x.CompanyId,x.Id});
    }
}
internal sealed class StrategicScenarioCashConfiguration : IEntityTypeConfiguration<StrategicScenarioCash>
{
    public void Configure(EntityTypeBuilder<StrategicScenarioCash>b){b.ToTable("strategic_scenario_cash");b.HasKey(x=>x.Id);b.HasIndex(x=>new{x.ScenarioId,x.Year}).IsUnique();b.Property(x=>x.Investment).HasPrecision(19,2);b.Property(x=>x.Funding).HasPrecision(19,2);b.Property(x=>x.Rationale).HasMaxLength(1000);}
}
internal sealed class StrategicScenarioOutputConfiguration : IEntityTypeConfiguration<StrategicScenarioOutput>
{
    public void Configure(EntityTypeBuilder<StrategicScenarioOutput>b){b.ToTable("strategic_scenario_outputs");b.HasKey(x=>x.Id);b.OwnsOne(x=>x.Result,r=>{r.HasIndex(x=>new{x.Year});foreach(var p in r.OwnedEntityType.GetProperties().Where(p=>p.ClrType==typeof(decimal))){p.SetPrecision(19);p.SetScale(p.Name is "Demand" or "Capacity" or "Fulfilled" or "CapacityShortfall"?4:2);}});}
}
internal sealed class StrategicScenarioCheckpointConfiguration : IEntityTypeConfiguration<StrategicScenarioCheckpoint>
{
    public void Configure(EntityTypeBuilder<StrategicScenarioCheckpoint>b){b.ToTable("strategic_scenario_checkpoints");b.HasKey(x=>x.Id);b.Property(x=>x.Title).HasMaxLength(200);b.Property(x=>x.OwnerName).HasMaxLength(200);b.HasOne<OperatingInitiative>().WithMany().HasForeignKey(x=>new{x.CompanyId,Id=x.InitiativeId}).HasPrincipalKey(x=>new{x.CompanyId,x.Id}).OnDelete(DeleteBehavior.NoAction);}
}
