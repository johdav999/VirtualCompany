using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal sealed class TeamsTenantRegistrationConfiguration : IEntityTypeConfiguration<TeamsTenantRegistration>
{
    public void Configure(EntityTypeBuilder<TeamsTenantRegistration> builder)
    {
        builder.ToTable("teams_tenant_registrations", table =>
        {
            table.HasCheckConstraint("CK_teams_tenant_registration_status", "status IN ('pending_consent','pending_policy','ready','blocked','disabled','revoked')");
            table.HasCheckConstraint("CK_teams_tenant_registration_consent", "consent_status IN ('pending','verified','revoked')");
            table.HasCheckConstraint("CK_teams_tenant_registration_permission", "permission_status IN ('pending','verified','missing','excess','revoked')");
            table.HasCheckConstraint("CK_teams_tenant_registration_policy", "policy_status IN ('pending','attested','revoked')");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.FirstUatMeetingId).HasColumnName("first_uat_meeting_id");
        builder.Property(x => x.FirstUatOrganizerId).HasColumnName("first_uat_organizer_id");
        builder.Property(x => x.FirstUatAuthorizedByUserId).HasColumnName("first_uat_authorized_by_user_id");
        builder.Property(x => x.FirstUatAuthorizedUtc).HasColumnName("first_uat_authorized_at");
        builder.Property(x => x.FirstUatExpiresUtc).HasColumnName("first_uat_expires_at");
        builder.Property(x => x.FirstUatReason).HasColumnName("first_uat_reason").HasMaxLength(500);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.EntraTenantId).HasColumnName("entra_tenant_id");
        builder.Property(x => x.TeamsAppId).HasColumnName("teams_app_id");
        builder.Property(x => x.BotApplicationId).HasColumnName("bot_application_id");
        builder.Property(x => x.ApprovedMediaRoute).HasColumnName("approved_media_route").HasMaxLength(64).IsRequired();
        builder.Property(x => x.RequiredPermissions).HasColumnName("required_permissions").HasMaxLength(500).IsRequired();
        builder.Property(x => x.GrantedPermissions).HasColumnName("granted_permissions").HasMaxLength(500).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.ConsentStatus).HasColumnName("consent_status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.PermissionStatus).HasColumnName("permission_status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.PolicyStatus).HasColumnName("policy_status").HasMaxLength(32).IsRequired();
        builder.Property(x => x.FailureCode).HasColumnName("failure_code").HasMaxLength(120);
        builder.Property(x => x.ConsentVerifiedUtc).HasColumnName("consent_verified_at");
        builder.Property(x => x.ConsentVerifiedByUserId).HasColumnName("consent_verified_by_user_id");
        builder.Property(x => x.PermissionsVerifiedUtc).HasColumnName("permissions_verified_at");
        builder.Property(x => x.PolicyApprovedUtc).HasColumnName("policy_approved_at");
        builder.Property(x => x.PolicyApprovedByUserId).HasColumnName("policy_approved_by_user_id");
        builder.Property(x => x.DisabledUtc).HasColumnName("disabled_at");
        builder.Property(x => x.DisabledByUserId).HasColumnName("disabled_by_user_id");
        builder.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id");
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at");
        builder.Property(x => x.UpdatedUtc).HasColumnName("updated_at");
        builder.Property(x => x.ConcurrencyVersion).HasColumnName("concurrency_version").IsConcurrencyToken();
        builder.HasIndex(x => x.CompanyId).IsUnique();
        builder.HasIndex(x => x.EntraTenantId).IsUnique();
        builder.HasIndex(x => new { x.Status, x.UpdatedUtc });
        builder.HasOne(x => x.Company).WithMany().HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class TeamsAdminConsentSessionConfiguration : IEntityTypeConfiguration<TeamsAdminConsentSession>
{
    public void Configure(EntityTypeBuilder<TeamsAdminConsentSession> builder)
    {
        builder.ToTable("teams_admin_consent_sessions");
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.CompanyId, x.Id });
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CompanyId).HasColumnName("company_id");
        builder.Property(x => x.RegistrationId).HasColumnName("registration_id");
        builder.Property(x => x.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(x => x.StateHash).HasColumnName("state_hash").HasMaxLength(128).IsRequired();
        builder.Property(x => x.CreatedUtc).HasColumnName("created_at");
        builder.Property(x => x.ExpiresUtc).HasColumnName("expires_at");
        builder.Property(x => x.ConsumedUtc).HasColumnName("consumed_at");
        builder.HasIndex(x => x.StateHash).IsUnique();
        builder.HasIndex(x => new { x.ExpiresUtc, x.ConsumedUtc });
        builder.HasOne(x => x.Registration).WithMany()
            .HasForeignKey(x => new { x.CompanyId, x.RegistrationId })
            .HasPrincipalKey(x => new { x.CompanyId, x.Id })
            .OnDelete(DeleteBehavior.Cascade);
    }
}
