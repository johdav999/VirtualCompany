using System.Data;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Finance;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingScheduleWorkerOptions
{
    public const string SectionName = "AccountingScheduleWorker";
    public bool Enabled { get; set; } = true;
    public int PollIntervalSeconds { get; set; } = 30;
    public int ClaimBatchSize { get; set; } = 20;
    public int LeaseSeconds { get; set; } = 120;
    public int MaximumAttempts { get; set; } = 5;
    public int BaseRetryDelaySeconds { get; set; } = 30;
    public int MaximumRetryDelaySeconds { get; set; } = 1800;
}

public sealed class AccountingScheduleTelemetry
{
    private readonly Meter _meter = new("VirtualCompany.Finance.AccountingSchedules", "1.0.0");
    private readonly Counter<long> _occurrences;
    private readonly Histogram<double> _scanDuration;
    public AccountingScheduleTelemetry()
    {
        _occurrences = _meter.CreateCounter<long>("accounting_schedule_occurrences_total");
        _scanDuration = _meter.CreateHistogram<double>("accounting_schedule_scan_duration_ms", "ms");
    }
    public void Record(string outcome, string? reason = null) => _occurrences.Add(1,
        new("outcome", outcome), new("reason", reason ?? "none"));
    public void RecordScan(int handled, TimeSpan elapsed)
    {
        TagList tags = default;
        tags.Add("handled", Math.Min(handled, 100));
        _scanDuration.Record(elapsed.TotalMilliseconds, tags);
    }
}

public sealed class AccountingScheduleGenerationRunner : IAccountingScheduleGenerationRunner
{
    private readonly VirtualCompanyDbContext _db;
    private readonly IAccountingPostingService _posting;
    private readonly ICompanyExecutionScopeFactory _tenantScopes;
    private readonly IAuditEventWriter _audit;
    private readonly IOptions<AccountingScheduleWorkerOptions> _options;
    private readonly AccountingScheduleTelemetry _telemetry;
    private readonly TimeProvider _clock;
    private readonly ILogger<AccountingScheduleGenerationRunner> _logger;

