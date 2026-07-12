using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Sales;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Api.Tests;

public sealed class WebsiteLeadCaptureIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public WebsiteLeadCaptureIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Public_submission_creates_lead_and_sequence_enrollment()
    {
        var seed = await SeedAsync();
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/public/website-leads", ValidRequest(seed.FormKey, "Buyer@Example.com"));

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<WebsiteLeadSubmissionResponse>();
        Assert.NotNull(result);
        Assert.NotNull(result!.LeadId);
        Assert.True(result.EnrollmentAccepted);
        Assert.Equal(seed.SequenceId, result.SequenceId);

        var state = await _factory.ExecuteDbContextAsync(async dbContext => new
        {
            LeadCount = await dbContext.Leads.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId && x.WebsiteSubmissionEmail == "buyer@example.com"),
            SubmissionCount = await dbContext.WebsiteLeadSubmissions.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId),
            ExecutionCount = await dbContext.SalesSequenceExecutions.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId),
            StepCount = await dbContext.SalesSequenceExecutionSteps.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId),
            AuditCreated = await dbContext.AuditEvents.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == seed.CompanyId && x.Action == "sales.website_lead.submitted"),
            AuditEnrolled = await dbContext.AuditEvents.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == seed.CompanyId && x.Action == "sales.website_lead.enrolled"),
            SourceTouches = await dbContext.SalesSourceTouches.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId && x.Category == SalesSourceCategories.Website),
            Permissions = await dbContext.SalesContactPermissions.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId && x.Status == "granted")
        });

        Assert.Equal(1, state.LeadCount);
        Assert.Equal(1, state.SubmissionCount);
        Assert.Equal(1, state.ExecutionCount);
        Assert.Equal(4, state.StepCount);
        Assert.True(state.AuditCreated);
        Assert.True(state.AuditEnrolled);
        Assert.Equal(1, state.SourceTouches);
        Assert.Equal(1, state.Permissions);
    }

    [Fact]
    public async Task Invalid_payload_returns_validation_problem_without_persisting()
    {
        var seed = await SeedAsync();
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/public/website-leads", ValidRequest(seed.FormKey, "not-an-email"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await _factory.ExecuteDbContextAsync(dbContext => dbContext.WebsiteLeadSubmissions.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId)));
    }

    [Fact]
    public async Task Unknown_form_key_returns_safe_validation_problem()
    {
        using var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/api/public/website-leads", ValidRequest("missing-form-key", "buyer@example.com"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Duplicate_email_inside_window_updates_existing_lead_and_keeps_enrollment_idempotent()
    {
        var seed = await SeedAsync();
        using var client = _factory.CreateClient();

        var first = await client.PostAsJsonAsync("/api/public/website-leads", ValidRequest(seed.FormKey, "buyer@example.com", "first"));
        var second = await client.PostAsJsonAsync("/api/public/website-leads", ValidRequest(seed.FormKey, " BUYER@example.com ", "second"));

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        var firstResult = await first.Content.ReadFromJsonAsync<WebsiteLeadSubmissionResponse>();
        var secondResult = await second.Content.ReadFromJsonAsync<WebsiteLeadSubmissionResponse>();
        Assert.Equal(firstResult!.LeadId, secondResult!.LeadId);
        Assert.True(secondResult.Deduplicated);

        var state = await _factory.ExecuteDbContextAsync(async dbContext => new
        {
            LeadCount = await dbContext.Leads.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId && x.WebsiteSubmissionEmail == "buyer@example.com"),
            SubmissionCount = await dbContext.WebsiteLeadSubmissions.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId),
            MergedCount = await dbContext.WebsiteLeadSubmissions.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId && x.Status == "merged"),
            ExecutionCount = await dbContext.SalesSequenceExecutions.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId)
        });

        Assert.Equal(1, state.LeadCount);
        Assert.Equal(2, state.SubmissionCount);
        Assert.Equal(1, state.MergedCount);
        Assert.Equal(1, state.ExecutionCount);
    }

    [Fact]
    public async Task External_submission_id_is_idempotent()
    {
        var seed = await SeedAsync();
        using var client = _factory.CreateClient();

        var first = await client.PostAsJsonAsync("/api/public/website-leads", ValidRequest(seed.FormKey, "buyer@example.com", externalSubmissionId: "form-123"));
        var second = await client.PostAsJsonAsync("/api/public/website-leads", ValidRequest(seed.FormKey, "buyer@example.com", externalSubmissionId: "form-123"));

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, second.StatusCode);
        Assert.Equal(1, await _factory.ExecuteDbContextAsync(dbContext => dbContext.WebsiteLeadSubmissions.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId)));
    }

    [Fact]
    public async Task Duplicate_submissions_do_not_create_two_active_leads()
    {
        var seed = await SeedAsync();
        using var client = _factory.CreateClient();

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/api/public/website-leads", ValidRequest(seed.FormKey, "race@example.com")),
            client.PostAsJsonAsync("/api/public/website-leads", ValidRequest(seed.FormKey, " RACE@example.com ")));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
        Assert.Equal(1, await _factory.ExecuteDbContextAsync(dbContext =>
            dbContext.Leads.IgnoreQueryFilters().CountAsync(x => x.CompanyId == seed.CompanyId && x.WebsiteSubmissionEmail == "race@example.com" && !x.IsDeleted)));
    }

    private async Task<Seed> SeedAsync()
    {
        var companyId = Guid.NewGuid();
        var sequenceId = Guid.NewGuid();
        var formKey = $"wlf_{Guid.NewGuid():N}";

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Companies.Add(new Company(companyId, "Website Lead Company"));
            var sequence = new SalesSequence(sequenceId, companyId, "Website lead follow-up", SalesStatuses.Active);
            sequence.Steps.Add(new SalesSequenceStep(Guid.NewGuid(), companyId, sequenceId, 1, 0, "Thanks for reaching out. Alex will follow up shortly.", templateSubject: "Thanks for your enquiry"));
            sequence.Steps.Add(new SalesSequenceStep(Guid.NewGuid(), companyId, sequenceId, 2, 1, "Sharing a short follow-up with next steps.", templateSubject: "Next steps"));
            sequence.Steps.Add(new SalesSequenceStep(Guid.NewGuid(), companyId, sequenceId, 3, 3, "Checking whether this is still useful.", templateSubject: "Checking in"));
            sequence.Steps.Add(new SalesSequenceStep(Guid.NewGuid(), companyId, sequenceId, 4, 7, "Final follow-up from Alex.", templateSubject: "Final follow-up"));
            dbContext.SalesSequences.Add(sequence);

            var policy = new SalesAutomationPolicy(Guid.NewGuid(), companyId, "assistive");
            policy.UpdateOutboundSettings(
                outboundEnabled: true,
                maxEmailsPerDay: 25,
                requireApprovalFirstContact: true,
                requireApprovalPricingDiscussion: true,
                requireApprovalFollowUps: true,
                requireApprovalReEngagement: true,
                websiteLeadDeduplicationWindowMinutes: 60,
                websiteLeadFollowUpSequenceId: sequenceId);
            typeof(SalesAutomationPolicy).GetProperty(nameof(SalesAutomationPolicy.WebsiteLeadFormKey))!.SetValue(policy, formKey);
            dbContext.SalesAutomationPolicies.Add(policy);
            return Task.CompletedTask;
        });

        return new Seed(companyId, formKey, sequenceId);
    }

    private static WebsiteLeadSubmissionRequest ValidRequest(string formKey, string email, string? message = null, string? externalSubmissionId = null) =>
        new(
            formKey,
            email,
            "Buyer Example",
            "Buyer Company",
            message ?? "I want to talk to sales.",
            "https://example.com/contact",
            "contact-us",
            Phone: "+1 555 0100",
            ExternalSubmissionId: externalSubmissionId,
            Utm: new Dictionary<string, string?> { ["source"] = "website" },
            ContactConsent: true,
            ConsentLegalBasis: "consent");

    private sealed record Seed(Guid CompanyId, string FormKey, Guid SequenceId);
}
