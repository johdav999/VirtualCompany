using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanySimulationOptions
{
    public const string SectionName = "CompanySimulation";

    public int DefaultStepHours { get; set; } = 24;
    public int MaxStepHours { get; set; } = 168;
    public bool AllowAcceleratedExecution { get; set; } = true;
    public int DefaultAutoAdvanceIntervalSeconds { get; set; } = 0;
}

public sealed class CompanySimulationService : ICompanySimulationService
{
    private const string ClockExtensionKey = "financeSimulationClock";
    private const string ProfileExtensionKey = "financeSimulationProfile";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly CompanySimulationOptions _options;
    private readonly ICompanyOutboxEnqueuer? _outboxEnqueuer;
    private readonly ILogger<CompanySimulationService> _logger;
    private readonly ISimulationFeatureGate? _featureGate;

    public CompanySimulationService(
        VirtualCompanyDbContext dbContext,
        TimeProvider timeProvider,
        IOptions<CompanySimulationOptions> options,
        ILogger<CompanySimulationService> logger)
        : this(dbContext, timeProvider, null, null, options, null, logger)
    {
    }

    public CompanySimulationService(
        VirtualCompanyDbContext dbContext,
        TimeProvider timeProvider,
        IOptions<CompanySimulationOptions> options,
        ILogger<CompanySimulationService> logger,
        ICompanyOutboxEnqueuer? outboxEnqueuer)
        : this(dbContext, timeProvider, null, outboxEnqueuer, options, null, logger)
    {
    }

    public CompanySimulationService(
        VirtualCompanyDbContext dbContext,
        TimeProvider timeProvider,
        ICompanyContextAccessor? companyContextAccessor,
        IOptions<CompanySimulationOptions> options,
        ILogger<CompanySimulationService> logger)
        : this(dbContext, timeProvider, companyContextAccessor, null, options, null, logger)
    {
    }

