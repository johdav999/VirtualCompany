using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class BankStatementImportConfiguration : IEntityTypeConfiguration<BankStatementImport>
{
    public void Configure(EntityTypeBuilder<BankStatementImport> builder)
    {
        builder.ToTable("bank_statement_imports");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.BankAccountId).HasColumnName("bank_account_id").IsRequired();
        builder.Property(x => x.SourceKey).HasColumnName("source_key").HasMaxLength(64).IsRequired();
        builder.Property(x => x.StatementIdentity).HasColumnName("statement_identity").HasMaxLength(128).IsRequired();
        builder.Property(x => x.ContentHash).HasColumnName("content_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ImportedByUserId).HasColumnName("imported_by_user_id").IsRequired();
        builder.Property(x => x.ImportedUtc).HasColumnName("imported_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.BankAccountId, x.SourceKey, x.StatementIdentity }).IsUnique();
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.BankAccount).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.BankAccountId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BankStatementImportRowConfiguration : IEntityTypeConfiguration<BankStatementImportRow>
{
    public void Configure(EntityTypeBuilder<BankStatementImportRow> builder)
    {
        builder.ToTable("bank_statement_import_rows");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.BankStatementImportId).HasColumnName("bank_statement_import_id").IsRequired();
        builder.Property(x => x.BankTransactionId).HasColumnName("bank_transaction_id").IsRequired();
        builder.Property(x => x.RowIdentity).HasColumnName("row_identity").HasMaxLength(128).IsRequired();
        builder.Property(x => x.RowContentHash).HasColumnName("row_content_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.BankStatementImportId, x.RowIdentity }).IsUnique();
        // The row already cascades from Company through BankStatementImport. A direct
        // cascade from Company to each row creates a second SQL Server cascade path.
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.BankStatementImport).WithMany(x => x.Rows)
            .HasForeignKey(x => new { x.CompanyId, x.BankStatementImportId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.BankTransaction).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.BankTransactionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Restrict);
    }
}
