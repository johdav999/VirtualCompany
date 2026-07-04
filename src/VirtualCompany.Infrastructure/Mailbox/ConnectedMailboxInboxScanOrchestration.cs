using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Companies;
using VirtualCompany.Infrastructure.Persistence;

namespace VirtualCompany.Infrastructure.Mailbox;

public sealed class ScopedConnectedMailboxInboxScanJobScheduler : IConnectedMailboxInboxScanJobScheduler
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScopedConnectedMailboxInboxScanJobScheduler> _logger;

    public ScopedConnectedMailboxInboxScanJobScheduler(
        IServiceScopeFactory scopeFactory,
        ILogger<ScopedConnectedMailboxInboxScanJobScheduler> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public Task EnqueueConnectedMailboxScanAsync(ConnectedMailboxInboxScanJob job, CancellationToken cancellationToken)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var companyScopeFactory = scope.ServiceProvider.GetRequiredService<ICompanyExecutionScopeFactory>();
                using var companyScope = companyScopeFactory.BeginScope(job.CompanyId);
                var orchestrator = scope.ServiceProvider.GetRequiredService<IConnectedMailboxInboxScanOrchestrator>();
                await orchestrator.ExecuteConnectedMailboxScanAsync(job, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogError(
                    ex,
                    "Laura's automatic mailbox scan task failed before completion. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}.",
                    job.CompanyId,
                    job.MailboxConnectionId);
            }
        }, CancellationToken.None);

        return Task.CompletedTask;
    }
}

public sealed class CompanyConnectedMailboxInboxScanOrchestrator : IConnectedMailboxInboxScanOrchestrator
{
    private const string TaskType = "finance_mailbox_inbox_scan";
    private const string TriggerSource = "mailbox_connected";
    private const string CorrelationPrefix = "finance-mailbox-scan";

    private readonly VirtualCompanyDbContext _dbContext;
    private readonly IManualInboxBillScanOrchestrator _manualScanOrchestrator;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CompanyConnectedMailboxInboxScanOrchestrator> _logger;

    public CompanyConnectedMailboxInboxScanOrchestrator(
        VirtualCompanyDbContext dbContext,
        IManualInboxBillScanOrchestrator manualScanOrchestrator,
        TimeProvider timeProvider,
        ILogger<CompanyConnectedMailboxInboxScanOrchestrator> logger)
    {
        _dbContext = dbContext;
        _manualScanOrchestrator = manualScanOrchestrator;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task ExecuteConnectedMailboxScanAsync(
        ConnectedMailboxInboxScanJob job,
        CancellationToken cancellationToken)
    {
        var connection = await _dbContext.MailboxConnections
            .SingleAsync(
                x => x.CompanyId == job.CompanyId &&
                    x.UserId == job.UserId &&
                    x.Id == job.MailboxConnectionId,
                cancellationToken);

        if (connection.Status != MailboxConnectionStatus.Active)
        {
            _logger.LogInformation(
                "Laura skipped automatic mailbox scan because the mailbox is not active. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}. Status: {Status}.",
                job.CompanyId,
                job.MailboxConnectionId,
                connection.Status);
            return;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var scanFromUtc = now.Subtract(CompanyMailboxConnectionService.ManualScanWindow);
        var scanToUtc = now;
        var run = new EmailIngestionRun(
            Guid.NewGuid(),
            job.CompanyId,
            connection.Id,
            job.UserId,
            connection.Provider,
            now,
            scanFromUtc,
            scanToUtc);

        var laura = await ResolveOrCreateLauraAsync(job.CompanyId, cancellationToken);
        var task = new WorkTask(
            Guid.NewGuid(),
            job.CompanyId,
            TaskType,
            $"Scan {FormatProvider(connection.Provider)} inbox for supplier bills",
            $"Laura is checking {connection.EmailAddress} for supplier invoices and preparing any clean bills for review.",
            WorkTaskPriority.Normal,
            laura.Id,
            null,
            AuditActorTypes.Agent,
            laura.Id,
            BuildInputPayload(connection, run, scanFromUtc, scanToUtc),
            null,
            null,
            "Laura started scanning the connected mailbox for supplier invoices.",
            null,
            BuildCorrelationId(job.CompanyId, run.Id),
            WorkTaskSourceTypes.Agent,
            laura.Id,
            TriggerSource,
            "A mailbox was connected, so Laura started checking the inbox for supplier bills.",
            connection.Id.ToString("N"),
            WorkTaskStatus.InProgress);

        _dbContext.EmailIngestionRuns.Add(run);
        _dbContext.WorkTasks.Add(task);
        _dbContext.AuditEvents.Add(CreateAuditEvent(
            job.CompanyId,
            laura,
            task,
            run,
            AuditEventOutcomes.Started,
            "Laura started checking the connected mailbox for supplier invoices.",
            now));
        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Laura automatic mailbox scan task started. CompanyId: {CompanyId}. Provider: {Provider}. ConnectionId: {ConnectionId}. RunId: {RunId}. TaskId: {TaskId}.",
            job.CompanyId,
            connection.Provider,
            connection.Id,
            run.Id,
            task.Id);

        await _manualScanOrchestrator.ExecuteManualScanAsync(
            new ManualInboxBillScanJob(
                job.CompanyId,
                job.UserId,
                connection.Id,
                run.Id,
                scanFromUtc,
                scanToUtc,
                task.Id,
                laura.Id,
                TriggerSource),
            cancellationToken);

        _dbContext.ChangeTracker.Clear();
        var completedRun = await _dbContext.EmailIngestionRuns
            .SingleAsync(x => x.CompanyId == job.CompanyId && x.Id == run.Id, cancellationToken);
        var completedTask = await _dbContext.WorkTasks
            .SingleAsync(x => x.CompanyId == job.CompanyId && x.Id == task.Id, cancellationToken);

        var output = BuildOutputPayload(completedRun);
        var completedSuccessfully = string.IsNullOrWhiteSpace(completedRun.FailureDetails);
        var rationale = completedSuccessfully
            ? $"Laura scanned {completedRun.ScannedMessageCount} message(s) and found {completedRun.DetectedCandidateCount} supplier bill candidate(s)."
            : $"Laura could not finish the mailbox scan: {completedRun.FailureDetails}";
        completedTask.UpdateStatus(
            completedSuccessfully ? WorkTaskStatus.Completed : WorkTaskStatus.Failed,
            output,
            rationale);
        _dbContext.AuditEvents.Add(CreateAuditEvent(
            job.CompanyId,
            laura,
            completedTask,
            completedRun,
            completedSuccessfully ? AuditEventOutcomes.Succeeded : AuditEventOutcomes.Failed,
            rationale,
            _timeProvider.GetUtcNow().UtcDateTime));

        await _dbContext.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Laura automatic mailbox scan task completed. CompanyId: {CompanyId}. Provider: {Provider}. ConnectionId: {ConnectionId}. RunId: {RunId}. TaskId: {TaskId}. Status: {Status}. Scanned: {Scanned}. Candidates: {Candidates}.",
            job.CompanyId,
            connection.Provider,
            connection.Id,
            completedRun.Id,
            completedTask.Id,
            completedTask.Status,
            completedRun.ScannedMessageCount,
            completedRun.DetectedCandidateCount);
    }

