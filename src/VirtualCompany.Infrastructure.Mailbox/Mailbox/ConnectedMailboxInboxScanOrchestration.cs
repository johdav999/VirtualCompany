using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Companies;
using VirtualCompany.Application.Mailbox;
using VirtualCompany.Application.Sales;
using VirtualCompany.Application.Support;
using VirtualCompany.Application.Workflows;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Security;

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
    private readonly IMailboxProviderRegistry? _providerRegistry;
    private readonly IFieldEncryptionService? _fieldEncryption;
    private readonly ISalesEmailIngestionService? _salesIngestion;
    private readonly ISupportMailboxIngestionService? _supportIngestion;
    private readonly ICoreCompanyAgentSeeder? _coreAgentSeeder;
    private readonly IDistributedLockProvider? _lockProvider;
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

    public CompanyConnectedMailboxInboxScanOrchestrator(
        VirtualCompanyDbContext dbContext,
        IManualInboxBillScanOrchestrator manualScanOrchestrator,
        IMailboxProviderRegistry providerRegistry,
        IFieldEncryptionService fieldEncryption,
        ISalesEmailIngestionService salesIngestion,
        ISupportMailboxIngestionService supportIngestion,
        ICoreCompanyAgentSeeder coreAgentSeeder,
        IDistributedLockProvider lockProvider,
        TimeProvider timeProvider,
        ILogger<CompanyConnectedMailboxInboxScanOrchestrator> logger)
        : this(dbContext, manualScanOrchestrator, timeProvider, logger)
    {
        _providerRegistry = providerRegistry;
        _fieldEncryption = fieldEncryption;
        _salesIngestion = salesIngestion;
        _supportIngestion = supportIngestion;
        _coreAgentSeeder = coreAgentSeeder;
        _lockProvider = lockProvider;
    }

    public async Task ExecuteConnectedMailboxScanAsync(
        ConnectedMailboxInboxScanJob job,
        CancellationToken cancellationToken)
    {
        await using var scanLease = _lockProvider is null
            ? null
            : await _lockProvider.TryAcquireAsync(
                BuildMailboxScanLockKey(job.CompanyId, job.MailboxConnectionId),
                TimeSpan.FromMinutes(5),
                cancellationToken);
        if (_lockProvider is not null && scanLease is null)
        {
            _logger.LogInformation(
                "Connected mailbox scan skipped because another worker owns the scan lease. CompanyId: {CompanyId}. ConnectionId: {ConnectionId}.",
                job.CompanyId,
                job.MailboxConnectionId);
            return;
        }

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

        if (connection.Purpose is MailboxPurpose.Sales or MailboxPurpose.Support)
        {
            await ExecuteBusinessMailboxScanAsync(connection, cancellationToken);
            return;
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var scanFromUtc = now.Subtract(MailboxConnectionDefaults.ManualScanWindow);
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

    private async Task ExecuteBusinessMailboxScanAsync(
        MailboxConnection connection,
        CancellationToken cancellationToken)
    {
        if (_providerRegistry is null || _fieldEncryption is null || _salesIngestion is null || _supportIngestion is null)
        {
            throw new InvalidOperationException("Business mailbox ingestion services are not configured.");
        }

        var now = _timeProvider.GetUtcNow().UtcDateTime;
        var accessToken = connection.Provider == MailboxProvider.StandardEmail
            ? StandardMailboxSessionCodec.Create(connection, _fieldEncryption)
            : _fieldEncryption.Decrypt(
                connection.CompanyId,
                MailboxConnectionDefaults.TokenPurpose(connection.Provider, "access_token"),
                connection.EncryptedAccessToken ?? throw new InvalidOperationException("Mailbox credentials are unavailable."));
        var provider = _providerRegistry.Resolve(connection.Provider);
        var messages = await provider.ListMessagesAsync(
            accessToken,
            new MailboxMessageQuery(
                now.Subtract(MailboxConnectionDefaults.ManualScanWindow),
                now,
                MailboxConnectionDefaults.NormalizeFolders(connection.ConfiguredFolders, connection.Provider)),
            cancellationToken);

        foreach (var summary in messages)
        {
            if (connection.Purpose == MailboxPurpose.Sales)
            {
                await _salesIngestion.ProcessMessageAsync(
                    new ProcessSalesEmailMessageCommand(
                        connection.CompanyId,
                        connection.UserId,
                        connection.Id,
                        summary.ProviderMessageId),
                    cancellationToken);
                continue;
            }

            var message = await provider.GetMessageAsync(
                accessToken,
                new MailboxMessageFetchRequest(summary.ProviderMessageId),
                cancellationToken);
            if (string.IsNullOrWhiteSpace(message.Sender.Email))
            {
                continue;
            }

            await _supportIngestion.IngestMessageAsync(
                connection.CompanyId,
                new SupportMailboxMessageInput(
                    connection.Id,
                    null,
                    message.Sender.Email,
                    message.Sender.DisplayName,
                    message.Recipients.FirstOrDefault()?.Email,
                    message.Subject ?? "Support request",
                    message.PlainTextBody ?? message.Subject ?? "Support request",
                    message.ProviderMessageId,
                    message.ProviderThreadId,
                    message.ReceivedUtc ?? now),
                cancellationToken);
        }

        if (connection.Provider == MailboxProvider.StandardEmail)
        {
            await AdvanceStandardMailboxCursorsAsync(connection, messages, now, cancellationToken);
        }

        connection.MarkScanSucceeded(now);
        await _dbContext.SaveChangesAsync(cancellationToken);
        _logger.LogInformation(
            "Connected mailbox scan completed. CompanyId: {CompanyId}. Purpose: {Purpose}. ConnectionId: {ConnectionId}. Messages: {MessageCount}.",
            connection.CompanyId,
            connection.Purpose,
            connection.Id,
            messages.Count);
    }

    private async Task AdvanceStandardMailboxCursorsAsync(
        MailboxConnection connection,
        IReadOnlyList<MailboxMessageSummary> messages,
        DateTime completedUtc,
        CancellationToken cancellationToken)
    {
        var checkpoints = messages
            .Where(message => !string.IsNullOrWhiteSpace(message.FolderId) &&
                StandardMailboxMessageReference.TryRead(message.ProviderMessageId, out _, out _))
            .Select(message =>
            {
                StandardMailboxMessageReference.TryRead(message.ProviderMessageId, out var uidValidity, out var uid);
                return new { FolderId = message.FolderId!, UidValidity = uidValidity, Uid = uid };
            })
            .GroupBy(item => new { item.FolderId, item.UidValidity })
            .Select(group => new { group.Key.FolderId, group.Key.UidValidity, LastUid = group.Max(item => item.Uid) })
            .ToArray();

        foreach (var checkpoint in checkpoints)
        {
            var cursor = await _dbContext.MailboxFolderSyncCursors.SingleOrDefaultAsync(
                item => item.CompanyId == connection.CompanyId &&
                    item.MailboxConnectionId == connection.Id &&
                    item.FolderId == checkpoint.FolderId,
                cancellationToken);
            if (cursor is null)
            {
                cursor = new MailboxFolderSyncCursor(
                    Guid.NewGuid(),
                    connection.CompanyId,
                    connection.Id,
                    checkpoint.FolderId,
                    completedUtc);
                _dbContext.MailboxFolderSyncCursors.Add(cursor);
            }
            else if (cursor.Status == MailboxCursorStatus.ReconciliationRequired)
            {
                cursor.ResetAfterReconciliation(checkpoint.UidValidity, completedUtc);
            }

            cursor.Advance(checkpoint.UidValidity, checkpoint.LastUid, null, completedUtc);
        }
    }

    private async Task<Agent> ResolveOrCreateLauraAsync(Guid companyId, CancellationToken cancellationToken)
    {
        var laura = await _dbContext.Agents
            .SingleOrDefaultAsync(
                x => x.CompanyId == companyId &&
                    x.TemplateId == CoreAgentTemplateIds.Finance,
                cancellationToken);
        if (laura is not null)
        {
            return laura;
        }

        if (_coreAgentSeeder is null)
        {
            throw new InvalidOperationException("Core company agent provisioning is not configured.");
        }

        await _coreAgentSeeder.SeedAsync(companyId, cancellationToken);
        return await _dbContext.Agents.SingleAsync(
            x => x.CompanyId == companyId && x.TemplateId == CoreAgentTemplateIds.Finance,
            cancellationToken);
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

    internal static string BuildMailboxScanLockKey(Guid companyId, Guid connectionId) =>
        $"mailbox-sync:{companyId:N}:{connectionId:N}";

    private static string FormatProvider(MailboxProvider provider) =>
        provider switch
        {
            MailboxProvider.Gmail => "Gmail",
            MailboxProvider.Microsoft365 => "Microsoft 365",
            _ => "connected"
        };
}
