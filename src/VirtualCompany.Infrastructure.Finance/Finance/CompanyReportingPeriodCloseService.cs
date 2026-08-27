using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Shared;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CompanyReportingPeriodCloseService : IReportingPeriodCloseService
{
    private const int DefaultMaxAttempts = 3;

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly ICompanyMembershipContextResolver _membershipContextResolver;
    private readonly ICurrentUserAccessor _currentUserAccessor;
    private readonly IAuditEventWriter _auditEventWriter;
    private readonly IAccountingReportingService _accountingReportingService;
    private readonly IAccountingPolicyPackResolver _packResolver;
    private readonly IVatReturnService _vatReturnService;

    public CompanyReportingPeriodCloseService(
        VirtualCompanyDbContext dbContext,
        ICompanyMembershipContextResolver membershipContextResolver,
        ICurrentUserAccessor currentUserAccessor,
        IAuditEventWriter auditEventWriter,
        IAccountingReportingService accountingReportingService,
        IAccountingPolicyPackResolver packResolver,
        IVatReturnService vatReturnService)
    {
        _dbContext = dbContext;
        _membershipContextResolver = membershipContextResolver;
        _currentUserAccessor = currentUserAccessor;
        _auditEventWriter = auditEventWriter;
        _accountingReportingService = accountingReportingService;
        _packResolver = packResolver;
        _vatReturnService = vatReturnService;
    }

    public async Task<ReportingPeriodCloseValidationResultDto> ValidateAsync(
        ValidateReportingPeriodCloseQuery query,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(query.CompanyId, cancellationToken);
        EnsureFinanceViewPermission(membership);
        var actor = RequireUserActor();
        var period = await LoadFiscalPeriodAsync(query.CompanyId, query.FiscalPeriodId, track: true, cancellationToken);
        var issues = await BuildBlockingIssuesAsync(query.CompanyId, period.Id, cancellationToken);
        var executedAtUtc = DateTime.UtcNow;
        period.RecordCloseValidation(actor.ActorId, executedAtUtc);
        var result = new ReportingPeriodCloseValidationResultDto(
            query.CompanyId,
            period.Id,
            period.Name,
            executedAtUtc,
            actor.ActorType,
            actor.ActorId,
            membership.MembershipId,
            membership.MembershipRole.ToStorageValue(),
            issues.Count == 0,
            period.IsClosed,
            period.IsReportingLocked,
            issues);

        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                query.CompanyId,
                actor.ActorType,
                actor.ActorId,
                AuditEventActions.ReportingPeriodCloseValidationExecuted,
                AuditTargetTypes.FiscalPeriod,
                period.Id.ToString("D"),
                result.IsReadyToClose ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Failed,
                result.IsReadyToClose
                    ? $"Executed reporting close validation for fiscal period '{period.Name}' with no blocking issues."
                    : $"Executed reporting close validation for fiscal period '{period.Name}' and found {issues.Count} blocking issue type(s).",
                Metadata: new Dictionary<string, string?>
                {
                    ["fiscalPeriodName"] = period.Name,
                    ["actorMembershipId"] = membership.MembershipId.ToString("D"),
                    ["actorMembershipRole"] = membership.MembershipRole.ToStorageValue(),
                    ["validatedByUserId"] = actor.ActorId?.ToString("D"),
                    ["blockingIssueCodes"] = issues.Count == 0 ? string.Empty : string.Join(",", issues.Select(x => x.Code)),
                    ["isReadyToClose"] = result.IsReadyToClose ? "true" : "false",
                    ["issueCount"] = issues.Count.ToString(),
                    ["isClosed"] = period.IsClosed ? "true" : "false",
                    ["isReportingLocked"] = period.IsReportingLocked ? "true" : "false"
                },
                OccurredUtc: executedAtUtc),
            cancellationToken);

        await SavePeriodChangesAsync(cancellationToken);
        return result;
    }

    public async Task<ReportingPeriodLockStateDto> LockAsync(
        LockReportingPeriodCommand command,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(command.CompanyId, cancellationToken);
        EnsureFinanceEditPermission(membership);
        var actor = RequireUserActor();
        var period = await LoadFiscalPeriodAsync(command.CompanyId, command.FiscalPeriodId, track: true, cancellationToken);

        EnsureClosed(period);
        if (!period.IsReportingLocked)
        {
            var lockedAtUtc = DateTime.UtcNow;
            period.LockReporting(actor.ActorId, lockedAtUtc);

            await _auditEventWriter.WriteAsync(
                new AuditEventWriteRequest(
                    command.CompanyId,
                    actor.ActorType,
                    actor.ActorId,
                    AuditEventActions.ReportingPeriodLockApplied,
                    AuditTargetTypes.FiscalPeriod,
                    period.Id.ToString("D"),
                    AuditEventOutcomes.Succeeded,
                    $"Applied reporting lock for closed fiscal period '{period.Name}'.",
                    Metadata: new Dictionary<string, string?>
                    {
                        ["fiscalPeriodName"] = period.Name,
                        ["actorMembershipId"] = membership.MembershipId.ToString("D"),
                        ["actorMembershipRole"] = membership.MembershipRole.ToStorageValue(),
                        ["isClosed"] = period.IsClosed ? "true" : "false",
                        ["isReportingLocked"] = period.IsReportingLocked ? "true" : "false",
                        ["reportingLockedAtUtc"] = period.ReportingLockedUtc?.ToString("O"),
                        ["reportingLockedByUserId"] = period.ReportingLockedByUserId?.ToString("D")
                    },
                    OccurredUtc: period.ReportingLockedUtc ?? lockedAtUtc),
                cancellationToken);
        }

        await SavePeriodChangesAsync(cancellationToken);
        return MapLockState(period);
    }

    public async Task<ReportingPeriodLockStateDto> UnlockAsync(
        UnlockReportingPeriodCommand command,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(command.CompanyId, cancellationToken);
        if (membership.MembershipRole is not (CompanyMembershipRole.Owner or CompanyMembershipRole.Admin))
        {
            throw new UnauthorizedAccessException("Only company owners or admins can unlock reporting for a fiscal period.");
        }

        var actor = RequireUserActor();
        var period = await LoadFiscalPeriodAsync(command.CompanyId, command.FiscalPeriodId, track: true, cancellationToken);

        if (period.IsReportingLocked)
        {
            var unlockedAtUtc = DateTime.UtcNow;
            period.UnlockReporting(actor.ActorId, unlockedAtUtc);

            await _auditEventWriter.WriteAsync(
                new AuditEventWriteRequest(
                    command.CompanyId,
                    actor.ActorType,
                    actor.ActorId,
                    AuditEventActions.ReportingPeriodLockRemoved,
                    AuditTargetTypes.FiscalPeriod,
                    period.Id.ToString("D"),
                    AuditEventOutcomes.Succeeded,
                    $"Removed reporting lock for fiscal period '{period.Name}'.",
                    Metadata: new Dictionary<string, string?>
                    {
                        ["fiscalPeriodName"] = period.Name,
                        ["actorMembershipId"] = membership.MembershipId.ToString("D"),
                        ["actorMembershipRole"] = membership.MembershipRole.ToStorageValue(),
                        ["isClosed"] = period.IsClosed ? "true" : "false",
                        ["isReportingLocked"] = period.IsReportingLocked ? "true" : "false",
                        ["reportingUnlockedAtUtc"] = period.ReportingUnlockedUtc?.ToString("O"),
                        ["reportingUnlockedByUserId"] = period.ReportingUnlockedByUserId?.ToString("D")
                    },
                    OccurredUtc: period.ReportingUnlockedUtc ?? unlockedAtUtc),
                cancellationToken);
        }

        await SavePeriodChangesAsync(cancellationToken);
        return MapLockState(period);
    }

    public async Task<ReportingPeriodLockStateDto> CloseAndLockAsync(
        CloseAndLockReportingPeriodCommand command,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(command.CompanyId, cancellationToken);
        EnsureFinanceEditPermission(membership);
        var actor = RequireUserActor();
        var reason = string.IsNullOrWhiteSpace(command.Reason)
            ? throw new ReportingPeriodOperationException("close_reason_required", "A close reason is required.", "Explain why this period is ready to close.")
            : command.Reason.Trim();
        var period = await LoadFiscalPeriodAsync(command.CompanyId, command.FiscalPeriodId, track: true, cancellationToken);
        if (period.IsClosed && period.IsReportingLocked)
        {
            return MapLockState(period);
        }

        var issues = await BuildBlockingIssuesAsync(command.CompanyId, period.Id, cancellationToken);
        if (issues.Count > 0)
        {
            throw new ReportingPeriodOperationException(
                "reporting_period_close_blocked",
                "The period is not ready to close.",
                $"Resolve the {issues.Count} blocking close issue type(s), then run the close review again.");
        }

        var trialBalance = await _accountingReportingService.GetTrialBalanceAsync(
            new GetTrialBalanceQuery(command.CompanyId, period.Id), cancellationToken);
        var now = DateTime.UtcNow;
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        await RegenerateSnapshotsAsync(period, now, cancellationToken);
        period.RecordCloseValidation(actor.ActorId, now);
        period.Close(now);
        period.LockReporting(actor.ActorId, now);
        _dbContext.AccountingPeriodHistory.Add(new AccountingPeriodHistory(Guid.NewGuid(), command.CompanyId,
            period.Id, AccountingPeriodHistoryActions.ClosedAndLocked, actor.ActorId!.Value, reason,
            trialBalance.Checksum, now));
        await _auditEventWriter.WriteAsync(new AuditEventWriteRequest(command.CompanyId, actor.ActorType,
            actor.ActorId, AuditEventActions.ReportingPeriodClosedAndLocked, AuditTargetTypes.FiscalPeriod,
            period.Id.ToString("D"), AuditEventOutcomes.Succeeded,
            $"Closed and locked fiscal period '{period.Name}' after all close checks passed.",
            Metadata: new Dictionary<string, string?>
            {
                ["reason"] = reason,
                ["trialBalanceChecksum"] = trialBalance.Checksum,
                ["membershipRole"] = membership.MembershipRole.ToStorageValue()
            }, OccurredUtc: now), cancellationToken);
        await SavePeriodChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return MapLockState(period);
    }

    public async Task<ReportingPeriodLockStateDto> ReopenAsync(
        ReopenReportingPeriodCommand command,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(command.CompanyId, cancellationToken);
        if (membership.MembershipRole is not (CompanyMembershipRole.Owner or CompanyMembershipRole.Admin))
        {
            throw new UnauthorizedAccessException("Only company owners or admins can reopen a closed fiscal period.");
        }
        var actor = RequireUserActor();
        var reason = string.IsNullOrWhiteSpace(command.Reason) || command.Reason.Trim().Length < 10
            ? throw new ReportingPeriodOperationException("reopen_reason_required", "A detailed reopen reason is required.", "Enter at least 10 characters explaining the exceptional reason for reopening.")
            : command.Reason.Trim();
        var period = await LoadFiscalPeriodAsync(command.CompanyId, command.FiscalPeriodId, track: true, cancellationToken);
        if (!period.IsClosed && !period.IsReportingLocked) return MapLockState(period);
        var now = DateTime.UtcNow;
        period.Reopen(actor.ActorId!.Value, now);
        _dbContext.AccountingPeriodHistory.Add(new AccountingPeriodHistory(Guid.NewGuid(), command.CompanyId,
            period.Id, AccountingPeriodHistoryActions.Reopened, actor.ActorId.Value, reason, null, now));
        await _auditEventWriter.WriteAsync(new AuditEventWriteRequest(command.CompanyId, actor.ActorType,
            actor.ActorId, AuditEventActions.ReportingPeriodReopened, AuditTargetTypes.FiscalPeriod,
            period.Id.ToString("D"), AuditEventOutcomes.Succeeded,
            $"Exceptionally reopened fiscal period '{period.Name}'. Stored snapshots and posted vouchers were preserved.",
            Metadata: new Dictionary<string, string?> { ["reason"] = reason }, OccurredUtc: now), cancellationToken);
        await SavePeriodChangesAsync(cancellationToken);
        return MapLockState(period);
    }

    private async Task SavePeriodChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new ReportingPeriodOperationException(
                ReportingPeriodErrorCodes.ReportingPeriodStateChanged,
                "The fiscal period changed",
                "Another close or reopen action changed this fiscal period. Refresh the close review and try again.");
        }
    }

    public async Task<ReportingPeriodRegenerationRequestResultDto> RegenerateStoredStatementsAsync(
        RegenerateStoredReportingStatementsCommand command,
        CancellationToken cancellationToken)
    {
        var membership = await RequireMembershipAsync(command.CompanyId, cancellationToken);
        EnsureFinanceEditPermission(membership);
        var actor = RequireUserActor();
        var period = await LoadFiscalPeriodAsync(command.CompanyId, command.FiscalPeriodId, track: !command.RunInBackground, cancellationToken);

        EnsureClosed(period);
        await EnsureRegenerationAllowedAsync(period, actor, membership, cancellationToken);

        if (command.RunInBackground)
        {
            var execution = await QueueBackgroundRegenerationAsync(command.CompanyId, period.Id, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            return new ReportingPeriodRegenerationRequestResultDto(
                command.CompanyId,
                period.Id,
                true,
                execution.Id,
                0,
                execution.Status.ToStorageValue(),
                execution.UpdatedUtc,
                execution.CompletedUtc,
                actor.ActorType,
                actor.ActorId,
                MapLockState(period));
        }

        var completedAtUtc = DateTime.UtcNow;
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var snapshotCount = await RegenerateSnapshotsAsync(period, completedAtUtc, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);


        return new ReportingPeriodRegenerationRequestResultDto(
            command.CompanyId,
            period.Id,
            false,
            null,
            snapshotCount,
            BackgroundExecutionStatus.Succeeded.ToStorageValue(),
            completedAtUtc,
            completedAtUtc,
            actor.ActorType,
            actor.ActorId,
            MapLockState(period));
    }

    public async Task<int> RunBackgroundRegenerationAsync(
        Guid companyId,
        Guid fiscalPeriodId,
        string? correlationId,
        CancellationToken cancellationToken)
    {
        var period = await LoadFiscalPeriodAsync(companyId, fiscalPeriodId, track: true, cancellationToken);
        EnsureClosed(period);
        await EnsureRegenerationAllowedAsync(period, SystemActor(correlationId), null, cancellationToken);
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var regeneratedAtUtc = DateTime.UtcNow;
        var snapshotCount = await RegenerateSnapshotsAsync(period, regeneratedAtUtc, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return snapshotCount;
    }

    private async Task<ResolvedCompanyMembershipContext> RequireMembershipAsync(Guid companyId, CancellationToken cancellationToken) =>
        await _membershipContextResolver.ResolveAsync(companyId, cancellationToken)
        ?? throw new UnauthorizedAccessException("The current user does not have an active membership in the requested company.");

    private ActorContext RequireUserActor()
    {
        if (_currentUserAccessor.UserId is not Guid userId)
        {
            throw new UnauthorizedAccessException("A resolved user identity is required for reporting period operations.");
        }

        return new ActorContext(AuditActorTypes.User, userId, null);
    }

    private static ActorContext SystemActor(string? correlationId) =>
        new(AuditActorTypes.System, null, correlationId);

    private static void EnsureClosed(FiscalPeriod period)
    {
        if (!period.IsClosed)
        {
            throw new ReportingPeriodOperationException(
                ReportingPeriodErrorCodes.ReportingPeriodNotClosed,
                "Fiscal period is not closed.",
                $"Fiscal period '{period.Name}' must be closed before reporting can be locked or regenerated.");
        }
    }

    private static void EnsureFinanceViewPermission(ResolvedCompanyMembershipContext membership)
    {
        if (!FinanceAccess.CanView(membership.MembershipRole.ToStorageValue()))
        {
            throw new UnauthorizedAccessException("The current user is not allowed to validate reporting close for the requested company.");
        }
    }

    private static void EnsureFinanceEditPermission(ResolvedCompanyMembershipContext membership)
    {
        if (!FinanceAccess.CanEdit(membership.MembershipRole.ToStorageValue()))
        {
            throw new UnauthorizedAccessException("The current user is not allowed to change reporting lock state or regenerate stored statements for the requested company.");
        }
    }

    private async Task EnsureRegenerationAllowedAsync(
        FiscalPeriod period,
        ActorContext actor,
        ResolvedCompanyMembershipContext? membership,
        CancellationToken cancellationToken)
    {
        if (!period.IsReportingLocked)
        {
            return;
        }

        await _auditEventWriter.WriteAsync(
            new AuditEventWriteRequest(
                period.CompanyId,
                actor.ActorType,
                actor.ActorId,
                AuditEventActions.ReportingPeriodRegenerationBlocked,
                AuditTargetTypes.FiscalPeriod,
                period.Id.ToString("D"),
                AuditEventOutcomes.Denied,
                $"Blocked stored reporting regeneration because fiscal period '{period.Name}' is locked.",
                Metadata: new Dictionary<string, string?>
                {
                    ["fiscalPeriodName"] = period.Name,
                    ["isReportingLocked"] = "true",
                    ["requestedByUserId"] = actor.ActorId?.ToString("D"),
                    ["requestedByMembershipId"] = membership is null ? null : membership.MembershipId.ToString("D"),
                    ["requestedByMembershipRole"] = membership is null ? null : membership.MembershipRole.ToStorageValue(),
                    ["reportingLockedAtUtc"] = period.ReportingLockedUtc?.ToString("O"),
                    ["reportingLockedByUserId"] = period.ReportingLockedByUserId?.ToString("D"),
                    ["correlationId"] = actor.CorrelationId
                },
                CorrelationId: actor.CorrelationId,
                OccurredUtc: DateTime.UtcNow),
            cancellationToken);

        // A denied regeneration is still an auditable outcome. Persist it before
        // returning the stable conflict response that aborts the operation.
        await _dbContext.SaveChangesAsync(cancellationToken);

        throw new ReportingPeriodLockedException(period.Id, period.Name);
    }

    private async Task<BackgroundExecution> QueueBackgroundRegenerationAsync(
        Guid companyId,
        Guid fiscalPeriodId,
        CancellationToken cancellationToken)
    {
        var execution = await _dbContext.BackgroundExecutions
            .IgnoreQueryFilters()
            .SingleOrDefaultAsync(
                x =>
                    x.CompanyId == companyId &&
                    x.ExecutionType == BackgroundExecutionType.FinanceReportRegeneration &&
                    x.RelatedEntityType == BackgroundExecutionRelatedEntityTypes.FiscalPeriod &&
                    x.RelatedEntityId == fiscalPeriodId.ToString("D"),
                cancellationToken);

        var correlationId = Guid.NewGuid().ToString("N");
        var idempotencyKey = $"finance-report-regeneration:{companyId:N}:{fiscalPeriodId:N}";
        if (execution is null)
        {
            execution = new BackgroundExecution(
                Guid.NewGuid(),
                companyId,
                BackgroundExecutionType.FinanceReportRegeneration,
                BackgroundExecutionRelatedEntityTypes.FiscalPeriod,
                fiscalPeriodId.ToString("D"),
                correlationId,
                idempotencyKey,
                DefaultMaxAttempts);
            _dbContext.BackgroundExecutions.Add(execution);
            return execution;
        }

        if (!execution.IsTerminal)
        {
            return execution;
        }

        execution.Queue(DateTime.UtcNow, correlationId, resetAttempts: true);
        return execution;
    }

    private async Task<int> RegenerateSnapshotsAsync(
        FiscalPeriod period,
        DateTime regeneratedAtUtc,
        CancellationToken cancellationToken)
    {
        var accountRows = await _dbContext.FinanceAccounts
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == period.CompanyId)
            .Select(x => new AccountSnapshotSeed(x.Id, x.OpeningBalance, x.Currency))
            .ToListAsync(cancellationToken);

        var ledgerBalances = await _dbContext.LedgerEntryLines
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == period.CompanyId &&
                x.LedgerEntry.Status == LedgerEntryStatuses.Posted &&
                x.LedgerEntry.EntryUtc < period.EndUtc)
            .GroupBy(x => x.FinanceAccountId)
            .Select(group => new
            {
                FinanceAccountId = group.Key,
                SignedAmount = group.Sum(line => line.DebitAmount - line.CreditAmount)
            })
            .ToDictionaryAsync(x => x.FinanceAccountId, x => x.SignedAmount, cancellationToken);

        await _dbContext.TrialBalanceSnapshots
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == period.CompanyId && x.FiscalPeriodId == period.Id)
            .ExecuteDeleteAsync(cancellationToken);

        if (accountRows.Count == 0)
        {
            return 0;
        }

        var snapshots = accountRows
            .Select(account => new TrialBalanceSnapshot(
                Guid.NewGuid(),
                period.CompanyId,
                period.Id,
                account.AccountId,
                account.OpeningBalance + ledgerBalances.GetValueOrDefault(account.AccountId),
                account.Currency,
                regeneratedAtUtc))
            .ToList();

        _dbContext.TrialBalanceSnapshots.AddRange(snapshots);
        var statementSnapshotCount = await CreateFinancialStatementSnapshotsAsync(period, regeneratedAtUtc, cancellationToken);
        return snapshots.Count + statementSnapshotCount;
    }

    private async Task<int> CreateFinancialStatementSnapshotsAsync(
        FiscalPeriod period,
        DateTime generatedAtUtc,
        CancellationToken cancellationToken)
    {
        var profitAndLossLines = await BuildProfitAndLossSnapshotLinesAsync(period, cancellationToken);
        var balanceSheetLines = await BuildBalanceSheetSnapshotLinesAsync(period, cancellationToken);

        var created = 0;
        created += await CreateFinancialStatementSnapshotAsync(period, FinancialStatementType.ProfitAndLoss, profitAndLossLines, generatedAtUtc, cancellationToken);
        created += await CreateFinancialStatementSnapshotAsync(period, FinancialStatementType.BalanceSheet, balanceSheetLines, generatedAtUtc, cancellationToken);
        return created;
    }

    private async Task<int> CreateFinancialStatementSnapshotAsync(
        FiscalPeriod period,
        FinancialStatementType statementType,
        IReadOnlyList<StatementSnapshotLineSeed> lines,
        DateTime generatedAtUtc,
        CancellationToken cancellationToken)
    {
        var nextVersionNumber = await _dbContext.FinancialStatementSnapshots
            .IgnoreQueryFilters()
            .Where(x =>
                x.CompanyId == period.CompanyId &&
                x.FiscalPeriodId == period.Id &&
                x.StatementType == statementType)
            .Select(x => (int?)x.VersionNumber)
            .MaxAsync(cancellationToken) ?? 0;
        nextVersionNumber++;

        var orderedLines = lines
            .Where(x => x.Amount != 0m)
            .OrderBy(x => x.LineOrder)
            .ThenBy(x => x.LineCode, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var currency = ResolveCurrency(orderedLines.Select(x => x.Currency));
        var snapshot = new FinancialStatementSnapshot(
            Guid.NewGuid(),
            period.CompanyId,
            period.Id,
            statementType,
            period.StartUtc,
            period.EndUtc,
            nextVersionNumber,
            ComputeChecksum(orderedLines),
            generatedAtUtc,
            currency);

        _dbContext.FinancialStatementSnapshots.Add(snapshot);
        _dbContext.FinancialStatementSnapshotLines.AddRange(
            orderedLines.Select(line => new FinancialStatementSnapshotLine(
                Guid.NewGuid(),
                period.CompanyId,
                snapshot.Id,
                line.FinanceAccountId,
                line.LineCode,
                line.LineName,
                line.LineOrder,
                line.ReportSection,
                line.LineClassification,
                line.Amount,
                line.Currency)));

        return 1;
    }

    private async Task<List<StatementSnapshotLineSeed>> BuildProfitAndLossSnapshotLinesAsync(
        FiscalPeriod period,
        CancellationToken cancellationToken)
    {
        var mappings = await _dbContext.FinancialStatementMappings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == period.CompanyId &&
                x.IsActive &&
                x.StatementType == FinancialStatementType.ProfitAndLoss)
            .Select(x => new SnapshotMappingRow(
                x.FinanceAccountId,
                x.FinanceAccount.Code,
                x.FinanceAccount.Name,
                x.FinanceAccount.OpeningBalance,
                x.FinanceAccount.Currency,
                x.ReportSection,
                x.LineClassification))
            .ToListAsync(cancellationToken);

        if (mappings.Count == 0)
        {
            return [];
        }

        var accountIds = mappings.Select(x => x.FinanceAccountId).ToArray();
        var postings = await _dbContext.LedgerEntryLines
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == period.CompanyId &&
                x.LedgerEntry.Status == LedgerEntryStatuses.Posted &&
                x.LedgerEntry.EntryUtc >= period.StartUtc &&
                x.LedgerEntry.EntryUtc < period.EndUtc &&
                accountIds.Contains(x.FinanceAccountId))
            .GroupBy(x => x.FinanceAccountId)
            .Select(group => new SnapshotPostingRow(
                group.Key,
                group.Sum(line => line.DebitAmount - line.CreditAmount)))
            .ToDictionaryAsync(x => x.FinanceAccountId, x => x.SignedAmount, cancellationToken);

        return mappings
            .Select((mapping, index) => new StatementSnapshotLineSeed(
                mapping.FinanceAccountId,
                mapping.AccountCode,
                mapping.AccountName,
                index,
                mapping.ReportSection,
                mapping.LineClassification,
                NormalizeAmount(mapping.ReportSection, mapping.LineClassification, postings.GetValueOrDefault(mapping.FinanceAccountId)),
                mapping.Currency))
            .Where(x => x.Amount != 0m)
            .ToList();
    }

    private async Task<List<StatementSnapshotLineSeed>> BuildBalanceSheetSnapshotLinesAsync(
        FiscalPeriod period,
        CancellationToken cancellationToken)
    {
        var mappings = await _dbContext.FinancialStatementMappings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == period.CompanyId &&
                x.IsActive &&
                x.StatementType == FinancialStatementType.BalanceSheet)
            .Select(x => new SnapshotMappingRow(
                x.FinanceAccountId,
                x.FinanceAccount.Code,
                x.FinanceAccount.Name,
                x.FinanceAccount.OpeningBalance,
                x.FinanceAccount.Currency,
                x.ReportSection,
                x.LineClassification))
            .ToListAsync(cancellationToken);

        var lines = new List<StatementSnapshotLineSeed>();
        if (mappings.Count > 0)
        {
            var accountIds = mappings.Select(x => x.FinanceAccountId).ToArray();
            var postings = await _dbContext.LedgerEntryLines
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x =>
                    x.CompanyId == period.CompanyId &&
                    x.LedgerEntry.Status == LedgerEntryStatuses.Posted &&
                    x.LedgerEntry.EntryUtc < period.EndUtc &&
                    accountIds.Contains(x.FinanceAccountId))
                .GroupBy(x => x.FinanceAccountId)
                .Select(group => new SnapshotPostingRow(
                    group.Key,
                    group.Sum(line => line.DebitAmount - line.CreditAmount)))
                .ToDictionaryAsync(x => x.FinanceAccountId, x => x.SignedAmount, cancellationToken);

            lines.AddRange(mappings
                .Select((mapping, index) => new StatementSnapshotLineSeed(
                    mapping.FinanceAccountId,
                    mapping.AccountCode,
                    mapping.AccountName,
                    index,
                    mapping.ReportSection,
                    mapping.LineClassification,
                    NormalizeAmount(
                        mapping.ReportSection,
                        mapping.LineClassification,
                        mapping.OpeningBalance + postings.GetValueOrDefault(mapping.FinanceAccountId)),
                    mapping.Currency))
                .Where(x => x.Amount != 0m));
        }

        var currentEarnings = await CalculateCurrentEarningsAsync(period.CompanyId, period.EndUtc, cancellationToken);
        if (currentEarnings != 0m)
        {
            lines.Add(new StatementSnapshotLineSeed(
                null,
                "current_earnings",
                "Current Earnings",
                int.MaxValue,
                FinancialStatementReportSection.BalanceSheetEquity,
                FinancialStatementLineClassification.Equity,
                Math.Round(currentEarnings, 2, MidpointRounding.AwayFromZero),
                ResolveCurrency(lines.Select(x => x.Currency))));
        }

        return lines;
    }

    private async Task<decimal> CalculateCurrentEarningsAsync(
        Guid companyId,
        DateTime endUtc,
        CancellationToken cancellationToken)
    {
        var rows = await _dbContext.FinancialStatementMappings
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.IsActive &&
                x.StatementType == FinancialStatementType.ProfitAndLoss)
            .Select(x => new SnapshotMappingRow(
                x.FinanceAccountId,
                x.FinanceAccount.Code,
                x.FinanceAccount.Name,
                x.FinanceAccount.OpeningBalance,
                x.FinanceAccount.Currency,
                x.ReportSection,
                x.LineClassification))
            .ToListAsync(cancellationToken);

        if (rows.Count == 0)
        {
            return 0m;
        }

        var accountIds = rows.Select(x => x.FinanceAccountId).ToArray();
        var postings = await _dbContext.LedgerEntryLines
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.LedgerEntry.Status == LedgerEntryStatuses.Posted &&
                x.LedgerEntry.EntryUtc < endUtc &&
                accountIds.Contains(x.FinanceAccountId))
            .GroupBy(x => x.FinanceAccountId)
            .Select(group => new SnapshotPostingRow(
                group.Key,
                group.Sum(line => line.DebitAmount - line.CreditAmount)))
            .ToDictionaryAsync(x => x.FinanceAccountId, x => x.SignedAmount, cancellationToken);

        return Math.Round(rows.Sum(row =>
            NormalizeAmount(
                row.ReportSection,
                row.LineClassification,
                row.OpeningBalance + postings.GetValueOrDefault(row.FinanceAccountId)) *
            (row.LineClassification == FinancialStatementLineClassification.NonOperatingIncome ||
             row.ReportSection == FinancialStatementReportSection.ProfitAndLossRevenue
                ? 1m
                : -1m)), 2, MidpointRounding.AwayFromZero);
    }

    private static decimal NormalizeAmount(
        FinancialStatementReportSection reportSection,
        FinancialStatementLineClassification lineClassification,
        decimal balanceAmount)
    {
        var normalized = reportSection switch
        {
            FinancialStatementReportSection.BalanceSheetAssets => balanceAmount,
            FinancialStatementReportSection.BalanceSheetLiabilities => -balanceAmount,
            FinancialStatementReportSection.BalanceSheetEquity => -balanceAmount,
            FinancialStatementReportSection.ProfitAndLossRevenue => -balanceAmount,
            FinancialStatementReportSection.ProfitAndLossCostOfSales => balanceAmount,
            FinancialStatementReportSection.ProfitAndLossOperatingExpenses => balanceAmount,
            FinancialStatementReportSection.ProfitAndLossTaxes => balanceAmount,
            FinancialStatementReportSection.ProfitAndLossOtherIncomeExpense when lineClassification == FinancialStatementLineClassification.NonOperatingIncome => -balanceAmount,
            FinancialStatementReportSection.ProfitAndLossOtherIncomeExpense => balanceAmount,
            _ => balanceAmount
        };

        return Math.Abs(normalized) < 0.0001m ? 0m : Math.Round(normalized, 2, MidpointRounding.AwayFromZero);
    }

    private static string ComputeChecksum(IEnumerable<StatementSnapshotLineSeed> lines)
    {
        var canonical = string.Join('\n', lines.Select(x =>
            $"{x.LineCode}|{x.Amount.ToString("0.00####", CultureInfo.InvariantCulture)}|{x.Currency}"));
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ResolveCurrency(IEnumerable<string> currencies)
    {
        var distinct = currencies
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return distinct.Count == 1 ? distinct[0] : "MIXED";
    }

    private async Task<IReadOnlyList<ReportingPeriodBlockingIssueDto>> BuildBlockingIssuesAsync(
        Guid companyId,
        Guid fiscalPeriodId,
        CancellationToken cancellationToken)
    {
        var issues = new List<ReportingPeriodBlockingIssueDto>();

        var unpostedEntries = await _dbContext.LedgerEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x =>
                x.CompanyId == companyId &&
                x.FiscalPeriodId == fiscalPeriodId &&
                x.Status != LedgerEntryStatuses.Posted)
            .OrderBy(x => x.EntryNumber)
            .Select(x => x.EntryNumber)
            .ToListAsync(cancellationToken);

        var unpostedCustomerInvoices = await _dbContext.CustomerInvoiceAccountingProfiles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.FiscalPeriodId == fiscalPeriodId &&
                        x.Status != CustomerInvoiceAccountingStatuses.Posted &&
                        x.Status != CustomerInvoiceAccountingStatuses.Reversed)
            .OrderBy(x => x.InvoiceId)
            .Select(x => x.InvoiceId)
            .ToListAsync(cancellationToken);

        var unpostedSupplierBills = await _dbContext.SupplierBillAccountingProfiles
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.FiscalPeriodId == fiscalPeriodId &&
                        x.Status != SupplierBillAccountingStatuses.Posted &&
                        x.Status != SupplierBillAccountingStatuses.Reversed)
            .OrderBy(x => x.BillId)
            .Select(x => x.BillId)
            .ToListAsync(cancellationToken);

        var unpostedSourceReferences = unpostedEntries
            .Select(x => $"journal:{x}")
            .Concat(unpostedCustomerInvoices.Select(x => $"customer-invoice:{x:D}"))
            .Concat(unpostedSupplierBills.Select(x => $"supplier-bill:{x:D}"))
            .ToArray();

        if (unpostedSourceReferences.Length > 0)
        {
            issues.Add(new ReportingPeriodBlockingIssueDto(
                ReportingPeriodBlockingIssueCodes.UnpostedSourceDocuments,
                "One or more source documents have not been posted to the ledger for the fiscal period.",
                unpostedSourceReferences.Length,
                unpostedSourceReferences.Take(5).ToArray(),
                RecordLinks: unpostedCustomerInvoices.Select(x => $"/finance/invoices?invoiceId={x:D}")
                    .Concat(unpostedSupplierBills.Select(x => $"/finance/bills?billId={x:D}"))
                    .Take(5).ToArray(),
                Remediation: "Post, correct, or explicitly reverse each source document before closing the period."));
        }

        var unbalancedEntries = await _dbContext.LedgerEntries
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.FiscalPeriodId == fiscalPeriodId)
            .Select(x => new
            {
                x.EntryNumber,
                DebitTotal = x.Lines.Sum(line => (decimal?)line.DebitAmount) ?? 0m,
                CreditTotal = x.Lines.Sum(line => (decimal?)line.CreditAmount) ?? 0m
            })
            .Where(x => x.DebitTotal != x.CreditTotal)
            .OrderBy(x => x.EntryNumber)
            .ToListAsync(cancellationToken);

        if (unbalancedEntries.Count > 0)
        {
            issues.Add(new ReportingPeriodBlockingIssueDto(
                ReportingPeriodBlockingIssueCodes.UnbalancedJournalEntries,
                "One or more journal entries are not balanced for the fiscal period.",
                unbalancedEntries.Count,
                unbalancedEntries.Select(x => x.EntryNumber).Take(5).ToArray()));
        }

        var referencedAccounts = await _dbContext.LedgerEntryLines
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.LedgerEntry.FiscalPeriodId == fiscalPeriodId)
            .Select(x => new ReferencedAccountRow(
                x.FinanceAccountId,
                x.FinanceAccount.Code,
                x.FinanceAccount.Name,
                x.FinanceAccount.AccountType))
            .Distinct()
            .ToListAsync(cancellationToken);

        if (referencedAccounts.Count > 0)
        {
            var accountIds = referencedAccounts.Select(x => x.AccountId).ToArray();
            var mappings = await _dbContext.FinancialStatementMappings
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.IsActive && accountIds.Contains(x.FinanceAccountId))
                .Select(x => new MappingRequirementRow(x.FinanceAccountId, x.StatementType))
                .ToListAsync(cancellationToken);

            var mappingLookup = mappings
                .GroupBy(x => x.AccountId)
                .ToDictionary(x => x.Key, x => x.Select(y => y.StatementType).ToHashSet());

            var missingMappings = referencedAccounts
                .Where(account =>
                {
                    var requiredStatementType = ResolveRequiredStatementType(account.AccountType);
                    if (!mappingLookup.TryGetValue(account.AccountId, out var statementTypes))
                    {
                        return true;
                    }

                    return requiredStatementType is null
                        ? statementTypes.Count == 0
                        : !statementTypes.Contains(requiredStatementType.Value);
                })
                .OrderBy(x => x.AccountCode, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (missingMappings.Count > 0)
            {
                issues.Add(new ReportingPeriodBlockingIssueDto(
                    ReportingPeriodBlockingIssueCodes.MissingStatementMappings,
                    "One or more accounts used in the fiscal period are missing required financial statement mappings.",
                    missingMappings.Count,
                    missingMappings.Select(x => x.AccountCode).Take(5).ToArray()));
            }
        }

        var period = await _dbContext.FiscalPeriods.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.CompanyId == companyId && x.Id == fiscalPeriodId, cancellationToken);
        var suspense = await _dbContext.BankReconciliationFollowUps.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.Status == BankReconciliationFollowUpStatuses.Open &&
                        x.LedgerEntry.FiscalPeriodId == fiscalPeriodId)
            .Select(x => new { x.Id, x.Reason, x.BankTransaction.Amount, x.BankTransaction.Currency })
            .ToListAsync(cancellationToken);
        if (suspense.Count > 0)
        {
            issues.Add(new ReportingPeriodBlockingIssueDto(
                ReportingPeriodBlockingIssueCodes.UnresolvedSuspense,
                "Suspense postings still need an accounting classification before this period can close.",
                suspense.Count,
                suspense.Select(x => x.Id.ToString("D")).Take(5).ToArray(),
                suspense.Sum(x => Math.Abs(x.Amount)),
                ResolveCurrency(suspense.Select(x => x.Currency)),
                suspense.Select(x => $"/finance/accounting/reconciliation?followUpId={x.Id:D}").Take(5).ToArray(),
                "Open bank reconciliation and reclassify or resolve each suspense item.",
                new Dictionary<string, string> { ["period"] = period.Name }));
        }

        var conflicts = await _dbContext.BankTransactionPostingStateRecords.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId && x.PostingState == BankTransactionPostingStates.Conflict &&
                        x.BankTransaction.BookingDate >= period.StartUtc && x.BankTransaction.BookingDate < period.EndUtc)
            .Select(x => new { x.BankTransactionId, x.ConflictCode, x.BankTransaction.Amount, x.BankTransaction.Currency })
            .ToListAsync(cancellationToken);
        if (conflicts.Count > 0)
        {
            issues.Add(new ReportingPeriodBlockingIssueDto(
                ReportingPeriodBlockingIssueCodes.ReconciliationConflicts,
                "Bank reconciliation conflicts must be resolved before this period can close.",
                conflicts.Count,
                conflicts.Select(x => x.BankTransactionId.ToString("D")).Take(5).ToArray(),
                conflicts.Sum(x => Math.Abs(x.Amount)), ResolveCurrency(conflicts.Select(x => x.Currency)),
                conflicts.Select(x => $"/finance/accounting/reconciliation?transactionId={x.BankTransactionId:D}").Take(5).ToArray(),
                "Review the conflicting bank transactions and correct their payment allocations."));
        }

        var control = await _accountingReportingService.GetControlAccountReconciliationAsync(
            new GetControlAccountReconciliationQuery(companyId, fiscalPeriodId), cancellationToken);
        var differences = control.Accounts.Where(x => !x.IsReconciled).ToArray();
        if (differences.Length > 0)
        {
            issues.Add(new ReportingPeriodBlockingIssueDto(
                ReportingPeriodBlockingIssueCodes.ControlAccountDifference,
                "One or more control accounts do not agree with their posted source journals.",
                differences.Length,
                differences.Select(x => x.AccountCode).Take(5).ToArray(),
                differences.Sum(x => Math.Abs(x.Difference)), ResolveCurrency(differences.Select(x => x.Currency)),
                differences.Select(x => $"/finance/accounting/reports?periodId={fiscalPeriodId:D}&accountId={x.AccountId:D}").Take(5).ToArray(),
                "Open the account drill-down and correct unauthorized manual or unmatched source postings."));
        }

        var configuration = await _dbContext.AccountingConfigurations.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId, cancellationToken);
        var hasVatReturnPolicy = configuration is not null &&
            _packResolver.TryResolve(configuration.PolicyPackKey, configuration.PolicyPackVersion, out var configuredPack) &&
            configuredPack!.Definition.SupportedCapabilities.Contains(
                AccountingPolicyCapabilityKeys.CountrySpecificReporting, StringComparer.OrdinalIgnoreCase);
        if (hasVatReturnPolicy)
        {
            var filingPeriods = await _dbContext.VatFilingPeriods.IgnoreQueryFilters().AsNoTracking()
                .Where(x => x.CompanyId == companyId && x.FiscalPeriodId == fiscalPeriodId)
                .OrderBy(x => x.StartDate).ToListAsync(cancellationToken);
            if (filingPeriods.Count == 0)
            {
                issues.Add(new ReportingPeriodBlockingIssueDto(
                    ReportingPeriodBlockingIssueCodes.VatReturnMissing,
                    "No VAT filing period is configured for this Swedish fiscal period.", 1,
                    [fiscalPeriodId.ToString("D")], RecordLinks: [$"/finance/accounting/reports?periodId={fiscalPeriodId:D}&view=vat"],
                    Remediation: "Configure the applicable non-overlapping VAT filing period and calculate its return."));
            }
            foreach (var filing in filingPeriods)
            {
                var latestId = await _dbContext.VatReturns.IgnoreQueryFilters().AsNoTracking()
                    .Where(x => x.CompanyId == companyId && x.FilingPeriodId == filing.Id)
                    .OrderByDescending(x => x.Version).Select(x => (Guid?)x.Id).FirstOrDefaultAsync(cancellationToken);
                if (!latestId.HasValue)
                {
                    issues.Add(new ReportingPeriodBlockingIssueDto(
                        ReportingPeriodBlockingIssueCodes.VatReturnMissing,
                        $"VAT filing period {filing.PeriodCode} has no calculated return.", 1,
                        [filing.Id.ToString("D")], RecordLinks: [$"/finance/accounting/reports?periodId={fiscalPeriodId:D}&view=vat"],
                        Remediation: "Calculate the VAT return from posted tax facts."));
                    continue;
                }
                var vat = await _vatReturnService.GetAsync(new GetVatReturnQuery(companyId, latestId.Value), cancellationToken);
                if (vat.IsStale)
                    issues.Add(new ReportingPeriodBlockingIssueDto(ReportingPeriodBlockingIssueCodes.VatReturnStale,
                        $"VAT return {filing.PeriodCode} is stale because posted source evidence changed.", 1,
                        [vat.Id.ToString("D")], vat.SettlementExact, vat.Currency,
                        [$"/finance/accounting/reports?periodId={fiscalPeriodId:D}&view=vat&vatReturnId={vat.Id:D}"],
                        "Recalculate the return and complete a new approval before closing."));
                else if (vat.Issues.Any(x => x.IsBlocking))
                    issues.Add(new ReportingPeriodBlockingIssueDto(ReportingPeriodBlockingIssueCodes.VatReturnBlocking,
                        $"VAT return {filing.PeriodCode} has blocking validation or reconciliation issues.",
                        vat.Issues.Count(x => x.IsBlocking), vat.Issues.Where(x => x.IsBlocking).Select(x => x.Code).Take(5).ToArray(),
                        vat.SettlementExact, vat.Currency,
                        [$"/finance/accounting/reports?periodId={fiscalPeriodId:D}&view=vat&vatReturnId={vat.Id:D}"],
                        "Resolve every source and control-account issue, then recalculate the return."));
                else if (vat.Status != VatReturnStatusValues.Locked)
                    issues.Add(new ReportingPeriodBlockingIssueDto(ReportingPeriodBlockingIssueCodes.VatReturnUnreviewed,
                        $"VAT return {filing.PeriodCode} has not been approved and finalized for its current evidence.",
                        1, [vat.Id.ToString("D")], vat.SettlementExact, vat.Currency,
                        [$"/finance/accounting/reports?periodId={fiscalPeriodId:D}&view=vat&vatReturnId={vat.Id:D}"],
                        "Complete separate finance approval and finalize the human-filing package."));
            }
        }
        else
        {
            var tax = await _accountingReportingService.GetTaxSummaryAsync(
                new GetAccountingTaxSummaryQuery(companyId, fiscalPeriodId), cancellationToken);
            if (tax.Lines.Count > 0 && !tax.IsReviewed)
            {
                issues.Add(new ReportingPeriodBlockingIssueDto(
                    ReportingPeriodBlockingIssueCodes.TaxReviewIncomplete,
                    "The configured tax summary has not been reviewed for this ledger version.",
                    1, [tax.Checksum], tax.Lines.Sum(x => Math.Abs(x.TaxAmount)),
                    ResolveCurrency(tax.Lines.Select(x => x.Currency)),
                    [$"/finance/accounting/reports?periodId={fiscalPeriodId:D}&view=tax"],
                    "Review the tax summary and confirm it before closing the period.",
                    new Dictionary<string, string> { ["taxSummaryChecksum"] = tax.Checksum }));
            }
        }

        var failedRegenerations = await _dbContext.BackgroundExecutions.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == companyId &&
                        x.ExecutionType == BackgroundExecutionType.FinanceReportRegeneration &&
                        x.RelatedEntityType == BackgroundExecutionRelatedEntityTypes.FiscalPeriod &&
                        x.RelatedEntityId == fiscalPeriodId.ToString("D") &&
                        (x.Status == BackgroundExecutionStatus.Failed ||
                         x.Status == BackgroundExecutionStatus.Escalated ||
                         x.Status == BackgroundExecutionStatus.Blocked))
            .OrderByDescending(x => x.UpdatedUtc)
            .Select(x => new { x.Id, x.FailureCode, x.FailureMessage })
            .ToListAsync(cancellationToken);
        if (failedRegenerations.Count > 0)
        {
            issues.Add(new ReportingPeriodBlockingIssueDto(
                ReportingPeriodBlockingIssueCodes.StoredReportsStale,
                "Stored financial reports could not be regenerated and are not safe to lock.",
                failedRegenerations.Count,
                failedRegenerations.Select(x => x.Id.ToString("D")).Take(5).ToArray(),
                RecordLinks: [$"/finance/accounting/reports?periodId={fiscalPeriodId:D}"],
                Remediation: "Retry report regeneration and resolve the visible failure before closing the period.",
                Evidence: failedRegenerations.Where(x => !string.IsNullOrWhiteSpace(x.FailureCode))
                    .Take(5).ToDictionary(x => x.Id.ToString("D"), x => x.FailureCode!)));
        }

        return issues;
    }

    private async Task<FiscalPeriod> LoadFiscalPeriodAsync(
        Guid companyId,
        Guid fiscalPeriodId,
        bool track,
        CancellationToken cancellationToken)
    {
        var query = _dbContext.FiscalPeriods
            .IgnoreQueryFilters()
            .Where(x => x.CompanyId == companyId && x.Id == fiscalPeriodId);

        if (!track)
        {
            query = query.AsNoTracking();
        }

        return await query.SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("Fiscal period was not found in the requested company.");
    }

    private static ReportingPeriodLockStateDto MapLockState(FiscalPeriod period) =>
        new(
            period.CompanyId,
            period.Id,
            period.Name,
            period.IsClosed,
            period.IsReportingLocked,
            period.ReportingLockedUtc,
            period.ReportingLockedByUserId,
            period.ReportingUnlockedUtc,
            period.ReportingUnlockedByUserId,
            period.LastCloseValidatedUtc,
            period.LastCloseValidatedByUserId,
            period.UpdatedUtc);

    private static FinancialStatementType? ResolveRequiredStatementType(string accountType)
    {
        if (string.IsNullOrWhiteSpace(accountType))
        {
            return null;
        }

        return accountType.Trim().ToLowerInvariant() switch
        {
            "asset" or "liability" or "equity" => FinancialStatementType.BalanceSheet,
            "revenue" or "expense" => FinancialStatementType.ProfitAndLoss,
            _ => null
        };
    }

    private sealed record ActorContext(
        string ActorType,
        Guid? ActorId,
        string? CorrelationId);

    private sealed record AccountSnapshotSeed(
        Guid AccountId,
        decimal OpeningBalance,
        string Currency);

    private sealed record ReferencedAccountRow(
        Guid AccountId,
        string AccountCode,
        string AccountName,
        string AccountType);

    private sealed record MappingRequirementRow(
        Guid AccountId,
        FinancialStatementType StatementType);

    private sealed record SnapshotMappingRow(
        Guid FinanceAccountId,
        string AccountCode,
        string AccountName,
        decimal OpeningBalance,
        string Currency,
        FinancialStatementReportSection ReportSection,
        FinancialStatementLineClassification LineClassification);

    private sealed record SnapshotPostingRow(
        Guid FinanceAccountId,
        decimal SignedAmount);

    private sealed record StatementSnapshotLineSeed(
        Guid? FinanceAccountId,
        string LineCode,
        string LineName,
        int LineOrder,
        FinancialStatementReportSection ReportSection,
        FinancialStatementLineClassification LineClassification,
        decimal Amount,
        string Currency);
}
