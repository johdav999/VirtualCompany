using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.BackgroundJobs;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanyFinanceSeedBootstrapService : IFinanceSeedBootstrapService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyOutboxEnqueuer? _outboxEnqueuer;
    private readonly FinanceTransactionCreationOptions _transactionCreationOptions;

    public CompanyFinanceSeedBootstrapService(VirtualCompanyDbContext dbContext)
        : this(dbContext, null, Microsoft.Extensions.Options.Options.Create(new FinanceTransactionCreationOptions()))
    {
    }

    public CompanyFinanceSeedBootstrapService(
        VirtualCompanyDbContext dbContext,
        ICompanyOutboxEnqueuer? outboxEnqueuer,
        IOptions<FinanceTransactionCreationOptions> transactionCreationOptions)
    {
        _dbContext = dbContext;
        _outboxEnqueuer = outboxEnqueuer;
        _transactionCreationOptions = transactionCreationOptions.Value;
    }
    public async Task<FinanceSeedBootstrapResultDto> GenerateAsync(
        FinanceSeedBootstrapCommand command,
        CancellationToken cancellationToken)
    {
        if (command.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(command));
        }

        var companyExists = await _dbContext.Companies
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(x => x.Id == command.CompanyId, cancellationToken);
        if (!companyExists)
        {
            throw new KeyNotFoundException($"Company '{command.CompanyId}' was not found.");
        }

        if (!command.ReplaceExisting && await HasExistingFinanceSeedAsync(command.CompanyId, cancellationToken))
        {
            throw new InvalidOperationException("Finance seed data already exists for this company. Use replaceExisting to regenerate it.");
        }

        if (_dbContext.Database.IsRelational())
        {
            var executionStrategy = _dbContext.Database.CreateExecutionStrategy();
            return await executionStrategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                var result = await GenerateInternalAsync(command, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            });
        }

        return await GenerateInternalAsync(command, cancellationToken);
    }

    private async Task<FinanceSeedBootstrapResultDto> GenerateInternalAsync(
        FinanceSeedBootstrapCommand command,
        CancellationToken cancellationToken)
    {
        var allowNonSimulationTransactions = _transactionCreationOptions.AllowNonSimulationTransactionCreation;
        var anomalyOptions = allowNonSimulationTransactions
            ? new FinanceAnomalyInjectionOptions(command.InjectAnomalies, command.AnomalyScenarioProfile ?? "baseline")
            : FinanceAnomalyInjectionOptions.Disabled;

        if (command.ReplaceExisting)
        {
            await RemoveExistingFinanceSeedAsync(command.CompanyId, cancellationToken);
        }

        var dataset = command.SeedAnchorUtc.HasValue
            ? DeterministicFinanceSeedDatasetGenerator.Generate(
                _dbContext,
                command.CompanyId,
                command.SeedValue,
                command.SeedAnchorUtc.Value,
                anomalyOptions,
                allowNonSimulationTransactions)
            : DeterministicFinanceSeedDatasetGenerator.Generate(
                _dbContext,
                command.CompanyId,
                command.SeedValue,
                anomalyOptions,
                allowNonSimulationTransactions);

        if (dataset.ValidationErrors.Count > 0)
        {
            _dbContext.ChangeTracker.Clear();
            var summary = string.Join("; ", dataset.ValidationErrors.Select(x => $"{x.Code}: {x.Message}"));
            throw new PermanentBackgroundJobException($"Finance seed dataset validation failed: {summary}");
        }

        var company = await _dbContext.Companies
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == command.CompanyId, cancellationToken);

        FinanceSeedingMetadata.MarkSeeded(company, "finance_seed_bootstrap:v1", command.SeedValue);

        EnqueueSeedPlatformEvents(command.CompanyId, dataset);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Map(dataset);
    }

    private async Task<bool> HasExistingFinanceSeedAsync(Guid companyId, CancellationToken cancellationToken) =>
        await _dbContext.FinanceAccounts.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken) ||
        await _dbContext.FinanceCounterparties.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken) ||
        await _dbContext.FinanceTransactions.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken) ||
        await _dbContext.FinanceInvoices.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken) ||
        await _dbContext.FinanceBills.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken) ||
        await _dbContext.FinanceBalances.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken) ||
        await _dbContext.FinancePolicyConfigurations.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken) ||
        await _dbContext.FinanceSeedAnomalies.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken);

    private async Task RemoveExistingFinanceSeedAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var transactions = await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .ToListAsync(cancellationToken);
        var balances = await _dbContext.FinanceBalances
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .ToListAsync(cancellationToken);
        var invoices = await _dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .ToListAsync(cancellationToken);
        var bills = await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .ToListAsync(cancellationToken);
        var policies = await _dbContext.FinancePolicyConfigurations
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .ToListAsync(cancellationToken);
        var counterparties = await _dbContext.FinanceCounterparties
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .ToListAsync(cancellationToken);
        var anomalies = await _dbContext.FinanceSeedAnomalies
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .ToListAsync(cancellationToken);
        var accounts = await _dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .ToListAsync(cancellationToken);
        var seedDocuments = await _dbContext.CompanyKnowledgeDocuments
            .IgnoreQueryFilters()
            .Where(x =>
                x.CompanyId == companyId &&
                x.StorageKey != null &&
                x.StorageKey.StartsWith("seed-finance/"))
            .ToListAsync(cancellationToken);
        var company = await _dbContext.Companies
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == companyId, cancellationToken);
        _dbContext.FinanceSeedAnomalies.RemoveRange(anomalies);
        _dbContext.FinanceTransactions.RemoveRange(transactions);
        _dbContext.FinanceBalances.RemoveRange(balances);
        _dbContext.FinanceInvoices.RemoveRange(invoices);
        _dbContext.FinanceBills.RemoveRange(bills);
        _dbContext.FinancePolicyConfigurations.RemoveRange(policies);
        _dbContext.FinanceCounterparties.RemoveRange(counterparties);
        _dbContext.FinanceAccounts.RemoveRange(accounts);
        _dbContext.CompanyKnowledgeDocuments.RemoveRange(seedDocuments);
        FinanceSeedingMetadata.MarkNotSeeded(company);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private void EnqueueSeedPlatformEvents(Guid companyId, FinanceSeedDataset dataset)
    {
        if (_outboxEnqueuer is null)
        {
            return;
        }

        var counterpartiesById = _dbContext.ChangeTracker.Entries<FinanceCounterparty>()
            .Where(x => x.Entity.CompanyId == companyId)
            .Select(x => x.Entity)
            .GroupBy(x => x.Id)
            .ToDictionary(x => x.Key, x => x.First().Name, EqualityComparer<Guid>.Default);

        var invoices = _dbContext.ChangeTracker.Entries<FinanceInvoice>()
            .Where(x => x.State == EntityState.Added && x.Entity.CompanyId == companyId)
            .Select(x => x.Entity)
            .OrderBy(x => x.CreatedUtc)
            .ToList();
        foreach (var invoice in invoices)
        {
            FinanceDomainEvents.EnqueueInvoiceCreated(
                _outboxEnqueuer,
                invoice,
                ResolveCounterpartyReference(counterpartiesById, invoice.CounterpartyId));
        }

        var transactions = _dbContext.ChangeTracker.Entries<FinanceTransaction>()
            .Where(x => x.State == EntityState.Added && x.Entity.CompanyId == companyId)
            .Select(x => x.Entity)
            .OrderBy(x => x.CreatedUtc)
            .ToList();
        foreach (var transaction in transactions)
        {
            FinanceDomainEvents.EnqueueTransactionCreated(_outboxEnqueuer, transaction);
        }

        foreach (var anomaly in dataset.Anomalies)
        {
            var evaluationDetails = BuildSeedAnomalyEvaluationDetails(anomaly);
            foreach (var affectedRecordId in anomaly.GetAffectedRecordIds())
            {
                FinanceDomainEvents.EnqueueThresholdBreached(
                    _outboxEnqueuer,
                    anomaly.CompanyId,
                    anomaly.AnomalyType,
                    "finance_record",
                    affectedRecordId,
                    anomaly.CreatedUtc,
                    evaluationDetails,
                    correlationId: $"finance-seed-anomaly:{anomaly.CompanyId:N}:{anomaly.Id:N}",
                    idempotencyScope: anomaly.Id.ToString("N"));
            }
        }
    }

    private static FinanceSeedBootstrapResultDto Map(FinanceSeedDataset dataset) =>
        new(
            dataset.CompanyId,
            dataset.SeedValue,
            dataset.WindowStartUtc,
            dataset.WindowEndUtc,
            dataset.AccountIds.Count,
            dataset.CounterpartyIds.Count,
            dataset.SupplierIds.Count,
            dataset.CategoryIds.Count,
            dataset.InvoiceIds.Count,
            dataset.BillIds.Count,
            dataset.RecurringExpenses.Count,
            dataset.TransactionIds.Count,
            dataset.BalanceIds.Count,
            dataset.DocumentIds.Count,
            dataset.PolicyConfigurationId,
            dataset.RecurringExpenses
                .Select(x => new FinanceSeedRecurringExpenseDto(
                    x.Id,
                    x.SupplierId,
                    x.CategoryId,
                    x.Name,
                    x.Amount,
                    x.Currency,
                    x.Cadence,
                    x.DayOfPeriod))
                .ToArray(),
            dataset.ValidationErrors
                .Select(x => new FinanceSeedValidationErrorDto(x.Code, x.Message))
                .ToArray(),
            dataset.Anomalies
                .Select(x => new FinanceSeedAnomalyDto(
                    x.Id,
                    x.AnomalyType,
                    x.ScenarioProfile,
                    x.GetAffectedRecordIds(),
                    x.ExpectedDetectionMetadataJson))
                .ToArray());

    private static string ResolveCounterpartyReference(
        IReadOnlyDictionary<Guid, string> counterpartiesById,
        Guid counterpartyId) =>
        counterpartiesById.TryGetValue(counterpartyId, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : counterpartyId.ToString("N");

    private static Dictionary<string, JsonNode?> BuildSeedAnomalyEvaluationDetails(FinanceSeedAnomaly anomaly)
    {
        var details = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["anomalyId"] = JsonValue.Create(anomaly.Id),
            ["scenarioProfile"] = JsonValue.Create(anomaly.ScenarioProfile)
        };

        var parsedNode = JsonNode.Parse(anomaly.ExpectedDetectionMetadataJson);
        details["expectedDetection"] = parsedNode?.DeepClone();

        return details;
    }
}