    private async Task<Agent> ResolveOrCreateLauraAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var laura = await _dbContext.Agents
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId &&
                    x.TemplateId == LauraFinanceAgentSeedData.TemplateId,
                cancellationToken);
        if (laura is not null)
        {
            return laura;
        }

        laura = LauraFinanceAgentSeedData.CreateCompanyAgent(companyId);
        _dbContext.Agents.Add(laura);
        return laura;
    }

    private static Dictionary<string, JsonNode?> BuildInputPayload(
        MailboxConnection connection,
        EmailIngestionRun run,
        DateTime scanFromUtc,
        DateTime scanToUtc) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["mailboxConnectionId"] = JsonValue.Create(connection.Id),
            ["emailIngestionRunId"] = JsonValue.Create(run.Id),
            ["provider"] = JsonValue.Create(connection.Provider.ToStorageValue()),
            ["emailAddress"] = JsonValue.Create(connection.EmailAddress),
            ["scanFromUtc"] = JsonValue.Create(scanFromUtc),
            ["scanToUtc"] = JsonValue.Create(scanToUtc),
            ["trigger"] = JsonValue.Create("Mailbox connected")
        };

    private static Dictionary<string, JsonNode?> BuildOutputPayload(EmailIngestionRun run) =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["emailIngestionRunId"] = JsonValue.Create(run.Id),
            ["scannedMessageCount"] = JsonValue.Create(run.ScannedMessageCount),
            ["detectedCandidateCount"] = JsonValue.Create(run.DetectedCandidateCount),
            ["nonCandidateMessageCount"] = JsonValue.Create(run.NonCandidateMessageCount),
            ["candidateAttachmentSnapshotCount"] = JsonValue.Create(run.CandidateAttachmentSnapshotCount),
            ["deduplicatedAttachmentCount"] = JsonValue.Create(run.DeduplicatedAttachmentCount),
            ["failureDetails"] = string.IsNullOrWhiteSpace(run.FailureDetails) ? null : JsonValue.Create(run.FailureDetails)
        };

    private static AuditEvent CreateAuditEvent(
        Guid companyId,
        Agent laura,
        WorkTask task,
        EmailIngestionRun run,
        string outcome,
        string rationale,
        DateTime occurredUtc) =>
        new(
            Guid.NewGuid(),
            companyId,
            AuditActorTypes.Agent,
            laura.Id,
            AuditEventActions.AgentInitiatedTaskCreated,
            AuditTargetTypes.WorkTask,
            task.Id.ToString("D"),
            outcome,
            rationale,
            ["mailbox", "finance"],
            new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            {
                ["agentName"] = "Laura",
                ["agentRole"] = "Finance Manager",
                ["responsibilityDomain"] = "finance",
                ["taskId"] = task.Id.ToString("D"),
                ["emailIngestionRunId"] = run.Id.ToString("D"),
                ["scannedMessageCount"] = run.ScannedMessageCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["detectedCandidateCount"] = run.DetectedCandidateCount.ToString(System.Globalization.CultureInfo.InvariantCulture)
            },
            task.CorrelationId,
            occurredUtc,
            [new AuditDataSourceUsed("mailbox_connection", run.MailboxConnectionId.ToString("D"), "Connected mailbox")],
            agentName: "Laura",
            agentRole: "Finance Manager",
            responsibilityDomain: "finance");

    private static string BuildCorrelationId(Guid companyId, Guid runId) =>
        $"{CorrelationPrefix}:{companyId:N}:{runId:N}";

    private static string FormatProvider(MailboxProvider provider) =>
        provider switch
        {
            MailboxProvider.Gmail => "Gmail",
            MailboxProvider.Microsoft365 => "Microsoft 365",
            _ => "connected"
        };
}
