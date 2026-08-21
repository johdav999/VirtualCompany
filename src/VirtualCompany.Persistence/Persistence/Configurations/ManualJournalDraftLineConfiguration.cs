using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class ManualJournalDraftLineConfiguration : IEntityTypeConfiguration<ManualJournalDraftLine>
{
    public void Configure(EntityTypeBuilder<ManualJournalDraftLine> builder)
    {
        builder.ToTable("manual_journal_draft_lines");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.DraftId).HasColumnName("draft_id").IsRequired();
        builder.Property(x => x.FinanceAccountId).HasColumnName("finance_account_id").IsRequired();
        builder.Property(x => x.LineNumber).HasColumnName("line_number").IsRequired();
        builder.Property(x => x.DebitAmount).HasColumnName("debit_amount").HasPrecision(19, 6).IsRequired();
        builder.Property(x => x.CreditAmount).HasColumnName("credit_amount").HasPrecision(19, 6).IsRequired();
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(x => x.CostCenterId).HasColumnName("cost_center_id");
        builder.Property(x => x.TaxFactsJson).HasColumnName("tax_facts_json").HasMaxLength(8000);
        builder.Property(x => x.DimensionFactsJson).HasColumnName("dimension_facts_json").HasMaxLength(8000);
        builder.HasIndex(x => new { x.CompanyId, x.DraftId, x.LineNumber }).IsUnique();
        builder.HasOne(x => x.Draft).WithMany(x => x.Lines)
            .HasForeignKey(x => new { x.CompanyId, x.DraftId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.FinanceAccount).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.FinanceAccountId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
