using VirtualCompany.Api.Tests;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Contract.Tests;

public sealed class BusinessWorkEvidenceWireTests
{
    [Theory]
    [InlineData("deal")][InlineData("case")][InlineData("invoice")][InlineData("bill")][InlineData("campaign")][InlineData("brief")]
    public async Task Real_typed_client_preserves_business_identity_current_work_and_partial_evidence(string kind)
    {
        using var factory=new TestWebApplicationFactory();var company=Guid.NewGuid();var owner=Guid.NewGuid();
        await factory.SeedAsync(db=>{db.AddRange(new User(owner,"p13-wire@example.com","P13 Wire","dev-header","p13-wire"),new Company(company,"P13 Wire"),
            new CompanyMembership(Guid.NewGuid(),company,owner,CompanyMembershipRole.Owner,CompanyMembershipStatus.Active));return Task.CompletedTask;});
        var fixture=await BusinessEvidenceFixture.SeedAsync(factory,company,owner,"p13-wire");
        using var http=factory.CreateClient();http.DefaultRequestHeaders.Add("X-Dev-Auth-Subject","p13-wire");http.DefaultRequestHeaders.Add("X-Dev-Auth-Email","p13-wire@example.com");
        var client=new AgentWorkApiClient(new CompanyApiTransport(http));var result=(await client.BusinessAsync(company,kind,fixture.Records[kind]))!;
        Assert.Equal(company,result.CompanyId);Assert.Equal(kind,result.RecordKind);Assert.Contains(result.Work,x=>x.Id==fixture.Tasks[kind]);
        Assert.Contains(result.Work.Single(x=>x.Id==fixture.Tasks[kind]).RelatedRecords,x=>x.Route.Contains(fixture.Approvals[kind].ToString()));
        if(kind=="case")Assert.Contains(result.Artifacts.Single().Diagnostics,x=>x.Contains("No source references"));
        Assert.Null(await client.BusinessAsync(Guid.NewGuid(),kind,fixture.Records[kind]));
    }
}
