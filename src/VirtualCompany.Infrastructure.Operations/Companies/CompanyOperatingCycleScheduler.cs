using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Orchestration;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Companies;

public sealed class CompanyOperatingCycleScheduler(IServiceScopeFactory scopes, TimeProvider clock,
    ILogger<CompanyOperatingCycleScheduler> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        await ScanAsync(stoppingToken);
        while (await timer.WaitForNextTickAsync(stoppingToken)) await ScanAsync(stoppingToken);
    }

    private async Task ScanAsync(CancellationToken ct)
    {
        try
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<VirtualCompanyDbContext>();
            var configs = await db.CompanyOperatingConfigurations.IgnoreQueryFilters().AsNoTracking()
                .Where(x => !x.IsPaused && !x.EmergencyStopped && x.CoordinatorAgentId != null).ToListAsync(ct);
            var events = scope.ServiceProvider.GetRequiredService<ICompanyOperatingEventService>();
            var pendingMarketingSignals = await db.MarketingCompanySignals.IgnoreQueryFilters()
                .Where(x => x.CycleEvaluationRequested && x.Status == "pending")
                .OrderBy(x => x.CreatedUtc).Take(20).ToListAsync(ct);
            foreach (var signal in pendingMarketingSignals)
            {
                var config = configs.SingleOrDefault(x => x.CompanyId == signal.CompanyId);
                if (config is null) continue;
                var key = $"marketing-signal:{signal.Id:N}";
                try
                {
                    await events.RecordAsync(signal.CompanyId, new RecordOperatingEventCommand(
                        "marketing_signal", "marketing_company_signal", signal.Id.ToString("N"), 1,
                        signal.CreatedUtc, "high", key, signal.CorrelationId,
                        Payload: new Dictionary<string, System.Text.Json.Nodes.JsonNode?>
                        {
                            ["signalType"] = System.Text.Json.Nodes.JsonValue.Create(signal.SignalType),
                            ["summary"] = System.Text.Json.Nodes.JsonValue.Create(signal.Summary)
                        }), ct);
                    signal.MarkEvaluated(); await db.SaveChangesAsync(ct);
                }
                catch (CompanyOperatingValidationException) { }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "Deferred company evaluation failed for Marketing signal {SignalId} in company {CompanyId}.",
                        signal.Id, signal.CompanyId);
                }
            }
            var configuredCompanies = configs.Select(x => x.CompanyId).ToHashSet();
            var since = clock.GetUtcNow().UtcDateTime.AddHours(-24);
            var workflowOutcomes = await db.WorkflowInstances.IgnoreQueryFilters().AsNoTracking()
                .Where(x => configuredCompanies.Contains(x.CompanyId) && x.UpdatedUtc >= since &&
                    (x.State == WorkflowInstanceStatus.Completed || x.State == WorkflowInstanceStatus.Failed ||
                     x.State == WorkflowInstanceStatus.Blocked || x.State == WorkflowInstanceStatus.Cancelled))
                .OrderByDescending(x => x.UpdatedUtc).Take(50)
                .Select(x => new { x.Id, x.CompanyId, x.State, x.CurrentStep, x.UpdatedUtc }).ToListAsync(ct);
            foreach (var workflow in workflowOutcomes)
            {
                var key = $"workflow-outcome:{workflow.Id:N}:{workflow.UpdatedUtc.Ticks}";
                try
                {
                    await events.RecordAsync(workflow.CompanyId, new RecordOperatingEventCommand(
                        "workflow_outcome", "workflow_instance", workflow.Id.ToString("N"), 1,
                        workflow.UpdatedUtc, workflow.State == WorkflowInstanceStatus.Completed ? "medium" : "high",
                        key, key, Payload: new Dictionary<string, System.Text.Json.Nodes.JsonNode?>
                        {
                            ["status"] = System.Text.Json.Nodes.JsonValue.Create(workflow.State.ToStorageValue()),
                            ["currentStep"] = System.Text.Json.Nodes.JsonValue.Create(workflow.CurrentStep)
                        }), ct);
                }
                catch (CompanyOperatingValidationException) { }
            }
            var approvalOutcomes = await db.ApprovalRequests.IgnoreQueryFilters().AsNoTracking()
                .Where(x => configuredCompanies.Contains(x.CompanyId) && x.UpdatedUtc >= since &&
                    (x.Status == ApprovalRequestStatus.Approved || x.Status == ApprovalRequestStatus.Rejected ||
                     x.Status == ApprovalRequestStatus.Expired || x.Status == ApprovalRequestStatus.Cancelled))
                .OrderByDescending(x => x.UpdatedUtc).Take(50)
                .Select(x => new { x.Id, x.CompanyId, x.Status, x.TargetEntityType, x.TargetEntityId, x.UpdatedUtc }).ToListAsync(ct);
            foreach (var approval in approvalOutcomes)
            {
                var key = $"approval-outcome:{approval.Id:N}:{approval.UpdatedUtc.Ticks}";
                try
                {
                    await events.RecordAsync(approval.CompanyId, new RecordOperatingEventCommand(
                        "approval_outcome", "approval_request", approval.Id.ToString("N"), 1,
                        approval.UpdatedUtc, approval.Status == ApprovalRequestStatus.Approved ? "medium" : "high",
                        key, key, Payload: new Dictionary<string, System.Text.Json.Nodes.JsonNode?>
                        {
                            ["status"] = System.Text.Json.Nodes.JsonValue.Create(approval.Status.ToStorageValue()),
                            ["targetType"] = System.Text.Json.Nodes.JsonValue.Create(approval.TargetEntityType),
                            ["targetId"] = System.Text.Json.Nodes.JsonValue.Create(approval.TargetEntityId)
                        }), ct);
                }
                catch (CompanyOperatingValidationException) { }
            }
            var executionFailures = await db.BackgroundExecutions.IgnoreQueryFilters().AsNoTracking()
                .Where(x => configuredCompanies.Contains(x.CompanyId) && x.UpdatedUtc >= since &&
                    (x.Status == BackgroundExecutionStatus.Failed || x.Status == BackgroundExecutionStatus.Blocked ||
                     x.Status == BackgroundExecutionStatus.Escalated))
                .OrderByDescending(x => x.UpdatedUtc).Take(50)
                .Select(x => new { x.Id, x.CompanyId, x.Status, x.ExecutionType, x.RelatedEntityType,
                    x.RelatedEntityId, x.FailureCode, x.UpdatedUtc }).ToListAsync(ct);
            foreach (var execution in executionFailures)
            {
                var key = $"background-outcome:{execution.Id:N}:{execution.UpdatedUtc.Ticks}";
                try
                {
                    await events.RecordAsync(execution.CompanyId, new RecordOperatingEventCommand(
                        "background_execution_outcome", "background_execution", execution.Id.ToString("N"), 1,
                        execution.UpdatedUtc, "high", key, key,
                        Payload: new Dictionary<string, System.Text.Json.Nodes.JsonNode?>
                        {
                            ["status"] = System.Text.Json.Nodes.JsonValue.Create(execution.Status.ToStorageValue()),
                            ["executionType"] = System.Text.Json.Nodes.JsonValue.Create(execution.ExecutionType.ToStorageValue()),
                            ["relatedEntityType"] = System.Text.Json.Nodes.JsonValue.Create(execution.RelatedEntityType),
                            ["relatedEntityId"] = System.Text.Json.Nodes.JsonValue.Create(execution.RelatedEntityId),
                            ["failureCode"] = System.Text.Json.Nodes.JsonValue.Create(execution.FailureCode)
                        }), ct);
                }
                catch (CompanyOperatingValidationException) { }
            }
            foreach (var config in configs)
            {
                TimeZoneInfo zone;
                try { zone = TimeZoneInfo.FindSystemTimeZoneById(config.Timezone); } catch { continue; }
                var local = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone);
                if (local.Hour != config.DailyCycleHour || local.Minute > 4) continue;
                var key = $"schedule:{config.CompanyId:N}:{local:yyyyMMdd}:{config.DailyCycleHour:00}";
                try
                {
                    await events.RequestAsync(config.CompanyId, "schedule", local.ToString("O"), key, key,
                        DateTime.UtcNow, null, ct);
                }
                catch (CompanyOperatingValidationException) { }
                catch (Exception ex) { logger.LogError(ex, "Scheduled company operation failed for company {CompanyId}.", config.CompanyId); }
            }
            await scope.ServiceProvider.GetRequiredService<IOperatingCycleRequestProcessor>().RunOnceAsync(10, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { logger.LogError(ex, "Company operating schedule scan failed."); }
    }
}
