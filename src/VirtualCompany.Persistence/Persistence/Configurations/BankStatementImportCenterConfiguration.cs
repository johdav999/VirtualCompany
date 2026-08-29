using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class BankStatementImportJobConfiguration : IEntityTypeConfiguration<BankStatementImportJob>
{
    public void Configure(EntityTypeBuilder<BankStatementImportJob> builder)
    {
        builder.ToTable("bank_statement_import_jobs"); builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.BankAccountId).HasColumnName("bank_account_id").IsRequired();
        builder.Property(x => x.CsvMappingProfileId).HasColumnName("csv_mapping_profile_id");
        builder.Property(x => x.CsvMappingProfileVersion).HasColumnName("csv_mapping_profile_version");
        builder.Property(x => x.OriginalFileName).HasColumnName("original_file_name").HasMaxLength(255).IsRequired();
        builder.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(128);
        builder.Property(x => x.ContentLength).HasColumnName("content_length").IsRequired();
        builder.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(512).IsRequired();
        builder.Property(x => x.Checksum).HasColumnName("checksum").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Format).HasColumnName("format").HasMaxLength(32);
        builder.Property(x => x.MessageVersion).HasColumnName("message_version").HasMaxLength(64);
        builder.Property(x => x.ParserVersion).HasColumnName("parser_version").HasMaxLength(32);
        builder.Property(x => x.StatementIdentity).HasColumnName("statement_identity").HasMaxLength(128);
        builder.Property(x => x.SourceAccountIdentifier).HasColumnName("source_account_identifier").HasMaxLength(128);
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
        builder.Property(x => x.OpeningBalance).HasColumnName("opening_balance").HasPrecision(19, 4);
        builder.Property(x => x.ClosingBalance).HasColumnName("closing_balance").HasPrecision(19, 4);
        builder.Property(x => x.DebitTotal).HasColumnName("debit_total").HasPrecision(19, 4);
        builder.Property(x => x.CreditTotal).HasColumnName("credit_total").HasPrecision(19, 4);
        builder.Property(x => x.CalculatedClosingBalance).HasColumnName("calculated_closing_balance").HasPrecision(19, 4);
        builder.Property(x => x.TotalRowCount).HasColumnName("total_row_count");
        builder.Property(x => x.AcceptedRowCount).HasColumnName("accepted_row_count");
        builder.Property(x => x.DuplicateRowCount).HasColumnName("duplicate_row_count");
        builder.Property(x => x.ErrorRowCount).HasColumnName("error_row_count");
        builder.Property(x => x.ImportedRowCount).HasColumnName("imported_row_count");
        builder.Property(x => x.LastCommittedRowNumber).HasColumnName("last_committed_row_number");
        builder.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(64);
        builder.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(500);
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(x => x.CompletedByUserId).HasColumnName("completed_by_user_id");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.Property(x => x.CompletedUtc).HasColumnName("completed_at");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.HasIndex(x => new { x.CompanyId, x.Checksum });
        builder.HasIndex(x => new { x.CompanyId, x.Status, x.UpdatedUtc });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.BankAccount).WithMany().HasForeignKey(x => new { x.CompanyId, x.BankAccountId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(x => x.CsvMappingProfile).WithMany().HasForeignKey(x => new { x.CompanyId, x.CsvMappingProfileId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
    }
}

internal sealed class BankStatementImportJobRowConfiguration : IEntityTypeConfiguration<BankStatementImportJobRow>
{
    public void Configure(EntityTypeBuilder<BankStatementImportJobRow> builder)
    {
        builder.ToTable("bank_statement_import_job_rows"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.JobId).HasColumnName("job_id").IsRequired(); builder.Property(x => x.RowNumber).HasColumnName("row_number").IsRequired();
        builder.Property(x => x.RowIdentity).HasColumnName("row_identity").HasMaxLength(128).IsRequired();
        builder.Property(x => x.RowHash).HasColumnName("row_hash").HasMaxLength(64).IsRequired();
        builder.Property(x => x.BookingDateUtc).HasColumnName("booking_date"); builder.Property(x => x.ValueDateUtc).HasColumnName("value_date");
        builder.Property(x => x.Amount).HasColumnName("amount").HasPrecision(19, 4); builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
        builder.Property(x => x.ReferenceText).HasColumnName("reference_text").HasMaxLength(500);
        builder.Property(x => x.Counterparty).HasColumnName("counterparty").HasMaxLength(240);
        builder.Property(x => x.ExternalReference).HasColumnName("external_reference").HasMaxLength(160);
        builder.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(32).IsRequired();
        builder.Property(x => x.IssueCode).HasColumnName("issue_code").HasMaxLength(64); builder.Property(x => x.IssueSeverity).HasColumnName("issue_severity").HasMaxLength(16);
        builder.Property(x => x.IssueMessage).HasColumnName("issue_message").HasMaxLength(500); builder.Property(x => x.PaymentStatus).HasColumnName("payment_status").HasMaxLength(64);
        builder.Property(x => x.ConflictDecision).HasColumnName("conflict_decision").HasMaxLength(32);
        builder.Property(x => x.ConflictDecisionReason).HasColumnName("conflict_decision_reason").HasMaxLength(500);
        builder.Property(x => x.ImportedBankTransactionId).HasColumnName("imported_bank_transaction_id");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired(); builder.Property(x => x.ProcessedUtc).HasColumnName("processed_at");
        builder.HasIndex(x => new { x.CompanyId, x.JobId, x.RowNumber }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.JobId, x.Outcome, x.ProcessedUtc });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Job).WithMany(x => x.Rows).HasForeignKey(x => new { x.CompanyId, x.JobId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(x => x.ImportedBankTransaction).WithMany().HasForeignKey(x => new { x.CompanyId, x.ImportedBankTransactionId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict).IsRequired(false);
    }
}

internal sealed class BankStatementImportJobIssueConfiguration : IEntityTypeConfiguration<BankStatementImportJobIssue>
{
    public void Configure(EntityTypeBuilder<BankStatementImportJobIssue> builder)
    {
        builder.ToTable("bank_statement_import_job_issues"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.JobId).HasColumnName("job_id").IsRequired(); builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Severity).HasColumnName("severity").HasMaxLength(16).IsRequired(); builder.Property(x => x.Message).HasColumnName("message").HasMaxLength(500).IsRequired();
        builder.Property(x => x.RowNumber).HasColumnName("row_number"); builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.JobId, x.Severity });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Job).WithMany(x => x.Issues).HasForeignKey(x => new { x.CompanyId, x.JobId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BankStatementCsvMappingProfileConfiguration : IEntityTypeConfiguration<BankStatementCsvMappingProfile>
{
    public void Configure(EntityTypeBuilder<BankStatementCsvMappingProfile> builder)
    {
        builder.ToTable("bank_statement_csv_mapping_profiles"); builder.HasKey(x => x.Id); builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired(); builder.Property(x => x.CurrentVersion).HasColumnName("current_version");
        builder.Property(x => x.IsActive).HasColumnName("is_active"); builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired(); builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.Name }).IsUnique(); builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BankStatementCsvMappingProfileVersionConfiguration : IEntityTypeConfiguration<BankStatementCsvMappingProfileVersion>
{
    public void Configure(EntityTypeBuilder<BankStatementCsvMappingProfileVersion> builder)
    {
        builder.ToTable("bank_statement_csv_mapping_profile_versions"); builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id"); builder.Property(x => x.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(x => x.ProfileId).HasColumnName("profile_id").IsRequired(); builder.Property(x => x.Version).HasColumnName("version").IsRequired();
        builder.Property(x => x.Delimiter).HasColumnName("delimiter").HasConversion<string>().HasMaxLength(1).IsRequired();
        builder.Property(x => x.CultureName).HasColumnName("culture_name").HasMaxLength(32).IsRequired(); builder.Property(x => x.DateFormat).HasColumnName("date_format").HasMaxLength(64).IsRequired();
        builder.Property(x => x.HasHeader).HasColumnName("has_header"); builder.Property(x => x.BookingDateColumn).HasColumnName("booking_date_column").HasMaxLength(64).IsRequired();
        builder.Property(x => x.ValueDateColumn).HasColumnName("value_date_column").HasMaxLength(64); builder.Property(x => x.AmountColumn).HasColumnName("amount_column").HasMaxLength(64);
        builder.Property(x => x.DebitColumn).HasColumnName("debit_column").HasMaxLength(64); builder.Property(x => x.CreditColumn).HasColumnName("credit_column").HasMaxLength(64);
        builder.Property(x => x.CurrencyColumn).HasColumnName("currency_column").HasMaxLength(64); builder.Property(x => x.ReferenceColumn).HasColumnName("reference_column").HasMaxLength(64).IsRequired();
        builder.Property(x => x.CounterpartyColumn).HasColumnName("counterparty_column").HasMaxLength(64); builder.Property(x => x.ExternalReferenceColumn).HasColumnName("external_reference_column").HasMaxLength(64);
        builder.Property(x => x.AccountIdentifierColumn).HasColumnName("account_identifier_column").HasMaxLength(64); builder.Property(x => x.DefaultCurrency).HasColumnName("default_currency").HasMaxLength(3);
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired(); builder.Property(x => x.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.HasIndex(x => new { x.CompanyId, x.ProfileId, x.Version }).IsUnique();
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(x => x.Profile).WithMany(x => x.Versions).HasForeignKey(x => new { x.CompanyId, x.ProfileId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}
