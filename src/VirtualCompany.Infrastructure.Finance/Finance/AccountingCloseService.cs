using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using VirtualCompany.Application.Approvals;
using VirtualCompany.Application.Auditing;
using VirtualCompany.Application.Auth;
using VirtualCompany.Application.Documents;
using VirtualCompany.Application.Finance;
using VirtualCompany.Domain.Entities;
using VirtualCompany.Domain.Enums;
using VirtualCompany.Infrastructure.Persistence;
using VirtualCompany.Infrastructure.Tenancy;

namespace VirtualCompany.Infrastructure.Finance;

public sealed class AccountingCloseService : IAccountingCloseService
{
    private const string SourceType = "accounting_close_task";
    private readonly VirtualCompanyDbContext _db;
    private readonly ICompanyMembershipContextResolver _memberships;
    private readonly IApprovalRequestService _approvals;
    private readonly IKnowledgeAccessPolicyEvaluator _knowledgeAccess;
    private readonly IAuditEventWriter _audit;
    private readonly AccountingCloseTelemetry _telemetry;
    private readonly TimeProvider _clock;

    public AccountingCloseService(VirtualCompanyDbContext db, ICompanyMembershipContextResolver memberships,
        IApprovalRequestService approvals, IKnowledgeAccessPolicyEvaluator knowledgeAccess,
        IAuditEventWriter audit, AccountingCloseTelemetry telemetry, TimeProvider clock)
    {
        _db = db; _memberships = memberships; _approvals = approvals; _knowledgeAccess = knowledgeAccess;
        _audit = audit; _telemetry = telemetry; _clock = clock;
    }

    public async Task<AccountingCloseTemplateDto> CreateTemplateAsync(
        CreateAccountingCloseTemplateCommand command, CancellationToken cancellationToken)
    {
        var member = await RequireManagerAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        ValidateIdempotency(command.IdempotencyKey);
        var hash = Hash(new { Action = "create_template", command.Template });
        var replay = await FindOperationAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
        if (replay is not null) return await ReplayTemplateAsync(replay, hash, cancellationToken);
        await ValidateTemplateInputAsync(command.CompanyId, command.Template, cancellationToken);
        var now = Now();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        var template = new AccountingCloseTemplate(Guid.NewGuid(), command.CompanyId, command.Template.Code,
            command.Template.Name, command.Template.Description, member.UserId, now);
        _db.AccountingCloseTemplates.Add(template);
        var versionNumber = template.ReserveNextVersion(member.UserId, now);
        var version = BuildVersion(template, versionNumber, command.Template, member.UserId, now);
        _db.AccountingCloseTemplateVersions.Add(version);
        _db.AccountingCloseTemplateHistory.Add(new(Guid.NewGuid(), command.CompanyId, template.Id, version.Id,
            "created", member.UserId, null, now));
        _db.AccountingCloseOperations.Add(new(Guid.NewGuid(), command.CompanyId, "create_template",
            command.IdempotencyKey, hash, template.Id, template.Version, now));
        await WriteAuditAsync(command.CompanyId, member.UserId, AuditEventActions.AccountingCloseTemplateCreated,
            AuditTargetTypes.AccountingCloseTemplate, template.Id, "Created a versioned close template draft.",
            command.CorrelationId, now, cancellationToken);
        await SaveAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        _telemetry.Template("created", "succeeded");
        return await GetTemplateAsync(new(command.CompanyId, template.Id), cancellationToken);
    }

    public async Task<AccountingCloseTemplateDto> CreateTemplateVersionAsync(
        CreateAccountingCloseTemplateVersionCommand command, CancellationToken cancellationToken)
    {
        var member = await RequireManagerAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        ValidateIdempotency(command.IdempotencyKey);
        var hash = Hash(new { Action = "version_template", command.TemplateId, command.ExpectedVersion, command.Template });
        var replay = await FindOperationAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
        if (replay is not null) return await ReplayTemplateAsync(replay, hash, cancellationToken);
        await ValidateTemplateInputAsync(command.CompanyId, command.Template, cancellationToken);
        var template = await LoadTemplateAsync(command.CompanyId, command.TemplateId, true, cancellationToken);
        EnsureVersion(template.Version, command.ExpectedVersion);
        if (!string.Equals(template.Code, command.Template.Code.Trim(), StringComparison.OrdinalIgnoreCase))
            throw Error(AccountingCloseReasonCodes.InvalidTemplate, "A template code cannot change between versions.");
        var now = Now(); var number = template.ReserveNextVersion(member.UserId, now);
        var version = BuildVersion(template, number, command.Template, member.UserId, now);
        _db.AccountingCloseTemplateVersions.Add(version);
        _db.AccountingCloseTemplateHistory.Add(new(Guid.NewGuid(), command.CompanyId, template.Id, version.Id,
            "versioned", member.UserId, null, now));
        _db.AccountingCloseOperations.Add(new(Guid.NewGuid(), command.CompanyId, "version_template",
            command.IdempotencyKey, hash, template.Id, template.Version, now));
        await WriteAuditAsync(command.CompanyId, member.UserId, AuditEventActions.AccountingCloseTemplateVersioned,
            AuditTargetTypes.AccountingCloseTemplate, template.Id,
            $"Created close template version {number}; active and historical closes were not changed.",
            command.CorrelationId, now, cancellationToken);
        await SaveAsync(cancellationToken); _telemetry.Template("versioned", "succeeded");
        return await GetTemplateAsync(new(command.CompanyId, template.Id), cancellationToken);
    }

    public async Task<AccountingCloseTemplateDto> CopyTemplateAsync(CopyAccountingCloseTemplateCommand command,
        CancellationToken cancellationToken)
    {
        var member = await RequireManagerAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        ValidateIdempotency(command.IdempotencyKey);
        var hash = Hash(new { Action = "copy_template", command.SourceTemplateId, command.SourceVersionId,
            command.NewCode, command.NewName });
        var replay = await FindOperationAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
        if (replay is not null) return await ReplayTemplateAsync(replay, hash, cancellationToken);
        var source = await LoadTemplateAsync(command.CompanyId, command.SourceTemplateId, false, cancellationToken);
        var sourceVersion = source.Versions.SingleOrDefault(x => x.Id == command.SourceVersionId)
            ?? throw Error(AccountingCloseReasonCodes.NotFound, "The source close template version was not found.");
        var input = ToInput(sourceVersion) with { Code = command.NewCode, Name = command.NewName };
        await ValidateTemplateInputAsync(command.CompanyId, input, cancellationToken);
        var now = Now();
        var template = new AccountingCloseTemplate(Guid.NewGuid(), command.CompanyId, command.NewCode,
            command.NewName, input.Description, member.UserId, now);
        _db.AccountingCloseTemplates.Add(template);
        var version = BuildVersion(template, template.ReserveNextVersion(member.UserId, now), input, member.UserId, now);
        _db.AccountingCloseTemplateVersions.Add(version);
        _db.AccountingCloseTemplateHistory.Add(new(Guid.NewGuid(), command.CompanyId, template.Id, version.Id,
            "copied", member.UserId, $"Copied from {source.Id:D} version {sourceVersion.VersionNumber}.", now));
        _db.AccountingCloseOperations.Add(new(Guid.NewGuid(), command.CompanyId, "copy_template",
            command.IdempotencyKey, hash, template.Id, template.Version, now));
        await WriteAuditAsync(command.CompanyId, member.UserId, AuditEventActions.AccountingCloseTemplateCreated,
            AuditTargetTypes.AccountingCloseTemplate, template.Id, "Copied a retained close template version into a new draft.",
            command.CorrelationId, now, cancellationToken);
        await SaveAsync(cancellationToken); _telemetry.Template("copied", "succeeded");
        return await GetTemplateAsync(new(command.CompanyId, template.Id), cancellationToken);
    }