    public AccountingScheduleGenerationRunner(VirtualCompanyDbContext db, IAccountingPostingService posting,
        ICompanyExecutionScopeFactory tenantScopes, IAuditEventWriter audit,
        IOptions<AccountingScheduleWorkerOptions> options, AccountingScheduleTelemetry telemetry,
        TimeProvider clock, ILogger<AccountingScheduleGenerationRunner> logger)
    { _db = db; _posting = posting; _tenantScopes = tenantScopes; _audit = audit; _options = options; _telemetry = telemetry; _clock = clock; _logger = logger; }

    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var handled = 0;
        var now = Now(); var upperDate = DateOnly.FromDateTime(now).AddDays(1);
        var dueSchedules = await _db.AccountingSchedules.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Status == AccountingScheduleStatuses.Active && x.NextOccurrenceDate <= upperDate)
            .OrderBy(x => x.NextOccurrenceDate).ThenBy(x => x.CompanyId).ThenBy(x => x.Id)
            .Select(x => new { x.CompanyId, x.Id }).Take(BatchSize()).ToArrayAsync(cancellationToken);
        foreach (var candidate in dueSchedules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var claim = await ClaimOccurrenceAsync(candidate.CompanyId, candidate.Id, cancellationToken);
            if (claim is null) continue;
            handled++; using var scope = _tenantScopes.BeginScope(claim.CompanyId);
            try { await PostOccurrenceAsync(claim, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (AccountingPostingException exception) { await BlockAsync(claim, exception.ReasonCode, exception.Message, cancellationToken); }
            catch (AccountingScheduleException exception) { await BlockAsync(claim, exception.ReasonCode, exception.Message, cancellationToken); }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Accounting schedule generation failed for {OccurrenceId}.", claim.OccurrenceId);
                await RetryOrBlockAsync(claim, "accounting_schedule_generation_failed",
                    "The scheduled journal generation attempt did not complete.", cancellationToken);
            }
            finally { _db.ChangeTracker.Clear(); }
        }

        var reversals = await _db.AccountingScheduleOccurrences.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Status == AccountingScheduleOccurrenceStatuses.Posted && x.ReversalDueDate <= upperDate &&
                x.ReversalLedgerEntryId == null && x.ReversalRule != AccountingScheduleReversalRules.None)
            .OrderBy(x => x.ReversalDueDate).ThenBy(x => x.CompanyId).ThenBy(x => x.Id)
            .Select(x => new { x.CompanyId, x.Id }).Take(BatchSize()).ToArrayAsync(cancellationToken);
        foreach (var candidate in reversals)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var claim = await ClaimReversalAsync(candidate.CompanyId, candidate.Id, cancellationToken);
            if (claim is null) continue;
            handled++; using var scope = _tenantScopes.BeginScope(claim.CompanyId);
            try { await PostReversalAsync(claim, cancellationToken); }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Accounting schedule reversal failed for {OccurrenceId}.", claim.OccurrenceId);
                await BlockAsync(claim, exception is AccountingPostingException posting ? posting.ReasonCode :
                    "accounting_schedule_reversal_failed", "The automatic reversal could not be posted safely.", cancellationToken);
            }
            finally { _db.ChangeTracker.Clear(); }
        }
        _telemetry.RecordScan(handled, Stopwatch.GetElapsedTime(started));
        return handled;
    }

    private async Task<Claim?> ClaimOccurrenceAsync(Guid companyId, Guid scheduleId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var schedule = await WorkerScheduleQuery().SingleOrDefaultAsync(x => x.CompanyId == companyId &&
                x.Id == scheduleId && x.Status == AccountingScheduleStatuses.Active, cancellationToken);
            if (schedule?.CurrentVersion is null || schedule.NextOccurrenceDate > schedule.LocalDate(Now())) return null;
            if (schedule.EndDate.HasValue && schedule.NextOccurrenceDate > schedule.EndDate.Value) return null;
            var occurrence = await _db.AccountingScheduleOccurrences.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                x.CompanyId == companyId && x.ScheduleId == schedule.Id &&
                x.OccurrenceDate == schedule.NextOccurrenceDate, cancellationToken);
            if (occurrence is null)
            {
                var calculation = AccountingScheduleCalculator.Calculate(schedule, schedule.CurrentVersion,
                    schedule.NextOccurrenceDate);
                var reversalDue = await ResolveReversalDateAsync(schedule, schedule.NextOccurrenceDate, cancellationToken);
                occurrence = new(Guid.NewGuid(), companyId, schedule.Id, schedule.CurrentVersion.Id,
                    schedule.CurrentVersionNumber, schedule.CurrentVersionHash!, schedule.NextOccurrenceDate,
                    schedule.NextOccurrenceDate, calculation.DebitTotal, schedule.Currency,
                    schedule.ReversalRule, reversalDue, Now());
                _db.AccountingScheduleOccurrences.Add(occurrence);
            }
            var owner = $"accounting-schedule:{Environment.MachineName}:{Guid.NewGuid():N}";
            if (!occurrence.TryClaim(owner, Now(), TimeSpan.FromSeconds(LeaseSeconds()))) return null;
            await _db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
            _telemetry.Record("claimed"); return new(companyId, schedule.Id, occurrence.Id, owner, false);
        }
        catch (DbUpdateException) { _db.ChangeTracker.Clear(); return null; }
    }

    private async Task<Claim?> ClaimReversalAsync(Guid companyId, Guid occurrenceId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        try
        {
            var occurrence = await _db.AccountingScheduleOccurrences.IgnoreQueryFilters()
                .Include(x => x.Schedule).ThenInclude(x => x.ApprovalRequest)
                .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == occurrenceId, cancellationToken);
            if (occurrence?.ReversalDueDate is null || occurrence.ReversalDueDate > occurrence.Schedule.LocalDate(Now())) return null;
            var owner = $"accounting-schedule-reversal:{Environment.MachineName}:{Guid.NewGuid():N}";
            if (!occurrence.TryClaimReversal(owner, Now(), TimeSpan.FromSeconds(LeaseSeconds()))) return null;
            await _db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
            _telemetry.Record("reversal_claimed"); return new(companyId, occurrence.ScheduleId, occurrence.Id, owner, true);
        }
        catch (DbUpdateException) { _db.ChangeTracker.Clear(); return null; }
    }

    private async Task PostOccurrenceAsync(Claim claim, CancellationToken cancellationToken)
    {
        var occurrence = await _db.AccountingScheduleOccurrences.IgnoreQueryFilters().AsNoTracking()
            .SingleAsync(x => x.CompanyId == claim.CompanyId && x.Id == claim.OccurrenceId, cancellationToken);
        var schedule = await WorkerScheduleQuery().SingleAsync(x => x.CompanyId == claim.CompanyId && x.Id == claim.ScheduleId, cancellationToken);
        if (!occurrence.IsClaimedBy(claim.LeaseOwner, Now())) return;
        if (!AccountingScheduleService.HasCurrentApproval(schedule) || schedule.CurrentVersionId != occurrence.ScheduleVersionId ||
            schedule.CurrentVersionNumber != occurrence.ScheduleVersionNumber ||
            !string.Equals(schedule.CurrentVersionHash, occurrence.ScheduleVersionHash, StringComparison.OrdinalIgnoreCase))
            throw new AccountingScheduleException(AccountingScheduleReasonCodes.ApprovalStale,
                "The schedule or its approval changed before the due journal could be posted.");
        var calculation = AccountingScheduleCalculator.Calculate(schedule, schedule.CurrentVersion!, occurrence.OccurrenceDate);
        var period = await ResolvePeriodAsync(claim.CompanyId, occurrence.PostingDate, cancellationToken);
        var key = $"accounting-schedule:{claim.CompanyId:N}:{schedule.Id:N}:{occurrence.OccurrenceDate:yyyyMMdd}:{occurrence.ScheduleVersionHash}";
        var proposed = AccountingScheduleService.ToProposed(schedule, schedule.CurrentVersion!, occurrence.Id,
            period.Id, occurrence.PostingDate, calculation, Guid.Empty, true, schedule.ApprovalRequestId,
            key, AuditActorTypes.System);
        var preview = await _posting.PreviewAsync(new(proposed), cancellationToken);
        if (!preview.IsValid)
        {
            var issue = preview.Issues.First();
            throw new AccountingScheduleException(issue.ReasonCode, issue.Explanation);
        }
        var posted = await _posting.PostAsync(new(proposed, key), cancellationToken);
        var stored = await _db.AccountingScheduleOccurrences.IgnoreQueryFilters().SingleAsync(x =>
            x.CompanyId == claim.CompanyId && x.Id == claim.OccurrenceId, cancellationToken);
        var current = await _db.AccountingSchedules.IgnoreQueryFilters().SingleAsync(x =>
            x.CompanyId == claim.CompanyId && x.Id == claim.ScheduleId, cancellationToken);
        if (!stored.IsClaimedBy(claim.LeaseOwner, Now())) return;
        stored.MarkPosted(claim.LeaseOwner, posted.Journal.Id, Now());
        current.Advance(stored.OccurrenceDate, current.UpdatedByUserId, Now());
        await WriteWorkerAuditAsync(claim.CompanyId, stored.Id,
            AuditEventActions.AccountingScheduleOccurrencePosted,
            "Posted one approved accounting schedule occurrence through the native ledger boundary.", key, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken); _telemetry.Record("posted");
    }

    private async Task PostReversalAsync(Claim claim, CancellationToken cancellationToken)
    {
        var occurrence = await _db.AccountingScheduleOccurrences.IgnoreQueryFilters().Include(x => x.Schedule)
            .ThenInclude(x => x.ApprovalRequest).SingleAsync(x => x.CompanyId == claim.CompanyId &&
                x.Id == claim.OccurrenceId, cancellationToken);
        if (!occurrence.IsClaimedBy(claim.LeaseOwner, Now()) || !occurrence.LedgerEntryId.HasValue ||
            !occurrence.ReversalDueDate.HasValue) return;
        var approvalBinding = await _db.AccountingScheduleApprovalBindings.IgnoreQueryFilters().AsNoTracking()
            .Include(x => x.ApprovalRequest).Where(x => x.CompanyId == claim.CompanyId &&
                x.ScheduleId == occurrence.ScheduleId && x.ScheduleVersionId == occurrence.ScheduleVersionId &&
                x.VersionNumber == occurrence.ScheduleVersionNumber && x.PayloadHash == occurrence.ScheduleVersionHash &&
                x.ApprovalRequest.Status == VirtualCompany.Domain.Enums.ApprovalRequestStatus.Approved)
            .OrderByDescending(x => x.BoundUtc).FirstOrDefaultAsync(cancellationToken);
        if (approvalBinding is null)
            throw new AccountingScheduleException(AccountingScheduleReasonCodes.ApprovalStale,
                "The exact posted schedule version no longer has retained approval evidence for automatic reversal.");
        var existing = await _db.LedgerEntries.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.CompanyId == claim.CompanyId && x.OriginalLedgerEntryId == occurrence.LedgerEntryId &&
            x.PostingType == LedgerPostingTypeValues.Reversal).Select(x => (Guid?)x.Id)
            .SingleOrDefaultAsync(cancellationToken);
        var key = $"accounting-schedule-reversal:{claim.CompanyId:N}:{occurrence.Id:N}:{occurrence.ScheduleVersionHash}";
        Guid reversalId;
        if (existing.HasValue) reversalId = existing.Value;
        else
        {
            var period = await ResolvePeriodAsync(claim.CompanyId, occurrence.ReversalDueDate.Value, cancellationToken);
            var reversed = await _posting.ReverseAsync(new(claim.CompanyId, occurrence.LedgerEntryId.Value,
                period.Id, occurrence.Schedule.VoucherSeriesCode, occurrence.ReversalDueDate.Value,
                $"Automatic reversal for {occurrence.Schedule.Name}",
                occurrence.ScheduleVersionNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), key,
                Guid.Empty, approvalBinding.ApprovalRequestId, key, AuditActorTypes.System), cancellationToken);
            reversalId = reversed.Journal.Id;
        }
        occurrence.MarkReversed(claim.LeaseOwner, reversalId, Now());
        await WriteWorkerAuditAsync(claim.CompanyId, occurrence.Id,
            AuditEventActions.AccountingScheduleOccurrenceReversed,
            "Posted the occurrence's approved automatic reversal.", key, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken); _telemetry.Record("reversed");
    }

    private async Task RetryOrBlockAsync(Claim claim, string code, string summary,
        CancellationToken cancellationToken)
    {
        var occurrence = await _db.AccountingScheduleOccurrences.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == claim.CompanyId && x.Id == claim.OccurrenceId, cancellationToken);
        if (occurrence is null || !occurrence.IsClaimedBy(claim.LeaseOwner, Now())) return;
        if (occurrence.AttemptCount >= Math.Clamp(_options.Value.MaximumAttempts, 1, 20))
        { await BlockAsync(claim, code, summary, cancellationToken); return; }
        var delay = Math.Min(Math.Clamp(_options.Value.MaximumRetryDelaySeconds, 30, 86400),
            Math.Clamp(_options.Value.BaseRetryDelaySeconds, 1, 3600) * Math.Pow(2, Math.Max(0, occurrence.AttemptCount - 1)));
        occurrence.ReleaseForRetry(claim.LeaseOwner, code, summary, Now(), TimeSpan.FromSeconds(delay));
        await _db.SaveChangesAsync(cancellationToken); _telemetry.Record("retry_scheduled", code);
    }

    private async Task BlockAsync(Claim claim, string code, string summary,
        CancellationToken cancellationToken)
    {
        var occurrence = await _db.AccountingScheduleOccurrences.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == claim.CompanyId && x.Id == claim.OccurrenceId, cancellationToken);
        if (occurrence is null || !occurrence.IsClaimedBy(claim.LeaseOwner, Now())) return;
        occurrence.MarkBlocked(claim.LeaseOwner, code, Safe(summary), Now());
        if (!occurrence.Exceptions.Any(x => x.Status == "open" && x.ReasonCode == code))
            occurrence.Exceptions.Add(new AccountingScheduleOccurrenceException(Guid.NewGuid(), claim.CompanyId,
                occurrence.ScheduleId, occurrence.Id, code, Safe(summary), SafeNextAction(code), Now()));
        var schedule = await _db.AccountingSchedules.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
            x.CompanyId == claim.CompanyId && x.Id == occurrence.ScheduleId, cancellationToken);
        schedule?.PauseForException(Now());
        await WriteWorkerAuditAsync(claim.CompanyId, occurrence.Id,
            AuditEventActions.AccountingScheduleOccurrenceBlocked,
            "Blocked an accounting schedule occurrence and retained an actionable exception.", code, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken); _telemetry.Record("blocked", code);
    }

    private IQueryable<AccountingSchedule> WorkerScheduleQuery() => _db.AccountingSchedules.IgnoreQueryFilters()
        .Include(x => x.ApprovalRequest).Include(x => x.CurrentVersion).ThenInclude(x => x!.Lines)
        .ThenInclude(x => x.DimensionAssignments).Include(x => x.CurrentVersion).ThenInclude(x => x!.EvidenceLinks);
    private async Task<FiscalPeriod> ResolvePeriodAsync(Guid companyId, DateOnly date, CancellationToken cancellationToken)
    {
        var instant = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        return await _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == companyId && x.StartUtc <= instant && x.EndUtc > instant, cancellationToken)
            ?? throw new AccountingScheduleException(AccountingScheduleReasonCodes.PeriodUnavailable,
                $"No fiscal period contains {date:yyyy-MM-dd}.");
    }
    private async Task<DateOnly?> ResolveReversalDateAsync(AccountingSchedule schedule, DateOnly postingDate,
        CancellationToken cancellationToken)
    {
        if (schedule.ReversalRule == AccountingScheduleReversalRules.None) return null;
        if (schedule.ReversalRule == AccountingScheduleReversalRules.NextDay) return postingDate.AddDays(1);
        var instant = postingDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var next = await _db.FiscalPeriods.IgnoreQueryFilters().AsNoTracking().Where(x =>
            x.CompanyId == schedule.CompanyId && x.StartUtc > instant).OrderBy(x => x.StartUtc)
            .Select(x => (DateTime?)x.StartUtc).FirstOrDefaultAsync(cancellationToken);
        if (!next.HasValue) throw new AccountingScheduleException(AccountingScheduleReasonCodes.PeriodUnavailable,
            "Create the next fiscal period before activating a next-period reversal schedule.");
        return DateOnly.FromDateTime(next.Value);
    }
    private async Task WriteWorkerAuditAsync(Guid companyId, Guid occurrenceId, string action,
        string summary, string correlationId, CancellationToken cancellationToken) => await _audit.WriteAsync(new(
        companyId, AuditActorTypes.System, null, action, AuditTargetTypes.AccountingScheduleOccurrence,
        occurrenceId.ToString("D"), AuditEventOutcomes.Succeeded, summary, ["accounting_schedule"],
        CorrelationId: correlationId, OccurredUtc: Now()), cancellationToken);
    private int BatchSize() => Math.Clamp(_options.Value.ClaimBatchSize, 1, 100);
    private int LeaseSeconds() => Math.Clamp(_options.Value.LeaseSeconds, 30, 900);
    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;
    private static string Safe(string summary) => string.IsNullOrWhiteSpace(summary)
        ? "The scheduled accounting operation could not be completed safely." : summary.Trim()[..Math.Min(summary.Trim().Length, 1000)];
    private static string SafeNextAction(string code) => code.Contains("approval", StringComparison.OrdinalIgnoreCase)
        ? "Review and approve the current schedule version, then regenerate this occurrence."
        : code.Contains("period", StringComparison.OrdinalIgnoreCase)
            ? "Open or create the required fiscal period, then regenerate this occurrence."
            : "Review the account, dimension, period, and evidence checks, correct the schedule prospectively, then regenerate safely.";
    private sealed record Claim(Guid CompanyId, Guid ScheduleId, Guid OccurrenceId, string LeaseOwner, bool IsReversal);
}

public sealed class AccountingScheduleGenerationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<AccountingScheduleWorkerOptions> _options;
    private readonly ILogger<AccountingScheduleGenerationBackgroundService> _logger;
    public AccountingScheduleGenerationBackgroundService(IServiceScopeFactory scopes,
        IOptions<AccountingScheduleWorkerOptions> options,
        ILogger<AccountingScheduleGenerationBackgroundService> logger)
    { _scopes = scopes; _options = options; _logger = logger; }
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_options.Value.Enabled)
                {
                    using var scope = _scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<IAccountingScheduleGenerationRunner>()
                        .RunDueAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { _logger.LogError(exception, "Accounting schedule worker failed."); }
            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(_options.Value.PollIntervalSeconds, 5, 3600)), stoppingToken);
        }
    }
}
