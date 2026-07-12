using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using VirtualCompany.Domain.Entities;

namespace VirtualCompany.Infrastructure.Persistence;

internal abstract class LeadGenerationConfiguration<T> : IEntityTypeConfiguration<T> where T : class, ICompanyOwnedEntity
{
    protected abstract string TableName { get; }
    public void Configure(EntityTypeBuilder<T> builder)
    {
        builder.ToTable(TableName);
        builder.HasKey("Id");
        builder.HasAlternateKey("CompanyId", "Id");
        builder.Property<Guid>("Id").HasColumnName("id");
        builder.Property<Guid>("CompanyId").HasColumnName("company_id").IsRequired();
        builder.HasIndex("CompanyId");
        ConfigureEntity(builder);
    }
    protected abstract void ConfigureEntity(EntityTypeBuilder<T> builder);
    protected static void RowVersion(EntityTypeBuilder<T> builder) => builder.Property<byte[]>("RowVersion").HasColumnName("row_version").IsRowVersion();
}

internal sealed class IdealCustomerProfileConfiguration : LeadGenerationConfiguration<IdealCustomerProfile>
{
    protected override string TableName => "sales_icp_profiles";
    protected override void ConfigureEntity(EntityTypeBuilder<IdealCustomerProfile> b)
    {
        b.Property(x => x.PreviousVersionId).HasColumnName("previous_version_id"); b.Property(x => x.Name).HasColumnName("name").HasMaxLength(160).IsRequired(); b.Property(x => x.Version).HasColumnName("version"); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        b.Property(x => x.Countries).HasColumnName("countries").HasMaxLength(1000); b.Property(x => x.Industries).HasColumnName("industries").HasMaxLength(1000); b.Property(x => x.EmployeeMin).HasColumnName("employee_min"); b.Property(x => x.EmployeeMax).HasColumnName("employee_max"); b.Property(x => x.RevenueMin).HasColumnName("revenue_min").HasPrecision(18, 2); b.Property(x => x.RevenueMax).HasColumnName("revenue_max").HasPrecision(18, 2);
        b.Property(x => x.BuyerRoles).HasColumnName("buyer_roles").HasMaxLength(2000); b.Property(x => x.Technologies).HasColumnName("technologies").HasMaxLength(2000); b.Property(x => x.PainHypotheses).HasColumnName("pain_hypotheses").HasMaxLength(4000); b.Property(x => x.PositiveCriteria).HasColumnName("positive_criteria").HasMaxLength(4000); b.Property(x => x.Disqualifiers).HasColumnName("disqualifiers").HasMaxLength(4000);
        b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id"); b.Property(x => x.UpdatedByUserId).HasColumnName("updated_by_user_id"); b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at"); b.Property(x => x.ActivatedUtc).HasColumnName("activated_at"); RowVersion(b);
        b.HasIndex(x => new { x.CompanyId, x.Name, x.Version }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.Status });
    }
}

internal sealed class ProspectSourcePolicyConfiguration : LeadGenerationConfiguration<ProspectSourcePolicy>
{
    protected override string TableName => "sales_prospect_source_policies";
    protected override void ConfigureEntity(EntityTypeBuilder<ProspectSourcePolicy> b)
    {
        b.Property(x => x.Version).HasColumnName("version"); b.Property(x => x.EnabledSources).HasColumnName("enabled_sources").HasMaxLength(1000); b.Property(x => x.AllowedCountries).HasColumnName("allowed_countries").HasMaxLength(1000); b.Property(x => x.AllowedFields).HasColumnName("allowed_fields").HasMaxLength(2000);
        b.Property(x => x.PerRunBudget).HasColumnName("per_run_budget").HasPrecision(18, 2); b.Property(x => x.MonthlyBudget).HasColumnName("monthly_budget").HasPrecision(18, 2); b.Property(x => x.ApprovalThreshold).HasColumnName("approval_threshold").HasPrecision(18, 2); b.Property(x => x.RetentionDays).HasColumnName("retention_days"); b.Property(x => x.RefreshDays).HasColumnName("refresh_days"); b.Property(x => x.ReservedThisMonth).HasColumnName("reserved_this_month").HasPrecision(18, 2); b.Property(x => x.ActualThisMonth).HasColumnName("actual_this_month").HasPrecision(18, 2); b.Property(x => x.IsActive).HasColumnName("is_active"); b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at"); RowVersion(b); b.HasIndex(x => x.CompanyId).IsUnique();
    }
}

