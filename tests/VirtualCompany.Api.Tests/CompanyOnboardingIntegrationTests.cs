using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Auth;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class CompanyOnboardingIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public CompanyOnboardingIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task GetTemplates_returns_seeded_catalog()
    {
        using var client = CreateAuthenticatedClient("templates-user", "templates@example.com", "Templates User");
        var templates = await client.GetFromJsonAsync<List<TemplateResponse>>("/api/onboarding/templates");

        Assert.NotNull(templates);
        Assert.NotEmpty(templates!);
        Assert.Contains(templates, x => x.TemplateId == "professional-services-agency");
        Assert.Contains(templates, x => x.TemplateId == "field-service-default");
    }

    [Fact]
    public async Task GetTemplates_returns_generic_defaults_and_metadata_for_extensible_templates()
    {
        using var client = CreateAuthenticatedClient("template-payload-user", "template-payload@example.com", "Template Payload User");
        var templates = await client.GetFromJsonAsync<List<TemplateResponse>>("/api/onboarding/templates");

        Assert.NotNull(templates);
        var template = templates!.Single(x => x.TemplateId == "field-service-default");
        Assert.Equal("Operations", template.Category);
        Assert.True(template.Defaults.TryGetValue("workWeekStartsOn", out var workWeekStartsOn));
        Assert.Equal("monday", workWeekStartsOn.GetString());
        Assert.True(template.Metadata.TryGetValue("brandingHint", out var brandingHint));
        Assert.Equal("dispatch", brandingHint.GetString());
        Assert.True(template.Metadata.TryGetValue("starterWorkflows", out var starterWorkflows));
        Assert.Equal(JsonValueKind.Array, starterWorkflows.ValueKind);
    }

    [Fact]
    public async Task RecommendedDefaults_prefers_exact_match_over_fallbacks()
    {
        using var client = CreateAuthenticatedClient("recommendation-user", "recommendation@example.com", "Recommendation User");
        var recommendation = await client.GetFromJsonAsync<RecommendationResponse>("/api/onboarding/recommended-defaults?industry=Technology&businessType=Software%20Company");

        Assert.NotNull(recommendation);
        Assert.Equal("saas-operations", recommendation!.TemplateId);
        Assert.Equal("industry_business_type", recommendation.MatchKind);
        Assert.Equal("America/New_York", recommendation.Defaults.Timezone);
    }

    [Fact]
    public async Task RecommendedDefaults_falls_back_to_industry_match()
    {
        using var client = CreateAuthenticatedClient("industry-recommendation-user", "industry-recommendation@example.com", "Industry Recommendation User");
        var recommendation = await client.GetFromJsonAsync<RecommendationResponse>("/api/onboarding/recommended-defaults?industry=Technology&businessType=Consultancy");

        Assert.NotNull(recommendation);
        Assert.Equal("technology-default", recommendation!.TemplateId);
        Assert.Equal("industry", recommendation.MatchKind);
        Assert.Equal("America/Los_Angeles", recommendation.Defaults.Timezone);
    }

    [Fact]
    public async Task RecommendedDefaults_falls_back_to_business_type_match()
    {
        using var client = CreateAuthenticatedClient("business-type-recommendation-user", "business-type-recommendation@example.com", "Business Type Recommendation User");
        var recommendation = await client.GetFromJsonAsync<RecommendationResponse>("/api/onboarding/recommended-defaults?industry=Education&businessType=Clinic");

        Assert.NotNull(recommendation);
        Assert.Equal("clinic-default", recommendation!.TemplateId);
        Assert.Equal("business_type", recommendation.MatchKind);
        Assert.Equal("America/Denver", recommendation.Defaults.Timezone);
    }

    [Fact]
    public async Task RecommendedDefaults_returns_null_when_no_template_matches()
    {
        using var client = CreateAuthenticatedClient("no-match-user", "no-match@example.com", "No Match User");
        var recommendation = await client.GetFromJsonAsync<RecommendationResponse?>("/api/onboarding/recommended-defaults?industry=Manufacturing&businessType=Distributor");

        Assert.Null(recommendation);
    }

    [Fact]
    public async Task CreateCompany_creates_company_and_owner_membership()
    {
        using var client = CreateAuthenticatedClient("alice", "alice@example.com", "Alice");
        var response = await client.PostAsJsonAsync("/api/onboarding/company", new
        {
            Name = "Northwind Ops",
            Industry = "Technology",
            BusinessType = "Software Company",
            Timezone = "Europe/Stockholm",
            Currency = "SEK",
            Language = "sv-SE",
            ComplianceRegion = "EU",
            SelectedTemplateId = "saas-operations"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ProgressResponse>();
        Assert.NotNull(payload);
        Assert.NotNull(payload!.CompanyId);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var company = await dbContext.Companies.SingleAsync(x => x.Id == payload.CompanyId!.Value);
        var memberships = await dbContext.CompanyMemberships.Where(x => x.CompanyId == company.Id).ToListAsync();
        var user = await dbContext.Users.SingleAsync(x => x.AuthSubject == "alice");
        var membership = await dbContext.CompanyMemberships.SingleAsync(x => x.CompanyId == company.Id && x.UserId == user.Id);

        Assert.Equal("Northwind Ops", company.Name);
        Assert.Equal("Technology", company.Industry);
        Assert.Equal("Software Company", company.BusinessType);
        Assert.Contains("&welcome=onboarding", payload.DashboardPath);
        Assert.Single(memberships);
        Assert.Equal(CompanyMembershipRole.Owner, membership.Role);
        Assert.Equal(CompanyMembershipStatus.Active, membership.Status);
    }

    [Fact]
    public async Task CreateCompany_seeds_laura_finance_agent_and_exposes_versioned_configuration()
    {
        using var client = CreateAuthenticatedClient("laura-owner", "laura-owner@example.com", "Laura Owner");
        var response = await client.PostAsJsonAsync("/api/onboarding/company", new
        {
            Name = "Finance Agent Co",
            Industry = "Technology",
            BusinessType = "Software Company",
            Timezone = "Europe/Stockholm",
            Currency = "SEK",
            Language = "sv-SE",
            ComplianceRegion = "EU",
            SelectedTemplateId = "saas-operations"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<ProgressResponse>();
        Assert.NotNull(created);
        var companyId = created!.CompanyId!.Value;

        var roster = await client.GetFromJsonAsync<AgentRosterResponse>($"/api/companies/{companyId}/agents/roster?department=Finance");
        Assert.NotNull(roster);
        var lauraRosterItem = Assert.Single(roster!.Items, item => item.DisplayName == "Laura");

        Assert.Equal("laura-finance-agent", lauraRosterItem.TemplateId);
        Assert.Equal("Finance Agent", lauraRosterItem.RoleName);
        Assert.Equal("Finance", lauraRosterItem.Department);
        Assert.Equal("active", lauraRosterItem.Status);
        Assert.Equal("finance", lauraRosterItem.RoleMetadata["roleKey"].GetString());
        Assert.Equal("finance", lauraRosterItem.RoleMetadata["responsibilityDomain"].GetString());
        Assert.Contains(
            lauraRosterItem.WorkflowCapabilities["defaults"].EnumerateArray(),
            capability => capability.GetString() == "finance_risk_detection");

        var profile = await client.GetFromJsonAsync<AgentProfileResponse>($"/api/companies/{companyId}/agents/{lauraRosterItem.Id}");
        Assert.NotNull(profile);
        Assert.Equal(1, profile!.Configuration.PersonaVersion);
        Assert.Equal(1, profile.Configuration.WorkflowVersion);
        Assert.Equal("Laura", profile.DisplayName);
        Assert.Equal("Conservative finance agent focused on accounting accuracy, variance checks, cash visibility, and early risk detection.", profile.RoleBrief);

        var traits = profile.Configuration.Persona["traits"].EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Contains("conservative", traits);
        Assert.Contains("precise", traits);
        Assert.Equal("finance", profile.Configuration.Persona["roleMetadata"].GetProperty("roleKey").GetString());

        var objectives = profile.Objectives["primary"].EnumerateArray().Select(x => x.GetString()).ToArray();
        Assert.Contains("Maintain accurate finance records", objectives);
        Assert.Contains("Detect cash, invoice, and transaction risks early", objectives);
        Assert.True(profile.Objectives["accuracy"].GetProperty("required").GetBoolean());
        Assert.True(profile.Objectives["riskDetection"].GetProperty("required").GetBoolean());

        Assert.Equal(
            new[]
            {
                "get_cash_balance",
                "list_transactions",
                "list_uncategorized_transactions",
                "list_invoices_awaiting_approval",
                "get_profit_and_loss_summary",
                "recommend_transaction_category",
                "recommend_invoice_approval_decision",
                "categorize_transaction",
                "approve_invoice"
            },
            profile.ToolPermissions["allowed"].EnumerateArray().Select(x => x.GetString()));
        Assert.DoesNotContain("erp", profile.ToolPermissions["allowed"].EnumerateArray().Select(x => x.GetString()));
        Assert.Contains("erp", profile.ToolPermissions["denied"].EnumerateArray().Select(x => x.GetString()));
        Assert.Equal("finance", Assert.Single(profile.DataScopes["read"].EnumerateArray()).GetString());
        Assert.Equal("finance", Assert.Single(profile.DataScopes["recommend"].EnumerateArray()).GetString());
        Assert.Equal("finance", Assert.Single(profile.DataScopes["execute"].EnumerateArray()).GetString());
        Assert.Empty(profile.DataScopes["write"].EnumerateArray());

        Assert.Contains(
            profile.Configuration.WorkflowCapabilities["defaults"].EnumerateArray(),
            capability => capability.GetString() == "cash_balance_review");
        Assert.Contains(
            profile.Configuration.WorkflowCapabilities["defaults"].EnumerateArray(),
            capability => capability.GetString() == "finance_risk_detection");
        Assert.Equal("finance", profile.Configuration.WorkflowCapabilities["financeBoundary"].GetString());
    }

    [Fact]
    public async Task SaveProgress_and_resume_restore_latest_values()
    {
        using var client = CreateAuthenticatedClient("resume-user", "resume@example.com", "Resume User");
        var createResponse = await client.PostAsJsonAsync("/api/onboarding/workspace", new
        {
            Name = "Resume Co",
            Industry = "Professional Services",
            BusinessType = "Agency",
            Timezone = "Europe/Stockholm",
            Currency = "SEK",
            Language = "sv-SE",
            Branding = new
            {
                LogoUrl = "https://cdn.example.com/logo.png",
                PrimaryColor = "#112233",
                SecondaryColor = "#445566",
                Theme = "nordic"
            },
            Settings = new
            {
                Locale = "sv-SE",
                FeatureFlags = new
                {
                    onboardingResume = true
                }
            },
            ComplianceRegion = "EU",
            CurrentStep = 1,
            SelectedTemplateId = "professional-services-agency"
        });

        var created = await createResponse.Content.ReadFromJsonAsync<ProgressResponse>();
        Assert.NotNull(created);

        var saveResponse = await client.PutAsJsonAsync("/api/onboarding/progress", new
        {
            CompanyId = created!.CompanyId,
            Name = "Resume Co",
            Industry = "Professional Services",
            BusinessType = "Agency",
            Timezone = "Europe/Stockholm",
            Currency = "SEK",
            Language = "sv-SE",
            ComplianceRegion = "EU",
            CurrentStep = 3,
            SelectedTemplateId = "professional-services-agency"
        });

        Assert.Equal(HttpStatusCode.OK, saveResponse.StatusCode);

        var progress = await client.GetFromJsonAsync<ProgressResponse>("/api/onboarding/progress");
        Assert.NotNull(progress);
        Assert.NotNull(progress!.Branding);
        Assert.Equal("https://cdn.example.com/logo.png", progress.Branding!.LogoUrl);
        Assert.Equal("#112233", progress.Branding.PrimaryColor);
        Assert.NotNull(progress.Settings);
        Assert.Equal("sv-SE", progress.Settings!.Locale);
        Assert.NotNull(progress.Settings.Onboarding);
        Assert.Equal(3, progress.Settings.Onboarding!.CurrentStep);
        Assert.Equal(3, progress!.CurrentStep);
        Assert.Equal("professional-services-agency", progress.SelectedTemplateId);
        Assert.Equal("Resume Co", progress.Name);

        using var scope = _factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
        var company = await dbContext.Companies.SingleAsync(x => x.Id == progress.CompanyId);
        Assert.Equal("#445566", company.Branding.SecondaryColor);
        Assert.True(company.Settings.FeatureFlags["onboardingResume"]);
        Assert.Equal(3, company.Settings.Onboarding.CurrentStep);
    }

    [Fact]
    public async Task CreateWorkspace_persists_generic_template_snapshot_in_settings_extensions()
    {
        using var client = CreateAuthenticatedClient("template-settings-user", "template-settings@example.com", "Template Settings User");
        var response = await client.PostAsJsonAsync("/api/onboarding/workspace", new
        {
            Name = "Field Ops Co",
            Industry = "Services",
            BusinessType = "Field Service",
            Timezone = "",
            Currency = "",
            Language = "",
            ComplianceRegion = "",
            CurrentStep = 1,
            SelectedTemplateId = "field-service-default"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ProgressResponse>();
        Assert.NotNull(payload);
        Assert.NotNull(payload!.Settings);
        Assert.True(payload.Settings!.Extensions.TryGetProperty("companySetupTemplate", out var templateSnapshot));
        Assert.Equal("field-service-default", templateSnapshot.GetProperty("templateId").GetString());
        Assert.Equal("monday", templateSnapshot.GetProperty("defaults").GetProperty("workWeekStartsOn").GetString());
        Assert.Equal("dispatch", templateSnapshot.GetProperty("metadata").GetProperty("brandingHint").GetString());
        Assert.Equal("America/Chicago", payload.Timezone);
        Assert.Equal("USD", payload.Currency);
        Assert.Equal("US", payload.ComplianceRegion);
    }

    [Fact]
    public async Task CreateWorkspace_allows_incomplete_draft_values()
    {
        using var client = CreateAuthenticatedClient("draft-user", "draft@example.com", "Draft User");
        var response = await client.PostAsJsonAsync("/api/onboarding/workspace", new
        {
            Name = "Draft Co",
            Industry = "",
            BusinessType = "",
            Timezone = "",
            Currency = "",
            Language = "",
            ComplianceRegion = "",
            CurrentStep = 1,
            SelectedTemplateId = (string?)null
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<ProgressResponse>();
        Assert.NotNull(payload);
        Assert.Equal("in_progress", payload!.Status);
        Assert.True(payload.CanResume);
        Assert.Equal("Draft Co", payload.Name);
    }

    [Fact]
    public async Task CreateCompany_rejects_blank_required_fields()
    {
        using var client = CreateAuthenticatedClient("validation-user", "validation@example.com", "Validation User");
        var response = await client.PostAsJsonAsync("/api/onboarding/company", new
        {
            Name = "   ",
            Industry = "  ",
            BusinessType = "",
            Timezone = " ",
            Currency = "",
            Language = "   ",
            ComplianceRegion = ""
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var payload = await response.Content.ReadFromJsonAsync<ValidationProblemDetails>();
        Assert.NotNull(payload);
        Assert.Contains("Name", payload!.Errors.Keys);
        Assert.Contains("Industry", payload.Errors.Keys);
        Assert.Contains("BusinessType", payload.Errors.Keys);
        Assert.Contains("Timezone", payload.Errors.Keys);
        Assert.Contains("Currency", payload.Errors.Keys);
        Assert.Contains("Language", payload.Errors.Keys);
        Assert.Contains("ComplianceRegion", payload.Errors.Keys);
    }

    [Fact]
    public async Task CreateCompany_requires_authenticated_user()
    {
        using var client = _factory.CreateClient();
        var response = await client.PostAsJsonAsync("/api/onboarding/company", new
        {
            Name = "Unauthenticated Co",
            Industry = "Technology",
            BusinessType = "Software Company",
            Timezone = "Europe/Stockholm",
            Currency = "SEK",
            Language = "sv-SE",
            ComplianceRegion = "EU"
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CompleteOnboarding_marks_company_complete_and_clears_resume_state()
    {
        using var client = CreateAuthenticatedClient("complete-user", "complete@example.com", "Complete User");
        var createResponse = await client.PostAsJsonAsync("/api/onboarding/workspace", new
        {
            Name = "Complete Co",
            Industry = "Healthcare",
            BusinessType = "Clinic",
            Timezone = "America/Chicago",
            Currency = "USD",
            Language = "en-US",
            ComplianceRegion = "HIPAA",
            CurrentStep = 2,
            SelectedTemplateId = "healthcare-clinic"
        });

        var created = await createResponse.Content.ReadFromJsonAsync<ProgressResponse>();
        Assert.NotNull(created);

        var completeResponse = await client.PostAsJsonAsync("/api/onboarding/complete", new
        {
            CompanyId = created!.CompanyId,
            Name = "Complete Co",
            Industry = "Healthcare",
            BusinessType = "Clinic",
            Timezone = "America/Chicago",
            Currency = "USD",
            Language = "en-US",
            ComplianceRegion = "HIPAA",
            SelectedTemplateId = "healthcare-clinic"
        });

        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        var result = await completeResponse.Content.ReadFromJsonAsync<CompleteResponse>();
        Assert.NotNull(result);
        Assert.Contains("/dashboard?companyId=", result!.DashboardPath);
        Assert.Contains("&welcome=onboarding", result.DashboardPath);

        using (var scope = _factory.Services.CreateScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var company = await dbContext.Companies.SingleAsync(x => x.Id == created.CompanyId);
            Assert.NotNull(company.OnboardingCompletedUtc);
        }

        var progressAfterComplete = await client.GetFromJsonAsync<ProgressResponse>("/api/onboarding/progress");
        Assert.NotNull(progressAfterComplete);
        Assert.Equal("completed", progressAfterComplete!.Status);
        Assert.False(progressAfterComplete.CanResume);
    }

    [Fact]
    public async Task DashboardEntry_returns_starter_guidance_for_newly_completed_workspace()
    {
        using var client = CreateAuthenticatedClient("dashboard-entry-user", "dashboard-entry@example.com", "Dashboard Entry User");
        var createResponse = await client.PostAsJsonAsync("/api/onboarding/workspace", new
        {
            Name = "Starter Co",
            Industry = "Technology",
            BusinessType = "Software Company",
            Timezone = "Europe/Stockholm",
            Currency = "SEK",
            Language = "sv-SE",
            ComplianceRegion = "EU",
            CurrentStep = 3,
            SelectedTemplateId = "saas-operations"
        });

        var created = await createResponse.Content.ReadFromJsonAsync<ProgressResponse>();
        Assert.NotNull(created);

        var completeResponse = await client.PostAsJsonAsync("/api/onboarding/complete", new
        {
            CompanyId = created!.CompanyId,
            Name = "Starter Co",
            Industry = "Technology",
            BusinessType = "Software Company",
            Timezone = "Europe/Stockholm",
            Currency = "SEK",
            Language = "sv-SE",
            ComplianceRegion = "EU",
            SelectedTemplateId = "saas-operations"
        });

        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);

        var dashboardEntry = await client.GetFromJsonAsync<DashboardEntryResponse>($"/api/companies/{created.CompanyId}/dashboard-entry");
        Assert.NotNull(dashboardEntry);
        Assert.True(dashboardEntry!.ShowStarterGuidance);
        Assert.NotEmpty(dashboardEntry.StarterGuidance);
        Assert.False(dashboardEntry.RequiresOnboarding);
    }

    [Fact]
    public async Task DashboardEntry_flags_incomplete_workspace_as_requiring_onboarding()
    {
        using var client = CreateAuthenticatedClient("dashboard-gate-user", "dashboard-gate@example.com", "Dashboard Gate User");
        var createResponse = await client.PostAsJsonAsync("/api/onboarding/workspace", new
        {
            Name = "Incomplete Co",
            Industry = "Technology",
            BusinessType = "Software Company",
            Timezone = "Europe/Stockholm",
            Currency = "SEK",
            Language = "sv-SE",
            ComplianceRegion = "EU",
            CurrentStep = 2,
            SelectedTemplateId = "saas-operations"
        });

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        var created = await createResponse.Content.ReadFromJsonAsync<ProgressResponse>();
        Assert.NotNull(created);

        var dashboardEntry = await client.GetFromJsonAsync<DashboardEntryResponse>($"/api/companies/{created!.CompanyId}/dashboard-entry");

        Assert.NotNull(dashboardEntry);
        Assert.True(dashboardEntry!.RequiresOnboarding);
        Assert.False(dashboardEntry.ShowStarterGuidance);
    }

    [Fact]
    public async Task Progress_is_scoped_to_creator()
    {
        using var aliceClient = CreateAuthenticatedClient("owner-user", "owner@example.com", "Owner User");
        using var bobClient = CreateAuthenticatedClient("other-user", "other@example.com", "Other User");

        var createResponse = await aliceClient.PostAsJsonAsync("/api/onboarding/workspace", new
        {
            Name = "Owner Co",
            Industry = "Technology",
            BusinessType = "Software Company",
            Timezone = "America/New_York",
            Currency = "USD",
            Language = "en-US",
            ComplianceRegion = "US",
            CurrentStep = 1,
            SelectedTemplateId = "saas-operations"
        });

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);

        var bobsProgress = await bobClient.GetFromJsonAsync<ProgressResponse?>("/api/onboarding/progress");
        Assert.Null(bobsProgress);
    }

    [Fact]
    public async Task SaveProgress_blocks_other_user_from_mutating_draft()
    {
        using var aliceClient = CreateAuthenticatedClient("owner-edit-user", "owner-edit@example.com", "Owner Edit User");
        using var bobClient = CreateAuthenticatedClient("mutating-user", "mutating@example.com", "Mutating User");

        var createResponse = await aliceClient.PostAsJsonAsync("/api/onboarding/workspace", new
        {
            Name = "Private Co",
            Industry = "Technology",
            BusinessType = "Software Company",
            Timezone = "Europe/Stockholm",
            Currency = "SEK",
            Language = "sv-SE",
            ComplianceRegion = "EU",
            CurrentStep = 2,
            SelectedTemplateId = "saas-operations"
        });

        var created = await createResponse.Content.ReadFromJsonAsync<ProgressResponse>();
        Assert.NotNull(created);

        var saveResponse = await bobClient.PutAsJsonAsync("/api/onboarding/progress", new
        {
            CompanyId = created!.CompanyId,
            Name = "Private Co",
            Industry = "Technology",
            BusinessType = "Software Company",
            Timezone = "Europe/Stockholm",
            Currency = "SEK",
            Language = "sv-SE",
            ComplianceRegion = "EU",
            CurrentStep = 3,
            SelectedTemplateId = "saas-operations"
        });

        Assert.Equal(HttpStatusCode.Forbidden, saveResponse.StatusCode);
    }

    [Fact]
    public async Task Abandoning_progress_marks_session_discarded_and_disables_resume()
    {
        using var client = CreateAuthenticatedClient("abandon-user", "abandon@example.com", "Abandon User");
        var createResponse = await client.PostAsJsonAsync("/api/onboarding/workspace", new
        {
            Name = "Abandon Co",
            Industry = "Retail",
            BusinessType = "Store",
            Timezone = "Europe/Stockholm",
            Currency = "SEK",
            Language = "sv-SE",
            ComplianceRegion = "EU",
            CurrentStep = 2,
            SelectedTemplateId = (string?)null
        });

        var created = await createResponse.Content.ReadFromJsonAsync<ProgressResponse>();
        var abandonResponse = await client.PostAsJsonAsync("/api/onboarding/abandon", new { CompanyId = created!.CompanyId });
        Assert.Equal(HttpStatusCode.OK, abandonResponse.StatusCode);

        var progress = await client.GetFromJsonAsync<ProgressResponse>("/api/onboarding/progress");
        Assert.NotNull(progress);
        Assert.Equal("abandoned", progress!.Status);
        Assert.False(progress.CanResume);
    }

    private HttpClient CreateAuthenticatedClient(string subject, string email, string displayName, string? provider = null)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.SubjectHeader, subject);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.EmailHeader, email);
        client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.DisplayNameHeader, displayName);
        if (!string.IsNullOrWhiteSpace(provider))
        {
            client.DefaultRequestHeaders.Add(DevHeaderAuthenticationDefaults.ProviderHeader, provider);
        }

        return client;
    }

    private sealed class TemplateResponse
    {
        public string TemplateId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Category { get; set; }
        public Dictionary<string, JsonElement> Defaults { get; set; } = new();
        public Dictionary<string, JsonElement> Metadata { get; set; } = new();
        public string? Industry { get; set; }
        public string? BusinessType { get; set; }
    }

    private sealed class AgentRosterResponse
    {
        public List<AgentRosterItem> Items { get; set; } = [];
    }

    private sealed class AgentRosterItem
    {
        public Guid Id { get; set; }
        public string TemplateId { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string RoleName { get; set; } = string.Empty;
        public string Department { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public Dictionary<string, JsonElement> RoleMetadata { get; set; } = [];
        public Dictionary<string, JsonElement> WorkflowCapabilities { get; set; } = [];
    }

    private sealed class AgentProfileResponse
    {
        public Guid Id { get; set; }
        public string DisplayName { get; set; } = string.Empty;
        public string? RoleBrief { get; set; }
        public AgentConfigurationResponse Configuration { get; set; } = new();
        public Dictionary<string, JsonElement> Objectives { get; set; } = [];
        public Dictionary<string, JsonElement> ToolPermissions { get; set; } = [];
        public Dictionary<string, JsonElement> DataScopes { get; set; } = [];
    }

    private sealed class AgentConfigurationResponse
    {
        public int PersonaVersion { get; set; }
        public int WorkflowVersion { get; set; }
        public Dictionary<string, JsonElement> Persona { get; set; } = [];
        public Dictionary<string, JsonElement> WorkflowCapabilities { get; set; } = [];
    }

    private sealed class RecommendationResponse
    {
        public string TemplateId { get; set; } = string.Empty;
        public string MatchKind { get; set; } = string.Empty;
        public RecommendationDefaultsResponse Defaults { get; set; } = new();
    }

    private sealed class RecommendationDefaultsResponse
    {
        public string? Timezone { get; set; }
        public string? Currency { get; set; }
        public string? Language { get; set; }
        public string? ComplianceRegion { get; set; }
    }

    private sealed class ProgressResponse
    {
        public Guid? CompanyId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Industry { get; set; } = string.Empty;
        public string BusinessType { get; set; } = string.Empty;
        public string Timezone { get; set; } = string.Empty;
        public string Currency { get; set; } = string.Empty;
        public string Language { get; set; } = string.Empty;
        public string ComplianceRegion { get; set; } = string.Empty;
        public int CurrentStep { get; set; }
        public string? SelectedTemplateId { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool IsCompleted { get; set; }
        public bool CanResume { get; set; }
        public DateTime? LastSavedUtc { get; set; }
        public DateTime? CompletedUtc { get; set; }
        public DateTime? AbandonedUtc { get; set; }
        public string? DashboardPath { get; set; }
        public BrandingResponse? Branding { get; set; }
        public SettingsResponse? Settings { get; set; }
    }

    private sealed class BrandingResponse
    {
        public string? LogoUrl { get; set; }
        public string? PrimaryColor { get; set; }
        public string? SecondaryColor { get; set; }
        public string? Theme { get; set; }
    }

    private sealed class SettingsResponse
    {
        public string? Locale { get; set; }
        public string? TemplateId { get; set; }
        public OnboardingSettingsResponse? Onboarding { get; set; }
        public JsonElement Extensions { get; set; }
        public JsonElement FeatureFlags { get; set; }
    }

    private sealed class CompleteResponse
    {
        public Guid CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public string DashboardPath { get; set; } = string.Empty;
        public List<string> StarterGuidance { get; set; } = [];
    }

    private sealed class DashboardEntryResponse
    {
        public Guid CompanyId { get; set; }
        public string CompanyName { get; set; } = string.Empty;
        public bool RequiresOnboarding { get; set; }
        public bool ShowStarterGuidance { get; set; }
        public DateTime? OnboardingCompletedUtc { get; set; }
        public List<string> StarterGuidance { get; set; } = [];
    }

    private sealed class OnboardingSettingsResponse
    {
        public string? Name { get; set; }
        public string? Industry { get; set; }
        public string? BusinessType { get; set; }
        public string? Timezone { get; set; }
        public string? Currency { get; set; }
        public string? Language { get; set; }
        public string? ComplianceRegion { get; set; }
        public int? CurrentStep { get; set; }
        public string? SelectedTemplateId { get; set; }
        public bool IsCompleted { get; set; }
        public List<string> StarterGuidance { get; set; } = [];
        public JsonElement Extensions { get; set; }
    }
}
