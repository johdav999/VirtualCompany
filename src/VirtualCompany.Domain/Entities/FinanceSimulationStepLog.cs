using System.Text.Json.Nodes;
using VirtualCompany.Domain.Enums;

namespace VirtualCompany.Domain.Entities;
public sealed class FinanceSimulationStepLog : ICompanyOwnedEntity
{
    private FinanceSimulationStepLog()
    {
    }

    public FinanceSimulationStepLog(
        Guid id,
        Guid companyId,
        Guid runId,
        int stepNumber,
        DateTime windowStartUtc,
        DateTime windowEndUtc,
        int executionStepHours,
        int totalHoursProcessed,
        bool isAccelerated,
        int transactionsGenerated,
        int invoicesGenerated,
        int billsGenerated,
        int recurringExpenseInstancesGenerated,
        int eventsEmitted,
        DateTime? createdUtc = null)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("CompanyId is required.", nameof(companyId));
        }

        if (runId == Guid.Empty)
        {
            throw new ArgumentException("RunId is required.", nameof(runId));
        }

        if (stepNumber <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(stepNumber), "Step number must be positive.");
        }

        if (executionStepHours <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(executionStepHours), "Execution step hours must be positive.");
        }

        if (totalHoursProcessed <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalHoursProcessed), "Total processed hours must be positive.");
        }

        var normalizedWindowStartUtc = EntityTimestampNormalizer.NormalizeUtc(windowStartUtc, nameof(windowStartUtc));
        var normalizedWindowEndUtc = EntityTimestampNormalizer.NormalizeUtc(windowEndUtc, nameof(windowEndUtc));
        if (normalizedWindowEndUtc <= normalizedWindowStartUtc)
        {
            throw new ArgumentException("Window end must be after window start.", nameof(windowEndUtc));
        }

        Id = id == Guid.Empty ? Guid.NewGuid() : id;
        CompanyId = companyId;
        RunId = runId;
        StepNumber = stepNumber;
        WindowStartUtc = normalizedWindowStartUtc;
        WindowEndUtc = normalizedWindowEndUtc;
        ExecutionStepHours = executionStepHours;
        TotalHoursProcessed = totalHoursProcessed;
        IsAccelerated = isAccelerated;
        TransactionsGenerated = Math.Max(0, transactionsGenerated);
        InvoicesGenerated = Math.Max(0, invoicesGenerated);
        BillsGenerated = Math.Max(0, billsGenerated);
        RecurringExpenseInstancesGenerated = Math.Max(0, recurringExpenseInstancesGenerated);
        EventsEmitted = Math.Max(0, eventsEmitted);
        CreatedUtc = EntityTimestampNormalizer.NormalizeUtc(createdUtc ?? normalizedWindowEndUtc, nameof(createdUtc));
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid RunId { get; private set; }
    public int StepNumber { get; private set; }
    public DateTime WindowStartUtc { get; private set; }
    public DateTime WindowEndUtc { get; private set; }
    public int ExecutionStepHours { get; private set; }
    public int TotalHoursProcessed { get; private set; }
    public bool IsAccelerated { get; private set; }
    public int TransactionsGenerated { get; private set; }
    public int InvoicesGenerated { get; private set; }
    public int BillsGenerated { get; private set; }
    public int RecurringExpenseInstancesGenerated { get; private set; }
    public int EventsEmitted { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
}

