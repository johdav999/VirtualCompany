using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Finance;
using VirtualCompany.Application.Tasks;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CustomerInvoiceScheduleGenerationWorkerOptions
{
    public const string SectionName = "CustomerInvoiceScheduleGenerationWorker";
    public bool Enabled { get; set; } = true;
    public int PollIntervalMilliseconds { get; set; } = 10000;
    public int ClaimBatchSize { get; set; } = 20;
    public int LeaseSeconds { get; set; } = 120;
    public int MaximumAttempts { get; set; } = 5;
    public int BaseRetryDelaySeconds { get; set; } = 30;
    public int MaximumRetryDelaySeconds { get; set; } = 1800;
}

public sealed class CustomerInvoiceScheduleGenerationRunner : ICustomerInvoiceScheduleGenerationRunner
{
    private readonly VirtualCompanyDbContext _db;
    private readonly ICustomerInvoiceDraftService _drafts;
    private readonly ICustomerInvoiceScheduleOccurrencePolicy _occurrencePolicy;
    private readonly ICompanyTaskCommandService _tasks;
    private readonly ICompanyExecutionScopeFactory _tenantScopes;
    private readonly IOptions<CustomerInvoiceScheduleGenerationWorkerOptions> _options;
    private readonly CustomerInvoiceScheduleTelemetry _telemetry;
    private readonly TimeProvider _clock;
    private readonly ILogger<CustomerInvoiceScheduleGenerationRunner> _logger;

    public CustomerInvoiceScheduleGenerationRunner(VirtualCompanyDbContext db,
        ICustomerInvoiceDraftService drafts, ICustomerInvoiceScheduleOccurrencePolicy occurrencePolicy,
        ICompanyTaskCommandService tasks, ICompanyExecutionScopeFactory tenantScopes,
        IOptions<CustomerInvoiceScheduleGenerationWorkerOptions> options,
        CustomerInvoiceScheduleTelemetry telemetry, TimeProvider clock,
        ILogger<CustomerInvoiceScheduleGenerationRunner> logger)
    {
        _db = db;
        _drafts = drafts;
        _occurrencePolicy = occurrencePolicy;
        _tasks = tasks;
        _tenantScopes = tenantScopes;
        _options = options;
        _telemetry = telemetry;
        _clock = clock;
        _logger = logger;
    }

    public async Task<int> RunDueAsync(CancellationToken cancellationToken)
    {
        var candidates = await _db.CustomerInvoiceSchedules.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.Status == CustomerInvoiceScheduleStatuses.Active)
            .OrderBy(x => x.NextOccurrenceDate).ThenBy(x => x.CompanyId).ThenBy(x => x.Id)
            .Select(x => new { x.CompanyId, x.Id })
            .Take(Math.Clamp(_options.Value.ClaimBatchSize, 1, 100))
            .ToArrayAsync(cancellationToken);
        var handled = 0;
        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var claim = await ClaimAsync(candidate.CompanyId, candidate.Id, cancellationToken);
            if (claim is null) continue;
            handled++;
            using var tenantScope = _tenantScopes.BeginScope(candidate.CompanyId);
            try
            {
                await GenerateAsync(claim, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (CustomerInvoiceDraftException exception)
            {
                await BlockAsync(claim.CompanyId, claim.OccurrenceId, claim.LeaseOwner,
                    exception.ReasonCode, exception.Message, cancellationToken);
            }
            catch (CustomerInvoiceScheduleException exception)
            {
                await BlockAsync(claim.CompanyId, claim.OccurrenceId, claim.LeaseOwner,
                    exception.ReasonCode, exception.Message, cancellationToken);
            }
            catch (Exception exception)
            {
                _logger.LogError(exception,
                    "Recurring invoice generation failed for schedule {ScheduleId}, occurrence {OccurrenceId}.",
                    claim.Schedule.Id, claim.OccurrenceId);
                await RetryOrBlockAsync(claim.CompanyId, claim.OccurrenceId, claim.LeaseOwner,
                    "customer_invoice_schedule_generation_failed",
                    "The recurring invoice generation attempt did not complete.", cancellationToken);
            }
            finally
            {
                _db.ChangeTracker.Clear();
            }
        }
        return handled;
    }

