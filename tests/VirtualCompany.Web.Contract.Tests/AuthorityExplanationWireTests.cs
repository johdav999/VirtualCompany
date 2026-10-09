using VirtualCompany.Api.Tests;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Contract.Tests;

public sealed class AuthorityExplanationWireTests
{
    [Fact]
    public async Task Real_client_preserves_current_work_and_grant_policy_contract()
    {
        using var factory=new TestWebApplicationFactory();var company=Guid.NewGuid();var owner=Guid.NewGuid();
        await factory.SeedAsync(db=> {db.AddRange(new Company(company,"P14 typed wire"),new User(owner,"p14-wire@example.com","Wire","dev-header","p14-wire"),
            new CompanyMembership(Guid.NewGuid(),company,owner,CompanyMembershipRole.Owner,CompanyMembershipStatus.Active));return Task.CompletedTask;});
        var fixture=await AuthorityExplanationFixture.SeedAsync(factory,company,owner);
        using var http=factory.CreateClient();http.DefaultRequestHeaders.Add("X-Dev-Auth-Subject","p14-wire");
        var client=new AgentWorkApiClient(new CompanyApiTransport(http));
        var result=(await client.AuthorityAsync(company,fixture.AgentId,"task",fixture.TaskId))!;
        Assert.Equal("controlled_execution",result.CompanyIntent);Assert.Equal("within_autonomy",result.TaskPolicy.ReasonCode);
        Assert.Contains(result.Agents.Single().Actions,x=>x.ToolName=="list_transactions"&&x.Check.State=="integration_unavailable");
        Assert.Contains(result.Agents.Single().Actions,x=>x.ToolName=="finance.removed_tool"&&x.Check.State=="not_implemented");
        Assert.Null(await client.AuthorityAsync(Guid.NewGuid(),fixture.AgentId));
    }
}
