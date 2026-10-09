using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Briefings;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
namespace VirtualCompany.Api.Tests;

public sealed record BriefingCadenceFixture(Guid Company, Guid Owner, Guid Delegate, Guid Fallback, Guid SalesTask, Guid FinanceTask)
{
    public sealed class Clock : TimeProvider { public DateTime Now = new(2026, 10, 6, 7, 0, 0, DateTimeKind.Utc); public override DateTimeOffset GetUtcNow() => new(Now); }
    public string Root => $"/api/companies/{Company:D}/briefings/cadence";
    public static async Task<BriefingCadenceFixture> Seed(TestWebApplicationFactory f)
    {
        var s = new BriefingCadenceFixture(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());
        await f.SeedAsync(db => {
            var company = new Company(s.Company, "P29 recorded company"); company.UpdateWorkspaceProfile(company.Name, null, null, "Europe/Stockholm", "SEK", "en", "SE");
            db.Add(company); foreach (var (id, subject, role) in new[] { (s.Owner, "p29-owner", CompanyMembershipRole.Owner), (s.Delegate, "p29-delegate", CompanyMembershipRole.Manager), (s.Fallback, "p29-fallback", CompanyMembershipRole.Admin) })
                db.AddRange(new User(id, subject + "@example.test", subject, "dev-header", subject), new CompanyMembership(Guid.NewGuid(), s.Company, id, role, CompanyMembershipStatus.Active));
            db.Add(new WorkTask(s.SalesTask, s.Company, "sales_meeting", "Confirm recorded customer meeting", "Recorded commitment", WorkTaskPriority.High, null, null, "user", s.Owner));
            db.Add(new WorkTask(s.FinanceTask, s.Company, "finance_close", "Restricted close obligation", null, WorkTaskPriority.Normal, null, null, "user", s.Owner));
            return Task.CompletedTask; }); return s;
    }
    public HttpClient Client(TestWebApplicationFactory f, string subject = "p29-owner") { var h = f.CreateClient(); h.DefaultRequestHeaders.Add("X-Dev-Auth-Subject", subject); h.DefaultRequestHeaders.Add("X-Dev-Auth-Email", subject + "@example.test"); h.DefaultRequestHeaders.Add("X-Company-Id", Company.ToString()); return h; }
    public static BriefingCadenceSettings Settings => new("sales", "Europe/Stockholm", new(8, 0), new(18, 0), [1, 2, 3, 4, 5], new(20, 0), new(7, 0), true, true, false, false, ["sales"],
        BriefingCadenceCalendar.Kinds.Select(k => new BriefingCadenceRow(k, k == "morning", new(8, 30))).ToArray());
    public async Task<BriefingCadenceContext> Save(HttpClient h, BriefingCadenceSettings? s = null) => await AnnualPlanningFixture.Read<BriefingCadenceContext>(await h.PutAsJsonAsync(Root, s ?? Settings));
    public async Task<int> Schedule(TestWebApplicationFactory f, DateTime now)
    { var count = 0; await f.ExecuteScopeAsync(async scope => { using var tenant = scope.ServiceProvider.GetRequiredService<ICompanyExecutionScopeFactory>().BeginScope(Company); count = await scope.ServiceProvider.GetRequiredService<IBriefingCadenceService>().ScheduleDueAsync(Company, now, default); }); return count; }
    public async Task RunJobs(TestWebApplicationFactory f) => await f.ExecuteScopeAsync(async scope => await scope.ServiceProvider.GetRequiredService<IBriefingUpdateJobRunner>().RunDueAsync(default));
    public async Task Dispatch(TestWebApplicationFactory f) => await f.ExecuteScopeAsync(async scope => await scope.ServiceProvider.GetRequiredService<ICompanyOutboxProcessor>().DispatchPendingAsync(default));
}
