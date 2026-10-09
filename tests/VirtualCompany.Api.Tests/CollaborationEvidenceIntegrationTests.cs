using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class CollaborationEvidenceIntegrationTests
{
    [Fact]
    public async Task Versioned_inputs_failures_challenges_and_current_decision_reconcile_without_duplicate_outcomes()
    {
        using var factory = new TestWebApplicationFactory(); var company = Guid.NewGuid(); var owner = Guid.NewGuid();
        await SeedOwner(factory, company, owner);
        var fixture = await CollaborationEvidenceFixture.SeedAsync(factory, company, owner);
        using var client = Client(factory);
        var result = (await client.GetFromJsonAsync<CollaborationEvidenceDto>(Route(company, fixture.RootId)))!;
        Assert.Equal("awaiting_approval", result.OutcomeState); Assert.Equal(6, result.Artifacts.Count);
        Assert.Equal(4, result.Artifacts.DistinctBy(x => (x.ParentTaskId, x.Sequence)).Count());
        var proposal = result.Artifacts.Single(x => x.Id == fixture.ProposalArtifactId);
        Assert.Equal(2, proposal.Version); Assert.Equal(2, proposal.InputIds.Count);
        Assert.Contains(fixture.FinanceArtifactId, proposal.InputIds);
        Assert.Equal("needs_review", proposal.State); Assert.Null(proposal.ReviewOutcome);
        Assert.Contains(result.Artifacts, x => x.State == "failed" && string.IsNullOrEmpty(x.Output));
        Assert.Contains(result.Artifacts, x => x.Role == "challenger" && x.ReviewOutcome!.Contains("12%"));
        Assert.Contains(result.Handoffs, x => !x.Passed && x.Reason!.Contains("human decision"));
        Assert.Contains(result.RelatedRecords, x => x.Label.Contains("approval", StringComparison.OrdinalIgnoreCase) && x.Route.Contains("&itemId="));
        var direct = (await client.GetFromJsonAsync<CollaborationEvidenceDto>(Route(company, fixture.RevisionId)))!;
        Assert.Equal(result.Artifacts.Select(x => x.Id), direct.Artifacts.Select(x => x.Id));
        var again = (await client.GetFromJsonAsync<CollaborationEvidenceDto>(Route(company, fixture.RootId)))!;
        Assert.Equal(result.Artifacts.Select(x => x.Id), again.Artifacts.Select(x => x.Id));
        Assert.False(result.HasRestrictedEvidence); Assert.False(result.IsPartial);
    }

    [Fact]
    public async Task Company_and_responsibility_denial_do_not_disclose_artifacts_or_derived_Work_payloads()
    {
        using var factory = new TestWebApplicationFactory(); var company = Guid.NewGuid(); var owner = Guid.NewGuid();
        await SeedOwner(factory, company, owner);
        var fixture = await CollaborationEvidenceFixture.SeedAsync(factory, company, owner);
        var member = Guid.NewGuid();
        await factory.SeedAsync(db => { db.Users.Add(new User(member,"p11-member@example.com","Member","dev-header","p11-member"));
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(),company,member,CompanyMembershipRole.Employee,CompanyMembershipStatus.Active)); return Task.CompletedTask; });
        using var denied = Client(factory, "p11-member");
        Assert.Equal(HttpStatusCode.NotFound, (await denied.GetAsync(Route(company, fixture.RootId))).StatusCode);
        using var allowed = Client(factory);
        Assert.Equal(HttpStatusCode.Forbidden, (await allowed.GetAsync(Route(Guid.NewGuid(),fixture.RootId))).StatusCode);
    }

    internal static async Task SeedOwner(TestWebApplicationFactory factory, Guid company, Guid owner) =>
        await factory.SeedAsync(db => { db.Users.Add(new User(owner,"p11-owner@example.com","P11 Owner","dev-header","p11-owner"));
            db.Companies.Add(new Company(company,"P11 Company")); db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(),company,owner,CompanyMembershipRole.Owner,CompanyMembershipStatus.Active)); return Task.CompletedTask; });
    internal static HttpClient Client(TestWebApplicationFactory factory, string subject = "p11-owner")
    { var client = factory.CreateClient(); client.DefaultRequestHeaders.Add("X-Dev-Auth-Subject",subject); client.DefaultRequestHeaders.Add("X-Dev-Auth-Email", subject + "@example.com"); return client; }
    private static string Route(Guid company, Guid id) => $"/api/companies/{company:D}/agent-work/task/{id:D}/collaboration";

    [Fact]
    public async Task Scoped_reader_cannot_open_restricted_inputs_or_derived_outputs_through_any_Work_entry()
    {
        using var factory = new TestWebApplicationFactory(); var company = Guid.NewGuid(); var owner = Guid.NewGuid();
        await SeedOwner(factory, company, owner); var fixture = await CollaborationEvidenceFixture.SeedAsync(factory, company, owner);
        var user = Guid.NewGuid(); var membership = Guid.NewGuid(); Guid hiddenFinanceAgent = Guid.Empty;
        await factory.SeedAsync(async db =>
        {
            db.Users.Add(new User(user,"p11-sales@example.com","Sales reader","dev-header","p11-sales"));
            db.CompanyMemberships.Add(new CompanyMembership(membership,company,user,CompanyMembershipRole.Manager,CompanyMembershipStatus.Active));
            db.CompanyResponsibilityAssignments.Add(new CompanyResponsibilityAssignment(Guid.NewGuid(),company,ResponsibilityArea.Sales,
                ResponsibilityAssignmentKind.Primary,membership,null,AgentAutonomyLevel.Level1,null,null));
            var task = await db.WorkTasks.IgnoreQueryFilters().SingleAsync(x => x.Id == fixture.RevisionId);
            hiddenFinanceAgent = await db.CollaborationContributions.IgnoreQueryFilters().Where(x => x.CompanyId == company && x.Id == fixture.FinanceArtifactId).Select(x => x.AgentId).SingleAsync();
            task.InputPayload["contributionInputs"] = System.Text.Json.Nodes.JsonValue.Create("PRIVATE margin");
            task.UpdateStatus(WorkTaskStatus.AwaitingApproval, new Dictionary<string, System.Text.Json.Nodes.JsonNode?> { ["finalResponse"] = System.Text.Json.Nodes.JsonValue.Create("PRIVATE margin") }, "PRIVATE margin");
        });
        using var client = Client(factory, "p11-sales");
        var response = await client.GetAsync(Route(company,fixture.RootId)); response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(); var evidence = (await response.Content.ReadFromJsonAsync<CollaborationEvidenceDto>())!;
        Assert.True(evidence.HasRestrictedEvidence); Assert.DoesNotContain(fixture.FinanceArtifactId.ToString(),json);
        Assert.DoesNotContain(fixture.ProposalArtifactId.ToString(),json); Assert.DoesNotContain("8%",json);
        Assert.All(evidence.Artifacts, x => Assert.Equal("P11 Alex",x.Agent.Name));
        var raw = await client.GetStringAsync($"/api/companies/{company}/tasks/{fixture.RevisionId}");
        Assert.DoesNotContain("PRIVATE", raw);
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync(Route(company,fixture.FinanceId))).StatusCode);
        var deniedCommand=new StartMultiAgentCollaborationCommand(company,"Unauthorized cross-department delegation",
            evidence.Artifacts[0].Agent.Id,[new(hiddenFinanceAgent,"Hidden finance")],CorrelationId:"p11-denied-delegation");
        Assert.Equal(HttpStatusCode.Forbidden,(await client.PostAsJsonAsync($"/api/companies/{company}/tasks/manager-worker-collaborations",deniedCommand)).StatusCode);
        await factory.SeedAsync(async db=>Assert.False(await db.CollaborationExecutionLeases.IgnoreQueryFilters().AnyAsync(x=>x.CompanyId==company && x.Key=="p11-denied-delegation")));
    }
}
