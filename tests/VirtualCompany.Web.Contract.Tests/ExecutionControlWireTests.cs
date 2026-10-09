using VirtualCompany.Api.Tests;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Web.Services;
namespace VirtualCompany.Web.Contract.Tests;

public sealed class ExecutionControlWireTests
{
    [Fact]
    public async Task Real_typed_client_round_trips_scope_preview_idempotency_and_history()
    {
        using var f=new TestWebApplicationFactory();var company=Guid.NewGuid();var owner=Guid.NewGuid();
        await f.SeedAsync(db=>{db.AddRange(new Company(company,"P16 wire company"),new User(owner,"p16-wire@example.com","Wire owner","dev-header","p16-wire"),new CompanyMembership(Guid.NewGuid(),company,owner,CompanyMembershipRole.Owner,CompanyMembershipStatus.Active));return Task.CompletedTask;});
        var p=await AuthorityExplanationFixture.SeedAsync(f,company,owner);using var http=f.CreateClient();http.DefaultRequestHeaders.Add("X-Dev-Auth-Subject","p16-wire");http.DefaultRequestHeaders.Add("X-Dev-Auth-DisplayName","Wire owner");http.DefaultRequestHeaders.Add("X-Dev-Auth-Email","p16-wire@example.com");var client=new ExecutionControlApiClient(new CompanyApiTransport(http));
        var before=await client.Get(company,p.AgentId);var change=new ExecutionControlChange(Guid.NewGuid(),p.AgentId,true,before.Version,before.CompanyVersion,"Typed scope reviewed");
        var preview=await client.Preview(company,change);Assert.Equal(change,preview.Change);Assert.Equal(before.Version,preview.Before.Version);
        var after=await client.Apply(company,new(change,preview.PreviewHash));Assert.True(after.Paused);Assert.Equal("Wire owner",after.History.Single().Actor);
        var duplicate=await client.Apply(company,new(change,preview.PreviewHash));Assert.Equal(after.Version,duplicate.Version);Assert.Single(duplicate.History);
        await Assert.ThrowsAsync<OnboardingApiException>(()=>client.Apply(company,new(change with{CommandId=Guid.NewGuid()},preview.PreviewHash)));
        await Assert.ThrowsAsync<OnboardingApiException>(()=>client.Get(Guid.NewGuid(),p.AgentId));
    }
}