internal sealed class ProspectingRunConfiguration : LeadGenerationConfiguration<ProspectingRun>
{
    protected override string TableName => "sales_prospecting_runs";
    protected override void ConfigureEntity(EntityTypeBuilder<ProspectingRun> b)
    {
        b.Property(x => x.IdealCustomerProfileId).HasColumnName("icp_id"); b.Property(x => x.OwnerUserId).HasColumnName("owner_user_id"); b.Property(x => x.ApprovalId).HasColumnName("approval_id"); b.Property(x => x.Name).HasColumnName("name").HasMaxLength(160); b.Property(x => x.AccountLimit).HasColumnName("account_limit"); b.Property(x => x.ContactLimit).HasColumnName("contact_limit"); b.Property(x => x.Sources).HasColumnName("sources").HasMaxLength(1000); b.Property(x => x.Geography).HasColumnName("geography").HasMaxLength(1000); b.Property(x => x.FreshnessDays).HasColumnName("freshness_days"); b.Property(x => x.EstimatedCost).HasColumnName("estimated_cost").HasPrecision(18, 2); b.Property(x => x.ActualCost).HasColumnName("actual_cost").HasPrecision(18, 2); b.Property(x => x.Schedule).HasColumnName("schedule").HasMaxLength(200); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32); b.Property(x => x.CurrentStep).HasColumnName("current_step").HasMaxLength(160); b.Property(x => x.AccountsFound).HasColumnName("accounts_found"); b.Property(x => x.ContactsFound).HasColumnName("contacts_found"); b.Property(x => x.Cursor).HasColumnName("cursor").HasMaxLength(1000); b.Property(x => x.FailureSummary).HasColumnName("failure_summary").HasMaxLength(1000); b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at"); b.Property(x => x.StartedUtc).HasColumnName("started_at"); b.Property(x => x.CompletedUtc).HasColumnName("completed_at"); RowVersion(b); b.HasIndex(x => new { x.CompanyId, x.Status, x.CreatedUtc });
    }
}

internal sealed class ProspectAccountConfiguration : LeadGenerationConfiguration<ProspectAccount>
{
    protected override string TableName => "sales_prospect_accounts";
    protected override void ConfigureEntity(EntityTypeBuilder<ProspectAccount> b)
    {
        b.Property(x => x.ProspectingRunId).HasColumnName("run_id"); b.Property(x => x.IdealCustomerProfileId).HasColumnName("icp_id"); b.Property(x => x.CustomerCompanyId).HasColumnName("customer_company_id"); b.Property(x => x.LeadId).HasColumnName("lead_id"); b.Property(x => x.MergedIntoId).HasColumnName("merged_into_id"); b.Property(x => x.Name).HasColumnName("name").HasMaxLength(200); b.Property(x => x.LegalName).HasColumnName("legal_name").HasMaxLength(200); b.Property(x => x.Domain).HasColumnName("domain").HasMaxLength(256); b.Property(x => x.Country).HasColumnName("country").HasMaxLength(120); b.Property(x => x.Industry).HasColumnName("industry").HasMaxLength(160); b.Property(x => x.Employees).HasColumnName("employees"); b.Property(x => x.Revenue).HasColumnName("revenue").HasPrecision(18, 2); b.Property(x => x.Technologies).HasColumnName("technologies").HasMaxLength(2000); b.Property(x => x.SourceKey).HasColumnName("source_key").HasMaxLength(80); b.Property(x => x.SourceReference).HasColumnName("source_reference").HasMaxLength(512); b.Property(x => x.LastObservedUtc).HasColumnName("last_observed_at"); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32); b.Property(x => x.FitOutcome).HasColumnName("fit_outcome").HasMaxLength(32); b.Property(x => x.FitScore).HasColumnName("fit_score").HasPrecision(5, 2); b.Property(x => x.TimingScore).HasColumnName("timing_score").HasPrecision(5, 2); b.Property(x => x.RoleScore).HasColumnName("role_score").HasPrecision(5, 2); b.Property(x => x.DataConfidenceScore).HasColumnName("data_confidence_score").HasPrecision(5, 2); b.Property(x => x.OverallScore).HasColumnName("overall_score").HasPrecision(5, 2); b.Property(x => x.ScoreBand).HasColumnName("score_band").HasMaxLength(32); b.Property(x => x.EvaluationJson).HasColumnName("evaluation_json").HasColumnType("nvarchar(max)"); b.Property(x => x.ResearchBriefJson).HasColumnName("research_brief_json").HasColumnType("nvarchar(max)"); b.Property(x => x.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(500); b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at"); RowVersion(b);
        b.HasIndex(x => new { x.CompanyId, x.Domain }); b.HasIndex(x => new { x.CompanyId, x.SourceKey, x.SourceReference }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.Status, x.OverallScore });
    }
}

