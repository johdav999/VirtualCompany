using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Domain.Events;
using VirtualCompany.Infrastructure.Finance;
using VirtualCompany.Infrastructure.Persistence;
using Xunit;

namespace VirtualCompany.Api.Tests;

public sealed class FinanceDomainEventIntegrationTests : IClassFixture<TestWebApplicationFactory>
{
    private readonly TestWebApplicationFactory _factory;

    public FinanceDomainEventIntegrationTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task Seed_bootstrap_emits_finance_domain_events_with_trigger_compatible_envelopes()
    {
        var companyId = Guid.NewGuid();
        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "Seed Bootstrap Company"));
        await dbContext.SaveChangesAsync();

        var outbox = new RecordingOutboxEnqueuer();
        var service = new CompanyFinanceSeedBootstrapService(dbContext, outbox);

        var result = await service.GenerateAsync(
            new FinanceSeedBootstrapCommand(companyId, 42, injectAnomalies: true, anomalyScenarioProfile: "baseline"),
            CancellationToken.None);

        Assert.Equal(result.TransactionCount, outbox.Messages.Count(x => x.Topic == SupportedPlatformEventTypeRegistry.FinanceTransactionCreated));
        Assert.Equal(result.InvoiceCount, outbox.Messages.Count(x => x.Topic == SupportedPlatformEventTypeRegistry.FinanceInvoiceCreated));
        Assert.True(outbox.Messages.Count(x => x.Topic == SupportedPlatformEventTypeRegistry.FinanceThresholdBreached) > 0);

        var transactionEnvelope = Assert.IsType<PlatformEventEnvelope>(
            outbox.Messages.First(x => x.Topic == SupportedPlatformEventTypeRegistry.FinanceTransactionCreated).Payload);
        Assert.Equal(SupportedPlatformEventTypeRegistry.FinanceTransactionCreated, transactionEnvelope.EventType);
        Assert.Equal(companyId, transactionEnvelope.CompanyId);
        Assert.True(transactionEnvelope.Metadata.ContainsKey("recordId"));
        Assert.True(transactionEnvelope.Metadata.ContainsKey("amount"));
        Assert.True(transactionEnvelope.Metadata.ContainsKey("category"));
        Assert.True(transactionEnvelope.Metadata.ContainsKey("timestampUtc"));

        var invoiceEnvelope = Assert.IsType<PlatformEventEnvelope>(
            outbox.Messages.First(x => x.Topic == SupportedPlatformEventTypeRegistry.FinanceInvoiceCreated).Payload);
        Assert.Equal(SupportedPlatformEventTypeRegistry.FinanceInvoiceCreated, invoiceEnvelope.EventType);
        Assert.True(invoiceEnvelope.Metadata.ContainsKey("invoiceId"));
        Assert.True(invoiceEnvelope.Metadata.ContainsKey("supplierOrCustomerReference"));
        Assert.True(invoiceEnvelope.Metadata.ContainsKey("dueDateUtc"));

        var thresholdEnvelope = Assert.IsType<PlatformEventEnvelope>(
            outbox.Messages.First(x => x.Topic == SupportedPlatformEventTypeRegistry.FinanceThresholdBreached).Payload);
        Assert.Equal(SupportedPlatformEventTypeRegistry.FinanceThresholdBreached, thresholdEnvelope.EventType);
        Assert.True(thresholdEnvelope.Metadata.ContainsKey("breachType"));
        Assert.True(thresholdEnvelope.Metadata.ContainsKey("affectedRecordId"));
        Assert.IsType<JsonObject>(thresholdEnvelope.Metadata["evaluationDetails"]);
    }

    [Fact]
    public async Task Simulation_advance_emits_single_created_event_per_new_transaction_and_invoice()
    {
        var companyId = Guid.NewGuid();
        var timeProvider = new FixedTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();
        dbContext.Companies.Add(new Company(companyId, "Simulation Event Company"));
        await dbContext.SaveChangesAsync();

        var outbox = new RecordingOutboxEnqueuer();
        var service = new CompanySimulationService(
            dbContext,
            timeProvider,
            Options.Create(new CompanySimulationOptions
            {
                DefaultStepHours = 24,
                MaxStepHours = 168,
                AllowAcceleratedExecution = true
            }),
            NullLogger<CompanySimulationService>.Instance,
            outbox);

        var result = await service.AdvanceAsync(
            new AdvanceCompanySimulationTimeCommand(companyId, 72, 24, accelerated: true),
            CancellationToken.None);

        var transactionEvents = outbox.Messages
            .Where(x => x.Topic == SupportedPlatformEventTypeRegistry.FinanceTransactionCreated)
            .Select(x => Assert.IsType<PlatformEventEnvelope>(x.Payload))
            .ToArray();
        var invoiceEvents = outbox.Messages
            .Where(x => x.Topic == SupportedPlatformEventTypeRegistry.FinanceInvoiceCreated)
            .Select(x => Assert.IsType<PlatformEventEnvelope>(x.Payload))
            .ToArray();

        Assert.Equal(result.TransactionsGenerated, transactionEvents.Length);
        Assert.Equal(result.InvoicesGenerated, invoiceEvents.Length);
        Assert.Equal(transactionEvents.Length, transactionEvents.Select(x => x.EventId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(invoiceEvents.Length, invoiceEvents.Select(x => x.EventId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public async Task Threshold_evaluation_emits_threshold_breached_event_with_evaluation_details()
    {
        var companyId = Guid.NewGuid();
        var accountId = Guid.NewGuid();
        var counterpartyId = Guid.NewGuid();
        var transactionId = Guid.NewGuid();

        await using var connection = await OpenConnectionAsync();
        await using var dbContext = CreateContext(connection);
        await dbContext.Database.EnsureCreatedAsync();

        dbContext.Companies.Add(new Company(companyId, "Threshold Event Company"));
        dbContext.FinanceAccounts.Add(new FinanceAccount(
            accountId,
            companyId,
            "1000",
            "Operating Cash",
            "asset",
            "USD",
            5000m,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        dbContext.FinanceCounterparties.Add(new FinanceCounterparty(
            counterpartyId,
            companyId,
            "Northwind Retail",
            "customer",
            "ap@northwind.example"));
        dbContext.FinanceTransactions.Add(new FinanceTransaction(
            transactionId,
            companyId,
            accountId,
            counterpartyId,
            null,
            null,
            new DateTime(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc),
            "customer_payment",
            25000m,
            "USD",
            "Large customer payment",
            "TXN-THRESHOLD-001"));
        await dbContext.SaveChangesAsync();

        var outbox = new RecordingOutboxEnqueuer();
        var service = new CompanyFinanceTransactionAnomalyDetectionService(
            dbContext,
            new CompanyFinanceCommandService(dbContext),
            Options.Create(new FinanceAnomalyDetectionOptions()),
            new FixedTimeProvider(new DateTimeOffset(2026, 1, 16, 12, 0, 0, TimeSpan.Zero)),
            outbox);

        var result = await service.EvaluateAsync(
            new EvaluateFinanceTransactionAnomalyCommand(companyId, transactionId),
            CancellationToken.None);

        Assert.True(result.IsAnomalous);

        var envelope = Assert.IsType<PlatformEventEnvelope>(
            Assert.Single(outbox.Messages.Where(x => x.Topic == SupportedPlatformEventTypeRegistry.FinanceThresholdBreached)).Payload);
        Assert.Equal(SupportedPlatformEventTypeRegistry.FinanceThresholdBreached, envelope.EventType);
        Assert.Equal("finance_transaction", envelope.SourceEntityType);
        Assert.Equal(transactionId.ToString("N"), envelope.SourceEntityId);
        Assert.Equal("threshold_breach", envelope.Metadata["breachType"]!.GetValue<string>());
        Assert.Equal(transactionId, envelope.Metadata["affectedRecordId"]!.GetValue<Guid>());

        var evaluationDetails = Assert.IsType<JsonObject>(envelope.Metadata["evaluationDetails"]);
        Assert.Equal(transactionId, evaluationDetails["transactionId"]!.GetValue<Guid>());
        Assert.Equal(25000m, evaluationDetails["transactionAmount"]!.GetValue<decimal>());
        Assert.True(evaluationDetails.ContainsKey("workflowOutput"));
        Assert.True(evaluationDetails.ContainsKey("alertId"));
        Assert.True(evaluationDetails.ContainsKey("followUpTaskId"));
    }

    [Fact]
    public async Task Finance_outbox_events_reach_event_trigger_workflows_end_to_end()
    {
        var companyId = Guid.NewGuid();
        var invoiceDefinitionId = Guid.NewGuid();
        var thresholdDefinitionId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Companies.Add(new Company(companyId, "Finance Outbox Workflow Company"));
            dbContext.WorkflowDefinitions.Add(new WorkflowDefinition(
                invoiceDefinitionId,
                companyId,
                "finance-invoice-created",
                "Finance invoice created",
                "Finance",
                WorkflowTriggerType.Event,
                1,
                ValidDefinition("invoice-event-step")));
            dbContext.WorkflowDefinitions.Add(new WorkflowDefinition(
                thresholdDefinitionId,
                companyId,
                "finance-threshold-breached",
                "Finance threshold breached",
                "Finance",
                WorkflowTriggerType.Event,
                1,
                ValidDefinition("threshold-event-step")));
            dbContext.WorkflowTriggers.Add(new WorkflowTrigger(
                Guid.NewGuid(),
                companyId,
                invoiceDefinitionId,
                SupportedPlatformEventTypeRegistry.FinanceInvoiceCreated));
            dbContext.WorkflowTriggers.Add(new WorkflowTrigger(
                Guid.NewGuid(),
                companyId,
                thresholdDefinitionId,
                SupportedPlatformEventTypeRegistry.FinanceThresholdBreached));
            return Task.CompletedTask;
        });

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var bootstrap = scope.ServiceProvider.GetRequiredService<IFinanceSeedBootstrapService>();
            await bootstrap.GenerateAsync(
                new FinanceSeedBootstrapCommand(companyId, 77, injectAnomalies: true, anomalyScenarioProfile: "baseline"),
                CancellationToken.None);

            var processor = scope.ServiceProvider.GetRequiredService<ICompanyOutboxProcessor>();
            while (await processor.DispatchPendingAsync(CancellationToken.None) > 0)
            {
            }
        }

        await using var assertionScope = _factory.Services.CreateAsyncScope();
        var dbContext = assertionScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        Assert.True(await dbContext.WorkflowInstances.IgnoreQueryFilters().AnyAsync(x =>
            x.CompanyId == companyId &&
            x.DefinitionId == invoiceDefinitionId));
        Assert.True(await dbContext.WorkflowInstances.IgnoreQueryFilters().AnyAsync(x =>
            x.CompanyId == companyId &&
            x.DefinitionId == thresholdDefinitionId));
    }

    [Fact]
    public async Task Finance_simulation_outbox_events_reach_event_trigger_workflows_end_to_end()
    {
        var companyId = Guid.NewGuid();
        var transactionDefinitionId = Guid.NewGuid();
        var invoiceDefinitionId = Guid.NewGuid();

        await _factory.SeedAsync(dbContext =>
        {
            dbContext.Companies.Add(new Company(companyId, "Finance Simulation Workflow Company"));
            dbContext.WorkflowDefinitions.Add(new WorkflowDefinition(
                transactionDefinitionId,
                companyId,
                "finance-transaction-created",
                "Finance transaction created",
                "Finance",
                WorkflowTriggerType.Event,
                1,
                ValidDefinition("transaction-event-step")));
            dbContext.WorkflowDefinitions.Add(new WorkflowDefinition(
                invoiceDefinitionId,
                companyId,
                "finance-invoice-created",
                "Finance invoice created",
                "Finance",
                WorkflowTriggerType.Event,
                1,
                ValidDefinition("invoice-event-step")));
            dbContext.WorkflowTriggers.Add(new WorkflowTrigger(
                Guid.NewGuid(),
                companyId,
                transactionDefinitionId,
                SupportedPlatformEventTypeRegistry.FinanceTransactionCreated));
            dbContext.WorkflowTriggers.Add(new WorkflowTrigger(
                Guid.NewGuid(),
                companyId,
                invoiceDefinitionId,
                SupportedPlatformEventTypeRegistry.FinanceInvoiceCreated));
            return Task.CompletedTask;
        });

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var simulation = scope.ServiceProvider.GetRequiredService<ICompanySimulationService>();
            var result = await simulation.AdvanceAsync(
                new AdvanceCompanySimulationTimeCommand(companyId, 72, 24, accelerated: true),
                CancellationToken.None);
            Assert.True(result.TransactionsGenerated > 0);
            Assert.True(result.InvoicesGenerated > 0);

            var processor = scope.ServiceProvider.GetRequiredService<ICompanyOutboxProcessor>();
            while (await processor.DispatchPendingAsync(CancellationToken.None) > 0)
            {
            }
        }

        await using var assertionScope = _factory.Services.CreateAsyncScope();
        var dbContext = assertionScope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();

        Assert.True(await dbContext.WorkflowInstances.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.DefinitionId == transactionDefinitionId));
        Assert.True(await dbContext.WorkflowInstances.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId && x.DefinitionId == invoiceDefinitionId));
    }

    private static async Task<SqliteConnection> OpenConnectionAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        return connection;
    }

    private static VirtualCompanyDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<VirtualCompanyDbContext>()
            .UseSqlite(connection)
            .Options);

    private static Dictionary<string, JsonNode?> ValidDefinition(string stepId) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["steps"] = new JsonArray(
                new JsonObject
                {
                    ["id"] = stepId,
                    ["type"] = "task"
                })
        };

    private sealed class RecordingOutboxEnqueuer : ICompanyOutboxEnqueuer
    {
        private readonly List<RecordedOutboxMessage> _messages = [];

        public IReadOnlyList<RecordedOutboxMessage> Messages => _messages;

        public void Enqueue(
            Guid companyId,
            string topic,
            object payload,
            string? correlationId = null,
            DateTime? availableAtUtc = null,
            string? idempotencyKey = null,
            string? messageType = null,
            string? causationId = null,
            IReadOnlyDictionary<string, string?>? headers = null)
        {
            _messages.Add(new RecordedOutboxMessage(
                companyId,
                topic,
                payload,
                correlationId,
                availableAtUtc,
                idempotencyKey,
                messageType,
                causationId));
        }
    }

    private sealed record RecordedOutboxMessage(
        Guid CompanyId,
        string Topic,
        object Payload,
        string? CorrelationId,
        DateTime? AvailableAtUtc,
        string? IdempotencyKey,
        string? MessageType,
        string? CausationId);

    private sealed class FixedTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public FixedTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
