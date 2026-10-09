using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Agents;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Api.Tests;

public sealed class ExecutionControlOutboxTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Claimed_delivery_defers_without_attempts_and_uncertain_admission_cannot_resend(bool payment)
    {
        using var factory = new TestWebApplicationFactory();
        var company = Guid.NewGuid(); var owner = Guid.NewGuid(); var id = Guid.NewGuid();
        await BusinessWorkEvidenceIntegrationTests.SeedOwner(factory, company, owner);
        var profile = await AuthorityExplanationFixture.SeedAsync(factory, company, owner);
        var draft = Guid.NewGuid(); var caseId = Guid.NewGuid(); var execution = Guid.NewGuid();
        var key = payment ? execution.ToString("D") : "p16-delivery-" + id.ToString("N");
        await factory.SeedAsync(async db =>
        {
            var config = await db.CompanyOperatingConfigurations.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == company);
            config.Pause("Review outbound work");
            if (!payment)
            {
                db.Add(new SupportCase(caseId, company, "P16", "Controlled delivery", "Account question", "manual"));
                db.Add(new SupportReplyDraft(draft, company, caseId, "A retained response", "Helpful", .9m, .9m, null, "[]", profile.AgentId, owner));
            }
            object payload = payment
                ? new PaymentBatchSubmissionRequestedMessage(company, execution, "p16")
                : new SupportReplyDeliveryRequestedMessage(company, caseId, draft, owner, true, false, null, "controlled@example.test", null, "Account question", "original", null, null, key, "p16");
            db.Add(new CompanyOutboxMessage(id, company,
                payment ? CompanyOutboxTopics.PaymentBatchSubmissionRequested : CompanyOutboxTopics.SupportReplyDeliveryRequested,
                JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web)), idempotencyKey: key));
        });
        await factory.ExecuteScopeAsync(async scope =>
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<ICompanyOutboxProcessor>().DispatchPendingAsync(default)));
        await factory.SeedAsync(async db =>
        {
            var row = await db.CompanyOutboxMessages.SingleAsync(x => x.Id == id);
            Assert.Equal(CompanyOutboxMessageStatus.Pending, row.Status);
            Assert.Equal(0, row.AttemptCount); Assert.Null(row.ClaimToken);
            Assert.Contains("paused", row.LastError!, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(await db.AgentExecutionAdmissions.IgnoreQueryFilters().Where(x => x.CompanyId == company).ToListAsync());
            (await db.CompanyOperatingConfigurations.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == company)).Resume();
            db.Entry(row).Property(x => x.AvailableUtc).CurrentValue = DateTime.UtcNow.AddSeconds(-1);
        });
        // An earlier admitted effect has no confirmed provider acknowledgement. Resume must not resend it.
        await factory.ExecuteScopeAsync(async scope =>
        {
            var gate = scope.ServiceProvider.GetRequiredService<IAgentExecutionControlGate>();
            var admission = await gate.AdmitAsync(company, payment ? null : profile.AgentId, payment ? "payment_submission" : "support_delivery", key, default);
            await gate.AcknowledgeAsync(company, admission, false, default);
        });
        await factory.ExecuteScopeAsync(async scope =>
            Assert.Equal(0, await scope.ServiceProvider.GetRequiredService<ICompanyOutboxProcessor>().DispatchPendingAsync(default)));
        await factory.SeedAsync(async db =>
        {
            var row = await db.CompanyOutboxMessages.SingleAsync(x => x.Id == id);
            Assert.Equal(CompanyOutboxMessageStatus.Failed, row.Status); Assert.Equal(1, row.AttemptCount);
            Assert.Single(await db.AgentExecutionAdmissions.IgnoreQueryFilters().Where(x => x.CompanyId == company).ToListAsync());
            Assert.False((await db.AgentExecutionAdmissions.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == company)).Confirmed);
        });
    }
}
