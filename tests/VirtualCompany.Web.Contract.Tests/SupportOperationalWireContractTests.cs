using VirtualCompany.Api.Tests;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Web.Services;

namespace VirtualCompany.Web.Contract.Tests;

public sealed class SupportOperationalWireContractTests
{
    [Fact]
    public async Task Real_support_contract_preserves_calendar_cases_waiting_and_delivery_evidence()
    {
        using var factory = new TestWebApplicationFactory(); var company = Guid.NewGuid(); var user = Guid.NewGuid(); var caseId = Guid.NewGuid();
        await factory.SeedAsync(db => {
            db.Users.Add(new User(user,"p08-wire@example.com","Support wire","dev-header","p08-wire"));
            db.Companies.Add(new Company(company,"Support wire"));
            db.CompanyMemberships.Add(new CompanyMembership(Guid.NewGuid(),company,user,CompanyMembershipRole.Owner,CompanyMembershipStatus.Active));
            var c = new SupportCase(caseId,company,"WIRE","Customer question",null,"manual",createdUtc:DateTime.UtcNow.AddDays(-2));
            c.SetStatus(SupportCaseStatuses.WaitingInternal);c.SetSla(DateTime.UtcNow.AddHours(-1),DateTime.UtcNow.AddDays(1));db.SupportCases.Add(c);
            return Task.CompletedTask;
        });
        using var http=factory.CreateClient(); http.DefaultRequestHeaders.Add("X-Dev-Auth-Subject","p08-wire");http.DefaultRequestHeaders.Add("X-Dev-Auth-Email","p08-wire@example.com");
        var support=new SupportApiClient(http,transport:new CompanyApiTransport(http));
        var report=await support.GetOperationalReportAsync(company,new("sla"));
        Assert.Equal(caseId,Assert.Single(report.Cases).Case.Id); Assert.Equal(1,report.Waiting); Assert.Equal(1,report.Breached);
        Assert.Contains(DayOfWeek.Monday,report.Calendar.WorkingDays);
        var today=await new TodayWorkspaceApiClient(new CompanyApiTransport(http),false).RefreshAsync(company,"customers");
        Assert.Equal(1,today!.Support!.WaitingCases); Assert.Equal(1,today.Support.SlaBreached);
        Assert.Equal(report.Cases.Single().NextDeadlineUtc, today.Priorities.Single(x=>x.EvidenceSourceId==caseId.ToString("D")).DueUtc);
        var knowledge=await support.GetCaseKnowledgeAsync(company,caseId); Assert.NotNull(knowledge); Assert.DoesNotContain(knowledge.Sources,x=>x.IsTrusted);
        var draft=await support.GenerateDraftAsync(company,caseId,"Helpful");
        Assert.False(draft.DeliveryRequested); Assert.Equal("pending",draft.DeliveryStatus); Assert.Null(draft.SentUtc);
        var current=await support.GetCaseAsync(company,caseId); Assert.Single(current!.ReplyDrafts);Assert.Null(current.ReplyDrafts[0].SentUtc);
    }
}
