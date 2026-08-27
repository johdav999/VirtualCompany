using System.Data;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class CustomerCollectionWorkerOptions
{
    public const string SectionName = "CustomerCollectionWorker";
    public bool Enabled { get; set; } = true;
    public int PollIntervalMilliseconds { get; set; } = 60000;
    public int BatchSize { get; set; } = 100;
    public int LeaseSeconds { get; set; } = 120;
    public int MaximumAttempts { get; set; } = 5;
    public int BaseRetryDelaySeconds { get; set; } = 30;
    public int MaximumRetryDelaySeconds { get; set; } = 1800;
}

public sealed class CustomerCollectionWorkerRunner(
    VirtualCompanyDbContext db,
    ICustomerCollectionsService collections,
    IOptions<CustomerCollectionWorkerOptions> options,
    ILogger<CustomerCollectionWorkerRunner> logger,
    CustomerCollectionsTelemetry? telemetry = null) : ICustomerCollectionWorkerRunner
{
    public async Task<CustomerCollectionWorkerResult> RunAsync(RunCustomerCollectionWorkerCommand command, CancellationToken ct)
    {
        var batchSize = Math.Clamp(command.BatchSize <= 0 ? options.Value.BatchSize : command.BatchSize, 1, 200);
        var today = DateOnly.FromDateTime(command.AsOfUtc.Kind == DateTimeKind.Utc ? command.AsOfUtc : command.AsOfUtc.ToUniversalTime());
        if (command.ResetBlockedLease && command.CompanyId.HasValue) await ResetLeaseAsync(command.CompanyId.Value, ct);
        var policyQuery = db.CustomerCollectionPolicies.IgnoreQueryFilters().AsNoTracking().Include(x => x.Stages)
            .Where(x => x.Stages.Any());
        if (command.CompanyId.HasValue) policyQuery = policyQuery.Where(x => x.CompanyId == command.CompanyId.Value);
        var policies = await policyQuery.OrderBy(x => x.CompanyId).Take(100).ToListAsync(ct);
        var total = new Counts();
        foreach (var policy in policies)
        {
            ct.ThrowIfCancellationRequested();
            var leaseOwner = await ClaimAsync(policy.CompanyId, ct); if (leaseOwner is null) continue;
            try
            {
                var result = await ProcessCompanyAsync(policy, today, command.AsOfUtc, batchSize, ct); total.Add(result);
                await CompleteAsync(policy.CompanyId, leaseOwner, ct); telemetry?.Worker("succeeded");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                logger.LogError(ex, "Customer collection preparation failed for company {CompanyId}.", policy.CompanyId);
                await RetryOrBlockAsync(policy.CompanyId, leaseOwner, ex is CustomerCollectionException c ? c.ReasonCode : "customer_collection_worker_failed",
                    "The collection preparation cycle did not complete. Review the retained worker failure before retrying.", ct);
                telemetry?.Worker("retry_or_blocked");
            }
            finally { db.ChangeTracker.Clear(); }
        }
        return total.Result();
    }

    private async Task<CustomerCollectionWorkerResult> ProcessCompanyAsync(CustomerCollectionPolicy policy,
        DateOnly today, DateTime asOfUtc, int batchSize, CancellationToken ct)
    {
        var examined = 0; var casesCreated = 0; var draftsPrepared = 0; var tasksCreated = 0; var promisesBroken = 0;
        var actor = await db.CompanyMemberships.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == policy.CompanyId && x.Status == CompanyMembershipStatus.Active && x.UserId != null &&
                (x.Role == CompanyMembershipRole.Owner || x.Role == CompanyMembershipRole.Admin || x.Role == CompanyMembershipRole.FinanceApprover))
            .OrderBy(x => x.Role).Select(x => x.UserId).FirstOrDefaultAsync(ct);
        if (!actor.HasValue) return new(0, 0, 0, 0, 0);

        var brokenPromises = await db.CustomerCollectionCases.IgnoreQueryFilters()
            .Where(x => x.CompanyId == policy.CompanyId && x.PromiseStatus == "pending" && x.PromiseDueDate < today)
            .OrderBy(x => x.PromiseDueDate).Take(batchSize).ToListAsync(ct);
        foreach (var collectionCase in brokenPromises)
        {
            try
            {
                await collections.ResolvePromiseAsync(new(policy.CompanyId, collectionCase.Id, collectionCase.Version, false,
                    "The promised payment date passed without sufficient allocated payment evidence.", actor.Value,
                    $"collection-worker:{policy.CompanyId:N}:{today:yyyyMMdd}"), ct); promisesBroken++;
            }
            catch (CustomerCollectionException ex) when (ex.IsConflict) { logger.LogDebug("Promise case {CaseId} changed during worker evaluation.", collectionCase.Id); }
        }

        var currencies = await db.FinanceInvoices.IgnoreQueryFilters().AsNoTracking()
            .Where(x => x.CompanyId == policy.CompanyId && x.Amount > 0m && x.PostingStatus == FinanceDocumentPostingStatuses.Booked)
            .Select(x => x.Currency).Distinct().OrderBy(x => x).Take(20).ToArrayAsync(ct);
        foreach (var currency in currencies)
        {
            var aging = await collections.GetAgingAsync(new(policy.CompanyId, today, "UTC", null, currency, 0, batchSize), ct);
            foreach (var item in aging.Items.Where(x => x.DaysOverdue > 0))
            {
                ct.ThrowIfCancellationRequested(); examined++;
                if (item.IsDisputed || item.IsOnHold || item.PromiseStatus == "pending") continue;
                var dueStage = policy.Stages.Where(x => x.DaysAfterDue + policy.GracePeriodDays <= item.DaysOverdue)
                    .OrderByDescending(x => x.Stage).FirstOrDefault();
                if (dueStage is null || dueStage.Stage <= item.ReminderStage) continue;
                var existed = await db.CustomerCollectionCases.IgnoreQueryFilters().AsNoTracking().AnyAsync(x => x.CompanyId == policy.CompanyId && x.InvoiceId == item.InvoiceId, ct);
                var key = $"scheduled:{policy.CompanyId:N}:{item.InvoiceId:N}:{dueStage.Stage}:{item.OpenAmount:0.00}:{today:yyyyMMdd}";
                try
                {
                    var draft = await collections.PrepareReminderAsync(new(policy.CompanyId, item.InvoiceId, dueStage.Stage, null,
                        key, actor.Value, key), ct);
                    if (!draft.IsIdempotentReplay) draftsPrepared++;
                    if (!existed) casesCreated++;
                    var collectionCase = await db.CustomerCollectionCases.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == policy.CompanyId && x.InvoiceId == item.InvoiceId, ct);
                    if (!collectionCase.WorkTaskId.HasValue)
                    {
                        var task = new WorkTask(Guid.NewGuid(), policy.CompanyId, "customer_collection_follow_up",
                            $"Review overdue invoice {item.InvoiceNumber}",
                            $"A governed reminder draft is ready for {item.CustomerName}. Review current balance, approval, and customer context before sending.",
                            item.DaysOverdue > 60 ? WorkTaskPriority.High : WorkTaskPriority.Normal, null, null, "system", null,
                            new Dictionary<string, JsonNode?> { ["collectionCaseId"] = JsonValue.Create(collectionCase.Id), ["invoiceId"] = JsonValue.Create(item.InvoiceId), ["reminderDraftId"] = JsonValue.Create(draft.Id), ["sourceHash"] = JsonValue.Create(draft.SourceHash) },
                            correlationId: key, sourceType: "system", triggerSource: "customer_collection_worker",
                            creationReason: "A configured collection stage became due.", triggerEventId: key);
                        task.SetDueDate(asOfUtc.AddDays(1)); db.WorkTasks.Add(task); collectionCase.LinkTask(task.Id, DateTime.UtcNow);
                        await db.SaveChangesAsync(ct); tasksCreated++;
                    }
                }
                catch (CustomerCollectionException ex) when (ex.ReasonCode is CustomerCollectionReasonCodes.NoOpenBalance or
                    CustomerCollectionReasonCodes.CollectionOnHold or CustomerCollectionReasonCodes.DisputeOpen or
                    CustomerCollectionReasonCodes.InvoiceNotOverdue or CustomerCollectionReasonCodes.StaleEvidence)
                { logger.LogDebug("Collection worker skipped invoice {InvoiceId}: {ReasonCode}.", item.InvoiceId, ex.ReasonCode); }
                catch (DbUpdateException) { db.ChangeTracker.Clear(); }
            }
        }
        return new(examined, casesCreated, draftsPrepared, tasksCreated, promisesBroken);
    }

    private async Task<string?> ClaimAsync(Guid companyId, CancellationToken ct)
    {
        var now = DateTime.UtcNow; var owner = $"customer-collections:{Environment.MachineName}:{Guid.NewGuid():N}";
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        try
        {
            var lease = await db.CustomerCollectionWorkerLeases.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId, ct);
            if (lease is null) { lease = new(Guid.NewGuid(), companyId, now); db.CustomerCollectionWorkerLeases.Add(lease); }
            if (!lease.TryClaim(owner, now, TimeSpan.FromSeconds(Math.Clamp(options.Value.LeaseSeconds, 30, 900)))) { await transaction.RollbackAsync(ct); return null; }
            await db.SaveChangesAsync(ct); await transaction.CommitAsync(ct); return owner;
        }
        catch (DbUpdateException) { await transaction.RollbackAsync(ct); db.ChangeTracker.Clear(); return null; }
    }

    private async Task CompleteAsync(Guid companyId, string owner, CancellationToken ct)
    {
        var lease = await db.CustomerCollectionWorkerLeases.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == companyId, ct);
        lease.Complete(owner, DateTime.UtcNow); await db.SaveChangesAsync(ct);
    }

    private async Task RetryOrBlockAsync(Guid companyId, string owner, string code, string summary, CancellationToken ct)
    {
        db.ChangeTracker.Clear(); var lease = await db.CustomerCollectionWorkerLeases.IgnoreQueryFilters().SingleAsync(x => x.CompanyId == companyId, ct);
        var maximumAttempts = Math.Clamp(options.Value.MaximumAttempts, 1, 20); var blocked = lease.AttemptCount >= maximumAttempts;
        var exponent = Math.Max(0, lease.AttemptCount - 1); var delay = Math.Min(Math.Clamp(options.Value.MaximumRetryDelaySeconds, 1, 86400),
            Math.Clamp(options.Value.BaseRetryDelaySeconds, 1, 3600) * Math.Pow(2, exponent));
        lease.Retry(owner, code, summary, DateTime.UtcNow.AddSeconds(delay), blocked, DateTime.UtcNow); await db.SaveChangesAsync(ct);
    }

    private async Task ResetLeaseAsync(Guid companyId, CancellationToken ct)
    {
        var lease = await db.CustomerCollectionWorkerLeases.IgnoreQueryFilters().SingleOrDefaultAsync(x => x.CompanyId == companyId, ct);
        if (lease is null) return; lease.Reset(DateTime.UtcNow); await db.SaveChangesAsync(ct);
    }

    private sealed class Counts
    {
        private int _examined, _cases, _drafts, _tasks, _promises;
        public void Add(CustomerCollectionWorkerResult x) { _examined += x.Examined; _cases += x.CasesCreated; _drafts += x.DraftsPrepared; _tasks += x.TasksCreated; _promises += x.PromisesMarkedBroken; }
        public CustomerCollectionWorkerResult Result() => new(_examined, _cases, _drafts, _tasks, _promises);
    }
}

internal sealed class CustomerCollectionBackgroundService(
    IServiceScopeFactory scopes,
    IOptions<CustomerCollectionWorkerOptions> options,
    ILogger<CustomerCollectionBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var runner = scope.ServiceProvider.GetRequiredService<ICustomerCollectionWorkerRunner>();
                await runner.RunAsync(new(DateTime.UtcNow, Math.Clamp(options.Value.BatchSize, 1, 200)), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex, "The customer collection worker cycle failed."); }
            await Task.Delay(TimeSpan.FromMilliseconds(Math.Clamp(options.Value.PollIntervalMilliseconds, 10000, 3600000)), stoppingToken);
        }
    }
}
