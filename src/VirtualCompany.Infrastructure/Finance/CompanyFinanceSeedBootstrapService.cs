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
    private readonly IPlanningBaselineService? _planningBaselineService;
    private readonly FinanceTransactionCreationOptions _transactionCreationOptions;

    public CompanyFinanceSeedBootstrapService(VirtualCompanyDbContext dbContext)
        : this(dbContext, null, null, Microsoft.Extensions.Options.Options.Create(new FinanceTransactionCreationOptions()))
    {
    }

    public CompanyFinanceSeedBootstrapService(
        VirtualCompanyDbContext dbContext,
        ICompanyOutboxEnqueuer? outboxEnqueuer,
        IPlanningBaselineService? planningBaselineService,
        IOptions<FinanceTransactionCreationOptions> transactionCreationOptions)
    {
        _dbContext = dbContext;
        _outboxEnqueuer = outboxEnqueuer;
        _transactionCreationOptions = transactionCreationOptions.Value;
        _planningBaselineService = planningBaselineService;
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
        var bootstrapState = await LoadBootstrapStateAsync(command.CompanyId, cancellationToken);

        if (!command.ReplaceExisting && bootstrapState.HasExistingFinanceSeed && !bootstrapState.RequiresBankingBootstrap)
        {
            await EnsurePlanningBaselineAsync(command.CompanyId, cancellationToken);
            return PreviewExistingSeed(command);
        }

        if (_dbContext.Database.IsRelational())
        {
            var executionStrategy = _dbContext.Database.CreateExecutionStrategy();
            return await executionStrategy.ExecuteAsync(async () =>
            {
                await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
                var result = await GenerateInternalAsync(command, bootstrapState, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            });
        }

        return await GenerateInternalAsync(command, bootstrapState, cancellationToken);
    }

    private async Task<FinanceSeedBootstrapResultDto> GenerateInternalAsync(
        FinanceSeedBootstrapCommand command,
        FinanceSeedBootstrapState bootstrapState,
        CancellationToken cancellationToken)
    {
        if (command.ReplaceExisting)
        {
            await RemoveExistingFinanceSeedAsync(command.CompanyId, cancellationToken);
        }

        if (!command.ReplaceExisting && bootstrapState.RequiresBankingBootstrap)
        {
            return await GenerateBankingOnlyAsync(command, cancellationToken);
        }

        var dataset = CreateDataset(command);

        if (dataset.ValidationErrors.Count > 0)
        {
            _dbContext.ChangeTracker.Clear();
            var summary = string.Join("; ", dataset.ValidationErrors.Select(x => $"{x.Code}: {x.Message}"));
            throw new PermanentBackgroundJobException($"Finance seed dataset validation failed: {summary}");
        }

        var company = await _dbContext.Companies
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == command.CompanyId, cancellationToken);

        EnqueueSeedPlatformEvents(command.CompanyId, dataset);
        BankTransactionSeedData.AddMockBankingData(_dbContext, command.CompanyId, dataset.WindowEndUtc);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // Historical mock data predates explicit payment allocations, so backfill them before
        // the company is marked as fully seeded.
        var allocationService = new FinancePaymentAllocationService(_dbContext);
        await allocationService.BackfillAsync(new BackfillFinancePaymentAllocationsCommand(command.CompanyId, true), cancellationToken);
        await EnsurePlanningBaselineAsync(command.CompanyId, cancellationToken);

        FinanceSeedingMetadata.MarkSeeded(company, "finance_seed_bootstrap:v1", command.SeedValue);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return Map(dataset);
    }

    private FinanceSeedDataset CreateDataset(FinanceSeedBootstrapCommand command)
    {
        var allowNonSimulationTransactions = _transactionCreationOptions.AllowNonSimulationTransactionCreation;
        var anomalyOptions = allowNonSimulationTransactions
            ? new FinanceAnomalyInjectionOptions(command.InjectAnomalies, command.AnomalyScenarioProfile ?? "baseline")
            : FinanceAnomalyInjectionOptions.Disabled;

        return command.SeedAnchorUtc.HasValue
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
    }

    private FinanceSeedBootstrapResultDto PreviewExistingSeed(FinanceSeedBootstrapCommand command)
    {
        _dbContext.ChangeTracker.Clear();
        var result = Map(CreateDataset(command));
        _dbContext.ChangeTracker.Clear();
        return result;
    }

    private async Task<FinanceSeedBootstrapResultDto> GenerateBankingOnlyAsync(
        FinanceSeedBootstrapCommand command,
        CancellationToken cancellationToken)
    {
        var anchorUtc = await ResolveBankingSeedAnchorUtcAsync(command.CompanyId, command.SeedAnchorUtc, cancellationToken);
        BankTransactionSeedData.AddMockBankingData(_dbContext, command.CompanyId, anchorUtc);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var allocationService = new FinancePaymentAllocationService(_dbContext);
        await allocationService.BackfillAsync(new BackfillFinancePaymentAllocationsCommand(command.CompanyId, true), cancellationToken);
        await EnsurePlanningBaselineAsync(command.CompanyId, cancellationToken);

        var company = await _dbContext.Companies
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == command.CompanyId, cancellationToken);
        FinanceSeedingMetadata.MarkSeeded(company, "finance_seed_bootstrap:v1", command.SeedValue);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await BuildExistingResultAsync(command.CompanyId, command.SeedValue, anchorUtc, cancellationToken);
    }

    private async Task<FinanceSeedBootstrapState> LoadBootstrapStateAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var hasAccounts = await _dbContext.FinanceAccounts.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken);
        var hasCounterparties = await _dbContext.FinanceCounterparties.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken);
        var hasTransactions = await _dbContext.FinanceTransactions.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken);
        var hasBalances = await _dbContext.FinanceBalances.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken);
        var hasPolicy = await _dbContext.FinancePolicyConfigurations.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken);
        var hasBankAccounts = await _dbContext.CompanyBankAccounts.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken);
        var hasBankTransactions = await _dbContext.BankTransactions.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken);
        var hasInvoices = await _dbContext.FinanceInvoices.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken);
        var hasBills = await _dbContext.FinanceBills.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken);
        var hasPayments = await _dbContext.Payments.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken);
        var hasMappings = await _dbContext.FinancialStatementMappings.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken);
        var hasAnomalies = await _dbContext.FinanceSeedAnomalies.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken);

        return new FinanceSeedBootstrapState(
            hasAccounts || hasCounterparties || hasTransactions || hasBalances || hasPolicy || hasBankAccounts || hasBankTransactions || hasInvoices || hasBills || hasPayments || hasMappings || hasAnomalies,
            hasAccounts && hasCounterparties && hasTransactions && hasBalances && hasPolicy,
            hasBankAccounts,
            hasBankTransactions);
    }

    private async Task<DateTime> ResolveBankingSeedAnchorUtcAsync(
        Guid companyId,
        DateTime? requestedAnchorUtc,
        CancellationToken cancellationToken)
    {
        if (requestedAnchorUtc.HasValue)
        {
            return requestedAnchorUtc.Value.Kind == DateTimeKind.Utc
                ? requestedAnchorUtc.Value
                : requestedAnchorUtc.Value.ToUniversalTime();
        }

        var latestPaymentUtc = await _dbContext.Payments
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .Select(x => (DateTime?)x.PaymentDate)
            .MaxAsync(cancellationToken);
        if (latestPaymentUtc.HasValue)
        {
            return latestPaymentUtc.Value;
        }

        var latestTransactionUtc = await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .Select(x => (DateTime?)x.TransactionUtc)
            .MaxAsync(cancellationToken);
        if (latestTransactionUtc.HasValue)
        {
            return latestTransactionUtc.Value;
        }

        var latestBalanceUtc = await _dbContext.FinanceBalances
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .Select(x => (DateTime?)x.AsOfUtc)
            .MaxAsync(cancellationToken);
        if (latestBalanceUtc.HasValue)
        {
            return latestBalanceUtc.Value;
        }

        return DateTime.UtcNow;
    }

    private async Task<bool> HasExistingFinanceSeedAsync(Guid companyId, CancellationToken cancellationToken) =>
        await _dbContext.FinanceAccounts.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken) ||
        await _dbContext.FinanceCounterparties.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken) ||
        await _dbContext.FinanceTransactions.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken) ||
        await _dbContext.FinanceInvoices.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken) ||
        await _dbContext.FinanceBills.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken) ||
        await _dbContext.CompanyBankAccounts.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken) ||
        await _dbContext.BankTransactions.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken) ||
        await _dbContext.PaymentAllocations.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken) ||
        await _dbContext.Payments.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken) ||
        await _dbContext.FinanceBalances.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken) ||
        await _dbContext.FinancialStatementMappings.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken) ||
        await _dbContext.FinancePolicyConfigurations.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken) ||
        await _dbContext.FinanceSeedAnomalies.IgnoreQueryFilters().AnyAsync(x => x.CompanyId == companyId, cancellationToken);

    private async Task<FinanceSeedBootstrapResultDto> BuildExistingResultAsync(
        Guid companyId,
        int seedValue,
        DateTime anchorUtc,
        CancellationToken cancellationToken)
    {
        var accountCount = await _dbContext.FinanceAccounts.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId, cancellationToken);
        var counterpartyCount = await _dbContext.FinanceCounterparties.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId, cancellationToken);
        var supplierCount = await _dbContext.FinanceCounterparties.IgnoreQueryFilters().CountAsync(
            x => x.CompanyId == companyId &&
                (x.CounterpartyType == "supplier" || x.CounterpartyType == "vendor"),
            cancellationToken);
        var categoryCount = await _dbContext.FinanceTransactions.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .Select(x => x.TransactionType)
            .Distinct()
            .CountAsync(cancellationToken);
        var invoiceCount = await _dbContext.FinanceInvoices.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId, cancellationToken);
        var billCount = await _dbContext.FinanceBills.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId, cancellationToken);
        var transactionCount = await _dbContext.FinanceTransactions.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId, cancellationToken);
        var balanceCount = await _dbContext.FinanceBalances.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId, cancellationToken);
        var paymentCount = await _dbContext.Payments.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId, cancellationToken);
        var documentCount = await _dbContext.CompanyKnowledgeDocuments.IgnoreQueryFilters().CountAsync(x => x.CompanyId == companyId, cancellationToken);
        var policyConfigurationId = await _dbContext.FinancePolicyConfigurations.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .OrderBy(x => x.CreatedUtc)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken)
            ?? Guid.Empty;
        var earliestWindowUtc = await _dbContext.FinanceTransactions.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .Select(x => (DateTime?)x.TransactionUtc)
            .MinAsync(cancellationToken);
        var latestWindowUtc = await _dbContext.BankTransactions.IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .Select(x => (DateTime?)x.BookingDate)
            .MaxAsync(cancellationToken);

        return new FinanceSeedBootstrapResultDto(
            companyId,
            seedValue,
            earliestWindowUtc ?? anchorUtc,
            latestWindowUtc ?? anchorUtc,
            accountCount,
            counterpartyCount,
            supplierCount,
            categoryCount,
            invoiceCount,
            billCount,
            0,
            transactionCount,
            balanceCount,
            paymentCount,
            documentCount,
            policyConfigurationId,
            [],
            [],
            []);
    }

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
        var bankTransactionPaymentLinks = await _dbContext.BankTransactionPaymentLinks
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .ToListAsync(cancellationToken);
        var bankTransactionCashLedgerLinks = await _dbContext.BankTransactionCashLedgerLinks
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .ToListAsync(cancellationToken);
        var paymentCashLedgerLinks = await _dbContext.PaymentCashLedgerLinks
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .ToListAsync(cancellationToken);
        var bankTransactions = await _dbContext.BankTransactions
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId)
            .ToListAsync(cancellationToken);
        var cashPostingLedgerEntryIds = paymentCashLedgerLinks
            .Select(x => x.LedgerEntryId)
            .Concat(bankTransactionCashLedgerLinks.Select(x => x.LedgerEntryId))
            .Distinct()
            .ToArray();
        var cashPostingSourceMappings = await _dbContext.LedgerEntrySourceMappings
            .IgnoreQueryFilters()
            .Where(x =>
                x.CompanyId == companyId &&
                (cashPostingLedgerEntryIds.Contains(x.LedgerEntryId) ||
                 x.SourceType == FinanceCashPostingSourceTypes.BankTransaction ||
                 x.SourceType == FinanceCashPostingSourceTypes.PaymentAllocation ||
                 x.SourceType == FinanceCashPostingSourceTypes.PaymentSettlement))
            .ToListAsync(cancellationToken);
        cashPostingLedgerEntryIds = cashPostingLedgerEntryIds
            .Concat(cashPostingSourceMappings.Select(x => x.LedgerEntryId))
            .Distinct()
            .ToArray();
        var cashPostingLedgerEntryLines = cashPostingLedgerEntryIds.Length == 0
            ? []
            : await _dbContext.LedgerEntryLines
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && cashPostingLedgerEntryIds.Contains(x.LedgerEntryId))
                .ToListAsync(cancellationToken);
        var cashPostingLedgerEntries = cashPostingLedgerEntryIds.Length == 0
            ? []
            : await _dbContext.LedgerEntries
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId && cashPostingLedgerEntryIds.Contains(x.Id))
                .ToListAsync(cancellationToken);
        var bankAccounts = await _dbContext.CompanyBankAccounts.IgnoreQueryFilters().Where(x => x.CompanyId == companyId).ToListAsync(cancellationToken);
        var payments = await _dbContext.Payments
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
        var mappings = await _dbContext.FinancialStatementMappings
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
        _dbContext.PaymentCashLedgerLinks.RemoveRange(paymentCashLedgerLinks);
        _dbContext.LedgerEntrySourceMappings.RemoveRange(cashPostingSourceMappings);
        _dbContext.LedgerEntryLines.RemoveRange(cashPostingLedgerEntryLines);
        _dbContext.LedgerEntries.RemoveRange(cashPostingLedgerEntries);
        _dbContext.BankTransactionCashLedgerLinks.RemoveRange(bankTransactionCashLedgerLinks);
        _dbContext.BankTransactionPaymentLinks.RemoveRange(bankTransactionPaymentLinks);
        _dbContext.BankTransactions.RemoveRange(bankTransactions);
        _dbContext.CompanyBankAccounts.RemoveRange(bankAccounts);
        _dbContext.FinancialStatementMappings.RemoveRange(mappings);
        _dbContext.FinanceSeedAnomalies.RemoveRange(anomalies);
        _dbContext.FinanceTransactions.RemoveRange(transactions);
        _dbContext.FinanceBalances.RemoveRange(balances);
        _dbContext.Payments.RemoveRange(payments);
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
            dataset.PaymentIds.Count,
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

    private Task EnsurePlanningBaselineAsync(Guid companyId, CancellationToken cancellationToken) =>
        _planningBaselineService is null
            ? Task.CompletedTask
            : _planningBaselineService.EnsureBaselineAsync(companyId, cancellationToken);

    private sealed record FinanceSeedBootstrapState(
        bool HasExistingFinanceSeed,
        bool HasCoreFinanceSeed,
        bool HasBankAccounts,
        bool HasBankTransactions)
    {
        public bool RequiresBankingBootstrap => HasCoreFinanceSeed && (!HasBankAccounts || !HasBankTransactions);
    }
}
