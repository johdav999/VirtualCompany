using VirtualCompany.Api.Tests;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Contract.Tests;

public sealed class CollaborationEvidenceWireTests
{
    [Fact]
    public async Task Typed_client_reads_exact_versioned_artifacts_and_denies_foreign_company()
    {
        using var factory=new TestWebApplicationFactory();var company=Guid.NewGuid();var human=Guid.NewGuid();
        await factory.SeedAsync(db=>{db.AddRange(new User(human,"p11-wire@example.com","Wire owner","dev-header","p11-wire"),
            new Company(company,"P11 wire"),new CompanyMembership(Guid.NewGuid(),company,human,CompanyMembershipRole.Owner,CompanyMembershipStatus.Active));return Task.CompletedTask;});
        var fixture=await CollaborationEvidenceFixture.SeedAsync(factory,company,human);
        using var http=factory.CreateClient();http.DefaultRequestHeaders.Add("X-Dev-Auth-Subject","p11-wire");http.DefaultRequestHeaders.Add("X-Dev-Auth-Email","p11-wire@example.com");
        var client=new AgentWorkApiClient(new CompanyApiTransport(http));
        var evidence=(await client.CollaborationAsync(company,"task",fixture.RootId))!;
        Assert.Equal(company,evidence.CompanyId);Assert.Equal("awaiting_approval",evidence.OutcomeState);Assert.Equal(6,evidence.Artifacts.Count);
        var revised=evidence.Artifacts.Single(x=>x.Id==fixture.ProposalArtifactId);Assert.Equal(2,revised.Version);Assert.Contains(fixture.FinanceArtifactId,revised.InputIds);
        Assert.Equal("needs_review",revised.State);Assert.Null(revised.ReviewOutcome);Assert.Equal(3,evidence.Handoffs.Count);
        Assert.All(evidence.Handoffs,x=>{Assert.Contains(evidence.Artifacts,a=>a.Id==x.InputId);Assert.Contains(evidence.Artifacts,a=>a.Id==x.RecipientId);});
        Assert.Null(await client.CollaborationAsync(Guid.NewGuid(),"task",fixture.RootId));
    }
}
