using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence;
internal sealed class FinanceAccountConfiguration : IEntityTypeConfiguration<FinanceAccount>
{
    public void Configure(EntityTypeBuilder<FinanceAccount> builder)
    {
        builder.ToTable("finance_accounts");

        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired();
        builder.Property(x => x.AccountType).HasColumnName("account_type").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.OpeningBalance).HasColumnName("opening_balance").HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(x => x.OpenedUtc).HasColumnName("opened_at").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.AccountClass).HasColumnName("account_class").HasMaxLength(32);
        builder.Property(x => x.NormalBalance).HasColumnName("normal_balance").HasMaxLength(16);
        builder.Property(x => x.EffectiveFrom).HasColumnName("effective_from").HasColumnType("date");
        builder.Property(x => x.EffectiveTo).HasColumnName("effective_to").HasColumnType("date");
        builder.Property(x => x.IsPostingEnabled).HasColumnName("is_posting_enabled").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.ControlAccountRole).HasColumnName("control_account_role").HasMaxLength(96);
        builder.Property(x => x.RestrictManualPosting).HasColumnName("restrict_manual_posting").HasDefaultValue(false).IsRequired();
        builder.Property(x => x.IsReportable).HasColumnName("is_reportable").HasDefaultValue(true).IsRequired();
        builder.Property(x => x.PostingRestriction).HasColumnName("posting_restriction").HasMaxLength(16).HasDefaultValue(FinanceAccountPostingRestrictionValues.None).IsRequired();
        builder.Property(x => x.ReplacementAccountId).HasColumnName("replacement_account_id");
        builder.Property(x => x.LifecycleReason).HasColumnName("lifecycle_reason").HasMaxLength(512);
        builder.Property(x => x.LifecycleVersion).HasColumnName("lifecycle_version").HasDefaultValue(1L).IsRequired();

        builder.HasIndex(x => new { x.CompanyId, x.Code }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.AccountType });
        builder.HasIndex(x => new { x.CompanyId, x.AccountClass, x.IsPostingEnabled });
        builder.HasIndex(x => new { x.CompanyId, x.ReplacementAccountId });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(x => x.Transactions).WithOne(x => x.Account).HasForeignKey(x => new { x.CompanyId, x.AccountId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.ReplacementAccount).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.ReplacementAccountId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