    public CompanySimulationService(
        VirtualCompanyDbContext dbContext,
        TimeProvider timeProvider,
        ICompanyContextAccessor? companyContextAccessor,
        ICompanyOutboxEnqueuer? outboxEnqueuer,
        IOptions<CompanySimulationOptions> options,
        ISimulationFeatureGate? featureGate,
        ILogger<CompanySimulationService> logger)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _companyContextAccessor = companyContextAccessor;
        _outboxEnqueuer = outboxEnqueuer;
        _options = options.Value;
        _logger = logger;
        _featureGate = featureGate;
    }

    public async Task<CompanySimulationClockDto> GetClockAsync(
        GetCompanySimulationClockQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);
        var company = await LoadCompanyAsync(query.CompanyId, cancellationToken);
        var state = await GetOrCreateClockStateAsync(company, cancellationToken);
        return MapClock(company.Id, state);
    }

    public async Task<AdvanceCompanySimulationTimeResultDto> AdvanceAsync(
        AdvanceCompanySimulationTimeCommand command,
        CancellationToken cancellationToken)
    {
        EnsureBackendExecutionEnabled(command.CompanyId, "advance");
        EnsureTenant(command.CompanyId);
        ValidateAdvanceCommand(command);

        var company = await LoadCompanyAsync(command.CompanyId, cancellationToken);
        var state = await GetOrCreateClockStateAsync(company, cancellationToken);
        var previousUtc = state.CurrentUtc;
        var totalHoursProcessed = command.TotalHours;
        var executionStepHours = Math.Clamp(
            command.ExecutionStepHours ?? state.DefaultStepHours,
            1,
            _options.MaxStepHours);
        var targetUtc = previousUtc.AddHours(totalHoursProcessed);
        var simulationContext = await CreateSimulationContextAsync(company, state, cancellationToken);

        var logs = new List<SimulationExecutionLogDto>();
        var transactionsGenerated = 0;
        var invoicesGenerated = 0;
        var billsGenerated = 0;
        var recurringExpenseInstancesGenerated = 0;
        var eventsEmitted = 0;
        var cursorUtc = previousUtc;
        var runId = Guid.NewGuid();
        var stepNumber = 0;

        if (_dbContext.Database.IsRelational())
        {
            await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
            while (cursorUtc < targetUtc)
            {
                var stepEndUtc = cursorUtc.AddHours(Math.Min(executionStepHours, (int)Math.Ceiling((targetUtc - cursorUtc).TotalHours)));
                var step = await AdvanceWindowAsync(company, state, simulationContext, cursorUtc, stepEndUtc, cancellationToken);

                stepNumber++;
                _dbContext.FinanceSimulationStepLogs.Add(new FinanceSimulationStepLog(
                    Guid.NewGuid(),
                    command.CompanyId,
                    runId,
                    stepNumber,
                    step.WindowStartUtc,
                    step.WindowEndUtc,
                    executionStepHours,
                    totalHoursProcessed,
                    command.Accelerated,
                    step.TransactionsGenerated,
                    step.InvoicesGenerated,
                    step.BillsGenerated,
                    step.RecurringExpenseInstancesGenerated,
                    step.EventsEmitted,
                    createdUtc: step.WindowEndUtc));

                logs.Add(step);
                transactionsGenerated += step.TransactionsGenerated;
                invoicesGenerated += step.InvoicesGenerated;
                billsGenerated += step.BillsGenerated;
                recurringExpenseInstancesGenerated += step.RecurringExpenseInstancesGenerated;
                eventsEmitted += step.EventsEmitted;
                cursorUtc = stepEndUtc;
                FinanceDomainEvents.EnqueueSimulationDayAdvanced(
                    _outboxEnqueuer,
                    command.CompanyId,
                    step.WindowStartUtc,
                    step.WindowEndUtc,
                    (int)Math.Max(1d, Math.Round((step.WindowEndUtc - step.WindowStartUtc).TotalHours, MidpointRounding.AwayFromZero)),
                    $"finance-simulation:{command.CompanyId:N}:{step.WindowEndUtc:yyyyMMddHHmm}");
                eventsEmitted++;
            }

            state.CurrentUtc = targetUtc;
            state.LastAdvancedUtc = targetUtc;
            state.DefaultStepHours = executionStepHours;
            SaveClockState(company, state);
            await _dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        else
        {
            while (cursorUtc < targetUtc)
            {
                var stepEndUtc = cursorUtc.AddHours(Math.Min(executionStepHours, (int)Math.Ceiling((targetUtc - cursorUtc).TotalHours)));
                var step = await AdvanceWindowAsync(company, state, simulationContext, cursorUtc, stepEndUtc, cancellationToken);

                stepNumber++;
                _dbContext.FinanceSimulationStepLogs.Add(new FinanceSimulationStepLog(
                    Guid.NewGuid(),
                    command.CompanyId,
                    runId,
                    stepNumber,
                    step.WindowStartUtc,
                    step.WindowEndUtc,
                    executionStepHours,
                    totalHoursProcessed,
                    command.Accelerated,
                    step.TransactionsGenerated,
                    step.InvoicesGenerated,
                    step.BillsGenerated,
                    step.RecurringExpenseInstancesGenerated,
                    step.EventsEmitted,
                    createdUtc: step.WindowEndUtc));

                logs.Add(step);
                transactionsGenerated += step.TransactionsGenerated;
                invoicesGenerated += step.InvoicesGenerated;
                billsGenerated += step.BillsGenerated;
                recurringExpenseInstancesGenerated += step.RecurringExpenseInstancesGenerated;
                eventsEmitted += step.EventsEmitted;
                cursorUtc = stepEndUtc;
                FinanceDomainEvents.EnqueueSimulationDayAdvanced(
                    _outboxEnqueuer,
                    command.CompanyId,
                    step.WindowStartUtc,
                    step.WindowEndUtc,
                    (int)Math.Max(1d, Math.Round((step.WindowEndUtc - step.WindowStartUtc).TotalHours, MidpointRounding.AwayFromZero)),
                    $"finance-simulation:{command.CompanyId:N}:{step.WindowEndUtc:yyyyMMddHHmm}");
                eventsEmitted++;
            }

            state.CurrentUtc = targetUtc;
            state.LastAdvancedUtc = targetUtc;
            state.DefaultStepHours = executionStepHours;
            SaveClockState(company, state);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return new AdvanceCompanySimulationTimeResultDto(
            command.CompanyId,
            previousUtc,
            targetUtc,
            totalHoursProcessed,
            executionStepHours,
            transactionsGenerated,
            invoicesGenerated,
            billsGenerated,
            recurringExpenseInstancesGenerated,
            eventsEmitted,
            logs);
    }

    public async Task<AdvanceCompanySimulationTimeResultDto?> RunScheduledAdvanceAsync(
        RunScheduledCompanySimulationCommand command,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken)
    {
        if (_featureGate?.IsBackgroundExecutionAllowed() == false)
        {
            var featureState = _featureGate.GetState();
            _logger.LogInformation(
                "Skipping scheduled simulation advance for company {CompanyId} because simulation execution is disabled. BackendExecutionEnabled={BackendExecutionEnabled}, BackgroundJobsEnabled={BackgroundJobsEnabled}.",
                command.CompanyId,
                featureState.BackendExecutionEnabled,
                featureState.BackgroundJobsEnabled);
            return null;
        }

        EnsureTenant(command.CompanyId);
        var company = await LoadCompanyAsync(command.CompanyId, cancellationToken);
        var state = await GetOrCreateClockStateAsync(company, cancellationToken);
        if (!state.Enabled || !state.AutoAdvanceEnabled || state.AutoAdvanceIntervalSeconds <= 0)
        {
            return null;
        }

        var lastAdvanceUtc = state.LastAdvancedUtc ?? state.CurrentUtc;
        if (utcNow.UtcDateTime - lastAdvanceUtc < TimeSpan.FromSeconds(state.AutoAdvanceIntervalSeconds))
        {
            return null;
        }

        return await AdvanceAsync(
            new AdvanceCompanySimulationTimeCommand(
                command.CompanyId,
                state.DefaultStepHours,
                state.DefaultStepHours,
                Accelerated: true),
            cancellationToken);
    }

    private async Task<SimulationExecutionLogDto> AdvanceWindowAsync(
        Company company,
        CompanySimulationState state,
        SimulationContext context,
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        CancellationToken cancellationToken)
    {
        var invoiceCount = 0;
        var billCount = 0;
        var transactionCount = 0;
        var recurringExpenseCount = 0;
        var eventCount = 0;

        foreach (var rule in context.Profile.RevenueRules)
        {
            foreach (var occurrenceUtc in EnumerateRevenueOccurrences(windowStartUtc, windowEndUtc, state.AnchorUtc, rule))
            {
                var invoice = new FinanceInvoice(
                    Guid.NewGuid(),
                    company.Id,
                    ResolveCustomerId(rule, context.Customers),
                    NextInvoiceNumber(state),
                    occurrenceUtc,
                    occurrenceUtc.AddDays(Math.Max(1, rule.InvoiceDueDays)),
                    Money(rule.Amount),
                    NormalizeCurrency(rule.Currency, context.Currency),
                    "open",
                    createdUtc: occurrenceUtc,
                    updatedUtc: occurrenceUtc);
                _dbContext.FinanceInvoices.Add(invoice);
                context.KnownInvoices.Add(invoice);
                FinanceDomainEvents.EnqueueInvoiceCreated(
                    _outboxEnqueuer,
                    invoice,
                    ResolveCounterpartyReference(invoice.CounterpartyId, context.Customers));

                invoiceCount++;

                var paymentUtc = occurrenceUtc.AddDays(Math.Max(0, rule.PaymentDelayDays));
                if (paymentUtc > windowStartUtc && paymentUtc <= windowEndUtc)
                {
                    var payment = new FinanceTransaction(
                        Guid.NewGuid(),
                        company.Id,
                        context.OperatingCashAccount.Id,
                        invoice.CounterpartyId,
                        invoice.Id,
                        null,
                        paymentUtc,
                        "customer_payment",
                        invoice.Amount,
                        invoice.Currency,
                        $"Simulated payment for {invoice.InvoiceNumber}",
                        NextExternalReference("SIM-PAY", state),
                        createdUtc: paymentUtc);
                    _dbContext.FinanceTransactions.Add(payment);
                    FinanceDomainEvents.EnqueueTransactionCreated(_outboxEnqueuer, payment);
                    context.SettledInvoiceIds.Add(invoice.Id);
                    transactionCount++;
                }
            }
        }

        foreach (var invoice in context.KnownInvoices)
        {
            if (context.SettledInvoiceIds.Contains(invoice.Id))
            {
                continue;
            }

            var paymentDelayDays = context.Profile.RevenueRules.FirstOrDefault()?.PaymentDelayDays ?? 5;
            var paymentUtc = invoice.IssuedUtc.AddDays(Math.Max(0, paymentDelayDays));
            if (paymentUtc <= windowStartUtc || paymentUtc > windowEndUtc)
            {
                continue;
            }

            var payment = new FinanceTransaction(
                Guid.NewGuid(),
                company.Id,
                context.OperatingCashAccount.Id,
                invoice.CounterpartyId,
                invoice.Id,
                null,
                paymentUtc,
                "customer_payment",
                invoice.Amount,
                invoice.Currency,
                $"Simulated payment for {invoice.InvoiceNumber}",
                NextExternalReference("SIM-PAY", state),
                createdUtc: paymentUtc);
            _dbContext.FinanceTransactions.Add(payment);
            FinanceDomainEvents.EnqueueTransactionCreated(_outboxEnqueuer, payment);
            context.SettledInvoiceIds.Add(invoice.Id);
            transactionCount++;
        }

        foreach (var rule in context.Profile.RecurringExpenseRules)
        {
            foreach (var occurrenceUtc in EnumerateRecurringOccurrences(windowStartUtc, windowEndUtc, state.AnchorUtc, rule))
            {
                var bill = new FinanceBill(
                    Guid.NewGuid(),
                    company.Id,
                    ResolveSupplierId(rule, context.Suppliers),
                    NextBillNumber(state),
                    occurrenceUtc,
                    occurrenceUtc.AddDays(14),
                    Money(rule.Amount),
                    NormalizeCurrency(rule.Currency, context.Currency),
                    "open",
                    createdUtc: occurrenceUtc,
                    updatedUtc: occurrenceUtc);
                _dbContext.FinanceBills.Add(bill);
                FinanceDomainEvents.EnqueueBillCreated(_outboxEnqueuer, bill, rule.Name);
                billCount++;
                recurringExpenseCount++;

                var disbursementUtc = occurrenceUtc.AddHours(Math.Max(1, rule.PaymentDelayHours));
                if (disbursementUtc <= windowEndUtc)
                {
                    var payment = new FinanceTransaction(
                        Guid.NewGuid(),
                        company.Id,
                        context.OperatingCashAccount.Id,
                        bill.CounterpartyId,
                        null,
                        bill.Id,
                        disbursementUtc,
                        NormalizeCategory(rule.CategoryId),
                        -Money(rule.Amount),
                        NormalizeCurrency(rule.Currency, context.Currency),
                        rule.Name,
                        NextExternalReference("SIM-BILL", state),
                        createdUtc: disbursementUtc);
                    _dbContext.FinanceTransactions.Add(payment);
                    FinanceDomainEvents.EnqueueTransactionCreated(_outboxEnqueuer, payment);
                    transactionCount++;
                }
            }
        }

        var summaryEvent = new ActivityEvent(
            Guid.NewGuid(),
            company.Id,
            agentId: null,
            eventType: "finance.simulation.progressed",
            occurredUtc: windowEndUtc,
            status: "completed",
            summary: $"Advanced finance simulation from {windowStartUtc:O} to {windowEndUtc:O}.",
            correlationId: $"finance-simulation:{company.Id:N}:{windowStartUtc:yyyyMMddHHmm}",
            sourceMetadata: new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["companyId"] = JsonValue.Create(company.Id.ToString()),
                ["windowStartUtc"] = JsonValue.Create(windowStartUtc),
                ["windowEndUtc"] = JsonValue.Create(windowEndUtc),
                ["transactionsGenerated"] = JsonValue.Create(transactionCount),
                ["invoicesGenerated"] = JsonValue.Create(invoiceCount),
                ["billsGenerated"] = JsonValue.Create(billCount),
                ["recurringExpenseInstancesGenerated"] = JsonValue.Create(recurringExpenseCount),
                ["eventsEmitted"] = JsonValue.Create(1)
            },
            department: "finance",
            createdUtc: windowEndUtc);
        _dbContext.ActivityEvents.Add(summaryEvent);
        eventCount++;

        using var logScope = _logger.BeginScope(ExecutionLogScope.ForBackground($"finance-simulation:{company.Id:N}", company.Id));
        _logger.LogInformation(
            "Finance simulation step completed for company {CompanyId}. Range {WindowStartUtc} - {WindowEndUtc}. Transactions {TransactionsGenerated}. Invoices {InvoicesGenerated}. Bills {BillsGenerated}. Recurring expense instances {RecurringExpenseInstancesGenerated}. Events {EventsEmitted}.",
            company.Id,
            windowStartUtc,
            windowEndUtc,
            transactionCount,
            invoiceCount,
            billCount,
            recurringExpenseCount,
            eventCount);

        return new SimulationExecutionLogDto(
            company.Id,
            windowStartUtc,
            windowEndUtc,
            transactionCount,
            invoiceCount,
            billCount,
            recurringExpenseCount,
            eventCount);
    }

    private async Task<SimulationContext> CreateSimulationContextAsync(
        Company company,
        CompanySimulationState state,
        CancellationToken cancellationToken)
    {
        var currency = NormalizeCurrency(company.Currency, "USD");

        var operatingCashAccount = await _dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.CompanyId == company.Id &&
                     (x.Code == "1000" || x.Name.Contains("cash")),
                cancellationToken);
        if (operatingCashAccount is null)
        {
            operatingCashAccount = new FinanceAccount(
                Guid.NewGuid(),
                company.Id,
                "1000",
                "Operating Cash",
                "asset",
                currency,
                50000m,
                state.CurrentUtc,
                createdUtc: state.CurrentUtc,
                updatedUtc: state.CurrentUtc);
            _dbContext.FinanceAccounts.Add(operatingCashAccount);
        }

        var customers = await _dbContext.FinanceCounterparties
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == company.Id && x.CounterpartyType == "customer")
            .ToListAsync(cancellationToken);
        if (customers.Count == 0)
        {
            customers.Add(new FinanceCounterparty(
                Guid.NewGuid(),
                company.Id,
                $"{company.Name} Customer",
                "customer",
                "ar@example.com",
                createdUtc: state.CurrentUtc,
                updatedUtc: state.CurrentUtc));
            _dbContext.FinanceCounterparties.AddRange(customers);
        }

        var suppliers = await _dbContext.FinanceCounterparties
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == company.Id && (x.CounterpartyType == "supplier" || x.CounterpartyType == "vendor"))
            .ToListAsync(cancellationToken);
        if (suppliers.Count == 0)
        {
            suppliers.AddRange(
            [
                new FinanceCounterparty(
                    Guid.NewGuid(),
                    company.Id,
                    "Contoso Cloud",
                    "supplier",
                    "billing@contoso.example",
                    createdUtc: state.CurrentUtc,
                    updatedUtc: state.CurrentUtc),
                new FinanceCounterparty(
                    Guid.NewGuid(),
                    company.Id,
                    "Wide World Rentals",
                    "supplier",
                    "rent@wideworld.example",
                    createdUtc: state.CurrentUtc,
                    updatedUtc: state.CurrentUtc),
                new FinanceCounterparty(
                    Guid.NewGuid(),
                    company.Id,
                    "Tailspin Telecom",
                    "supplier",
                    "billing@tailspin.example",
                    createdUtc: state.CurrentUtc,
                    updatedUtc: state.CurrentUtc),
                new FinanceCounterparty(
                    Guid.NewGuid(),
                    company.Id,
                    "Northwind Insurance",
                    "supplier",
                    "accounts@northwind.example",
                    createdUtc: state.CurrentUtc,
                    updatedUtc: state.CurrentUtc)
            ]);
            _dbContext.FinanceCounterparties.AddRange(suppliers);
        }

        var knownInvoices = await _dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == company.Id)
            .ToListAsync(cancellationToken);

        var settledInvoiceIds = await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == company.Id && x.InvoiceId.HasValue)
            .Select(x => x.InvoiceId!.Value)
            .ToHashSetAsync(cancellationToken);

        var profile = ReadExtension<SimulationProfile>(company, ProfileExtensionKey) ??
            CreateDefaultProfile(customers, suppliers, currency);
        NormalizeProfile(profile, customers, suppliers, currency);
        SaveExtension(company, ProfileExtensionKey, profile);

        return new SimulationContext(
            operatingCashAccount,
            currency,
            customers,
            suppliers,
            knownInvoices,
            settledInvoiceIds,
            profile);
    }

    private async Task<CompanySimulationState> GetOrCreateClockStateAsync(
        Company company,
        CancellationToken cancellationToken)
    {
        var state = ReadExtension<CompanySimulationState>(company, ClockExtensionKey);
        if (state is not null)
        {
            state.CurrentUtc = EnsureUtc(state.CurrentUtc);
            state.AnchorUtc = EnsureUtc(state.AnchorUtc);
            state.DefaultStepHours = Math.Clamp(state.DefaultStepHours <= 0 ? _options.DefaultStepHours : state.DefaultStepHours, 1, _options.MaxStepHours);
            state.AutoAdvanceIntervalSeconds = Math.Max(0, state.AutoAdvanceIntervalSeconds);
            if (state.LastAdvancedUtc.HasValue)
            {
                state.LastAdvancedUtc = EnsureUtc(state.LastAdvancedUtc.Value);
            }

            SaveExtension(company, ClockExtensionKey, state);
            return state;
        }

        var initialUtc = await ResolveInitialClockUtcAsync(company.Id, company.CreatedUtc, cancellationToken);
        state = new CompanySimulationState
        {
            Enabled = true,
            AutoAdvanceEnabled = false,
            DefaultStepHours = _options.DefaultStepHours,
            AutoAdvanceIntervalSeconds = _options.DefaultAutoAdvanceIntervalSeconds,
            AnchorUtc = initialUtc,
            CurrentUtc = initialUtc
        };
        SaveExtension(company, ClockExtensionKey, state);
        return state;
    }

    private async Task<DateTime> ResolveInitialClockUtcAsync(
        Guid companyId,
        DateTime companyCreatedUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var latestTransactionUtc = await _dbContext.FinanceTransactions
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId)
                .Select(x => (DateTime?)x.TransactionUtc)
                .MaxAsync(cancellationToken);
            var latestInvoiceUtc = await _dbContext.FinanceInvoices
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId)
                .Select(x => (DateTime?)x.IssuedUtc)
                .MaxAsync(cancellationToken);
            var latestBillUtc = await _dbContext.FinanceBills
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId)
                .Select(x => (DateTime?)x.ReceivedUtc)
                .MaxAsync(cancellationToken);
            var latestBalanceUtc = await _dbContext.FinanceBalances
                .IgnoreQueryFilters()
                .Where(x => x.CompanyId == companyId)
                .Select(x => (DateTime?)x.AsOfUtc)
                .MaxAsync(cancellationToken);

            var latestUtc = new[]
            {
                latestTransactionUtc,
                latestInvoiceUtc,
                latestBillUtc,
                latestBalanceUtc,
                companyCreatedUtc
            }
            .Where(x => x.HasValue)
            .Select(x => EnsureUtc(x!.Value))
            .DefaultIfEmpty(_timeProvider.GetUtcNow().UtcDateTime)
            .Max();

            return new DateTime(latestUtc.Year, latestUtc.Month, latestUtc.Day, latestUtc.Hour, 0, 0, DateTimeKind.Utc);
        }
        catch (SqlException ex) when (ex.Number == 208 && ex.Message.Contains("finance_", StringComparison.OrdinalIgnoreCase))
        {
            var fallbackUtc = EnsureUtc(companyCreatedUtc);
            return new DateTime(fallbackUtc.Year, fallbackUtc.Month, fallbackUtc.Day, fallbackUtc.Hour, 0, 0, DateTimeKind.Utc);
        }
    }

    private static IEnumerable<DateTime> EnumerateRevenueOccurrences(
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        DateTime anchorUtc,
        SimulationRevenueRule rule)
    {
        var startDateUtc = windowStartUtc.Date;
        var endDateUtc = windowEndUtc.Date;
        for (var currentDateUtc = startDateUtc; currentDateUtc <= endDateUtc; currentDateUtc = currentDateUtc.AddDays(1))
        {
            var daysSinceAnchor = (int)(currentDateUtc - anchorUtc.Date).TotalDays;
            if (daysSinceAnchor < 0 || daysSinceAnchor % Math.Max(1, rule.IntervalDays) != 0)
            {
                continue;
            }

            var occurrenceUtc = currentDateUtc.AddHours(Math.Clamp(rule.OccurrenceHourUtc, 0, 23));
            if (occurrenceUtc > windowStartUtc && occurrenceUtc <= windowEndUtc)
            {
                yield return occurrenceUtc;
            }
        }
    }

    private static IEnumerable<DateTime> EnumerateRecurringOccurrences(
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        DateTime anchorUtc,
        SimulationRecurringExpenseRule rule)
    {
        var cadence = NormalizeCadence(rule.Cadence);
        if (cadence == "monthly")
        {
            var monthCursor = new DateTime(windowStartUtc.Year, windowStartUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc);
            while (monthCursor <= windowEndUtc)
            {
                var day = Math.Min(Math.Max(1, rule.DayOfPeriod), DateTime.DaysInMonth(monthCursor.Year, monthCursor.Month));
                var occurrenceUtc = new DateTime(
                    monthCursor.Year,
                    monthCursor.Month,
                    day,
                    Math.Clamp(rule.OccurrenceHourUtc, 0, 23),
                    0,
                    0,
                    DateTimeKind.Utc);
                if (occurrenceUtc > windowStartUtc && occurrenceUtc <= windowEndUtc)
                {
                    yield return occurrenceUtc;
                }

                monthCursor = monthCursor.AddMonths(1);
            }

            yield break;
        }

        if (cadence == "biweekly")
        {
            var anchorOccurrenceUtc = anchorUtc.Date.AddDays(Math.Max(0, rule.DayOfPeriod - 1)).AddHours(Math.Clamp(rule.OccurrenceHourUtc, 0, 23));
            while (anchorOccurrenceUtc <= windowEndUtc)
            {
                if (anchorOccurrenceUtc > windowStartUtc)
                {
                    yield return anchorOccurrenceUtc;
                }

                anchorOccurrenceUtc = anchorOccurrenceUtc.AddDays(14);
            }

            yield break;
        }

        throw new InvalidOperationException($"Unsupported recurring simulation cadence '{rule.Cadence}'.");
    }

    private static string NormalizeCadence(string? cadence) =>
        string.IsNullOrWhiteSpace(cadence)
            ? "monthly"
            : cadence.Trim().ToLowerInvariant();

    private static string NormalizeCurrency(string? currency, string fallback) =>
        string.IsNullOrWhiteSpace(currency)
            ? fallback
            : currency.Trim().ToUpperInvariant();

    private static string NormalizeCategory(string? category) =>
        string.IsNullOrWhiteSpace(category)
            ? "operating_expense"
            : category.Trim();

    private static decimal Money(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static Guid ResolveCustomerId(SimulationRevenueRule rule, IReadOnlyList<FinanceCounterparty> customers) =>
        rule.CustomerId is Guid configuredCustomerId &&
        customers.Any(x => x.Id == configuredCustomerId)
            ? configuredCustomerId
            : customers[0].Id;

    private static Guid ResolveSupplierId(SimulationRecurringExpenseRule rule, IReadOnlyList<FinanceCounterparty> suppliers) =>
        rule.SupplierId is Guid configuredSupplierId &&
        suppliers.Any(x => x.Id == configuredSupplierId)
            ? configuredSupplierId
            : suppliers[0].Id;

    private static string ResolveCounterpartyReference(Guid counterpartyId, IReadOnlyList<FinanceCounterparty> counterparties) =>
        counterparties.FirstOrDefault(x => x.Id == counterpartyId)?.Name is { Length: > 0 } name
            ? name
            : counterpartyId.ToString("N");

    private static SimulationProfile CreateDefaultProfile(
        IReadOnlyList<FinanceCounterparty> customers,
        IReadOnlyList<FinanceCounterparty> suppliers,
        string currency) =>
        new()
        {
            RevenueRules =
            [
                new SimulationRevenueRule
                {
                    CustomerId = customers[0].Id,
                    Name = "Weekly consulting invoice",
                    Amount = 4200m,
                    Currency = currency,
                    IntervalDays = 7,
                    InvoiceDueDays = 14,
                    PaymentDelayDays = 5,
                    OccurrenceHourUtc = 10
                }
            ],
            RecurringExpenseRules =
            [
                new SimulationRecurringExpenseRule
                {
                    SupplierId = suppliers[0].Id,
                    CategoryId = "cloud_hosting",
                    Name = "Monthly cloud hosting",
                    Amount = 1250m,
                    Currency = currency,
                    Cadence = "monthly",
                    DayOfPeriod = 1,
                    OccurrenceHourUtc = 9,
                    PaymentDelayHours = 2
                },
                new SimulationRecurringExpenseRule
                {
                    SupplierId = suppliers[Math.Min(1, suppliers.Count - 1)].Id,
                    CategoryId = "rent",
                    Name = "Monthly office rent",
                    Amount = 3200m,
                    Currency = currency,
                    Cadence = "monthly",
                    DayOfPeriod = 1,
                    OccurrenceHourUtc = 8,
                    PaymentDelayHours = 1
                },
                new SimulationRecurringExpenseRule
                {
                    SupplierId = suppliers[Math.Min(2, suppliers.Count - 1)].Id,
                    CategoryId = "telecom",
                    Name = "Biweekly telecom service",
                    Amount = 275m,
                    Currency = currency,
                    Cadence = "biweekly",
                    DayOfPeriod = 3,
                    OccurrenceHourUtc = 11,
                    PaymentDelayHours = 2
                },
                new SimulationRecurringExpenseRule
                {
                    SupplierId = suppliers[Math.Min(3, suppliers.Count - 1)].Id,
                    CategoryId = "insurance",
                    Name = "Monthly insurance premium",
                    Amount = 640m,
                    Currency = currency,
                    Cadence = "monthly",
                    DayOfPeriod = 15,
                    OccurrenceHourUtc = 12,
                    PaymentDelayHours = 2
                }
            ]
        };

    private static void NormalizeProfile(
        SimulationProfile profile,
        IReadOnlyList<FinanceCounterparty> customers,
        IReadOnlyList<FinanceCounterparty> suppliers,
        string currency)
    {
        if (profile.RevenueRules.Count == 0)
        {
            profile.RevenueRules.Add(new SimulationRevenueRule
            {
                CustomerId = customers[0].Id,
                Name = "Weekly consulting invoice",
                Amount = 4200m,
                Currency = currency,
                IntervalDays = 7,
                InvoiceDueDays = 14,
                PaymentDelayDays = 5,
                OccurrenceHourUtc = 10
            });
        }

        foreach (var rule in profile.RevenueRules)
        {
            if (rule.CustomerId is null || customers.All(x => x.Id != rule.CustomerId.Value))
            {
                rule.CustomerId = customers[0].Id;
            }

            rule.Name = string.IsNullOrWhiteSpace(rule.Name) ? "Scheduled revenue invoice" : rule.Name.Trim();
            rule.Currency = NormalizeCurrency(rule.Currency, currency);
            rule.IntervalDays = Math.Max(1, rule.IntervalDays);
            rule.InvoiceDueDays = Math.Max(1, rule.InvoiceDueDays);
            rule.PaymentDelayDays = Math.Max(0, rule.PaymentDelayDays);
            rule.OccurrenceHourUtc = Math.Clamp(rule.OccurrenceHourUtc, 0, 23);
        }

        if (profile.RecurringExpenseRules.Count == 0)
        {
            profile.RecurringExpenseRules.AddRange(CreateDefaultProfile(customers, suppliers, currency).RecurringExpenseRules);
        }

        foreach (var rule in profile.RecurringExpenseRules)
        {
            if (rule.SupplierId is null || suppliers.All(x => x.Id != rule.SupplierId.Value))
            {
                rule.SupplierId = suppliers[0].Id;
            }

            rule.Name = string.IsNullOrWhiteSpace(rule.Name) ? "Recurring expense" : rule.Name.Trim();
            rule.CategoryId = NormalizeCategory(rule.CategoryId);
            rule.Currency = NormalizeCurrency(rule.Currency, currency);
            rule.Cadence = NormalizeCadence(rule.Cadence);
            rule.DayOfPeriod = Math.Max(1, rule.DayOfPeriod);
            rule.OccurrenceHourUtc = Math.Clamp(rule.OccurrenceHourUtc, 0, 23);
            rule.PaymentDelayHours = Math.Max(1, rule.PaymentDelayHours);
        }
    }

    private static string NextInvoiceNumber(CompanySimulationState state) =>
        $"SIM-INV-{state.InvoiceSequence++:000000}";

    private static string NextBillNumber(CompanySimulationState state) =>
        $"SIM-BILL-{state.BillSequence++:000000}";

    private static string NextExternalReference(string prefix, CompanySimulationState state) =>
        $"{prefix}-{state.ReferenceSequence++:000000}".Substring(0, Math.Min($"{prefix}-{state.ReferenceSequence:000000}".Length, 100));

    private static CompanySimulationClockDto MapClock(Guid companyId, CompanySimulationState state) =>
        new(
            companyId,
            state.CurrentUtc,
            state.Enabled,
            state.AutoAdvanceEnabled,
            state.DefaultStepHours,
            state.AutoAdvanceIntervalSeconds,
            state.LastAdvancedUtc);

    private async Task<Company> LoadCompanyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }

        var company = await _dbContext.Companies
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.Id == companyId, cancellationToken);
        return company ?? throw new KeyNotFoundException($"Company '{companyId}' was not found.");
    }

    private void EnsureTenant(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }

        if (_companyContextAccessor?.IsResolved == true &&
            _companyContextAccessor.CompanyId is Guid resolvedCompanyId &&
            resolvedCompanyId != companyId)
        {
            throw new UnauthorizedAccessException("Company simulation operations are scoped to the active company context.");
        }
    }

    private void ValidateAdvanceCommand(AdvanceCompanySimulationTimeCommand command)
    {
        if (command.TotalHours <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(command.TotalHours), "Total simulated time must be at least one hour.");
        }

        var executionStepHours = command.ExecutionStepHours ?? _options.DefaultStepHours;
        if (executionStepHours < 1 || executionStepHours > _options.MaxStepHours)
        {
            throw new ArgumentOutOfRangeException(nameof(command.ExecutionStepHours), "Execution step hours must be between 1 hour and 7 days.");
        }

        if (!command.Accelerated && command.TotalHours > _options.MaxStepHours)
        {
            throw new ArgumentOutOfRangeException(nameof(command.TotalHours), "Non-accelerated simulation advances must be between 1 hour and 7 days.");
        }

        if (command.Accelerated && !_options.AllowAcceleratedExecution && command.TotalHours > _options.MaxStepHours)
        {
            throw new InvalidOperationException("Accelerated simulation execution is disabled.");
        }
    }

    private void EnsureBackendExecutionEnabled(Guid companyId, string operationName)
    {
        if (_featureGate?.IsBackendExecutionEnabled() == false)
        {
            var featureState = _featureGate.GetState();
            _logger.LogInformation(
                "Blocked simulation execution for company {CompanyId} on {OperationName} because simulation execution is disabled. BackendExecutionEnabled={BackendExecutionEnabled}, BackgroundJobsEnabled={BackgroundJobsEnabled}.",
                companyId,
                operationName,
                featureState.BackendExecutionEnabled,
                featureState.BackgroundJobsEnabled);

            _featureGate.EnsureBackendExecutionEnabled();
        }
    }

    private static DateTime EnsureUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    private static T? ReadExtension<T>(Company company, string key)
        where T : class
    {
        if (!company.Settings.Extensions.TryGetValue(key, out var node) || node is null)
        {
            return null;
        }

        return node.Deserialize<T>(SerializerOptions);
    }

    private static void SaveClockState(Company company, CompanySimulationState state) =>
        SaveExtension(company, ClockExtensionKey, state);

    private static void SaveExtension<T>(Company company, string key, T value)
    {
        company.Settings.Extensions[key] = JsonSerializer.SerializeToNode(value, SerializerOptions);
    }

    private sealed record SimulationContext(
        FinanceAccount OperatingCashAccount,
        string Currency,
        IReadOnlyList<FinanceCounterparty> Customers,
        IReadOnlyList<FinanceCounterparty> Suppliers,
        List<FinanceInvoice> KnownInvoices,
        HashSet<Guid> SettledInvoiceIds,
        SimulationProfile Profile);

    private sealed class CompanySimulationState
    {
        public bool Enabled { get; set; } = true;
        public bool AutoAdvanceEnabled { get; set; }
        public int DefaultStepHours { get; set; } = 24;
        public int AutoAdvanceIntervalSeconds { get; set; }
        public DateTime AnchorUtc { get; set; }
        public DateTime CurrentUtc { get; set; }
        public DateTime? LastAdvancedUtc { get; set; }
        public int InvoiceSequence { get; set; } = 1;
        public int BillSequence { get; set; } = 1;
        public int ReferenceSequence { get; set; } = 1;
    }

    private sealed class SimulationProfile
    {
        public List<SimulationRevenueRule> RevenueRules { get; set; } = [];
        public List<SimulationRecurringExpenseRule> RecurringExpenseRules { get; set; } = [];
    }

    private sealed class SimulationRevenueRule
    {
        public Guid? CustomerId { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Amount { get; set; } = 4200m;
        public string Currency { get; set; } = "USD";
        public int IntervalDays { get; set; } = 7;
        public int InvoiceDueDays { get; set; } = 14;
        public int PaymentDelayDays { get; set; } = 5;
        public int OccurrenceHourUtc { get; set; } = 10;
    }

    private sealed class SimulationRecurringExpenseRule
    {
        public Guid? SupplierId { get; set; }
        public string CategoryId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal Amount { get; set; } = 100m;
        public string Currency { get; set; } = "USD";
        public string Cadence { get; set; } = "monthly";
        public int DayOfPeriod { get; set; } = 1;
        public int OccurrenceHourUtc { get; set; } = 9;
        public int PaymentDelayHours { get; set; } = 2;
    }
}