    public async Task<AccountingCloseTemplateDto> ActivateTemplateAsync(
        ActivateAccountingCloseTemplateCommand command, CancellationToken cancellationToken)
    {
        var member = await RequireManagerAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        ValidateIdempotency(command.IdempotencyKey);
        var hash = Hash(new { Action = "activate_template", command.TemplateId, command.TemplateVersionId,
            command.ExpectedVersion });
        var replay = await FindOperationAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
        if (replay is not null) return await ReplayTemplateAsync(replay, hash, cancellationToken);
        var template = await LoadTemplateAsync(command.CompanyId, command.TemplateId, true, cancellationToken);
        EnsureVersion(template.Version, command.ExpectedVersion);
        var version = template.Versions.SingleOrDefault(x => x.Id == command.TemplateVersionId)
            ?? throw Error(AccountingCloseReasonCodes.NotFound, "The close template version was not found.");
        ValidateGraph(ToInput(version));
        var now = Now();
        foreach (var active in template.Versions.Where(x => x.Status == AccountingCloseTemplateVersionStatuses.Active && x.Id != version.Id))
            active.Supersede(now);
        version.Activate(member.UserId, now); template.Activate(version.Id, member.UserId, now);
        _db.AccountingCloseTemplateHistory.Add(new(Guid.NewGuid(), command.CompanyId, template.Id, version.Id,
            "activated", member.UserId, null, now));
        _db.AccountingCloseOperations.Add(new(Guid.NewGuid(), command.CompanyId, "activate_template",
            command.IdempotencyKey, hash, template.Id, template.Version, now));
        await WriteAuditAsync(command.CompanyId, member.UserId, AuditEventActions.AccountingCloseTemplateActivated,
            AuditTargetTypes.AccountingCloseTemplate, template.Id,
            $"Activated immutable close template version {version.VersionNumber}.", command.CorrelationId, now, cancellationToken);
        await SaveAsync(cancellationToken); _telemetry.Template("activated", "succeeded");
        return await GetTemplateAsync(new(command.CompanyId, template.Id), cancellationToken);
    }

    public async Task<AccountingCloseTemplateDto> RetireTemplateAsync(
        RetireAccountingCloseTemplateCommand command, CancellationToken cancellationToken)
    {
        var member = await RequireManagerAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        ValidateIdempotency(command.IdempotencyKey);
        if (string.IsNullOrWhiteSpace(command.Reason)) throw Error(AccountingCloseReasonCodes.InvalidState, "A retirement reason is required.");
        var hash = Hash(new { Action = "retire_template", command.TemplateId, command.TemplateVersionId,
            command.ExpectedVersion, command.Reason });
        var replay = await FindOperationAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
        if (replay is not null) return await ReplayTemplateAsync(replay, hash, cancellationToken);
        var template = await LoadTemplateAsync(command.CompanyId, command.TemplateId, true, cancellationToken);
        EnsureVersion(template.Version, command.ExpectedVersion); var now = Now();
        var version = command.TemplateVersionId.HasValue
            ? template.Versions.SingleOrDefault(x => x.Id == command.TemplateVersionId.Value)
                ?? throw Error(AccountingCloseReasonCodes.NotFound, "The close template version was not found.")
            : template.Versions.OrderByDescending(x => x.VersionNumber).First();
        version.Retire(now);
        if (!command.TemplateVersionId.HasValue || template.ActiveVersionId == version.Id) template.Retire(member.UserId, now);
        _db.AccountingCloseTemplateHistory.Add(new(Guid.NewGuid(), command.CompanyId, template.Id, version.Id,
            "retired", member.UserId, command.Reason, now));
        _db.AccountingCloseOperations.Add(new(Guid.NewGuid(), command.CompanyId, "retire_template",
            command.IdempotencyKey, hash, template.Id, template.Version, now));
        await WriteAuditAsync(command.CompanyId, member.UserId, AuditEventActions.AccountingCloseTemplateRetired,
            AuditTargetTypes.AccountingCloseTemplate, template.Id, "Retired a close template or version without changing retained instances.",
            command.CorrelationId, now, cancellationToken);
        await SaveAsync(cancellationToken); _telemetry.Template("retired", "succeeded");
        return await GetTemplateAsync(new(command.CompanyId, template.Id), cancellationToken);
    }

    public async Task<AccountingCloseTemplateDto> GetTemplateAsync(GetAccountingCloseTemplateQuery query,
        CancellationToken cancellationToken)
    {
        await RequireMemberAsync(query.CompanyId, null, cancellationToken);
        return MapTemplate(await LoadTemplateAsync(query.CompanyId, query.TemplateId, false, cancellationToken));
    }

    public async Task<AccountingCloseTemplatePreviewDto> PreviewTemplateAsync(
        PreviewAccountingCloseTemplateQuery query, CancellationToken cancellationToken)
    {
        await RequireMemberAsync(query.CompanyId, null, cancellationToken);
        var template = await LoadTemplateAsync(query.CompanyId, query.TemplateId, false, cancellationToken);
        var version = template.Versions.SingleOrDefault(x => x.Id == query.TemplateVersionId)
            ?? throw Error(AccountingCloseReasonCodes.NotFound, "The close template version was not found.");
        var input = ToInput(version); var order = TopologicalOrder(input);
        return new(MapTemplate(template), version.Id, version.TaskDefinitions.Count, version.Dependencies.Count,
            version.TaskDefinitions.Sum(x => x.EvidenceRequirements.Count), order, []);
    }

    public async Task<AccountingCloseTemplateListResult> ListTemplatesAsync(
        ListAccountingCloseTemplatesQuery query, CancellationToken cancellationToken)
    {
        await RequireMemberAsync(query.CompanyId, null, cancellationToken);
        var skip = Math.Max(0, query.Skip); var take = Math.Clamp(query.Take, 1, 250);
        var source = _db.AccountingCloseTemplates.AsNoTracking().Where(x => x.CompanyId == query.CompanyId);
        if (!string.IsNullOrWhiteSpace(query.Status)) source = source.Where(x => x.Status == query.Status.Trim().ToLowerInvariant());
        var total = await source.CountAsync(cancellationToken);
        var ids = await source.OrderBy(x => x.Name).Skip(skip).Take(take).Select(x => x.Id).ToListAsync(cancellationToken);
        var items = new List<AccountingCloseTemplateDto>(ids.Count);
        foreach (var id in ids) items.Add(MapTemplate(await LoadTemplateAsync(query.CompanyId, id, false, cancellationToken)));
        return new(items, total, skip, take);
    }

