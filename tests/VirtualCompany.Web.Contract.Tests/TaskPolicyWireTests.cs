using VirtualCompany.Api.Tests;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Contract.Tests;

public sealed class TaskPolicyWireTests
{
    [Fact]
    public async Task Real_policy_client_preserves_catalogue_preview_limits_and_native_finance_history()
    {
        using var factory=new TestWebApplicationFactory();var company=Guid.NewGuid();var owner=Guid.NewGuid();
        await factory.SeedAsync(db=>{db.AddRange(new Company(company,"P18 typed policy wire"),new User(owner,"p18-wire@example.test","Wire","dev-header","p18-wire"),new CompanyMembership(Guid.NewGuid(),company,owner,CompanyMembershipRole.Owner,CompanyMembershipStatus.Active));return Task.CompletedTask;});
        var fixture=await AuthorityExplanationFixture.SeedAsync(factory,company,owner);
        using var http=factory.CreateClient();http.DefaultRequestHeaders.Add("X-Dev-Auth-Subject","p18-wire");
        var client=new TaskTypePolicyApiClient(new CompanyApiTransport(http));
        var catalogue=await client.Catalogue(company);Assert.Equal(5,catalogue.Count);
        var type=Assert.Single(catalogue,x=>x.Finance);Assert.Equal("finance.stale_cash_monitoring",type.Code);
        var before=await client.Get(company,fixture.AgentId,type.Code);
        var change=new TaskPolicyChange(fixture.AgentId,type.Code,"disabled",1,DateTime.UtcNow.AddDays(1),"Preserve native Finance grant bounds",before.Version);
        var preview=await client.Preview(company,change);Assert.True(preview.CanApply);Assert.Equal(change,preview.Change);Assert.NotEmpty(preview.Limits!);
        Assert.Equal(before.Version,(await client.Get(company,fixture.AgentId,type.Code)).Version);
        var applied=await client.Apply(company,new(change,preview.PreviewHash));Assert.Equal(company,applied.CompanyId);
        var retained=await client.Get(company,fixture.AgentId,type.Code);Assert.Equal(applied.Version,retained.Version);Assert.Empty(retained.History); // No existing grant to revoke.
        var unavailable=await client.Preview(company,change with {Mode="review",ExpectedVersion=retained.Version});Assert.False(unavailable.CanApply);Assert.Equal("finance_template_review_unavailable",unavailable.Check.ReasonCode);
        await Assert.ThrowsAsync<OnboardingApiException>(()=>client.Get(Guid.NewGuid(),fixture.AgentId,type.Code));
        var sales=Guid.NewGuid();
        await factory.SeedAsync(db=>{db.Add(new Agent(sales,company,"p18-wire-sales","Wire Sales","Sales specialist","Sales",null,AgentSeniority.Senior,AgentStatus.Active,AgentAutonomyLevel.Level2,
            tools:new Dictionary<string,System.Text.Json.Nodes.JsonNode?>{["allowed"]=new System.Text.Json.Nodes.JsonArray("sales.draft_proposal"),["actions"]=new System.Text.Json.Nodes.JsonArray("recommend")},
            scopes:new Dictionary<string,System.Text.Json.Nodes.JsonNode?>{["recommend"]=new System.Text.Json.Nodes.JsonArray("sales"),["responsibilityPolicy"]=new System.Text.Json.Nodes.JsonObject{["allowedDomains"]=new System.Text.Json.Nodes.JsonArray("sales")}}));return Task.CompletedTask;});
        var draftChange=new TaskPolicyChange(sales,"sales.proposal_drafting","automatic",2,DateTime.UtcNow.AddDays(1),"Internal draft wire history");
        var draftPreview=await client.Preview(company,draftChange);Assert.True(draftPreview.CanApply);
        var draftPolicy=await client.Apply(company,new(draftChange,draftPreview.PreviewHash));Assert.Equal(1,draftPolicy.Version);Assert.Equal("automatic",Assert.Single(draftPolicy.History).Mode);
        var reloaded=await client.Get(company,sales,draftChange.TaskType);Assert.Equal(draftPolicy.History.Single(),reloaded.History.Single());
    }
}
