using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class CustomerBillingProfileConfiguration : IEntityTypeConfiguration<CustomerBillingProfile>
{
    public void Configure(EntityTypeBuilder<CustomerBillingProfile> builder)
    {
        builder.ToTable("customer_billing_profiles");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.CounterpartyId).HasColumnName("counterparty_id");
        builder.Property(x => x.LegalName).HasColumnName("legal_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
        builder.Property(x => x.PartyKind).HasColumnName("party_kind").HasMaxLength(32).IsRequired();
        builder.Property(x => x.TaxIdentifier).HasColumnName("tax_identifier").HasMaxLength(64);
        builder.Property(x => x.NormalizedTaxIdentifier).HasColumnName("normalized_tax_identifier").HasMaxLength(64);
        builder.Property(x => x.VatIdentifier).HasColumnName("vat_identifier").HasMaxLength(64);
        builder.Property(x => x.NormalizedVatIdentifier).HasColumnName("normalized_vat_identifier").HasMaxLength(64);
        builder.Property(x => x.IdentityValidationState).HasColumnName("identity_validation_state").HasMaxLength(32).IsRequired();
        builder.Property(x => x.BillingAddressLine1).HasColumnName("billing_address_line1").HasMaxLength(200).IsRequired();
        builder.Property(x => x.BillingAddressLine2).HasColumnName("billing_address_line2").HasMaxLength(200);
        builder.Property(x => x.BillingPostalCode).HasColumnName("billing_postal_code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.BillingCity).HasColumnName("billing_city").HasMaxLength(100).IsRequired();
        builder.Property(x => x.BillingRegion).HasColumnName("billing_region").HasMaxLength(100);
        builder.Property(x => x.BillingCountryCode).HasColumnName("billing_country_code").HasMaxLength(2).IsRequired();
        builder.Property(x => x.DeliveryAddressLine1).HasColumnName("delivery_address_line1").HasMaxLength(200);
        builder.Property(x => x.DeliveryAddressLine2).HasColumnName("delivery_address_line2").HasMaxLength(200);
        builder.Property(x => x.DeliveryPostalCode).HasColumnName("delivery_postal_code").HasMaxLength(32);
        builder.Property(x => x.DeliveryCity).HasColumnName("delivery_city").HasMaxLength(100);
        builder.Property(x => x.DeliveryRegion).HasColumnName("delivery_region").HasMaxLength(100);
        builder.Property(x => x.DeliveryCountryCode).HasColumnName("delivery_country_code").HasMaxLength(2);
        builder.Property(x => x.LanguageCode).HasColumnName("language_code").HasMaxLength(16).IsRequired();
        builder.Property(x => x.CurrencyCode).HasColumnName("currency_code").HasMaxLength(3).IsRequired();
        builder.Property(x => x.PaymentTermKind).HasColumnName("payment_term_kind").HasMaxLength(32).IsRequired();
        builder.Property(x => x.PaymentTermDays).HasColumnName("payment_term_days").IsRequired();
        builder.Property(x => x.PaymentMethod).HasColumnName("payment_method").HasMaxLength(64).IsRequired();
        builder.Property(x => x.InvoiceDeliveryChannel).HasColumnName("invoice_delivery_channel").HasMaxLength(32).IsRequired();
        builder.Property(x => x.InvoiceDeliveryEmail).HasColumnName("invoice_delivery_email").HasMaxLength(256);
        builder.Property(x => x.NormalizedInvoiceDeliveryEmail).HasColumnName("normalized_invoice_delivery_email").HasMaxLength(256);
        builder.Property(x => x.BuyerReference).HasColumnName("buyer_reference").HasMaxLength(100);
        builder.Property(x => x.EInvoiceIdentifier).HasColumnName("e_invoice_identifier").HasMaxLength(128);
        builder.Property(x => x.NormalizedEInvoiceIdentifier).HasColumnName("normalized_e_invoice_identifier").HasMaxLength(128);
        builder.Property(x => x.EInvoiceIdentifierType).HasColumnName("e_invoice_identifier_type").HasMaxLength(64);
        builder.Property(x => x.CreditLimit).HasColumnName("credit_limit").HasColumnType("decimal(18,2)");
        builder.Property(x => x.CreditStatus).HasColumnName("credit_status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.DefaultAccountMapping).HasColumnName("default_account_mapping").HasMaxLength(64);
        builder.Property(x => x.DefaultDimensionCode).HasColumnName("default_dimension_code").HasMaxLength(64);
        builder.Property(x => x.EffectiveFrom).HasColumnName("effective_from").HasColumnType("date");
        builder.Property(x => x.EffectiveTo).HasColumnName("effective_to").HasColumnType("date");
        builder.Property(x => x.SourceKind).HasColumnName("source_kind").HasMaxLength(32).IsRequired();
        builder.Property(x => x.SourceReference).HasColumnName("source_reference").HasMaxLength(200);
        builder.Property(x => x.UserAttestedUtc).HasColumnName("user_attested_at");
        builder.Property(x => x.ExternallyVerifiedUtc).HasColumnName("externally_verified_at");
        builder.Property(x => x.VerificationSource).HasColumnName("verification_source").HasMaxLength(200);
        builder.Property(x => x.ConflictState).HasColumnName("conflict_state").HasMaxLength(32).IsRequired();
        builder.Property(x => x.MergedIntoCounterpartyId).HasColumnName("merged_into_counterparty_id");
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at");
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        builder.HasIndex(x => new { x.CompanyId, x.CounterpartyId }).IsUnique();
        builder.HasIndex(x => new { x.CompanyId, x.NormalizedTaxIdentifier });
        builder.HasIndex(x => new { x.CompanyId, x.NormalizedVatIdentifier });
        builder.HasIndex(x => new { x.CompanyId, x.NormalizedEInvoiceIdentifier });
        builder.HasIndex(x => new { x.CompanyId, x.NormalizedInvoiceDeliveryEmail });
        builder.HasIndex(x => new { x.CompanyId, x.ConflictState });
        builder.HasOne(x => x.Counterparty).WithMany().HasForeignKey(x => new { x.CompanyId, x.CounterpartyId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
