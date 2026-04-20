using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanySimulationFinanceGenerationService : IFinanceGenerationPolicy
{
    private const string FinanceApproverRole = "finance_approver";
    private const string FinanceWorkflowQueue = "finance_workflow_queue";
    private const string InvoiceReviewTaskType = "invoice_approval_review";
    private const string BillReviewTaskType = "bill_approval_review";
    private const string AnomalyTaskType = "finance_transaction_anomaly_follow_up";
    private const string SystemActorType = "system";
    private const string DefaultApprovalCurrency = "USD";
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private static readonly InvoiceScenario[] InvoiceScenarioCatalog =
    [
        InvoiceScenario.PendingOverThreshold,
        InvoiceScenario.DifferentApprovalCurrency,
        InvoiceScenario.PartialPayment,
        InvoiceScenario.FullPayment,
        InvoiceScenario.OverPayment,
        InvoiceScenario.DueSoon,
        InvoiceScenario.Overdue,
        InvoiceScenario.LowRiskPending
    ];

    private static readonly ThresholdCase[] ThresholdCaseCatalog =
    [
        ThresholdCase.JustBelow,
        ThresholdCase.ExactlyAt,
        ThresholdCase.JustAbove,
        ThresholdCase.HumanApprovalRequired,
        ThresholdCase.EligibleWithoutEscalation,
        ThresholdCase.AlreadyApprovedOrNotActionable
    ];

    private static readonly FinanceAnomalyKind[] AnomalyCatalog =
    [
        FinanceAnomalyKind.DuplicateVendorCharge,
        FinanceAnomalyKind.UnusuallyHighAmount,
        FinanceAnomalyKind.CategoryMismatch,
        FinanceAnomalyKind.MissingDocument,
        FinanceAnomalyKind.SuspiciousPaymentTiming,
        FinanceAnomalyKind.MultiplePayments,
        FinanceAnomalyKind.PaymentBeforeExpectedStateTransition
    ];

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CompanySimulationFinanceGenerationService> _logger;
    private readonly IFinanceScenarioFactory _scenarioFactory;
    private readonly IFinanceAnomalyScheduleFactory _anomalyScheduleFactory;
    private readonly ISimulationFeatureGate? _featureGate;
    private readonly ICompanyOutboxEnqueuer? _outboxEnqueuer;

    public CompanySimulationFinanceGenerationService(
        VirtualCompanyDbContext dbContext,
        TimeProvider timeProvider,
        ILogger<CompanySimulationFinanceGenerationService> logger)
        : this(
            dbContext,
            timeProvider,
            logger,
            new DefaultFinanceScenarioFactory(new Sha256FinanceDeterministicValueSource()),
            new PeriodicFinanceAnomalyScheduleFactory(new Sha256FinanceDeterministicValueSource()),
            null,
            null)
    {
    }

    public CompanySimulationFinanceGenerationService(
        VirtualCompanyDbContext dbContext,
        TimeProvider timeProvider,
        ILogger<CompanySimulationFinanceGenerationService> logger,
        ICompanyOutboxEnqueuer? outboxEnqueuer)
        : this(
            dbContext,
            timeProvider,
            logger,
            new DefaultFinanceScenarioFactory(new Sha256FinanceDeterministicValueSource()),
            new PeriodicFinanceAnomalyScheduleFactory(new Sha256FinanceDeterministicValueSource()),
            null,
            outboxEnqueuer)
    {
    }

    public CompanySimulationFinanceGenerationService(
        VirtualCompanyDbContext dbContext,
        TimeProvider timeProvider,
        ILogger<CompanySimulationFinanceGenerationService> logger,
        IFinanceScenarioFactory scenarioFactory,
        IFinanceAnomalyScheduleFactory anomalyScheduleFactory,
        ISimulationFeatureGate? featureGate,
        ICompanyOutboxEnqueuer? outboxEnqueuer)
    {
        _dbContext = dbContext;
        _timeProvider = timeProvider;
        _logger = logger;
        _scenarioFactory = scenarioFactory;
        _anomalyScheduleFactory = anomalyScheduleFactory;
        _featureGate = featureGate;
        _outboxEnqueuer = outboxEnqueuer;
    }

    public async Task<CompanySimulationFinanceGenerationResultDto> GenerateAsync(
        GenerateCompanySimulationFinanceCommand command,
        CancellationToken cancellationToken)
    {
        EnsureBackendExecutionEnabled(command.CompanyId, command.ActiveSessionId);

        Validate(command);

        var company = await _dbContext.Companies
            .IgnoreQueryFilters()
            .SingleAsync(x => x.Id == command.CompanyId, cancellationToken);
        var policy = await LoadPolicyAsync(command.CompanyId, cancellationToken);
        var config = FinanceGenerationConfiguration.Parse(command.DeterministicConfigurationJson);
        var account = await EnsureOperatingCashAccountAsync(company, command.StartSimulatedUtc, policy, cancellationToken);
        var financeActorId = await ResolveFinanceActorIdAsync(command.CompanyId, cancellationToken);
        var counterparties = await EnsureCounterpartiesAsync(company, command.StartSimulatedUtc, cancellationToken);

        var invoicesCreated = 0;
        var billsCreated = 0;
        var transactionsCreated = 0;
        var balancesCreated = 0;
        var recurringExpenseInstancesCreated = 0;
        var workflowTasksCreated = 0;
        var approvalRequestsCreated = 0;
        var auditEventsCreated = 0;
        var activityEventsCreated = 0;
        var alertsCreated = 0;
        var daysProcessed = 0;
        var dailyLogs = new List<CompanySimulationFinanceGenerationDayLogDto>();

        foreach (var simulatedDateUtc in EnumerateSimulatedDates(command.PreviousSimulatedUtc, command.CurrentSimulatedUtc))
        {
            var dayIndex = (int)(simulatedDateUtc.Date - command.StartSimulatedUtc.Date).TotalDays;
            if (dayIndex < 0)
            {
                continue;
            }

            daysProcessed++;
            var plan = BuildDayPlan(company, simulatedDateUtc, dayIndex, command, policy, config, counterparties);
            var applied = await ApplyDayPlanAsync(
                company,
                command,
                policy,
                plan,
                account,
                financeActorId,
                cancellationToken);

            invoicesCreated += applied.InvoicesCreated;
            billsCreated += applied.BillsCreated;
            transactionsCreated += applied.TransactionsCreated;
            balancesCreated += applied.BalancesCreated;
            recurringExpenseInstancesCreated += applied.RecurringExpenseInstancesCreated;
            workflowTasksCreated += applied.WorkflowTasksCreated;
            approvalRequestsCreated += applied.ApprovalRequestsCreated;
            auditEventsCreated += applied.AuditEventsCreated;
            activityEventsCreated += applied.ActivityEventsCreated;
            alertsCreated += applied.AlertsCreated;
            dailyLogs.Add(applied.ToDayLog());
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Generated deterministic finance activity for company {CompanyId}, session {SessionId}. Days={DaysProcessed}, invoices={InvoicesCreated}, bills={BillsCreated}, transactions={TransactionsCreated}, balances={BalancesCreated}, tasks={WorkflowTasksCreated}, approvals={ApprovalRequestsCreated}, alerts={AlertsCreated}.",
            command.CompanyId,
            command.ActiveSessionId,
            daysProcessed,
            invoicesCreated,
            billsCreated,
            transactionsCreated,
            balancesCreated,
            workflowTasksCreated,
            approvalRequestsCreated,
            alertsCreated);

        return new CompanySimulationFinanceGenerationResultDto(
            command.CompanyId,
            command.ActiveSessionId,
            daysProcessed,
            invoicesCreated,
            billsCreated,
            transactionsCreated,
            balancesCreated,
            recurringExpenseInstancesCreated,
            workflowTasksCreated,
            approvalRequestsCreated,
            auditEventsCreated,
            activityEventsCreated,
            alertsCreated,
            dailyLogs);
    }

    private async Task<DayApplyResult> ApplyDayPlanAsync(
        Company company,
        GenerateCompanySimulationFinanceCommand command,
        FinancePolicyConfigurationDto policy,
        FinanceDayPlan plan,
        FinanceAccount account,
        Guid financeActorId,
        CancellationToken cancellationToken)
    {
        var invoicesCreated = 0;
        var billsCreated = 0;
        var transactionsCreated = 0;
        var balancesCreated = 0;
        var recurringExpenseInstancesCreated = 0;
        var workflowTasksCreated = 0;
        var approvalRequestsCreated = 0;
        var auditEventsCreated = 0;
        var activityEventsCreated = 0;
        var alertsCreated = 0;

        var invoice = await _dbContext.FinanceInvoices
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == company.Id && x.InvoiceNumber == plan.InvoiceNumber, cancellationToken);

        if (invoice is null)
        {
            invoice = new FinanceInvoice(
                DeterministicGuid(company.Id, $"invoice:{plan.InvoiceNumber}"),
                company.Id,
                plan.CustomerId,
                plan.InvoiceNumber,
                plan.IssuedUtc,
                plan.DueUtc,
                plan.InvoiceAmount,
                plan.InvoiceCurrency,
                plan.InvoiceStatus,
                createdUtc: plan.IssuedUtc,
                updatedUtc: plan.IssuedUtc);
            _dbContext.FinanceInvoices.Add(invoice);
            FinanceDomainEvents.EnqueueInvoiceCreated(_outboxEnqueuer, invoice, plan.CustomerName, BuildDayCorrelationId(company.Id, plan.DateUtc));
            invoicesCreated++;

            if (await EnsureAuditEventAsync(
                    company.Id,
                    DeterministicGuid(company.Id, $"audit:invoice:{plan.InvoiceNumber}"),
                    "finance.invoice.generated",
                    "finance_invoice",
                    invoice.Id.ToString("N"),
                    BuildDayCorrelationId(company.Id, plan.DateUtc),
                    plan.IssuedUtc,
                    financeActorId,
                    new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["invoiceNumber"] = plan.InvoiceNumber,
                        ["scenario"] = plan.InvoiceScenarioToken,
                        ["thresholdCase"] = plan.ThresholdCaseToken
                    },
                    cancellationToken))
            {
                auditEventsCreated++;
            }
        }

        var bill = await _dbContext.FinanceBills
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == company.Id && x.BillNumber == plan.BillNumber, cancellationToken);

        if (bill is null)
        {
            bill = new FinanceBill(
                DeterministicGuid(company.Id, $"bill:{plan.BillNumber}"),
                company.Id,
                plan.SupplierId,
                plan.BillNumber,
                plan.ReceivedUtc,
                plan.BillDueUtc,
                plan.BillAmount,
                policy.ApprovalCurrency,
                plan.BillStatus,
                createdUtc: plan.ReceivedUtc,
                updatedUtc: plan.ReceivedUtc);
            _dbContext.FinanceBills.Add(bill);
            billsCreated++;
            recurringExpenseInstancesCreated++;

            if (await EnsureAuditEventAsync(
                    company.Id,
                    DeterministicGuid(company.Id, $"audit:bill:{plan.BillNumber}"),
                    "finance.bill.generated",
                    "finance_bill",
                    bill.Id.ToString("N"),
                    BuildDayCorrelationId(company.Id, plan.DateUtc),
                    plan.ReceivedUtc,
                    financeActorId,
                    new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["billNumber"] = plan.BillNumber,
                        ["supplier"] = plan.SupplierName,
                        ["thresholdCase"] = plan.ThresholdCaseToken,
                        ["isRecurringExpenseInstance"] = bool.TrueString
                    },
                    cancellationToken))
            {
                auditEventsCreated++;
            }
        }

        foreach (var payment in plan.Transactions)
        {
            var transaction = await _dbContext.FinanceTransactions
                .IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.CompanyId == company.Id && x.ExternalReference == payment.ExternalReference, cancellationToken);

            if (transaction is not null)
            {
                continue;
            }

            transaction = new FinanceTransaction(
                DeterministicGuid(company.Id, $"transaction:{payment.ExternalReference}"),
                company.Id,
                account.Id,
                payment.CounterpartyId,
                payment.InvoiceId,
                payment.BillId,
                payment.TransactionUtc,
                payment.Category,
                payment.Amount,
                payment.Currency,
                payment.Description,
                payment.ExternalReference,
                createdUtc: payment.TransactionUtc);

            _dbContext.FinanceTransactions.Add(transaction);
            FinanceDomainEvents.EnqueueTransactionCreated(_outboxEnqueuer, transaction, BuildDayCorrelationId(company.Id, plan.DateUtc));
            transactionsCreated++;

            if (await EnsureAuditEventAsync(
                    company.Id,
                    DeterministicGuid(company.Id, $"audit:transaction:{payment.ExternalReference}"),
                    "finance.transaction.generated",
                    "finance_transaction",
                    transaction.Id.ToString("N"),
                    BuildDayCorrelationId(company.Id, plan.DateUtc),
                    payment.TransactionUtc,
                    financeActorId,
                    new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["externalReference"] = payment.ExternalReference,
                        ["scenario"] = plan.InvoiceScenarioToken,
                        ["amount"] = payment.Amount.ToString("0.00"),
                        ["currency"] = payment.Currency
                    },
                    cancellationToken))
            {
                auditEventsCreated++;
            }
        }

        var invoiceTask = await EnsureWorkflowTaskAsync(
            company.Id,
            plan.InvoiceTaskId,
            InvoiceReviewTaskType,
            $"Review invoice {plan.InvoiceNumber}",
            $"Review generated invoice {plan.InvoiceNumber} for scenario {plan.InvoiceScenarioToken}.",
            plan.InvoiceApprovalRequired ? WorkTaskPriority.High : WorkTaskPriority.Normal,
            financeActorId,
            BuildTaskCorrelationId(company.Id, "invoice", plan.InvoiceNumber),
            plan.InvoicePayload,
            plan.InvoiceApprovalRequired ? plan.InvoicePayload : plan.InvoiceResultPayload,
            plan.InvoiceApprovalRequired ? WorkTaskStatus.AwaitingApproval : WorkTaskStatus.Completed,
            plan.DueUtc,
            cancellationToken);

        if (invoiceTask.Created)
        {
            workflowTasksCreated++;
        }

        if (plan.InvoiceApprovalRequired)
        {
            var approval = await EnsureApprovalRequestAsync(
                company.Id,
                plan.InvoiceApprovalId,
                invoiceTask.Task.Id,
                "invoice_review",
                financeActorId,
                plan.InvoicePayload,
                cancellationToken);
            if (approval.Created)
            {
                approvalRequestsCreated++;
            }

            if (await EnsureAuditEventAsync(
                    company.Id,
                    DeterministicGuid(company.Id, $"audit:approval:invoice:{plan.InvoiceNumber}"),
                    "finance.approval.generated",
                    "approval_request",
                    approval.Approval.Id.ToString("N"),
                    BuildTaskCorrelationId(company.Id, "invoice", plan.InvoiceNumber),
                    plan.IssuedUtc,
                    financeActorId,
                    new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["approvalType"] = "invoice_review",
                        ["targetTaskId"] = invoiceTask.Task.Id.ToString("N"),
                        ["invoiceNumber"] = plan.InvoiceNumber
                    },
                    cancellationToken))
            {
                auditEventsCreated++;
            }
        }

        var billTask = await EnsureWorkflowTaskAsync(
            company.Id,
            plan.BillTaskId,
            BillReviewTaskType,
            $"Review bill {plan.BillNumber}",
            $"Review generated bill {plan.BillNumber} for threshold case {plan.ThresholdCaseToken}.",
            plan.BillApprovalRequired ? WorkTaskPriority.High : WorkTaskPriority.Normal,
            financeActorId,
            BuildTaskCorrelationId(company.Id, "bill", plan.BillNumber),
            plan.BillPayload,
            plan.BillApprovalRequired ? plan.BillPayload : plan.BillResultPayload,
            plan.BillApprovalRequired ? WorkTaskStatus.AwaitingApproval : WorkTaskStatus.Completed,
            plan.BillDueUtc,
            cancellationToken);

        if (billTask.Created)
        {
            workflowTasksCreated++;
        }

        if (plan.BillApprovalRequired)
        {
            var approval = await EnsureApprovalRequestAsync(
                company.Id,
                plan.BillApprovalId,
                billTask.Task.Id,
                "bill_review",
                financeActorId,
                plan.BillPayload,
                cancellationToken);
            if (approval.Created)
            {
                approvalRequestsCreated++;
            }
        }

        var balanceSnapshotUtc = plan.DateUtc.Date.AddHours(23);
        var existingBalance = await _dbContext.FinanceBalances
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x => x.CompanyId == company.Id &&
                     x.AccountId == account.Id &&
                     x.AsOfUtc == balanceSnapshotUtc,
                cancellationToken);

        if (existingBalance is null)
        {
            var runningBalance = await CalculateBalanceAsync(company.Id, account.Id, balanceSnapshotUtc, cancellationToken);
            existingBalance = new FinanceBalance(
                DeterministicGuid(company.Id, $"balance:{account.Id:N}:{balanceSnapshotUtc:yyyyMMddHH}"),
                company.Id,
                account.Id,
                balanceSnapshotUtc,
                runningBalance,
                account.Currency,
                createdUtc: balanceSnapshotUtc);
            _dbContext.FinanceBalances.Add(existingBalance);
            balancesCreated++;

            if (await EnsureAuditEventAsync(
                    company.Id,
                    DeterministicGuid(company.Id, $"audit:balance:{balanceSnapshotUtc:yyyyMMddHH}"),
                    "finance.balance.generated",
                    "finance_balance",
                    existingBalance.Id.ToString("N"),
                    BuildDayCorrelationId(company.Id, plan.DateUtc),
                    balanceSnapshotUtc,
                    financeActorId,
                    new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["accountId"] = account.Id.ToString("N"),
                        ["asOfUtc"] = balanceSnapshotUtc.ToString("O"),
                        ["amount"] = runningBalance.ToString("0.00")
                    },
                    cancellationToken))
            {
                auditEventsCreated++;
            }
        }

        if (plan.AnomalyType is not null)
        {
            var anomalyCorrelationId = BuildAnomalyCorrelationId(
                company.Id,
                plan.AnomalyTransactionId!.Value,
                plan.AnomalyTypeToken!,
                plan.DateUtc);

            await EnsureSeedAnomalyAsync(company.Id, plan, anomalyCorrelationId, cancellationToken);

            var alert = await EnsureAnomalyAlertAsync(
                company.Id,
                plan,
                anomalyCorrelationId,
                financeActorId,
                cancellationToken);

            if (alert.Created)
            {
                alertsCreated++;
            }

            var anomalyTask = await EnsureWorkflowTaskAsync(
                company.Id,
                plan.AnomalyTaskId,
                AnomalyTaskType,
                $"Review anomalous transaction {plan.AnomalyTransactionExternalReference}",
                plan.AnomalyExplanation!,
                WorkTaskPriority.High,
                financeActorId,
                anomalyCorrelationId,
                plan.AnomalyPayload,
                plan.AnomalyPayload,
                WorkTaskStatus.AwaitingApproval,
                plan.DateUtc.AddDays(2),
                cancellationToken);

            if (anomalyTask.Created)
            {
                workflowTasksCreated++;
            }

            if (await EnsureAuditEventAsync(
                    company.Id,
                    DeterministicGuid(company.Id, $"audit:anomaly:{plan.DateUtc:yyyyMMdd}:{plan.AnomalyTypeToken}"),
                    "finance.anomaly.generated",
                    "finance_transaction",
                    plan.AnomalyTransactionId!.Value.ToString("N"),
                    anomalyCorrelationId,
                    plan.DateUtc.AddHours(17),
                    financeActorId,
                    new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["anomalyType"] = plan.AnomalyTypeToken,
                        ["alertId"] = alert.Alert.Id.ToString("N"),
                        ["followUpTaskId"] = anomalyTask.Task.Id.ToString("N")
                    },
                    cancellationToken))
            {
                auditEventsCreated++;
            }
        }

        if (await EnsureActivityEventAsync(
                company.Id,
                plan.ActivityEventId,
                financeActorId == Guid.Empty ? null : financeActorId,
                "finance.simulation.day_generated",
                plan.DateUtc.AddHours(18),
                BuildDayCorrelationId(company.Id, plan.DateUtc),
                invoiceTask.Task.Id,
                DeterministicGuid(company.Id, $"audit:day:{plan.DateUtc:yyyyMMdd}"),
                plan.ActivityMetadata,
                cancellationToken))
        {
            activityEventsCreated++;
        }

        return new DayApplyResult(
            plan.DateUtc,
            plan.AnomalyTypeToken,
            invoicesCreated,
            billsCreated,
            transactionsCreated,
            balancesCreated,
            recurringExpenseInstancesCreated,
            workflowTasksCreated,
            approvalRequestsCreated,
            auditEventsCreated,
            activityEventsCreated,
            alertsCreated);
    }

    private FinanceDayPlan BuildDayPlan(
        Company company,
        DateTime simulatedDateUtc,
        int dayIndex,
        GenerateCompanySimulationFinanceCommand command,
        FinancePolicyConfigurationDto policy,
        FinanceGenerationConfiguration config,
        CounterpartyCatalog counterparties)
    {
        var generationContext = new FinanceDeterministicGenerationContext(
            company.Id,
            command.Seed,
            command.StartSimulatedUtc,
            simulatedDateUtc.Date,
            dayIndex,
            command.DeterministicConfigurationJson);
        var scenarioSelection = _scenarioFactory.Create(
            generationContext,
            InvoiceScenarioCatalog.Length,
            ThresholdCaseCatalog.Length,
            counterparties.Customers.Count,
            counterparties.Suppliers.Count);
        var invoiceScenario = InvoiceScenarioCatalog[scenarioSelection.InvoiceScenarioIndex];
        var thresholdCase = ThresholdCaseCatalog[scenarioSelection.ThresholdCaseIndex];
        var customer = counterparties.Customers[scenarioSelection.CustomerIndex];
        var supplier = counterparties.Suppliers[scenarioSelection.SupplierIndex];

        var invoiceAmount = ResolveInvoiceAmount(invoiceScenario, thresholdCase, policy.InvoiceApprovalThreshold);
        var billAmount = ResolveBillAmount(thresholdCase, policy.BillApprovalThreshold);
        var invoiceCurrency = invoiceScenario == InvoiceScenario.DifferentApprovalCurrency
            ? ResolveAlternateCurrency(policy.ApprovalCurrency)
            : policy.ApprovalCurrency;

        var invoiceStatus = ResolveInvoiceStatus(invoiceScenario, thresholdCase);
        var billStatus = ResolveBillStatus(thresholdCase);
        var invoiceDueUtc = ResolveInvoiceDueUtc(simulatedDateUtc, invoiceScenario);
        var billDueUtc = simulatedDateUtc.Date.AddDays(14);

        var invoiceNumber = $"SIM-INV-{simulatedDateUtc:yyyyMMdd}-{ToToken(invoiceScenario)}";
        var billNumber = $"SIM-BILL-{simulatedDateUtc:yyyyMMdd}-{ToToken(thresholdCase)}";
        var invoiceId = DeterministicGuid(company.Id, $"invoice:{invoiceNumber}");
        var billId = DeterministicGuid(company.Id, $"bill:{billNumber}");

        var invoiceApprovalRequired =
            string.Equals(invoiceStatus, "pending_approval", StringComparison.OrdinalIgnoreCase) ||
            invoiceAmount > policy.InvoiceApprovalThreshold ||
            !string.Equals(invoiceCurrency, policy.ApprovalCurrency, StringComparison.OrdinalIgnoreCase);

        var billApprovalRequired =
            billAmount > policy.BillApprovalThreshold ||
            thresholdCase == ThresholdCase.JustAbove ||
            thresholdCase == ThresholdCase.HumanApprovalRequired;

        var transactions = BuildTransactions(
            company.Id,
            simulatedDateUtc,
            thresholdCase,
            invoiceScenario,
            invoiceId,
            billId,
            customer,
            supplier,
            invoiceAmount,
            billAmount,
            invoiceCurrency,
            policy.ApprovalCurrency,
            invoiceNumber,
            billNumber);

        var anomalySchedule = _anomalyScheduleFactory.Create(
            generationContext,
            AnomalyCatalog.Length,
            transactions.Count,
            config.AnomalyCadenceDays,
            config.AnomalyOffsetDays);
        var anomalyType = anomalySchedule.IsAnomalyDay && anomalySchedule.AnomalyIndex.HasValue
            ? AnomalyCatalog[anomalySchedule.AnomalyIndex.Value]
            : (FinanceAnomalyKind?)null;
        var anomalyTransaction = ResolveAnomalyTransaction(
            transactions,
            anomalyType,
            anomalySchedule.TargetTransactionIndex);

        var invoicePayload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["invoiceId"] = JsonValue.Create(invoiceId),
            ["invoiceNumber"] = JsonValue.Create(invoiceNumber),
            ["invoiceScenario"] = JsonValue.Create(ToToken(invoiceScenario)),
            ["thresholdCase"] = JsonValue.Create(ToToken(thresholdCase)),
            ["amount"] = JsonValue.Create(invoiceAmount),
            ["currency"] = JsonValue.Create(invoiceCurrency),
            ["simulatedDateUtc"] = JsonValue.Create(simulatedDateUtc.Date),
            ["approvalThreshold"] = JsonValue.Create(policy.InvoiceApprovalThreshold),
            ["approvalCurrency"] = JsonValue.Create(policy.ApprovalCurrency)
        };

        var billPayload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["billId"] = JsonValue.Create(billId),
            ["billNumber"] = JsonValue.Create(billNumber),
            ["thresholdCase"] = JsonValue.Create(ToToken(thresholdCase)),
            ["amount"] = JsonValue.Create(billAmount),
            ["currency"] = JsonValue.Create(policy.ApprovalCurrency),
            ["simulatedDateUtc"] = JsonValue.Create(simulatedDateUtc.Date),
            ["billApprovalThreshold"] = JsonValue.Create(policy.BillApprovalThreshold)
        };

        var invoiceResultPayload = new Dictionary<string, JsonNode?>(invoicePayload, StringComparer.OrdinalIgnoreCase);
        FinanceWorkflowOutputSchemas.CopyToPayload(
            invoiceResultPayload,
            FinanceWorkflowOutputSchemas.Create(
                invoiceApprovalRequired ? "invoice_requires_review" : "invoice_auto_cleared",
                invoiceApprovalRequired ? "high" : "low",
                invoiceApprovalRequired ? "request_human_approval" : "monitor",
                $"Generated invoice scenario {ToToken(invoiceScenario)} on {simulatedDateUtc:yyyy-MM-dd}.",
                invoiceApprovalRequired ? 0.89m : 0.82m,
                "simulation_finance_generation"));

        var billResultPayload = new Dictionary<string, JsonNode?>(billPayload, StringComparer.OrdinalIgnoreCase);
        FinanceWorkflowOutputSchemas.CopyToPayload(
            billResultPayload,
            FinanceWorkflowOutputSchemas.Create(
                billApprovalRequired ? "bill_requires_review" : "bill_auto_cleared",
                billApprovalRequired ? "high" : "low",
                billApprovalRequired ? "request_human_approval" : "monitor",
                $"Generated bill threshold case {ToToken(thresholdCase)} on {simulatedDateUtc:yyyy-MM-dd}.",
                billApprovalRequired ? 0.88m : 0.80m,
                "simulation_finance_generation"));

        Dictionary<string, JsonNode?>? anomalyPayload = null;
        string? anomalyExplanation = null;
        Guid? anomalyTransactionId = null;
        string? anomalyTransactionExternalReference = null;
        string? anomalyTypeToken = null;

        if (anomalyType is not null && anomalyTransaction is not null)
        {
            anomalyTransactionId = DeterministicGuid(company.Id, $"transaction:{anomalyTransaction.ExternalReference}");
            anomalyTransactionExternalReference = anomalyTransaction.ExternalReference;
            anomalyTypeToken = ToToken(anomalyType.Value);
            anomalyExplanation = BuildAnomalyExplanation(anomalyType.Value, anomalyTransaction, supplier.Name);
            anomalyPayload = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["transactionId"] = JsonValue.Create(anomalyTransactionId.Value),
                ["transactionExternalReference"] = JsonValue.Create(anomalyTransaction.ExternalReference),
                ["anomalyType"] = JsonValue.Create(anomalyTypeToken),
                ["simulatedDateUtc"] = JsonValue.Create(simulatedDateUtc.Date),
                ["supplierName"] = JsonValue.Create(supplier.Name),
                ["dedupeKey"] = JsonValue.Create($"finance-transaction-anomaly:{anomalyTypeToken}"),
                ["deduplicationWindowStartUtc"] = JsonValue.Create(simulatedDateUtc.Date),
                ["deduplicationWindowEndUtc"] = JsonValue.Create(simulatedDateUtc.Date.AddDays(1)),
                ["recommendedAction"] = JsonValue.Create("Review generated anomaly evidence before approving downstream finance work.")
            };
            FinanceWorkflowOutputSchemas.CopyToPayload(
                anomalyPayload,
                FinanceWorkflowOutputSchemas.Create(
                    anomalyTypeToken,
                    "high",
                    "investigate",
                    anomalyExplanation,
                    0.91m,
                    "simulation_finance_generation"));
        }

        return new FinanceDayPlan(
            simulatedDateUtc.Date,
            invoiceScenario,
            thresholdCase,
            invoiceNumber,
            invoiceId,
            customer.Id,
            customer.Name,
            simulatedDateUtc.Date.AddHours(9),
            invoiceDueUtc,
            invoiceAmount,
            invoiceCurrency,
            invoiceStatus,
            billNumber,
            billId,
            supplier.Id,
            supplier.Name,
            simulatedDateUtc.Date.AddHours(8),
            billDueUtc,
            billAmount,
            billStatus,
            invoiceApprovalRequired,
            billApprovalRequired,
            transactions,
            anomalyType,
            anomalyTypeToken,
            anomalyTransactionId,
            anomalyTransactionExternalReference,
            anomalyExplanation,
            invoicePayload,
            invoiceResultPayload,
            billPayload,
            billResultPayload,
            anomalyPayload,
            DeterministicGuid(company.Id, $"task:invoice:{invoiceNumber}"),
            DeterministicGuid(company.Id, $"approval:invoice:{invoiceNumber}"),
            DeterministicGuid(company.Id, $"task:bill:{billNumber}"),
            DeterministicGuid(company.Id, $"approval:bill:{billNumber}"),
            DeterministicGuid(company.Id, $"task:anomaly:{simulatedDateUtc:yyyyMMdd}:{anomalyTypeToken ?? "none"}"),
            DeterministicGuid(company.Id, $"activity:{simulatedDateUtc:yyyyMMdd}"),
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["simulatedDateUtc"] = JsonValue.Create(simulatedDateUtc.Date),
                ["invoiceScenario"] = JsonValue.Create(ToToken(invoiceScenario)),
                ["thresholdCase"] = JsonValue.Create(ToToken(thresholdCase)),
                ["invoiceNumber"] = JsonValue.Create(invoiceNumber),
                ["billNumber"] = JsonValue.Create(billNumber),
                ["anomalyType"] = anomalyTypeToken is null ? null : JsonValue.Create(anomalyTypeToken)
            });
    }

    private async Task<FinancePolicyConfigurationDto> LoadPolicyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var policy = await _dbContext.FinancePolicyConfigurations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);

        return policy is null
            ? new FinancePolicyConfigurationDto(companyId, DefaultApprovalCurrency, 10000m, 5000m, true, -10000m, 10000m, 90, 30)
            : new FinancePolicyConfigurationDto(
                companyId,
                policy.ApprovalCurrency,
                policy.InvoiceApprovalThreshold,
                policy.BillApprovalThreshold,
                policy.RequireCounterpartyForTransactions,
                policy.AnomalyDetectionLowerBound,
                policy.AnomalyDetectionUpperBound,
                policy.CashRunwayWarningThresholdDays,
                policy.CashRunwayCriticalThresholdDays);
    }

    private async Task<FinanceAccount> EnsureOperatingCashAccountAsync(
        Company company,
        DateTime startSimulatedUtc,
        FinancePolicyConfigurationDto policy,
        CancellationToken cancellationToken)
    {
        var account = await _dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == company.Id && x.Code == "1000", cancellationToken);

        if (account is not null)
        {
            return account;
        }

        account = new FinanceAccount(
            DeterministicGuid(company.Id, "finance-account:operating-cash"),
            company.Id,
            "1000",
            "Operating Cash",
            "asset",
            string.IsNullOrWhiteSpace(company.Currency) ? policy.ApprovalCurrency : company.Currency,
            150000m,
            startSimulatedUtc.Date,
            createdUtc: startSimulatedUtc.Date,
            updatedUtc: startSimulatedUtc.Date);
        _dbContext.FinanceAccounts.Add(account);
        return account;
    }

    private async Task<CounterpartyCatalog> EnsureCounterpartiesAsync(
        Company company,
        DateTime startSimulatedUtc,
        CancellationToken cancellationToken)
    {
        var names = new[]
        {
            ("Northwind Retail", "customer"),
            ("Adventure Works", "customer"),
            ("Graphic Design Institute", "customer"),
            ("Contoso Cloud", "supplier"),
            ("Wide World Rentals", "supplier"),
            ("Tailspin Telecom", "supplier"),
            ("Northwind Insurance", "supplier")
        };

        var existing = await _dbContext.FinanceCounterparties
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == company.Id)
            .ToListAsync(cancellationToken);

        foreach (var (name, type) in names)
        {
            if (existing.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            var counterparty = new FinanceCounterparty(
                DeterministicGuid(company.Id, $"counterparty:{type}:{name}"),
                company.Id,
                name,
                type,
                $"{name.ToLowerInvariant().Replace(" ", ".")}@example.com",
                createdUtc: startSimulatedUtc.Date,
                updatedUtc: startSimulatedUtc.Date);
            _dbContext.FinanceCounterparties.Add(counterparty);
            existing.Add(counterparty);
        }

        return new CounterpartyCatalog(
            existing.Where(x => string.Equals(x.CounterpartyType, "customer", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            existing.Where(x => string.Equals(x.CounterpartyType, "supplier", StringComparison.OrdinalIgnoreCase) || string.Equals(x.CounterpartyType, "vendor", StringComparison.OrdinalIgnoreCase))
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList());
    }

    private async Task<decimal> CalculateBalanceAsync(
        Guid companyId,
        Guid accountId,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        var account = await _dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId && x.Id == accountId, cancellationToken);

        var transactionSum = await _dbContext.FinanceTransactions
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.AccountId == accountId && x.TransactionUtc <= asOfUtc)
            .SumAsync(x => (decimal?)x.Amount, cancellationToken) ?? 0m;

        return account.OpeningBalance + transactionSum;
    }

    private async Task<Guid> ResolveFinanceActorIdAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var agentId = await _dbContext.Agents
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                (x.TemplateId == "laura-finance" ||
                 x.Department == "Finance" ||
                 x.DisplayName.Contains("Laura")))
            .OrderByDescending(x => x.TemplateId == "laura-finance")
            .ThenBy(x => x.DisplayName)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return agentId ?? DeterministicGuid(companyId, "finance-system-actor");
    }

    private async Task<(WorkTask Task, bool Created)> EnsureWorkflowTaskAsync(
        Guid companyId,
        Guid taskId,
        string taskType,
        string title,
        string description,
        WorkTaskPriority priority,
        Guid financeActorId,
        string correlationId,
        Dictionary<string, JsonNode?> inputPayload,
        Dictionary<string, JsonNode?> outputPayload,
        WorkTaskStatus status,
        DateTime dueUtc,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.WorkTasks
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.CorrelationId == correlationId, cancellationToken);

        if (existing is not null)
        {
            return (existing, false);
        }

        var task = new WorkTask(
            taskId,
            companyId,
            taskType,
            title,
            description,
            priority,
            financeActorId == Guid.Empty ? null : financeActorId,
            null,
            "agent",
            financeActorId,
            inputPayload,
            null,
            outputPayload,
            description,
            0.88m,
            correlationId,
            WorkTaskSourceTypes.Agent,
            financeActorId,
            FinanceWorkflowQueue,
            "Deterministic simulation finance generation",
            correlationId,
            status);
        task.SetDueDate(dueUtc);
        _dbContext.WorkTasks.Add(task);
        return (task, true);
    }

    private async Task<(ApprovalRequest Approval, bool Created)> EnsureApprovalRequestAsync(
        Guid companyId,
        Guid approvalId,
        Guid taskId,
        string approvalType,
        Guid financeActorId,
        Dictionary<string, JsonNode?> thresholdContext,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.ApprovalRequests
            .IgnoreQueryFilters()
            .Include(x => x.Steps)
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId &&
                     x.TargetEntityType == ApprovalTargetEntityTypeValues.Parse("task").ToStorageValue() &&
                     x.TargetEntityId == taskId,
                cancellationToken);

        if (existing is not null)
        {
            return (existing, false);
        }

        var approval = ApprovalRequest.CreateForTarget(
            approvalId,
            companyId,
            ApprovalTargetEntityType.Task,
            taskId,
            SystemActorType,
            financeActorId,
            approvalType,
            thresholdContext,
            FinanceApproverRole,
            null,
            []);

        _dbContext.ApprovalRequests.Add(approval);
        return (approval, true);
    }

    private async Task<bool> EnsureSeedAnomalyAsync(
        Guid companyId,
        FinanceDayPlan plan,
        string correlationId,
        CancellationToken cancellationToken)
    {
        if (!plan.AnomalyTransactionId.HasValue || string.IsNullOrWhiteSpace(plan.AnomalyTypeToken))
        {
            return false;
        }

        var anomalyId = DeterministicGuid(companyId, $"seed-anomaly:{plan.DateUtc:yyyyMMdd}:{plan.AnomalyTypeToken}");
        var exists = await _dbContext.FinanceSeedAnomalies
            .IgnoreQueryFilters()
            .AnyAsync(x => x.CompanyId == companyId && x.Id == anomalyId, cancellationToken);

        if (exists)
        {
            return false;
        }

        var affectedRecordIds = new List<Guid> { plan.AnomalyTransactionId.Value };
        var anomalyTransaction = plan.Transactions.FirstOrDefault(x =>
            string.Equals(x.ExternalReference, plan.AnomalyTransactionExternalReference, StringComparison.OrdinalIgnoreCase));
        if (anomalyTransaction?.InvoiceId is Guid invoiceId)
        {
            affectedRecordIds.Add(invoiceId);
        }

        if (anomalyTransaction?.BillId is Guid billId)
        {
            affectedRecordIds.Add(billId);
        }

        var expectedDetectionMetadata = new JsonObject
        {
            ["simulatedDateUtc"] = JsonValue.Create(plan.DateUtc),
            ["correlationId"] = JsonValue.Create(correlationId),
            ["anomalyType"] = JsonValue.Create(plan.AnomalyTypeToken),
            ["invoiceScenario"] = JsonValue.Create(plan.InvoiceScenarioToken),
            ["thresholdCase"] = JsonValue.Create(plan.ThresholdCaseToken)
        };

        _dbContext.FinanceSeedAnomalies.Add(new FinanceSeedAnomaly(
            anomalyId,
            companyId,
            plan.AnomalyTypeToken,
            "deterministic_simulation",
            affectedRecordIds,
            expectedDetectionMetadata.ToJsonString()));
        return true;
    }

    private async Task<(Alert Alert, bool Created)> EnsureAnomalyAlertAsync(
        Guid companyId,
        FinanceDayPlan plan,
        string correlationId,
        Guid financeActorId,
        CancellationToken cancellationToken)
    {
        var fingerprint = $"finance-transaction-anomaly:{companyId:N}:{plan.DateUtc:yyyyMMdd}:{plan.AnomalyTypeToken}";
        var existing = await _dbContext.Alerts
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Fingerprint == fingerprint, cancellationToken);

        if (existing is not null)
        {
            return (existing, false);
        }

        var alertEvidence = new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
        {
            ["transactionId"] = plan.AnomalyTransactionId.HasValue ? JsonValue.Create(plan.AnomalyTransactionId.Value) : null,
            ["transactionExternalReference"] = JsonValue.Create(plan.AnomalyTransactionExternalReference),
            ["anomalyType"] = JsonValue.Create(plan.AnomalyTypeToken),
            ["simulatedDateUtc"] = JsonValue.Create(plan.DateUtc.Date),
            ["supplierName"] = JsonValue.Create(plan.SupplierName),
            ["dedupeKey"] = JsonValue.Create($"finance-transaction-anomaly:{plan.AnomalyTypeToken}"),
            ["deduplicationWindowStartUtc"] = JsonValue.Create(plan.DateUtc.Date),
            ["deduplicationWindowEndUtc"] = JsonValue.Create(plan.DateUtc.Date.AddDays(1)),
            ["recommendedAction"] = JsonValue.Create("Review generated anomaly evidence before approving downstream finance work.")
        };

        var alert = new Alert(
            DeterministicGuid(companyId, $"alert:{plan.DateUtc:yyyyMMdd}:{plan.AnomalyTypeToken}"),
            companyId,
            AlertType.Anomaly,
            AlertSeverity.High,
            $"Finance anomaly: {plan.AnomalyTypeToken}",
            plan.AnomalyExplanation!,
            alertEvidence,
            correlationId,
            fingerprint,
            AlertStatus.Open,
            financeActorId,
            new Dictionary<string, JsonNode?>(StringComparer.OrdinalIgnoreCase)
            {
                ["anomalyType"] = JsonValue.Create(plan.AnomalyTypeToken),
                ["confidence"] = JsonValue.Create(0.91m),
                ["recommendedAction"] = JsonValue.Create("Investigate the generated anomaly through the finance review workflow."),
                ["supplierName"] = JsonValue.Create(plan.SupplierName),
                ["dedupeKey"] = JsonValue.Create($"finance-transaction-anomaly:{plan.AnomalyTypeToken}"),
                ["deduplicationWindowStartUtc"] = JsonValue.Create(plan.DateUtc.Date),
                ["deduplicationWindowEndUtc"] = JsonValue.Create(plan.DateUtc.Date.AddDays(1))
            });

        _dbContext.Alerts.Add(alert);
        return (alert, true);
    }

    private async Task<bool> EnsureAuditEventAsync(
        Guid companyId,
        Guid auditEventId,
        string action,
        string targetType,
        string targetId,
        string correlationId,
        DateTime occurredUtc,
        Guid financeActorId,
        IReadOnlyDictionary<string, string?> metadata,
        CancellationToken cancellationToken)
    {
        var existing = await _dbContext.AuditEvents
            .IgnoreQueryFilters()
            .AnyAsync(
                x => x.CompanyId == companyId &&
                     x.Id == auditEventId,
                cancellationToken);

        if (existing)
        {
            return false;
        }

        _dbContext.AuditEvents.Add(new AuditEvent(
            auditEventId,
            companyId,
            SystemActorType,
            financeActorId,
            action,
            targetType,
            targetId,
            AuditEventOutcomes.Succeeded,
            "Generated by the deterministic finance simulation policy.",
            ["finance", "simulation"],
            metadata,
            correlationId,
            occurredUtc));
        return true;
    }

    private async Task<bool> EnsureActivityEventAsync(
        Guid companyId,
        Guid activityEventId,
        Guid? agentId,
        string eventType,
        DateTime occurredUtc,
        string correlationId,
        Guid taskId,
        Guid auditEventId,
        IReadOnlyDictionary<string, JsonNode?> sourceMetadata,
        CancellationToken cancellationToken)
    {
        var exists = await _dbContext.ActivityEvents
            .IgnoreQueryFilters()
            .AnyAsync(x => x.CompanyId == companyId && x.Id == activityEventId, cancellationToken);

        if (exists)
        {
            return false;
        }

        _dbContext.ActivityEvents.Add(new ActivityEvent(
            activityEventId,
            companyId,
            agentId,
            eventType,
            occurredUtc,
            "completed",
            $"Generated finance activity for {occurredUtc:yyyy-MM-dd}.",
            correlationId,
            sourceMetadata,
            "finance",
            taskId,
            auditEventId,
            occurredUtc));
        return true;
    }

    private static IReadOnlyList<PlannedTransaction> BuildTransactions(
        Guid companyId,
        DateTime simulatedDateUtc,
        ThresholdCase thresholdCase,
        InvoiceScenario invoiceScenario,
        Guid invoiceId,
        Guid billId,
        FinanceCounterparty customer,
        FinanceCounterparty supplier,
        decimal invoiceAmount,
        decimal billAmount,
        string invoiceCurrency,
        string billCurrency,
        string invoiceNumber,
        string billNumber)
    {
        var results = new List<PlannedTransaction>
        {
            new(
                invoiceScenario == InvoiceScenario.DifferentApprovalCurrency ? "customer_payment_fx" : "customer_payment",
                ResolveInvoicePaymentAmount(invoiceScenario, invoiceAmount),
                simulatedDateUtc.Date.AddHours(14),
                invoiceCurrency,
                $"SIM-TXN-INV-{simulatedDateUtc:yyyyMMdd}-{ToToken(invoiceScenario)}",
                $"Simulated payment activity for {invoiceNumber}",
                customer.Id,
                invoiceId,
                null)
        };

        if (invoiceScenario is not InvoiceScenario.DueSoon and not InvoiceScenario.Overdue and not InvoiceScenario.PendingOverThreshold and not InvoiceScenario.LowRiskPending and not InvoiceScenario.DifferentApprovalCurrency)
        {
            results[0] = results[0] with { Amount = ResolveInvoicePaymentAmount(invoiceScenario, invoiceAmount) };
        }
        else
        {
            results.Clear();
        }

        results.Add(new PlannedTransaction(
            "recurring_expense",
            -billAmount,
            simulatedDateUtc.Date.AddHours(10),
            billCurrency,
            $"SIM-TXN-BILL-{simulatedDateUtc:yyyyMMdd}-{ToToken(thresholdCase)}",
            $"Simulated recurring expense for {billNumber}",
            supplier.Id,
            null,
            billId));

        return results;
    }

    private static PlannedTransaction? ResolveAnomalyTransaction(
        IReadOnlyList<PlannedTransaction> transactions,
        FinanceAnomalyKind? anomalyType,
        int targetTransactionIndex)
    {
        if (anomalyType is null || transactions.Count == 0)
        {
            return null;
        }

        var preferred = transactions[Math.Clamp(targetTransactionIndex, 0, transactions.Count - 1)];
        var debitTransactions = transactions.Where(x => x.Amount < 0).ToArray();
        var creditTransactions = transactions.Where(x => x.Amount > 0).ToArray();
        var debit = debitTransactions.Length == 0
            ? preferred
            : debitTransactions[targetTransactionIndex % debitTransactions.Length];
        var credit = creditTransactions.Length == 0
            ? preferred
            : creditTransactions[targetTransactionIndex % creditTransactions.Length];

        return anomalyType switch
        {
            FinanceAnomalyKind.DuplicateVendorCharge => debit,
            FinanceAnomalyKind.UnusuallyHighAmount => debit,
            FinanceAnomalyKind.CategoryMismatch => debit,
            FinanceAnomalyKind.MissingDocument => debit,
            FinanceAnomalyKind.SuspiciousPaymentTiming => credit,
            FinanceAnomalyKind.MultiplePayments => credit,
            FinanceAnomalyKind.PaymentBeforeExpectedStateTransition => credit,
            _ => preferred
        };
    }

    private static IEnumerable<DateTime> EnumerateSimulatedDates(DateTime previousSimulatedUtc, DateTime currentSimulatedUtc)
    {
        var start = NormalizeUtc(previousSimulatedUtc).Date.AddDays(1);
        var end = NormalizeUtc(currentSimulatedUtc).Date;

        for (var cursor = start; cursor <= end; cursor = cursor.AddDays(1))
        {
            yield return cursor;
        }
    }

    private static decimal ResolveInvoiceAmount(InvoiceScenario invoiceScenario, ThresholdCase thresholdCase, decimal threshold)
    {
        if (invoiceScenario == InvoiceScenario.PendingOverThreshold)
        {
            return threshold + 1250m;
        }

        if (invoiceScenario == InvoiceScenario.DifferentApprovalCurrency)
        {
            return threshold + 0.25m;
        }

        return thresholdCase switch
        {
            ThresholdCase.JustBelow => Math.Max(100m, threshold - 0.01m),
            ThresholdCase.ExactlyAt => threshold,
            ThresholdCase.JustAbove => threshold + 0.01m,
            ThresholdCase.HumanApprovalRequired => threshold + 2750m,
            ThresholdCase.EligibleWithoutEscalation => Math.Max(100m, threshold * 0.45m),
            ThresholdCase.AlreadyApprovedOrNotActionable => Math.Max(100m, threshold * 0.65m),
            _ => Math.Max(100m, threshold * 0.50m)
        };
    }

    private static decimal ResolveBillAmount(ThresholdCase thresholdCase, decimal threshold) =>
        thresholdCase switch
        {
            ThresholdCase.JustBelow => Math.Max(50m, threshold - 0.01m),
            ThresholdCase.ExactlyAt => threshold,
            ThresholdCase.JustAbove => threshold + 0.01m,
            ThresholdCase.HumanApprovalRequired => threshold + 900m,
            ThresholdCase.EligibleWithoutEscalation => Math.Max(50m, threshold * 0.40m),
            ThresholdCase.AlreadyApprovedOrNotActionable => Math.Max(50m, threshold * 0.25m),
            _ => Math.Max(50m, threshold * 0.40m)
        };

    private static decimal ResolveInvoicePaymentAmount(InvoiceScenario scenario, decimal invoiceAmount) =>
        scenario switch
        {
            InvoiceScenario.PartialPayment => Math.Round(invoiceAmount * 0.50m, 2, MidpointRounding.AwayFromZero),
            InvoiceScenario.FullPayment => invoiceAmount,
            InvoiceScenario.OverPayment => invoiceAmount + 37.50m,
            _ => invoiceAmount
        };

    private static DateTime ResolveInvoiceDueUtc(DateTime simulatedDateUtc, InvoiceScenario scenario) =>
        scenario switch
        {
            InvoiceScenario.DueSoon => simulatedDateUtc.Date.AddDays(2),
            InvoiceScenario.Overdue => simulatedDateUtc.Date.AddDays(-2),
            _ => simulatedDateUtc.Date.AddDays(14)
        };

    private static string ResolveInvoiceStatus(InvoiceScenario scenario, ThresholdCase thresholdCase) =>
        scenario switch
        {
            InvoiceScenario.PendingOverThreshold => "pending_approval",
            InvoiceScenario.DifferentApprovalCurrency => "pending_approval",
            InvoiceScenario.PartialPayment => "approved",
            InvoiceScenario.FullPayment => "paid",
            InvoiceScenario.OverPayment => "paid",
            InvoiceScenario.DueSoon => "pending",
            InvoiceScenario.Overdue => "pending",
            InvoiceScenario.LowRiskPending when thresholdCase == ThresholdCase.AlreadyApprovedOrNotActionable => "approved",
            _ => "pending"
        };

    private static string ResolveBillStatus(ThresholdCase thresholdCase) =>
        thresholdCase switch
        {
            ThresholdCase.JustAbove => "pending_approval",
            ThresholdCase.HumanApprovalRequired => "pending_approval",
            ThresholdCase.AlreadyApprovedOrNotActionable => "paid",
            _ => "open"
        };

    private static string ResolveAlternateCurrency(string approvalCurrency) =>
        string.Equals(approvalCurrency, "USD", StringComparison.OrdinalIgnoreCase)
            ? "EUR"
            : "USD";

    private static bool ShouldInjectAnomaly(int dayIndex, FinanceGenerationConfiguration config)
    {
        var cadence = Math.Max(1, config.AnomalyCadenceDays);
        return (dayIndex + config.AnomalyOffsetDays) % cadence == 0;
    }

    private static string BuildAnomalyExplanation(FinanceAnomalyKind anomalyType, PlannedTransaction transaction, string supplierName) =>
        anomalyType switch
        {
            FinanceAnomalyKind.DuplicateVendorCharge => $"Generated duplicate vendor charge pattern for {supplierName} on {transaction.ExternalReference}.",
            FinanceAnomalyKind.UnusuallyHighAmount => $"Generated unusually high amount signal for {transaction.ExternalReference}.",
            FinanceAnomalyKind.CategoryMismatch => $"Generated category mismatch signal for {transaction.ExternalReference}.",
            FinanceAnomalyKind.MissingDocument => $"Generated missing document signal for {transaction.ExternalReference}.",
            FinanceAnomalyKind.SuspiciousPaymentTiming => $"Generated suspicious payment timing for {transaction.ExternalReference}.",
            FinanceAnomalyKind.MultiplePayments => $"Generated multiple payments review signal for {transaction.ExternalReference}.",
            FinanceAnomalyKind.PaymentBeforeExpectedStateTransition => $"Generated payment-before-state-transition signal for {transaction.ExternalReference}.",
            _ => $"Generated anomaly signal for {transaction.ExternalReference}."
        };

    private static Guid DeterministicGuid(Guid companyId, string scope)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(FormattableString.Invariant($"{companyId:N}:{scope}")));
        Span<byte> guidBytes = stackalloc byte[16];
        bytes.AsSpan(0, 16).CopyTo(guidBytes);
        guidBytes[7] = (byte)((guidBytes[7] & 0x0F) | 0x40);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }

    private static string BuildDayCorrelationId(Guid companyId, DateTime simulatedDateUtc) =>
        $"finance-sim:{companyId:N}:day:{simulatedDateUtc:yyyyMMdd}";

    private static string BuildTaskCorrelationId(Guid companyId, string scope, string key) =>
        $"finance-sim:{companyId:N}:{scope}:{key}".ToLowerInvariant();

    private static string BuildAnomalyCorrelationId(Guid companyId, Guid transactionId, string anomalyTypeToken, DateTime simulatedDateUtc) =>
        $"fin-anom:{companyId:N}:{transactionId:N}:{anomalyTypeToken}:{simulatedDateUtc:yyyyMMdd}".ToLowerInvariant();

    private static string ToToken(InvoiceScenario scenario) =>
        scenario switch
        {
            InvoiceScenario.PendingOverThreshold => "PTH",
            InvoiceScenario.DifferentApprovalCurrency => "FX",
            InvoiceScenario.PartialPayment => "PART",
            InvoiceScenario.FullPayment => "FULL",
            InvoiceScenario.OverPayment => "OVER",
            InvoiceScenario.DueSoon => "SOON",
            InvoiceScenario.Overdue => "LATE",
            InvoiceScenario.LowRiskPending => "LOW",
            _ => "GEN"
        };

    private static string ToToken(ThresholdCase thresholdCase) =>
        thresholdCase switch
        {
            ThresholdCase.JustBelow => "BELOW",
            ThresholdCase.ExactlyAt => "EXACT",
            ThresholdCase.JustAbove => "ABOVE",
            ThresholdCase.HumanApprovalRequired => "HUMAN",
            ThresholdCase.EligibleWithoutEscalation => "AUTO",
            ThresholdCase.AlreadyApprovedOrNotActionable => "DONE",
            _ => "BASE"
        };

    private static string ToToken(FinanceAnomalyKind anomalyType) =>
        anomalyType switch
        {
            FinanceAnomalyKind.DuplicateVendorCharge => "duplicate_vendor_charge",
            FinanceAnomalyKind.UnusuallyHighAmount => "unusually_high_amount",
            FinanceAnomalyKind.CategoryMismatch => "category_mismatch",
            FinanceAnomalyKind.MissingDocument => "missing_document",
            FinanceAnomalyKind.SuspiciousPaymentTiming => "suspicious_payment_timing",
            FinanceAnomalyKind.MultiplePayments => "multiple_payments",
            FinanceAnomalyKind.PaymentBeforeExpectedStateTransition => "payment_before_expected_state_transition",
            _ => "generic"
        };

    private void EnsureBackendExecutionEnabled(Guid companyId, Guid activeSessionId)
    {
        if (_featureGate?.IsBackendExecutionEnabled() == false)
        {
            var featureState = _featureGate.GetState();
            _logger.LogInformation(
                "Blocked simulation finance generation for company {CompanyId}, session {SessionId} because simulation execution is disabled. BackendExecutionEnabled={BackendExecutionEnabled}, BackgroundJobsEnabled={BackgroundJobsEnabled}.",
                companyId,
                activeSessionId,
                featureState.BackendExecutionEnabled,
                featureState.BackgroundJobsEnabled);

            _featureGate.EnsureBackendExecutionEnabled();
        }
    }

    private static DateTime NormalizeUtc(DateTime value) =>
        value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
        };

    private static void Validate(GenerateCompanySimulationFinanceCommand command)
    {
        if (command.CompanyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(command));
        }

        if (command.ActiveSessionId == Guid.Empty)
        {
            throw new ArgumentException("Active session id is required.", nameof(command));
        }

        if (NormalizeUtc(command.CurrentSimulatedUtc) < NormalizeUtc(command.PreviousSimulatedUtc))
        {
            throw new ArgumentException("Current simulated time cannot be before the previous simulated time.", nameof(command));
        }
    }

    private sealed record FinanceDayPlan(
        DateTime DateUtc,
        InvoiceScenario InvoiceScenario,
        ThresholdCase ThresholdCase,
        string InvoiceNumber,
        Guid InvoiceId,
        Guid CustomerId,
        string CustomerName,
        DateTime IssuedUtc,
        DateTime DueUtc,
        decimal InvoiceAmount,
        string InvoiceCurrency,
        string InvoiceStatus,
        string BillNumber,
        Guid BillId,
        Guid SupplierId,
        string SupplierName,
        DateTime ReceivedUtc,
        DateTime BillDueUtc,
        decimal BillAmount,
        string BillStatus,
        bool InvoiceApprovalRequired,
        bool BillApprovalRequired,
        IReadOnlyList<PlannedTransaction> Transactions,
        FinanceAnomalyKind? AnomalyType,
        string? AnomalyTypeToken,
        Guid? AnomalyTransactionId,
        string? AnomalyTransactionExternalReference,
        string? AnomalyExplanation,
        Dictionary<string, JsonNode?> InvoicePayload,
        Dictionary<string, JsonNode?> InvoiceResultPayload,
        Dictionary<string, JsonNode?> BillPayload,
        Dictionary<string, JsonNode?> BillResultPayload,
        Dictionary<string, JsonNode?>? AnomalyPayload,
        Guid InvoiceTaskId,
        Guid InvoiceApprovalId,
        Guid BillTaskId,
        Guid BillApprovalId,
        Guid AnomalyTaskId,
        Guid ActivityEventId,
        Dictionary<string, JsonNode?> ActivityMetadata)
    {
        public string InvoiceScenarioToken => ToToken(InvoiceScenario);
        public string ThresholdCaseToken => ToToken(ThresholdCase);
    }

    private sealed record CounterpartyCatalog(
        List<FinanceCounterparty> Customers,
        List<FinanceCounterparty> Suppliers);

    private sealed record PlannedTransaction(
        string Category,
        decimal Amount,
        DateTime TransactionUtc,
        string Currency,
        string ExternalReference,
        string Description,
        Guid CounterpartyId,
        Guid? InvoiceId,
        Guid? BillId);

    private sealed record DayApplyResult(
        DateTime DateUtc,
        string? AnomalyTypeToken,
        int InvoicesCreated,
        int BillsCreated,
        int TransactionsCreated,
        int BalancesCreated,
        int RecurringExpenseInstancesCreated,
        int WorkflowTasksCreated,
        int ApprovalRequestsCreated,
        int AuditEventsCreated,
        int ActivityEventsCreated,
        int AlertsCreated)
    {
        public CompanySimulationFinanceGenerationDayLogDto ToDayLog()
        {
            var warnings = InvoicesCreated + BillsCreated + TransactionsCreated + RecurringExpenseInstancesCreated == 0
                ? ["The simulated day completed without generating finance records."]
                : Array.Empty<string>();

            return new CompanySimulationFinanceGenerationDayLogDto(
                DateUtc.Date,
                TransactionsCreated,
                InvoicesCreated,
                BillsCreated,
                RecurringExpenseInstancesCreated,
                AlertsCreated,
                string.IsNullOrWhiteSpace(AnomalyTypeToken) ? [] : [AnomalyTypeToken],
                warnings,
                []);
        }
    }

    private sealed class FinanceGenerationConfiguration
    {
        public int AnomalyCadenceDays { get; init; } = 3;
        public int AnomalyOffsetDays { get; init; }

        public static FinanceGenerationConfiguration Parse(string? deterministicConfigurationJson)
        {
            if (string.IsNullOrWhiteSpace(deterministicConfigurationJson))
            {
                return new FinanceGenerationConfiguration();
            }

            try
            {
                var node = JsonNode.Parse(deterministicConfigurationJson);
                var financeNode = node?["financeGeneration"] ?? node;
                return new FinanceGenerationConfiguration
                {
                    AnomalyCadenceDays = Math.Max(1, financeNode?["anomalyCadenceDays"]?.GetValue<int>() ?? 3),
                    AnomalyOffsetDays = Math.Max(0, financeNode?["anomalyOffsetDays"]?.GetValue<int>() ?? 0)
                };
            }
            catch (JsonException)
            {
                return new FinanceGenerationConfiguration();
            }
        }
    }

    private enum InvoiceScenario
    {
        PendingOverThreshold,
        DifferentApprovalCurrency,
        PartialPayment,
        FullPayment,
        OverPayment,
        DueSoon,
        Overdue,
        LowRiskPending
    }

    private enum ThresholdCase
    {
        JustBelow,
        ExactlyAt,
        JustAbove,
        HumanApprovalRequired,
        EligibleWithoutEscalation,
        AlreadyApprovedOrNotActionable
    }

    private enum FinanceAnomalyKind
    {
        DuplicateVendorCharge,
        UnusuallyHighAmount,
        CategoryMismatch,
        MissingDocument,
        SuspiciousPaymentTiming,
        MultiplePayments,
        PaymentBeforeExpectedStateTransition
    }
}