internal sealed class ProspectContactConfiguration : LeadGenerationConfiguration<ProspectContact>
{
    protected override string TableName => "sales_prospect_contacts";
    protected override void ConfigureEntity(EntityTypeBuilder<ProspectContact> b)
    {
        b.Property(x => x.ProspectAccountId).HasColumnName("prospect_account_id"); b.Property(x => x.ContactId).HasColumnName("contact_id"); b.Property(x => x.MergedIntoId).HasColumnName("merged_into_id"); b.Property(x => x.FullName).HasColumnName("full_name").HasMaxLength(160); b.Property(x => x.Title).HasColumnName("title").HasMaxLength(160); b.Property(x => x.Department).HasColumnName("department").HasMaxLength(120); b.Property(x => x.Seniority).HasColumnName("seniority").HasMaxLength(80); b.Property(x => x.BuyingRoles).HasColumnName("buying_roles").HasMaxLength(1000); b.Property(x => x.Email).HasColumnName("email").HasMaxLength(256); b.Property(x => x.EmailStatus).HasColumnName("email_status").HasMaxLength(32); b.Property(x => x.Phone).HasColumnName("phone").HasMaxLength(64); b.Property(x => x.ProfileUrl).HasColumnName("profile_url").HasMaxLength(512); b.Property(x => x.EmploymentStatus).HasColumnName("employment_status").HasMaxLength(40); b.Property(x => x.Confidence).HasColumnName("confidence").HasPrecision(5, 4); b.Property(x => x.SourceKey).HasColumnName("source_key").HasMaxLength(80); b.Property(x => x.SourceReference).HasColumnName("source_reference").HasMaxLength(512); b.Property(x => x.VerifiedUtc).HasColumnName("verified_at"); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32); b.Property(x => x.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(500); b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.UpdatedUtc).HasColumnName("updated_at"); RowVersion(b); b.HasIndex(x => new { x.CompanyId, x.SourceKey, x.SourceReference }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.Email });
    }
}

internal sealed class ProspectSignalConfiguration : LeadGenerationConfiguration<ProspectSignal>
{
    protected override string TableName => "sales_prospect_signals";
    protected override void ConfigureEntity(EntityTypeBuilder<ProspectSignal> b)
    {
        b.Property(x => x.ProspectAccountId).HasColumnName("prospect_account_id"); b.Property(x => x.SignalType).HasColumnName("signal_type").HasMaxLength(80); b.Property(x => x.SourceKey).HasColumnName("source_key").HasMaxLength(80); b.Property(x => x.SourceReference).HasColumnName("source_reference").HasMaxLength(512); b.Property(x => x.Summary).HasColumnName("summary").HasMaxLength(1000); b.Property(x => x.EventUtc).HasColumnName("event_at"); b.Property(x => x.FreshUntilUtc).HasColumnName("fresh_until"); b.Property(x => x.Confidence).HasColumnName("confidence").HasPrecision(5, 4); b.Property(x => x.Relevance).HasColumnName("relevance").HasPrecision(5, 2); b.Property(x => x.Status).HasColumnName("status").HasMaxLength(32); b.Property(x => x.DedupeKey).HasColumnName("dedupe_key").HasMaxLength(256); b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.HasIndex(x => new { x.CompanyId, x.DedupeKey }).IsUnique(); b.HasIndex(x => new { x.CompanyId, x.ProspectAccountId, x.FreshUntilUtc });
    }
}

internal sealed class SalesSuppressionConfiguration : LeadGenerationConfiguration<SalesSuppression>
{
    protected override string TableName => "sales_suppressions";
    protected override void ConfigureEntity(EntityTypeBuilder<SalesSuppression> b)
    {
        b.Property(x => x.ScopeType).HasColumnName("scope_type").HasMaxLength(32); b.Property(x => x.ScopeValue).HasColumnName("scope_value").HasMaxLength(512); b.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(500); b.Property(x => x.Source).HasColumnName("source").HasMaxLength(80); b.Property(x => x.CreatedByUserId).HasColumnName("created_by_user_id"); b.Property(x => x.IsActive).HasColumnName("is_active"); b.Property(x => x.CreatedUtc).HasColumnName("created_at"); b.Property(x => x.ExpiresUtc).HasColumnName("expires_at"); b.HasIndex(x => new { x.CompanyId, x.ScopeType, x.ScopeValue, x.IsActive });
    }
}
