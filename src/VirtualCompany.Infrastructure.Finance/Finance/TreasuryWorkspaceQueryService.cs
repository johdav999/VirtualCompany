using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Cockpit;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class TreasuryWorkspaceQueryService : ITreasuryWorkspaceQueryService
{
    private const int MaximumAccounts = 50;
    private const int MaximumPaymentItems = 30;
    private const int MaximumSourceRowsPerAccount = 10;
    private const int StaleEvidenceMinutes = 360;
    private const int AgedReconciliationDays = 7;

    private readonly VirtualCompanyDbContext _db;
    private readonly IDashboardFinanceSnapshotService _dashboard;
    private readonly IFinanceReadService _finance;
    private readonly ITreasuryWorkspacePolicy _policy;
    private readonly TimeProvider _time;
    private readonly TreasuryWorkspaceTelemetry _telemetry;
    private readonly ICompanyContextAccessor? _companyContext;

    public TreasuryWorkspaceQueryService(
        VirtualCompanyDbContext db,
        IDashboardFinanceSnapshotService dashboard,
        IFinanceReadService finance,
        ITreasuryWorkspacePolicy policy,
        TimeProvider time,
        TreasuryWorkspaceTelemetry telemetry,
        ICompanyContextAccessor? companyContext = null)
    {
        _db = db;
        _dashboard = dashboard;
        _finance = finance;
        _policy = policy;
        _time = time;
        _telemetry = telemetry;
        _companyContext = companyContext;
    }

    public async Task<TreasuryWorkspaceDto> GetAsync(
        GetTreasuryWorkspaceQuery query,
        CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        try
        {
            EnsureTenant(query.CompanyId);
            var asOfUtc = NormalizeUtc(query.AsOfUtc) ?? _time.GetUtcNow().UtcDateTime;
            var horizonDays = Math.Clamp(query.HorizonDays <= 0 ? 14 : query.HorizonDays, 1, 30);
            var exceptionLimit = Math.Clamp(query.ExceptionLimit <= 0 ? 12 : query.ExceptionLimit, 1, 50);
            var taskLimit = Math.Clamp(query.TaskLimit <= 0 ? 8 : query.TaskLimit, 1, 25);

            var dashboard = await _dashboard.GetAsync(
                query.CompanyId,
                asOfUtc,
                horizonDays,
                cancellationToken);
            var cashPosition = await _finance.GetCashPositionAsync(
                new GetFinanceCashPositionQuery(query.CompanyId, asOfUtc),
                cancellationToken);
            var liquidity = BuildLiquidity(dashboard, cashPosition, horizonDays, asOfUtc);

            var (accounts, accountsTruncated) = await LoadAccountsAsync(
                query,
                asOfUtc,
                cancellationToken);
            var reconciliation = await LoadReconciliationAsync(
                query,
                asOfUtc,
                exceptionLimit,
                cancellationToken);
            var paymentWork = await LoadPaymentWorkAsync(
                query,
                cancellationToken);
            var (tasks, tasksTruncated, laura) = await LoadTasksAndLauraAsync(
                query.CompanyId,
                taskLimit,
                cancellationToken);

            var allExceptions = BuildExceptions(
                query,
                asOfUtc,
                liquidity,
                accounts,
                reconciliation,
                paymentWork,
                tasks);
            var exceptions = allExceptions
                .OrderByDescending(item => item.PriorityScore)
                .ThenByDescending(item => item.ObservedUtc)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .Take(exceptionLimit)
                .ToArray();

            var missingEvidence = BuildMissingEvidence(accounts);
            var citations = BuildCitations(query.CompanyId, liquidity, accounts, reconciliation, paymentWork);
            var recommendation = BuildLauraRecommendation(
                query.CompanyId,
                laura,
                liquidity,
                accounts,
                reconciliation,
                paymentWork,
                citations,
                missingEvidence,
                exceptions.Length > 0);
            var globalActions = BuildGlobalActions(
                query,
                liquidity,
                accounts,
                reconciliation,
                paymentWork);

            var evidenceTimes = accounts
                .Select(account => account.EvidenceUtc)
                .Where(value => value.HasValue)
                .Select(value => value!.Value)
                .Append(dashboard.AsOfUtc)
                .ToArray();
            var hasStaleEvidence = accounts.Any(account =>
                account.EvidenceState == TreasuryWorkspaceEvidenceStates.Stale);
            var isTruncated = accountsTruncated || tasksTruncated || allExceptions.Count > exceptionLimit ||
                              reconciliation.TotalUnreconciled > reconciliation.Items.Count ||
                              paymentWork.Approved + paymentWork.Queued + paymentWork.AwaitingAuthorization +
                              paymentWork.Processing + paymentWork.Rejected + paymentWork.ReconciliationRequired >
                              paymentWork.Items.Count;

            var result = new TreasuryWorkspaceDto(
                query.CompanyId,
                asOfUtc,
                evidenceTimes.Length == 0 ? null : evidenceTimes.Max(),
                evidenceTimes.Length == 0 ? null : evidenceTimes.Min(),
                hasStaleEvidence,
                missingEvidence.Count > 0,
                liquidity,
                accounts,
                reconciliation,
                paymentWork,
                exceptions,
                tasks,
                recommendation,
                globalActions,
                isTruncated);
            _telemetry.Loaded(liquidity.RiskLevel, hasStaleEvidence, exceptions.Length,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            return result;
        }
        catch
        {
            _telemetry.Failed(Stopwatch.GetElapsedTime(started).TotalMilliseconds);
            throw;
        }
    }

    private async Task<(IReadOnlyList<TreasuryAccountCoverageDto> Items, bool Truncated)> LoadAccountsAsync(
        GetTreasuryWorkspaceQuery query,
        DateTime asOfUtc,
        CancellationToken cancellationToken)
    {
        var mappingRows = await (
                from mapping in _db.BankAccountMappings.IgnoreQueryFilters().AsNoTracking()
                join discovered in _db.BankDiscoveredAccounts.IgnoreQueryFilters().AsNoTracking()
                    on new { mapping.CompanyId, Id = mapping.DiscoveredAccountId }
                    equals new { discovered.CompanyId, discovered.Id }
                join connection in _db.BankConnections.IgnoreQueryFilters().AsNoTracking()
                    on new { discovered.CompanyId, Id = discovered.ConnectionId }
                    equals new { connection.CompanyId, connection.Id }
                join account in _db.CompanyBankAccounts.IgnoreQueryFilters().AsNoTracking()
                    on new { mapping.CompanyId, Id = mapping.CompanyBankAccountId }
                    equals new { account.CompanyId, account.Id }
                where mapping.CompanyId == query.CompanyId && mapping.IsCurrent && account.IsActive &&
                      connection.Status != BankConnectionStatuses.Disconnected
                orderby account.IsPrimary descending, connection.InstitutionName, account.DisplayName
                select new AccountMappingRow(
                    connection.Id,
                    connection.InstitutionName,
                    connection.Status,
                    connection.HealthStatus,
                    connection.ReasonCode,
                    connection.ReasonSummary,
                    account.Id,
                    account.FinanceAccountId,
                    account.DisplayName,
                    account.MaskedAccountNumber,
                    account.Currency))
            .Take(MaximumAccounts + 1)
            .ToListAsync(cancellationToken);
        var truncated = mappingRows.Count > MaximumAccounts;
        mappingRows = mappingRows.Take(MaximumAccounts).ToList();

        var accountIds = mappingRows.Select(row => row.CompanyBankAccountId).ToArray();
        var checkpoints = accountIds.Length == 0
            ? []
            : await _db.BankFeedCheckpoints.IgnoreQueryFilters().AsNoTracking()
                .Where(row => row.CompanyId == query.CompanyId && accountIds.Contains(row.CompanyBankAccountId))
                .OrderByDescending(row => row.UpdatedUtc)
                .Take(MaximumAccounts * 2)
                .ToListAsync(cancellationToken);
        var checkpointByAccount = checkpoints
            .GroupBy(row => row.CompanyBankAccountId)
            .ToDictionary(group => group.Key, group => group.First());
        var checkpointIds = checkpointByAccount.Values.Select(row => row.Id).ToArray();
        var gaps = checkpointIds.Length == 0
            ? []
            : await _db.BankFeedGaps.IgnoreQueryFilters().AsNoTracking()
                .Where(row => row.CompanyId == query.CompanyId && checkpointIds.Contains(row.CheckpointId) &&
                              row.Status == BankFeedGapStatuses.Open)
                .OrderByDescending(row => row.DetectedUtc)
                .Take(MaximumAccounts * 4)
                .ToListAsync(cancellationToken);
        var gapsByCheckpoint = gaps.GroupBy(row => row.CheckpointId)
            .ToDictionary(group => group.Key, group => group.ToArray());

        var sourceBalances = checkpointIds.Length == 0
            ? []
            : await _db.BankFeedBalanceSnapshots.IgnoreQueryFilters().AsNoTracking()
                .Where(row => row.CompanyId == query.CompanyId && checkpointIds.Contains(row.CheckpointId))
                .OrderByDescending(row => row.ObservedUtc ?? row.CreatedUtc)
                .ThenByDescending(row => row.CreatedUtc)
                .Take(MaximumAccounts * MaximumSourceRowsPerAccount)
                .ToListAsync(cancellationToken);
        var sourceBalanceByCheckpoint = sourceBalances
            .GroupBy(row => row.CheckpointId)
            .ToDictionary(group => group.Key, group => group
                .OrderByDescending(row => row.ObservedUtc ?? row.CreatedUtc)
                .ThenByDescending(row => row.CreatedUtc)
                .First());

        var financeAccountIds = mappingRows.Select(row => row.FinanceAccountId).Distinct().ToArray();
        var financeBalances = financeAccountIds.Length == 0
            ? []
            : await _db.FinanceBalances.IgnoreQueryFilters().AsNoTracking()
                .Where(row => row.CompanyId == query.CompanyId &&
                              financeAccountIds.Contains(row.AccountId) && row.AsOfUtc <= asOfUtc)
                .OrderByDescending(row => row.AsOfUtc)
                .Take(MaximumAccounts * MaximumSourceRowsPerAccount)
                .ToListAsync(cancellationToken);
        var financeBalanceByAccount = financeBalances
            .GroupBy(row => row.AccountId)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(row => row.AsOfUtc).First());

        var result = new List<TreasuryAccountCoverageDto>(mappingRows.Count);
        foreach (var mapping in mappingRows)
        {
            checkpointByAccount.TryGetValue(mapping.CompanyBankAccountId, out var checkpoint);
            var openGaps = checkpoint is null || !gapsByCheckpoint.TryGetValue(checkpoint.Id, out var checkpointGaps)
                ? []
                : checkpointGaps;
            var sourceBalance = checkpoint is null ||
                                !sourceBalanceByCheckpoint.TryGetValue(checkpoint.Id, out var retainedBalance)
                ? null
                : retainedBalance;
            financeBalanceByAccount.TryGetValue(mapping.FinanceAccountId, out var financeBalance);
            var evidenceUtc = sourceBalance?.ObservedUtc ?? sourceBalance?.CreatedUtc ?? financeBalance?.AsOfUtc;
            var balance = sourceBalance?.Amount ?? financeBalance?.Amount;
            var currency = sourceBalance?.Currency ?? financeBalance?.Currency ?? mapping.Currency;
            var evidenceSource = sourceBalance is not null ? "bank_feed_balance" :
                financeBalance is not null ? "finance_balance" : "none";
            var evidenceState = EvidenceState(evidenceUtc, asOfUtc);
            var reasonCode = mapping.ConnectionReasonCode ?? checkpoint?.ReasonCode ?? openGaps.FirstOrDefault()?.ReasonCode;
            var feedStatus = checkpoint?.Status ?? "not_configured";
            var explanation = AccountExplanation(mapping, checkpoint, openGaps.Length, evidenceState);
            var actions = _policy.Evaluate(new TreasuryWorkspacePolicyInput(
                    query.CanEdit,
                    query.CanApprove,
                    mapping.ConnectionStatus,
                    mapping.ConnectionReasonCode,
                    openGaps.Length > 0))
                .Where(action => action.Action is TreasuryWorkspaceActionTypes.Reconnect or
                    TreasuryWorkspaceActionTypes.RecoverGap)
                .Select(action => action with
                {
                    NavigationTarget = BuildBankRecoveryPath(
                        query.CompanyId,
                        mapping.ConnectionId,
                        checkpoint?.Id,
                        openGaps.FirstOrDefault()?.Id)
                })
                .ToArray();

            result.Add(new TreasuryAccountCoverageDto(
                mapping.ConnectionId,
                mapping.CompanyBankAccountId,
                checkpoint?.Id,
                mapping.InstitutionName,
                mapping.AccountName,
                mapping.MaskedAccountNumber,
                balance,
                currency,
                evidenceState,
                evidenceSource,
                evidenceUtc,
                checkpoint?.CoverageFrom,
                checkpoint?.CoverageThrough,
                checkpoint?.LastSuccessfulSyncUtc is null
                    ? null
                    : Math.Max(0, (int)Math.Ceiling((asOfUtc - checkpoint.LastSuccessfulSyncUtc.Value).TotalMinutes)),
                mapping.ConnectionStatus,
                feedStatus,
                reasonCode,
                explanation,
                actions));
        }

        return (result, truncated);
    }

    private async Task<TreasuryReconciliationSummaryDto> LoadReconciliationAsync(
        GetTreasuryWorkspaceQuery query,
        DateTime asOfUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        var baseQuery = _db.BankTransactions.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.CompanyId == query.CompanyId &&
                          row.Status != BankTransactionReconciliationStatuses.Reconciled);
        var total = await baseQuery.CountAsync(cancellationToken);
        var agedBoundary = asOfUtc.Date.AddDays(-AgedReconciliationDays);
        var aged = await baseQuery.CountAsync(row => row.BookingDate < agedBoundary, cancellationToken);
        var oldestUtc = total == 0
            ? null
            : await baseQuery.MinAsync(row => (DateTime?)row.BookingDate, cancellationToken);
        var rows = await baseQuery
            .OrderBy(row => row.BookingDate)
            .ThenBy(row => row.Id)
            .Take(limit)
            .Select(row => new UnreconciledRow(
                row.Id,
                row.BankAccountId,
                row.BankAccount.DisplayName,
                row.BookingDate,
                row.Amount,
                row.ReconciledAmount,
                row.Currency,
                row.Counterparty,
                row.ReferenceText,
                row.Status))
            .ToListAsync(cancellationToken);
        var items = rows.Select(row =>
        {
            var action = _policy.Evaluate(new TreasuryWorkspacePolicyInput(
                    query.CanEdit,
                    query.CanApprove,
                    ReconciliationStatus: row.Status))
                .Single(candidate => candidate.Action == TreasuryWorkspaceActionTypes.Reconcile) with
            {
                NavigationTarget = BuildReconciliationPath(query.CompanyId, row.Id)
            };
            return new TreasuryUnreconciledItemDto(
                row.Id,
                row.BankAccountId,
                row.AccountName,
                row.BookingDate,
                Math.Max(0, (asOfUtc.Date - row.BookingDate.Date).Days),
                row.Amount,
                Math.Max(0m, Math.Abs(row.Amount) - row.ReconciledAmount),
                row.Currency,
                row.Counterparty,
                row.ReferenceText,
                row.Status,
                action);
        }).ToArray();
        return new TreasuryReconciliationSummaryDto(
            total,
            aged,
            oldestUtc.HasValue ? Math.Max(0, (asOfUtc.Date - oldestUtc.Value.Date).Days) : null,
            items);
    }

    private async Task<TreasuryPaymentWorkSummaryDto> LoadPaymentWorkAsync(
        GetTreasuryWorkspaceQuery query,
        CancellationToken cancellationToken)
    {
        var executionCounts = await _db.PaymentBatchExecutions.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.CompanyId == query.CompanyId)
            .GroupBy(row => row.Status)
            .Select(group => new StatusCountRow(group.Key, group.Count()))
            .ToDictionaryAsync(row => row.Status, row => row.Count, StringComparer.OrdinalIgnoreCase, cancellationToken);
        var approvedCount = await _db.PaymentBatches.IgnoreQueryFilters().AsNoTracking()
            .CountAsync(row => row.CompanyId == query.CompanyId && row.Status == PaymentBatchStatuses.Approved &&
                               !_db.PaymentBatchExecutions.IgnoreQueryFilters().Any(execution =>
                                   execution.CompanyId == query.CompanyId && execution.BatchId == row.Id),
                cancellationToken);
        var executionRows = await (
                from execution in _db.PaymentBatchExecutions.IgnoreQueryFilters().AsNoTracking()
                join batch in _db.PaymentBatches.IgnoreQueryFilters().AsNoTracking()
                    on new { execution.CompanyId, Id = execution.BatchId }
                    equals new { batch.CompanyId, batch.Id }
                where execution.CompanyId == query.CompanyId &&
                      execution.Status != PaymentExecutionStatuses.Settled &&
                      execution.Status != PaymentExecutionStatuses.Cancelled
                orderby execution.UpdatedUtc descending
                select new PaymentWorkRow(
                    batch.Id,
                    execution.Id,
                    batch.Reference,
                    execution.Status,
                    execution.CanCancelAtProvider,
                    execution.SafeSummary,
                    execution.UpdatedUtc,
                    _db.PaymentExecutionInstructions.IgnoreQueryFilters()
                        .Where(instruction => instruction.CompanyId == query.CompanyId &&
                                              instruction.ExecutionId == execution.Id)
                        .Sum(instruction => (decimal?)instruction.Amount) ?? 0m,
                    _db.PaymentExecutionInstructions.IgnoreQueryFilters()
                        .Where(instruction => instruction.CompanyId == query.CompanyId &&
                                              instruction.ExecutionId == execution.Id)
                        .OrderBy(instruction => instruction.Sequence)
                        .Select(instruction => instruction.Currency)
                        .FirstOrDefault() ?? "SEK"))
            .Take(MaximumPaymentItems)
            .ToListAsync(cancellationToken);
        var executionBatchIds = executionRows.Select(row => row.BatchId).ToArray();
        var approvedRows = await _db.PaymentBatches.IgnoreQueryFilters().AsNoTracking()
            .Where(batch => batch.CompanyId == query.CompanyId && batch.Status == PaymentBatchStatuses.Approved &&
                            !executionBatchIds.Contains(batch.Id) &&
                            !_db.PaymentBatchExecutions.IgnoreQueryFilters().Any(execution =>
                                execution.CompanyId == query.CompanyId && execution.BatchId == batch.Id))
            .OrderBy(batch => batch.PlannedExecutionDate)
            .ThenBy(batch => batch.Id)
            .Take(Math.Max(0, MaximumPaymentItems - executionRows.Count))
            .Select(batch => new PaymentWorkRow(
                batch.Id,
                null,
                batch.Reference,
                PaymentBatchStatuses.Approved,
                false,
                "Approved instructions are ready for the payment execution authority recheck.",
                batch.UpdatedUtc,
                _db.PaymentInstructions.IgnoreQueryFilters()
                    .Where(instruction => instruction.CompanyId == query.CompanyId &&
                                          instruction.BatchId == batch.Id && instruction.IsCurrent)
                    .Sum(instruction => (decimal?)instruction.Amount) ?? 0m,
                _db.PaymentInstructions.IgnoreQueryFilters()
                    .Where(instruction => instruction.CompanyId == query.CompanyId &&
                                          instruction.BatchId == batch.Id && instruction.IsCurrent)
                    .OrderBy(instruction => instruction.Sequence)
                    .Select(instruction => instruction.Currency)
                    .FirstOrDefault() ?? "SEK"))
            .ToListAsync(cancellationToken);

        var items = executionRows.Concat(approvedRows)
            .OrderByDescending(row => PaymentPriority(row.Status))
            .ThenByDescending(row => row.UpdatedUtc)
            .Take(MaximumPaymentItems)
            .Select(row =>
            {
                var decisions = _policy.Evaluate(new TreasuryWorkspacePolicyInput(
                    query.CanEdit,
                    query.CanApprove,
                    PaymentStatus: row.Status,
                    PaymentCanCancel: row.CanCancelAtProvider));
                var target = BuildPaymentBatchPath(query.CompanyId, row.BatchId);
                return new TreasuryPaymentWorkItemDto(
                    row.BatchId,
                    row.ExecutionId,
                    row.Reference,
                    row.Status,
                    PaymentSeverity(row.Status),
                    row.Amount,
                    row.Currency,
                    PaymentExplanation(row.Status, row.SafeSummary),
                    row.UpdatedUtc,
                    decisions.Single(action => action.Action == TreasuryWorkspaceActionTypes.ReviewPayment) with
                    {
                        NavigationTarget = target
                    },
                    decisions.Single(action => action.Action == TreasuryWorkspaceActionTypes.CancelPayment) with
                    {
                        NavigationTarget = target
                    });
            })
            .ToArray();

        return new TreasuryPaymentWorkSummaryDto(
            approvedCount,
            Count(executionCounts, PaymentExecutionStatuses.Queued) + Count(executionCounts, PaymentExecutionStatuses.Submitting),
            Count(executionCounts, PaymentExecutionStatuses.AwaitingAuthorization),
            Count(executionCounts, PaymentExecutionStatuses.ProviderAccepted) + Count(executionCounts, PaymentExecutionStatuses.Processing) +
            Count(executionCounts, PaymentExecutionStatuses.ProviderCompleted),
            Count(executionCounts, PaymentExecutionStatuses.Rejected),
            Count(executionCounts, PaymentExecutionStatuses.ReconciliationRequired),
            Count(executionCounts, PaymentExecutionStatuses.Settled),
            items);
    }

    private async Task<(IReadOnlyList<TreasuryWorkspaceTaskDto> Items, bool Truncated, LauraRow? Laura)>
        LoadTasksAndLauraAsync(Guid companyId, int limit, CancellationToken cancellationToken)
    {
        var laura = await _db.Agents.IgnoreQueryFilters().AsNoTracking()
            .Where(agent => agent.CompanyId == companyId &&
                            (agent.TemplateId == CoreAgentTemplateIds.Finance ||
                             agent.DisplayName == "Laura" && agent.Department == "Finance"))
            .OrderByDescending(agent => agent.TemplateId == CoreAgentTemplateIds.Finance)
            .Select(agent => new LauraRow(agent.Id, agent.DisplayName, agent.RoleName, agent.AvatarUrl))
            .FirstOrDefaultAsync(cancellationToken);
        var taskRows = await _db.WorkTasks.IgnoreQueryFilters().AsNoTracking()
            .Where(task => task.CompanyId == companyId && task.Status != WorkTaskStatus.Completed &&
                           (laura != null && task.AssignedAgentId == laura.Id ||
                            task.Type.Contains("finance") ||
                            task.TriggerSource != null && task.TriggerSource.Contains("finance")))
            .OrderByDescending(task => task.Priority)
            .ThenBy(task => task.DueUtc == null)
            .ThenBy(task => task.DueUtc)
            .ThenByDescending(task => task.UpdatedUtc)
            .Take(limit + 1)
            .Select(task => new TaskRow(
                task.Id,
                task.Title,
                task.Description,
                task.Priority,
                task.Status,
                task.DueUtc,
                task.AssignedAgentId))
            .ToListAsync(cancellationToken);
        var truncated = taskRows.Count > limit;
        var tasks = taskRows.Take(limit).Select(task => new TreasuryWorkspaceTaskDto(
            task.Id,
            task.Title,
            task.Description,
            task.Priority.ToStorageValue(),
            task.Status.ToStorageValue(),
            task.DueUtc,
            laura is not null && task.AssignedAgentId == laura.Id ? laura.DisplayName : "Finance team",
            BuildTaskPath(companyId, task.Id))).ToArray();
        return (tasks, truncated, laura);
    }

    private List<TreasuryWorkspaceExceptionDto> BuildExceptions(
        GetTreasuryWorkspaceQuery query,
        DateTime asOfUtc,
        TreasuryLiquiditySummaryDto liquidity,
        IReadOnlyList<TreasuryAccountCoverageDto> accounts,
        TreasuryReconciliationSummaryDto reconciliation,
        TreasuryPaymentWorkSummaryDto paymentWork,
        IReadOnlyList<TreasuryWorkspaceTaskDto> tasks)
    {
        var result = new List<TreasuryWorkspaceExceptionDto>();
        foreach (var account in accounts)
        {
            var reconnect = account.AllowedActions.Single(action => action.Action == TreasuryWorkspaceActionTypes.Reconnect);
            var recoverGap = account.AllowedActions.Single(action => action.Action == TreasuryWorkspaceActionTypes.RecoverGap);
            if (account.ConnectionStatus != BankConnectionStatuses.Active)
            {
                result.Add(new($"connection:{account.ConnectionId:N}", "bank_connection",
                    TreasuryWorkspaceSeverity.Critical,
                    $"{account.AccountName} needs reconnection",
                    account.Explanation,
                    account.Balance,
                    account.Currency,
                    account.EvidenceUtc ?? asOfUtc,
                    100,
                    reconnect));
            }
            if (recoverGap.ReasonCode == TreasuryWorkspaceReasonCodes.FeedGapOpen)
            {
                result.Add(new($"feed-gap:{account.CheckpointId:N}", "bank_feed_gap",
                    TreasuryWorkspaceSeverity.High,
                    $"Missing bank-feed range for {account.AccountName}",
                    account.Explanation,
                    account.Balance,
                    account.Currency,
                    account.EvidenceUtc ?? asOfUtc,
                    90,
                    recoverGap));
            }
            else if (account.EvidenceState == TreasuryWorkspaceEvidenceStates.Stale)
            {
                result.Add(new($"stale-feed:{account.CompanyBankAccountId:N}", "stale_evidence",
                    TreasuryWorkspaceSeverity.Medium,
                    $"Stale balance evidence for {account.AccountName}",
                    account.Explanation,
                    account.Balance,
                    account.Currency,
                    account.EvidenceUtc ?? asOfUtc,
                    65,
                    reconnect.IsAllowed ? reconnect : recoverGap));
            }
        }

        foreach (var payment in paymentWork.Items.Where(item =>
                     item.Status is PaymentExecutionStatuses.ReconciliationRequired or PaymentExecutionStatuses.Rejected or
                         PaymentExecutionStatuses.AwaitingAuthorization or PaymentBatchStatuses.Approved))
        {
            result.Add(new($"payment:{payment.ExecutionId?.ToString("N") ?? payment.BatchId.ToString("N")}",
                "payment",
                payment.Severity,
                PaymentExceptionTitle(payment.Status, payment.Reference),
                payment.Explanation,
                payment.Amount,
                payment.Currency,
                payment.UpdatedUtc,
                PaymentPriority(payment.Status),
                payment.ReviewAction));
        }

        foreach (var item in reconciliation.Items)
        {
            result.Add(new($"reconciliation:{item.BankTransactionId:N}", "reconciliation",
                item.AgeDays >= AgedReconciliationDays ? TreasuryWorkspaceSeverity.High : TreasuryWorkspaceSeverity.Medium,
                $"Unreconciled bank transaction from {item.Counterparty}",
                item.AgeDays >= AgedReconciliationDays
                    ? $"This retained bank row has remained unreconciled for {item.AgeDays} days."
                    : "Review the retained bank row and available matching evidence.",
                item.RemainingAmount,
                item.Currency,
                item.BookingDateUtc,
                Math.Min(85, 55 + item.AgeDays),
                item.Action));
        }

        var liquidityAction = _policy.Evaluate(new TreasuryWorkspacePolicyInput(
                query.CanEdit,
                query.CanApprove,
                LiquidityRisk: liquidity.RiskLevel))
            .Single(action => action.Action == TreasuryWorkspaceActionTypes.InvestigateLiquidity) with
        {
            NavigationTarget = BuildCashPath(query.CompanyId)
        };
        if (liquidityAction.IsAllowed)
        {
            result.Add(new("liquidity", "liquidity", liquidity.RiskLevel == "critical"
                    ? TreasuryWorkspaceSeverity.Critical
                    : TreasuryWorkspaceSeverity.High,
                "Short-horizon liquidity needs review",
                $"Projected cash is {liquidity.ProjectedCash:0.##} {liquidity.Currency} through {liquidity.ProjectionThroughUtc:yyyy-MM-dd}.",
                liquidity.ProjectedCash,
                liquidity.Currency,
                asOfUtc,
                liquidity.RiskLevel == "critical" ? 95 : 80,
                liquidityAction));
        }

        foreach (var task in tasks.Where(task => task.Status is WorkTaskStatusValues.Blocked or WorkTaskStatusValues.Failed))
        {
            result.Add(new($"task:{task.TaskId:N}", "task", TreasuryWorkspaceSeverity.High,
                task.Title,
                task.Description ?? "Finance work is blocked and needs a human review.",
                null,
                null,
                task.DueUtc ?? asOfUtc,
                75,
                new TreasuryWorkspaceActionDecisionDto("review_task", true,
                    TreasuryWorkspaceReasonCodes.Allowed,
                    "Open the retained finance task and its evidence.",
                    false,
                    task.NavigationTarget)));
        }

        return result;
    }

    private IReadOnlyList<TreasuryWorkspaceActionDecisionDto> BuildGlobalActions(
        GetTreasuryWorkspaceQuery query,
        TreasuryLiquiditySummaryDto liquidity,
        IReadOnlyList<TreasuryAccountCoverageDto> accounts,
        TreasuryReconciliationSummaryDto reconciliation,
        TreasuryPaymentWorkSummaryDto paymentWork)
    {
        var connection = accounts.FirstOrDefault(account => account.ConnectionStatus != BankConnectionStatuses.Active);
        var gap = accounts.FirstOrDefault(account => account.AllowedActions.Any(action =>
            action.Action == TreasuryWorkspaceActionTypes.RecoverGap &&
            action.ReasonCode == TreasuryWorkspaceReasonCodes.FeedGapOpen));
        var payment = paymentWork.Items.FirstOrDefault();
        var reconciliationItem = reconciliation.Items.FirstOrDefault();
        return _policy.Evaluate(new TreasuryWorkspacePolicyInput(
                query.CanEdit,
                query.CanApprove,
                connection?.ConnectionStatus,
                connection?.ReasonCode,
                gap is not null,
                reconciliationItem?.Status,
                payment?.Status,
                payment?.CancelAction.ReasonCode is TreasuryWorkspaceReasonCodes.PaymentCancellationAllowed or
                    TreasuryWorkspaceReasonCodes.FinanceApprovalRequired,
                liquidity.RiskLevel))
            .Select(action => action with
            {
                NavigationTarget = action.Action switch
                {
                    TreasuryWorkspaceActionTypes.Reconnect => connection?.AllowedActions.FirstOrDefault(item =>
                        item.Action == TreasuryWorkspaceActionTypes.Reconnect)?.NavigationTarget,
                    TreasuryWorkspaceActionTypes.RecoverGap => gap?.AllowedActions.FirstOrDefault(item =>
                        item.Action == TreasuryWorkspaceActionTypes.RecoverGap)?.NavigationTarget,
                    TreasuryWorkspaceActionTypes.Reconcile => reconciliationItem?.Action.NavigationTarget,
                    TreasuryWorkspaceActionTypes.ReviewPayment or TreasuryWorkspaceActionTypes.CancelPayment =>
                        payment?.ReviewAction.NavigationTarget,
                    TreasuryWorkspaceActionTypes.InvestigateLiquidity => BuildCashPath(query.CompanyId),
                    _ => null
                }
            })
            .ToArray();
    }

    private static TreasuryLiquiditySummaryDto BuildLiquidity(
        DashboardFinanceSnapshotDto dashboard,
        FinanceCashPositionDto cashPosition,
        int horizonDays,
        DateTime asOfUtc)
    {
        var projected = Math.Round(dashboard.CurrentCashBalance + dashboard.ExpectedIncomingCash -
                                   dashboard.ExpectedOutgoingCash, 2, MidpointRounding.AwayFromZero);
        var risk = ResolveLiquidityRisk(projected, cashPosition);
        var through = DateTime.SpecifyKind(
            asOfUtc.Date.AddDays(horizonDays).AddDays(1).AddTicks(-1),
            DateTimeKind.Utc);
        return new TreasuryLiquiditySummaryDto(
            dashboard.CurrentCashBalance,
            projected,
            dashboard.ExpectedIncomingCash,
            dashboard.ExpectedOutgoingCash,
            dashboard.Currency,
            horizonDays,
            through,
            risk,
            cashPosition.EstimatedRunwayDays,
            cashPosition.Thresholds.WarningCashAmount,
            cashPosition.Thresholds.CriticalCashAmount,
            cashPosition.Thresholds.WarningRunwayDays,
            cashPosition.Thresholds.CriticalRunwayDays,
            [
                new TreasuryProjectionPointDto(DateOnly.FromDateTime(asOfUtc),
                    dashboard.CurrentCashBalance, "Posted cash evidence at the workspace as-of time."),
                new TreasuryProjectionPointDto(DateOnly.FromDateTime(through), projected,
                    "Open receivables and payables due inside the selected horizon, net of retained payment allocations.")
            ]);
    }

    private static string ResolveLiquidityRisk(decimal projected, FinanceCashPositionDto position)
    {
        if (position.Thresholds.CriticalCashAmount.HasValue && projected <= position.Thresholds.CriticalCashAmount.Value)
            return "critical";
        if (position.Thresholds.WarningCashAmount.HasValue && projected <= position.Thresholds.WarningCashAmount.Value)
            return "warning";
        return position.AlertState.RiskLevel is "critical" or "warning" or "missing"
            ? position.AlertState.RiskLevel
            : "healthy";
    }

    private static IReadOnlyList<string> BuildMissingEvidence(IReadOnlyList<TreasuryAccountCoverageDto> accounts)
    {
        var missing = new List<string>();
        if (accounts.Count == 0)
            missing.Add("No mapped connected bank account evidence is available.");
        foreach (var account in accounts)
        {
            if (account.EvidenceState == TreasuryWorkspaceEvidenceStates.Missing)
                missing.Add($"{account.AccountName} has no retained balance evidence.");
            else if (account.EvidenceState == TreasuryWorkspaceEvidenceStates.Stale)
                missing.Add($"{account.AccountName} balance evidence is older than the freshness policy.");
            if (account.ConnectionStatus != BankConnectionStatuses.Active)
                missing.Add($"{account.AccountName} connection is not active.");
            if (account.AllowedActions.Any(action => action.ReasonCode == TreasuryWorkspaceReasonCodes.FeedGapOpen))
                missing.Add($"{account.AccountName} has an open bank-feed range gap.");
        }
        return missing.Distinct(StringComparer.OrdinalIgnoreCase).Take(10).ToArray();
    }

    private static IReadOnlyList<TreasuryEvidenceReferenceDto> BuildCitations(
        Guid companyId,
        TreasuryLiquiditySummaryDto liquidity,
        IReadOnlyList<TreasuryAccountCoverageDto> accounts,
        TreasuryReconciliationSummaryDto reconciliation,
        TreasuryPaymentWorkSummaryDto paymentWork)
    {
        var citations = new List<TreasuryEvidenceReferenceDto>
        {
            new($"treasury-projection:{companyId:N}:{liquidity.ProjectionThroughUtc:yyyyMMdd}",
                "cash_projection",
                $"{liquidity.HorizonDays}-day cash projection",
                liquidity.ProjectionThroughUtc,
                BuildCashPath(companyId))
        };
        citations.AddRange(accounts.OrderBy(account => account.EvidenceUtc).Take(2).Select(account =>
            new TreasuryEvidenceReferenceDto(
                $"bank-account:{account.CompanyBankAccountId:N}:{account.EvidenceUtc?.Ticks ?? 0}",
                account.EvidenceSource,
                account.AccountName,
                account.EvidenceUtc,
                BuildBankRecoveryPath(companyId, account.ConnectionId, account.CheckpointId, null))));
        if (paymentWork.Items.FirstOrDefault() is { } payment)
            citations.Add(new($"payment-batch:{payment.BatchId:N}", "payment_batch", payment.Reference,
                payment.UpdatedUtc, BuildPaymentBatchPath(companyId, payment.BatchId)));
        if (reconciliation.Items.FirstOrDefault() is { } reconciliationItem)
            citations.Add(new($"bank-transaction:{reconciliationItem.BankTransactionId:N}", "bank_transaction",
                reconciliationItem.ReferenceText, reconciliationItem.BookingDateUtc,
                BuildReconciliationPath(companyId, reconciliationItem.BankTransactionId)));
        return citations.Take(5).ToArray();
    }

    private static TreasuryLauraRecommendationDto BuildLauraRecommendation(
        Guid companyId,
        LauraRow? laura,
        TreasuryLiquiditySummaryDto liquidity,
        IReadOnlyList<TreasuryAccountCoverageDto> accounts,
        TreasuryReconciliationSummaryDto reconciliation,
        TreasuryPaymentWorkSummaryDto paymentWork,
        IReadOnlyList<TreasuryEvidenceReferenceDto> citations,
        IReadOnlyList<string> missingEvidence,
        bool hasExceptions)
    {
        var summary = accounts.Any(account => account.ConnectionStatus != BankConnectionStatuses.Active ||
                                              account.AllowedActions.Any(action => action.ReasonCode == TreasuryWorkspaceReasonCodes.FeedGapOpen))
            ? "Recover the missing bank evidence before relying on the short-horizon cash projection, then review the remaining exceptions in priority order."
            : paymentWork.ReconciliationRequired > 0
                ? "Review ambiguous payment outcomes against provider acknowledgements before any retry or new instruction is considered."
                : liquidity.RiskLevel is "critical" or "warning"
                    ? $"Projected cash is {liquidity.ProjectedCash:0.##} {liquidity.Currency} over the next {liquidity.HorizonDays} days. Review the cited inflows, outflows, and configured thresholds."
                    : reconciliation.AgedUnreconciled > 0
                        ? $"Cash coverage is currently stable, but {reconciliation.AgedUnreconciled} aged bank item(s) should be reconciled to keep the evidence current."
                        : "Cash coverage and retained source evidence are current for this review window. Continue with the highest-priority finance task."
        ;
        return new TreasuryLauraRecommendationDto(
            laura?.Id,
            laura?.DisplayName ?? "Laura",
            laura?.RoleName ?? "Finance Manager",
            laura?.AvatarUrl,
            "recommend_only",
            summary,
            citations,
            missingEvidence,
            hasExceptions || missingEvidence.Count > 0,
            $"/agents?companyId={companyId:D}&agent=Laura");
    }

    private static string AccountExplanation(
        AccountMappingRow mapping,
        VirtualCompany.Domain.Entities.BankFeedCheckpoint? checkpoint,
        int openGapCount,
        string evidenceState)
    {
        if (mapping.ConnectionStatus != BankConnectionStatuses.Active)
            return mapping.ConnectionReasonSummary ?? "The bank connection needs renewal or recovery before new evidence can be trusted.";
        if (openGapCount > 0)
            return $"{openGapCount} retained missing range(s) require bounded recovery. Coverage does not claim completeness.";
        if (checkpoint?.Status == BankFeedCheckpointStatuses.AttentionRequired)
            return checkpoint.FailureSummary ?? "The bank feed requires an operator review.";
        if (evidenceState == TreasuryWorkspaceEvidenceStates.Missing)
            return "No retained source balance is available for this mapped account.";
        if (evidenceState == TreasuryWorkspaceEvidenceStates.Stale)
            return "The latest retained balance evidence is older than the six-hour freshness policy.";
        return "Retained source balance evidence and feed coverage are current.";
    }

    private static string EvidenceState(DateTime? evidenceUtc, DateTime asOfUtc) => evidenceUtc is null
        ? TreasuryWorkspaceEvidenceStates.Missing
        : asOfUtc - evidenceUtc.Value > TimeSpan.FromMinutes(StaleEvidenceMinutes)
            ? TreasuryWorkspaceEvidenceStates.Stale
            : TreasuryWorkspaceEvidenceStates.Current;

    private static int Count(IReadOnlyDictionary<string, int> counts, string status) =>
        counts.TryGetValue(status, out var count) ? count : 0;

    private static int PaymentPriority(string status) => status switch
    {
        PaymentExecutionStatuses.ReconciliationRequired => 98,
        PaymentExecutionStatuses.Rejected => 94,
        PaymentExecutionStatuses.AwaitingAuthorization => 78,
        PaymentBatchStatuses.Approved => 70,
        PaymentExecutionStatuses.Queued or PaymentExecutionStatuses.Submitting => 60,
        PaymentExecutionStatuses.ProviderAccepted or PaymentExecutionStatuses.Processing => 50,
        PaymentExecutionStatuses.ProviderCompleted => 45,
        _ => 20
    };

    private static string PaymentSeverity(string status) => status switch
    {
        PaymentExecutionStatuses.ReconciliationRequired or PaymentExecutionStatuses.Rejected =>
            TreasuryWorkspaceSeverity.Critical,
        PaymentExecutionStatuses.AwaitingAuthorization => TreasuryWorkspaceSeverity.High,
        PaymentBatchStatuses.Approved => TreasuryWorkspaceSeverity.Medium,
        _ => TreasuryWorkspaceSeverity.Info
    };

    private static string PaymentExplanation(string status, string? safeSummary) => status switch
    {
        PaymentExecutionStatuses.ReconciliationRequired => safeSummary ??
            "The provider outcome is ambiguous. Reconcile retained evidence; do not resubmit blindly.",
        PaymentExecutionStatuses.Rejected => safeSummary ??
            "The provider rejected the instructions. Review the retained reason before creating new work.",
        PaymentExecutionStatuses.AwaitingAuthorization =>
            "The bank authorization step is still required. The payment is not complete or settled.",
        PaymentBatchStatuses.Approved =>
            "The native instructions are approved but no bank execution outcome is claimed.",
        PaymentExecutionStatuses.Queued or PaymentExecutionStatuses.Submitting =>
            "The durable worker is processing approved instructions. No bank outcome is claimed yet.",
        PaymentExecutionStatuses.ProviderAccepted or PaymentExecutionStatuses.Processing =>
            "The bank has acknowledged processing. Continue status reconciliation until final evidence arrives.",
        PaymentExecutionStatuses.ProviderCompleted =>
            "The provider reports completion. Exact booked bank evidence is still required for settlement.",
        _ => safeSummary ?? "Review retained payment evidence."
    };

    private static string PaymentExceptionTitle(string status, string reference) => status switch
    {
        PaymentExecutionStatuses.ReconciliationRequired => $"Payment {reference} needs reconciliation",
        PaymentExecutionStatuses.Rejected => $"Payment {reference} was rejected",
        PaymentExecutionStatuses.AwaitingAuthorization => $"Payment {reference} awaits bank authorization",
        PaymentBatchStatuses.Approved => $"Payment {reference} is approved and ready for review",
        _ => $"Review payment {reference}"
    };

    private static string BuildCashPath(Guid companyId) => $"/finance/cash-position?companyId={companyId:D}";
    private static string BuildBankRecoveryPath(Guid companyId, Guid connectionId, Guid? checkpointId, Guid? gapId)
    {
        var path = $"/finance/settings/bank-connections?companyId={companyId:D}&connectionId={connectionId:D}";
        if (checkpointId.HasValue) path += $"&checkpointId={checkpointId.Value:D}";
        if (gapId.HasValue) path += $"&gapId={gapId.Value:D}";
        return path;
    }
    private static string BuildReconciliationPath(Guid companyId, Guid transactionId) =>
        $"/finance/accounting/reconciliation/{transactionId:D}?companyId={companyId:D}";
    private static string BuildPaymentBatchPath(Guid companyId, Guid batchId) =>
        $"/finance/payments/batches/{batchId:D}?companyId={companyId:D}";
    private static string BuildTaskPath(Guid companyId, Guid taskId) =>
        $"/work?companyId={companyId:D}&taskId={taskId:D}";

    private void EnsureTenant(Guid companyId)
    {
        if (companyId == Guid.Empty)
            throw new ArgumentException("Company id is required.", nameof(companyId));
        if (_companyContext?.CompanyId is Guid scopedCompanyId && scopedCompanyId != companyId)
            throw new UnauthorizedAccessException("The treasury workspace is scoped to the active company context.");
    }

    private static DateTime? NormalizeUtc(DateTime? value) => value switch
    {
        null => null,
        { Kind: DateTimeKind.Utc } utc => utc,
        { } local => local.ToUniversalTime()
    };

    private sealed record AccountMappingRow(
        Guid ConnectionId,
        string InstitutionName,
        string ConnectionStatus,
        string ConnectionHealthStatus,
        string? ConnectionReasonCode,
        string? ConnectionReasonSummary,
        Guid CompanyBankAccountId,
        Guid FinanceAccountId,
        string AccountName,
        string MaskedAccountNumber,
        string Currency);

    private sealed record UnreconciledRow(
        Guid Id,
        Guid BankAccountId,
        string AccountName,
        DateTime BookingDate,
        decimal Amount,
        decimal ReconciledAmount,
        string Currency,
        string Counterparty,
        string ReferenceText,
        string Status);

    private sealed record StatusCountRow(string Status, int Count);
    private sealed record PaymentWorkRow(Guid BatchId, Guid? ExecutionId, string Reference,
        string Status, bool CanCancelAtProvider, string? SafeSummary, DateTime UpdatedUtc,
        decimal Amount, string Currency);
    private sealed record LauraRow(Guid Id, string DisplayName, string RoleName, string? AvatarUrl);
    private sealed record TaskRow(Guid Id, string Title, string? Description, WorkTaskPriority Priority,
        WorkTaskStatus Status, DateTime? DueUtc, Guid? AssignedAgentId);
}
