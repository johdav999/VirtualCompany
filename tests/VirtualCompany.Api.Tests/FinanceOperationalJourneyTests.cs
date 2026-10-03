using System.Net;
using System.Net.Http.Json;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceOperationalJourneyTests
{
    [Fact]
    public async Task Authorized_report_today_and_record_review_use_persisted_obligations_and_deny_foreign_or_restricted_reads()
    {
        using var factory = new TestWebApplicationFactory(); var company = Guid.NewGuid(); var foreign = Guid.NewGuid(); var invoice = Guid.NewGuid(); var user = Guid.NewGuid(); var employee = Guid.NewGuid();
        await factory.SeedAsync(db => {
            db.Users.AddRange(new User(user, "p06-owner@example.com", "Finance owner", "dev-header", "p06-owner"), new User(employee, "p06-employee@example.com", "Employee", "dev-header", "p06-employee"));
            var c = new Company(company, "P06 Finance"); c.SetFinanceSeedStatus(FinanceSeedingState.Seeded, DateTime.UtcNow, DateTime.UtcNow);
            var other = new Company(foreign, "Foreign"); other.SetFinanceSeedStatus(FinanceSeedingState.Seeded, DateTime.UtcNow, DateTime.UtcNow); db.Companies.AddRange(c, other);
            db.CompanyMemberships.AddRange(new CompanyMembership(Guid.NewGuid(), company, user, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active), new CompanyMembership(Guid.NewGuid(), foreign, user, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(Guid.NewGuid(), company, employee, CompanyMembershipRole.Employee, CompanyMembershipStatus.Active));
            var customer = Guid.NewGuid(); db.FinanceCounterparties.Add(new(customer, company, "P06 Customer", "customer"));
            db.FinanceInvoices.Add(new(invoice, company, customer, "P06 overdue", DateTime.UtcNow.AddDays(-40), DateTime.UtcNow.AddDays(-31), 100, "SEK", "open", paidAmount: 20)); return Task.CompletedTask;
        });
        using var http = factory.CreateClient(); http.DefaultRequestHeaders.Add("X-Dev-Auth-Subject", "p06-owner"); http.DefaultRequestHeaders.Add("X-Dev-Auth-Email", "p06-owner@example.com");
        var path = $"/api/companies/{company}/finance/operational-report?currency=SEK";
        var report = await http.GetFromJsonAsync<FinanceOperationalReportDto>(path); Assert.Equal(80, report!.Totals.Single().Receivables); Assert.Equal(invoice, report.Receivables.Single().Id);
        var detail = await http.GetAsync($"/internal/companies/{company}/finance/invoices/{invoice}"); Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        var today = await http.GetFromJsonAsync<TodayWorkspaceDto>($"/api/companies/{company}/workspace/today?lens=finance&refresh=true");
        Assert.Equal(1, today!.Finance!.OverdueReceivables); Assert.Contains(today.Priorities, x => x.EvidenceSourceId == invoice.ToString("D"));
        Assert.NotNull(today.Priorities.Single(x => x.EvidenceSourceId == invoice.ToString("D")).DueUtc);
        Assert.Equal(HttpStatusCode.NotFound, (await http.GetAsync($"/internal/companies/{foreign}/finance/invoices/{invoice}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.GetAsync(path + "&horizonDays=99")).StatusCode);
        using var denied = factory.CreateClient(); denied.DefaultRequestHeaders.Add("X-Dev-Auth-Subject", "p06-employee"); denied.DefaultRequestHeaders.Add("X-Dev-Auth-Email", "p06-employee@example.com");
        Assert.Equal(HttpStatusCode.Forbidden, (await denied.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await denied.GetAsync($"/internal/companies/{company}/finance/invoices/{invoice}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await denied.PatchAsJsonAsync($"/internal/companies/{company}/finance/invoices/{invoice}/approval-status", new { status = "approved" })).StatusCode);
    }
}
