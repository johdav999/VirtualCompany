using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class BankConnectionConfiguration : IEntityTypeConfiguration<BankConnection>
{
    public void Configure(EntityTypeBuilder<BankConnection> b)
    {
        b.ToTable("bank_connections"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64).IsRequired();
        b.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128).IsRequired();
        b.Property(x => x.InstitutionName).HasColumnName("institution_name").HasMaxLength(200).IsRequired();
        b.Property(x => x.ConnectedByUserId).HasColumnName("connected_by_user_id");
        b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        b.Property(x => x.HealthStatus).HasColumnName("health_status").HasMaxLength(32).IsRequired();
        b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(96); b.Property(x => x.ReasonSummary).HasColumnName("reason_summary").HasMaxLength(1000);
        b.Property(x => x.ConsentExpiresUtc).HasColumnName("consent_expires_at"); b.Property(x => x.LastHealthCheckedUtc).HasColumnName("last_health_checked_at");
        b.Property(x => x.SuspendedUtc).HasColumnName("suspended_at"); b.Property(x => x.RevokedUtc).HasColumnName("revoked_at"); b.Property(x => x.DisconnectedUtc).HasColumnName("disconnected_at");
        b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken(); b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        b.HasIndex(x => new { x.CompanyId, x.ProviderKey, x.InstitutionId }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.Status }); b.HasIndex(x => new { x.CompanyId, x.ConsentExpiresUtc });
        b.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.ConnectedByUser).WithMany().HasForeignKey(x => x.ConnectedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class BankConsentSessionConfiguration : IEntityTypeConfiguration<BankConsentSession>
{
    public void Configure(EntityTypeBuilder<BankConsentSession> b)
    {
        b.ToTable("bank_consent_sessions"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.ConnectionId).HasColumnName("connection_id"); b.Property(x => x.ProviderKey).HasColumnName("provider_key").HasMaxLength(64).IsRequired();
        b.Property(x => x.InstitutionId).HasColumnName("institution_id").HasMaxLength(128).IsRequired(); b.Property(x => x.StartedByUserId).HasColumnName("started_by_user_id");
        b.Property(x => x.StateHash).HasColumnName("state_hash").HasMaxLength(64).IsRequired(); b.Property(x => x.NonceHash).HasColumnName("nonce_hash").HasMaxLength(64).IsRequired();
        b.Property(x => x.ProviderSessionReference).HasColumnName("provider_session_reference").HasMaxLength(256); b.Property(x => x.ReturnUri).HasColumnName("return_uri").HasMaxLength(1000);
        b.Property(x => x.IsRenewal).HasColumnName("is_renewal"); b.Property(x => x.ExpiresUtc).HasColumnName("expires_at"); b.Property(x => x.ConsumedUtc).HasColumnName("consumed_at"); b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => x.StateHash).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.ExpiresUtc });
        b.HasOne(x => x.Connection).WithMany().HasForeignKey(x => new { x.CompanyId, x.ConnectionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class BankConsentVersionConfiguration : IEntityTypeConfiguration<BankConsentVersion>
{
    public void Configure(EntityTypeBuilder<BankConsentVersion> b)
    {
        b.ToTable("bank_consent_versions"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.ConnectionId).HasColumnName("connection_id");
        b.Property(x => x.Version).HasColumnName("version"); b.Property(x => x.ProviderConsentId).HasColumnName("provider_consent_id").HasMaxLength(256).IsRequired(); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        b.Property(x => x.EffectiveUtc).HasColumnName("effective_at"); b.Property(x => x.ExpiresUtc).HasColumnName("expires_at"); b.Property(x => x.EndedUtc).HasColumnName("ended_at"); b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.ConnectionId, x.Version }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.ProviderConsentId }).IsUnique();
        b.HasOne(x => x.Connection).WithMany(x => x.Consents).HasForeignKey(x => new { x.CompanyId, x.ConnectionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BankConnectionCapabilityGrantConfiguration : IEntityTypeConfiguration<BankConnectionCapabilityGrant>
{
    public void Configure(EntityTypeBuilder<BankConnectionCapabilityGrant> b)
    {
        b.ToTable("bank_connection_capability_grants"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.ConnectionId).HasColumnName("connection_id"); b.Property(x => x.ConsentVersionId).HasColumnName("consent_version_id"); b.Property(x => x.Capability).HasColumnName("capability").HasMaxLength(96).IsRequired(); b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.ConsentVersionId, x.Capability }).IsUnique();
        b.HasOne(x => x.Connection).WithMany(x => x.CapabilityGrants).HasForeignKey(x => new { x.CompanyId, x.ConnectionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ConsentVersion).WithMany().HasForeignKey(x => new { x.CompanyId, x.ConsentVersionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BankDiscoveredAccountConfiguration : IEntityTypeConfiguration<BankDiscoveredAccount>
{
    public void Configure(EntityTypeBuilder<BankDiscoveredAccount> b)
    {
        b.ToTable("bank_discovered_accounts"); b.HasKey(x => x.Id); b.HasAlternateKey(x => new { x.CompanyId, x.Id });
        b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.ConnectionId).HasColumnName("connection_id");
        b.Property(x => x.ProviderAccountId).HasColumnName("provider_account_id").HasMaxLength(256).IsRequired(); b.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(200).IsRequired();
        b.Property(x => x.ProviderAccessReference).HasColumnName("provider_access_reference").HasMaxLength(256);
        b.Property(x => x.MaskedAccountNumber).HasColumnName("masked_account_number").HasMaxLength(64).IsRequired(); b.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        b.Property(x => x.OwnershipStatus).HasColumnName("ownership_status").HasMaxLength(32).IsRequired(); b.Property(x => x.OwnershipSummary).HasColumnName("ownership_summary").HasMaxLength(500);
        b.Property(x => x.IsAvailable).HasColumnName("is_available"); b.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken(); b.Property(x => x.FirstDiscoveredUtc).HasColumnName("first_discovered_at"); b.Property(x => x.LastSeenUtc).HasColumnName("last_seen_at");
        b.HasIndex(x => new { x.CompanyId, x.ConnectionId, x.ProviderAccountId }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.OwnershipStatus });
        b.HasOne(x => x.Connection).WithMany(x => x.DiscoveredAccounts).HasForeignKey(x => new { x.CompanyId, x.ConnectionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BankAccountMappingConfiguration : IEntityTypeConfiguration<BankAccountMapping>
{
    public void Configure(EntityTypeBuilder<BankAccountMapping> b)
    {
        b.ToTable("bank_account_mappings"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id");
        b.Property(x => x.DiscoveredAccountId).HasColumnName("discovered_account_id"); b.Property(x => x.CompanyBankAccountId).HasColumnName("company_bank_account_id"); b.Property(x => x.Version).HasColumnName("version");
        b.Property(x => x.MappedByUserId).HasColumnName("mapped_by_user_id"); b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500).IsRequired(); b.Property(x => x.IsCurrent).HasColumnName("is_current"); b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.SupersededUtc).HasColumnName("superseded_at");
        b.HasIndex(x => new { x.CompanyId, x.DiscoveredAccountId, x.Version }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.DiscoveredAccountId }).IsUnique().HasFilter("[is_current] = 1");
        b.HasOne(x => x.DiscoveredAccount).WithMany(x => x.Mappings).HasForeignKey(x => new { x.CompanyId, x.DiscoveredAccountId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.CompanyBankAccount).WithMany().HasForeignKey(x => new { x.CompanyId, x.CompanyBankAccountId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class BankConnectionCredentialConfiguration : IEntityTypeConfiguration<BankConnectionCredential>
{
    public void Configure(EntityTypeBuilder<BankConnectionCredential> b)
    {
        b.ToTable("bank_connection_credentials"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.ConnectionId).HasColumnName("connection_id");
        b.Property(x => x.EncryptedEnvelope).HasColumnName("encrypted_envelope").HasColumnType("nvarchar(max)").IsRequired(); b.Property(x => x.EncryptionKeyId).HasColumnName("encryption_key_id").HasMaxLength(128).IsRequired();
        b.Property(x => x.ExpiresUtc).HasColumnName("expires_at"); b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at"); b.HasIndex(x => new { x.CompanyId, x.ConnectionId }).IsUnique();
        b.HasOne(x => x.Connection).WithMany().HasForeignKey(x => new { x.CompanyId, x.ConnectionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class BankConnectionAuditEventConfiguration : IEntityTypeConfiguration<BankConnectionAuditEvent>
{
    public void Configure(EntityTypeBuilder<BankConnectionAuditEvent> b)
    {
        b.ToTable("bank_connection_audit_events"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.ConnectionId).HasColumnName("connection_id"); b.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
        b.Property(x => x.EventType).HasColumnName("event_type").HasMaxLength(96).IsRequired(); b.Property(x => x.Outcome).HasColumnName("outcome").HasMaxLength(32).IsRequired(); b.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(1000).IsRequired(); b.Property(x => x.ReasonCode).HasColumnName("reason_code").HasMaxLength(96); b.Property(x => x.CorrelationId).HasColumnName("correlation_id").HasMaxLength(128); b.Property(x => x.BeforeState).HasColumnName("before_state").HasMaxLength(2000); b.Property(x => x.AfterState).HasColumnName("after_state").HasMaxLength(2000); b.Property(x => x.CreatedUtc).HasColumnName("created_at");
        b.HasIndex(x => new { x.CompanyId, x.ConnectionId, x.CreatedUtc }); b.HasIndex(x => new { x.CompanyId, x.CorrelationId });
        b.HasOne(x => x.Connection).WithMany().HasForeignKey(x => new { x.CompanyId, x.ConnectionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class BankConsentRevocationTaskConfiguration : IEntityTypeConfiguration<BankConsentRevocationTask>
{
    public void Configure(EntityTypeBuilder<BankConsentRevocationTask> b)
    {
        b.ToTable("bank_consent_revocation_tasks"); b.HasKey(x => x.Id); b.Property(x => x.Id).HasColumnName("id"); b.Property(x => x.CompanyId).HasColumnName("company_id"); b.Property(x => x.ConnectionId).HasColumnName("connection_id"); b.Property(x => x.ConsentVersionId).HasColumnName("consent_version_id"); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired(); b.Property(x => x.AttemptCount).HasColumnName("attempt_count"); b.Property(x => x.NextAttemptUtc).HasColumnName("next_attempt_at"); b.Property(x => x.LeaseExpiresUtc).HasColumnName("lease_expires_at"); b.Property(x => x.SafeFailureSummary).HasColumnName("safe_failure_summary").HasMaxLength(1000); b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        b.HasIndex(x => new { x.Status, x.NextAttemptUtc }); b.HasIndex(x => new { x.CompanyId, x.ConsentVersionId });
        b.HasOne(x => x.Connection).WithMany().HasForeignKey(x => new { x.CompanyId, x.ConnectionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne(x => x.ConsentVersion).WithMany().HasForeignKey(x => new { x.CompanyId, x.ConsentVersionId }).HasPrincipalKey(x => new { x.CompanyId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
