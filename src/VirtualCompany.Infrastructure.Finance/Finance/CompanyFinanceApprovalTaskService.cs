using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Observability;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanyFinanceApprovalTaskService : IFinanceApprovalTaskService
{
    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyContextAccessor? _companyContextAccessor;
    private readonly ILogger<CompanyFinanceApprovalTaskService> _logger;

    public CompanyFinanceApprovalTaskService(
        VirtualCompanyDbContext dbContext,
        ICompanyContextAccessor? companyContextAccessor,
        ILogger<CompanyFinanceApprovalTaskService> logger)
    {
        _dbContext = dbContext;
        _companyContextAccessor = companyContextAccessor;
        _logger = logger;
    }

    public async Task<bool> EnsureTaskAsync(
        EnsureFinanceApprovalTaskCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);

        if (command.TargetId == Guid.Empty)
        {
            throw new ArgumentException("Target id is required.", nameof(command));
        }

        ApprovalTargetTypeValues.EnsureSupported(command.TargetType, nameof(command.TargetType));

        var policy = await LoadPolicyAsync(command.CompanyId, cancellationToken);
        var threshold = ResolveThreshold(policy, command.TargetType);
        if (!threshold.HasValue ||
            command.Amount <= threshold.Value ||
            !string.Equals(command.Currency, policy.ApprovalCurrency, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (await HasExistingTaskAsync(command.CompanyId, command.TargetType, command.TargetId, cancellationToken))
        {
            return false;
        }

        var assignee = await ResolveAssigneeAsync(command.CompanyId, cancellationToken);
        var status = assignee.UserId.HasValue
            ? ApprovalTaskStatus.Pending
            : ApprovalTaskStatus.Escalated;

        _dbContext.ApprovalTasks.Add(new ApprovalTask(
            Guid.NewGuid(),
            command.CompanyId,
            command.TargetType,
            command.TargetId,
            assignee.UserId,
            command.DueDateUtc,
            status));

        return true;
    }

    public async Task<IReadOnlyList<FinancePendingApprovalTaskDto>> GetPendingTasksAsync(
        GetPendingFinanceApprovalTasksQuery query,
        CancellationToken cancellationToken)
    {
        EnsureTenant(query.CompanyId);

        return await _dbContext.ApprovalTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.Assignee)
            .Where(x =>
                x.CompanyId == query.CompanyId &&
                (x.Status == ApprovalTaskStatus.Pending || x.Status == ApprovalTaskStatus.Escalated))
            .OrderBy(x => x.DueDate ?? DateTime.MaxValue)
            .ThenBy(x => x.CreatedUtc)
            .Select(x => new FinancePendingApprovalTaskDto(
                x.Id,
                x.TargetType.ToStorageValue(),
                x.TargetId,
                x.AssigneeId.HasValue
                    ? new FinanceApprovalTaskAssigneeDto(x.AssigneeId, x.Assignee == null ? null : x.Assignee.DisplayName)
                    : null,
                x.DueDate,
                x.Status.ToStorageValue()))
            .ToListAsync(cancellationToken);
    }

    public async Task<FinancePendingApprovalTaskDto> ActOnTaskAsync(
        ActOnFinanceApprovalTaskCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);

        if (command.ApprovalTaskId == Guid.Empty)
        {
            throw new ArgumentException("Approval task id is required.", nameof(command));
        }

        ApprovalTaskStatusValues.EnsureSupported(command.Action, nameof(command.Action));

        var task = await _dbContext.ApprovalTasks
            .IgnoreQueryFilters()
            .Include(x => x.Assignee)
            .SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.ApprovalTaskId, cancellationToken);

        if (task is null)
        {
            throw new KeyNotFoundException("Finance approval task was not found.");
        }

        _ = command.Comment;
        switch (command.Action)
        {
            case ApprovalTaskStatus.Approved: task.Approve(); break;
            case ApprovalTaskStatus.Rejected: task.Reject(); break;
            case ApprovalTaskStatus.Escalated: task.Escalate(); break;
            default: throw new InvalidOperationException($"Unsupported finance approval task action '{command.Action.ToStorageValue()}'.");
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return MapTask(task);
    }

    public async Task<FinanceApprovalTaskBackfillResultDto> BackfillApprovalTasksAsync(
        BackfillFinanceApprovalTasksCommand command,
        CancellationToken cancellationToken)
    {
        EnsureTenant(command.CompanyId);

        var correlationId = string.IsNullOrWhiteSpace(command.CorrelationId)
            ? Guid.NewGuid().ToString("N")
            : command.CorrelationId.Trim();
        var batchSize = Math.Clamp(command.BatchSize, 1, 1000);
        var scannedCount = 0;
        var matchedCount = 0;
        var createdCount = 0;
        var skippedExistingCount = 0;
        var policy = await LoadPolicyAsync(command.CompanyId, cancellationToken);
        var assignee = await ResolveAssigneeAsync(command.CompanyId, cancellationToken);

        var billCounts = await BackfillBillsAsync(command, policy, assignee, batchSize, cancellationToken);
        scannedCount += billCounts.ScannedCount;
        matchedCount += billCounts.MatchedCount;
        createdCount += billCounts.CreatedCount;
        skippedExistingCount += billCounts.SkippedExistingCount;

        var paymentCounts = BackfillTargetCounts.Empty;
        if (command.IncludePayments)
        {
            paymentCounts = await BackfillPaymentsAsync(command, policy, assignee, batchSize, cancellationToken);
            scannedCount += paymentCounts.ScannedCount;
            matchedCount += paymentCounts.MatchedCount;
            createdCount += paymentCounts.CreatedCount;
            skippedExistingCount += paymentCounts.SkippedExistingCount;
        }

        using var scope = _logger.BeginScope(ExecutionLogScope.ForBackground(correlationId, command.CompanyId));
        _logger.LogInformation(
            "Completed finance approval task backfill for company {CompanyId}. Scanned={ScannedCount} Matched={MatchedCount} Created={CreatedCount} SkippedExisting={SkippedExistingCount} BillCreated={BillCreatedCount} PaymentCreated={PaymentCreatedCount}.",
            command.CompanyId,
            scannedCount,
            matchedCount,
            createdCount,
            skippedExistingCount,
            billCounts.CreatedCount,
            paymentCounts.CreatedCount);

        return new FinanceApprovalTaskBackfillResultDto(
            command.CompanyId,
            correlationId,
            scannedCount,
            matchedCount,
            createdCount,
            skippedExistingCount,
            billCounts.ScannedCount,
            paymentCounts.ScannedCount,
            billCounts.CreatedCount,
            paymentCounts.CreatedCount);
    }

    private async Task<FinancePolicyConfigurationDto> LoadPolicyAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var configuration = await _dbContext.FinancePolicyConfigurations
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);

        return configuration is null
            ? new FinancePolicyConfigurationDto(companyId, "USD", 10000m, 5000m, true, -10000m, 10000m, 90, 30)
            : new FinancePolicyConfigurationDto(
                configuration.CompanyId,
                configuration.ApprovalCurrency,
                configuration.InvoiceApprovalThreshold,
                configuration.BillApprovalThreshold,
                configuration.RequireCounterpartyForTransactions,
                configuration.AnomalyDetectionLowerBound,
                configuration.AnomalyDetectionUpperBound,
                configuration.CashRunwayWarningThresholdDays,
                configuration.CashRunwayCriticalThresholdDays);
    }

    private static decimal? ResolveThreshold(FinancePolicyConfigurationDto policy, ApprovalTargetType targetType) =>
        targetType switch
        {
            ApprovalTargetType.Bill => policy.BillApprovalThreshold,
            ApprovalTargetType.Payment => policy.BillApprovalThreshold,
            _ => null
        };

    private async Task<BackfillTargetCounts> BackfillBillsAsync(
        BackfillFinanceApprovalTasksCommand command,
        FinancePolicyConfigurationDto policy,
        FinanceApprovalTaskAssigneeDto assignee,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var page = 0;
        var counts = BackfillTargetCounts.Empty;

        while (true)
        {
            var bills = await _dbContext.FinanceBills
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.CompanyId == command.CompanyId)
                .OrderBy(x => x.DueUtc)
                .ThenBy(x => x.Id)
                .Skip(page * batchSize)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            if (bills.Count == 0)
            {
                return counts;
            }

            page++;
            counts = counts with { ScannedCount = counts.ScannedCount + bills.Count };

            var billIds = bills.Select(x => x.Id).ToArray();
            var existingTaskTargetIds = await LoadExistingTargetIdsAsync(command.CompanyId, ApprovalTargetType.Bill, billIds, cancellationToken);

            foreach (var bill in bills)
            {
                if (!MatchesBillApprovalCriteria(bill, policy))
                {
                    continue;
                }

                counts = counts with { MatchedCount = counts.MatchedCount + 1 };
                if (existingTaskTargetIds.Contains(bill.Id) ||
                    HasTrackedTask(command.CompanyId, ApprovalTargetType.Bill, bill.Id))
                {
                    counts = counts with { SkippedExistingCount = counts.SkippedExistingCount + 1 };
                    continue;
                }

                if (await TryCreateTaskAsync(BuildTask(command.CompanyId, ApprovalTargetType.Bill, bill.Id, bill.DueUtc, assignee), cancellationToken))
                {
                    existingTaskTargetIds.Add(bill.Id);
                    counts = counts with { CreatedCount = counts.CreatedCount + 1 };
                    continue;
                }

                existingTaskTargetIds.Add(bill.Id);
                counts = counts with { SkippedExistingCount = counts.SkippedExistingCount + 1 };
            }
        }
    }

    private async Task<BackfillTargetCounts> BackfillPaymentsAsync(
        BackfillFinanceApprovalTasksCommand command,
        FinancePolicyConfigurationDto policy,
        FinanceApprovalTaskAssigneeDto assignee,
        int batchSize,
        CancellationToken cancellationToken)
    {
        var page = 0;
        var counts = BackfillTargetCounts.Empty;

        while (true)
        {
            var payments = await _dbContext.Payments
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.CompanyId == command.CompanyId)
                .OrderBy(x => x.PaymentDate)
                .ThenBy(x => x.Id)
                .Skip(page * batchSize)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            if (payments.Count == 0)
            {
                return counts;
            }

            page++;
            counts = counts with { ScannedCount = counts.ScannedCount + payments.Count };

            var paymentIds = payments.Select(x => x.Id).ToArray();
            var existingTaskTargetIds = await LoadExistingTargetIdsAsync(command.CompanyId, ApprovalTargetType.Payment, paymentIds, cancellationToken);

            foreach (var payment in payments)
            {
                if (!MatchesPaymentApprovalCriteria(payment, policy))
                {
                    continue;
                }

                counts = counts with { MatchedCount = counts.MatchedCount + 1 };
                if (existingTaskTargetIds.Contains(payment.Id) ||
                    HasTrackedTask(command.CompanyId, ApprovalTargetType.Payment, payment.Id))
                {
                    counts = counts with { SkippedExistingCount = counts.SkippedExistingCount + 1 };
                    continue;
                }

                if (await TryCreateTaskAsync(BuildTask(command.CompanyId, ApprovalTargetType.Payment, payment.Id, payment.PaymentDate, assignee), cancellationToken))
                {
                    existingTaskTargetIds.Add(payment.Id);
                    counts = counts with { CreatedCount = counts.CreatedCount + 1 };
                    continue;
                }

                existingTaskTargetIds.Add(payment.Id);
                counts = counts with { SkippedExistingCount = counts.SkippedExistingCount + 1 };
            }
        }
    }

    private static bool MatchesBillApprovalCriteria(FinanceBill bill, FinancePolicyConfigurationDto policy) =>
        MatchesApprovalCriteria(bill.Amount, bill.Currency, policy.BillApprovalThreshold, policy.ApprovalCurrency) &&
        !string.Equals(bill.Status, "paid", StringComparison.OrdinalIgnoreCase) &&
        !string.Equals(bill.SettlementStatus, FinanceSettlementStatuses.Paid, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesPaymentApprovalCriteria(Payment payment, FinancePolicyConfigurationDto policy) =>
        MatchesApprovalCriteria(payment.Amount, payment.Currency, policy.BillApprovalThreshold, policy.ApprovalCurrency) &&
        !string.Equals(payment.Status, PaymentStatuses.Completed, StringComparison.OrdinalIgnoreCase);

    private static bool MatchesApprovalCriteria(decimal amount, string currency, decimal threshold, string approvalCurrency) =>
        amount > threshold &&
        string.Equals(currency, approvalCurrency, StringComparison.OrdinalIgnoreCase);

    private async Task<HashSet<Guid>> LoadExistingTargetIdsAsync(Guid companyId, ApprovalTargetType targetType, IReadOnlyCollection<Guid> targetIds, CancellationToken cancellationToken) =>
        await _dbContext.ApprovalTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.TargetType == targetType && targetIds.Contains(x.TargetId))
            .Select(x => x.TargetId)
            .ToHashSetAsync(cancellationToken);

    private async Task<bool> HasExistingTaskAsync(Guid companyId, ApprovalTargetType targetType, Guid targetId, CancellationToken cancellationToken) =>
        HasTrackedTask(companyId, targetType, targetId) ||
        await _dbContext.ApprovalTasks
            .IgnoreQueryFilters()
            .AsNoTracking()
            .AnyAsync(x => x.CompanyId == companyId && x.TargetType == targetType && x.TargetId == targetId, cancellationToken);

    private bool HasTrackedTask(Guid companyId, ApprovalTargetType targetType, Guid targetId) =>
        _dbContext.ChangeTracker.Entries<ApprovalTask>()
            .Any(x =>
                x.State != EntityState.Deleted &&
                x.Entity.CompanyId == companyId &&
                x.Entity.TargetType == targetType &&
                x.Entity.TargetId == targetId);

    private async Task<bool> TryCreateTaskAsync(ApprovalTask task, CancellationToken cancellationToken)
    {
        _dbContext.ApprovalTasks.Add(task);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return true;
        }
        catch (DbUpdateException ex) when (IsDuplicateApprovalTaskException(ex))
        {
            _dbContext.Entry(task).State = EntityState.Detached;
            return false;
        }
    }

    private static bool IsDuplicateApprovalTaskException(DbUpdateException exception)
    {
        var message = exception.InnerException?.Message ?? exception.Message;
        return message.Contains("IX_approval_tasks_company_id_target_type_target_id", StringComparison.OrdinalIgnoreCase) ||
               (message.Contains("approval_tasks", StringComparison.OrdinalIgnoreCase) &&
                message.Contains("target_id", StringComparison.OrdinalIgnoreCase) &&
                message.Contains("unique", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<FinanceApprovalTaskAssigneeDto> ResolveAssigneeAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var assignee = await _dbContext.CompanyMemberships
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Include(x => x.User)
            .Where(x =>
                x.CompanyId == companyId &&
                x.Status == CompanyMembershipStatus.Active &&
                x.UserId.HasValue &&
                (x.Role == CompanyMembershipRole.FinanceApprover ||
                 x.Role == CompanyMembershipRole.Owner ||
                 x.Role == CompanyMembershipRole.Admin ||
                 x.Role == CompanyMembershipRole.Manager))
            .OrderBy(x => x.Role == CompanyMembershipRole.FinanceApprover ? 0 : x.Role == CompanyMembershipRole.Owner ? 1 : x.Role == CompanyMembershipRole.Admin ? 2 : 3)
            .ThenBy(x => x.CreatedUtc)
            .Select(x => new FinanceApprovalTaskAssigneeDto(x.UserId, x.User == null ? null : x.User.DisplayName))
            .FirstOrDefaultAsync(cancellationToken);

        return assignee ?? new FinanceApprovalTaskAssigneeDto(null, null);
    }

    private static ApprovalTask BuildTask(
        Guid companyId,
        ApprovalTargetType targetType,
        Guid targetId,
        DateTime? dueDateUtc,
        FinanceApprovalTaskAssigneeDto assignee) =>
        new(
            Guid.NewGuid(),
            companyId,
            targetType,
            targetId,
            assignee.UserId,
            dueDateUtc,
            assignee.UserId.HasValue ? ApprovalTaskStatus.Pending : ApprovalTaskStatus.Escalated);

    private static FinancePendingApprovalTaskDto MapTask(ApprovalTask task) =>
        new(
            task.Id,
            task.TargetType.ToStorageValue(),
            task.TargetId,
            task.AssigneeId.HasValue
                ? new FinanceApprovalTaskAssigneeDto(task.AssigneeId, task.Assignee?.DisplayName)
                : null,
            task.DueDate,
            task.Status.ToStorageValue());

    private void EnsureTenant(Guid companyId)
    {
        if (companyId == Guid.Empty)
        {
            throw new ArgumentException("Company id is required.", nameof(companyId));
        }

        if (_companyContextAccessor?.CompanyId is Guid currentCompanyId && currentCompanyId != companyId)
        {
            throw new UnauthorizedAccessException("Finance approval tasks are scoped to the active company context.");
        }
    }

    private sealed record BackfillTargetCounts(
        int ScannedCount,
        int MatchedCount,
        int CreatedCount,
        int SkippedExistingCount)
    {
        public static BackfillTargetCounts Empty { get; } = new(0, 0, 0, 0);
    }
}
