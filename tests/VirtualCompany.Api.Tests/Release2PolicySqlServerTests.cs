using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Agents;

namespace VirtualCompany.Api.Tests;

public sealed class Release2PolicySqlServerTests
{
    [ApiSqlServerFact][Trait("Category","SqlServer")]
    public async Task Two_activation_requests_cannot_overwrite_a_newer_policy_or_duplicate_history()
    {
        using var f=TestWebApplicationFactory.CreateSqlServer(TimeProvider.System);var company=Guid.NewGuid();var owner=Guid.NewGuid();
        await BusinessWorkEvidenceIntegrationTests.SeedOwner(f,company,owner);var p=await ExecutionControlFixture.SeedAsync(f,company,owner);
        using var first=BusinessWorkEvidenceIntegrationTests.Client(f);using var second=BusinessWorkEvidenceIntegrationTests.Client(f);
        var a=await Release2PolicyIntegrationTests.Preview(first,company,new(p.AgentId,"sales.account_research","automatic",2,DateTime.UtcNow.AddDays(1),"SQL concurrent activation A",1));
        var b=await Release2PolicyIntegrationTests.Preview(second,company,a.Change with{MaximumActionsPerDay=3,Rationale="SQL concurrent activation B"});
        var responses=await Task.WhenAll(first.PostAsJsonAsync($"/api/companies/{company}/task-policies/apply",new TaskPolicyApply(a.Change,a.PreviewHash)),second.PostAsJsonAsync($"/api/companies/{company}/task-policies/apply",new TaskPolicyApply(b.Change,b.PreviewHash)));
        Assert.Single(responses.Where(x=>x.StatusCode==HttpStatusCode.OK));Assert.Single(responses.Where(x=>x.StatusCode==HttpStatusCode.Conflict));
        await f.SeedAsync(async db=>{var policy=await db.TaskTypePolicies.IgnoreQueryFilters().SingleAsync(x=>x.CompanyId==company&&x.AgentId==p.AgentId);Assert.Equal(2,policy.Version);Assert.Equal(2,await db.TaskTypePolicyRevisions.IgnoreQueryFilters().CountAsync(x=>x.CompanyId==company&&x.PolicyId==policy.Id));Assert.True(await db.WorkTasks.IgnoreQueryFilters().AnyAsync(x=>x.Id==p.TaskId));});
        foreach(var response in responses)response.Dispose();
    }
}
