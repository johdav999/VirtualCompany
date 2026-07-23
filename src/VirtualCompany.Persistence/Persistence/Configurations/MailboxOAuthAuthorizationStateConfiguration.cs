using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Infrastructure.Persistence.Configurations;

internal sealed class MailboxOAuthAuthorizationStateConfiguration : IEntityTypeConfiguration<MailboxOAuthAuthorizationState>
{
    public void Configure(EntityTypeBuilder<MailboxOAuthAuthorizationState> builder)
    {
        builder.ToTable("mailbox_oauth_authorization_states", table =>
        {
            table.HasCheckConstraint("CK_mailbox_oauth_authorization_states_purpose", MailboxPurposeValues.BuildCheckConstraintSql("purpose"));
            table.HasCheckConstraint("CK_mailbox_oauth_authorization_states_provider", MailboxProviderValues.BuildCheckConstraintSql("provider"));
            table.HasCheckConstraint("CK_mailbox_oauth_authorization_states_expiry", "expires_at > created_at");
        });
        builder.HasKey(state => state.Id);
        builder.Property(state => state.Id).HasColumnName("id");
        builder.Property(state => state.CompanyId).HasColumnName("company_id").IsRequired();
        builder.Property(state => state.UserId).HasColumnName("user_id").IsRequired();
        builder.Property(state => state.Purpose)
            .HasColumnName("purpose")
            .HasConversion(value => value.ToStorageValue(), value => MailboxPurposeValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(state => state.Provider)
            .HasColumnName("provider")
            .HasConversion(value => value.ToStorageValue(), value => MailboxProviderValues.Parse(value))
            .HasMaxLength(32)
            .IsRequired();
        builder.Property(state => state.NonceHash).HasColumnName("nonce_hash").HasMaxLength(64).IsRequired();
        builder.Property(state => state.CreatedUtc).HasColumnName("created_at").IsRequired();
        builder.Property(state => state.ExpiresUtc).HasColumnName("expires_at").IsRequired();
        builder.Property(state => state.ConsumedUtc).HasColumnName("consumed_at");

        builder.HasIndex(state => state.NonceHash).IsUnique();
        builder.HasIndex(state => new { state.CompanyId, state.UserId, state.Purpose, state.Provider, state.ExpiresUtc });
        builder.HasIndex(state => new { state.CompanyId, state.ConsumedUtc });
        builder.HasOne(state => state.Company).WithMany().HasForeignKey(state => state.CompanyId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne(state => state.User).WithMany().HasForeignKey(state => state.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}
