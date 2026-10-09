using Microsoft.EntityFrameworkCore;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed record WeeklyWorkspaceFixture(Guid Company, Guid Foreign, Guid Owner, Guid Manager,
    Guid Deal, Guid Case, Guid Invoice, Guid Campaign, Guid TaskId, string OwnerSubject, string ManagerSubject)
{
    public static readonly DateTime Now = new(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);
    public static readonly DateTime Start = new(2026, 9, 27, 22, 0, 0, DateTimeKind.Utc);
    public static async Task<WeeklyWorkspaceFixture> Seed(TestWebApplicationFactory factory, bool priorCash = true)
    {
        var s = new WeeklyWorkspaceFixture(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "p19-owner", "p19-manager");
        await factory.SeedAsync(db =>
        {
            db.Users.AddRange(new User(s.Owner, "p19-owner@example.test", "Weekly owner", "dev-header", s.OwnerSubject),
                new User(s.Manager, "p19-manager@example.test", "Sales manager", "dev-header", s.ManagerSubject));
            var company = new Company(s.Company, "P19 recorded company");
            company.UpdateWorkspaceProfile(company.Name, null, null, "Europe/Stockholm", "SEK", "en", "SE");
            company.SetFinanceSeedStatus(FinanceSeedingState.Seeded, Now, Now);
            db.AddRange(company, new Company(s.Foreign, "Foreign weekly company"));
            var ownerMembership = Guid.NewGuid(); var managerMembership = Guid.NewGuid();
            db.CompanyMemberships.AddRange(new CompanyMembership(ownerMembership, s.Company, s.Owner, CompanyMembershipRole.Owner, CompanyMembershipStatus.Active),
                new CompanyMembership(managerMembership, s.Company, s.Manager, CompanyMembershipRole.Manager, CompanyMembershipStatus.Active));
            foreach (var area in new[] { ResponsibilityArea.CompanyPerformance, ResponsibilityArea.Sales, ResponsibilityArea.Marketing, ResponsibilityArea.CashAndAccounting, ResponsibilityArea.CustomerSupport })
                db.Add(new CompanyResponsibilityAssignment(Guid.NewGuid(), s.Company, area, ResponsibilityAssignmentKind.ExecutiveOversight,
                    ownerMembership, null, AgentAutonomyLevel.Level1, null, null));
            db.Add(new CompanyResponsibilityAssignment(Guid.NewGuid(), s.Company, ResponsibilityArea.Sales, ResponsibilityAssignmentKind.Primary,
                managerMembership, null, AgentAutonomyLevel.Level1, null, ownerMembership));
            db.Add(new Deal(s.Deal, s.Company, "Weekly proposal", SalesPipelineStage.NewStageId, 1000, "SEK", expectedCloseUtc: Now.AddDays(1), createdUtc: Start));
            foreach (var (time, text) in new[] { (Start.AddTicks(-1), "Before week"), (Start, "At week start"), (Now.AddTicks(-1), "Before cutoff"),
                         (Now, "At excluded cutoff"), (Start.AddDays(-7), "Prior start"), (Now.AddDays(-7), "Prior excluded cutoff") })
                db.Add(new SalesActivity(Guid.NewGuid(), s.Company, "stage change", text, time, dealId: s.Deal));
            db.Add(new SalesActivity(Guid.NewGuid(), s.Foreign, "stage change", "Foreign secret stage", Start.AddDays(1)));
            var recommendation = new SalesAgentRecommendation(Guid.NewGuid(), s.Company, "Recorded draft follow-up", "Recorded owner outcome", dealId: s.Deal,
                executionStatus: SalesStatuses.DraftCreated);
            db.Add(recommendation); db.Entry(recommendation).Property(x => x.ExecutedUtc).CurrentValue = Now.AddHours(-1);
            var task = new WorkTask(s.TaskId, s.Company, "sales_review", "Weekly root commitment", "Recorded root work", WorkTaskPriority.Normal,
                null, null, "user", s.Owner);
            task.UpdateStatus(WorkTaskStatus.Completed); db.Add(task);
            db.Entry(task).Property(x => x.CreatedUtc).CurrentValue = Start;
            db.Entry(task).Property(x => x.CompletedUtc).CurrentValue = Now.AddHours(-2);
            var onTime = new SupportCase(s.Case, s.Company, "P19-1", "On-time reply", "Recorded source", "manual", createdUtc: Start);
            onTime.SetSla(Now.AddHours(-4), Now.AddDays(1)); onTime.MarkFirstResponseSent(Now.AddHours(-5));
            var unanswered = new SupportCase(Guid.NewGuid(), s.Company, "P19-2", "Unanswered overdue", null, "manual", createdUtc: Start.AddDays(-10));
            unanswered.SetSla(Now.AddHours(-3), Now.AddDays(1)); unanswered.MarkSlaState(true, true);
            var notDue = new SupportCase(Guid.NewGuid(), s.Company, "P19-3", "Future target", null, "manual", createdUtc: Now.AddHours(-1));
            notDue.SetSla(Now.AddDays(1), Now.AddDays(2));
            db.AddRange(onTime, unanswered, notDue);
            db.AddRange(new SupportCaseEvent(Guid.NewGuid(), s.Company, s.Case, SupportCaseEventTypes.Resolved, "First resolution", "human", s.Owner, Now.AddHours(-2)),
                new SupportCaseEvent(Guid.NewGuid(), s.Company, s.Case, SupportCaseEventTypes.Reopened, "Recorded reopening", "human", s.Owner, Now.AddHours(-1)),
                new SupportCaseEvent(Guid.NewGuid(), s.Company, s.Case, SupportCaseEventTypes.Resolved, "Second resolution", "human", s.Owner, Now.AddMinutes(-30)));
            var sequence = Guid.NewGuid(); db.Add(new SalesSequence(sequence, s.Company, "Weekly launch sequence"));
            var campaign = new SalesCampaign(s.Campaign, s.Company, sequence, "Recorded campaign launch", "customers", createdUtc: Start);
            campaign.Start(Now.AddHours(-3)); db.Add(campaign);
            db.Add(new MarketingContentBrief(Guid.NewGuid(), s.Company, "Content deadline this week", "Prepare recorded brief", "Customers", "email", "en", "clear",
                "Review proposal", s.Campaign, null, Now.AddDays(1), s.Owner, null));
            var activity = new SalesCampaignActivity(Guid.NewGuid(), s.Company, s.Campaign, "Completed internal content", "content", "internal", "manual", Start, Now, "Europe/Stockholm");
            activity.Complete("Prepared internal content"); db.Add(activity); db.Entry(activity).Property(x => x.CompletedUtc).CurrentValue = Now.AddHours(-2);
            var counterparty = Guid.NewGuid(); db.Add(new FinanceCounterparty(counterparty, s.Company, "Weekly customer", "customer"));
            db.Add(new FinanceInvoice(s.Invoice, s.Company, counterparty, "P19-INVOICE", Start.AddDays(-1), Now.AddDays(1), 150, "SEK", "open", paidAmount: 20));
            var account = Guid.NewGuid(); db.Add(new FinanceAccount(account, s.Company, "1930", "Bank cash", "asset", "SEK", 100, Start.AddDays(-30)));
            db.Add(new FinanceBalance(Guid.NewGuid(), s.Company, account, Now.AddHours(-1), 500, "SEK"));
            if (priorCash) db.Add(new FinanceBalance(Guid.NewGuid(), s.Company, account, Start.AddDays(-8), 200, "SEK"));
            return Task.CompletedTask;
        });
        return s;
    }
    public static HttpClient Client(TestWebApplicationFactory factory, string subject = "p19-owner")
    {
        var http = factory.CreateClient(); http.DefaultRequestHeaders.Add("X-Dev-Auth-Subject", subject);
        http.DefaultRequestHeaders.Add("X-Dev-Auth-Email", subject + "@example.test"); return http;
    }
    public sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => new(Now); }
}
