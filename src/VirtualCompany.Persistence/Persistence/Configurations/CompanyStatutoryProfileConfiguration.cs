using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

public sealed class CompanyStatutoryProfileConfiguration : IEntityTypeConfiguration<CompanyStatutoryProfile>
{
    public void Configure(EntityTypeBuilder<CompanyStatutoryProfile> builder)
    {
        builder.ToTable("company_statutory_profiles", table =>
        {
            table.HasCheckConstraint("CK_company_statutory_profiles_vat_status", "[vat_registration_status] IN ('not_registered', 'pending', 'registered')");
            table.HasCheckConstraint("CK_company_statutory_profiles_fiscal_basis", "[fiscal_year_basis] IN ('calendar_year', 'non_calendar_year')");
            table.HasCheckConstraint("CK_company_statutory_profiles_bookkeeping_method", "[bookkeeping_method] IN ('not_specified', 'accrual', 'cash')");
            table.HasCheckConstraint("CK_company_statutory_profiles_verification_status", "[verification_status] IN ('unverified', 'externally_verified', 'verification_failed')");
            table.HasCheckConstraint("CK_company_statutory_profiles_source_kind", "[source_kind] IN ('user_entry', 'imported_document', 'external_registry')");
            table.HasCheckConstraint("CK_company_statutory_profiles_vat_dates", "[vat_registration_effective_to] IS NULL OR [vat_registration_effective_from] IS NULL OR [vat_registration_effective_to] >= [vat_registration_effective_from]");
        });

        builder.HasKey(profile => profile.Id);
        builder.HasAlternateKey(profile => new { profile.CompanyId, profile.Id });
        builder.Property(profile => profile.Id).HasColumnName("id");
        builder.Property(profile => profile.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(profile => profile.LegalName).HasColumnName("legal_name").HasMaxLength(200);
        builder.Property(profile => profile.SwedishOrganisationNumber).HasColumnName("swedish_organisation_number").HasMaxLength(10).IsFixedLength();
        builder.Property(profile => profile.VatRegistrationNumber).HasColumnName("vat_registration_number").HasMaxLength(14).IsFixedLength();
        builder.Property(profile => profile.VatRegistrationStatus).HasColumnName("vat_registration_status").HasMaxLength(32).IsRequired();
        builder.Property(profile => profile.RegisteredAddressLine1).HasColumnName("registered_address_line_1").HasMaxLength(200);
        builder.Property(profile => profile.RegisteredAddressLine2).HasColumnName("registered_address_line_2").HasMaxLength(200);
        builder.Property(profile => profile.RegisteredPostalCode).HasColumnName("registered_postal_code").HasMaxLength(16);
        builder.Property(profile => profile.RegisteredCity).HasColumnName("registered_city").HasMaxLength(100);
        builder.Property(profile => profile.RegisteredCountryCode).HasColumnName("registered_country_code").HasMaxLength(2).IsFixedLength();
        builder.Property(profile => profile.CorrespondenceAddressLine1).HasColumnName("correspondence_address_line_1").HasMaxLength(200);
        builder.Property(profile => profile.CorrespondenceAddressLine2).HasColumnName("correspondence_address_line_2").HasMaxLength(200);
        builder.Property(profile => profile.CorrespondencePostalCode).HasColumnName("correspondence_postal_code").HasMaxLength(16);
        builder.Property(profile => profile.CorrespondenceCity).HasColumnName("correspondence_city").HasMaxLength(100);
        builder.Property(profile => profile.CorrespondenceCountryCode).HasColumnName("correspondence_country_code").HasMaxLength(2).IsFixedLength();
        builder.Property(profile => profile.CountryCode).HasColumnName("country_code").HasMaxLength(2).IsFixedLength().IsRequired();
        builder.Property(profile => profile.AccountingCurrency).HasColumnName("accounting_currency").HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(profile => profile.FiscalYearBasis).HasColumnName("fiscal_year_basis").HasMaxLength(32).IsRequired();
        builder.Property(profile => profile.BookkeepingMethod).HasColumnName("bookkeeping_method").HasMaxLength(32).IsRequired();
        builder.Property(profile => profile.OrganisationRegistrationEffectiveFrom).HasColumnName("organisation_registration_effective_from").HasColumnType("date");
        builder.Property(profile => profile.VatRegistrationEffectiveFrom).HasColumnName("vat_registration_effective_from").HasColumnType("date");
        builder.Property(profile => profile.VatRegistrationEffectiveTo).HasColumnName("vat_registration_effective_to").HasColumnType("date");
        builder.Property(profile => profile.IsUserAttested).HasColumnName("is_user_attested").IsRequired();
        builder.Property(profile => profile.AttestedByUserId).HasColumnName("attested_by_user_id");
        builder.Property(profile => profile.AttestedUtc).HasColumnName("attested_utc");
        builder.Property(profile => profile.VerificationStatus).HasColumnName("verification_status").HasMaxLength(32).IsRequired();
        builder.Property(profile => profile.SourceKind).HasColumnName("source_kind").HasMaxLength(64).IsRequired();
        builder.Property(profile => profile.SourceReference).HasColumnName("source_reference").HasMaxLength(256);
        builder.Property(profile => profile.SourceCapturedUtc).HasColumnName("source_captured_utc").IsRequired();
        builder.Property(profile => profile.ExternalVerifier).HasColumnName("external_verifier").HasMaxLength(200);
        builder.Property(profile => profile.ExternallyVerifiedUtc).HasColumnName("externally_verified_utc");
        builder.Property(profile => profile.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        builder.Property(profile => profile.UpdatedByUserId).HasColumnName("updated_by_user_id").IsRequired();
        builder.Property(profile => profile.CreatedUtc).HasColumnName("created_utc").IsRequired();
        builder.Property(profile => profile.UpdatedUtc).HasColumnName("updated_utc").IsRequired();
        builder.Property(profile => profile.Version).HasColumnName("version").IsConcurrencyToken().IsRequired();

        builder.HasIndex(profile => profile.CompanyId).IsUnique();
        builder.HasIndex(profile => new { profile.CompanyId, profile.SwedishOrganisationNumber });
        builder.HasIndex(profile => new { profile.CompanyId, profile.VatRegistrationNumber });
        builder.HasOne(profile => profile.Company)
            .WithMany()
            .HasForeignKey(profile => profile.CompanyId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