    private async Task<ClaimedOccurrence?> ClaimAsync(Guid companyId, Guid scheduleId,
        CancellationToken cancellationToken)
    {
        var now = Now();
        await using var transaction = await _db.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);
        try
        {
            var schedule = await _db.CustomerInvoiceSchedules.IgnoreQueryFilters()
                .Include(x => x.Lines).Include(x => x.EvidenceLinks).Include(x => x.ApprovalRequest)
                .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == scheduleId &&
                    x.Status == CustomerInvoiceScheduleStatuses.Active, cancellationToken);
            if (schedule is null) return null;
            var localDate = LocalDate(schedule, now);
            if (schedule.NextOccurrenceDate > localDate ||
                schedule.EndDate.HasValue && schedule.NextOccurrenceDate > schedule.EndDate.Value)
                return null;

            var occurrence = await _db.CustomerInvoiceScheduleOccurrences.IgnoreQueryFilters()
                .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.ScheduleId == schedule.Id &&
                    x.OccurrenceDate == schedule.NextOccurrenceDate, cancellationToken);
            if (occurrence is null)
            {
                occurrence = new CustomerInvoiceScheduleOccurrence(Guid.NewGuid(), companyId, schedule.Id,
                    schedule.NextOccurrenceDate, schedule.NextOccurrenceDate,
                    schedule.DueDateFor(schedule.NextOccurrenceDate), schedule.Version,
                    schedule.TemplateVersion, schedule.TemplateHash, now);
                _db.CustomerInvoiceScheduleOccurrences.Add(occurrence);
            }

            var owner = $"customer-invoice-schedule:{Environment.MachineName}:{Guid.NewGuid():N}";
            if (!occurrence.TryClaim(owner, now,
                TimeSpan.FromSeconds(Math.Clamp(_options.Value.LeaseSeconds, 30, 900))))
                return null;
            await _db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            _telemetry.RecordOccurrence("claimed");
            return new(companyId, schedule, occurrence.Id, occurrence.OccurrenceDate,
                occurrence.ScheduleVersion, occurrence.TemplateVersion, occurrence.TemplateHash, owner);
        }
        catch (DbUpdateException)
        {
            _db.ChangeTracker.Clear();
            return null;
        }
    }

    private async Task GenerateAsync(ClaimedOccurrence claim, CancellationToken cancellationToken)
    {
        var schedule = claim.Schedule;
        if (!HasCurrentApproval(schedule))
        {
            await BlockAsync(claim.CompanyId, claim.OccurrenceId, claim.LeaseOwner,
                CustomerInvoiceScheduleReasonCodes.ApprovalStale,
                "The recurring invoice template no longer has current approval.", cancellationToken);
            return;
        }

        var input = CustomerInvoiceScheduleService.BuildDraftInput(schedule, claim.OccurrenceDate);
        var decision = await _occurrencePolicy.EvaluateAsync(claim.CompanyId, input, cancellationToken);
        if (!decision.IsAllowed)
        {
            await BlockAsync(claim.CompanyId, claim.OccurrenceId, claim.LeaseOwner,
                decision.ReasonCode, decision.Explanation, cancellationToken);
            return;
        }

        var key = $"customer-invoice-schedule:{claim.CompanyId:N}:{schedule.Id:N}:" +
            $"{claim.OccurrenceDate:yyyyMMdd}:{claim.TemplateVersion}:{claim.TemplateHash}";
        var draft = await _drafts.CreateAsync(new(claim.CompanyId, input, key,
            schedule.UpdatedByUserId, key), cancellationToken);
        var readiness = await _drafts.GetReadinessAsync(new(claim.CompanyId, draft.Id, draft.Version),
            cancellationToken);
        var blocker = readiness.Blockers.FirstOrDefault(x => x.ReasonCode is not
            CustomerInvoiceDraftReasonCodes.ApprovalRequired and not CustomerInvoiceDraftReasonCodes.ApprovalPending and not
            CustomerInvoiceDraftReasonCodes.ApprovalRejected and not CustomerInvoiceDraftReasonCodes.ApprovalStale);
        if (blocker is not null)
        {
            await _drafts.DiscardAsync(new(claim.CompanyId, draft.Id, draft.Version,
                $"{key}:discard", schedule.UpdatedByUserId, key), cancellationToken);
            await BlockAsync(claim.CompanyId, claim.OccurrenceId, claim.LeaseOwner,
                blocker.ReasonCode, blocker.Explanation, cancellationToken, draft.Id);
            return;
        }

        var now = Now();
        var stored = await _db.CustomerInvoiceScheduleOccurrences.IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == claim.CompanyId && x.Id == claim.OccurrenceId,
                cancellationToken);
        if (stored.Status == CustomerInvoiceScheduleOccurrenceStatuses.Generated && stored.DraftId == draft.Id)
            return;
        var current = await _db.CustomerInvoiceSchedules.IgnoreQueryFilters()
            .SingleAsync(x => x.CompanyId == claim.CompanyId && x.Id == schedule.Id, cancellationToken);
        if (!stored.IsClaimedBy(claim.LeaseOwner, now)) return;
        if (current.Status != CustomerInvoiceScheduleStatuses.Active ||
            current.Version != claim.ScheduleVersion ||
            current.TemplateVersion != claim.TemplateVersion ||
            !string.Equals(current.TemplateHash, claim.TemplateHash, StringComparison.OrdinalIgnoreCase) ||
            current.NextOccurrenceDate != claim.OccurrenceDate)
        {
            await BlockAsync(claim.CompanyId, claim.OccurrenceId, claim.LeaseOwner,
                CustomerInvoiceScheduleReasonCodes.VersionConflict,
                "The schedule changed while its occurrence was being generated. Review the retained draft before continuing.",
                cancellationToken, draft.Id);
            return;
        }

        if (!stored.TryMarkGenerated(claim.LeaseOwner, draft.Id, now)) return;
        current.AdvanceAfterGeneration(claim.OccurrenceDate, current.UpdatedByUserId, now);
        try
        {
            await _db.SaveChangesAsync(cancellationToken);
            _telemetry.RecordOccurrence("generated");
        }
        catch (DbUpdateConcurrencyException)
        {
            _db.ChangeTracker.Clear();
            _telemetry.RecordOccurrence("lease_lost", CustomerInvoiceScheduleReasonCodes.VersionConflict);
        }
    }

    private async Task RetryOrBlockAsync(Guid companyId, Guid occurrenceId, string leaseOwner,
        string code, string summary, CancellationToken cancellationToken)
    {
        var occurrence = await _db.CustomerInvoiceScheduleOccurrences.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == occurrenceId,
                cancellationToken);
        if (occurrence is null || !occurrence.IsClaimedBy(leaseOwner, Now())) return;
        if (occurrence.AttemptCount >= Math.Clamp(_options.Value.MaximumAttempts, 1, 20))
        {
            await BlockAsync(companyId, occurrenceId, leaseOwner, code, summary, cancellationToken);
            return;
        }

        var exponent = Math.Max(0, occurrence.AttemptCount - 1);
        var delaySeconds = Math.Min(Math.Clamp(_options.Value.MaximumRetryDelaySeconds, 30, 86400),
            Math.Clamp(_options.Value.BaseRetryDelaySeconds, 1, 3600) * Math.Pow(2, exponent));
        if (occurrence.TryReleaseRetry(leaseOwner, code, summary, Now(),
            TimeSpan.FromSeconds(delaySeconds)))
        {
            await _db.SaveChangesAsync(cancellationToken);
            _telemetry.RecordOccurrence("retry_scheduled", code);
        }
    }

    private async Task BlockAsync(Guid companyId, Guid occurrenceId, string leaseOwner,
        string code, string summary, CancellationToken cancellationToken, Guid? draftId = null)
    {
        var occurrence = await _db.CustomerInvoiceScheduleOccurrences.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == occurrenceId,
                cancellationToken);
        var now = Now();
        if (occurrence is null || !occurrence.IsClaimedBy(leaseOwner, now)) return;
        Guid? taskId = occurrence.TaskId;
        if (!taskId.HasValue)
        {
            try
            {
                var task = await _tasks.CreateTaskAsync(companyId,
                    new("finance_recurring_invoice_blocker", "Recurring invoice needs attention",
                        summary, "high", now, null, new()
                        {
                            ["scheduleId"] = occurrence.ScheduleId.ToString("N"),
                            ["occurrenceId"] = occurrence.Id.ToString("N"),
                            ["reasonCode"] = code
                        }), cancellationToken);
                taskId = task.Id;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                _logger.LogWarning(exception,
                    "Could not create task for blocked recurring invoice occurrence {OccurrenceId}.",
                    occurrence.Id);
            }
        }

        if (!occurrence.TryMarkBlocked(leaseOwner, code, summary, now, taskId, draftId)) return;
        var schedule = await _db.CustomerInvoiceSchedules.IgnoreQueryFilters()
            .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.Id == occurrence.ScheduleId,
                cancellationToken);
        schedule?.PauseAfterBlockedOccurrence(now);
        await _db.SaveChangesAsync(cancellationToken);
        _telemetry.RecordOccurrence("blocked", code);
    }

    private static bool HasCurrentApproval(CustomerInvoiceSchedule schedule) =>
        schedule.ApprovalRequest is { Status: ApprovalRequestStatus.Approved } &&
        schedule.ApprovalRequest.TargetEntityType == ApprovalTargetEntityType.CustomerInvoiceSchedule.ToStorageValue() &&
        schedule.ApprovalRequest.TargetEntityId == schedule.Id &&
        schedule.ApprovalTemplateVersion == schedule.TemplateVersion &&
        string.Equals(schedule.ApprovalTemplateHash, schedule.TemplateHash, StringComparison.OrdinalIgnoreCase);

    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;
    private static DateOnly LocalDate(CustomerInvoiceSchedule schedule, DateTime utcNow) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utcNow,
            DateTimeKind.Utc), TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId)).Date);

    private sealed record ClaimedOccurrence(Guid CompanyId, CustomerInvoiceSchedule Schedule,
        Guid OccurrenceId, DateOnly OccurrenceDate, long ScheduleVersion, long TemplateVersion,
        string TemplateHash, string LeaseOwner);
}

public sealed class CustomerInvoiceScheduleGenerationBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopes;
    private readonly IOptions<CustomerInvoiceScheduleGenerationWorkerOptions> _options;
    private readonly ILogger<CustomerInvoiceScheduleGenerationBackgroundService> _logger;

    public CustomerInvoiceScheduleGenerationBackgroundService(IServiceScopeFactory scopes,
        IOptions<CustomerInvoiceScheduleGenerationWorkerOptions> options,
        ILogger<CustomerInvoiceScheduleGenerationBackgroundService> logger)
    {
        _scopes = scopes;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (_options.Value.Enabled)
                {
                    using var scope = _scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<ICustomerInvoiceScheduleGenerationRunner>()
                        .RunDueAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Recurring invoice schedule worker failed.");
            }
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Clamp(
                _options.Value.PollIntervalMilliseconds, 250, 60000)), stoppingToken);
        }
    }
}