    public async Task<AccountingCloseDto> StartAsync(StartAccountingCloseCommand command,
        CancellationToken cancellationToken)
    {
        var member = await RequireManagerAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        ValidateIdempotency(command.IdempotencyKey);
        var hash = Hash(new { Action = "start_close", command.FiscalPeriodId, command.TemplateId, command.TemplateVersionId });
        var replay = await FindOperationAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
        if (replay is not null) return await ReplayCloseAsync(replay, hash, cancellationToken);
        var period = await _db.FiscalPeriods.SingleOrDefaultAsync(x => x.CompanyId == command.CompanyId && x.Id == command.FiscalPeriodId, cancellationToken)
            ?? throw Error(AccountingCloseReasonCodes.NotFound, "The fiscal period was not found.");
        var template = await LoadTemplateAsync(command.CompanyId, command.TemplateId, false, cancellationToken);
        var versionId = command.TemplateVersionId ?? template.ActiveVersionId
            ?? throw Error(AccountingCloseReasonCodes.InvalidState, "Activate a close template version before starting a close.");
        var version = template.Versions.SingleOrDefault(x => x.Id == versionId)
            ?? throw Error(AccountingCloseReasonCodes.NotFound, "The close template version was not found.");
        if (template.ActiveVersionId != version.Id || version.Status != AccountingCloseTemplateVersionStatuses.Active)
            throw Error(AccountingCloseReasonCodes.InvalidState, "Only the active close template version can start a new close.");
        ValidateGraph(ToInput(version)); var now = Now();
        await using var transaction = await _db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken);
        var existing = await _db.AccountingCloseInstances.AsNoTracking().SingleOrDefaultAsync(x =>
            x.CompanyId == command.CompanyId && x.FiscalPeriodId == period.Id && x.TemplateVersionId == version.Id, cancellationToken);
        if (existing is not null)
        {
            _db.AccountingCloseOperations.Add(new(Guid.NewGuid(), command.CompanyId, "start_close",
                command.IdempotencyKey, hash, existing.Id, existing.Version, now));
            await SaveAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return await GetAsync(new(command.CompanyId, existing.Id), cancellationToken);
        }
        var instance = new AccountingCloseInstance(Guid.NewGuid(), command.CompanyId, period.Id, template.Id,
            version.Id, version.VersionNumber, $"{version.Name} · {period.Name}", member.UserId,
            command.IdempotencyKey, now);
        _db.AccountingCloseInstances.Add(instance);
        var generated = new Dictionary<Guid, AccountingCloseTask>();
        foreach (var definition in version.TaskDefinitions.OrderBy(x => x.Sequence))
        {
            var ownerUserId = await ResolveDefaultOwnerAsync(command.CompanyId, definition, member.UserId, cancellationToken);
            var workTask = new WorkTask(Guid.NewGuid(), command.CompanyId, SourceType, definition.Title,
                definition.Description, WorkTaskPriority.Normal, null, null, AuditActorTypes.User, member.UserId,
                new Dictionary<string, JsonNode?>
                {
                    ["closeInstanceId"] = JsonValue.Create(instance.Id.ToString("D")),
                    ["templateVersionId"] = JsonValue.Create(version.Id.ToString("D")),
                    ["taskDefinitionKey"] = JsonValue.Create(definition.Key),
                    ["ownerUserId"] = JsonValue.Create(ownerUserId?.ToString("D")),
                    ["ownerRole"] = JsonValue.Create(definition.DefaultOwnerRole)
                }, sourceType: WorkTaskSourceTypes.User, triggerSource: SourceType,
                creationReason: "Generated from an immutable accounting close template version.",
                triggerEventId: $"{instance.Id:N}:{definition.Key}");
            var due = period.EndUtc.AddDays(definition.DueOffsetDays);
            workTask.SetDueDate(due); _db.WorkTasks.Add(workTask);
            var task = new AccountingCloseTask(Guid.NewGuid(), command.CompanyId, instance.Id, definition.Id,
                definition.SectionId, definition.Key, definition.Title, definition.Description, definition.Sequence,
                due, ownerUserId, definition.DefaultOwnerRole, definition.RequiresSignOff, definition.SignOffRole,
                definition.MaterialityAmount ?? version.MaterialityAmount, workTask.Id, now);
            _db.AccountingCloseTasks.Add(task); generated[definition.Id] = task;
        }
        foreach (var dependency in version.Dependencies)
            _db.AccountingCloseTaskDependencies.Add(new(Guid.NewGuid(), command.CompanyId, instance.Id,
                generated[dependency.PredecessorTaskDefinitionId].Id, generated[dependency.DependentTaskDefinitionId].Id));
        _db.AccountingCloseStatusHistory.Add(new(Guid.NewGuid(), command.CompanyId, instance.Id, null,
            "started", null, AccountingCloseInstanceStatuses.Active, member.UserId, null, now));
        _db.AccountingCloseOperations.Add(new(Guid.NewGuid(), command.CompanyId, "start_close",
            command.IdempotencyKey, hash, instance.Id, instance.Version, now));
        await SaveAsync(cancellationToken);
        foreach (var pair in version.TaskDefinitions.Where(x => x.RequiresSignOff)
                     .Select(x => (Definition: x, Task: generated[x.Id])))
        {
            var approval = await CreateTaskApprovalAsync(command.CompanyId, instance, pair.Task,
                member.UserId, null, cancellationToken);
            pair.Task.BindApproval(approval.Id, now);
        }
        await WriteAuditAsync(command.CompanyId, member.UserId, AuditEventActions.AccountingCloseStarted,
            AuditTargetTypes.AccountingCloseInstance, instance.Id,
            $"Started one close work plan with {generated.Count} generated tasks from template version {version.VersionNumber}.",
            command.CorrelationId, now, cancellationToken);
        await SaveAsync(cancellationToken); await transaction.CommitAsync(cancellationToken);
        _telemetry.Close("started", "succeeded", generated.Count);
        return await GetAsync(new(command.CompanyId, instance.Id), cancellationToken);
    }

    public async Task<AccountingCloseDto> AssignTaskAsync(AssignAccountingCloseTaskCommand command,
        CancellationToken cancellationToken)
    {
        var member = await RequireManagerAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        ValidateIdempotency(command.IdempotencyKey);
        var hash = Hash(new { Action = "assign_task", command.CloseInstanceId, command.CloseTaskId,
            command.ExpectedVersion, command.OwnerUserId });
        var replay = await ReplayTaskOperationAsync(command.CompanyId, command.IdempotencyKey, hash, cancellationToken);
        if (replay is not null) return replay;
        await EnsureActiveOwnerAsync(command.CompanyId, command.OwnerUserId, cancellationToken);
        var instance = await LoadCloseAsync(command.CompanyId, command.CloseInstanceId, true, cancellationToken);
        var task = RequireTask(instance, command.CloseTaskId); EnsureVersion(task.Version, command.ExpectedVersion);
        EnsureActive(instance);
        if (task.Status is AccountingCloseTaskStatuses.Completed or AccountingCloseTaskStatuses.Cancelled)
            throw Error(AccountingCloseReasonCodes.InvalidState, "A completed or cancelled close task cannot be assigned.", true);
        var from = task.Status; var now = Now(); task.Assign(command.OwnerUserId, now);
        task.WorkTask.UpdateStatus(WorkTaskStatus.InProgress);
        RecordHistory(instance, task, "assigned", from, task.Status, member.UserId, $"Assigned to {command.OwnerUserId:D}.", now);
        AddOperation(command.CompanyId, "assign_task", command.IdempotencyKey, hash, instance.Id, task.Version, now);
        await WriteAuditAsync(command.CompanyId, member.UserId, AuditEventActions.AccountingCloseTaskAssigned,
            AuditTargetTypes.AccountingCloseTask, task.Id, "Assigned an accounting close task to an active company member.",
            command.CorrelationId, now, cancellationToken);
        await SaveAsync(cancellationToken); _telemetry.Task("assigned", "succeeded", null);
        return await GetAsync(new(command.CompanyId, instance.Id), cancellationToken);
    }

    public async Task<AccountingCloseDto> CompleteTaskAsync(CompleteAccountingCloseTaskCommand command,
        CancellationToken cancellationToken)
    {
        var member = await RequireMemberAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        ValidateIdempotency(command.IdempotencyKey);
        var hash = Hash(new { Action = "complete_task", command.CloseInstanceId, command.CloseTaskId,
            command.ExpectedVersion, command.ReportedAmount, command.Evidence, command.Note });
        var replay = await ReplayTaskOperationAsync(command.CompanyId, command.IdempotencyKey, hash, cancellationToken);
        if (replay is not null) return replay;
        var instance = await LoadCloseAsync(command.CompanyId, command.CloseInstanceId, true, cancellationToken);
        EnsureActive(instance); var task = RequireTask(instance, command.CloseTaskId); EnsureVersion(task.Version, command.ExpectedVersion);
        EnsureCanComplete(member, task);
        if (task.Status == AccountingCloseTaskStatuses.Cancelled)
            throw Error(AccountingCloseReasonCodes.InvalidState, "A cancelled close task must be reopened before completion.", true);
        var incomplete = instance.Dependencies.Where(x => x.DependentTaskId == task.Id)
            .Select(x => instance.Tasks.Single(t => t.Id == x.PredecessorTaskId))
            .Where(x => x.Status != AccountingCloseTaskStatuses.Completed).OrderBy(x => x.Sequence).ToArray();
        if (incomplete.Length > 0)
            throw Error(AccountingCloseReasonCodes.PredecessorIncomplete,
                $"Complete predecessor task '{incomplete[0].Key}' before completing '{task.Key}'.", true);
        var openBlocker = task.Blockers.FirstOrDefault(x => x.Status == "open");
        if (openBlocker is not null)
            throw Error(openBlocker.ReasonCode, openBlocker.Explanation, true);
        var now = Now();
        foreach (var input in command.Evidence ?? [])
        {
            if (string.IsNullOrWhiteSpace(input.EvidenceType))
                throw Error(AccountingCloseReasonCodes.EvidenceRequired, "An evidence type is required for every evidence link.");
            if (task.Evidence.Any(x => x.DocumentId == input.DocumentId &&
                                       x.EvidenceType == input.EvidenceType.Trim().ToLowerInvariant())) continue;
            var document = await _db.CompanyKnowledgeDocuments.IgnoreQueryFilters().SingleOrDefaultAsync(x =>
                x.Id == input.DocumentId && x.CompanyId == command.CompanyId, cancellationToken)
                ?? throw Error(AccountingCloseReasonCodes.EvidenceAccessDenied, "The evidence document is not available in this company.");
            var accessContext = new CompanyKnowledgeAccessContext(command.CompanyId, member.MembershipId,
                member.UserId, member.MembershipRole.ToStorageValue(), ["finance", "accounting", "knowledge"]);
            if (!_knowledgeAccess.CanAccess(accessContext, document))
                throw Error(AccountingCloseReasonCodes.EvidenceAccessDenied, "The evidence document is not accessible to the current user.");
            var evidence = new AccountingCloseTaskEvidence(Guid.NewGuid(), command.CompanyId, task.Id, document.Id,
                input.EvidenceType, document.Title, Metadata(document, "content_hash") ?? Hash(new
                {
                    document.Id,
                    document.StorageKey,
                    document.FileSizeBytes,
                    document.UpdatedUtc
                }), member.UserId, now);
            _db.AccountingCloseTaskEvidence.Add(evidence);
            task.Evidence.Add(evidence);
        }
        var requirements = await _db.AccountingCloseEvidenceRequirements.AsNoTracking()
            .Where(x => x.CompanyId == command.CompanyId && x.TaskDefinitionId == task.TaskDefinitionId)
            .ToListAsync(cancellationToken);
        foreach (var requirement in requirements)
        {
            var count = task.Evidence.Count(x => x.EvidenceType == requirement.EvidenceType);
            if (count < requirement.MinimumCount)
                throw Error(AccountingCloseReasonCodes.EvidenceRequired,
                    $"Task '{task.Key}' requires {requirement.MinimumCount} '{requirement.EvidenceType}' evidence link(s).", true);
        }
        if (task.MaterialityAmount > 0m && !command.ReportedAmount.HasValue)
            throw Error(AccountingCloseReasonCodes.ReportedAmountRequired,
                $"Task '{task.Key}' requires a reported amount so its materiality threshold can be evaluated.");
        var needsSignOff = task.RequiresSignOff || task.MaterialityAmount > 0m &&
            Math.Abs(command.ReportedAmount!.Value) >= task.MaterialityAmount;
        if (needsSignOff && task.ApprovalRequestId is null)
        {
            var approval = await CreateTaskApprovalAsync(command.CompanyId, instance, task,
                member.UserId, command.ReportedAmount, cancellationToken);
            var beforeApprovalVersion = task.Version;
            task.BindApproval(approval.Id, now);
            instance.Touch(now);
            RecordHistory(instance, task, "sign_off_requested", task.Status, task.Status, member.UserId,
                $"Reported amount {command.ReportedAmount:0.00} met the {task.MaterialityAmount:0.00} materiality threshold.", now);
            await WriteAuditAsync(command.CompanyId, member.UserId,
                AuditEventActions.AccountingCloseTaskSignOffRequested, AuditTargetTypes.AccountingCloseTask, task.Id,
                $"Requested exact task sign-off after task version {beforeApprovalVersion} met its materiality threshold.",
                command.CorrelationId, now, cancellationToken);
            await SaveAsync(cancellationToken);
            _telemetry.Task("sign_off_requested", "succeeded", AccountingCloseReasonCodes.SignOffRequired);
            throw Error(AccountingCloseReasonCodes.SignOffRequired,
                "The reported amount requires final sign-off. Reload the task and approve its exact approval request before completion.", true,
                task.Version);
        }
        if (needsSignOff && (task.ApprovalRequest is null ||
            task.ApprovalRequest.TargetEntityType != ApprovalTargetEntityType.AccountingCloseTask.ToStorageValue() ||
            task.ApprovalRequest.TargetEntityId != task.Id || task.ApprovalRequest.Status != ApprovalRequestStatus.Approved))
            throw Error(AccountingCloseReasonCodes.SignOffRequired,
                "The exact close task requires final sign-off before completion.", true);
        var from = task.Status; task.Complete(member.UserId, command.ReportedAmount, now);
        task.WorkTask.UpdateStatus(WorkTaskStatus.Completed,
            new Dictionary<string, JsonNode?> { ["closeTaskId"] = JsonValue.Create(task.Id.ToString("D")) },
            "Completed through the accountable accounting close work plan.");
        if (!string.IsNullOrWhiteSpace(command.Note))
            _db.AccountingCloseTaskNotes.Add(new(Guid.NewGuid(), command.CompanyId, task.Id, member.UserId, command.Note, now));
        RecordHistory(instance, task, "completed", from, task.Status, member.UserId, command.Note, now);
        if (instance.Tasks.All(x => x.Id == task.Id || x.Status == AccountingCloseTaskStatuses.Completed))
        {
            var instanceFrom = instance.Status; instance.MarkCompleted(now);
            RecordHistory(instance, null, "completed", instanceFrom, instance.Status, member.UserId, null, now);
        }
        else instance.Touch(now);
        AddOperation(command.CompanyId, "complete_task", command.IdempotencyKey, hash, instance.Id, task.Version, now);
        await WriteAuditAsync(command.CompanyId, member.UserId, AuditEventActions.AccountingCloseTaskCompleted,
            AuditTargetTypes.AccountingCloseTask, task.Id, "Completed an accounting close task after dependency, evidence, and sign-off checks.",
            command.CorrelationId, now, cancellationToken);
        await SaveAsync(cancellationToken); _telemetry.Task("completed", "succeeded", null);
        return await GetAsync(new(command.CompanyId, instance.Id), cancellationToken);
    }

    public async Task<AccountingCloseDto> ReopenTaskAsync(ReopenAccountingCloseTaskCommand command,
        CancellationToken cancellationToken) => await ChangeTaskStateAsync(command.CompanyId, command.CloseInstanceId,
        command.CloseTaskId, command.ExpectedVersion, command.Reason, command.IdempotencyKey,
        command.ActorUserId, command.CorrelationId, "reopen", cancellationToken);

    public async Task<AccountingCloseDto> CancelTaskAsync(CancelAccountingCloseTaskCommand command,
        CancellationToken cancellationToken) => await ChangeTaskStateAsync(command.CompanyId, command.CloseInstanceId,
        command.CloseTaskId, command.ExpectedVersion, command.Reason, command.IdempotencyKey,
        command.ActorUserId, command.CorrelationId, "cancel", cancellationToken);

    public async Task<AccountingCloseDto> CancelAsync(CancelAccountingCloseCommand command,
        CancellationToken cancellationToken)
    {
        var member = await RequireManagerAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        ValidateIdempotency(command.IdempotencyKey); RequireReason(command.Reason);
        var hash = Hash(new { Action = "cancel_close", command.CloseInstanceId, command.ExpectedVersion, command.Reason });
        var replay = await FindOperationAsync(command.CompanyId, command.IdempotencyKey, cancellationToken);
        if (replay is not null) return await ReplayCloseAsync(replay, hash, cancellationToken);
        var instance = await LoadCloseAsync(command.CompanyId, command.CloseInstanceId, true, cancellationToken);
        EnsureVersion(instance.Version, command.ExpectedVersion); var from = instance.Status; var now = Now();
        instance.Cancel(now);
        foreach (var task in instance.Tasks.Where(x => x.Status != AccountingCloseTaskStatuses.Completed))
        { task.Cancel(now); task.WorkTask.UpdateStatus(WorkTaskStatus.Failed, rationaleSummary: "The accounting close instance was cancelled."); }
        RecordHistory(instance, null, "cancelled", from, instance.Status, member.UserId, command.Reason, now);
        AddOperation(command.CompanyId, "cancel_close", command.IdempotencyKey, hash, instance.Id, instance.Version, now);
        await WriteAuditAsync(command.CompanyId, member.UserId, AuditEventActions.AccountingCloseCancelled,
            AuditTargetTypes.AccountingCloseInstance, instance.Id, "Cancelled an accounting close instance with a retained reason.",
            command.CorrelationId, now, cancellationToken);
        await SaveAsync(cancellationToken); _telemetry.Close("cancelled", "succeeded", instance.Tasks.Count);
        return await GetAsync(new(command.CompanyId, instance.Id), cancellationToken);
    }

    public async Task<AccountingCloseDto> AddBlockerAsync(AddAccountingCloseTaskBlockerCommand command,
        CancellationToken cancellationToken)
    {
        var member = await RequireMemberAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        ValidateIdempotency(command.IdempotencyKey);
        var hash = Hash(new { Action = "add_blocker", command.CloseInstanceId, command.CloseTaskId,
            command.ExpectedVersion, command.ReasonCode, command.Explanation, command.SafeNextAction });
        var replay = await ReplayTaskOperationAsync(command.CompanyId, command.IdempotencyKey, hash, cancellationToken);
        if (replay is not null) return replay;
        var instance = await LoadCloseAsync(command.CompanyId, command.CloseInstanceId, true, cancellationToken);
        EnsureActive(instance); var task = RequireTask(instance, command.CloseTaskId); EnsureVersion(task.Version, command.ExpectedVersion);
        EnsureCanComplete(member, task); var now = Now();
        _db.AccountingCloseTaskBlockers.Add(new(Guid.NewGuid(), command.CompanyId, task.Id, command.ReasonCode,
            command.Explanation, command.SafeNextAction, member.UserId, now));
        task.RecordActivity(now);
        task.WorkTask.UpdateStatus(WorkTaskStatus.Blocked, rationaleSummary: command.Explanation);
        RecordHistory(instance, task, "blocked", task.Status, task.Status, member.UserId, command.Explanation, now);
        instance.Touch(now); AddOperation(command.CompanyId, "add_blocker", command.IdempotencyKey, hash, instance.Id, task.Version, now);
        await WriteAuditAsync(command.CompanyId, member.UserId, AuditEventActions.AccountingCloseTaskBlocked,
            AuditTargetTypes.AccountingCloseTask, task.Id, "Recorded an explicit accounting close task blocker.",
            command.CorrelationId, now, cancellationToken);
        await SaveAsync(cancellationToken); _telemetry.Task("blocked", "succeeded", command.ReasonCode);
        return await GetAsync(new(command.CompanyId, instance.Id), cancellationToken);
    }

    public async Task<AccountingCloseDto> ResolveBlockerAsync(ResolveAccountingCloseTaskBlockerCommand command,
        CancellationToken cancellationToken)
    {
        var member = await RequireManagerAsync(command.CompanyId, command.ActorUserId, cancellationToken);
        ValidateIdempotency(command.IdempotencyKey);
        var hash = Hash(new { Action = "resolve_blocker", command.CloseInstanceId, command.CloseTaskId,
            command.BlockerId, command.ExpectedVersion });
        var replay = await ReplayTaskOperationAsync(command.CompanyId, command.IdempotencyKey, hash, cancellationToken);
        if (replay is not null) return replay;
        var instance = await LoadCloseAsync(command.CompanyId, command.CloseInstanceId, true, cancellationToken);
        EnsureActive(instance); var task = RequireTask(instance, command.CloseTaskId); EnsureVersion(task.Version, command.ExpectedVersion);
        var blocker = task.Blockers.SingleOrDefault(x => x.Id == command.BlockerId)
            ?? throw Error(AccountingCloseReasonCodes.NotFound, "The close task blocker was not found.");
        var now = Now(); blocker.Resolve(member.UserId, now); task.RecordActivity(now);
        if (task.Blockers.All(x => x.Id == blocker.Id || x.Status != "open")) task.WorkTask.UpdateStatus(WorkTaskStatus.InProgress);
        RecordHistory(instance, task, "blocker_resolved", task.Status, task.Status, member.UserId, blocker.ReasonCode, now);
        instance.Touch(now); AddOperation(command.CompanyId, "resolve_blocker", command.IdempotencyKey, hash, instance.Id, task.Version, now);
        await WriteAuditAsync(command.CompanyId, member.UserId, AuditEventActions.AccountingCloseTaskBlockerResolved,
            AuditTargetTypes.AccountingCloseTask, task.Id, "Resolved an accounting close task blocker.",
            command.CorrelationId, now, cancellationToken);
        await SaveAsync(cancellationToken); _telemetry.Task("blocker_resolved", "succeeded", blocker.ReasonCode);
        return await GetAsync(new(command.CompanyId, instance.Id), cancellationToken);
    }

    public async Task<AccountingCloseDto> GetAsync(GetAccountingCloseQuery query,
        CancellationToken cancellationToken)
    {
        await RequireMemberAsync(query.CompanyId, null, cancellationToken);
        return MapClose(await LoadCloseAsync(query.CompanyId, query.CloseInstanceId, false, cancellationToken));
    }

    public async Task<AccountingCloseListResult> ListAsync(ListAccountingClosesQuery query,
        CancellationToken cancellationToken)
    {
        await RequireMemberAsync(query.CompanyId, null, cancellationToken);
        var skip = Math.Max(0, query.Skip); var take = Math.Clamp(query.Take, 1, 250);
        var source = _db.AccountingCloseInstances.AsNoTracking().Where(x => x.CompanyId == query.CompanyId);
        if (query.FiscalPeriodId.HasValue) source = source.Where(x => x.FiscalPeriodId == query.FiscalPeriodId);
        if (!string.IsNullOrWhiteSpace(query.Status)) source = source.Where(x => x.Status == query.Status.Trim().ToLowerInvariant());
        var total = await source.CountAsync(cancellationToken);
        var ids = await source.OrderByDescending(x => x.UpdatedUtc).Skip(skip).Take(take).Select(x => x.Id).ToListAsync(cancellationToken);
        var items = new List<AccountingCloseDto>(ids.Count);
        foreach (var id in ids) items.Add(MapClose(await LoadCloseAsync(query.CompanyId, id, false, cancellationToken)));
        return new(items, total, skip, take);
    }

    private async Task<AccountingCloseDto> ChangeTaskStateAsync(Guid companyId, Guid instanceId, Guid taskId,
        long expectedVersion, string reason, string idempotencyKey, Guid actorUserId, string? correlationId,
        string action, CancellationToken cancellationToken)
    {
        var member = await RequireManagerAsync(companyId, actorUserId, cancellationToken);
        ValidateIdempotency(idempotencyKey); RequireReason(reason);
        var hash = Hash(new { Action = action, instanceId, taskId, expectedVersion, reason });
        var replay = await ReplayTaskOperationAsync(companyId, idempotencyKey, hash, cancellationToken);
        if (replay is not null) return replay;
        var instance = await LoadCloseAsync(companyId, instanceId, true, cancellationToken);
        var task = RequireTask(instance, taskId); EnsureVersion(task.Version, expectedVersion); var from = task.Status; var now = Now();
        if (action == "reopen")
        {
            if (instance.Status == AccountingCloseInstanceStatuses.Cancelled)
                throw Error(AccountingCloseReasonCodes.InvalidState, "A task cannot be reopened after its close instance is cancelled.", true);
            if (task.Status is not AccountingCloseTaskStatuses.Completed and not AccountingCloseTaskStatuses.Cancelled)
                throw Error(AccountingCloseReasonCodes.InvalidState, "Only a completed or cancelled close task can be reopened.", true);
            task.Reopen(now); task.WorkTask.UpdateStatus(WorkTaskStatus.InProgress, rationaleSummary: reason);
            if (instance.Status == AccountingCloseInstanceStatuses.Completed) instance.Reopen(now);
        }
        else
        {
            EnsureActive(instance);
            if (task.Status is AccountingCloseTaskStatuses.Completed or AccountingCloseTaskStatuses.Cancelled)
                throw Error(AccountingCloseReasonCodes.InvalidState, "A completed or cancelled close task cannot be cancelled.", true);
            task.Cancel(now); task.WorkTask.UpdateStatus(WorkTaskStatus.Failed, rationaleSummary: reason);
            instance.Touch(now);
        }
        RecordHistory(instance, task, action == "reopen" ? "reopened" : "cancelled", from, task.Status, member.UserId, reason, now);
        AddOperation(companyId, action + "_task", idempotencyKey, hash, instance.Id, task.Version, now);
        var auditAction = action == "reopen" ? AuditEventActions.AccountingCloseTaskReopened : AuditEventActions.AccountingCloseTaskCancelled;
        await WriteAuditAsync(companyId, member.UserId, auditAction, AuditTargetTypes.AccountingCloseTask, task.Id,
            action == "reopen" ? "Reopened a completed accounting close task." : "Cancelled an accounting close task.",
            correlationId, now, cancellationToken);
        await SaveAsync(cancellationToken); _telemetry.Task(action, "succeeded", null);
        return await GetAsync(new(companyId, instance.Id), cancellationToken);
    }

    private AccountingCloseTemplateVersion BuildVersion(AccountingCloseTemplate template, int number,
        AccountingCloseTemplateInput input, Guid actorUserId, DateTime now)
    {
        var version = new AccountingCloseTemplateVersion(Guid.NewGuid(), template.CompanyId, template.Id, number,
            input.Name, input.Description, input.MaterialityAmount, input.MaterialityPercentage, actorUserId, now);
        var sectionIds = input.Sections.ToDictionary(x => x.Key.Trim(), _ => Guid.NewGuid(), StringComparer.OrdinalIgnoreCase);
        var taskIds = input.Sections.SelectMany(x => x.Tasks).ToDictionary(x => x.Key.Trim(), _ => Guid.NewGuid(), StringComparer.OrdinalIgnoreCase);
        foreach (var sectionInput in input.Sections)
        {
            var section = new AccountingCloseTemplateSection(sectionIds[sectionInput.Key.Trim()], template.CompanyId,
                version.Id, sectionInput.Key, sectionInput.Name, sectionInput.Sequence);
            version.Sections.Add(section);
            foreach (var taskInput in sectionInput.Tasks)
            {
                var task = new AccountingCloseTaskDefinition(taskIds[taskInput.Key.Trim()], template.CompanyId,
                    version.Id, section.Id, taskInput.Key, taskInput.Title, taskInput.Description,
                    taskInput.Sequence, taskInput.DueOffsetDays, taskInput.DefaultOwnerUserId,
                    taskInput.DefaultOwnerRole, taskInput.RequiresSignOff, taskInput.SignOffRole,
                    taskInput.MaterialityAmount);
                foreach (var evidence in taskInput.EvidenceRequirements ?? [])
                    task.EvidenceRequirements.Add(new(Guid.NewGuid(), template.CompanyId, task.Id,
                        evidence.EvidenceType, evidence.Description, evidence.MinimumCount));
                version.TaskDefinitions.Add(task);
            }
        }
        foreach (var dependent in input.Sections.SelectMany(x => x.Tasks))
            foreach (var predecessor in dependent.PredecessorKeys ?? [])
                version.Dependencies.Add(new(Guid.NewGuid(), template.CompanyId, version.Id,
                    taskIds[predecessor.Trim()], taskIds[dependent.Key.Trim()]));
        return version;
    }

    private async Task ValidateTemplateInputAsync(Guid companyId, AccountingCloseTemplateInput input,
        CancellationToken cancellationToken)
    {
        ValidateGraph(input);
        foreach (var task in input.Sections.SelectMany(x => x.Tasks))
        {
            if (task.DefaultOwnerUserId.HasValue) await EnsureActiveOwnerAsync(companyId, task.DefaultOwnerUserId.Value, cancellationToken);
            ValidateRole(task.DefaultOwnerRole); ValidateRole(task.SignOffRole);
            if (task.RequiresSignOff && string.IsNullOrWhiteSpace(task.SignOffRole))
                throw Error(AccountingCloseReasonCodes.InvalidTemplate, $"Task '{task.Key}' requires a sign-off role.");
        }
    }

    internal static void ValidateGraph(AccountingCloseTemplateInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Code) || string.IsNullOrWhiteSpace(input.Name) || input.Sections is null || input.Sections.Count == 0)
            throw Error(AccountingCloseReasonCodes.InvalidTemplate, "A close template requires a code, name, and at least one section.");
        if (input.Code.Trim().Length > 64 || input.Name.Trim().Length > 200 || input.Description?.Trim().Length > 2000)
            throw Error(AccountingCloseReasonCodes.InvalidTemplate, "Close template text exceeds the supported length.");
        if (input.MaterialityAmount < 0m || input.MaterialityPercentage is < 0m or > 100m)
            throw Error(AccountingCloseReasonCodes.InvalidTemplate, "Close materiality settings are outside supported bounds.");
        var sections = input.Sections.ToArray();
        if (sections.Any(x => string.IsNullOrWhiteSpace(x.Key) || string.IsNullOrWhiteSpace(x.Name) ||
                              x.Key.Trim().Length > 64 || x.Name.Trim().Length > 200 || x.Sequence < 1 ||
                              x.Tasks is null || x.Tasks.Count == 0) ||
            sections.GroupBy(x => x.Key.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1) ||
            sections.GroupBy(x => x.Sequence).Any(x => x.Count() > 1))
            throw Error(AccountingCloseReasonCodes.InvalidTemplate, "Close sections require bounded keys, names, positive unique sequence numbers, and at least one task.");
        var tasks = sections.SelectMany(x => x.Tasks).ToArray();
        if (tasks.Any(x => string.IsNullOrWhiteSpace(x.Key) || string.IsNullOrWhiteSpace(x.Title) ||
                           x.Key.Trim().Length > 64 || x.Title.Trim().Length > 200 || x.Description?.Trim().Length > 4000 ||
                           x.Sequence < 1 || x.DueOffsetDays is < -366 or > 366 || x.MaterialityAmount < 0m) ||
            tasks.GroupBy(x => x.Key.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
            throw Error(AccountingCloseReasonCodes.InvalidTemplate, "Close tasks require bounded keys and titles, valid offsets and materiality, positive sequences, and globally unique keys.");
        var keys = tasks.Select(x => x.Key.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var task in tasks)
        {
            var predecessors = task.PredecessorKeys ?? [];
            if (predecessors.Any(string.IsNullOrWhiteSpace) || predecessors.Any(x => !keys.Contains(x.Trim())))
                throw Error(AccountingCloseReasonCodes.InvalidTemplate, $"Task '{task.Key}' references a predecessor outside this template version.");
            if (predecessors.Any(x => string.Equals(x.Trim(), task.Key.Trim(), StringComparison.OrdinalIgnoreCase)))
                throw Error(AccountingCloseReasonCodes.DependencyCycle, $"Task '{task.Key}' cannot depend on itself.");
            if (predecessors.GroupBy(x => x.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
                throw Error(AccountingCloseReasonCodes.InvalidTemplate, $"Task '{task.Key}' contains duplicate predecessor links.");
            var evidence = task.EvidenceRequirements ?? [];
            if (evidence.Any(x => string.IsNullOrWhiteSpace(x.EvidenceType) || string.IsNullOrWhiteSpace(x.Description) ||
                                  x.EvidenceType.Trim().Length > 64 || x.Description.Trim().Length > 500 || x.MinimumCount < 1) ||
                evidence.GroupBy(x => x.EvidenceType.Trim(), StringComparer.OrdinalIgnoreCase).Any(x => x.Count() > 1))
                throw Error(AccountingCloseReasonCodes.InvalidTemplate, $"Task '{task.Key}' has invalid evidence requirements.");
        }
        _ = TopologicalOrder(input);
    }

    internal static IReadOnlyList<string> TopologicalOrder(AccountingCloseTemplateInput input)
    {
        var tasks = input.Sections.SelectMany(x => x.Tasks).ToArray();
        var incoming = tasks.ToDictionary(x => x.Key.Trim(), x => (x.PredecessorKeys ?? []).Select(y => y.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var ready = new SortedSet<string>(incoming.Where(x => x.Value.Count == 0).Select(x => x.Key), StringComparer.OrdinalIgnoreCase);
        var result = new List<string>(tasks.Length);
        while (ready.Count > 0)
        {
            var key = ready.Min!; ready.Remove(key); result.Add(key);
            foreach (var pair in incoming.Where(x => x.Value.Remove(key)).ToArray()) if (pair.Value.Count == 0) ready.Add(pair.Key);
        }
        if (result.Count != tasks.Length)
            throw Error(AccountingCloseReasonCodes.DependencyCycle, "The close template dependency graph contains a cycle.", true);
        return result;
    }

    private async Task<Guid?> ResolveDefaultOwnerAsync(Guid companyId, AccountingCloseTaskDefinition definition,
        Guid fallbackOwnerUserId,
        CancellationToken cancellationToken)
    {
        if (definition.DefaultOwnerUserId.HasValue)
        { await EnsureActiveOwnerAsync(companyId, definition.DefaultOwnerUserId.Value, cancellationToken); return definition.DefaultOwnerUserId; }
        if (string.IsNullOrWhiteSpace(definition.DefaultOwnerRole)) return fallbackOwnerUserId;
        var role = CompanyMembershipRoles.Parse(definition.DefaultOwnerRole);
        var ownerUserId = await _db.CompanyMemberships.AsNoTracking().Where(x => x.CompanyId == companyId &&
            x.Status == CompanyMembershipStatus.Active && x.Role == role && x.UserId.HasValue)
            .OrderBy(x => x.CreatedUtc).Select(x => x.UserId).FirstOrDefaultAsync(cancellationToken);
        return ownerUserId ?? throw Error(AccountingCloseReasonCodes.OwnerOutsideCompany,
            $"No active company member is available for default owner role '{definition.DefaultOwnerRole}'.");
    }

    private async Task<ApprovalRequestDto> CreateTaskApprovalAsync(Guid companyId,
        AccountingCloseInstance instance, AccountingCloseTask task, Guid requestedByUserId,
        decimal? reportedAmount, CancellationToken cancellationToken)
    {
        var role = task.SignOffRole ?? "finance_approver";
        return await _approvals.CreateAsync(companyId, new(
            ApprovalTargetEntityType.AccountingCloseTask.ToStorageValue(), task.Id,
            AuditActorTypes.User, requestedByUserId, "accounting_close_task_sign_off",
            new Dictionary<string, JsonNode?>
            {
                ["closeInstanceId"] = JsonValue.Create(instance.Id.ToString("D")),
                ["templateVersionId"] = JsonValue.Create(instance.TemplateVersionId.ToString("D")),
                ["taskKey"] = JsonValue.Create(task.Key),
                ["materialityAmount"] = JsonValue.Create(task.MaterialityAmount),
                ["reportedAmount"] = JsonValue.Create(reportedAmount)
            }, Steps: [new(1, ApprovalStepApproverType.Role.ToStorageValue(), role)]), cancellationToken);
    }

    private IQueryable<AccountingCloseTemplate> TemplateQuery(bool tracking)
    {
        var source = tracking ? _db.AccountingCloseTemplates : _db.AccountingCloseTemplates.AsNoTracking();
        return source.Include(x => x.Versions).ThenInclude(x => x.Sections)
            .Include(x => x.Versions).ThenInclude(x => x.TaskDefinitions).ThenInclude(x => x.EvidenceRequirements)
            .Include(x => x.Versions).ThenInclude(x => x.Dependencies)
            .Include(x => x.History);
    }

    private async Task<AccountingCloseTemplate> LoadTemplateAsync(Guid companyId, Guid templateId, bool tracking,
        CancellationToken cancellationToken) => await TemplateQuery(tracking).SingleOrDefaultAsync(x =>
        x.CompanyId == companyId && x.Id == templateId, cancellationToken)
        ?? throw Error(AccountingCloseReasonCodes.NotFound, "The accounting close template was not found.");

    private IQueryable<AccountingCloseInstance> CloseQuery(bool tracking)
    {
        var source = tracking ? _db.AccountingCloseInstances : _db.AccountingCloseInstances.AsNoTracking();
        return source.Include(x => x.FiscalPeriod)
            .Include(x => x.Tasks).ThenInclude(x => x.WorkTask)
            .Include(x => x.Tasks).ThenInclude(x => x.ApprovalRequest)
            .Include(x => x.Tasks).ThenInclude(x => x.Evidence)
            .Include(x => x.Tasks).ThenInclude(x => x.Notes)
            .Include(x => x.Tasks).ThenInclude(x => x.Blockers)
            .Include(x => x.Dependencies).Include(x => x.History);
    }

    private async Task<AccountingCloseInstance> LoadCloseAsync(Guid companyId, Guid instanceId, bool tracking,
        CancellationToken cancellationToken) => await CloseQuery(tracking).SingleOrDefaultAsync(x =>
        x.CompanyId == companyId && x.Id == instanceId, cancellationToken)
        ?? throw Error(AccountingCloseReasonCodes.NotFound, "The accounting close instance was not found.");

    private static AccountingCloseTemplateDto MapTemplate(AccountingCloseTemplate template)
    {
        var versions = template.Versions.OrderByDescending(x => x.VersionNumber).Select(version =>
        {
            var keys = version.TaskDefinitions.ToDictionary(x => x.Id, x => x.Key);
            var predecessors = version.Dependencies.GroupBy(x => x.DependentTaskDefinitionId)
                .ToDictionary(x => x.Key, x => x.Select(y => keys[y.PredecessorTaskDefinitionId]).OrderBy(y => y).ToArray());
            var sections = version.Sections.OrderBy(x => x.Sequence).Select(section => new AccountingCloseSectionDto(
                section.Id, section.Key, section.Name, section.Sequence,
                version.TaskDefinitions.Where(x => x.SectionId == section.Id).OrderBy(x => x.Sequence).Select(task =>
                    new AccountingCloseTaskDefinitionDto(task.Id, task.SectionId, task.Key, task.Title, task.Description,
                        task.Sequence, task.DueOffsetDays, task.DefaultOwnerUserId, task.DefaultOwnerRole,
                        task.RequiresSignOff, task.SignOffRole, task.MaterialityAmount,
                        predecessors.GetValueOrDefault(task.Id) ?? [],
                        task.EvidenceRequirements.OrderBy(x => x.EvidenceType).Select(x =>
                            new AccountingCloseEvidenceRequirementDto(x.Id, x.EvidenceType, x.Description, x.MinimumCount)).ToArray())).ToArray())).ToArray();
            return new AccountingCloseTemplateVersionDto(version.Id, version.VersionNumber, version.Name,
                version.Description, version.MaterialityAmount, version.MaterialityPercentage, version.Status,
                version.CreatedUtc, version.ActivatedUtc, sections);
        }).ToArray();
        return new(template.Id, template.CompanyId, template.Code, template.Name, template.Description,
            template.Status, template.ActiveVersionId, template.LatestVersionNumber, template.Version,
            template.CreatedUtc, template.UpdatedUtc, versions.FirstOrDefault(x => x.Id == template.ActiveVersionId),
            versions, template.History.OrderByDescending(x => x.OccurredUtc).Select(x =>
                new AccountingCloseTemplateHistoryDto(x.Id, x.TemplateVersionId, x.Action, x.ActorUserId, x.Reason, x.OccurredUtc)).ToArray(),
            template.Status == AccountingCloseTemplateStatuses.Retired ? ["copy"] : ["preview", "version", "copy", "activate", "retire"]);
    }

    private static AccountingCloseDto MapClose(AccountingCloseInstance instance)
    {
        var predecessors = instance.Dependencies.GroupBy(x => x.DependentTaskId)
            .ToDictionary(x => x.Key, x => x.Select(y => y.PredecessorTaskId).ToArray());
        var tasksById = instance.Tasks.ToDictionary(x => x.Id);
        var tasks = instance.Tasks.OrderBy(x => x.Sequence).Select(task =>
        {
            var predecessorIds = predecessors.GetValueOrDefault(task.Id) ?? [];
            var reasons = predecessorIds.Where(x => tasksById[x].Status != AccountingCloseTaskStatuses.Completed)
                .Select(_ => AccountingCloseReasonCodes.PredecessorIncomplete)
                .Concat(task.Blockers.Where(x => x.Status == "open").Select(x => x.ReasonCode)).Distinct().ToArray();
            var actions = new List<string>();
            if (instance.Status == AccountingCloseInstanceStatuses.Active && task.Status != AccountingCloseTaskStatuses.Completed && task.Status != AccountingCloseTaskStatuses.Cancelled)
                actions.AddRange(["assign", "complete", "cancel", "add_blocker"]);
            if (instance.Status != AccountingCloseInstanceStatuses.Cancelled &&
                task.Status is AccountingCloseTaskStatuses.Completed or AccountingCloseTaskStatuses.Cancelled)
                actions.Add("reopen");
            return new AccountingCloseTaskDto(task.Id, task.TaskDefinitionId, task.SectionId, task.Key, task.Title,
                task.Description, task.Sequence, task.Status, task.OwnerUserId, task.OwnerRole, task.DueUtc,
                task.RequiresSignOff, task.SignOffRole, task.MaterialityAmount, task.WorkTaskId, task.ApprovalRequestId,
                task.ApprovalRequest?.Status.ToStorageValue(), task.CreatedUtc, task.UpdatedUtc, task.CompletedUtc,
                task.CompletedByUserId, task.ReportedAmount, task.Version, predecessorIds, reasons,
                task.Evidence.OrderBy(x => x.LinkedUtc).Select(x => new AccountingCloseTaskEvidenceDto(x.Id,
                    x.DocumentId, x.EvidenceType, x.DocumentTitle, x.ContentHash, x.LinkedByUserId, x.LinkedUtc)).ToArray(),
                task.Notes.OrderBy(x => x.CreatedUtc).Select(x => new AccountingCloseTaskNoteDto(x.Id,
                    x.AuthorUserId, x.Note, x.CreatedUtc)).ToArray(),
                task.Blockers.OrderBy(x => x.CreatedUtc).Select(x => new AccountingCloseTaskBlockerDto(x.Id,
                    x.ReasonCode, x.Explanation, x.SafeNextAction, x.Status, x.CreatedByUserId, x.CreatedUtc,
                    x.ResolvedByUserId, x.ResolvedUtc)).ToArray(), actions);
        }).ToArray();
        return new(instance.Id, instance.CompanyId, instance.FiscalPeriodId, instance.FiscalPeriod.Name,
            instance.TemplateId, instance.TemplateVersionId, instance.TemplateVersionNumber, instance.Name,
            instance.Status, instance.StartedByUserId, instance.StartedUtc, instance.UpdatedUtc,
            instance.CompletedUtc, instance.CancelledUtc, instance.Version,
            tasks.Count(x => x.Status == AccountingCloseTaskStatuses.Completed), tasks.Length, tasks,
            instance.History.OrderBy(x => x.OccurredUtc).Select(x => new AccountingCloseHistoryDto(x.Id,
                x.CloseTaskId, x.Action, x.FromStatus, x.ToStatus, x.ActorUserId, x.Reason, x.OccurredUtc)).ToArray(),
            instance.Status == AccountingCloseInstanceStatuses.Active ? ["cancel"] : []);
    }

    private static AccountingCloseTemplateInput ToInput(AccountingCloseTemplateVersion version)
    {
        var keys = version.TaskDefinitions.ToDictionary(x => x.Id, x => x.Key);
        var predecessors = version.Dependencies.GroupBy(x => x.DependentTaskDefinitionId)
            .ToDictionary(x => x.Key, x => x.Select(y => keys[y.PredecessorTaskDefinitionId]).ToArray());
        return new(version.Template.Code, version.Name, version.Description, version.MaterialityAmount,
            version.MaterialityPercentage, version.Sections.OrderBy(x => x.Sequence).Select(section =>
                new AccountingCloseSectionInput(section.Key, section.Name, section.Sequence,
                    version.TaskDefinitions.Where(x => x.SectionId == section.Id).OrderBy(x => x.Sequence).Select(task =>
                        new AccountingCloseTaskDefinitionInput(task.Key, task.Title, task.Description, task.Sequence,
                            task.DueOffsetDays, task.DefaultOwnerUserId, task.DefaultOwnerRole, task.RequiresSignOff,
                            task.SignOffRole, task.MaterialityAmount,
                            task.EvidenceRequirements.Select(x => new AccountingCloseEvidenceRequirementInput(
                                x.EvidenceType, x.Description, x.MinimumCount)).ToArray(),
                            predecessors.GetValueOrDefault(task.Id) ?? [])).ToArray())).ToArray());
    }

    private async Task<ResolvedCompanyMembershipContext> RequireMemberAsync(Guid companyId, Guid? actorUserId,
        CancellationToken cancellationToken)
    {
        var member = await _memberships.ResolveAsync(companyId, cancellationToken)
            ?? throw new UnauthorizedAccessException("Active company membership is required.");
        if (actorUserId.HasValue && member.UserId != actorUserId.Value)
            throw new UnauthorizedAccessException("The requested actor does not match the current company member.");
        return member;
    }

    private async Task<ResolvedCompanyMembershipContext> RequireManagerAsync(Guid companyId, Guid actorUserId,
        CancellationToken cancellationToken)
    {
        var member = await RequireMemberAsync(companyId, actorUserId, cancellationToken);
        if (member.MembershipRole is not (CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager))
            throw new UnauthorizedAccessException("Company manager access is required for this close action.");
        return member;
    }

    private async Task EnsureActiveOwnerAsync(Guid companyId, Guid ownerUserId, CancellationToken cancellationToken)
    {
        if (!await _db.CompanyMemberships.AsNoTracking().AnyAsync(x => x.CompanyId == companyId &&
            x.UserId == ownerUserId && x.Status == CompanyMembershipStatus.Active, cancellationToken))
            throw Error(AccountingCloseReasonCodes.OwnerOutsideCompany,
                "The selected close task owner is not an active member of this company.");
    }

    private static void EnsureCanComplete(ResolvedCompanyMembershipContext member, AccountingCloseTask task)
    {
        if (task.OwnerUserId == member.UserId || member.MembershipRole is CompanyMembershipRole.Owner or CompanyMembershipRole.Admin or CompanyMembershipRole.Manager)
            return;
        throw Error(AccountingCloseReasonCodes.CompletionForbidden,
            "Only the assigned owner or a company manager can complete this close task.");
    }

    private async Task<AccountingCloseTemplateDto> ReplayTemplateAsync(AccountingCloseOperation operation,
        string hash, CancellationToken cancellationToken)
    { EnsureReplayHash(operation, hash); return await GetTemplateAsync(new(operation.CompanyId, operation.TargetId), cancellationToken); }
    private async Task<AccountingCloseDto> ReplayCloseAsync(AccountingCloseOperation operation,
        string hash, CancellationToken cancellationToken)
    { EnsureReplayHash(operation, hash); return await GetAsync(new(operation.CompanyId, operation.TargetId), cancellationToken); }
    private async Task<AccountingCloseDto?> ReplayTaskOperationAsync(Guid companyId, string idempotencyKey,
        string hash, CancellationToken cancellationToken)
    {
        ValidateIdempotency(idempotencyKey); var operation = await FindOperationAsync(companyId, idempotencyKey, cancellationToken);
        return operation is null ? null : await ReplayCloseAsync(operation, hash, cancellationToken);
    }
    private async Task<AccountingCloseOperation?> FindOperationAsync(Guid companyId, string key,
        CancellationToken cancellationToken) => await _db.AccountingCloseOperations.AsNoTracking()
        .SingleOrDefaultAsync(x => x.CompanyId == companyId && x.IdempotencyKey == key.Trim(), cancellationToken);
    private static void EnsureReplayHash(AccountingCloseOperation operation, string hash)
    {
        if (!string.Equals(operation.PayloadHash, hash, StringComparison.OrdinalIgnoreCase))
            throw Error(AccountingCloseReasonCodes.IdempotencyConflict,
                "This request identity was already used for different accounting close content.", true);
    }
    private void AddOperation(Guid companyId, string action, string key, string hash, Guid targetId,
        long resultVersion, DateTime now) => _db.AccountingCloseOperations.Add(new(Guid.NewGuid(), companyId,
        action, key, hash, targetId, resultVersion, now));
    private void RecordHistory(AccountingCloseInstance instance, AccountingCloseTask? task, string action,
        string? from, string to, Guid actorUserId, string? reason, DateTime now) =>
        _db.AccountingCloseStatusHistory.Add(new(Guid.NewGuid(), instance.CompanyId, instance.Id, task?.Id,
            action, from, to, actorUserId, reason, now));
    private static AccountingCloseTask RequireTask(AccountingCloseInstance instance, Guid taskId) =>
        instance.Tasks.SingleOrDefault(x => x.Id == taskId)
        ?? throw Error(AccountingCloseReasonCodes.NotFound, "The accounting close task was not found.");
    private static void EnsureActive(AccountingCloseInstance instance)
    { if (instance.Status != AccountingCloseInstanceStatuses.Active) throw Error(AccountingCloseReasonCodes.InvalidState, "The accounting close instance is not active.", true); }
    private static void EnsureVersion(long current, long expected)
    { if (current != expected) throw Error(AccountingCloseReasonCodes.VersionConflict, $"This record is now version {current}. Reload it before continuing.", true, current); }
    private static void ValidateIdempotency(string key)
    { if (string.IsNullOrWhiteSpace(key) || key.Trim().Length > 200) throw Error(AccountingCloseReasonCodes.IdempotencyConflict, "A stable request identity is required."); }
    private static void RequireReason(string reason)
    { if (string.IsNullOrWhiteSpace(reason)) throw Error(AccountingCloseReasonCodes.InvalidState, "A reason is required."); }
    private static void ValidateRole(string? role)
    { if (!string.IsNullOrWhiteSpace(role) && !CompanyMembershipRoles.TryParse(role, out _)) throw Error(AccountingCloseReasonCodes.InvalidTemplate, CompanyMembershipRoles.BuildValidationMessage(role)); }
    private async Task SaveAsync(CancellationToken cancellationToken)
    {
        try { await _db.SaveChangesAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw Error(AccountingCloseReasonCodes.VersionConflict,
            "The accounting close changed while this request was running. Reload and retry.", true); }
    }
    private async Task WriteAuditAsync(Guid companyId, Guid actorId, string action, string targetType,
        Guid targetId, string summary, string? correlationId, DateTime now, CancellationToken cancellationToken) =>
        await _audit.WriteAsync(new(companyId, AuditActorTypes.User, actorId, action, targetType,
            targetId.ToString("D"), AuditEventOutcomes.Succeeded, summary, ["accounting_close"],
            CorrelationId: correlationId, OccurredUtc: now), cancellationToken);
    private static string Hash(object value) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)))).ToLowerInvariant();
    private static string? Metadata(CompanyKnowledgeDocument document, string key) =>
        document.Metadata.TryGetValue(key, out var node) ? node?.ToString() : null;
    private DateTime Now() => _clock.GetUtcNow().UtcDateTime;
    private static AccountingCloseException Error(string code, string message, bool conflict = false,
        long? currentVersion = null) => new(code, message, conflict, currentVersion);
}
