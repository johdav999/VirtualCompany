namespace VirtualCompany.Domain.Entities;

public static class AccountingCloseTemplateStatuses
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Retired = "retired";
}

public static class AccountingCloseTemplateVersionStatuses
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Superseded = "superseded";
    public const string Retired = "retired";
}

public static class AccountingCloseInstanceStatuses
{
    public const string Active = "active";
    public const string Completed = "completed";
    public const string Cancelled = "cancelled";
}

public static class AccountingCloseTaskStatuses
{
    public const string Pending = "pending";
    public const string InProgress = "in_progress";
    public const string Completed = "completed";
    public const string Reopened = "reopened";
    public const string Cancelled = "cancelled";
}

public sealed class AccountingCloseTemplate : ICompanyOwnedEntity
{
    private AccountingCloseTemplate() { }

    public AccountingCloseTemplate(Guid id, Guid companyId, string code, string name, string? description,
        Guid actorUserId, DateTime createdUtc)
    {
        Id = CloseValue.RequiredId(id == Guid.Empty ? Guid.NewGuid() : id, nameof(id));
        CompanyId = CloseValue.RequiredId(companyId, nameof(companyId));
        Code = CloseValue.Required(code, nameof(code), 64).ToUpperInvariant();
        Name = CloseValue.Required(name, nameof(name), 200);
        Description = CloseValue.Optional(description, nameof(description), 2000);
        CreatedByUserId = UpdatedByUserId = CloseValue.RequiredId(actorUserId, nameof(actorUserId));
        CreatedUtc = UpdatedUtc = CloseValue.Utc(createdUtc);
        Status = AccountingCloseTemplateStatuses.Draft;
        Version = 1;
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Code { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public string Status { get; private set; } = null!;
    public Guid? ActiveVersionId { get; private set; }
    public int LatestVersionNumber { get; private set; }
    public long Version { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public Guid UpdatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public Company Company { get; private set; } = null!;
    public AccountingCloseTemplateVersion? ActiveVersion { get; private set; }
    public ICollection<AccountingCloseTemplateVersion> Versions { get; } = new List<AccountingCloseTemplateVersion>();
    public ICollection<AccountingCloseTemplateHistory> History { get; } = new List<AccountingCloseTemplateHistory>();

    public int ReserveNextVersion(Guid actorUserId, DateTime now)
    {
        if (Status == AccountingCloseTemplateStatuses.Retired)
            throw new InvalidOperationException("A retired close template cannot be versioned.");
        LatestVersionNumber++;
        Touch(actorUserId, now);
        return LatestVersionNumber;
    }

    public void Activate(Guid versionId, Guid actorUserId, DateTime now)
    {
        ActiveVersionId = CloseValue.RequiredId(versionId, nameof(versionId));
        Status = AccountingCloseTemplateStatuses.Active;
        Touch(actorUserId, now);
    }

    public void Retire(Guid actorUserId, DateTime now)
    {
        Status = AccountingCloseTemplateStatuses.Retired;
        Touch(actorUserId, now);
    }

    private void Touch(Guid actorUserId, DateTime now)
    {
        UpdatedByUserId = CloseValue.RequiredId(actorUserId, nameof(actorUserId));
        UpdatedUtc = CloseValue.Utc(now);
        Version++;
    }
}

public sealed class AccountingCloseTemplateVersion : ICompanyOwnedEntity
{
    private AccountingCloseTemplateVersion() { }

    public AccountingCloseTemplateVersion(Guid id, Guid companyId, Guid templateId, int versionNumber,
        string name, string? description, decimal materialityAmount, decimal? materialityPercentage,
        Guid actorUserId, DateTime createdUtc)
    {
        Id = CloseValue.RequiredId(id == Guid.Empty ? Guid.NewGuid() : id, nameof(id));
        CompanyId = CloseValue.RequiredId(companyId, nameof(companyId));
        TemplateId = CloseValue.RequiredId(templateId, nameof(templateId));
        if (versionNumber < 1) throw new ArgumentOutOfRangeException(nameof(versionNumber));
        if (materialityAmount < 0m) throw new ArgumentOutOfRangeException(nameof(materialityAmount));
        if (materialityPercentage is < 0m or > 100m) throw new ArgumentOutOfRangeException(nameof(materialityPercentage));
        VersionNumber = versionNumber;
        Name = CloseValue.Required(name, nameof(name), 200);
        Description = CloseValue.Optional(description, nameof(description), 2000);
        MaterialityAmount = materialityAmount;
        MaterialityPercentage = materialityPercentage;
        Status = AccountingCloseTemplateVersionStatuses.Draft;
        CreatedByUserId = CloseValue.RequiredId(actorUserId, nameof(actorUserId));
        CreatedUtc = CloseValue.Utc(createdUtc);
    }

    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid TemplateId { get; private set; }
    public int VersionNumber { get; private set; }
    public string Name { get; private set; } = null!;
    public string? Description { get; private set; }
    public decimal MaterialityAmount { get; private set; }
    public decimal? MaterialityPercentage { get; private set; }
    public string Status { get; private set; } = null!;
    public DateTime? ActivatedUtc { get; private set; }
    public Guid? ActivatedByUserId { get; private set; }
    public DateTime? RetiredUtc { get; private set; }
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public AccountingCloseTemplate Template { get; private set; } = null!;
    public ICollection<AccountingCloseTemplateSection> Sections { get; } = new List<AccountingCloseTemplateSection>();
    public ICollection<AccountingCloseTaskDefinition> TaskDefinitions { get; } = new List<AccountingCloseTaskDefinition>();
    public ICollection<AccountingCloseTaskDefinitionDependency> Dependencies { get; } = new List<AccountingCloseTaskDefinitionDependency>();

    public void Activate(Guid actorUserId, DateTime now)
    {
        if (Status == AccountingCloseTemplateVersionStatuses.Retired)
            throw new InvalidOperationException("A retired close template version cannot be activated.");
        Status = AccountingCloseTemplateVersionStatuses.Active;
        ActivatedByUserId = CloseValue.RequiredId(actorUserId, nameof(actorUserId));
        ActivatedUtc = CloseValue.Utc(now);
    }

    public void Supersede(DateTime now)
    {
        if (Status != AccountingCloseTemplateVersionStatuses.Active) return;
        Status = AccountingCloseTemplateVersionStatuses.Superseded;
        RetiredUtc = CloseValue.Utc(now);
    }

    public void Retire(DateTime now)
    {
        Status = AccountingCloseTemplateVersionStatuses.Retired;
        RetiredUtc = CloseValue.Utc(now);
    }
}

public sealed class AccountingCloseTemplateSection : ICompanyOwnedEntity
{
    private AccountingCloseTemplateSection() { }
    public AccountingCloseTemplateSection(Guid id, Guid companyId, Guid templateVersionId, string key,
        string name, int sequence)
    {
        Id = CloseValue.RequiredId(id == Guid.Empty ? Guid.NewGuid() : id, nameof(id));
        CompanyId = CloseValue.RequiredId(companyId, nameof(companyId));
        TemplateVersionId = CloseValue.RequiredId(templateVersionId, nameof(templateVersionId));
        Key = CloseValue.Required(key, nameof(key), 64).ToLowerInvariant();
        Name = CloseValue.Required(name, nameof(name), 200);
        if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
        Sequence = sequence;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid TemplateVersionId { get; private set; }
    public string Key { get; private set; } = null!;
    public string Name { get; private set; } = null!;
    public int Sequence { get; private set; }
    public AccountingCloseTemplateVersion TemplateVersion { get; private set; } = null!;
}

public sealed class AccountingCloseTaskDefinition : ICompanyOwnedEntity
{
    private AccountingCloseTaskDefinition() { }
    public AccountingCloseTaskDefinition(Guid id, Guid companyId, Guid templateVersionId, Guid sectionId,
        string key, string title, string? description, int sequence, int dueOffsetDays, Guid? defaultOwnerUserId,
        string? defaultOwnerRole, bool requiresSignOff, string? signOffRole, decimal? materialityAmount)
    {
        Id = CloseValue.RequiredId(id == Guid.Empty ? Guid.NewGuid() : id, nameof(id));
        CompanyId = CloseValue.RequiredId(companyId, nameof(companyId));
        TemplateVersionId = CloseValue.RequiredId(templateVersionId, nameof(templateVersionId));
        SectionId = CloseValue.RequiredId(sectionId, nameof(sectionId));
        Key = CloseValue.Required(key, nameof(key), 64).ToLowerInvariant();
        Title = CloseValue.Required(title, nameof(title), 200);
        Description = CloseValue.Optional(description, nameof(description), 4000);
        if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
        if (dueOffsetDays is < -366 or > 366) throw new ArgumentOutOfRangeException(nameof(dueOffsetDays));
        if (defaultOwnerUserId == Guid.Empty) throw new ArgumentException("Default owner cannot be empty.", nameof(defaultOwnerUserId));
        if (materialityAmount < 0m) throw new ArgumentOutOfRangeException(nameof(materialityAmount));
        Sequence = sequence;
        DueOffsetDays = dueOffsetDays;
        DefaultOwnerUserId = defaultOwnerUserId;
        DefaultOwnerRole = CloseValue.Optional(defaultOwnerRole, nameof(defaultOwnerRole), 64)?.ToLowerInvariant();
        RequiresSignOff = requiresSignOff;
        SignOffRole = CloseValue.Optional(signOffRole, nameof(signOffRole), 64)?.ToLowerInvariant();
        MaterialityAmount = materialityAmount;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid TemplateVersionId { get; private set; }
    public Guid SectionId { get; private set; }
    public string Key { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public int Sequence { get; private set; }
    public int DueOffsetDays { get; private set; }
    public Guid? DefaultOwnerUserId { get; private set; }
    public string? DefaultOwnerRole { get; private set; }
    public bool RequiresSignOff { get; private set; }
    public string? SignOffRole { get; private set; }
    public decimal? MaterialityAmount { get; private set; }
    public AccountingCloseTemplateVersion TemplateVersion { get; private set; } = null!;
    public AccountingCloseTemplateSection Section { get; private set; } = null!;
    public ICollection<AccountingCloseEvidenceRequirement> EvidenceRequirements { get; } = new List<AccountingCloseEvidenceRequirement>();
}

public sealed class AccountingCloseTaskDefinitionDependency : ICompanyOwnedEntity
{
    private AccountingCloseTaskDefinitionDependency() { }
    public AccountingCloseTaskDefinitionDependency(Guid id, Guid companyId, Guid templateVersionId,
        Guid predecessorTaskDefinitionId, Guid dependentTaskDefinitionId)
    {
        Id = CloseValue.RequiredId(id == Guid.Empty ? Guid.NewGuid() : id, nameof(id));
        CompanyId = CloseValue.RequiredId(companyId, nameof(companyId));
        TemplateVersionId = CloseValue.RequiredId(templateVersionId, nameof(templateVersionId));
        PredecessorTaskDefinitionId = CloseValue.RequiredId(predecessorTaskDefinitionId, nameof(predecessorTaskDefinitionId));
        DependentTaskDefinitionId = CloseValue.RequiredId(dependentTaskDefinitionId, nameof(dependentTaskDefinitionId));
        if (PredecessorTaskDefinitionId == DependentTaskDefinitionId)
            throw new ArgumentException("A close task cannot depend on itself.");
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid TemplateVersionId { get; private set; }
    public Guid PredecessorTaskDefinitionId { get; private set; }
    public Guid DependentTaskDefinitionId { get; private set; }
    public AccountingCloseTemplateVersion TemplateVersion { get; private set; } = null!;
}

public sealed class AccountingCloseEvidenceRequirement : ICompanyOwnedEntity
{
    private AccountingCloseEvidenceRequirement() { }
    public AccountingCloseEvidenceRequirement(Guid id, Guid companyId, Guid taskDefinitionId,
        string evidenceType, string description, int minimumCount)
    {
        Id = CloseValue.RequiredId(id == Guid.Empty ? Guid.NewGuid() : id, nameof(id));
        CompanyId = CloseValue.RequiredId(companyId, nameof(companyId));
        TaskDefinitionId = CloseValue.RequiredId(taskDefinitionId, nameof(taskDefinitionId));
        EvidenceType = CloseValue.Required(evidenceType, nameof(evidenceType), 64).ToLowerInvariant();
        Description = CloseValue.Required(description, nameof(description), 500);
        if (minimumCount < 1) throw new ArgumentOutOfRangeException(nameof(minimumCount));
        MinimumCount = minimumCount;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid TaskDefinitionId { get; private set; }
    public string EvidenceType { get; private set; } = null!;
    public string Description { get; private set; } = null!;
    public int MinimumCount { get; private set; }
    public AccountingCloseTaskDefinition TaskDefinition { get; private set; } = null!;
}

public sealed class AccountingCloseTemplateHistory : ICompanyOwnedEntity
{
    private AccountingCloseTemplateHistory() { }
    public AccountingCloseTemplateHistory(Guid id, Guid companyId, Guid templateId, Guid templateVersionId,
        string action, Guid actorUserId, string? reason, DateTime occurredUtc)
    {
        Id = CloseValue.RequiredId(id == Guid.Empty ? Guid.NewGuid() : id, nameof(id));
        CompanyId = CloseValue.RequiredId(companyId, nameof(companyId));
        TemplateId = CloseValue.RequiredId(templateId, nameof(templateId));
        TemplateVersionId = CloseValue.RequiredId(templateVersionId, nameof(templateVersionId));
        Action = CloseValue.Required(action, nameof(action), 32).ToLowerInvariant();
        ActorUserId = CloseValue.RequiredId(actorUserId, nameof(actorUserId));
        Reason = CloseValue.Optional(reason, nameof(reason), 1000);
        OccurredUtc = CloseValue.Utc(occurredUtc);
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid TemplateId { get; private set; }
    public Guid TemplateVersionId { get; private set; }
    public string Action { get; private set; } = null!;
    public Guid ActorUserId { get; private set; }
    public string? Reason { get; private set; }
    public DateTime OccurredUtc { get; private set; }
    public AccountingCloseTemplate Template { get; private set; } = null!;
}

public sealed class AccountingCloseInstance : ICompanyOwnedEntity
{
    private AccountingCloseInstance() { }
    public AccountingCloseInstance(Guid id, Guid companyId, Guid fiscalPeriodId, Guid templateId,
        Guid templateVersionId, int templateVersionNumber, string name, Guid startedByUserId,
        string idempotencyKey, DateTime startedUtc)
    {
        Id = CloseValue.RequiredId(id == Guid.Empty ? Guid.NewGuid() : id, nameof(id));
        CompanyId = CloseValue.RequiredId(companyId, nameof(companyId));
        FiscalPeriodId = CloseValue.RequiredId(fiscalPeriodId, nameof(fiscalPeriodId));
        TemplateId = CloseValue.RequiredId(templateId, nameof(templateId));
        TemplateVersionId = CloseValue.RequiredId(templateVersionId, nameof(templateVersionId));
        if (templateVersionNumber < 1) throw new ArgumentOutOfRangeException(nameof(templateVersionNumber));
        TemplateVersionNumber = templateVersionNumber;
        Name = CloseValue.Required(name, nameof(name), 200);
        StartedByUserId = CloseValue.RequiredId(startedByUserId, nameof(startedByUserId));
        StartIdempotencyKey = CloseValue.Required(idempotencyKey, nameof(idempotencyKey), 200);
        Status = AccountingCloseInstanceStatuses.Active;
        StartedUtc = UpdatedUtc = CloseValue.Utc(startedUtc);
        Version = 1;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid FiscalPeriodId { get; private set; }
    public Guid TemplateId { get; private set; }
    public Guid TemplateVersionId { get; private set; }
    public int TemplateVersionNumber { get; private set; }
    public string Name { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public string StartIdempotencyKey { get; private set; } = null!;
    public Guid StartedByUserId { get; private set; }
    public DateTime StartedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public DateTime? CancelledUtc { get; private set; }
    public long Version { get; private set; }
    public FiscalPeriod FiscalPeriod { get; private set; } = null!;
    public AccountingCloseTemplate Template { get; private set; } = null!;
    public AccountingCloseTemplateVersion TemplateVersion { get; private set; } = null!;
    public ICollection<AccountingCloseTask> Tasks { get; } = new List<AccountingCloseTask>();
    public ICollection<AccountingCloseTaskDependency> Dependencies { get; } = new List<AccountingCloseTaskDependency>();
    public ICollection<AccountingCloseStatusHistory> History { get; } = new List<AccountingCloseStatusHistory>();

    public void MarkCompleted(DateTime now)
    {
        Status = AccountingCloseInstanceStatuses.Completed;
        CompletedUtc = UpdatedUtc = CloseValue.Utc(now);
        Version++;
    }

    public void Cancel(DateTime now)
    {
        if (Status == AccountingCloseInstanceStatuses.Completed)
            throw new InvalidOperationException("A completed close cannot be cancelled.");
        Status = AccountingCloseInstanceStatuses.Cancelled;
        CancelledUtc = UpdatedUtc = CloseValue.Utc(now);
        Version++;
    }

    public void Touch(DateTime now)
    {
        UpdatedUtc = CloseValue.Utc(now);
        Version++;
    }

    public void Reopen(DateTime now)
    {
        Status = AccountingCloseInstanceStatuses.Active;
        CompletedUtc = null;
        UpdatedUtc = CloseValue.Utc(now);
        Version++;
    }
}

public sealed class AccountingCloseTask : ICompanyOwnedEntity
{
    private AccountingCloseTask() { }
    public AccountingCloseTask(Guid id, Guid companyId, Guid closeInstanceId, Guid taskDefinitionId,
        Guid sectionId, string key, string title, string? description, int sequence, DateTime dueUtc,
        Guid? ownerUserId, string? ownerRole, bool requiresSignOff, string? signOffRole,
        decimal materialityAmount, Guid workTaskId, DateTime createdUtc)
    {
        Id = CloseValue.RequiredId(id == Guid.Empty ? Guid.NewGuid() : id, nameof(id));
        CompanyId = CloseValue.RequiredId(companyId, nameof(companyId));
        CloseInstanceId = CloseValue.RequiredId(closeInstanceId, nameof(closeInstanceId));
        TaskDefinitionId = CloseValue.RequiredId(taskDefinitionId, nameof(taskDefinitionId));
        SectionId = CloseValue.RequiredId(sectionId, nameof(sectionId));
        Key = CloseValue.Required(key, nameof(key), 64).ToLowerInvariant();
        Title = CloseValue.Required(title, nameof(title), 200);
        Description = CloseValue.Optional(description, nameof(description), 4000);
        if (sequence < 1) throw new ArgumentOutOfRangeException(nameof(sequence));
        if (ownerUserId == Guid.Empty) throw new ArgumentException("Owner cannot be empty.", nameof(ownerUserId));
        if (materialityAmount < 0m) throw new ArgumentOutOfRangeException(nameof(materialityAmount));
        Sequence = sequence;
        DueUtc = CloseValue.Utc(dueUtc);
        OwnerUserId = ownerUserId;
        OwnerRole = CloseValue.Optional(ownerRole, nameof(ownerRole), 64)?.ToLowerInvariant();
        RequiresSignOff = requiresSignOff;
        SignOffRole = CloseValue.Optional(signOffRole, nameof(signOffRole), 64)?.ToLowerInvariant();
        MaterialityAmount = materialityAmount;
        WorkTaskId = CloseValue.RequiredId(workTaskId, nameof(workTaskId));
        Status = AccountingCloseTaskStatuses.Pending;
        CreatedUtc = UpdatedUtc = CloseValue.Utc(createdUtc);
        Version = 1;
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CloseInstanceId { get; private set; }
    public Guid TaskDefinitionId { get; private set; }
    public Guid SectionId { get; private set; }
    public string Key { get; private set; } = null!;
    public string Title { get; private set; } = null!;
    public string? Description { get; private set; }
    public int Sequence { get; private set; }
    public string Status { get; private set; } = null!;
    public Guid? OwnerUserId { get; private set; }
    public string? OwnerRole { get; private set; }
    public DateTime DueUtc { get; private set; }
    public bool RequiresSignOff { get; private set; }
    public string? SignOffRole { get; private set; }
    public decimal MaterialityAmount { get; private set; }
    public Guid WorkTaskId { get; private set; }
    public Guid? ApprovalRequestId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public DateTime UpdatedUtc { get; private set; }
    public DateTime? CompletedUtc { get; private set; }
    public Guid? CompletedByUserId { get; private set; }
    public decimal? ReportedAmount { get; private set; }
    public long Version { get; private set; }
    public AccountingCloseInstance CloseInstance { get; private set; } = null!;
    public WorkTask WorkTask { get; private set; } = null!;
    public ApprovalRequest? ApprovalRequest { get; private set; }
    public ICollection<AccountingCloseTaskEvidence> Evidence { get; } = new List<AccountingCloseTaskEvidence>();
    public ICollection<AccountingCloseTaskNote> Notes { get; } = new List<AccountingCloseTaskNote>();
    public ICollection<AccountingCloseTaskBlocker> Blockers { get; } = new List<AccountingCloseTaskBlocker>();

    public void Assign(Guid ownerUserId, DateTime now)
    {
        if (Status is AccountingCloseTaskStatuses.Completed or AccountingCloseTaskStatuses.Cancelled)
            throw new InvalidOperationException("A completed or cancelled close task cannot be assigned.");
        OwnerUserId = CloseValue.RequiredId(ownerUserId, nameof(ownerUserId));
        if (Status == AccountingCloseTaskStatuses.Pending) Status = AccountingCloseTaskStatuses.InProgress;
        Touch(now);
    }

    public void BindApproval(Guid approvalRequestId, DateTime now)
    {
        ApprovalRequestId = CloseValue.RequiredId(approvalRequestId, nameof(approvalRequestId));
        Touch(now);
    }

    public void Complete(Guid actorUserId, decimal? reportedAmount, DateTime now)
    {
        if (Status == AccountingCloseTaskStatuses.Cancelled)
            throw new InvalidOperationException("A cancelled close task cannot be completed.");
        Status = AccountingCloseTaskStatuses.Completed;
        CompletedByUserId = CloseValue.RequiredId(actorUserId, nameof(actorUserId));
        ReportedAmount = reportedAmount;
        CompletedUtc = CloseValue.Utc(now);
        Touch(now);
    }

    public void Reopen(DateTime now)
    {
        if (Status is not AccountingCloseTaskStatuses.Completed and not AccountingCloseTaskStatuses.Cancelled)
            throw new InvalidOperationException("Only a completed or cancelled close task can be reopened.");
        Status = AccountingCloseTaskStatuses.Reopened;
        CompletedByUserId = null;
        CompletedUtc = null;
        ReportedAmount = null;
        Touch(now);
    }

    public void Cancel(DateTime now)
    {
        if (Status is AccountingCloseTaskStatuses.Completed or AccountingCloseTaskStatuses.Cancelled)
            throw new InvalidOperationException("A completed or cancelled close task cannot be cancelled.");
        Status = AccountingCloseTaskStatuses.Cancelled;
        Touch(now);
    }

    public void RecordActivity(DateTime now) => Touch(now);

    private void Touch(DateTime now)
    {
        UpdatedUtc = CloseValue.Utc(now);
        Version++;
    }
}

public sealed class AccountingCloseTaskDependency : ICompanyOwnedEntity
{
    private AccountingCloseTaskDependency() { }
    public AccountingCloseTaskDependency(Guid id, Guid companyId, Guid closeInstanceId,
        Guid predecessorTaskId, Guid dependentTaskId)
    {
        Id = CloseValue.RequiredId(id == Guid.Empty ? Guid.NewGuid() : id, nameof(id));
        CompanyId = CloseValue.RequiredId(companyId, nameof(companyId));
        CloseInstanceId = CloseValue.RequiredId(closeInstanceId, nameof(closeInstanceId));
        PredecessorTaskId = CloseValue.RequiredId(predecessorTaskId, nameof(predecessorTaskId));
        DependentTaskId = CloseValue.RequiredId(dependentTaskId, nameof(dependentTaskId));
        if (PredecessorTaskId == DependentTaskId) throw new ArgumentException("A task cannot depend on itself.");
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CloseInstanceId { get; private set; }
    public Guid PredecessorTaskId { get; private set; }
    public Guid DependentTaskId { get; private set; }
    public AccountingCloseInstance CloseInstance { get; private set; } = null!;
}

public sealed class AccountingCloseTaskEvidence : ICompanyOwnedEntity
{
    private AccountingCloseTaskEvidence() { }
    public AccountingCloseTaskEvidence(Guid id, Guid companyId, Guid closeTaskId, Guid documentId,
        string evidenceType, string documentTitle, string? contentHash, Guid linkedByUserId, DateTime linkedUtc)
    {
        Id = CloseValue.RequiredId(id == Guid.Empty ? Guid.NewGuid() : id, nameof(id));
        CompanyId = CloseValue.RequiredId(companyId, nameof(companyId));
        CloseTaskId = CloseValue.RequiredId(closeTaskId, nameof(closeTaskId));
        DocumentId = CloseValue.RequiredId(documentId, nameof(documentId));
        EvidenceType = CloseValue.Required(evidenceType, nameof(evidenceType), 64).ToLowerInvariant();
        DocumentTitle = CloseValue.Required(documentTitle, nameof(documentTitle), 200);
        ContentHash = CloseValue.Optional(contentHash, nameof(contentHash), 128);
        LinkedByUserId = CloseValue.RequiredId(linkedByUserId, nameof(linkedByUserId));
        LinkedUtc = CloseValue.Utc(linkedUtc);
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CloseTaskId { get; private set; }
    public Guid DocumentId { get; private set; }
    public string EvidenceType { get; private set; } = null!;
    public string DocumentTitle { get; private set; } = null!;
    public string? ContentHash { get; private set; }
    public Guid LinkedByUserId { get; private set; }
    public DateTime LinkedUtc { get; private set; }
    public AccountingCloseTask CloseTask { get; private set; } = null!;
    public CompanyKnowledgeDocument Document { get; private set; } = null!;
}

public sealed class AccountingCloseTaskNote : ICompanyOwnedEntity
{
    private AccountingCloseTaskNote() { }
    public AccountingCloseTaskNote(Guid id, Guid companyId, Guid closeTaskId, Guid authorUserId,
        string note, DateTime createdUtc)
    {
        Id = CloseValue.RequiredId(id == Guid.Empty ? Guid.NewGuid() : id, nameof(id));
        CompanyId = CloseValue.RequiredId(companyId, nameof(companyId));
        CloseTaskId = CloseValue.RequiredId(closeTaskId, nameof(closeTaskId));
        AuthorUserId = CloseValue.RequiredId(authorUserId, nameof(authorUserId));
        Note = CloseValue.Required(note, nameof(note), 4000);
        CreatedUtc = CloseValue.Utc(createdUtc);
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CloseTaskId { get; private set; }
    public Guid AuthorUserId { get; private set; }
    public string Note { get; private set; } = null!;
    public DateTime CreatedUtc { get; private set; }
    public AccountingCloseTask CloseTask { get; private set; } = null!;
}

public sealed class AccountingCloseTaskBlocker : ICompanyOwnedEntity
{
    private AccountingCloseTaskBlocker() { }
    public AccountingCloseTaskBlocker(Guid id, Guid companyId, Guid closeTaskId, string reasonCode,
        string explanation, string safeNextAction, Guid createdByUserId, DateTime createdUtc)
    {
        Id = CloseValue.RequiredId(id == Guid.Empty ? Guid.NewGuid() : id, nameof(id));
        CompanyId = CloseValue.RequiredId(companyId, nameof(companyId));
        CloseTaskId = CloseValue.RequiredId(closeTaskId, nameof(closeTaskId));
        ReasonCode = CloseValue.Required(reasonCode, nameof(reasonCode), 100).ToLowerInvariant();
        Explanation = CloseValue.Required(explanation, nameof(explanation), 1000);
        SafeNextAction = CloseValue.Required(safeNextAction, nameof(safeNextAction), 1000);
        CreatedByUserId = CloseValue.RequiredId(createdByUserId, nameof(createdByUserId));
        Status = "open";
        CreatedUtc = CloseValue.Utc(createdUtc);
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CloseTaskId { get; private set; }
    public string ReasonCode { get; private set; } = null!;
    public string Explanation { get; private set; } = null!;
    public string SafeNextAction { get; private set; } = null!;
    public string Status { get; private set; } = null!;
    public Guid CreatedByUserId { get; private set; }
    public DateTime CreatedUtc { get; private set; }
    public Guid? ResolvedByUserId { get; private set; }
    public DateTime? ResolvedUtc { get; private set; }
    public AccountingCloseTask CloseTask { get; private set; } = null!;

    public void Resolve(Guid actorUserId, DateTime now)
    {
        Status = "resolved";
        ResolvedByUserId = CloseValue.RequiredId(actorUserId, nameof(actorUserId));
        ResolvedUtc = CloseValue.Utc(now);
    }
}

public sealed class AccountingCloseStatusHistory : ICompanyOwnedEntity
{
    private AccountingCloseStatusHistory() { }
    public AccountingCloseStatusHistory(Guid id, Guid companyId, Guid closeInstanceId, Guid? closeTaskId,
        string action, string? fromStatus, string toStatus, Guid actorUserId, string? reason, DateTime occurredUtc)
    {
        Id = CloseValue.RequiredId(id == Guid.Empty ? Guid.NewGuid() : id, nameof(id));
        CompanyId = CloseValue.RequiredId(companyId, nameof(companyId));
        CloseInstanceId = CloseValue.RequiredId(closeInstanceId, nameof(closeInstanceId));
        if (closeTaskId == Guid.Empty) throw new ArgumentException("Close task cannot be empty.", nameof(closeTaskId));
        CloseTaskId = closeTaskId;
        Action = CloseValue.Required(action, nameof(action), 32).ToLowerInvariant();
        FromStatus = CloseValue.Optional(fromStatus, nameof(fromStatus), 32)?.ToLowerInvariant();
        ToStatus = CloseValue.Required(toStatus, nameof(toStatus), 32).ToLowerInvariant();
        ActorUserId = CloseValue.RequiredId(actorUserId, nameof(actorUserId));
        Reason = CloseValue.Optional(reason, nameof(reason), 1000);
        OccurredUtc = CloseValue.Utc(occurredUtc);
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public Guid CloseInstanceId { get; private set; }
    public Guid? CloseTaskId { get; private set; }
    public string Action { get; private set; } = null!;
    public string? FromStatus { get; private set; }
    public string ToStatus { get; private set; } = null!;
    public Guid ActorUserId { get; private set; }
    public string? Reason { get; private set; }
    public DateTime OccurredUtc { get; private set; }
    public AccountingCloseInstance CloseInstance { get; private set; } = null!;
}

public sealed class AccountingCloseOperation : ICompanyOwnedEntity
{
    private AccountingCloseOperation() { }
    public AccountingCloseOperation(Guid id, Guid companyId, string action, string idempotencyKey,
        string payloadHash, Guid targetId, long resultVersion, DateTime createdUtc)
    {
        Id = CloseValue.RequiredId(id == Guid.Empty ? Guid.NewGuid() : id, nameof(id));
        CompanyId = CloseValue.RequiredId(companyId, nameof(companyId));
        Action = CloseValue.Required(action, nameof(action), 32).ToLowerInvariant();
        IdempotencyKey = CloseValue.Required(idempotencyKey, nameof(idempotencyKey), 200);
        PayloadHash = CloseValue.Required(payloadHash, nameof(payloadHash), 64).ToLowerInvariant();
        TargetId = CloseValue.RequiredId(targetId, nameof(targetId));
        ResultVersion = resultVersion;
        CreatedUtc = CloseValue.Utc(createdUtc);
    }
    public Guid Id { get; private set; }
    public Guid CompanyId { get; private set; }
    public string Action { get; private set; } = null!;
    public string IdempotencyKey { get; private set; } = null!;
    public string PayloadHash { get; private set; } = null!;
    public Guid TargetId { get; private set; }
    public long ResultVersion { get; private set; }
    public DateTime CreatedUtc { get; private set; }
}

internal static class CloseValue
{
    public static Guid RequiredId(Guid value, string name) => value == Guid.Empty
        ? throw new ArgumentException($"{name} is required.", name) : value;
    public static string Required(string? value, string name, int maximum)
    {
        var normalized = value?.Trim() ?? string.Empty;
        if (normalized.Length == 0 || normalized.Length > maximum)
            throw new ArgumentException($"{name} is required and must be {maximum} characters or fewer.", name);
        return normalized;
    }
    public static string? Optional(string? value, string name, int maximum) =>
        string.IsNullOrWhiteSpace(value) ? null : Required(value, name, maximum);
    public static DateTime Utc(DateTime value) => EntityTimestampNormalizer.NormalizeUtc(value, nameof(value));
}
