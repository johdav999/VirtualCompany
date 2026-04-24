using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceWorkflowTriggerIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public FinanceWorkflowTriggerIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Payment_event_runs_finance_trigger_pipeline_and_replay_is_idempotent()
    {
        var companyId = Guid.NewGuid();
        var companyName = "Finance Trigger Company";
        var paymentId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var firstOutboxId = Guid.NewGuid();
        var secondOutboxId = Guid.NewGuid();
        var utcNow = new DateTime(2026, 4, 22, 9, 0, 0, DateTimeKind.Utc);
        var paymentDate = utcNow.AddHours(-1);
        var correlationId = "finance-trigger-payment-001";
        var sourceEntityVersion = paymentDate.ToString("O");
        var eventId = $"finance.payment.created:{paymentId:N}";

        await _factory.SeedAsync(dbContext =>
        {
            var company = new Company(companyId, companyName);
            company.SetFinanceSeedStatus(FinanceSeedingState.Seeded, utcNow, utcNow);

            dbContext.Companies.Add(company);
            dbContext.Users.Add(new User(userId, "approver@virtualcompany.test", "Finance Approver"));
            dbContext.CompanyMemberships.Add(new CompanyMembership(
                Guid.NewGuid(),
                companyId,
                userId,
                CompanyMembershipRole.FinanceApprover,
                status: CompanyMembershipStatus.Active));
            dbContext.FinanceAccounts.Add(new FinanceAccount(
                accountId,
                companyId,
                "1000",
                "Operating Cash",
                "asset",
                "USD",
                1000m,
                utcNow.AddDays(-30)));
            dbContext.FinanceBalances.Add(new FinanceBalance(
                Guid.NewGuid(),
                companyId,
                accountId,
                utcNow,
                50m,
                "USD",
                utcNow));
            dbContext.FinancePolicyConfigurations.Add(new FinancePolicyConfiguration(
                Guid.NewGuid(),
                companyId,
                "USD",
                10000m,
                500m,
                true,
                -10000m,
                10000m,
                90,
                30));
            dbContext.Payments.Add(new Payment(
                paymentId,
                companyId,
                PaymentTypes.Outgoing,
                640.10m,
                "USD",
                paymentDate,
                PaymentMethods.BankTransfer,
                PaymentStatuses.Pending,
                "SUP-001",
                createdUtc: paymentDate,
                updatedUtc: paymentDate));
            dbContext.FinanceTransactions.Add(new FinanceTransaction(
                Guid.NewGuid(),
                companyId,
                accountId,
                null,
                null,
                null,
                utcNow.AddDays(-5),
                "operating_expense",
                -300m,
                "USD",
                "Recent operating expense",
                "TXN-LOW-CASH-001",
                createdUtc: utcNow.AddDays(-5)));

            dbContext.CompanyOutboxMessages.Add(CreatePlatformEventOutboxMessage(firstOutboxId, companyId, utcNow, correlationId, eventId, paymentId, sourceEntityVersion, "payment-outbox-001"));
            return Task.CompletedTask;
        });

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var processor = scope.ServiceProvider.GetRequiredService<ICompanyOutboxProcessor>();
            while (await processor.DispatchPendingAsync(CancellationToken.None) > 0)
            {
            }
        }

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.CompanyOutboxMessages.Add(CreatePlatformEventOutboxMessage(secondOutboxId, companyId, utcNow.AddMinutes(1), correlationId, eventId, paymentId, sourceEntityVersion, "payment-outbox-002"));
            return Task.CompletedTask;
        });

        await using (var replayScope = _factory.Services.CreateAsyncScope())
        {
            var processor = replayScope.ServiceProvider.GetRequiredService<ICompanyOutboxProcessor>();
            while (await processor.DispatchPendingAsync(CancellationToken.None) > 0)
            {
            }
        }

        await _factory.ExecuteDbContextAsync(async dbContext =>
        {
            var approvalTasks = await dbContext.ApprovalTasks
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.TargetType == ApprovalTargetType.Payment && x.TargetId == paymentId)
                .ToListAsync();
            Assert.Single(approvalTasks);

            var alerts = await dbContext.Alerts
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.Fingerprint == $"finance-cash-position:{companyId:N}:low-cash")
                .ToListAsync();
            var alert = Assert.Single(alerts);
            Assert.Equal(correlationId, alert.CorrelationId);

            var executions = await dbContext.FinanceWorkflowTriggerExecutions
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.TriggerType == "payment" && x.SourceEntityId == paymentId.ToString("N"))
                .OrderBy(x => x.StartedAtUtc)
                .ToListAsync();
            Assert.Equal(2, executions.Count);

            var successful = Assert.Single(executions.Where(x => x.Outcome == "succeeded"));
            Assert.Equal(companyId, successful.CompanyId);
            Assert.Equal("payment", successful.TriggerType);
            Assert.Equal(correlationId, successful.CorrelationId);
            Assert.Equal(eventId, successful.EventId);
            Assert.Equal(paymentId.ToString("N"), successful.CausationId);
            Assert.Equal(firstOutboxId.ToString("N"), successful.TriggerMessageId);
            Assert.Equal(paymentId.ToString("N"), successful.SourceEntityId);
            Assert.NotEqual(default, successful.StartedAtUtc);
            Assert.Equal(sourceEntityVersion, successful.SourceEntityVersion);
            Assert.NotNull(successful.CompletedAtUtc);
            Assert.Contains("refresh_insights_snapshot", successful.GetExecutedChecks());
            Assert.Contains("evaluate_cash_position", successful.GetExecutedChecks());
            Assert.Contains("ensure_approval_task", successful.GetExecutedChecks());
            Assert.Contains(eventId, successful.MetadataJson, StringComparison.Ordinal);
            Assert.Contains("payment", successful.MetadataJson, StringComparison.Ordinal);

            var checkExecutions = await dbContext.FinanceWorkflowTriggerCheckExecutions
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && x.TriggerType == "payment" && x.SourceEntityId == paymentId.ToString("N"))
                .OrderBy(x => x.CheckType)
                .ToListAsync();
            Assert.Equal(3, checkExecutions.Count);
            Assert.All(checkExecutions, x => Assert.Equal("succeeded", x.Outcome));
            Assert.Collection(
                checkExecutions.Select(x => x.CheckType),
                value => Assert.Equal("ensure_approval_task", value),
                value => Assert.Equal("evaluate_cash_position", value),
                value => Assert.Equal("refresh_insights_snapshot", value));

            var duplicate = Assert.Single(executions.Where(x => x.Outcome == "duplicate_skipped"));
            Assert.Equal(correlationId, duplicate.CorrelationId);
            Assert.Equal(eventId, duplicate.EventId);
            Assert.Equal(paymentId.ToString("N"), duplicate.CausationId);
            Assert.Equal(secondOutboxId.ToString("N"), duplicate.TriggerMessageId);
            Assert.NotNull(duplicate.CompletedAtUtc);
            Assert.Empty(duplicate.GetExecutedChecks());
            Assert.Contains(sourceEntityVersion, duplicate.ErrorDetails ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("duplicateChecks", duplicate.MetadataJson, StringComparison.OrdinalIgnoreCase);
            return 0;
        });
    }

    private static CompanyOutboxMessage CreatePlatformEventOutboxMessage(
        Guid outboxMessageId,
        Guid companyId,
        DateTime occurredAtUtc,
        string correlationId,
        string eventId,
        Guid paymentId,
        string sourceEntityVersion,
        string idempotencyKey)
    {
        var payload = JsonSerializer.Serialize(new PlatformEventEnvelope(
            eventId,
            CompanyOutboxTopics.FinancePaymentCreated,
            occurredAtUtc,
            companyId,
            correlationId,
            "payment",
            paymentId.ToString("N"),
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["paymentId"] = JsonValue.Create(paymentId),
                ["amount"] = JsonValue.Create(640.10m),
                ["sourceEntityVersion"] = JsonValue.Create(sourceEntityVersion),
                ["currency"] = JsonValue.Create("USD")
            }));

        return new CompanyOutboxMessage(
            outboxMessageId,
            companyId,
            CompanyOutboxTopics.FinancePaymentCreated,
            payload,
            correlationId: correlationId,
            idempotencyKey: idempotencyKey,
            causationId: paymentId.ToString("N"));
    }
}